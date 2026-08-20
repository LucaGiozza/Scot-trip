using ScotTrip.Models;

namespace ScotTrip.Services;

/// <summary>
/// Facade unica per le pagine: espone i dati utente in memoria e le azioni di scrittura.
/// Le pagine non parlano mai direttamente con IndexedDB o Supabase.
/// </summary>
public sealed class TripState : IDisposable
{
    private readonly LocalStore _store;
    private readonly SyncService _sync;
    private readonly SupabaseAuthService _auth;

    public List<Rating> Ratings { get; private set; } = [];
    public List<Meal> Meals { get; private set; } = [];
    public List<Stay> Stays { get; private set; } = [];
    public List<TripPhoto> Photos { get; private set; } = [];

    public event Action? Changed;

    public TripState(LocalStore store, SyncService sync, SupabaseAuthService auth)
    {
        _store = store;
        _sync = sync;
        _auth = auth;
        _sync.RemoteDataMerged += OnRemoteMerged;
    }

    public async Task InitializeAsync()
    {
        await _auth.InitializeAsync();
        await ReloadAsync();
        await _sync.InitializeAsync();
    }

    public async Task ReloadAsync()
    {
        Ratings = await _store.GetAllAsync<Rating>(LocalStore.RatingsStore);
        Meals = (await _store.GetAllAsync<Meal>(LocalStore.MealsStore))
            .OrderBy(m => m.DayDate).ThenBy(m => m.MealType).ToList();
        Stays = (await _store.GetAllAsync<Stay>(LocalStore.StaysStore))
            .OrderBy(s => s.CheckIn).ToList();
        Photos = (await _store.GetAllAsync<TripPhoto>(LocalStore.PhotosStore))
            .OrderBy(p => p.TakenAt).ToList();
        Changed?.Invoke();
    }

    private async void OnRemoteMerged() => await ReloadAsync();

    // ---------- voti ----------
    public Rating? RatingFor(RatingTarget kind, string targetId, string rater) =>
        Ratings.FirstOrDefault(r => r.TargetKind == kind && r.TargetId == targetId && r.Rater == rater);

    public double? AverageFor(RatingTarget kind, string targetId)
    {
        var stars = Ratings.Where(r => r.TargetKind == kind && r.TargetId == targetId).Select(r => r.Stars).ToList();
        return stars.Count == 0 ? null : stars.Average();
    }

    public async Task SetRatingAsync(RatingTarget kind, string targetId, string rater, int stars, string? note = null)
    {
        var existing = RatingFor(kind, targetId, rater) ?? new Rating
        {
            Id = Rating.DeterministicId(kind, targetId, rater),
            TargetKind = kind,
            TargetId = targetId,
            Rater = rater
        };
        existing.Stars = Math.Clamp(stars, 1, 5);
        if (note is not null) existing.Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        await _store.UpsertAsync(LocalStore.RatingsStore, existing);
        await ReloadAsync();
        _ = _sync.TrySyncAsync();
    }

    // ---------- pasti ----------
    public async Task SaveMealAsync(Meal meal)
    {
        await _store.UpsertAsync(LocalStore.MealsStore, meal);
        await ReloadAsync();
        _ = _sync.TrySyncAsync();
    }

    public async Task DeleteMealAsync(Meal meal)
    {
        await _store.SoftDeleteAsync(LocalStore.MealsStore, meal);
        await ReloadAsync();
        _ = _sync.TrySyncAsync();
    }

    // ---------- alloggi ----------
    public async Task SaveStayAsync(Stay stay)
    {
        await _store.UpsertAsync(LocalStore.StaysStore, stay);
        await ReloadAsync();
        _ = _sync.TrySyncAsync();
    }

    public async Task DeleteStayAsync(Stay stay)
    {
        await _store.SoftDeleteAsync(LocalStore.StaysStore, stay);
        await ReloadAsync();
        _ = _sync.TrySyncAsync();
    }

    // ---------- foto ----------
    public List<TripPhoto> PhotosForStop(string stopId) =>
        Photos.Where(p => p.StopId == stopId).ToList();

    public async Task AddPhotoAsync(TripPhoto photo, string photoKey)
    {
        await _store.UpsertAsync(LocalStore.PhotosStore, photo, photoKey);
        await ReloadAsync();
        _ = _sync.TrySyncAsync();
    }

    public async Task DeletePhotoAsync(TripPhoto photo)
    {
        await _store.SoftDeleteAsync(LocalStore.PhotosStore, photo);
        await ReloadAsync();
        _ = _sync.TrySyncAsync();
    }

    public void Dispose() => _sync.RemoteDataMerged -= OnRemoteMerged;
}
