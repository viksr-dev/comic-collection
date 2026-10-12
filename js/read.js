// The Read tab: digital comics kept on this phone, ready to read anywhere.
import { listBooks, addFile, removeBook, renameBook, startAgain, progressText, setFolder, folderOf, seriesFromTitle,
  groupBySeries, inIssueOrder, upTo, readCount, nextInSeries, placeKey, applyPlaces, getBook } from './shelf.js';
import { syncPlaces } from './lookup.js';
import { openReader } from './reader.js';

const $ = (sel) => document.querySelector(sel);
let wired = false;
let menuBook = null;
let openFolder = ''; // the series folder being looked at, or '' for the whole shelf

function toast(msg, ms = 2600) {
  const el = $('#toast');
  el.textContent = msg;
  el.hidden = false;
  clearTimeout(toast.timer);
  toast.timer = setTimeout(() => (el.hidden = true), ms);
}

const sizeText = (bytes) => (bytes >= 1e9 ? `${(bytes / 1e9).toFixed(1)} GB` : `${Math.max(1, Math.round(bytes / 1e6))} MB`);

function wire() {
  if (wired) return;
  wired = true;
  $('#read-input').addEventListener('change', async (e) => {
    const files = [...e.target.files];
    e.target.value = '';
    await addFiles(files);
  });
  $('#read-menu').addEventListener('close', () => (menuBook = null));
  $('#read-back').addEventListener('click', () => {
    openFolder = '';
    showRead();
  });
  $('#read-menu').addEventListener('click', async (e) => {
    const act = e.target.closest('[data-act]')?.dataset.act;
    if (!act || !menuBook) return;
    const book = menuBook;
    $('#read-menu').close();
    if (act === 'again') {
      await startAgain(book.id);
      open(book.id);
    } else if (act === 'rename') {
      const title = prompt('Name shown on the shelf:', book.title);
      if (title?.trim()) await renameBook(book.id, title.trim());
      showRead();
    } else if (act === 'move') {
      const names = [...new Set((await listBooks()).map(folderOf))].sort((a, b) => a.localeCompare(b));
      const answer = prompt(`Folder for ${book.title}? Type a folder name (yours: ${names.slice(0, 12).join(', ')}${names.length > 12 ? '…' : ''}). Leave it empty to go by the comic's name.`, folderOf(book));
      if (answer === null) return;
      const auto = seriesFromTitle(book.title);
      await setFolder([book.id], answer.trim().toLowerCase() === auto.toLowerCase() ? '' : answer);
      showRead();
    } else if (act === 'remove') {
      if (confirm(`Remove ${book.title} from this phone? It stays wherever you added it from (like iCloud Drive).`)) {
        await removeBook(book.id);
        showRead();
      }
    }
  });
}

async function addFiles(files) {
  if (!files.length) return;
  let added = 0;
  const problems = [];
  let last = null;
  for (let i = 0; i < files.length; i++) {
    $('#read-status').textContent = `Adding ${i + 1} of ${files.length}: ${files[i].name}`;
    try {
      const result = await addFile(files[i]);
      if (result.added) added++;
      last = result.book;
    } catch (err) {
      problems.push(`${files[i].name}: ${err.message}`);
    }
  }
  $('#read-status').textContent = '';
  await showRead();
  if (problems.length) alert(`${problems.length} couldn't be added:\n\n${problems.slice(0, 8).join('\n')}`);
  else if (files.length === 1 && last) open(last.id);
  else toast(`Added ${added} comic${added === 1 ? '' : 's'}.`);
}

// The sync code from Settings ("Send scans to your computer"), which also keeps reading places in step.
const syncCode = () => {
  try { return localStorage.getItem('syncCode') || ''; } catch { return ''; }
};

// Tells the computer where you got to in a comic (quietly; it tries again next time).
async function sendPlace(id) {
  const book = await getBook(id);
  if (!book?.lastRead || !syncCode()) return;
  try {
    await syncPlaces(syncCode(), { [placeKey(book)]: { page: book.page, pages: book.pages, at: book.lastRead } });
  } catch {}
}

// Picks up where you got to on the computer.
let lastPull = 0;
async function pullPlaces() {
  if (!syncCode() || Date.now() - lastPull < 20000) return;
  lastPull = Date.now();
  try {
    const places = await syncPlaces(syncCode());
    if (places && (await applyPlaces(places))) showRead();
  } catch {}
}

