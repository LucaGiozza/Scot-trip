using System.Net.Http.Json;
using System.Text.Json;
using ScotTrip.Models;

namespace ScotTrip.Services;

public static class Json
{
    // Niente DefaultIgnoreCondition: i null DEVONO viaggiare espliciti, altrimenti
    // svuotare una nota non sovrascriverebbe il valore precedente sul server
    // (l'upsert merge-duplicates aggiorna solo le chiavi presenti nel payload).
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true
    };
}

/// <summary>Carica wwwroot/appsettings.json e le preferenze locali (nomi dei viaggiatori).</summary>
public sealed class AppConfigService(HttpClient http, AppInterop interop)
{
    private const string TravelersKey = "scotTrip.travelers";

    public AppSettingsFile Settings { get; private set; } = new();
    public IReadOnlyList<string> Travelers { get; private set; } = ["Viaggiatore 1", "Viaggiatore 2"];
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Settings.SupabaseUrl)
                             && !Settings.SupabaseUrl.Contains("INSERISCI")
                             && !string.IsNullOrWhiteSpace(Settings.SupabaseAnonKey)
                             && !Settings.SupabaseAnonKey.Contains("INSERISCI");

    public async Task LoadAsync()
    {
        try
        {
            Settings = await http.GetFromJsonAsync<AppSettingsFile>("appsettings.json", Json.Options) ?? new();
        }
        catch
        {
            Settings = new(); // offline al primissimo avvio: impossibile, ma non si sa mai
        }

        var stored = await SafeLocalGet(TravelersKey);
        if (stored is not null)
        {
            var names = JsonSerializer.Deserialize<List<string>>(stored, Json.Options);
            if (names is { Count: 2 }) { Travelers = names; return; }
        }
        if (Settings.Travelers is { Count: 2 }) Travelers = Settings.Travelers;
    }

    public async Task SaveTravelersAsync(string first, string second)
    {
        Travelers = [Clean(first, "Viaggiatore 1"), Clean(second, "Viaggiatore 2")];
        await interop.LocalSetAsync(TravelersKey, JsonSerializer.Serialize(Travelers, Json.Options));
        static string Clean(string s, string fallback) => string.IsNullOrWhiteSpace(s) ? fallback : s.Trim();
    }

    private async Task<string?> SafeLocalGet(string key)
    {
        try { return await interop.LocalGetAsync(key); } catch { return null; }
    }
}

/// <summary>Carica l'itinerario bundlato. È in cache del service worker, quindi sempre disponibile offline.</summary>
public sealed class ItineraryService(HttpClient http)
{
    public Itinerary Trip { get; private set; } = new();
    public IReadOnlyDictionary<string, TripStop> StopsById { get; private set; } =
        new Dictionary<string, TripStop>();
    public IReadOnlyDictionary<string, TripDay> DayByStopId { get; private set; } =
        new Dictionary<string, TripDay>();

    public async Task LoadAsync()
    {
        Trip = await http.GetFromJsonAsync<Itinerary>("data/itinerary.json", Json.Options)
               ?? throw new InvalidOperationException("Itinerario mancante: data/itinerary.json");

        var byId = new Dictionary<string, TripStop>();
        var dayByStop = new Dictionary<string, TripDay>();
        foreach (var day in Trip.Days)
            foreach (var stop in day.Stops)
            {
                byId[stop.Id] = stop;
                dayByStop[stop.Id] = day;
            }
        StopsById = byId;
        DayByStopId = dayByStop;
    }

    /// <summary>Il giorno "di oggi" se siamo in viaggio, altrimenti il primo (prima della partenza) o l'ultimo (dopo).</summary>
    public TripDay CurrentDay(DateOnly today)
    {
        if (Trip.Days.Count == 0) return new TripDay();
        var match = Trip.Days.FirstOrDefault(d => d.Date == today);
        if (match is not null) return match;
        if (today < Trip.Days[0].Date) return Trip.Days[0];
        return Trip.Days[^1];
    }
}
