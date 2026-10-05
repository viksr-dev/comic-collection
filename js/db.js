// The collection lives in the phone's browser storage (IndexedDB).

const DB_NAME = 'comic-collection';
const STORE = 'comics';
let dbPromise;

function open() {
  if (!dbPromise) {
    dbPromise = new Promise((resolve, reject) => {
      const req = indexedDB.open(DB_NAME, 1);
      req.onupgradeneeded = () => {
        const store = req.result.createObjectStore(STORE, { keyPath: 'id' });
        store.createIndex('barcode', 'barcode');
      };
      req.onsuccess = () => resolve(req.result);
      req.onerror = () => reject(req.error);
    });
  }
  return dbPromise;
}

async function tx(mode, fn) {
  const db = await open();
  return new Promise((resolve, reject) => {
    const t = db.transaction(STORE, mode);
    const result = fn(t.objectStore(STORE));
    t.oncomplete = () => resolve(result && 'result' in result ? result.result : result);
    t.onerror = () => reject(t.error);
  });
}

export const allComics = () => tx('readonly', (s) => s.getAll());
export const getComic = (id) => tx('readonly', (s) => s.get(id));
export const deleteComic = (id) => tx('readwrite', (s) => s.delete(id));

export function saveComic(comic) {
  const now = new Date().toISOString();
  const record = { ...comic, id: comic.id || crypto.randomUUID(), addedAt: comic.addedAt || now, updatedAt: now };
  return tx('readwrite', (s) => s.put(record)).then(() => record);
}

export function deleteMany(ids) {
  return tx('readwrite', (s) => {
    for (const id of ids) s.delete(id);
  });
}

export function addMany(comics) {
  return tx('readwrite', (s) => {
    for (const c of comics) s.put(c);
  });
}

// Marks comics as sent to the computer, re-reading each so a lookup saved
// meanwhile isn't overwritten.
export function markSent(ids) {
  return tx('readwrite', (s) => {
    for (const id of ids) {
      const req = s.get(id);
      req.onsuccess = () => {
        if (req.result) s.put({ ...req.result, sentToComputer: true });
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
