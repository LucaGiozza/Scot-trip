using System.Globalization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;
using ScotTrip;
using ScotTrip.Services;

// Date e giorni sempre in italiano, a prescindere dalla lingua del telefono.
var culture = new CultureInfo("it-IT");
CultureInfo.DefaultThreadCurrentCulture = culture;
CultureInfo.DefaultThreadCurrentUICulture = culture;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// HttpClient "neutro" che punta al sito stesso: serve per caricare
// appsettings.json e data/itinerary.json rispettando il base href
// (fondamentale quando l'app vive in una sotto-cartella di GitHub Pages).
builder.Services.AddScoped(_ => new HttpClient
{
    BaseAddress = new Uri(builder.HostEnvironment.BaseAddress)
});

builder.Services.AddSingleton<AppInterop>();
builder.Services.AddSingleton<AppConfigService>();
builder.Services.AddSingleton<ItineraryService>();
builder.Services.AddSingleton<LocalStore>();
builder.Services.AddSingleton<SupabaseAuthService>();
builder.Services.AddSingleton<SupabaseApiService>();
builder.Services.AddSingleton<SyncService>();
builder.Services.AddSingleton<TripState>();

var host = builder.Build();

// Bootstrap: config → itinerario → stato locale. Tutto tollerante all'offline.
var config = host.Services.GetRequiredService<AppConfigService>();
await config.LoadAsync();
var itinerary = host.Services.GetRequiredService<ItineraryService>();
await itinerary.LoadAsync();
var state = host.Services.GetRequiredService<TripState>();
await state.InitializeAsync();

await host.RunAsync();
