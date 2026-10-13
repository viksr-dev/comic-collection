import { parseBarcode, isValidBase, ordinal } from './barcode.js';
import { allComics, saveComic, deleteMany, deleteForGood, replaceAll, addMany, markSent, requestPersistence, binComics, restoreFromBin, emptyBin, BIN_DAYS } from './db.js';
import { Scanner, readImageFile } from './scanner.js';
import { hasRelay, getRelayUrl, setRelayUrl, testRelay, lookupBarcode, searchTitle, issueDetails, sendToComputer, getLibrary, seriesLengths } from './lookup.js';
import { exportCsv, exportBackup, readBackup, toCsv, changedFields } from './backup.js';
import { clzToComics, looseKey } from './clz.js';
import { indexLibrary, checkLibrary, ownedDetails } from './library.js';
import { parseSpoken, speechSupported, listen } from './voice.js';

const $ = (sel) => document.querySelector(sel);
const esc = (s) => String(s ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]);
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

let comics = [];
let scanner;
let sheet = { parsed: null, editing: null, match: null };

// ---------- helpers ----------

// A short message at the bottom. With undo, it has an Undo button and stays a little longer.
function toast(msg, ms = 2600, undo = null) {
  const el = $('#toast');
  el.textContent = msg;
  if (undo) {
    const b = Object.assign(document.createElement('button'), { type: 'button', className: 'toast-undo', textContent: 'Undo' });
    b.onclick = async () => {
      el.hidden = true;
      await undo();
      await reload();
    };
    el.append(' ', b);
    ms = Math.max(ms, 7000);
  }
  el.hidden = false;
  clearTimeout(toast.timer);
  toast.timer = setTimeout(() => (el.hidden = true), ms);
}

function lsGet(key) {
  try { return localStorage.getItem(key); } catch { return null; }
}
function lsSet(key, value) {
  try { localStorage.setItem(key, value); } catch {}
}

// Trade paperbacks, hardcovers and other collected editions, kept apart from single issues
// (the computer app uses the same test).
const TRADE_WORDS = ['trade', 'paperback', 'hardcover', 'hard cover', 'hardback', 'graphic novel', 'omnibus', 'compendium',
  'collected', 'deluxe edition', 'absolute edition'];
function isTrade(c) {
  const f = String(c.format || '').trim().toLowerCase();
  return !!f && (['tpb', 'tp', 'hc', 'hb', 'gn', 'ogn'].includes(f) || TRADE_WORDS.some((w) => f.includes(w)));
}
// The Missing issues list shows series, not comics, so a comic never "matches" it.
const kindMatches = (c, kind) => kind !== 'missing' && (kind === 'all' || (kind === 'trades') === isTrade(c));

function label(c) {
  const vol = c.volume ? ` (${c.volume})` : '';
  const letter = /^[A-Z]{1,2}$/.test(c.variant || '') ? c.variant : '';
  const num = c.number ? ` #${c.number}${letter}` : '';
  return `${c.series || (c.barcode ? 'Scanned comic' : 'Untitled')}${vol}${num}`;
}

function setThumb(el, url) {
  if (url) {
    el.style.backgroundImage = `url(${JSON.stringify(url)})`;
    el.textContent = '';
  } else {
    el.textContent = 'No cover';
  }
}

function compareComics(a, b) {
  const opts = { numeric: true, sensitivity: 'base' };
  // "The Amazing Spider-Man" sorts under A.
  const name = (c) => (c.series || '').replace(/^the\s+/i, '');
  return (
    name(a).localeCompare(name(b), undefined, opts) ||
    String(a.volume || '').localeCompare(String(b.volume || ''), undefined, opts) ||
    String(a.number || '').localeCompare(String(b.number || ''), undefined, opts)
  );
}

// ---------- views ----------

function showView(name) {
  for (const v of ['scan', 'collection', 'read', 'wishlist', 'settings']) $(`#view-${v}`).hidden = v !== name;
  document.querySelectorAll('.tabs button').forEach((b) => b.classList.toggle('active', b.dataset.view === name));
  if (name !== 'scan') stopScan();
  if (name === 'collection') renderList();
  if (name === 'settings') renderSettings();
  if (name === 'read') import('./read.js').then((m) => m.showRead()).catch((err) => toast(`Couldn't open your comics: ${err.message}`));
  if (name === 'wishlist') {
    renderWishlist();
    refreshLibrary();
  }
}

async function reload() {
  comics = await allComics();
  await takeComputerChanges();
  const copies = comics.reduce((n, c) => n + (Number(c.quantity) || 1), 0);
  $('#count').textContent = comics.length ? `${copies} comic${copies === 1 ? '' : 's'}` : '';
  if (!$('#view-collection').hidden) renderList();
  queueSend();
}

// ---------- scanning ----------

async function startScan() {
  scanner ||= new Scanner($('#video'));
  const status = $('#scan-status');
  status.className = 'scan-status';
  status.textContent = 'Point the camera at the barcode on the cover, including the small 5-digit code on its right.';
  $('#camera-idle').hidden = true;
  $('#stop-scan').hidden = false;
  const batch = $('#batch-mode').checked;
  if (batch) status.textContent = 'Scan each comic in turn. They collect in the list below; save them all when you\'re done.';
  try {
    await scanner.start(
      (digits, gotAddon) => {
        navigator.vibrate?.(80);
        if (batch) {
          addToTrayAsking(digits).then((item) => {
            status.className = item ? 'scan-status good' : 'scan-status';
            status.textContent = item
              ? item.ownedId || item.pc
                ? `${item.note}. Next one!`
                : `Added ${trayLabel(item)}${item.box ? ` for ${item.box}` : ''}${gotAddon ? '' : " (couldn't read the small 5-digit code)"}. Next one!`
              : 'Skipped that one. Next one!';
          });
          return;
        }
        resetScanUi();
        if (!gotAddon) toast("Couldn't read the small 5-digit code. You can type it in.", 3500);
        openScanned(parseBarcode(digits));
      },
      () => {
        status.className = 'scan-status good';
        status.textContent = 'Got the main barcode. Hold steady for the small 5-digit code…';
      },
      { continuous: batch },
    );
    $('#torch').hidden = !scanner.torchSupported;
  } catch (err) {
    resetScanUi();
    status.textContent =
      err.name === 'NotAllowedError'
        ? 'Camera permission was blocked. Allow camera access for this app in your phone settings, then try again.'
        : `Couldn't start the camera (${err.message}). Try "Scan from a photo" instead.`;
  }
}

function resetScanUi() {
  $('#camera-idle').hidden = false;
  $('#stop-scan').hidden = true;
  $('#torch').hidden = true;
  $('#torch').classList.remove('primary');
}

function stopScan() {
  if (scanner?.running) scanner.stop();
  resetScanUi();
}

// ---------- "you already have this" question ----------

// Resolves true for "Add anyway", false for "Cancel" (or closing it), and with offerBox,
// { box: 'Box 8' } for "Put it in this box" (the one you have, instead of adding it again).
function askAddAnyway(html, { offerBox = false, box = '' } = {}) {
  const dialog = $('#dupe-dialog');
  $('#dupe-text').innerHTML = html;
  $('#dupe-box-row').hidden = !offerBox;
  $('#dupe-add').classList.toggle('primary', !offerBox);
  if (offerBox) {
    fillBoxChoices();
    $('#dupe-box-name').value = box || lsGet('lastBox') || '';
  }
  navigator.vibrate?.([60, 80, 60]);
  return new Promise((resolve) => {
    const done = (answer) => {
      dialog.onclose = null;
      if (dialog.open) dialog.close();
      resolve(answer);
    };
    $('#dupe-add').onclick = () => done(true);
    $('#dupe-box').onclick = () => {
      const name = $('#dupe-box-name').value.trim();
      if (!name) return $('#dupe-box-name').focus();
      lsSet('lastBox', name);
      done({ box: name });
    };
    $('#dupe-cancel').onclick = () => done(false);
    dialog.onclose = () => done(false);
    dialog.showModal();
  });
}

const copies = (c) => {
  const n = Number(c.quantity) || 1;
  return `${n} cop${n === 1 ? 'y' : 'ies'}`;
};

// What to say when this barcode (or Metron issue, or series and number) is
// already in the collection, on this phone or the computer. '' if it isn't.
function alreadyHaveText({ parsed, metronId, series, number, ignoreId } = {}) {
  const others = comics.filter((c) => c.id !== ignoreId);
  const exact = others.find((c) => (metronId && c.metronId === metronId) || (parsed?.addon && c.barcode === parsed.full));
  if (exact) return `You already have <b>${esc(label(exact))}</b> (${copies(exact)}).`;
  const key = looseKey(series, number);
  const similar = key ? others.filter((c) => looseKey(c.series, c.number) === key) : [];
  if (similar.length) return `You may already have <b>${esc(label(similar[0]))}</b>${similar.length > 1 ? ` and ${similar.length - 1} more like it` : ''}.`;
  const pc = checkLibrary(library, { barcode: parsed?.addon ? parsed.full : '', metronId, series, number });
  if (pc.owned) return `On your computer you ${pc.sure ? '' : 'probably '}already own <b>${esc(pc.owned)}</b>. ${esc(ownedDetails(pc.info))}`.trim();
  return '';
}

// The comic you already have that alreadyHaveText found: { phone: comic } or { pc: info }, or {}.
function alreadyHave({ parsed, metronId, series, number } = {}) {
  const exact = comics.find((c) => (metronId && c.metronId === metronId) || (parsed?.addon && c.barcode === parsed.full));
  if (exact) return { phone: exact };
  const key = looseKey(series, number);
  const similar = key && comics.find((c) => looseKey(c.series, c.number) === key);
  if (similar) return { phone: similar };
  const pc = checkLibrary(library, { barcode: parsed?.addon ? parsed.full : '', metronId, series, number });
  return pc.owned ? { pc: pc.info } : {};
}

