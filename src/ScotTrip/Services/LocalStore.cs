using System.Text.Json;
using ScotTrip.Models;

namespace ScotTrip.Services;

/// <summary>
/// Persistenza locale (IndexedDB) di tutte le entità utente e della coda di sync.
/// Regola d'oro offline-first: si scrive SEMPRE prima qui, la rete arriva dopo.
/// </summary>
public sealed class LocalStore(AppInterop interop)
{
    public const string RatingsStore = "ratings";
    public const string MealsStore = "meals";
    public const string StaysStore = "stays";
    public const string PhotosStore = "photos";
    public const string QueueStore = "queue";

    private static string TableFor(string store) => store; // nomi tabella Supabase = nomi store

    // ---------- letture ----------
    public async Task<List<T>> GetAllAsync<T>(string store) where T : UserEntity
    {
        var rows = await interop.IdbGetAllAsync(store);
        var list = new List<T>(rows.Length);
        foreach (var row in rows)
        {
            var item = JsonSerializer.Deserialize<T>(row, Json.Options);
            if (item is not null && !item.Deleted) list.Add(item);
        }
        return list;
    }

    public async Task<T?> GetAsync<T>(string store, Guid id) where T : UserEntity
    {
        var row = await interop.IdbGetAsync(store, id.ToString());
        return row is null ? null : JsonSerializer.Deserialize<T>(row, Json.Options);
    }

    // ---------- scritture (locale + coda) ----------
    /// <summary>Salva localmente e mette in coda l'upsert remoto.</summary>
    public async Task UpsertAsync<T>(string store, T entity, string? photoKey = null) where T : UserEntity
    {
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        var payload = JsonSerializer.Serialize(entity, Json.Options);
        await interop.IdbSetAsync(store, entity.Id.ToString(), payload);
        await EnqueueAsync(new PendingOp
        {
            Table = TableFor(store),
            EntityId = entity.Id,
            Payload = payload,
            PhotoKey = photoKey
        });
    }

    /// <summary>Soft delete: la riga resta (deleted=true) così la cancellazione si propaga all'altro telefono.</summary>
    public async Task SoftDeleteAsync<T>(string store, T entity) where T : UserEntity
    {
        entity.Deleted = true;
        await UpsertAsync(store, entity);
    }

    /// <summary>Scrive una riga arrivata dal server SENZA rimetterla in coda (evita ping-pong).</summary>
    public async Task ApplyRemoteAsync(string store, Guid id, string payloadJson)
        => await interop.IdbSetAsync(store, id.ToString(), payloadJson);

    // ---------- coda ----------
    public async Task EnqueueAsync(PendingOp op)
        => await interop.IdbSetAsync(QueueStore, op.OpId.ToString(), JsonSerializer.Serialize(op, Json.Options));

    public async Task<List<PendingOp>> GetQueueAsync()
    {
        var rows = await interop.IdbGetAllAsync(QueueStore);
        var ops = new List<PendingOp>(rows.Length);
        foreach (var row in rows)
        {
            var op = JsonSerializer.Deserialize<PendingOp>(row, Json.Options);
            if (op is not null) ops.Add(op);
        }
        return ops.OrderBy(o => o.QueuedAt).ToList();
    }

    public Task DequeueAsync(Guid opId) => interop.IdbDeleteAsync(QueueStore, opId.ToString()).AsTask();

    public async Task UpdateOpAsync(PendingOp op)
        => await interop.IdbSetAsync(QueueStore, op.OpId.ToString(), JsonSerializer.Serialize(op, Json.Options));
}
