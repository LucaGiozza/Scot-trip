using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ScotTrip.Models;

namespace ScotTrip.Services;

/// <summary>
/// Accesso diretto alle REST API di Supabase (PostgREST + Storage).
/// Scelta deliberata: niente SDK esterni → bundle WASM più piccolo e zero magia.
/// </summary>
public sealed class SupabaseApiService(AppConfigService config, SupabaseAuthService auth, AppInterop interop)
{
    private static readonly HttpClient Http = new();

    private string RestUrl(string table) => $"{config.Settings.SupabaseUrl}/rest/v1/{table}";

    private async Task<HttpRequestMessage?> AuthedRequestAsync(HttpMethod method, string url)
    {
        var token = await auth.GetValidAccessTokenAsync();
        if (token is null) return null;
        var req = new HttpRequestMessage(method, url);
        req.Headers.Add("apikey", config.Settings.SupabaseAnonKey);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return req;
    }

    /// <summary>Upsert idempotente: la riga vince/perde sul server in base a updated_at (trigger SQL).</summary>
    public async Task<bool> UpsertAsync(string table, string payloadJson)
    {
        using var req = await AuthedRequestAsync(HttpMethod.Post, RestUrl(table));
        if (req is null) return false;
        req.Headers.Add("Prefer", "resolution=merge-duplicates,return=minimal");
        req.Content = new StringContent(payloadJson, Encoding.UTF8, "application/json");
        try
        {
            using var resp = await Http.SendAsync(req);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    /// <summary>Scarica tutte le righe modificate dopo "since" (delta pull).</summary>
    public async Task<List<JsonElement>?> PullSinceAsync(string table, DateTimeOffset since)
    {
        var url = $"{RestUrl(table)}?updated_at=gt.{Uri.EscapeDataString(since.UtcDateTime.ToString("o"))}&order=updated_at.asc";
        using var req = await AuthedRequestAsync(HttpMethod.Get, url);
        if (req is null) return null;
        try
        {
            using var resp = await Http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return null;
            var json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.EnumerateArray().Select(e => e.Clone()).ToList();
        }
        catch { return null; }
    }

    /// <summary>Carica su Storage il blob locale identificato da photoKey. Il PUT lo fa il JS (il blob non passa per .NET).</summary>
    public async Task<bool> UploadPhotoBlobAsync(string photoKey, string storagePath)
    {
        var token = await auth.GetValidAccessTokenAsync();
        if (token is null) return false;
        var url = $"{config.Settings.SupabaseUrl}/storage/v1/object/{config.Settings.PhotosBucket}/{storagePath}";
        return await interop.UploadPhotoAsync(photoKey, url, token, config.Settings.SupabaseAnonKey);
    }

    /// <summary>URL firmato (bucket privato) per mostrare le foto scattate dall'altro telefono.</summary>
    public async Task<string?> CreateSignedPhotoUrlAsync(string storagePath, int expiresSeconds = 3600)
    {
        using var req = await AuthedRequestAsync(HttpMethod.Post,
            $"{config.Settings.SupabaseUrl}/storage/v1/object/sign/{config.Settings.PhotosBucket}/{storagePath}");
        if (req is null) return null;
        req.Content = new StringContent($"{{\"expiresIn\":{expiresSeconds}}}", Encoding.UTF8, "application/json");
        try
        {
            using var resp = await Http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var signed = doc.RootElement.GetProperty("signedURL").GetString();
            return signed is null ? null : $"{config.Settings.SupabaseUrl}/storage/v1{signed}";
        }
        catch { return null; }
    }
}