// Puts a comic you already have in a box, here and (through the mailbox) on the computer.
async function putInBox(have, box) {
  if (have.phone) {
    await saveComic({ ...have.phone, box, boxChanged: true });
    return label(have.phone);
  }
  const i = have.pc;
  const key = String(i.barcode || '') || `${i.series}|${i.volume}|${i.issue}`;
  saveBoxMoves([...boxMoves().filter((m) => m.key !== key),
    { series: i.series, volume: i.volume, number: i.issue, upc: i.barcode || '', metronId: i.metronId || '', box, key }]);
  return i.label;
}

// ---------- add / edit sheet ----------

const form = $('#comic-form');

function fillForm(c) {
  for (const name of ['series', 'number', 'volume', 'publisher', 'coverDate', 'variantName', 'format', 'title', 'condition', 'notes', 'box']) {
    form.elements[name].value = c[name] ?? '';
  }
  fillBoxChoices();
  form.elements.read.checked = !!c.read;
  form.elements.quantity.value = c.quantity || 1;
  showCover(c.coverUrl);
}

function showCover(url) {
  const img = $('#cover-img');
  img.hidden = !url;
  if (url) img.src = url;
  else img.removeAttribute('src');
}

function renderBarcodeBox(parsed) {
  const box = $('#barcode-box');
  box.hidden = !parsed;
  $('#addon-box').hidden = !parsed || !!parsed.addon;
  if (!parsed) return;
  let html = `Barcode <b>${esc(parsed.upc)}</b>`;
  if (parsed.addon) html += ` <b>${esc(parsed.addon)}</b>`;
  if (parsed.issue) {
    html += `<br>Issue ${esc(parsed.issue)}, cover ${esc(parsed.cover)}, ${esc(ordinal(parsed.printing))} printing`;
  }
  box.innerHTML = html;
}

// An exact match (same barcode or Metron issue), or failing that, comics with
// the same series name and issue number, e.g. ones imported from CLZ.
function findDuplicate(parsed, metronId) {
  const others = comics.filter((c) => c.id !== sheet.editing?.id);
  const exact = others.find(
    (c) => (metronId && c.metronId === metronId) || (parsed?.addon && c.barcode === parsed.full),
  );
  if (exact) return { exact, similar: [] };
  const key = looseKey(form.elements.series.value, form.elements.number.value);
  return { exact: null, similar: key ? others.filter((c) => looseKey(c.series, c.number) === key) : [] };
}

function renderDuplicate({ exact, similar } = { similar: [] }) {
  const box = $('#dupe');
  box.hidden = !exact && !similar.length;
  if (box.hidden) return;
  if (!exact) {
    const list = similar.slice(0, 4).map((c) => `<li>${esc(label(c))}${c.variantName ? ` · ${esc(c.variantName)}` : ''}</li>`).join('');
    box.innerHTML = `You may already have this:<ul class="similar">${list}</ul>
      ${similar.length === 1 ? '<div><button type="button" class="btn" id="add-copy">It\'s that one: add a copy</button></div>' : ''}`;
    if (similar.length === 1) wireAddCopy(similar[0]);
    return;
  }
  const n = Number(exact.quantity) || 1;
  box.innerHTML = `You already have <b>${esc(label(exact))}</b> (${n} cop${n === 1 ? 'y' : 'ies'}).
    <div><button type="button" class="btn" id="add-copy">Add another copy</button></div>`;
  wireAddCopy(exact);
}

// What the computer says about this comic: owned there, or on the wishlist.
function renderPcNote() {
  const box = $('#pc-note');
  const f = form.elements;
  const hit = sheet.editing ? {} : checkLibrary(library, {
    barcode: sheet.parsed?.addon ? sheet.parsed.full : '', metronId: sheet.match?.metronId, series: f.series.value, number: f.number.value,
  });
  // The phone's own "you already have this" covers it when it knows.
  const phoneKnows = !$('#dupe').hidden;
  const details = hit.owned ? ownedDetails(hit.info) : '';
  box.hidden = !(hit.wish || (hit.owned && (!phoneKnows || details)));
  if (box.hidden) return;
  box.classList.toggle('good', !!hit.wish);
  box.classList.toggle('warn', !hit.wish);
  box.innerHTML = hit.wish
    ? `On your wishlist: <b>${esc(hit.wish.label)}</b>${hit.wish.arc ? ` (for ${esc(hit.wish.arc)})` : ''}`
    : phoneKnows
      ? `On your computer: ${esc(details)}`
      : `On your computer you ${hit.sure ? '' : 'probably '}already own <b>${esc(hit.owned)}</b>. ${esc(details)}`;
}

function wireAddCopy(dupe) {
  const n = Number(dupe.quantity) || 1;
  $('#add-copy').onclick = async () => {
    await saveComic({ ...dupe, quantity: n + 1 });
    $('#sheet').close();
    await reload();
    toast(`Now ${n + 1} copies of ${label(dupe)}`);
  };
}

// A scanned barcode you already have asks first, before anything is looked up.
async function openScanned(parsed) {
  const have = parsed.addon ? alreadyHaveText({ parsed }) : '';
  const answer = have ? await askAddAnyway(`${have}<br><br>Put it in a box, or add it again?`, { offerBox: true }) : true;
  if (answer?.box) {
    toast(`Put ${await putInBox(alreadyHave({ parsed }), answer.box)} in ${answer.box}`);
    await reload();
    return;
  }
  if (!answer) {
    toast('Not added');
    return;
  }
  openAddSheet({ parsed });
  sheet.confirmed = !!have;
}

function openAddSheet({ parsed = null, editing = null } = {}) {
  sheet = { parsed, editing, match: null, confirmed: false };
  form.reset();
  $('#sheet-title').textContent = editing ? 'Edit comic' : 'Add comic';
  $('#delete-btn').hidden = !editing;
  $('#more-issues-btn').hidden = !editing || !editing.series;
  $('#matches').innerHTML = '';
  $('#lookup-status').textContent = '';
  $('#title-search').open = false;
  $('#addon-input').value = '';

  if (editing) {
    fillForm(editing);
    sheet.parsed = editing.upc ? parseBarcode(editing.upc, editing.addon) : null;
  } else {
    fillForm({ number: parsed?.issue || '', format: parsed?.isbn ? 'Trade Paperback' : '' });
  }
  renderBarcodeBox(sheet.parsed);
  renderDuplicate(editing ? undefined : findDuplicate(parsed));
  renderPcNote();
  $('#ts-number').value = form.elements.number.value;
  $('#ts-series').value = form.elements.series.value;

  $('#sheet').showModal();

  if (!editing) {
    if (parsed) runBarcodeLookup();
    else {
      $('#title-search').open = true;
      if (!hasRelay()) $('#lookup-status').textContent = 'Comic lookup is not set up yet, so fill in the details yourself. You can look them up later.';
      $('#ts-series').focus();
    }
  } else if (editing.needsLookup && hasRelay()) {
    $('#title-search').open = true;
  }
}

async function runBarcodeLookup() {
  const { parsed } = sheet;
  const status = $('#lookup-status');
  if (!hasRelay()) {
    status.textContent = 'Comic lookup is not set up yet. Fill in what you know and save; you can look up the details later.';
    form.elements.series.focus();
    return;
  }
  status.textContent = 'Looking up this barcode…';
  try {
    const results = await lookupBarcode(parsed);
    if (sheet.parsed !== parsed) return;
    if (results.length) {
      status.textContent = results.length === 1 ? 'Found it:' : 'Pick the right one:';
      renderMatches(results);
      if (results.length === 1) chooseMatch(results[0], $('#matches li'));
    } else {
      status.textContent = 'Not found by barcode. Try searching by title, or fill in the details yourself.';
      $('#title-search').open = true;
      $('#ts-series').focus();
    }
  } catch (err) {
    status.textContent = `${err.message} You can still fill in the details and save.`;
  }
}

async function runTitleSearch() {
  const series = $('#ts-series').value.trim();
  const number = $('#ts-number').value.trim();
  const status = $('#lookup-status');
  if (!series) return $('#ts-series').focus();
  if (!hasRelay()) {
    status.textContent = 'Comic lookup is not set up yet (see Settings). Fill in the details below instead.';
    form.elements.series.value = series;
    form.elements.number.value = number;
    return;
  }
  status.textContent = 'Searching…';
  $('#matches').innerHTML = '';
  try {
    const results = await searchTitle(series, number);
    status.textContent = results.length ? `${results.length} result${results.length === 1 ? '' : 's'}. Tap the right one:` : 'Nothing found. Try a shorter series name, or fill in the details yourself.';
    renderMatches(results);
  } catch (err) {
    status.textContent = err.message;
  }
}

function renderMatches(results) {
  const list = $('#matches');
  list.innerHTML = '';
  for (const m of results.slice(0, 30)) {
    const li = document.createElement('li');
    li.innerHTML = `<div class="thumb"></div><div class="item-main">
      <div class="item-title">${esc(label(m))}</div>
      <div class="item-sub">${esc([m.publisher, m.coverDate].filter(Boolean).join(' · '))}</div></div>`;
    setThumb(li.querySelector('.thumb'), m.coverUrl);
    li.onclick = () => chooseMatch(m, li);
    list.append(li);
  }
}

