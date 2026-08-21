using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace ScotTrip.Services;

/// <summary>
/// Unico punto di contatto con wwwroot/js/app.js.
/// Tenere qui tutte le chiamate JS rende i servizi testabili e il contratto esplicito.
/// </summary>
public sealed class AppInterop(IJSRuntime js)
{
    // ---- IndexedDB: chiave/valore JSON per store ----
    public ValueTask<string?> IdbGetAsync(string store, string key)
        => js.InvokeAsync<string?>("scotTrip.idbGet", store, key);

    public ValueTask IdbSetAsync(string store, string key, string json)
        => js.InvokeVoidAsync("scotTrip.idbSet", store, key, json);

    public ValueTask IdbDeleteAsync(string store, string key)
        => js.InvokeVoidAsync("scotTrip.idbDelete", store, key);

    public ValueTask<string[]> IdbGetAllAsync(string store)
        => js.InvokeAsync<string[]>("scotTrip.idbGetAll", store);

    // ---- Foto ----
    /// <summary>
    /// Legge il file selezionato nell'input, lo ridimensiona/comprime in JPEG
    /// e lo salva come blob in IndexedDB. Ritorna la dimensione in byte (0 = fallito).
    /// </summary>
    public ValueTask<long> CompressAndStorePhotoAsync(string inputElementId, string photoKey, int maxEdge, double quality)
        => js.InvokeAsync<long>("scotTrip.compressAndStorePhoto", inputElementId, photoKey, maxEdge, quality);

    /// <summary>Ritorna un object URL per mostrare il blob locale, o null se assente.</summary>
    public ValueTask<string?> GetLocalPhotoUrlAsync(string photoKey)
        => js.InvokeAsync<string?>("scotTrip.getLocalPhotoUrl", photoKey);

    /// <summary>True se il blob della foto esiste ancora in locale.</summary>
    public ValueTask<bool> HasLocalPhotoAsync(string photoKey)
        => js.InvokeAsync<bool>("scotTrip.hasLocalPhoto", photoKey);

    /// <summary>Carica il blob locale su Supabase Storage. Ritorna true se ok.</summary>
    public ValueTask<bool> UploadPhotoAsync(string photoKey, string uploadUrl, string bearerToken, string anonKey)
        => js.InvokeAsync<bool>("scotTrip.uploadPhoto", photoKey, uploadUrl, bearerToken, anonKey);

    public ValueTask DeleteLocalPhotoAsync(string photoKey)
        => js.InvokeVoidAsync("scotTrip.deleteLocalPhoto", photoKey);

    // ---- Rete / ambiente ----
    public ValueTask<bool> IsOnlineAsync() => js.InvokeAsync<bool>("scotTrip.isOnline");

    /// <summary>Registra una callback .NET invocata quando il browser torna online o l'app torna in primo piano.</summary>
    public ValueTask RegisterConnectivityCallbackAsync<T>(DotNetObjectReference<T> reference, string methodName) where T : class
        => js.InvokeVoidAsync("scotTrip.onConnectivityChange", reference, methodName);

    // ---- localStorage (per config leggera e sessione) ----
    public ValueTask<string?> LocalGetAsync(string key) => js.InvokeAsync<string?>("scotTrip.lsGet", key);
    public ValueTask LocalSetAsync(string key, string value) => js.InvokeVoidAsync("scotTrip.lsSet", key, value);
    public ValueTask LocalRemoveAsync(string key) => js.InvokeVoidAsync("scotTrip.lsRemove", key);

    // ---- fogli modali (bottom sheet): portale fuori da <main> per scroll pulito su iOS ----
    /// <summary>Sposta il foglio come figlio del body e blocca lo scroll di fondo.</summary>
    public ValueTask OpenSheetAsync(ElementReference backdrop) => js.InvokeVoidAsync("scotTrip.openSheet", backdrop);
    /// <summary>Ripristina lo scroll di fondo e rimuove eventuali fogli orfani.</summary>
    public ValueTask CloseSheetAsync() => js.InvokeVoidAsync("scotTrip.closeSheet");

    // ---- ruota della fortuna ----
    /// <summary>Anima la ruota dall'angolo di partenza a quello finale (frame-by-frame in JS).</summary>
    public ValueTask SpinWheelAsync(string wheelId, double fromAngle, double toAngle, int durationMs)
        => js.InvokeVoidAsync("scotTrip.spinWheel", wheelId, fromAngle, toAngle, durationMs);
}
