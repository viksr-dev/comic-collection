// Exports go through the phone's Share menu (so they can be saved straight to
// OneDrive), falling back to a normal download.

export const CSV_COLUMNS = [
  ['series', 'Series'],
  ['volume', 'Volume'],
  ['number', 'Issue'],
  ['variantName', 'Cover / variant'],
  ['format', 'Format'],
  ['title', 'Story title'],
  ['publisher', 'Publisher'],
  ['coverDate', 'Cover date'],
  ['variant', 'Variant code'],
  ['printing', 'Printing'],
  ['quantity', 'Copies'],
  ['condition', 'Condition'],
  ['notes', 'Notes'],
  ['barcode', 'Barcode'],
  ['metronId', 'Metron ID'],
  ['addedAt', 'Date added'],
];

function csvCell(value) {
  const s = value == null ? '' : String(value);
  // Stop a spreadsheet treating text as a formula.
  const guarded = /^[=+\-@]/.test(s) ? `'${s}` : s;
  return /[",\n\r]/.test(guarded) ? `"${guarded.replace(/"/g, '""')}"` : guarded;
}

export function toCsv(comics) {
  const lines = [CSV_COLUMNS.map(([, label]) => label).join(',')];
  for (const c of comics) {
    // UPC and add-on separated by a space, so Excel keeps it as text instead
    // of turning it into scientific notation.
    const row = { ...c, barcode: [c.upc, c.addon].filter(Boolean).join(' '), addedAt: (c.addedAt || '').slice(0, 10) };
    lines.push(CSV_COLUMNS.map(([key]) => csvCell(row[key])).join(','));
  }
  // The byte-order mark makes Excel open the file as UTF-8.
  return '\ufeff' + lines.join('\r\n');
}

const stamp = () => new Date().toISOString().slice(0, 10);

export async function shareOrDownload(content, filename, type) {
  const file = new File([content], filename, { type });
  if (navigator.canShare?.({ files: [file] })) {
    try {
      await navigator.share({ files: [file], title: filename });
      return 'shared';
    } catch (err) {
      if (err.name === 'AbortError') return 'cancelled';
    }
  }
  const url = URL.createObjectURL(file);
  const a = Object.assign(document.createElement('a'), { href: url, download: filename });
  document.body.append(a);
  a.click();
  a.remove();
  setTimeout(() => URL.revokeObjectURL(url), 10000);
  return 'downloaded';
}

export function exportCsv(comics) {
  return shareOrDownload(toCsv(comics), `comics-${stamp()}.csv`, 'text/csv');
}

export function exportBackup(comics) {
  const payload = JSON.stringify({ app: 'comic-collection', version: 1, exportedAt: new Date().toISOString(), comics }, null, 1);
  return shareOrDownload(payload, `comics-backup-${stamp()}.json`, 'application/json');
}

export async function readBackup(file) {
  const data = JSON.parse(await file.text());
  const comics = Array.isArray(data) ? data : data.comics;
  if (!Array.isArray(comics)) throw new Error('This file is not a comic collection backup.');
  return comics.filter((c) => c && typeof c === 'object' && c.id);
}
