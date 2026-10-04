import { parseBarcode, isValidBase, ordinal } from './barcode.js';
import { allComics, saveComic, deleteComic, deleteMany, replaceAll, addMany, requestPersistence } from './db.js';
import { Scanner, readImageFile } from './scanner.js';
import { hasRelay, getRelayUrl, setRelayUrl, testRelay, lookupBarcode, searchTitle, issueDetails } from './lookup.js';
import { exportCsv, exportBackup, readBackup } from './backup.js';
import { clzToComics, looseKey } from './clz.js';

const $ = (sel) => document.querySelector(sel);
const esc = (s) => String(s ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]);
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

let comics = [];
let scanner;
let sheet = { parsed: null, editing: null, match: null };

// ---------- helpers ----------

function toast(msg, ms = 2600) {
  const el = $('#toast');
  el.textContent = msg;
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
  for (const v of ['scan', 'collection', 'settings']) $(`#view-${v}`).hidden = v !== name;
  document.querySelectorAll('.tabs button').forEach((b) => b.classList.toggle('active', b.dataset.view === name));
  if (name !== 'scan') stopScan();
  if (name === 'collection') renderList();
  if (name === 'settings') renderSettings();
}

async function reload() {
  comics = await allComics();
  const copies = comics.reduce((n, c) => n + (Number(c.quantity) || 1), 0);
  $('#count').textContent = comics.length ? `${copies} comic${copies === 1 ? '' : 's'}` : '';
  if (!$('#view-collection').hidden) renderList();
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
          const item = addToTray(digits);
          status.className = 'scan-status good';
          status.textContent = `Added ${trayLabel(item)}${gotAddon ? '' : " (couldn't read the small 5-digit code)"}. Next one!`;
          return;
        }
        resetScanUi();
        if (!gotAddon) toast("Couldn't read the small 5-digit code. You can type it in.", 3500);
        openAddSheet({ parsed: parseBarcode(digits) });
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

// ---------- add / edit sheet ----------

const form = $('#comic-form');

