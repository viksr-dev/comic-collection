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
  // Newer desktop apps also send which box it's in, its key issue note and its eBay estimate (US$),
  // then its condition and whether it's read (left undefined by older ones).
  for (const [series = '', volume = '', issue = '', barcode = '', metronId = null, box = '', keyNote = '', ebay = null, condition, read] of data.owned || []) {
    const item = { label: name(series, volume, issue), box, keyNote, ebay: Number(ebay) > 0 ? Number(ebay) : null, series, volume, issue, barcode, metronId,
      condition, read: read === undefined ? undefined : !!read };
    index.owned++;
    if (digits(barcode).length >= 12) index.barcode.set(digits(barcode), item);
    if (metronId) index.metron.set(Number(metronId), item);
    const key = looseKey(series, issue);
    if (key && !index.key.has(key)) index.key.set(key, item);
  }
  for (const [series = '', issue = '', arc = ''] of data.wishlist || []) {
    const item = { series, issue, arc, label: name(series, '', issue) };
    index.wishlist.push(item);
    const key = looseKey(series, issue);
    if (key && !index.wish.has(key)) index.wish.set(key, item);
  }
  return index;
}

// { owned: 'Batman #404', info: { box, keyNote, ebay } } or { wish: { label, arc } } or {}.
// A full barcode (with the 5-digit code) or Metron issue is a sure match;
// series and issue number is a "probably".
export function checkLibrary(index, { barcode, metronId, series, number } = {}) {
  if (!index) return {};
  const code = digits(barcode);
  const owned = (item, sure) => ({ owned: item.label, sure, info: item });
  if (code.length > 12 && index.barcode.has(code)) return owned(index.barcode.get(code), true);
  if (metronId && index.metron.has(Number(metronId))) return owned(index.metron.get(Number(metronId)), true);
  const key = looseKey(series, number);
  if (key && index.key.has(key)) return owned(index.key.get(key), false);
  if (key && index.wish.has(key)) return { wish: index.wish.get(key) };
  return {};
}

// "In Box 3. Key issue: 1st Harley Quinn. eBay asking about US$12." from what the computer sent, or ''.
export function ownedDetails(info) {
  if (!info) return '';
  const parts = [];
  if (info.box) parts.push(`In ${info.box}.`);
  if (info.keyNote) parts.push(`Key issue: ${info.keyNote}.`);
  if (info.ebay) parts.push(`eBay asking about US$${info.ebay < 100 ? info.ebay.toFixed(2) : Math.round(info.ebay)}.`);
  return parts.join(' ');
}
