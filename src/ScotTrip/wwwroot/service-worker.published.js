// Service worker di produzione (usato solo da `dotnet publish`).
// Strategia:
//  - App shell (wasm, dll, css, js, itinerario, icone): cache-first, aggiornata a ogni deploy
//    tramite il manifest generato dalla build (service-worker-assets.js).
//  - Richieste verso Supabase: MAI intercettate → le gestisce l'app con la sua coda offline.
//  - Navigazioni: fallback a index.html (necessario per il routing SPA su GitHub Pages).
self.importScripts('./service-worker-assets.js');

self.addEventListener('install', event => event.waitUntil(onInstall(event)));
self.addEventListener('activate', event => event.waitUntil(onActivate(event)));
self.addEventListener('fetch', event => event.respondWith(onFetch(event)));

const cacheNamePrefix = 'scot-trip-cache-';
const cacheName = `${cacheNamePrefix}${self.assetsManifest.version}`;
const offlineAssetsInclude = [/\.dll$/, /\.pdb$/, /\.wasm$/, /\.html$/, /\.js$/, /\.json$/, /\.css$/, /\.woff2?$/, /\.png$/, /\.jpe?g$/, /\.gif$/, /\.svg$/, /\.ico$/, /\.dat$/, /\.blat$/, /\.webmanifest$/];
const offlineAssetsExclude = [/^service-worker\.js$/];

async function onInstall() {
    const assetsRequests = self.assetsManifest.assets
        .filter(asset => offlineAssetsInclude.some(pattern => pattern.test(asset.url)))
        .filter(asset => !offlineAssetsExclude.some(pattern => pattern.test(asset.url)))
        .map(asset => new Request(asset.url, { integrity: asset.hash, cache: 'no-cache' }));
    await (await caches.open(cacheName)).addAll(assetsRequests);
    self.skipWaiting(); // il nuovo deploy prende subito il controllo
}

async function onActivate() {
    const cacheKeys = await caches.keys();
    await Promise.all(
        cacheKeys
            .filter(key => key.startsWith(cacheNamePrefix) && key !== cacheName)
            .map(key => caches.delete(key))
    );
    await self.clients.claim();
}

async function onFetch(event) {
    if (event.request.method !== 'GET') return fetch(event.request);

    const url = new URL(event.request.url);

    // Supabase e qualsiasi origine esterna: passthrough, l'offline lo gestisce l'app.
    if (url.origin !== self.location.origin) return fetch(event.request);

    const isNavigation = event.request.mode === 'navigate';
    const cache = await caches.open(cacheName);
    // ignoreSearch: i nostri asset usano ?v=NN per forzare gli aggiornamenti online,
    // ma in cache sono salvati senza query → senza questa opzione l'offline si rompe.
    const cachedResponse = await cache.match(isNavigation ? 'index.html' : event.request, { ignoreSearch: true });
    return cachedResponse || fetch(event.request);
}
