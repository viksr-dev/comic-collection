// The collection lives in the phone's browser storage (IndexedDB).

const DB_NAME = 'comic-collection';
const STORE = 'comics';
const BIN = 'bin';
export const BIN_DAYS = 30;
let dbPromise;

function open() {
  if (!dbPromise) {
    dbPromise = new Promise((resolve, reject) => {
      // Version 2 added the bin: deleted comics wait there for 30 days.
      const req = indexedDB.open(DB_NAME, 2);
      req.onupgradeneeded = () => {
        const db = req.result;
        if (!db.objectStoreNames.contains(STORE)) db.createObjectStore(STORE, { keyPath: 'id' }).createIndex('barcode', 'barcode');
        if (!db.objectStoreNames.contains(BIN)) db.createObjectStore(BIN, { keyPath: 'id' });
      };
      req.onsuccess = () => resolve(req.result);
      req.onerror = () => reject(req.error);
    });
  }
  return dbPromise;
}

async function tx(mode, fn, stores = STORE) {
  const db = await open();
  return new Promise((resolve, reject) => {
    const t = db.transaction(stores, mode);
    const result = Array.isArray(stores) ? fn(...stores.map((n) => t.objectStore(n))) : fn(t.objectStore(stores));
    t.oncomplete = () => resolve(result && 'result' in result ? result.result : result);
    t.onerror = () => reject(t.error);
  });
}

export const allComics = () => tx('readonly', (s) => s.getAll());
export const getComic = (id) => tx('readonly', (s) => s.get(id));

export function saveComic(comic) {
  const now = new Date().toISOString();
  const record = { ...comic, id: comic.id || crypto.randomUUID(), addedAt: comic.addedAt || now, updatedAt: now };
  return tx('readwrite', (s) => s.put(record)).then(() => record);
}

// Deleting moves comics to the bin, where they wait BIN_DAYS days in case you want them back.
export function deleteMany(ids) {
  const deletedAt = new Date().toISOString();
  return tx('readwrite', (comics, bin) => {
    for (const id of ids) {
      const req = comics.get(id);
      req.onsuccess = () => {
        if (!req.result) return;
        bin.put({ ...req.result, deletedAt });
        comics.delete(id);
      };
    }
  }, [STORE, BIN]);
}

// Removes comics without keeping them in the bin (taking back ones just added).
export function deleteForGood(ids) {
  return tx('readwrite', (s) => {
    for (const id of ids) s.delete(id);
  });
}

export const binComics = () => tx('readonly', (s) => s.getAll(), BIN);

// Puts comics back from the bin, as they were.
export function restoreFromBin(ids) {
  return tx('readwrite', (comics, bin) => {
    for (const id of ids) {
      const req = bin.get(id);
      req.onsuccess = () => {
        if (!req.result) return;
        const { deletedAt, ...comic } = req.result;
        comics.put(comic);
        bin.delete(id);
      };
    }
  }, [STORE, BIN]);
}

// Gets rid of comics in the bin for good: all of them, or those deleted more than olderThanDays ago.
export function emptyBin(olderThanDays = 0) {
  const cutoff = new Date(Date.now() - olderThanDays * 864e5).toISOString();
  return tx('readwrite', (bin) => {
    const req = bin.getAll();
    req.onsuccess = () => {
      for (const c of req.result) if (!olderThanDays || (c.deletedAt || '') < cutoff) bin.delete(c.id);
    };
  }, BIN);
}

export function addMany(comics) {
  return tx('readwrite', (s) => {
    for (const c of comics) s.put(c);
  });
}

// Marks comics as sent to the computer, re-reading each so a lookup saved
// meanwhile isn't overwritten. Changes sent are remembered as waiting for the
// computer, so its older list doesn't undo them before it has collected them.
export function markSent(ids) {
  const at = new Date().toISOString();
  return tx('readwrite', (s) => {
    for (const id of ids) {
      const req = s.get(id);
      req.onsuccess = () => {
        const c = req.result;
        if (!c) return;
        const fields = [...new Set([...(c.changes || []), ...(c.boxChanged ? ['box'] : [])])];
        const pendingSync = fields.length ? { fields, at } : c.pendingSync;
        s.put({ ...c, sentToComputer: true, boxChanged: false, changes: [], ...(pendingSync ? { pendingSync } : {}) });
      };
    }
  });
}

export function replaceAll(comics) {
  return tx('readwrite', (s) => {
    s.clear();
    for (const c of comics) s.put(c);
  });
}

// Ask the browser not to clear our storage when the phone runs low on space.
export async function requestPersistence() {
  try {
    if (navigator.storage?.persist) return await navigator.storage.persist();
  } catch {}
  return false;
}
