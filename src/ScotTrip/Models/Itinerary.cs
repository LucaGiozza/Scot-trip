using System.Text.Json.Serialization;

namespace ScotTrip.Models;

/// <summary>Radice del file wwwroot/data/itinerary.json (contenuto di sola lettura, sempre disponibile offline).</summary>
public sealed class Itinerary
{
    [JsonPropertyName("tripName")] public string TripName { get; set; } = "";
    [JsonPropertyName("days")] public List<TripDay> Days { get; set; } = [];
}

public sealed class TripDay
{
    /// <summary>1-based, usato nelle route (/giorno/1).</summary>
    [JsonPropertyName("index")] public int Index { get; set; }
    [JsonPropertyName("date")] public DateOnly Date { get; set; }
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("area")] public string Area { get; set; } = "";
    [JsonPropertyName("stops")] public List<TripStop> Stops { get; set; } = [];
}

public sealed class TripStop
{
    /// <summary>Slug stabile, es. "edinburgh-castle". È la chiave a cui si agganciano foto e voti.</summary>
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("kind")] public StopKind Kind { get; set; } = StopKind.Sight;
    [JsonPropertyName("lat")] public double Lat { get; set; }
    [JsonPropertyName("lng")] public double Lng { get; set; }
    [JsonPropertyName("summary")] public string Summary { get; set; } = "";
    /// <summary>Info pratiche condensate: orari, costi, prenotazione.</summary>
    [JsonPropertyName("practical")] public string? Practical { get; set; }
    [JsonPropertyName("bookingRequired")] public bool BookingRequired { get; set; }
    [JsonPropertyName("curiosities")] public List<Curiosity> Curiosities { get; set; } = [];

    [JsonIgnore]
    public string MapsUrl =>
        $"https://www.google.com/maps/search/?api=1&query={Lat.ToString(System.Globalization.CultureInfo.InvariantCulture)},{Lng.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
}

[JsonConverter(typeof(JsonStringEnumConverter<StopKind>))]
public enum StopKind
{
    Sight, Castle, Nature, Village, Beach, Distillery, Church, Viewpoint, City
}

public sealed class Curiosity
{
    [JsonPropertyName("kind")] public CuriosityKind Kind { get; set; }
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("text")] public string Text { get; set; } = "";
}

[JsonConverter(typeof(JsonStringEnumConverter<CuriosityKind>))]
public enum CuriosityKind
{
    /// <summary>Curiosità assurda ma vera.</summary>
    Weird,
    /// <summary>Nozione storica raccontata in modo leggero.</summary>
    History,
    /// <summary>Leggenda o folklore (dichiarata come tale).</summary>
    Legend
}
