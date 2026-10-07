// What the Comic Catalog app on the computer owns and wants. The desktop app
// leaves this list at the relay (when a sync code is saved there), so scanning
// in a shop can say "you already own this" or "this is on your wishlist".
import { looseKey } from './clz.js';

const digits = (s) => String(s || '').replace(/\D/g, '');

function name(series, volume, issue) {
  return `${series}${volume ? ` (${volume})` : ''}${issue ? ` #${issue}` : ''}`;
}

// Turns the list from the relay into quick lookups by barcode, Metron issue and
// series + issue number.
export function indexLibrary(data) {
  const index = { barcode: new Map(), metron: new Map(), key: new Map(), wish: new Map(), wishlist: [], owned: 0 };
  if (!data) return index;
  for (const [series = '', volume = '', issue = '', barcode = '', metronId = null] of data.owned || []) {
    const label = name(series, volume, issue);
    index.owned++;
    if (digits(barcode).length >= 12) index.barcode.set(digits(barcode), label);
    if (metronId) index.metron.set(Number(metronId), label);
    const key = looseKey(series, issue);
    if (key && !index.key.has(key)) index.key.set(key, label);
  }
  for (const [series = '', issue = '', arc = ''] of data.wishlist || []) {
    const item = { series, issue, arc, label: name(series, '', issue) };
    index.wishlist.push(item);
    const key = looseKey(series, issue);
    if (key && !index.wish.has(key)) index.wish.set(key, item);
  }
  return index;
}

// { owned: 'Batman #404' } or { wish: { label, arc } } or {}.
// A full barcode (with the 5-digit code) or Metron issue is a sure match;
// series and issue number is a "probably".
export function checkLibrary(index, { barcode, metronId, series, number } = {}) {
  if (!index) return {};
  const code = digits(barcode);
  if (code.length > 12 && index.barcode.has(code)) return { owned: index.barcode.get(code), sure: true };
  if (metronId && index.metron.has(Number(metronId))) return { owned: index.metron.get(Number(metronId)), sure: true };
  const key = looseKey(series, number);
  if (key && index.key.has(key)) return { owned: index.key.get(key), sure: false };
  if (key && index.wish.has(key)) return { wish: index.wish.get(key) };
  return {};
}