// Opens a comic, and carries on with the next issue in its folder when you ask.
async function open(id) {
  let book = (await listBooks()).find((b) => b.id === id);
  while (book) {
    const nextBook = nextInSeries(await listBooks(), book);
    let result;
    try {
      result = await openReader(book, { nextBook });
    } catch (err) {
      alert(`Couldn't open it: ${err.message}`);
      break;
    }
    sendPlace(book.id);
    book = result === 'next' ? await getBook(nextBook.id) : null;
  }
  showRead();
}

async function showStorage() {
  const el = $('#read-storage');
  try {
    const { usage, quota } = await navigator.storage.estimate();
    el.textContent = quota ? `Using ${sizeText(usage || 0)} of about ${sizeText(quota)} this app can keep on the phone.` : '';
  } catch {
    el.textContent = '';
  }
}

function bookTile(book) {
  const tile = document.createElement('div');
  tile.className = 'read-tile';
  const progress = book.lastRead && book.pages ? Math.min(1, (book.page + 1) / book.pages) : 0;
  const done = book.lastRead && book.page >= book.pages - 1;
  tile.innerHTML = `
    <div class="read-cover">
      ${book.cover ? '<img alt="">' : `<span>${book.kind}</span>`}
      ${!book.lastRead ? '<b class="read-new">NEW</b>' : done ? '<b class="read-new">✓ READ</b>' : ''}
      ${progress && !done ? `<i class="read-progress"><i style="width:${Math.round(progress * 100)}%"></i></i>` : ''}
    </div>
    <div class="read-name"></div>
    <div class="read-sub"></div>
    <button class="read-more" aria-label="More">⋯</button>`;
  if (book.cover) tile.querySelector('img').src = book.cover;
  tile.querySelector('.read-name').textContent = book.title;
  tile.querySelector('.read-sub').textContent = progressText(book);
  tile.addEventListener('click', (e) => {
    if (e.target.closest('.read-more')) {
      menuBook = book;
      $('#read-menu-title').textContent = book.title;
      $('#read-menu').showModal();
      return;
    }
    open(book.id);
  });
  return tile;
}

function folderTile(group) {
  const tile = document.createElement('div');
  tile.className = 'read-tile folder';
  const cover = upTo(group);
  const done = readCount(group);
  const fresh = group.books.filter((b) => !b.lastRead).length;
  tile.innerHTML = `
    <div class="read-cover">
      ${cover.cover ? '<img alt="">' : `<span>${cover.kind}</span>`}
      ${fresh ? `<b class="read-new">${fresh} NEW</b>` : ''}
      ${done ? `<i class="read-progress"><i style="width:${Math.round((done / group.books.length) * 100)}%"></i></i>` : ''}
    </div>
    <div class="read-name"></div>
    <div class="read-sub"></div>`;
  if (cover.cover) tile.querySelector('img').src = cover.cover;
  tile.querySelector('.read-name').textContent = group.name;
  tile.querySelector('.read-sub').textContent = `${group.books.length} comics · ${done ? `${done} read` : 'none read yet'}`;
  tile.addEventListener('click', () => {
    openFolder = group.name;
    showRead();
    window.scrollTo(0, 0);
  });
  return tile;
}

// The shelf: a folder for each series (in issue order inside), a comic on its own where a series has just one.
export async function showRead() {
  wire();
  const all = await listBooks();
  const grid = $('#read-grid');
  grid.innerHTML = '';
  $('#read-empty').hidden = all.length > 0;
  const inFolder = openFolder ? inIssueOrder(all.filter((b) => folderOf(b).toLowerCase() === openFolder.toLowerCase())) : [];
  if (!inFolder.length) openFolder = '';
  $('#read-folder-bar').hidden = !openFolder;
  $('#read-folder-title').textContent = openFolder;
  if (openFolder) {
    for (const book of inFolder) grid.appendChild(bookTile(book));
  } else {
    for (const group of groupBySeries(all)) grid.appendChild(group.books.length > 1 ? folderTile(group) : bookTile(group.books[0]));
  }
  showStorage();
  pullPlaces();
}
