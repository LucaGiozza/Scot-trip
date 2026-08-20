using System.Text.Json.Serialization;

namespace ScotTrip.Models;

/// <summary>
/// Base comune per tutte le entità create in viaggio.
/// Id generato dal client (uuid) così la creazione funziona anche offline;
/// UpdatedAt guida la risoluzione dei conflitti (last-write-wins).
/// </summary>
public abstract class UserEntity
{
    [JsonPropertyName("id")] public Guid Id { get; set; } = Guid.NewGuid();
    [JsonPropertyName("updated_at")] public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    [JsonPropertyName("deleted")] public bool Deleted { get; set; }
}

/// <summary>Un voto (1–5) dato da una persona a una tappa, un pasto o un alloggio, per una categoria.</summary>
public sealed class Rating : UserEntity
{
    [JsonPropertyName("target_kind")] public RatingTarget TargetKind { get; set; }
    /// <summary>Slug della tappa oppure l'uuid (stringa) di pasto/alloggio.</summary>
    [JsonPropertyName("target_id")] public string TargetId { get; set; } = "";
    /// <summary>Nome di chi vota, es. "Luca". Due persone → due righe.</summary>
    [JsonPropertyName("rater")] public string Rater { get; set; } = "";
    /// <summary>Le tappe usano Generale; pasti e alloggi votano le 4 categorie.</summary>
    [JsonPropertyName("category")] public RatingCategory Category { get; set; } = RatingCategory.Generale;
    [JsonPropertyName("stars")] public int Stars { get; set; }
    [JsonPropertyName("note")] public string? Note { get; set; }

    /// <summary>
    /// Id DETERMINISTICO: lo stesso (target, persona, categoria) produce lo stesso uuid
    /// su qualsiasi telefono. Così se entrambi votano la stessa cosa offline, i due upsert
    /// convergono sulla stessa riga e il conflitto lo risolve il last-write-wins.
    /// </summary>
    public static Guid DeterministicId(RatingTarget kind, string targetId, string rater, RatingCategory category)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(
            $"rating|{kind}|{targetId}|{rater.Trim().ToLowerInvariant()}|{category}");
        var hash = System.Security.Cryptography.SHA256.HashData(bytes);
        var guidBytes = hash[..16];
        // marca versione/variant per ottenere un uuid formalmente valido (v4-like)
        guidBytes[7] = (byte)((guidBytes[7] & 0x0F) | 0x40);
        guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80);
        return new Guid(guidBytes);
    }
}

[JsonConverter(typeof(JsonStringEnumConverter<RatingCategory>))]
public enum RatingCategory
{
    Generale, Location, Prezzo, Qualita, Personale
}

public static class RatingCategoryInfo
{
    /// <summary>Le categorie votabili per pasti e alloggi, nell'ordine di visualizzazione.</summary>
    public static readonly RatingCategory[] ForPlaces =
        [RatingCategory.Location, RatingCategory.Prezzo, RatingCategory.Qualita, RatingCategory.Personale];

    public static string Label(this RatingCategory c) => c switch
    {
        RatingCategory.Location => "Location",
        RatingCategory.Prezzo => "Prezzo",
        RatingCategory.Qualita => "Qualità",
        RatingCategory.Personale => "Personale",
        _ => "Generale"
    };

    public static string Icon(this RatingCategory c) => c switch
    {
        RatingCategory.Location => "📍",
        RatingCategory.Prezzo => "💷",
        RatingCategory.Qualita => "✨",
        RatingCategory.Personale => "🤝",
        _ => "★"
    };
}

[JsonConverter(typeof(JsonStringEnumConverter<RatingTarget>))]
public enum RatingTarget { Stop, Meal, Stay }

public sealed class Meal : UserEntity
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("place")] public string Place { get; set; } = "";
    [JsonPropertyName("day_date")] public DateOnly DayDate { get; set; }
    [JsonPropertyName("meal_type")] public MealType MealType { get; set; } = MealType.Cena;
    [JsonPropertyName("cost")] public decimal? Cost { get; set; }
    /// <summary>Cosa abbiamo mangiato: i piatti da ricordare.</summary>
    [JsonPropertyName("dishes")] public string? Dishes { get; set; }
    [JsonPropertyName("note")] public string? Note { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter<MealType>))]
public enum MealType { Colazione, Pranzo, Cena, Spuntino }

public sealed class Stay : UserEntity
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("location")] public string Location { get; set; } = "";
    [JsonPropertyName("check_in")] public DateOnly CheckIn { get; set; }
    [JsonPropertyName("check_out")] public DateOnly CheckOut { get; set; }
    [JsonPropertyName("note")] public string? Note { get; set; }
}

/// <summary>
/// Metadati di una foto, agganciata a una tappa, un pasto o un alloggio.
/// Il file binario vive in IndexedDB finché non viene caricato su Supabase Storage.
/// </summary>
public sealed class TripPhoto : UserEntity
{
    [JsonPropertyName("target_kind")] public RatingTarget TargetKind { get; set; } = RatingTarget.Stop;
    [JsonPropertyName("target_id")] public string TargetId { get; set; } = "";
    [JsonPropertyName("taken_at")] public DateTimeOffset TakenAt { get; set; } = DateTimeOffset.UtcNow;
    [JsonPropertyName("storage_path")] public string? StoragePath { get; set; }
    [JsonPropertyName("caption")] public string? Caption { get; set; }
    [JsonPropertyName("author")] public string? Author { get; set; }
}

/// <summary>Operazione in coda, in attesa di essere sincronizzata verso Supabase.</summary>
public sealed class PendingOp
{
    [JsonPropertyName("opId")] public Guid OpId { get; set; } = Guid.NewGuid();
    [JsonPropertyName("table")] public string Table { get; set; } = "";
    [JsonPropertyName("entityId")] public Guid EntityId { get; set; }
    /// <summary>Payload JSON già serializzato pronto per l'upsert PostgREST.</summary>
    [JsonPropertyName("payload")] public string Payload { get; set; } = "";
    /// <summary>Se valorizzato, prima dell'upsert va caricato il blob locale con questa chiave.</summary>
    [JsonPropertyName("photoKey")] public string? PhotoKey { get; set; }
    [JsonPropertyName("queuedAt")] public DateTimeOffset QueuedAt { get; set; } = DateTimeOffset.UtcNow;
    [JsonPropertyName("attempts")] public int Attempts { get; set; }
}

public sealed class SupabaseSession
{
    [JsonPropertyName("access_token")] public string AccessToken { get; set; } = "";
    [JsonPropertyName("refresh_token")] public string RefreshToken { get; set; } = "";
    [JsonPropertyName("expires_at")] public long ExpiresAt { get; set; }
    [JsonPropertyName("user_email")] public string UserEmail { get; set; } = "";

    [JsonIgnore]
    public bool IsExpiringSoon =>
        DateTimeOffset.FromUnixTimeSeconds(ExpiresAt) - DateTimeOffset.UtcNow < TimeSpan.FromSeconds(90);
}

public sealed class AppSettingsFile
{
    [JsonPropertyName("supabaseUrl")] public string SupabaseUrl { get; set; } = "";
    [JsonPropertyName("supabaseAnonKey")] public string SupabaseAnonKey { get; set; } = "";
    [JsonPropertyName("photosBucket")] public string PhotosBucket { get; set; } = "trip-photos";
    [JsonPropertyName("travelers")] public List<string> Travelers { get; set; } = ["Viaggiatore 1", "Viaggiatore 2"];
}
