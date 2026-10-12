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

// Puts comics in a folder you name; '' goes back to the folder from their titles.
export function setFolder(ids, folder) {
  return run('books', 'readwrite', (s) => {
    for (const id of ids) {
      const req = s.get(id);
      req.onsuccess = () => {
        if (req.result) s.put({ ...req.result, folder: folder.trim() });
      };
    }
  });
}

// The series part of a title, for grouping: "Daredevil v1 003 (1964)" → "Daredevil v1 (1964)",
// "Saga 012" → "Saga", "Webtoon Ep 5" → "Webtoon". The computer app uses the same rule.
const ISSUE_AT_END = /^(.*?)\s*#?\s*\b(\d+(?:\.\d+)?)\b(?!.*\b\d+\b)/;
const LEAD_IN = /(?:[\s\-:,#]+(?:ep|episode|issue|no|nr|number|chapter|ch|part|pt|book|vol|volume|tome)\.?)*[\s\-:,#.]*$/i;
const BRACKETS = /\([^)]*\)|\[[^\]]*\]/g;

export function seriesFromTitle(title) {
  const t = String(title || '').trim();
  const year = (t.match(/\((\d{4})\)/) || [])[1] || '';
  let name = t.replace(BRACKETS, ' ');
  const m = name.match(ISSUE_AT_END);
  if (m && m[1].trim()) name = m[1];
  name = name.replace(LEAD_IN, '').replace(/\s+/g, ' ').trim();
  if (!name) name = t.replace(/\(\d{4}\)/, '').replace(/\s+/g, ' ').trim();
  return year ? `${name} (${year})` : name;
}

export function issueFromTitle(title) {
  const m = String(title || '').replace(BRACKETS, ' ').match(ISSUE_AT_END);
  return m ? Number(m[2]) : null;
}

export const folderOf = (book) => book.folder || seriesFromTitle(book.title);

// The comic after this one in its series folder, or null at the end of the folder.
export function nextInSeries(books, book) {
  const run = inIssueOrder(books.filter((b) => folderOf(b).toLowerCase() === folderOf(book).toLowerCase()));
  const i = run.findIndex((b) => b.id === book.id);
  return i >= 0 && i < run.length - 1 ? run[i + 1] : null;
}

// How the phone and the computer know they have the same comic: its file name and size.
export const placeKey = (book) => `${String(book.fileName || '').toLowerCase()}|${book.size}`;

// Places read on the computer (from the relay): a newer one moves this phone to that page.
// Returns how many comics moved.
export async function applyPlaces(places) {
  const books = await listBooks();
  const newer = books.filter((b) => {
    const p = places[placeKey(b)];
    return p && p.at > (b.lastRead || '');
  });
  if (!newer.length) return 0;
  await run('books', 'readwrite', (s) => {
    for (const b of newer) {
      const p = places[placeKey(b)];
      s.put({ ...b, page: Math.min(p.page, (p.pages || b.pages) - 1), pages: p.pages || b.pages, lastRead: p.at });
    }
  });
  return newer.length;
}

// A series' comics in issue order: numbered ones by number, then the rest by title.
export function inIssueOrder(books) {
  return [...books].sort((a, b) => {
    const x = issueFromTitle(a.title), y = issueFromTitle(b.title);
    if ((x === null) !== (y === null)) return x === null ? 1 : -1;
    return (x ?? 0) - (y ?? 0) || a.title.localeCompare(b.title, undefined, { numeric: true, sensitivity: 'base' });
  });
}

// The shelf grouped into series folders, each in issue order. The series read most recently comes first,
// then the ones added most recently.
export function groupBySeries(books) {
  const groups = new Map();
  for (const b of books) {
    const name = folderOf(b);
    const key = name.toLowerCase();
    if (!groups.has(key)) groups.set(key, { name, books: [] });
    groups.get(key).books.push(b);
  }
  const latest = (g, field) => g.books.reduce((m, b) => ((b[field] || '') > m ? b[field] || '' : m), '');
  return [...groups.values()]
    .map((g) => ({ ...g, books: inIssueOrder(g.books) }))
    .sort((a, b) => latest(b, 'lastRead').localeCompare(latest(a, 'lastRead')) || latest(b, 'addedAt').localeCompare(latest(a, 'addedAt')));
}

const finished = (b) => b.lastRead && b.page >= b.pages - 1;

// The one to carry on with: started but not finished, else the first not finished, else the first.
export function upTo(group) {
  return group.books.find((b) => b.lastRead && !finished(b)) || group.books.find((b) => !finished(b)) || group.books[0];
}

export const readCount = (group) => group.books.filter(finished).length;

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
  if (book.page >= book.pages - 1) return '✓ Read';
  return `Page ${book.page + 1} of ${book.pages}`;
}
