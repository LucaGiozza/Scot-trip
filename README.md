# 🏴󠁧󠁢󠁳󠁣󠁴󠁿 ScotTrip — il nostro viaggio in Scozia

App di viaggio personale per il road trip in Scozia (4–13 settembre 2026): itinerario
giorno per giorno con **curiosità assurde ma vere** su ogni tappa, **foto**, **voti di coppia**
per tappe, pasti e alloggi. Pensata per due telefoni, per funzionare **anche senza rete**
(Highlands e Skye non perdonano) e per costare **zero euro**.

| Tecnologia | Ruolo | Costo |
|---|---|---|
| Blazor WebAssembly (.NET 8) | App PWA, tutta C# | — |
| GitHub Pages | Hosting del sito | Gratis |
| Supabase (free tier) | Database + foto + login | Gratis |

## Come funziona l'offline

- L'itinerario e tutte le curiosità sono **dentro l'app**: una volta installata, funzionano sempre.
- Voti, pasti, alloggi e foto si salvano **prima sul telefono** (IndexedDB) e finiscono in una
  coda: appena c'è rete vengono sincronizzati su Supabase e appaiono anche sull'altro telefono.
- Le foto vengono **compresse sul telefono** (max 1600px) prima dell'upload: la banda in
  Scozia va trattata come una risorsa preziosa.
- Conflitti tra i due telefoni: vince la modifica più recente (last-write-wins), sia
  lato app che lato database (trigger SQL).

---

## Guida al primo avvio (una volta sola, ~20 minuti)

### 1. Crea il progetto Supabase

1. Registrati su [supabase.com](https://supabase.com) e crea un **nuovo progetto** (regione: `eu-west` va benissimo). Il piano Free basta e avanza.
2. Apri **SQL Editor** → incolla tutto il contenuto di [`supabase/schema.sql`](supabase/schema.sql) → **Run**.
   Questo crea le tabelle, le regole di sicurezza e il bucket privato per le foto.
3. Vai su **Authentication → Users → Add user → Create new user** e crea **due utenti**
   (una email a testa, con password). Spunta "Auto Confirm User".
   > Facoltativo ma consigliato: in **Authentication → Sign In / Up** disattiva "Allow new users to sign up",
   > così nessun altro potrà registrarsi.
4. Vai su **Project Settings → API** e copia:
   - **Project URL** (es. `https://abcdefgh.supabase.co`)
   - **anon public key**

### 2. Configura l'app

Apri `src/ScotTrip/wwwroot/appsettings.json` e inserisci i tuoi valori:

```json
{
  "supabaseUrl": "https://IL-TUO-PROGETTO.supabase.co",
  "supabaseAnonKey": "LA-TUA-ANON-KEY",
  "photosBucket": "trip-photos",
  "travelers": [ "Luca", "Alessia" ]
}
```

> La anon key è pensata per stare nel client: la sicurezza vera la fanno le
> policy RLS dello schema SQL, che richiedono il login dei vostri due utenti.

### 3. Prova in locale (Visual Studio)

1. Apri `src/ScotTrip/ScotTrip.csproj` con Visual Studio 2022 (workload "ASP.NET e sviluppo web").
2. F5. La prima build scarica i pacchetti NuGet.
3. Per provarla "da telefono": DevTools del browser → modalità dispositivo → iPhone.

### 4. Pubblica su GitHub Pages

1. Crea un repository su GitHub (es. `scot-trip`) e fai push di tutto.
2. Nel repo: **Settings → Pages → Source: GitHub Actions**.
3. Al primo push su `main` parte il workflow [`deploy.yml`](.github/workflows/deploy.yml):
   in un paio di minuti l'app è su `https://TUO-UTENTE.github.io/NOME-REPO/`.
   Il workflow sistema da solo base href, fallback SPA e `.nojekyll`.

### 5. Installala sugli iPhone

1. Apri l'URL dell'app in **Safari** (deve essere Safari).
2. Tasto **Condividi** → **Aggiungi alla schermata Home**.
3. Apri l'app dall'icona: schermo intero, senza barra del browser.
4. In **Altro → Account condiviso** fai login con uno dei due utenti (una volta sola per telefono).
5. In **Altro → I viaggiatori** controllate i vostri nomi (compaiono accanto alle stelle di voto).

> 📌 Aprite l'app almeno una volta con rete buona prima di partire: il service worker
> scarica tutto e da quel momento funziona anche in modalità aereo.

---

## Struttura del progetto

```
src/ScotTrip/
├── Models/            # Itinerario + dati utente (voti, pasti, alloggi, foto)
├── Services/          # Config, store IndexedDB, auth, API Supabase, motore di sync
├── Components/        # Stelle, galleria foto, carte curiosità, gattini SVG…
├── Pages/             # Itinerario, Tappa, Pasti, Alloggi, Impostazioni
├── Layout/            # Header con tartan + tab bar inferiore
└── wwwroot/
    ├── data/itinerary.json      # ⭐ il viaggio: 10 giorni, 45 tappe, curiosità
    ├── js/app.js                # IndexedDB, compressione foto, connettività
    ├── css/app.css              # design system
    └── service-worker.published.js  # cache offline dell'app
supabase/schema.sql    # tabelle, RLS, bucket foto (da eseguire una volta)
.github/workflows/     # deploy automatico su GitHub Pages
```

## Modificare l'itinerario

Tutto il contenuto è in `wwwroot/data/itinerary.json`. Ogni tappa ha:

```jsonc
{
  "id": "edinburgh-castle",     // slug stabile: NON cambiarlo dopo aver votato/fotografato
  "name": "Edinburgh Castle",
  "kind": "Castle",             // Sight | Castle | Nature | Village | Beach | Distillery | Church | Viewpoint | City
  "lat": 55.9486, "lng": -3.1999,
  "summary": "…",
  "practical": "orari, costi, prenotazioni…",
  "bookingRequired": true,
  "curiosities": [
    { "kind": "Weird",   "title": "…", "text": "…" },   // assurdo ma vero
    { "kind": "History", "title": "…", "text": "…" },   // un po' di storia
    { "kind": "Legend",  "title": "…", "text": "…" }    // leggende (dichiarate)
  ]
}
```

## Promemoria prenotazioni ⚠

Due tappe dell'itinerario sono segnate come **prenotazione obbligatoria**:

- **Edinburgh Castle** (giorno 1) — i biglietti sul posto quasi non esistono
- **The Macallan Estate** (giorno 5) — tour da prenotare con mesi di anticipo

L'app le evidenzia con il simbolo ⚠ nell'itinerario.

## Limiti noti (scelte consapevoli)

- **Login richiesto per la sincronizzazione**: senza login l'app funziona comunque, ma i dati restano sul singolo telefono.
- **iOS non supporta la Background Sync**: la sincronizzazione parte quando l'app è aperta (all'avvio, al ritorno online, al ritorno in primo piano e dopo ogni salvataggio). In pratica non ve ne accorgerete.
- **Foto dell'altro telefono**: visibili solo quando c'è rete (servono URL firmati dal bucket privato). Le proprie foto sono sempre visibili, anche offline.
- **Spazio iOS per le PWA**: Safari può liberare i dati dei siti web inutilizzati a lungo; usando l'app durante il viaggio non è un problema, ma non è un archivio a vita — a fine viaggio le foto stanno comunque al sicuro su Supabase.
