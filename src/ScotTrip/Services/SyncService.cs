using System.Text.Json;
using Microsoft.JSInterop;
using ScotTrip.Models;

namespace ScotTrip.Services;

public enum SyncStatus { Idle, Syncing, Offline, NeedsLogin }

/// <summary>
/// Cuore dell'offline-first:
///  1. PUSH — svuota la coda locale verso Supabase (foto prima dei metadati);
///  2. PULL — scarica le righe cambiate dall'ultima sincronizzazione e le fonde
///     in locale con last-write-wins su updated_at.
/// Nota iOS: Safari non supporta la Background Sync API, quindi la sync è guidata
/// dall'app: al ritorno online, al ritorno in primo piano e dopo ogni salvataggio.
/// </summary>
public sealed class SyncService : IDisposable
{
    private readonly LocalStore _store;
    private readonly SupabaseApiService _api;
    private readonly SupabaseAuthService _auth;
    private readonly AppInterop _interop;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DotNetObjectReference<SyncService>? _selfRef;

    private const string LastPullKey = "scotTrip.lastPull";
    private static readonly string[] Tables = [LocalStore.RatingsStore, LocalStore.MealsStore, LocalStore.StaysStore, LocalStore.PhotosStore, LocalStore.SpinsStore];

    public SyncStatus Status { get; private set; } = SyncStatus.Idle;
    public int PendingCount { get; private set; }
    public event Action? Changed;
    /// <summary>Notifica che dati remoti nuovi sono stati fusi in locale (le pagine si ricaricano).</summary>
    public event Action? RemoteDataMerged;

    public SyncService(LocalStore store, SupabaseApiService api, SupabaseAuthService auth, AppInterop interop)
    {
        _store = store;
        _api = api;
        _auth = auth;
        _interop = interop;
    }

    public async Task InitializeAsync()
    {
        PendingCount = (await _store.GetQueueAsync()).Count;
        _selfRef = DotNetObjectReference.Create(this);
        await _interop.RegisterConnectivityCallbackAsync(_selfRef, nameof(OnConnectivityChanged));
        Changed?.Invoke();
        _ = TrySyncAsync(); // tentativo iniziale, senza bloccare l'avvio
    }

    [JSInvokable]
    public Task OnConnectivityChanged() => TrySyncAsync();

    public async Task TrySyncAsync()
    {
        if (!await _gate.WaitAsync(0)) return; // una sync alla volta
        try
        {
            if (!await _interop.IsOnlineAsync()) { SetStatus(SyncStatus.Offline); return; }
            if (!_auth.IsLoggedIn) { SetStatus(SyncStatus.NeedsLogin); return; }

            SetStatus(SyncStatus.Syncing);
            await PushQueueAsync();
            await PullAllAsync();
            SetStatus(SyncStatus.Idle);
        }
        finally
        {
            PendingCount = (await _store.GetQueueAsync()).Count;
            Changed?.Invoke();
            _gate.Release();
        }
    }

    private async Task PushQueueAsync()
    {
        foreach (var op in await _store.GetQueueAsync())
        {
            var ok = true;

            // Le foto: prima il binario su Storage, poi i metadati su Postgres.
            // Se il blob non c'è più (foto cancellata prima della sync) l'upload si salta:
            // i metadati passano comunque e l'op di cancellazione successiva chiude il cerchio.
            if (op.PhotoKey is not null && await _interop.HasLocalPhotoAsync(op.PhotoKey))
            {
                using var doc = JsonDocument.Parse(op.Payload);
                var storagePath = doc.RootElement.TryGetProperty("storage_path", out var sp) ? sp.GetString() : null;
                if (storagePath is not null)
                    ok = await _api.UploadPhotoBlobAsync(op.PhotoKey, storagePath);
            }

            if (ok) ok = await _api.UpsertAsync(op.Table, op.Payload);

            if (ok)
            {
                await _store.DequeueAsync(op.OpId);
            }
            else
            {
                op.Attempts++;
                await _store.UpdateOpAsync(op);
                if (!await _interop.IsOnlineAsync()) break; // caduta la rete: inutile insistere ora
            }
        }
    }

    private async Task PullAllAsync()
    {
        var lastPullRaw = await _interop.LocalGetAsync(LastPullKey);
        var since = lastPullRaw is not null && DateTimeOffset.TryParse(lastPullRaw, out var parsed)
            ? parsed
            : DateTimeOffset.MinValue.AddDays(1);

        var pullStartedAt = DateTimeOffset.UtcNow;
        var mergedSomething = false;

        foreach (var table in Tables)
        {
            var rows = await _api.PullSinceAsync(table, since);
            if (rows is null) return; // errore rete/auth: il lastPull NON avanza, riproveremo

            foreach (var row in rows)
            {
                var id = Guid.Parse(row.GetProperty("id").GetString()!);
                var remoteUpdated = row.GetProperty("updated_at").GetDateTimeOffset();

                // Last-write-wins: applichiamo il remoto solo se più recente del locale.
                var localRaw = await _interop.IdbGetAsync(table, id.ToString());
                if (localRaw is not null)
                {
                    using var localDoc = JsonDocument.Parse(localRaw);
                    var localUpdated = localDoc.RootElement.GetProperty("updated_at").GetDateTimeOffset();
                    if (localUpdated >= remoteUpdated) continue;
                }

                await _store.ApplyRemoteAsync(table, id, row.GetRawText());
                mergedSomething = true;
            }
        }

        await _interop.LocalSetAsync(LastPullKey, pullStartedAt.ToString("o"));
        if (mergedSomething) RemoteDataMerged?.Invoke();
    }

    private void SetStatus(SyncStatus status)
    {
        Status = status;
        Changed?.Invoke();
    }

    public void Dispose()
    {
        _selfRef?.Dispose();
        _gate.Dispose();
    }
}
