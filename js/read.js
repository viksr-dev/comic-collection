// The Read tab: digital comics kept on this phone, ready to read anywhere.
import { listBooks, addFile, removeBook, renameBook, startAgain, progressText } from './shelf.js';
import { openReader } from './reader.js';

const $ = (sel) => document.querySelector(sel);
let wired = false;
let menuBook = null;

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

async function open(id) {
  const book = (await listBooks()).find((b) => b.id === id);
  if (!book) return;
  try {
    await openReader(book, { onClose: () => showRead() });
  } catch (err) {
    alert(`Couldn't open it: ${err.message}`);
  }
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

export async function showRead() {
  wire();
  const books = await listBooks();
  const grid = $('#read-grid');
  grid.innerHTML = '';
  $('#read-empty').hidden = books.length > 0;
  for (const book of books) {
    const tile = document.createElement('div');
    tile.className = 'read-tile';
    const progress = book.lastRead && book.pages ? Math.min(1, (book.page + 1) / book.pages) : 0;
    tile.innerHTML = `
      <div class="read-cover">
        ${book.cover ? '<img alt="">' : `<span>${book.kind}</span>`}
        ${book.lastRead ? '' : '<b class="read-new">NEW</b>'}
        ${progress ? `<i class="read-progress"><i style="width:${Math.round(progress * 100)}%"></i></i>` : ''}
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
    grid.appendChild(tile);
  }
  showStorage();
}