function fillForm(c) {
  for (const name of ['series', 'number', 'volume', 'publisher', 'coverDate', 'variantName', 'title', 'condition', 'notes']) {
    form.elements[name].value = c[name] ?? '';
  }
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

function wireAddCopy(dupe) {
  const n = Number(dupe.quantity) || 1;
  $('#add-copy').onclick = async () => {
    await saveComic({ ...dupe, quantity: n + 1 });
    $('#sheet').close();
    await reload();
    toast(`Now ${n + 1} copies of ${label(dupe)}`);
  };
}

function openAddSheet({ parsed = null, editing = null } = {}) {
  sheet = { parsed, editing, match: null };
  form.reset();
  $('#sheet-title').textContent = editing ? 'Edit comic' : 'Add comic';
  $('#delete-btn').hidden = !editing;
  $('#matches').innerHTML = '';
  $('#lookup-status').textContent = '';
  $('#title-search').open = false;
  $('#addon-input').value = '';

  if (editing) {
    fillForm(editing);
    sheet.parsed = editing.upc ? parseBarcode(editing.upc, editing.addon) : null;
  } else {
    fillForm({ number: parsed?.issue || '' });
  }
  renderBarcodeBox(sheet.parsed);
  renderDuplicate(editing ? undefined : findDuplicate(parsed));
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
  const record = {
    ...(editing || {}),
    series: f.series.value.trim(),
    number: f.number.value.trim(),
    volume: f.volume.value.trim(),
    publisher: f.publisher.value.trim(),
    coverDate: f.coverDate.value.trim(),
    title: f.title.value.trim(),
    variantName: f.variantName.value.trim(),
    condition: f.condition.value,
    notes: f.notes.value.trim(),
    quantity: Math.max(1, parseInt(f.quantity.value, 10) || 1),
  };
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
  await reload();
  toast(editing ? 'Saved' : `Added ${label(record)}`);
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
  $('#select-all').checked = shownIds.length > 0 && shownIds.every((id) => picked.has(id));
}

async function deletePicked() {
  const chosen = comics.filter((c) => picked.has(c.id));
  if (!chosen.length) return;
  const names = chosen.slice(0, 5).map(label).join('\n');
  const more = chosen.length > 5 ? `\n…and ${chosen.length - 5} more` : '';
  if (!confirm(`Delete ${chosen.length} comic${chosen.length === 1 ? '' : 's'}? This can't be undone.\n\n${names}${more}`)) return;
  await deleteMany(chosen.map((c) => c.id));
  picked.clear();
  await reload();
  toast(`Deleted ${chosen.length} comic${chosen.length === 1 ? '' : 's'}`);
}

function renderList() {
  const q = $('#search').value.trim().toLowerCase();
  const needsOnly = $('#needs-filter').checked;
  let items = comics.filter((c) => (!needsOnly || c.needsLookup) &&
    (!q || [c.series, c.volume, c.title, c.publisher, c.number, c.variantName, c.notes, c.barcode].some((v) => String(v || '').toLowerCase().includes(q))));
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
    const sub = [c.variantName || c.format, c.publisher, c.coverDate, c.condition].filter(Boolean).join(' · ') || (c.barcode ? `Barcode ${c.barcode}` : '');
    li.innerHTML = `<div class="thumb"></div><div class="item-main">
      <div class="item-title">${esc(label(c))}</div><div class="item-sub">${esc(sub)}</div></div>
      ${c.needsLookup ? '<span class="badge todo">Needs details</span>' : ''}
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
  $('#empty').hidden = comics.length > 0;
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

function renderBackupNudge() {
  const last = lsGet('lastBackup');
  const days = last ? (Date.now() - Date.parse(last)) / 864e5 : Infinity;
  const nudge = $('#backup-nudge');
  nudge.hidden = !(comics.length >= 10 && days > 30);
  if (nudge.hidden) return;
  nudge.innerHTML = `${last ? `Your last backup was ${Math.floor(days)} days ago.` : "You haven't backed up your collection yet."}
    <div><button class="btn" id="nudge-export">Export to OneDrive</button></div>`;
  $('#nudge-export').onclick = () => doExport('csv');
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
  const last = lsGet('lastBackup');
  $('#last-backup').textContent = last ? `Last export: ${new Date(last).toLocaleDateString()}` : 'No backup exported yet.';
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

// ---------- scan many in a row ----------

const TRAY_KEY = 'scanTray';
let tray = [];
try { tray = JSON.parse(lsGet(TRAY_KEY) || '[]'); } catch { tray = []; }
const saveTray = () => lsSet(TRAY_KEY, JSON.stringify(tray));

function trayLabel(item) {
  const p = parseBarcode(item.digits);
  return p.issue ? `issue #${p.issue}` : `barcode ${p.upc}`;
}

function addToTray(digits) {
  const p = parseBarcode(digits);
  const owned = p.addon && comics.find((c) => c.barcode === p.full);
  const again = tray.find((t) => parseBarcode(t.digits).full === p.full);
  // Comics you already have start unticked, so they aren't counted twice by accident.
  const item = { id: crypto.randomUUID(), digits, selected: !owned && !again, note: owned ? `Already have: ${label(owned)}` : again ? 'Scanned twice' : '' };
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
      <div class="item-sub">${esc(t.note || `Barcode ${p.upc}${p.addon ? ' ' + p.addon : ''}`)}</div></div>
      ${t.note ? '<span class="badge todo">Check</span>' : ''}
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
  const records = chosen.map((t) => {
    const p = parseBarcode(t.digits);
    return {
      id: crypto.randomUUID(), series: '', number: p.issue || '', upc: p.upc, addon: p.addon, barcode: p.full,
      variant: p.cover, printing: p.printing, quantity: 1, needsLookup: true, addedAt: now, updatedAt: now,
    };
  });
  await addMany(records);
  tray = tray.filter((t) => !t.selected);
  saveTray();
  renderTray();
  await reload();
  showView('collection');
  if (hasRelay()) {
    toast(`Saved ${records.length} comics. Looking up their details…`, 3500);
    bulkLookup(records.map((r) => r.id));
  } else {
    toast(`Saved ${records.length} comics. Set up comic lookup in Settings to fill in their details.`, 4500);
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
    openAddSheet({ parsed: parseBarcode(text) });
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
  openAddSheet({ parsed });
};
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
    runBarcodeLookup();
  }
};
$('#delete-btn').onclick = async () => {
  const c = sheet.editing;
  if (!c || !confirm(`Delete ${label(c)} from your collection?`)) return;
  await deleteComic(c.id);
  $('#sheet').close();
  await reload();
  toast('Deleted');
};

$('#search').oninput = renderList;
$('#sort').onchange = renderList;
$('#needs-filter').onchange = renderList;
$('#select-mode').onclick = () => setSelecting(!selecting);
$('#select-all').onchange = (e) => {
  for (const id of shownIds) e.target.checked ? picked.add(id) : picked.delete(id);
  renderList();
};
$('#select-delete').onclick = deletePicked;
$('#bulk-lookup').onclick = () => bulkLookup();
$('#export-csv').onclick = () => doExport('csv');
$('#export-json').onclick = () => doExport('json');
$('#restore-input').onchange = (e) => {
  const file = e.target.files[0];
  e.target.value = '';
  if (file) restore(file);
};
$('#save-relay').onclick = saveRelay;
$('#batch-mode').checked = lsGet('batchMode') === '1';
$('#batch-mode').onchange = (e) => {
  lsSet('batchMode', e.target.checked ? '1' : '0');
  stopScan();
};
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

requestPersistence();
reload();
