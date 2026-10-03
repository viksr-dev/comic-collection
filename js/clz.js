// Imports a collection exported from CLZ Comics as CSV.
// CLZ columns used: Series, Issue, Variant Description, Publisher,
// Release Date, Format, Added Date (Quantity, Grade, Notes and Barcode too,
// when they were ticked in the export).

// Small CSV parser that handles quoted fields, doubled quotes and newlines.
export function parseCsv(text) {
  const rows = [];
  let row = [];
  let field = '';
  let quoted = false;
  text = text.replace(/^﻿/, '');
  for (let i = 0; i < text.length; i++) {
    const ch = text[i];
    if (quoted) {
      if (ch === '"') {
        if (text[i + 1] === '"') {
          field += '"';
          i++;
        } else quoted = false;
      } else field += ch;
    } else if (ch === '"') quoted = true;
    else if (ch === ',') {
      row.push(field);
      field = '';
    } else if (ch === '\n' || ch === '\r') {
      if (ch === '\r' && text[i + 1] === '\n') i++;
      row.push(field);
      rows.push(row);
      row = [];
      field = '';
    } else field += ch;
  }
  if (field || row.length) {
    row.push(field);
    rows.push(row);
  }
  return rows.filter((r) => r.some((v) => v.trim()));
}

const MONTHS = { jan: 1, feb: 2, mar: 3, apr: 4, may: 5, jun: 6, jul: 7, aug: 8, sep: 9, oct: 10, nov: 11, dec: 12 };

// "Jun 10, 2026" -> { y: 2026, m: 6, d: 10 }; older comics are "Apr 2003" or "2003".
function parseClzDate(s) {
  const t = String(s || '').trim();
  const m = t.match(/^([A-Za-z]{3})[a-z]*\.?(?: (\d{1,2}),)? (\d{4})$/);
  if (m && MONTHS[m[1].toLowerCase()]) return { y: +m[3], m: MONTHS[m[1].toLowerCase()], d: +(m[2] || 1) };
  if (/^\d{4}$/.test(t)) return { y: +t, m: 1, d: 1, yearOnly: true };
  const iso = String(s || '').match(/^(\d{4})-(\d{2})(?:-(\d{2}))?/);
  if (iso) return { y: +iso[1], m: +iso[2], d: +(iso[3] || 1) };
  return null;
}
const pad = (n) => String(n).padStart(2, '0');

// "The Amazing Spider-Man, Vol. 7" -> ["The Amazing Spider-Man", "Vol. 7"]
export function splitSeries(name) {
  const m = String(name || '').trim().match(/^(.*?),?\s+Vol\.?\s*(\d+)$/i);
  return m ? [m[1].trim(), `Vol. ${m[2]}`] : [String(name || '').trim(), ''];
}

// "47A" -> ["47", "A"]; "1.50B" -> ["1.50", "B"]; "TP" and "½" stay as they are.
export function splitIssue(issue) {
  const s = String(issue || '').trim();
  const m = s.match(/^(\d+(?:\.\d+)?)([A-Z]{1,2})$/);
  return m ? [m[1], m[2]] : [s, ''];
}

function pick(record, ...names) {
  for (const n of names) if (record[n] != null && String(record[n]).trim()) return String(record[n]).trim();
  return '';
}

export function clzToComics(text) {
  const [header, ...rows] = parseCsv(text);
  const cols = (header || []).map((h) => h.trim().toLowerCase());
  if (!cols.includes('series') || !cols.includes('issue')) {
    throw new Error("This doesn't look like a CLZ Comics export (no Series and Issue columns).");
  }

  const byKey = new Map();
  for (const values of rows) {
    const r = Object.fromEntries(cols.map((c, i) => [c, values[i] ?? '']));
    const clzSeries = pick(r, 'series');
    if (!clzSeries) continue;
    const clzIssue = pick(r, 'issue', 'issue nr', 'issue number');
    const variantName = pick(r, 'variant description', 'variant');
    const key = [clzSeries, clzIssue, variantName].join('|').toLowerCase();
    const qty = parseInt(pick(r, 'quantity', 'qty'), 10) || 1;

    const existing = byKey.get(key);
    if (existing) {
      existing.quantity += qty;
      continue;
    }

    const [series, volume] = splitSeries(clzSeries);
    const [number, variant] = splitIssue(clzIssue);
    const release = parseClzDate(pick(r, 'release date', 'cover date', 'publication date'));
    const added = parseClzDate(pick(r, 'added date', 'date added'));
    const format = pick(r, 'format');
    const barcode = pick(r, 'barcode', 'upc').replace(/\D/g, '');

    const comic = {
      // A stable id means importing the same file twice doesn't double up.
      id: `clz:${key}`,
      source: 'clz',
      series,
      volume,
      number,
      variant,
      variantName,
      publisher: pick(r, 'publisher'),
      coverDate: release ? (release.yearOnly ? String(release.y) : `${release.y}-${pad(release.m)}`) : '',
      format: format && format.toLowerCase() !== 'comic' ? format : '',
      condition: pick(r, 'grade', 'condition'),
      notes: pick(r, 'notes'),
      quantity: qty,
      needsLookup: false,
      addedAt: added ? `${added.y}-${pad(added.m)}-${pad(added.d)}T00:00:00.000Z` : undefined,
    };
    if (barcode.length >= 12) {
      const upc = barcode.length > 13 ? barcode.slice(0, barcode.length - 5) : barcode;
      Object.assign(comic, { upc, addon: barcode.slice(upc.length) || null, barcode });
    }
    byKey.set(key, comic);
  }
  return [...byKey.values()];
}

// Loose "same comic" key for spotting a scanned comic that came in from CLZ
// under a slightly different series name.
export function looseKey(series, number) {
  const s = splitSeries(series)[0].toLowerCase().replace(/^the\s+/, '').replace(/[^a-z0-9]/g, '');
  const n = splitIssue(number)[0].replace(/^0+(?=\d)/, '');
  return s && n ? `${s}#${n}` : '';
}
