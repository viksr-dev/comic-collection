// Comic barcodes are a 12-digit UPC-A (or 13-digit EAN) followed by a small
// add-on. The 5-digit add-on encodes the issue number (3 digits), the cover
// variant (1 digit) and the printing (1 digit), e.g. 00111 = issue 1, cover 1,
// 1st printing.

export function parseBarcode(raw, typedAddon = '') {
  let digits = String(raw || '').replace(/\D/g, '');
  let addon = String(typedAddon || '').replace(/\D/g, '');

  // A 13-digit EAN starting with 0 is a UPC-A with a leading zero.
  const lengths = [[13, 5], [13, 2], [12, 5], [12, 2], [13, 0], [12, 0]];
  let base = digits;
  for (const [b, a] of lengths) {
    if (digits.length === b + a) {
      base = digits.slice(0, b);
      if (a) addon = digits.slice(b);
      break;
    }
  }
  if (base.length === 13 && base.startsWith('0')) base = base.slice(1);

  const result = { upc: base, addon: addon || null, full: base + addon };
  // Books (trade paperbacks, hardcovers) carry an ISBN, and their add-on is a price, not an issue number.
  result.isbn = base.length === 13 && /^97[89]/.test(base);
  if (addon.length === 5 && !result.isbn) {
    result.issue = String(parseInt(addon.slice(0, 3), 10));
    result.cover = addon[3];
    result.printing = addon[4];
  }
  return result;
}

export function isValidBase(upc) {
  return /^\d{12,13}$/.test(upc);
}

export function ordinal(n) {
  const v = Number(n);
  const s = ['th', 'st', 'nd', 'rd'];
  const m = v % 100;
  return v + (s[(m - 20) % 10] || s[m] || s[0]);
}

// Which printing or cover a comic barcode is, for Cover / variant, e.g. "2nd printing" (blank for a first-printing main cover).
// The relay names a copy found by another barcode the same way.
export function copyName(parsed) {
  if (!parsed?.addon || parsed.addon.length !== 5 || parsed.isbn) return '';
  const parts = [];
  if (Number(parsed.cover) > 1) parts.push('Variant cover');
  if (Number(parsed.printing) > 1) parts.push(`${ordinal(parsed.printing)} printing`);
  return parts.join(', ');
}
