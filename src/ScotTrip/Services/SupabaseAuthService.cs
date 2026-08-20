using System.Net.Http.Json;
using System.Text.Json;
using ScotTrip.Models;

namespace ScotTrip.Services;

/// <summary>
/// Login email+password contro GoTrue (Supabase Auth) via REST puro:
/// niente dipendenze extra, pieno controllo del refresh, funziona in WASM.
/// La sessione è persistita in localStorage così il login si fa una volta sola per telefono.
/// </summary>
public sealed class SupabaseAuthService(AppConfigService config, AppInterop interop)
{
    private const string SessionKey = "scotTrip.session";
    private static readonly HttpClient Http = new(); // client dedicato: URL assoluti verso Supabase

    public SupabaseSession? Session { get; private set; }
    public bool IsLoggedIn => Session is not null;
    public event Action? Changed;

    public async Task InitializeAsync()
    {
        var raw = await interop.LocalGetAsync(SessionKey);
        if (raw is null) return;
        Session = JsonSerializer.Deserialize<SupabaseSession>(raw, Json.Options);
        Changed?.Invoke();
    }

    public async Task<string?> LoginAsync(string email, string password)
    {
        if (!config.IsConfigured) return "Supabase non è configurato (appsettings.json).";
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post,
                $"{config.Settings.SupabaseUrl}/auth/v1/token?grant_type=password")
            {
                Content = JsonContent.Create(new { email, password })
            };
            req.Headers.Add("apikey", config.Settings.SupabaseAnonKey);
            using var resp = await Http.SendAsync(req);
            if (!resp.IsSuccessStatusCode)
                return "Email o password non corretti.";

            await StoreSessionAsync(await resp.Content.ReadAsStringAsync(), email);
            return null;
        }
        catch
        {
            return "Nessuna connessione: riprova quando sei online.";
        }
    }

    /// <summary>Ritorna un access token valido, rinnovandolo se sta per scadere. Null = serve rifare login.</summary>
    public async Task<string?> GetValidAccessTokenAsync()
    {
        if (Session is null) return null;
        if (!Session.IsExpiringSoon) return Session.AccessToken;

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post,
                $"{config.Settings.SupabaseUrl}/auth/v1/token?grant_type=refresh_token")
            {
                Content = JsonContent.Create(new { refresh_token = Session.RefreshToken })
            };
            req.Headers.Add("apikey", config.Settings.SupabaseAnonKey);
            using var resp = await Http.SendAsync(req);
            if (!resp.IsSuccessStatusCode)
            {
                // refresh token bruciato o revocato → logout pulito
                await LogoutAsync();
                return null;
            }
            await StoreSessionAsync(await resp.Content.ReadAsStringAsync(), Session.UserEmail);
            return Session?.AccessToken;
        }
        catch
        {
            // offline: se il token non è ancora scaduto DEL TUTTO lo usiamo lo stesso,
            // male che vada il server risponderà 401 e la coda riproverà più tardi.
            return DateTimeOffset.FromUnixTimeSeconds(Session.ExpiresAt) > DateTimeOffset.UtcNow
                ? Session.AccessToken
                : null;
        }
    }

    public async Task LogoutAsync()
    {
        Session = null;
        await interop.LocalRemoveAsync(SessionKey);
        Changed?.Invoke();
    }

    private async Task StoreSessionAsync(string gotrueJson, string email)
    {
        using var doc = JsonDocument.Parse(gotrueJson);
        var root = doc.RootElement;
        Session = new SupabaseSession
        {
            AccessToken = root.GetProperty("access_token").GetString() ?? "",
            RefreshToken = root.GetProperty("refresh_token").GetString() ?? "",
            ExpiresAt = root.TryGetProperty("expires_at", out var ea)
                ? ea.GetInt64()
                : DateTimeOffset.UtcNow.AddSeconds(root.GetProperty("expires_in").GetInt32()).ToUnixTimeSeconds(),
            UserEmail = email
        };
        await interop.LocalSetAsync(SessionKey, JsonSerializer.Serialize(Session, Json.Options));
        Changed?.Invoke();
    }
}
