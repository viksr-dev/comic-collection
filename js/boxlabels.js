// Box labels: a QR code for each box, to print and stick on it.
//
// The QR code is a link to the app with the box's name, like
// https://comics.keyissue.app/?box=Box%205. Scanned in the app it files the comic you just
// scanned in that box (or opens the box); scanned with the phone's own camera it opens the
// app on that box's list. The link only has the box's name, so it shows the comics of whoever
// is signed in: each person's boxes stay their own.

const QR = globalThis.qrcode; // vendor/qrcode.js (Kazuhiko Arase, MIT)
const PARAM = 'box';

/** The link a box's label holds. */
export function boxLink(name, base = location.href) {
  const url = new URL('./', base);
  url.search = '';
  url.hash = '';
  url.searchParams.set(PARAM, String(name).trim());
  return url.href;
}

/**
 * The box a scanned QR code (or a link the app was opened with) is for, or null when it's
 * something else. Links to this app on any address count, so labels printed on GitHub Pages work too.
 */
export function boxFromLink(text) {
  let url;
  try { url = new URL(String(text || '').trim()); } catch { return null; }
  if (!/^https?:$/.test(url.protocol)) return null;
  const name = (url.searchParams.get(PARAM) || '').trim();
  if (!name) return null;
  const ours = url.host === location.host || /(^|\.)keyissue\.app$/.test(url.hostname) || url.hostname.endsWith('.github.io');
  return ours ? name : null;
}

/** The QR code for text as an SVG (black on white), size in CSS pixels. */
export function qrSvg(text, size = 160) {
  if (!QR) return '';
  QR.stringToBytes = QR.stringToBytesFuncs['UTF-8'];
  const qr = QR(0, 'M');
  qr.addData(String(text), 'Byte');
  qr.make();
  const n = qr.getModuleCount();
  const quiet = 4; // the white border scanners need
  let path = '';
  for (let r = 0; r < n; r++) {
    for (let c = 0; c < n; c++) if (qr.isDark(r, c)) path += `M${c + quiet} ${r + quiet}h1v1h-1z`;
  }
  const all = n + quiet * 2;
  return `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ${all} ${all}" width="${size}" height="${size}" shape-rendering="crispEdges" role="img" aria-label="QR code">`
    + `<rect width="${all}" height="${all}" fill="#fff"/><path d="${path}" fill="#000"/></svg>`;
}

const esc = (s) => String(s ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]);

/** The labels for these boxes ([{ name, sub }]), as HTML for the print sheet. */
export function labelsHtml(boxes) {
  return boxes.map((b) => `<div class="label">
    ${qrSvg(boxLink(b.name), 150)}
    <div class="label-text"><div class="label-name">${esc(b.name)}</div>
    ${b.sub ? `<div class="label-sub">${esc(b.sub)}</div>` : ''}
    <div class="label-hint">Scan to see what's inside</div></div>
  </div>`).join('');
}
