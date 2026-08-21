// ScotTrip — ponte JS per Blazor.
// Tutto ciò che il runtime WASM non sa fare bene: IndexedDB, blob, canvas, eventi browser.
(function () {
  "use strict";

  const DB_NAME = "scot-trip";
  const DB_VERSION = 2;
  const STORES = ["ratings", "meals", "stays", "photos", "spins", "queue", "blobs"];

  let dbPromise = null;

  function openDb() {
    if (dbPromise) return dbPromise;
    dbPromise = new Promise((resolve, reject) => {
      const req = indexedDB.open(DB_NAME, DB_VERSION);
      req.onupgradeneeded = () => {
        const db = req.result;
        for (const name of STORES) {
          if (!db.objectStoreNames.contains(name)) db.createObjectStore(name);
        }
      };
      req.onsuccess = () => resolve(req.result);
      req.onerror = () => reject(req.error);
    });
    return dbPromise;
  }

  function tx(store, mode, work) {
    return openDb().then(
      (db) =>
        new Promise((resolve, reject) => {
          const t = db.transaction(store, mode);
          const os = t.objectStore(store);
          const result = work(os);
          t.oncomplete = () => resolve(result.value);
          t.onerror = () => reject(t.error);
          t.onabort = () => reject(t.error);
        })
    );
  }

  function reqToBox(request) {
    const box = { value: undefined };
    request.onsuccess = () => (box.value = request.result);
    return box;
  }

  const objectUrls = new Map(); // photoKey -> objectURL (revocati a coppie per non sprecare memoria)

  window.scotTrip = {
    // ---------- localStorage ----------
    lsGet: (k) => window.localStorage.getItem(k),
    lsSet: (k, v) => window.localStorage.setItem(k, v),
    lsRemove: (k) => window.localStorage.removeItem(k),

    // ---------- IndexedDB (valori JSON stringa) ----------
    idbGet: (store, key) => tx(store, "readonly", (os) => reqToBox(os.get(key))).then((v) => v ?? null),
    idbSet: (store, key, json) => tx(store, "readwrite", (os) => reqToBox(os.put(json, key))).then(() => undefined),
    idbDelete: (store, key) => tx(store, "readwrite", (os) => reqToBox(os.delete(key))).then(() => undefined),
    idbGetAll: (store) => tx(store, "readonly", (os) => reqToBox(os.getAll())).then((v) => v ?? []),


    // ---------- fogli modali: portali fuori da <main> per uno scroll pulito su iOS ----------
    // Blazor renderizza il foglio dentro la pagina (quindi dentro <main> che scrolla).
    // Lo spostiamo come figlio diretto del body: così l'overlay copre davvero il viewport
    // e lo scroll resta dentro il foglio, senza trascinare il contenuto di fondo.
    openSheet: (backdropEl) => {
      try {
        if (!backdropEl) return;
        document.body.classList.add("sheet-open");
        backdropEl.__originalParent = backdropEl.parentNode;
        document.body.appendChild(backdropEl);
      } catch (e) { console.error("openSheet", e); }
    },
    closeSheet: () => {
      try {
        document.body.classList.remove("sheet-open");
        // i nodi spostati vengono rimossi da Blazor al prossimo render; puliamo eventuali orfani
        document.querySelectorAll("body > .sheet-backdrop").forEach((el) => el.remove());
      } catch (e) { console.error("closeSheet", e); }
    },

    // ---------- utilità DOM ----------
    clickElement: (id) => {
      const el = document.getElementById(id);
      if (el) el.click();
    },

    // ---------- connettività ----------
    isOnline: () => navigator.onLine,
    onConnectivityChange: (dotnetRef, methodName) => {
      const notify = () => dotnetRef.invokeMethodAsync(methodName).catch(() => {});
      window.addEventListener("online", notify);
      // iOS sospende le PWA in background: al ritorno in primo piano riproviamo la sync.
      document.addEventListener("visibilitychange", () => {
        if (document.visibilityState === "visible" && navigator.onLine) notify();
      });
    },

    // ---------- foto: compressione client-side ----------
    // Legge il file dall'<input>, ridimensiona al lato massimo richiesto,
    // esporta JPEG e salva il blob in IndexedDB. Ritorna i byte salvati (0 = errore).
    compressAndStorePhoto: async (inputId, photoKey, maxEdge, quality) => {
      try {
        const input = document.getElementById(inputId);
        const file = input && input.files && input.files[0];
        if (!file) return 0;

        const bitmap = await createImageBitmap(file);
        const scale = Math.min(1, maxEdge / Math.max(bitmap.width, bitmap.height));
        const w = Math.max(1, Math.round(bitmap.width * scale));
        const h = Math.max(1, Math.round(bitmap.height * scale));

        const canvas = document.createElement("canvas");
        canvas.width = w;
        canvas.height = h;
        canvas.getContext("2d").drawImage(bitmap, 0, 0, w, h);
        bitmap.close();

        const blob = await new Promise((resolve) => canvas.toBlob(resolve, "image/jpeg", quality));
        if (!blob) return 0;

        await tx("blobs", "readwrite", (os) => reqToBox(os.put(blob, photoKey)));
        input.value = ""; // consenti di riselezionare lo stesso file
        return blob.size;
      } catch (e) {
        console.error("compressAndStorePhoto", e);
        return 0;
      }
    },

    hasLocalPhoto: async (photoKey) => {
      try {
        const blob = await tx("blobs", "readonly", (os) => reqToBox(os.get(photoKey)));
        return !!blob;
      } catch {
        return false;
      }
    },

    getLocalPhotoUrl: async (photoKey) => {
      try {
        if (objectUrls.has(photoKey)) return objectUrls.get(photoKey);
        const blob = await tx("blobs", "readonly", (os) => reqToBox(os.get(photoKey)));
        if (!blob) return null;
        const url = URL.createObjectURL(blob);
        objectUrls.set(photoKey, url);
        return url;
      } catch {
        return null;
      }
    },

    deleteLocalPhoto: async (photoKey) => {
      const url = objectUrls.get(photoKey);
      if (url) {
        URL.revokeObjectURL(url);
        objectUrls.delete(photoKey);
      }
      await tx("blobs", "readwrite", (os) => reqToBox(os.delete(photoKey)));
    },

    // ---------- foto: upload a Supabase Storage ----------
    // Il blob va dal browser a Storage senza mai passare per .NET (memoria WASM risparmiata).
    uploadPhoto: async (photoKey, uploadUrl, bearerToken, anonKey) => {
      try {
        const blob = await tx("blobs", "readonly", (os) => reqToBox(os.get(photoKey)));
        if (!blob) return false;
        const resp = await fetch(uploadUrl, {
          method: "POST",
          headers: {
            Authorization: "Bearer " + bearerToken,
            apikey: anonKey,
            "Content-Type": "image/jpeg",
            "x-upsert": "true",
          },
          body: blob,
        });
        return resp.ok;
      } catch (e) {
        console.error("uploadPhoto", e);
        return false;
      }
    },
  };
})();