async function chooseMatch(m, li) {
  sheet.match = m;
  document.querySelectorAll('#matches li').forEach((x) => x.classList.toggle('chosen', x === li));
  const keep = { quantity: form.elements.quantity.value, condition: form.elements.condition.value, notes: form.elements.notes.value };
  fillForm({ ...m, ...keep });
  if (!sheet.editing) renderDuplicate(findDuplicate(sheet.parsed, m.metronId));
  renderPcNote();
  if (!m.publisher && m.metronId) {
    try {
      const d = await issueDetails(m.metronId);
      if (d && sheet.match === m) {
        Object.assign(m, d);
        if (!form.elements.publisher.value) form.elements.publisher.value = d.publisher || '';
        if (!form.elements.title.value) form.elements.title.value = d.title || '';
      }
    } catch {}
  }
}

async function saveSheet(e) {
  e.preventDefault();
  const f = form.elements;
  if (!f.series.value.trim()) return f.series.focus();
  const { parsed, editing, match } = sheet;
  if (!editing && !sheet.confirmed) {
    const which = { parsed, metronId: match?.metronId, series: f.series.value, number: f.number.value };
    const have = alreadyHaveText(which);
    const answer = have ? await askAddAnyway(`${have}<br><br>Put the one you have in a box, or add this one anyway?`,
      { offerBox: true, box: f.box.value.trim() }) : true;
    if (answer?.box) {
      const name = await putInBox(alreadyHave(which), answer.box);
      $('#sheet').close();
      await reload();
      toast(`Put ${name} in ${answer.box}`);
      return;
    }
    if (!answer) {
      $('#sheet').close();
      toast('Not added');
      return;
    }
  }
  const record = {
    ...(editing || {}),
    series: f.series.value.trim(),
    number: f.number.value.trim(),
    volume: f.volume.value.trim(),
    publisher: f.publisher.value.trim(),
    coverDate: f.coverDate.value.trim(),
    title: f.title.value.trim(),
    variantName: f.variantName.value.trim(),
    format: f.format.value.trim(),
    condition: f.condition.value,
    notes: f.notes.value.trim(),
    box: f.box.value.trim(),
    read: f.read.checked,
    quantity: Math.max(1, parseInt(f.quantity.value, 10) || 1),
  };
  // A new box, condition or read for a comic the computer may already have is sent across too.
  if (editing) record.changes = [...new Set([...(editing.changes || []), ...syncedChanges(editing, record)])];
  if (parsed) {
    Object.assign(record, { upc: parsed.upc, addon: parsed.addon, barcode: parsed.full });
    if (parsed.addon?.length === 5) Object.assign(record, { variant: parsed.cover, printing: parsed.printing });
  }
  if (match) Object.assign(record, { metronId: match.metronId, coverUrl: match.coverUrl || record.coverUrl });
  // Imported comics already have their details; they only wait for a cover.
  record.needsLookup = !record.metronId && record.source !== 'clz';
  delete record.lookupFailed;

  await saveComic(record);
  $('#sheet').close();
  // A trade added while looking at single issues (or the other way round) switches the list so you can see it.
  if (!kindMatches(record, $('#kind').value)) setKind(isTrade(record) ? 'trades' : 'issues');
  await reload();
  toast(editing ? 'Saved' : `Added ${label(record)}`);
}

// ---------- add more issues of a series, without scanning ----------

