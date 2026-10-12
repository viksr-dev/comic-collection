// Digital comics kept on the phone (in the browser's storage), with the page
// you're up to. The files themselves are kept apart from the list so the list
// loads quickly.
import { openComic, titleFromFileName } from './comicfile.js';

const DB_NAME = 'comic-reader';
let dbPromise;

function open() {
  dbPromise ||= new Promise((resolve, reject) => {
    const req = indexedDB.open(DB_NAME, 1);
    req.onupgradeneeded = () => {
      req.result.createObjectStore('books', { keyPath: 'id' });
      req.result.createObjectStore('files', { keyPath: 'id' });
    };
    req.onsuccess = () => resolve(req.result);
    req.onerror = () => reject(req.error);
  });
  return dbPromise;
}

async function run(stores, mode, fn) {
  const db = await open();
  return new Promise((resolve, reject) => {
    const t = db.transaction(stores, mode);
    const result = fn(...[].concat(stores).map((s) => t.objectStore(s)));
    t.oncomplete = () => resolve(result && 'result' in result ? result.result : result);
    t.onerror = () => reject(t.error);
    t.onabort = () => reject(t.error || new Error('Saving was cancelled. The phone may be out of space for this app.'));
  });
}

// Newest read first, then newest added.
export async function listBooks() {
  const books = await run('books', 'readonly', (s) => s.getAll());
  return books.sort((a, b) => (b.lastRead || '').localeCompare(a.lastRead || '') || (b.addedAt || '').localeCompare(a.addedAt || ''));
}

export const getBook = (id) => run('books', 'readonly', (s) => s.get(id));

export async function getFile(id) {
  const row = await run('files', 'readonly', (s) => s.get(id));
  return row?.blob;
}

// A small picture of the first page for the shelf.
async function makeCover(comic) {
  try {
    const page = await comic.getPage(0, 480);
    const img = await createImageBitmap(page);
    const w = 240;
    const h = Math.round((img.height / img.width) * w);
    const canvas = document.createElement('canvas');
    canvas.width = w;
    canvas.height = Math.min(h, 480);
    canvas.getContext('2d').drawImage(img, 0, 0, w, h);
    img.close?.();
    return canvas.toDataURL('image/jpeg', 0.8);
  } catch {
    return '';
  }
}

// Adds a file to the shelf. The same file added twice is only kept once.
// Returns { book, added }.
export async function addFile(file) {
  const existing = (await listBooks()).find((b) => b.fileName === file.name && b.size === file.size);
  if (existing) return { book: existing, added: false };
  const comic = await openComic(file);
  try {
    const book = {
      id: crypto.randomUUID(),
      title: titleFromFileName(file.name),
      fileName: file.name,
      size: file.size,
      kind: comic.kind,
      pages: comic.pageCount,
      page: 0,
      lastRead: '',
      addedAt: new Date().toISOString(),
      cover: await makeCover(comic),
    };
    // Copied into a plain Blob: some phones lose a picked file's contents once the picker closes.
    const blob = new Blob([await file.arrayBuffer()], { type: file.type || 'application/octet-stream' });
    await run(['books', 'files'], 'readwrite', (books, files) => {
      files.put({ id: book.id, blob });
      books.put(book);
    });
    return { book, added: true };
  } finally {
    comic.close();
  }
}

export function savePlace(id, page, pages) {
  return run('books', 'readwrite', (s) => {
    const req = s.get(id);
    req.onsuccess = () => {
      if (req.result) s.put({ ...req.result, page, pages: pages || req.result.pages, lastRead: new Date().toISOString() });
    };
  });
}

export function startAgain(id) {
  return run('books', 'readwrite', (s) => {
    const req = s.get(id);
    req.onsuccess = () => {
      if (req.result) s.put({ ...req.result, page: 0 });
    };
  });
}

export function renameBook(id, title) {
  return run('books', 'readwrite', (s) => {
    const req = s.get(id);
    req.onsuccess = () => {
      if (req.result) s.put({ ...req.result, title });
    };
  });
}

export function removeBook(id) {
  return run(['books', 'files'], 'readwrite', (books, files) => {
    books.delete(id);
    files.delete(id);
  });
}

export function progressText(book) {
  if (!book.lastRead) return 'New';
  if (book.page >= book.pages - 1) return 'Finished';
  return `Page ${book.page + 1} of ${book.pages}`;
}