// Reads issue numbers typed like "1-12, 15, 20-24" (or "#3 #7"). null if something can't be read or it's over 2,000.
export function parseIssueList(text) {
  const out = [];
  for (const raw of String(text || '').split(/[,;\s]+/)) {
    const part = raw.trim().replace(/^#/, '');
    if (!part) continue;
    const range = part.match(/^(\d+)\s*[-–]\s*#?(\d+)$/);
    if (range) {
      const a = Number(range[1]), b = Number(range[2]);
      if (b < a || b - a > 2000) return null;
      for (let n = a; n <= b; n++) out.push(String(n));
    } else if (/^\d+(\.\d+)?$|^½$/.test(part)) {
      out.push(part === '½' ? part : part.replace(/^0+(?=\d)/, ''));
    } else return null;
    if (out.length > 2000) return null;
  }
  return [...new Set(out)];
}

const sameRun = (a, b) => (a.series || '').toLowerCase() === (b.series || '').toLowerCase() &&
  (a.volume || '').toLowerCase() === (b.volume || '').toLowerCase();
const issueKey = (n) => String(n || '').replace(/^0+(?=\d)/, '').toLowerCase();

// The issue numbers of a series you have: on this phone, and on your computer too when it sends its list.
function ownedIssues(run) {
  const phone = comics.filter((x) => sameRun(x, run)).map((x) => issueKey(x.number));
  const pc = (libraryInfo?.data?.owned || []).filter(([series = '', volume = '']) => sameRun({ series, volume }, run)).map((o) => issueKey(o[2]));
  return { phone: new Set(phone), pc: new Set(pc), all: new Set([...phone, ...pc]) };
}

// Numbers as short runs: 3, 4, 5, 9 gives "3-5, 9".
function runsText(nums) {
  const parts = [];
  for (let i = 0; i < nums.length; ) {
    let j = i;
    while (j + 1 < nums.length && nums[j + 1] === nums[j] + 1) j++;
    parts.push(j === i ? `${nums[i]}` : `${nums[i]}-${nums[j]}`);
    i = j + 1;
  }
  return parts.join(', ');
}

// How many issues each series has on Metron, remembered for a month: { 'name|volume': { total, at } }.
// total 0 means Metron didn't know the series.
const LENGTHS_KEY = 'seriesLengths';
const runKey = (run) => `${(run.series || '').toLowerCase()}|${(run.volume || '').toLowerCase()}`;
function seriesTotal(run) {
  try {
    const hit = JSON.parse(lsGet(LENGTHS_KEY) || '{}')[runKey(run)];
    return hit && Date.now() - Date.parse(hit.at) < 30 * 864e5 ? hit : null;
  } catch { return null; }
}

// Asks Metron (through the relay) how many issues the series has, and remembers it.
async function lookUpSeriesTotal(run) {
  const found = await seriesLengths(run.series);
  const vol = String(run.volume || '').toLowerCase().replace(/^vol(ume)?\.?\s*/, '');
  // The one for this volume (by its start year or number), else the only one, else the longest run.
  const pick = found.find((s) => vol && (String(s.yearBegan) === vol || String(s.volume) === vol)) ||
    (found.length === 1 ? found[0] : [...found].sort((a, b) => b.issueCount - a.issueCount)[0]);
  let all = {};
  try { all = JSON.parse(lsGet(LENGTHS_KEY) || '{}'); } catch {}
  all[runKey(run)] = { total: pick?.issueCount || 0, at: new Date().toISOString() };
  lsSet(LENGTHS_KEY, JSON.stringify(all));
  return all[runKey(run)].total;
}

// What's missing from this series (counting the computer's comics too): between the first and last
// issue you have, or once Metron says how long the series is, everything from #1 to the end.
function gapsText(c) {
  const nums = [...ownedIssues(c).all].filter((n) => /^\d+$/.test(n)).map(Number).sort((a, b) => a - b);
  const total = seriesTotal(c)?.total || 0;
  const from = total ? 1 : nums[0];
  const to = Math.max(nums[nums.length - 1] || 0, total);
  const have = new Set(nums);
  const gaps = [];
  for (let n = from; n <= to; n++) if (!have.has(n)) gaps.push(n);
  return { text: runsText(gaps), count: gaps.length, have: nums.length, first: nums[0], last: nums[nums.length - 1], total };
}

// Every series you have with gaps in it, most missing first: { run, gaps }. Uses the computer's comics too.
function seriesWithGaps() {
  const runs = new Map();
  const add = (series, volume, publisher, format) => {
    if (!series) return;
    const key = `${series.toLowerCase()}|${(volume || '').toLowerCase()}`;
    if (!runs.has(key)) runs.set(key, { series, volume: volume || '', publisher: publisher || '', format: format || '' });
  };
  for (const c of comics) if (!isTrade(c)) add(c.series, c.volume, c.publisher, c.format);
  for (const [series, volume] of libraryInfo?.data?.owned || []) add(series, volume);
  return [...runs.values()].map((run) => ({ run, gaps: gapsText(run) })).filter((x) => x.gaps.count > 0)
    .sort((a, b) => b.gaps.count - a.gaps.count || a.run.series.localeCompare(b.run.series));
}

// Collection › Missing issues: series with gaps; tap one to add the ones you have.
function renderMissing() {
  const list = $('#list');
  list.innerHTML = '';
  const items = seriesWithGaps();
  const q = $('#search').value.trim().toLowerCase();
  for (const { run, gaps } of items.filter((x) => !q || x.run.series.toLowerCase().includes(q))) {
    const li = document.createElement('li');
    const name = `${run.series}${run.volume ? ` (${run.volume})` : ''}`;
    const of = gaps.total ? `${gaps.have} of ${gaps.total}` : `${gaps.have} of #${gaps.first}–${gaps.last}`;
    li.innerHTML = `<div class="item-main"><div class="item-title">${esc(name)}</div>
      <div class="item-sub">You have ${of}. Missing: ${esc(gaps.text)}</div></div>
      <span class="badge todo">${gaps.count} missing</span>`;
    li.onclick = () => openMoreIssues(comics.find((c) => sameRun(c, run)) || run);
    list.append(li);
  }
  const empty = $('#empty');
  empty.hidden = items.length > 0;
  empty.innerHTML = 'No gaps in your runs. 🎉';
  $('#select-mode').hidden = true;
  $('#select-bar').hidden = true;
  $('#bulk-lookup').hidden = true;
  // Finding how long each series is shows issues after your last one too.
  const btn = $('#series-lengths');
  const unknown = allRuns().filter((run) => !seriesTotal(run));
  if (btn.dataset.running !== '1') {
    btn.hidden = !hasRelay() || !unknown.length;
    btn.textContent = `Find how long ${unknown.length === 1 ? 'this series is' : `your ${unknown.length} series are`} on Metron`;
  }
  const note = $('#missing-note');
  note.hidden = btn.hidden && !lsGet('lengthsNote');
  note.textContent = lsGet('lengthsNote') || 'Until a series is looked up, only the gaps between your first and last issue show.';
}

// Every series you have single issues of (here or on the computer).
function allRuns() {
  const runs = new Map();
  for (const c of comics) if (c.series && !isTrade(c)) runs.set(runKey(c), { series: c.series, volume: c.volume || '' });
  for (const [series, volume] of libraryInfo?.data?.owned || []) if (series && !runs.has(runKey({ series, volume }))) runs.set(runKey({ series, volume }), { series, volume: volume || '' });
  return [...runs.values()];
}

// Looks up each series' length one at a time (tap again to stop). The relay keeps answers for a week.
async function findSeriesLengths() {
  const btn = $('#series-lengths');
  if (btn.dataset.running === '1') {
    btn.dataset.stop = '1';
    btn.textContent = 'Stopping…';
    return;
  }
  const todo = allRuns().filter((run) => !seriesTotal(run));
  btn.dataset.running = '1';
  delete btn.dataset.stop;
  try { localStorage.removeItem('lengthsNote'); } catch {}
  let done = 0;
  try {
    for (const run of todo) {
      if (btn.dataset.stop) break;
      btn.textContent = `Looking up ${done + 1} of ${todo.length} (tap to stop)`;
      for (let attempt = 0; ; attempt++) {
        try {
          await lookUpSeriesTotal(run);
          break;
        } catch (err) {
          if (!/Too many/.test(err.message) || attempt >= 3) throw err;
          btn.textContent = 'Metron asked us to slow down, waiting a minute…';
          await sleep(65000);
        }
      }
      done++;
      if (done % 5 === 0) renderMissing();
      if (done < todo.length) await sleep(2500);
    }
  } catch (err) {
    lsSet('lengthsNote', err.message);
    toast(err.message, 6000);
  } finally {
    delete btn.dataset.running;
    delete btn.dataset.stop;
    renderList();
  }
}

// From a comic's edit sheet: adds other issues of its series that you have, by number, so you don't have to scan them.
function openMoreIssues(template) {
  const name = `${template.series}${template.volume ? ` (${template.volume})` : ''}`;
  const gaps = gapsText(template);
  $('#issues-title').textContent = `Add more ${name}`;
  const onPc = libraryInfo?.data?.owned?.length ? ' (here and on your computer)' : '';
  $('#issues-have').textContent = `You have ${gaps.have || 1} of ${name}${onPc}${gaps.count ? `, with ${gaps.count} missing in between (filled in below)` : ''}. ` +
    "Delete any you don't have, or type more with a dash for a run.";
  $('#issues-numbers').value = gaps.text;
  $('#issues-condition').value = '';
  fillBoxChoices();
  $('#issues-box').value = template.box || lsGet('lastBox') || '';
  const count = () => {
    const list = parseIssueList($('#issues-numbers').value);
    $('#issues-count').textContent = list === null ? 'Type numbers like 1-12, 15, 20-24' : `${list.length} issue${list.length === 1 ? '' : 's'}`;
  };
  $('#issues-numbers').oninput = count;
  count();
  // Not looked up yet: ask Metron how long the series is, and add the issues after your last one.
  if (!seriesTotal(template) && hasRelay()) {
    const before = $('#issues-numbers').value;
    lookUpSeriesTotal(template).then((total) => {
      if (!total || !$('#issues-dialog').open || $('#issues-numbers').value !== before) return;
      const more = gapsText(template);
      $('#issues-numbers').value = more.text;
      $('#issues-have').textContent = `You have ${more.have} of the ${more.total} issues of ${name}${onPc}, so ${more.count} are missing (filled in below). ` +
        "Delete any you don't have, or type more with a dash for a run.";
      count();
    }).catch(() => {});
  }
  const dialog = $('#issues-dialog');
  $('#issues-cancel').onclick = () => dialog.close();
  $('#issues-form').onsubmit = async (e) => {
    e.preventDefault();
    const list = parseIssueList($('#issues-numbers').value);
    if (!list?.length) return $('#issues-numbers').focus();
    // Ones on this phone or the computer already are skipped.
    const owned = ownedIssues(template);
    const have = owned.all;
    const onComputer = list.filter((n) => owned.pc.has(issueKey(n)) && !owned.phone.has(issueKey(n))).length;
    const box = $('#issues-box').value.trim();
    const condition = $('#issues-condition').value;
    const now = new Date().toISOString();
    // Same series, volume, publisher and format; the cover is looked up later by series and issue number.
    const records = list.filter((n) => !have.has(issueKey(n))).map((n) => ({
      id: crypto.randomUUID(), series: template.series, volume: template.volume || '', number: n, publisher: template.publisher || '',
      format: template.format || '', condition, box, quantity: 1, needsCover: true, addedAt: now, updatedAt: now,
    }));
    dialog.close();
    if (!records.length) return toast(`You already have all of those${onComputer ? ' (some are on your computer)' : ''}.`, 3500);
    if (box) lsSet('lastBox', box);
    await addMany(records);
    $('#sheet').close();
    setKind(isTrade(template) ? 'trades' : 'issues');
    $('#search').value = template.series;
    await reload();
    showView('collection');
    const skipped = list.length - records.length;
    toast(`Added ${records.length} of ${name}${skipped ? ` (${skipped} you had were skipped${onComputer ? `, ${onComputer} on your computer` : ''})` : ''}`,
      0, () => deleteForGood(records.map((r) => r.id)));
  };
  dialog.showModal();
  $('#issues-numbers').focus();
}

// ---------- collection ----------

// Select mode: tick comics in the list to delete several at once.
let selecting = false;
const picked = new Set();
let shownIds = [];

function setSelecting(on) {
  selecting = on;
  picked.clear();
  $('#select-mode').textContent = on ? 'Done' : 'Select';
  renderList();
}

function renderSelectBar() {
  $('#select-bar').hidden = !selecting;
  if (!selecting) return;
  const n = picked.size;
  $('#select-count').textContent = n ? `${n} selected` : 'Tap comics to select them';
  $('#select-delete').textContent = n ? `Delete ${n}` : 'Delete';
  $('#select-delete').disabled = !n;
  $('#select-box').textContent = n ? `Put ${n} in box` : 'Put in box';
  $('#select-box').disabled = !n;
  $('#select-all').checked = shownIds.length > 0 && shownIds.every((id) => picked.has(id));
}

// Puts the selected comics in a box (or, left empty, takes them out), here and on the computer.
function boxPicked() {
  const chosen = comics.filter((c) => picked.has(c.id));
  if (!chosen.length) return;
  fillBoxChoices();
  const boxes = [...new Set(chosen.map((c) => c.box || ''))];
  $('#box-title').textContent = `Put ${chosen.length} comic${chosen.length === 1 ? '' : 's'} in a box`;
  $('#box-name').value = boxes.length === 1 && boxes[0] ? boxes[0] : lsGet('lastBox') || '';
  const dialog = $('#box-dialog');
  $('#box-cancel').onclick = () => dialog.close();
  $('#box-form').onsubmit = async (e) => {
    e.preventDefault();
    const box = $('#box-name').value.trim();
    dialog.close();
    if (box) lsSet('lastBox', box);
    for (const c of chosen) if ((c.box || '') !== box) await saveComic({ ...c, box, boxChanged: true });
    setSelecting(false);
    await reload();
    toast(box ? `Put ${chosen.length} in ${box}` : `Took ${chosen.length} out of their box`);
  };
  dialog.showModal();
  $('#box-name').focus();
}

async function deletePicked() {
  const chosen = comics.filter((c) => picked.has(c.id));
  if (!chosen.length) return;
  const names = chosen.slice(0, 5).map(label).join('\n');
  const more = chosen.length > 5 ? `\n…and ${chosen.length - 5} more` : '';
  if (!confirm(`Delete ${chosen.length} comic${chosen.length === 1 ? '' : 's'}? They wait in Settings › Deleted comics for ${BIN_DAYS} days in case you want them back.\n\n${names}${more}`)) return;
  const ids = chosen.map((c) => c.id);
  await deleteMany(ids);
  picked.clear();
  await reload();
  toast(`Deleted ${chosen.length} comic${chosen.length === 1 ? '' : 's'}`, 0, () => restoreFromBin(ids));
}

// The key issue note the computer has for this comic (a sure match only), or ''.
function keyNoteFor(c) {
  const hit = checkLibrary(library, { barcode: c.addon ? c.barcode : '', metronId: c.metronId });
  return hit.owned ? hit.info.keyNote || '' : '';
}

function renderList() {
  const missing = $('#kind').value === 'missing';
  $('#sort').hidden = missing;
  $('#needs-filter').closest('label').hidden = missing;
  if (missing) return renderMissing();
  $('#series-lengths').hidden = true;
  $('#missing-note').hidden = true;
  const q = $('#search').value.trim().toLowerCase();
  const needsOnly = $('#needs-filter').checked;
  const kind = $('#kind').value;
  let items = comics.filter((c) => (!needsOnly || c.needsLookup) && kindMatches(c, kind) &&
    (!q || [c.series, c.volume, c.title, c.publisher, c.number, c.variantName, c.format, c.notes, c.barcode, c.box].some((v) => String(v || '').toLowerCase().includes(q))));
  items = $('#sort').value === 'recent'
    ? items.sort((a, b) => (b.addedAt || '').localeCompare(a.addedAt || ''))
    : items.sort(compareComics);

  shownIds = items.map((c) => c.id);
  for (const id of [...picked]) if (!comics.some((c) => c.id === id)) picked.delete(id);

  const list = $('#list');
  list.innerHTML = '';
  const frag = document.createDocumentFragment();
  for (const c of items) {
    const li = document.createElement('li');
    const n = Number(c.quantity) || 1;
    const sub = [c.variantName || c.format, c.publisher, c.coverDate, c.condition, c.box].filter(Boolean).join(' · ') || (c.barcode ? `Barcode ${c.barcode}` : '');
    li.innerHTML = `<div class="thumb"></div><div class="item-main">
      <div class="item-title">${esc(label(c))}</div><div class="item-sub">${esc(sub)}</div></div>
      ${c.needsLookup ? '<span class="badge todo">Needs details</span>' : ''}
      ${keyNoteFor(c) ? `<span class="badge key" title="${esc(keyNoteFor(c))}">★ Key</span>` : ''}
      ${c.read ? '<span class="badge read" title="Read">✓ Read</span>' : ''}
      ${n > 1 ? `<span class="badge">×${n}</span>` : ''}`;
    setThumb(li.querySelector('.thumb'), c.coverUrl);
    if (selecting) {
      const box = Object.assign(document.createElement('input'), { type: 'checkbox', className: 'pick', checked: picked.has(c.id) });
      box.setAttribute('aria-label', `Select ${label(c)}`);
      li.prepend(box);
      li.classList.toggle('picked', box.checked);
      li.onclick = (e) => {
        if (e.target !== box) box.checked = !box.checked;
        box.checked ? picked.add(c.id) : picked.delete(c.id);
        li.classList.toggle('picked', box.checked);
        renderSelectBar();
      };
    } else li.onclick = () => openAddSheet({ editing: c });
    frag.append(li);
  }
  list.append(frag);
  const empty = $('#empty');
  empty.hidden = items.length > 0 || (comics.length > 0 && (q || needsOnly));
  empty.innerHTML = !comics.length ? 'No comics yet. Tap <b>Scan</b> below to add your first one.'
    : kind === 'trades' ? 'No trade paperbacks or hardcovers yet. Set a comic\'s <b>Format</b> to Trade Paperback or Hardcover and it moves here. Scanning a book\'s ISBN barcode does it for you.'
      : 'No single issues here. Choose <b>Everything</b> above to see all your comics.';
  $('#select-mode').hidden = !comics.length;
  renderSelectBar();

  const pending = pendingLookups();
  const bulk = $('#bulk-lookup');
  if (bulk.dataset.running !== '1') {
    bulk.hidden = !pending.length || !hasRelay();
    bulk.textContent = `Find details and covers for ${pending.length} comic${pending.length === 1 ? '' : 's'}`;
  }

  renderBackupNudge();
}

// A reminder to back up once there's something worth losing: comics added or changed since the
// last backup, and that was over two weeks ago (or never). "Later" waits a week.
function renderBackupNudge() {
  const last = lsGet('lastBackup');
  const days = last ? (Date.now() - Date.parse(last)) / 864e5 : Infinity;
  const changed = comics.filter((c) => !last || (c.updatedAt || c.addedAt || '') > last).length;
  const snoozed = Date.now() < Date.parse(lsGet('backupSnooze') || 0);
  const nudge = $('#backup-nudge');
  nudge.hidden = snoozed || !(changed >= 5 && days > 14);
  if (nudge.hidden) return;
  const n = `${changed} comic${changed === 1 ? '' : 's'}`;
  nudge.innerHTML = `${last
    ? `<b>Time for a backup.</b> ${n} added or changed since your last one, ${Math.floor(days)} days ago.`
    : `<b>Back up your collection.</b> It's only on this phone, so if the phone is lost or the app is removed, it goes too.`}
    <div class="nudge-actions"><button class="btn primary" id="nudge-backup">Back up now</button>
    <button class="btn" id="nudge-later">Later</button></div>
    <p class="muted small">Saves a full backup. In the share menu, choose <b>OneDrive</b> or <b>Files</b> to keep it safe.</p>`;
  $('#nudge-backup').onclick = () => doExport('json');
  $('#nudge-later').onclick = () => {
    lsSet('backupSnooze', new Date(Date.now() + 7 * 864e5).toISOString());
    renderBackupNudge();
  };
}

// Imported comics (CLZ) keep their own details and only need a cover. Any
// imported comic not yet matched on Metron counts, whichever app version
// imported it; those without a barcode are found by series and issue number.
const wantsCover = (c) => (c.needsCover || c.source === 'clz') && !c.metronId && !c.coverNotFound;
const canLookUp = (c) => !!c.barcode || !!(c.series && c.number);

// New scans first, then covers for imported comics.
const pendingLookups = () =>
  comics
    .filter((c) => canLookUp(c) && (wantsCover(c) || (c.needsLookup && c.source !== 'clz' && !c.lookupFailed)))
    .sort((a, b) => wantsCover(a) - wantsCover(b));

// Picks the one Metron issue that matches a comic found by title, or none if
// it can't tell (for example the same series name across several volumes).
function pickTitleMatch(c, results) {
  const num = (n) => String(n || '').replace(/^0+(?=\d)/, '').toLowerCase();
  let m = results.filter((r) => num(r.number) === num(c.number));
  if (m.length > 1 && c.coverDate) {
    const sameMonth = m.filter((r) => r.coverDate === c.coverDate.slice(0, 7));
    const sameYear = m.filter((r) => r.coverDate.slice(0, 4) === c.coverDate.slice(0, 4));
    m = sameMonth.length ? sameMonth : sameYear.length ? sameYear : m;
  }
  return m.length === 1 ? m[0] : null;
}

async function lookUpOne(c) {
  if (c.barcode) {
    const results = await lookupBarcode(parseBarcode(c.upc || c.barcode, c.addon));
    if (results.length === 1) return results[0];
    if (results.length || !c.series || !c.number) return null;
  }
  return pickTitleMatch(c, await searchTitle(c.series, c.number));
}

// Looks up comics one at a time, slowly, to stay inside Metron's limit of
// about 20 requests a minute (each lookup can take a few). Progress is saved
// as it goes, so it can be stopped and picked up again later.
async function bulkLookup(onlyIds) {
  const btn = $('#bulk-lookup');
  if (btn.dataset.running === '1') {
    btn.dataset.stop = '1';
    btn.textContent = 'Stopping…';
    return;
  }
  const pending = pendingLookups().filter((c) => !Array.isArray(onlyIds) || onlyIds.includes(c.id));
  btn.dataset.running = '1';
  btn.hidden = false;
  delete btn.dataset.stop;
  let found = 0;
  let done = 0;
  try {
    for (const c of pending) {
      if (btn.dataset.stop) break;
      btn.textContent = `Looking up ${done + 1} of ${pending.length} (tap to stop)`;
      let m;
      for (let attempt = 0; ; attempt++) {
        try {
          m = await lookUpOne(c);
          break;
        } catch (err) {
          if (!/Too many/.test(err.message) || attempt >= 3) throw err;
          btn.textContent = 'Metron asked us to slow down, waiting a minute…';
          await sleep(65000);
        }
      }
      let record;
      if (wantsCover(c)) {
        // From CLZ: keep its details, just add the cover and fill any gaps.
        record = { ...c, needsCover: false, needsLookup: false, coverNotFound: !m };
        if (m) Object.assign(record, { metronId: m.metronId, coverUrl: m.coverUrl, publisher: c.publisher || m.publisher, title: c.title || m.title });
      } else if (m) {
        record = { ...c, series: m.series, volume: m.volume, number: m.number, publisher: m.publisher || c.publisher,
          coverDate: m.coverDate, title: m.title || c.title, metronId: m.metronId, coverUrl: m.coverUrl, needsLookup: false };
      } else {
        // Not found: don't keep retrying it on every run.
        record = { ...c, lookupFailed: true };
      }
      if (record) await saveComic(record);
      if (m) found++;
      done++;
      if (done % 10 === 0) await reload();
      if (done < pending.length) await sleep(6000);
    }
    toast(`Found ${found} of ${done} looked up.${done < pending.length ? ' Tap the button again to carry on.' : ''}`, 5000);
  } catch (err) {
    toast(err.message, 4500);
  } finally {
    delete btn.dataset.running;
    delete btn.dataset.stop;
    btn.textContent = 'Find details and covers';
    await reload();
    renderList();
  }
}

// ---------- settings ----------

function renderSettings() {
  $('#relay-url').value = getRelayUrl();
  renderSync();
  renderBin();
  const last = lsGet('lastBackup');
  const changed = comics.filter((c) => !last || (c.updatedAt || c.addedAt || '') > last).length;
  $('#last-backup').textContent = last
    ? `Last backup: ${new Date(last).toLocaleDateString()}.${changed ? ` ${changed} comic${changed === 1 ? '' : 's'} added or changed since.` : ' Nothing new since.'}`
    : 'No backup yet.';
}

// Settings › Deleted comics: what's in the bin, to put back.
async function renderBin() {
  const items = (await binComics()).sort((a, b) => (b.deletedAt || '').localeCompare(a.deletedAt || ''));
  $('#bin-note').textContent = items.length
    ? `Comics you delete wait here for ${BIN_DAYS} days. Tap Put back to bring one back.`
    : `Nothing here. Comics you delete wait here for ${BIN_DAYS} days in case you want them back.`;
  $('#bin-empty').hidden = !items.length;
  const list = $('#bin-list');
  list.innerHTML = '';
  for (const c of items) {
    const li = document.createElement('li');
    li.innerHTML = `<div class="thumb"></div><div class="item-main"><div class="item-title">${esc(label(c))}</div>
      <div class="item-sub">Deleted ${esc(new Date(c.deletedAt).toLocaleDateString())}</div></div>
      <button type="button" class="btn small-btn">Put back</button>`;
    setThumb(li.querySelector('.thumb'), c.coverUrl);
    li.querySelector('button').onclick = async () => {
      await restoreFromBin([c.id]);
      await reload();
      renderBin();
      toast(`Put back ${label(c)}`);
    };
    list.append(li);
  }
}

async function doExport(kind) {
  if (!comics.length) return toast('Nothing to export yet.');
  const sorted = [...comics].sort(compareComics);
  const result = kind === 'csv' ? await exportCsv(sorted) : await exportBackup(sorted);
  if (result !== 'cancelled') {
    lsSet('lastBackup', new Date().toISOString());
    renderSettings();
    renderBackupNudge();
  }
}

async function restore(file) {
  try {
    const incoming = await readBackup(file);
    if (!confirm(`Restore ${incoming.length} comics from this backup? Comics already on this phone are kept; matching ones are replaced by the backup copy.`)) return;
    const byId = new Map(comics.map((c) => [c.id, c]));
    for (const c of incoming) byId.set(c.id, c);
    await replaceAll([...byId.values()]);
    await reload();
    toast(`Restored ${incoming.length} comics`);
  } catch (err) {
    toast(`Couldn't restore: ${err.message}`, 4500);
  }
}

async function importClz(file) {
  try {
    const incoming = clzToComics(await file.text());
    const have = new Map(comics.map((c) => [c.id, c]));
    const fresh = incoming.filter((c) => !have.has(c.id));
    // Comics imported earlier without a barcode pick it up from a newer export,
    // so their covers can then be looked up.
    const upgrades = incoming
      .filter((c) => c.barcode && have.has(c.id) && !have.get(c.id).barcode)
      .map((c) => {
        const old = have.get(c.id);
        return { ...old, upc: c.upc, addon: c.addon, barcode: c.barcode, title: old.title || c.title, needsCover: !old.metronId };
      });
    if (!fresh.length && !upgrades.length) return toast('Everything in that file is already in your collection.', 4000);
    const skipped = incoming.length - fresh.length - upgrades.length;
    const copies = fresh.reduce((n, c) => n + c.quantity, 0);
    const parts = [];
    if (fresh.length) parts.push(`Import ${fresh.length} comics (${copies} copies) from CLZ?`);
    if (upgrades.length) parts.push(`${fresh.length ? 'Also add' : 'Add'} barcodes to ${upgrades.length} comics imported earlier${fresh.length ? '.' : '?'}`);
    if (skipped) parts.push(`${skipped} already imported will be skipped.`);
    if (!confirm(parts.join('\n\n'))) return;
    const now = new Date().toISOString();
    await addMany([
      ...fresh.map((c) => ({ ...c, addedAt: c.addedAt || now, updatedAt: now })),
      ...upgrades.map((c) => ({ ...c, updatedAt: now })),
    ]);
    await reload();
    showView('collection');
    toast(fresh.length ? `Imported ${fresh.length} comics from CLZ` : `Added barcodes to ${upgrades.length} comics`, 4000);
  } catch (err) {
    toast(`Couldn't import: ${err.message}`, 5000);
  }
}

async function saveRelay() {
  const url = $('#relay-url').value.trim();
  const status = $('#relay-status');
  if (!url) {
    setRelayUrl('');
    status.textContent = 'Lookup turned off.';
    return;
  }
  status.textContent = 'Testing…';
  try {
    await testRelay(url);
    setRelayUrl(url);
    status.textContent = 'Connected to Metron. Scans will now look up comic details.';
  } catch (err) {
    status.textContent = `That didn't work: ${err.message}. Check the address and the relay setup steps.`;
  }
}

// ---------- send scans to the computer ----------
// New comics go to the relay's mailbox, where the Comic Catalog desktop app
// collects them. The sync code (typed into the desktop app) keeps them private.

const syncCode = () => lsGet('syncCode') || '';
let sending = false;
let sendTimer;

function newSyncCode() {
  const abc = 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789';
  return [...crypto.getRandomValues(new Uint8Array(16))].map((b) => abc[b % abc.length]).join('');
}

// Added since sending was turned on (or put in a different box), and finished being looked up (or given up on).
function readyToSend() {
  const since = lsGet('syncSince') || '';
  return comics.filter((c) => c.series && ((!c.sentToComputer && (c.addedAt || '') >= since) || changedFields(c).length) &&
    (c.metronId || c.lookupFailed || !c.needsLookup || !c.barcode));
}

// Comics only the computer has, put in a box while filling one: { series, volume, number, upc, metronId, box }.
const MOVES_KEY = 'boxMoves';
function boxMoves() {
  try { return JSON.parse(lsGet(MOVES_KEY) || '[]'); } catch { return []; }
}
const saveBoxMoves = (moves) => lsSet(MOVES_KEY, JSON.stringify(moves));

function queueSend() {
  clearTimeout(sendTimer);
  if (syncCode() && hasRelay()) sendTimer = setTimeout(() => sendNow(true), 3000);
}

async function sendNow(quiet) {
  const code = syncCode();
  if (!code || sending) return;
  const batch = readyToSend();
  const moves = boxMoves();
  if (!batch.length && !moves.length) {
    if (!quiet) toast('Nothing new to send.');
    return;
  }
  sending = true;
  try {
    await sendToComputer(code, toCsv([...batch, ...moves.map((m) => ({ ...m, boxOnly: 'yes' }))]));
    await markSent(batch.map((c) => c.id));
    // Moves made while this was sending wait for next time.
    saveBoxMoves(boxMoves().filter((m) => !moves.some((x) => x.key === m.key)));
    lsSet('lastSent', new Date().toISOString());
    comics = await allComics();
    const n = batch.length + moves.length;
    if (!quiet) toast(`Sent ${n} comic${n === 1 ? '' : 's'} to your computer`);
  } catch (err) {
    if (!quiet) toast(err.message, 4500);
  } finally {
    sending = false;
    if (!$('#view-settings').hidden) renderSync();
  }
}

function renderSync() {
  const code = syncCode();
  $('#sync-off').hidden = !!code;
  $('#sync-box').hidden = !code;
  if (!code) return;
  $('#sync-code').textContent = code.match(/.{4}/g).join('-');
  const waiting = readyToSend().length + boxMoves().length;
  const last = lsGet('lastSent');
  $('#sync-status').textContent = !hasRelay()
    ? 'Set up comic lookup below first; scans are sent through the same relay.'
    : `${waiting ? `${waiting} comic${waiting === 1 ? '' : 's'} waiting to be sent. ` : 'All caught up. '}` +
      (last ? `Last sent ${new Date(last).toLocaleString()}.` : '');
  $('#library-status').textContent = hasRelay() ? libraryStatus() : '';
}

// ---------- scan many in a row ----------

// The box being filled ("Box 5"), or '' when just scanning. Only while scanning many in a row.
const fillBox = () => ($('#batch-mode').checked ? (lsGet('fillBox') || '').trim() : '');

// Boxes already used (here and on the computer), offered when typing one.
function fillBoxChoices() {
  const names = new Set(comics.map((c) => c.box).filter(Boolean));
  for (const item of library?.barcode.values() || []) if (item.box) names.add(item.box);
  for (const item of library?.key.values() || []) if (item.box) names.add(item.box);
  const sorted = [...names].sort((a, b) => a.localeCompare(b, undefined, { numeric: true, sensitivity: 'base' }));
  $('#boxes').innerHTML = sorted.map((n) => `<option value="${esc(n)}"></option>`).join('');
}

const TRAY_KEY = 'scanTray';
let tray = [];
try { tray = JSON.parse(lsGet(TRAY_KEY) || '[]'); } catch { tray = []; }
const saveTray = () => lsSet(TRAY_KEY, JSON.stringify(tray));

function trayLabel(item) {
  const p = parseBarcode(item.digits);
  return p.issue ? `issue #${p.issue}` : `barcode ${p.upc}`;
}

// Scan many in a row: asks before adding one you already have (or already
// scanned in this pile), pausing the camera while you answer.
async function addToTrayAsking(digits) {
  const p = parseBarcode(digits);
  const again = tray.find((t) => parseBarcode(t.digits).full === p.full);
  // Filling a box, comics you already have are expected: they just go in the box.
  const have = again
    ? `You already scanned <b>${esc(trayLabel(again))}</b> in this pile.`
    : p.addon && !fillBox() ? alreadyHaveText({ parsed: p }) : '';
  if (!have) return addToTray(digits);
  if (scanner) scanner.paused = true;
  const answer = await askAddAnyway(`${have}<br><br>${again ? 'Add it again?' : 'Put it in a box, or add it again?'}`, { offerBox: !again });
  if (scanner) {
    scanner.markSeen?.(digits);
    scanner.paused = false;
  }
  if (answer?.box) {
    toast(`Put ${await putInBox(alreadyHave({ parsed: p }), answer.box)} in ${answer.box}`);
    await reload();
    return null;
  }
  return answer ? addToTray(digits, true) : null;
}

function addToTray(digits, confirmed = false) {
  const p = parseBarcode(digits);
  const owned = p.addon && comics.find((c) => c.barcode === p.full);
  const pcHit = !owned && p.addon ? checkLibrary(library, { barcode: p.full }) : {};
  const onPc = pcHit.owned ? `${pcHit.owned}${pcHit.info?.box ? `, ${pcHit.info.box}` : ''}` : '';
  const again = tray.find((t) => parseBarcode(t.digits).full === p.full);
  const box = fillBox();
  // Comics you already have start unticked, so they aren't counted twice by accident;
  // filling a box, they're ticked to go in it (and not added again).
  const intoBox = box && !again && (owned || pcHit.owned);
  const item = {
    id: crypto.randomUUID(), digits, box, selected: confirmed || intoBox || (!owned && !onPc && !again),
    note: intoBox
      ? `Already have ${owned ? label(owned) : pcHit.owned}: will go in ${box}`
      : owned ? `Already have: ${label(owned)}` : onPc ? `Already have (on computer): ${onPc}` : again ? 'Scanned twice' : '',
  };
  if (intoBox && owned) item.ownedId = owned.id;
  if (intoBox && !owned) {
    const i = pcHit.info;
    item.pc = { series: i.series, volume: i.volume, number: i.issue, upc: p.full, metronId: i.metronId || '' };
  }
  tray.unshift(item);
  saveTray();
  renderTray();
  return item;
}

function renderTray() {
  $('#tray').hidden = !tray.length;
  $('#tray-list').innerHTML = '';
  if (!tray.length) return;
  const chosen = tray.filter((t) => t.selected).length;
  $('#tray-title').textContent = `Scanned (${tray.length})`;
  $('#tray-all').checked = chosen === tray.length;
  $('#tray-save').textContent = `Save selected (${chosen})`;
  $('#tray-save').disabled = !chosen;
  const list = $('#tray-list');
  list.innerHTML = '';
  for (const t of tray) {
    const p = parseBarcode(t.digits);
    const li = document.createElement('li');
    li.innerHTML = `<input type="checkbox" ${t.selected ? 'checked' : ''} aria-label="Select">
      <div class="item-main"><div class="item-title">${esc(p.issue ? `Issue #${p.issue}` : 'Issue number not read')}</div>
      <div class="item-sub">${esc(t.note || `Barcode ${p.upc}${p.addon ? ' ' + p.addon : ''}${t.box ? ` · goes in ${t.box}` : ''}`)}</div></div>
      ${t.note && !t.ownedId && !t.pc ? '<span class="badge todo">Check</span>' : ''}
      <button class="remove" aria-label="Remove">✕</button>`;
    li.querySelector('input').onchange = (e) => {
      t.selected = e.target.checked;
      saveTray();
      renderTray();
    };
    li.querySelector('.remove').onclick = () => {
      tray = tray.filter((x) => x !== t);
      saveTray();
      renderTray();
    };
    list.append(li);
  }
}

async function saveTrayItems() {
  const chosen = tray.filter((t) => t.selected);
  if (!chosen.length) return;
  stopScan();
  const now = new Date().toISOString();
  // Ones you already have just go in the box.
  let boxed = 0;
  for (const t of chosen.filter((x) => x.ownedId)) {
    const c = comics.find((x) => x.id === t.ownedId);
    if (!c) continue;
    await saveComic({ ...c, box: t.box, boxChanged: true });
    boxed++;
  }
  const moves = chosen.filter((t) => t.pc);
  if (moves.length) {
    const kept = boxMoves().filter((m) => !moves.some((t) => t.pc.upc === m.key));
    saveBoxMoves([...kept, ...moves.map((t) => ({ ...t.pc, box: t.box, key: t.pc.upc }))]);
    boxed += moves.length;
  }
  const records = chosen.filter((t) => !t.ownedId && !t.pc).map((t) => {
    const p = parseBarcode(t.digits);
    return {
      id: crypto.randomUUID(), series: '', number: p.issue || '', upc: p.upc, addon: p.addon, barcode: p.full,
      variant: p.cover, printing: p.printing, format: p.isbn ? 'Trade Paperback' : '', quantity: 1, needsLookup: true, addedAt: now, updatedAt: now,
      ...(t.box ? { box: t.box } : {}),
    };
  });
  await addMany(records);
  if (records.some((r) => !kindMatches(r, $('#kind').value))) setKind('all');
  tray = tray.filter((t) => !t.selected);
  saveTray();
  renderTray();
  await reload();
  showView('collection');
  if (!records.length) {
    toast(`Put ${boxed} comic${boxed === 1 ? '' : 's'} in ${chosen[0].box}.${syncCode() ? ' Your computer will get it too.' : ''}`, 3500);
  } else if (hasRelay()) {
    toast(`Saved ${records.length} comics${boxed ? ` and put ${boxed} you have in the box` : ''}. Looking up their details…`, 3500);
    bulkLookup(records.map((r) => r.id));
  } else {
    toast(`Saved ${records.length} comics. Set up comic lookup in Settings to fill in their details.`, 4500);
  }
}

// ---------- what the computer owns and wants ----------

let library = null;
let libraryInfo = null;
try {
  libraryInfo = JSON.parse(lsGet('pcLibrary') || 'null');
  library = libraryInfo ? indexLibrary(libraryInfo.data) : null;
} catch { libraryInfo = null; }

let fetchingLibrary = false;
async function refreshLibrary(force = false) {
  const code = syncCode();
  if (!code || !hasRelay() || fetchingLibrary) return;
  // At most every couple of minutes; it only changes when the computer sends a new list.
  if (!force && libraryInfo && Date.now() - Date.parse(libraryInfo.checkedAt || 0) < 120000) return;
  fetchingLibrary = true;
  try {
    const data = await getLibrary(code);
    libraryInfo = { data, checkedAt: new Date().toISOString() };
    library = data ? indexLibrary(data) : null;
    lsSet('pcLibrary', JSON.stringify(libraryInfo));
    await takeComputerChanges();
    if (!$('#view-wishlist').hidden) renderWishlist();
    if (!$('#view-settings').hidden) renderSync();
  } catch {
    // Offline in a shop: the last copy is still used.
  } finally {
    fetchingLibrary = false;
  }
}

// The fields kept the same on both: box, condition and read.
const SYNCED = ['box', 'condition', 'read'];
const syncValue = (c, f) => (f === 'read' ? !!c[f] : String(c[f] || ''));
const syncedChanges = (before, after) => SYNCED.filter((f) => syncValue(before, f) !== syncValue(after, f));

// Box, condition and read changed on the computer are copied onto the same comics here. A change made
// on this phone wins until the computer has it: unsent, or sent and not yet in the computer's list.
async function takeComputerChanges() {
  if (!library) return;
  const now = new Date().toISOString();
  const updates = [];
  for (const c of comics) {
    if (changedFields(c).length) continue;
    const hit = checkLibrary(library, { barcode: c.addon ? c.barcode : '', metronId: c.metronId, series: c.series, number: c.number });
    // Series and issue number only count for comics with no barcode or Metron match (a different cover could share them).
    if (!hit.owned || (!hit.sure && (c.addon || c.metronId))) continue;
    const pc = { ...hit.info, box: hit.info.box || '' };
    const fields = SYNCED.filter((f) => pc[f] !== undefined);
    let next = { ...c };
    if (c.pendingSync) {
      const stale = Date.now() - Date.parse(c.pendingSync.at) > 14 * 864e5;
      const arrived = c.pendingSync.fields.every((f) => !fields.includes(f) || syncValue(pc, f) === syncValue(c, f));
      if (!arrived && !stale) continue;
      delete next.pendingSync;
    }
    const diff = fields.filter((f) => syncValue(pc, f) !== syncValue(c, f));
    for (const f of diff) next[f] = f === 'read' ? !!pc[f] : pc[f];
    if (diff.length || next.pendingSync !== c.pendingSync) updates.push(diff.length ? { ...next, updatedAt: now } : next);
  }
  if (!updates.length) return;
  await addMany(updates);
  comics = await allComics();
  if (!$('#view-collection').hidden) renderList();
}

function libraryStatus() {
  if (!syncCode()) return '';
  if (!library) return "Nothing from your computer yet. Save this code in the Comic Catalog app's Settings and leave the app open for a couple of minutes.";
  return `Your computer's list: ${library.owned} comics, ${library.wishlist.length} on the wishlist (checked ${new Date(libraryInfo.checkedAt).toLocaleString()}).`;
}

function renderWishlist() {
  const note = $('#wish-note');
  const holder = $('#wish-list');
  holder.innerHTML = '';
  $('#wish-search').hidden = !library?.wishlist.length;
  if (!syncCode()) {
    note.textContent = 'Your wishlist lives in the Comic Catalog app on your computer. To see it here, open Settings below, turn on "Send scans to your computer", and type the code into the computer app.';
    return;
  }
  if (!library) {
    note.textContent = libraryStatus();
    return;
  }
  if (!library.wishlist.length) {
    note.textContent = 'Your wishlist on the computer is empty.';
    return;
  }
  const q = $('#wish-search').value.trim().toLowerCase();
  const items = library.wishlist.filter((w) => !q || `${w.label} ${w.arc}`.toLowerCase().includes(q));
  note.textContent = `${library.wishlist.length} comic${library.wishlist.length === 1 ? '' : 's'} on your wishlist, from your computer.`;
  const groups = new Map();
  for (const w of items) {
    const g = w.arc || 'Other comics';
    if (!groups.has(g)) groups.set(g, []);
    groups.get(g).push(w);
  }
  const names = [...groups.keys()].sort((a, b) => (a === 'Other comics') - (b === 'Other comics') || a.localeCompare(b));
  const frag = document.createDocumentFragment();
  for (const g of names) {
    frag.append(Object.assign(document.createElement('h3'), { className: 'wish-arc', textContent: g }));
    for (const w of groups.get(g)) frag.append(Object.assign(document.createElement('div'), { className: 'wish-item', textContent: w.label }));
  }
  holder.append(frag);
}

// ---------- voice ----------

let listening = null;
async function speak() {
  if (listening) {
    listening.stop();
    return;
  }
  if (!speechSupported()) {
    toast('Voice isn\'t available here. Tap the series box and use the microphone on your keyboard instead.', 5000);
    return;
  }
  stopScan();
  if (!$('#sheet').open) openAddSheet();
  $('#title-search').open = true;
  const mic = $('#ts-mic');
  mic.classList.add('listening');
  $('#lookup-status').textContent = 'Listening… say the series, issue number and condition.';
  try {
    const heard = await listen((rec) => (listening = rec));
    if (!heard) {
      $('#lookup-status').textContent = 'Didn\'t catch that. Tap 🎤 to try again.';
      return;
    }
    const said = parseSpoken(heard);
    $('#ts-series').value = said.series;
    $('#ts-number').value = said.number;
    form.elements.series.value = said.series;
    form.elements.number.value = said.number;
    if (said.volume) form.elements.volume.value = said.volume;
    if (said.condition) form.elements.condition.value = said.condition;
    renderDuplicate(findDuplicate(sheet.parsed));
    renderPcNote();
    toast(`Heard "${heard}"`, 3000);
    if (said.series) await runTitleSearch();
  } catch (err) {
    $('#lookup-status').textContent = err.message === 'not-allowed'
      ? 'Microphone permission was blocked. Allow it for this app in your phone settings.'
      : `Voice didn't work (${err.message}). You can type instead.`;
  } finally {
    listening = null;
    mic.classList.remove('listening');
  }
}

// ---------- wiring ----------

document.querySelectorAll('.tabs button').forEach((b) => (b.onclick = () => showView(b.dataset.view)));
$('#start-scan').onclick = startScan;
$('#stop-scan').onclick = stopScan;
$('#torch').onclick = async () => {
  const on = !$('#torch').classList.contains('primary');
  try {
    await scanner.setTorch(on);
    $('#torch').classList.toggle('primary', on);
  } catch {}
};
$('#photo-input').onchange = async (e) => {
  const file = e.target.files[0];
  e.target.value = '';
  if (!file) return;
  stopScan();
  toast('Reading barcode…');
  try {
    const text = await readImageFile(file);
    if (!text) return toast('No barcode found in that photo. Try again closer, with good light.', 4000);
    openScanned(parseBarcode(text));
  } catch (err) {
    toast(`Couldn't read the photo: ${err.message}`, 4000);
  }
};
$('#type-barcode').onclick = () => {
  stopScan();
  $('#type-form').reset();
  $('#type-dialog').showModal();
};
$('#type-form').onsubmit = (e) => {
  e.preventDefault();
  const parsed = parseBarcode($('#type-upc').value, $('#type-addon').value);
  if (!isValidBase(parsed.upc)) return toast('The main barcode should be 12 digits.');
  $('#type-dialog').close();
  openScanned(parsed);
};
$('#speak').onclick = () => speak();
$('#ts-mic').onclick = () => speak();
$('#wish-search').oninput = renderWishlist;
$('#no-barcode').onclick = () => {
  stopScan();
  openAddSheet();
};

document.querySelectorAll('[data-close]').forEach((b) => (b.onclick = () => b.closest('dialog').close()));
form.onsubmit = saveSheet;
$('#ts-go').onclick = runTitleSearch;
for (const id of ['#ts-series', '#ts-number']) {
  $(id).onkeydown = (e) => {
    if (e.key === 'Enter') {
      e.preventDefault();
      runTitleSearch();
    }
  };
}
$('#addon-apply').onclick = () => {
  const addon = $('#addon-input').value.replace(/\D/g, '');
  if (addon.length !== 5) return toast('The small code has 5 digits.');
  sheet.parsed = parseBarcode(sheet.parsed.upc, addon);
  renderBarcodeBox(sheet.parsed);
  if (!form.elements.number.value) form.elements.number.value = sheet.parsed.issue;
  if (!sheet.editing) {
    renderDuplicate(findDuplicate(sheet.parsed));
    renderPcNote();
    runBarcodeLookup();
  }
};
$('#more-issues-btn').onclick = () => {
  if (sheet.editing) openMoreIssues(sheet.editing);
};
$('#delete-btn').onclick = async () => {
  const c = sheet.editing;
  if (!c || !confirm(`Delete ${label(c)}? It waits in Settings › Deleted comics for ${BIN_DAYS} days in case you want it back.`)) return;
  await deleteMany([c.id]);
  $('#sheet').close();
  await reload();
  toast(`Deleted ${label(c)}`, 0, () => restoreFromBin([c.id]));
};

$('#search').oninput = renderList;
$('#sort').onchange = renderList;
function setKind(kind) {
  $('#kind').value = kind;
  lsSet('collectionKind', kind);
}
setKind(['issues', 'trades', 'all', 'missing'].includes(lsGet('collectionKind')) ? lsGet('collectionKind') : 'issues');
$('#kind').onchange = () => {
  setKind($('#kind').value);
  renderList();
};
$('#needs-filter').onchange = renderList;
$('#select-mode').onclick = () => setSelecting(!selecting);
$('#select-all').onchange = (e) => {
  for (const id of shownIds) e.target.checked ? picked.add(id) : picked.delete(id);
  renderList();
};
$('#select-delete').onclick = deletePicked;
$('#select-box').onclick = boxPicked;
$('#bulk-lookup').onclick = () => bulkLookup();
$('#series-lengths').onclick = findSeriesLengths;
$('#bin-empty').onclick = async () => {
  if (!confirm('Delete the comics in the bin for good? This can\'t be undone.')) return;
  await emptyBin();
  renderBin();
};
$('#export-csv').onclick = () => doExport('csv');
$('#export-json').onclick = () => doExport('json');
$('#restore-input').onchange = (e) => {
  const file = e.target.files[0];
  e.target.value = '';
  if (file) restore(file);
};
$('#save-relay').onclick = saveRelay;
$('#sync-start').onclick = () => {
  lsSet('syncCode', newSyncCode());
  // Only comics added from now on; the ones already here can go across as a CSV export.
  lsSet('syncSince', new Date().toISOString());
  library = libraryInfo = null;
  renderSync();
};
$('#sync-now').onclick = () => sendNow(false);
$('#sync-stop').onclick = () => {
  if (!confirm('Stop sending new scans to your computer? You can turn it on again later (with a new code).')) return;
  try { localStorage.removeItem('syncCode'); localStorage.removeItem('pcLibrary'); } catch {}
  library = libraryInfo = null;
  renderSync();
};
$('#batch-mode').checked = lsGet('batchMode') === '1';
$('#fill-box-row').hidden = !$('#batch-mode').checked;
$('#batch-mode').onchange = (e) => {
  lsSet('batchMode', e.target.checked ? '1' : '0');
  $('#fill-box-row').hidden = !e.target.checked;
  stopScan();
};
$('#fill-box').value = lsGet('fillBox') || '';
$('#fill-box').onfocus = fillBoxChoices;
$('#fill-box').oninput = (e) => lsSet('fillBox', e.target.value.trim());
$('#tray-all').onchange = (e) => {
  tray.forEach((t) => (t.selected = e.target.checked));
  saveTray();
  renderTray();
};
$('#tray-clear').onclick = () => {
  if (!confirm('Clear the scanned list without saving?')) return;
  tray = [];
  saveTray();
  renderTray();
};
$('#tray-save').onclick = saveTrayItems;
renderTray();
$('#clz-input').onchange = (e) => {
  const file = e.target.files[0];
  e.target.value = '';
  if (file) importClz(file);
};
// Re-check for comics you might already own as series and issue are filled in.
for (const name of ['series', 'number']) {
  form.elements[name].addEventListener('change', () => {
    if (!sheet.editing) renderDuplicate(findDuplicate(sheet.parsed, sheet.match?.metronId));
    renderPcNote();
  });
}

if ('serviceWorker' in navigator) {
  // When a new version of the app is published, switch to it straight away.
  const hadController = !!navigator.serviceWorker.controller;
  navigator.serviceWorker.addEventListener('controllerchange', () => {
    if (hadController && !reloading) {
      reloading = true;
      location.reload();
    }
  });
  navigator.serviceWorker.register('sw.js').then((reg) => {
    document.addEventListener('visibilitychange', () => {
      if (document.visibilityState === 'visible') reg.update().catch(() => {});
    });
  }).catch(() => {});
}
let reloading = false;

document.addEventListener('visibilitychange', () => {
  if (document.visibilityState === 'visible') refreshLibrary();
});

// Text size (Settings): everything in the app is made bigger together.
function applyTextSize(size) {
  const n = ['1', '1.15', '1.3'].includes(size) ? size : '1';
  document.documentElement.style.zoom = n === '1' ? '' : n;
  $('#text-size').value = n;
}
applyTextSize(lsGet('textSize'));
$('#text-size').onchange = (e) => {
  lsSet('textSize', e.target.value);
  applyTextSize(e.target.value);
};

requestPersistence();
// Comics in the bin longer than 30 days go for good.
emptyBin(BIN_DAYS).catch(() => {});
reload();
refreshLibrary();
