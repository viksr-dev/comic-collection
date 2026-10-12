// Opens digital comics on the phone: CBZ and EPUB (zip), CBR (RAR) and PDF.
// The file's contents decide how it's read, not its name, so a .cbr that's
// really a zip (common) still opens. Zip pages are read straight out of the
// file without loading the whole thing, which keeps big comics light on memory.

const IMAGE = /\.(jpe?g|png|gif|webp|bmp|avif)$/i;
const MIME = { jpg: 'image/jpeg', jpeg: 'image/jpeg', png: 'image/png', gif: 'image/gif', webp: 'image/webp', bmp: 'image/bmp', avif: 'image/avif' };

export const isImageName = (n) => IMAGE.test(n) && !/(^|\/)(\.|__MACOSX\/)/.test(n);
export const naturalSort = (a, b) => a.localeCompare(b, undefined, { numeric: true, sensitivity: 'base' });
const mimeOf = (n) => MIME[n.split('.').pop().toLowerCase()] || 'application/octet-stream';

// A tidy title from a file name: "Batman_002_(2016)_(digital).cbz" → "Batman 002 (2016)".
export function titleFromFileName(name) {
  return name
    .replace(/\.[^.]+$/, '')
    .replace(/_/g, ' ')
    .replace(/\((?!\d{4}\))[^)]*\)|\[[^\]]*\]/g, '')
    .replace(/\s+/g, ' ')
    .trim() || name;
}

// Returns { kind, pageCount, getPage(index, width) → Promise<Blob>, close() }.
export async function openComic(blob) {
  const head = new Uint8Array(await blob.slice(0, 8).arrayBuffer());
  const starts = (...bytes) => bytes.every((b, i) => head[i] === b);
  let book;
  if (starts(0x25, 0x50, 0x44, 0x46)) book = await openPdf(blob); // %PDF
  else if (starts(0x50, 0x4b)) book = await openZip(blob); // PK
  else if (starts(0x52, 0x61, 0x72, 0x21)) book = await openRar(blob); // Rar!
  else if (starts(0x37, 0x7a, 0xbc, 0xaf)) throw new Error("7-Zip comics (.cb7) can't be opened on the phone. Open it on the computer instead.");
  else throw new Error("That doesn't look like a comic file (CBZ, CBR, PDF or EPUB).");
  if (!book.pageCount) {
    book.close();
    throw new Error('No pages found in that file.');
  }
  return book;
}

// ---------- zip (CBZ and EPUB) ----------

async function zipEntries(blob) {
  const tailLen = Math.min(blob.size, 65557);
  const tailStart = blob.size - tailLen;
  const tail = new DataView(await blob.slice(tailStart).arrayBuffer());
  let eocd = -1;
  for (let i = tailLen - 22; i >= 0; i--) {
    if (tail.getUint32(i, true) === 0x06054b50) { eocd = i; break; }
  }
  if (eocd < 0) throw new Error("This zip file looks damaged (its list of files is missing).");
  let count = tail.getUint16(eocd + 10, true);
  let cdSize = tail.getUint32(eocd + 12, true);
  let cdOffset = tail.getUint32(eocd + 16, true);
  // Very big files (over 4 GB, or over 65,535 pages) use the zip64 layout.
  if ((cdOffset === 0xffffffff || count === 0xffff) && eocd >= 20 && tail.getUint32(eocd - 20, true) === 0x07064b50) {
    const at = Number(tail.getBigUint64(eocd - 20 + 8, true));
    const z = new DataView(await blob.slice(at, at + 56).arrayBuffer());
    count = Number(z.getBigUint64(32, true));
    cdSize = Number(z.getBigUint64(40, true));
    cdOffset = Number(z.getBigUint64(48, true));
  }
  const cd = new DataView(await blob.slice(cdOffset, cdOffset + cdSize).arrayBuffer());
  const decoder = new TextDecoder();
  const entries = new Map();
  let p = 0;
  for (let n = 0; n < count && p + 46 <= cd.byteLength; n++) {
    if (cd.getUint32(p, true) !== 0x02014b50) break;
    const method = cd.getUint16(p + 10, true);
    let compSize = cd.getUint32(p + 20, true);
    let size = cd.getUint32(p + 24, true);
    const nameLen = cd.getUint16(p + 28, true);
    const extraLen = cd.getUint16(p + 30, true);
    const commentLen = cd.getUint16(p + 32, true);
    let offset = cd.getUint32(p + 42, true);
    const name = decoder.decode(new Uint8Array(cd.buffer, p + 46, nameLen));
    // zip64 sizes and offset live in an extra field when they don't fit.
    let e = p + 46 + nameLen;
    const extraEnd = e + extraLen;
    while (e + 4 <= extraEnd) {
      const id = cd.getUint16(e, true);
      const len = cd.getUint16(e + 2, true);
      if (id === 0x0001) {
        let q = e + 4;
        if (size === 0xffffffff) { size = Number(cd.getBigUint64(q, true)); q += 8; }
        if (compSize === 0xffffffff) { compSize = Number(cd.getBigUint64(q, true)); q += 8; }
        if (offset === 0xffffffff) { offset = Number(cd.getBigUint64(q, true)); }
      }
      e += 4 + len;
    }
    if (!name.endsWith('/')) entries.set(name.replace(/\\/g, '/'), { name, method, compSize, size, offset });
    p += 46 + nameLen + extraLen + commentLen;
  }
  return entries;
}

let inflateModule;
async function inflate(blob) {
  if (typeof DecompressionStream !== 'undefined') {
    try {
      const stream = blob.stream().pipeThrough(new DecompressionStream('deflate-raw'));
      return await new Response(stream).blob();
    } catch {
      // Older phones: fall back to the small built-in decompressor.
    }
  }
  inflateModule ||= import('../vendor/reader/inflate.mjs');
  const { inflateSync } = await inflateModule;
  return new Blob([inflateSync(new Uint8Array(await blob.arrayBuffer()))]);
}

async function zipRead(blob, entry) {
  const local = new DataView(await blob.slice(entry.offset, entry.offset + 30).arrayBuffer());
  if (local.getUint32(0, true) !== 0x04034b50) throw new Error('This zip file looks damaged.');
  const start = entry.offset + 30 + local.getUint16(26, true) + local.getUint16(28, true);
  const data = blob.slice(start, start + entry.compSize);
  if (entry.method === 0) return data;
  if (entry.method === 8) return inflate(data);
  throw new Error('This comic uses a kind of zip compression the phone can\'t read.');
}

async function openZip(blob) {
  const entries = await zipEntries(blob);
  const lower = new Map([...entries.keys()].map((k) => [k.toLowerCase(), k]));
  const find = (path) => entries.get(path) || entries.get(lower.get(path.toLowerCase()));
  let kind = 'CBZ';
  let pages = [];
  if (find('META-INF/container.xml')) {
    kind = 'EPUB';
    pages = await epubPages(blob, find);
  }
  if (!pages.length) pages = [...entries.keys()].filter(isImageName).sort(naturalSort);
  return {
    kind,
    pageCount: pages.length,
    async getPage(i) {
      const data = await zipRead(blob, find(pages[i]));
      return new Blob([data], { type: mimeOf(pages[i]) });
    },
    close() {},
  };
}

// "OEBPS/Text" + "../Images/p1.jpg" → "OEBPS/Images/p1.jpg"
function resolvePath(base, href) {
  const parts = base ? base.split('/') : [];
  for (const part of decodeURIComponent(href.split('#')[0]).split('/')) {
    if (part === '..') parts.pop();
    else if (part && part !== '.') parts.push(part);
  }
  return parts.join('/');
}
const dirOf = (path) => path.includes('/') ? path.slice(0, path.lastIndexOf('/')) : '';

// The pictures in an EPUB, in reading order.
async function epubPages(blob, find) {
  const text = async (path) => (await zipRead(blob, find(path))).text();
  const xml = (s) => new DOMParser().parseFromString(s, 'application/xml');
  try {
    const container = xml(await text('META-INF/container.xml'));
    const opfPath = container.getElementsByTagNameNS('*', 'rootfile')[0]?.getAttribute('full-path');
    if (!opfPath || !find(opfPath)) return [];
    const opf = xml(await text(opfPath));
    const base = dirOf(opfPath);
    const manifest = new Map();
    for (const item of opf.getElementsByTagNameNS('*', 'item')) {
      manifest.set(item.getAttribute('id'), resolvePath(base, item.getAttribute('href') || ''));
    }
    const pages = [];
    const seen = new Set();
    for (const ref of opf.getElementsByTagNameNS('*', 'itemref')) {
      const href = manifest.get(ref.getAttribute('idref'));
      if (!href || !find(href)) continue;
      if (isImageName(href)) {
        if (!seen.has(href)) { seen.add(href); pages.push(find(href).name.replace(/\\/g, '/')); }
        continue;
      }
      const html = await text(href);
      for (const m of html.matchAll(/<(?:img|image)\b[^>]*?\b(?:src|xlink:href|href)\s*=\s*["']([^"']+)["']/gi)) {
        const pic = resolvePath(dirOf(href), m[1]);
        const entry = find(pic);
        if (entry && isImageName(pic) && !seen.has(pic)) { seen.add(pic); pages.push(entry.name.replace(/\\/g, '/')); }
      }
    }
    return pages;
  } catch {
    return [];
  }
}

// ---------- RAR (CBR) ----------

let unrarModule;
let unrarWasm;
async function openRar(blob) {
  unrarModule ||= import('../vendor/reader/unrar.mjs');
  unrarWasm ||= fetch(new URL('../vendor/reader/unrar.wasm', import.meta.url)).then((r) => r.arrayBuffer());
  const [{ createExtractorFromData }, wasmBinary] = await Promise.all([unrarModule, unrarWasm]);
  const data = await blob.arrayBuffer();
  const extractor = await createExtractorFromData({ data, wasmBinary });
  const names = [...extractor.getFileList().fileHeaders]
    .filter((h) => !h.flags.directory && isImageName(h.name.replace(/\\/g, '/')))
    .map((h) => h.name)
    .sort(naturalSort);
  return {
    kind: 'CBR',
    pageCount: names.length,
    async getPage(i) {
      const { files } = extractor.extract({ files: [names[i]] });
      const file = [...files][0];
      if (!file?.extraction) throw new Error(`Couldn't read page ${i + 1}.`);
      return new Blob([file.extraction], { type: mimeOf(names[i]) });
    },
    close() {},
  };
}

// ---------- PDF ----------

let pdfModule;
async function openPdf(blob) {
  pdfModule ||= import('../vendor/reader/pdf.min.mjs').then((pdfjs) => {
    pdfjs.GlobalWorkerOptions.workerSrc = new URL('../vendor/reader/pdf.worker.min.mjs', import.meta.url).href;
    return pdfjs;
  });
  const pdfjs = await pdfModule;
  const doc = await pdfjs.getDocument({ data: new Uint8Array(await blob.arrayBuffer()), isEvalSupported: false }).promise;
  return {
    kind: 'PDF',
    pageCount: doc.numPages,
    async getPage(i, width = 1200) {
      const page = await doc.getPage(i + 1);
      const natural = page.getViewport({ scale: 1 });
      // Phones limit how big a drawing can be, so stay well inside that.
      const scale = Math.min(width / natural.width, Math.sqrt(12e6 / (natural.width * natural.height)));
      const viewport = page.getViewport({ scale });
      const canvas = document.createElement('canvas');
      canvas.width = Math.round(viewport.width);
      canvas.height = Math.round(viewport.height);
      const ctx = canvas.getContext('2d');
      ctx.fillStyle = '#fff';
      ctx.fillRect(0, 0, canvas.width, canvas.height);
      await page.render({ canvasContext: ctx, viewport }).promise;
      page.cleanup();
      const out = await new Promise((resolve) => canvas.toBlob(resolve, 'image/jpeg', 0.9));
      canvas.width = canvas.height = 0;
      return out;
    },
    close() { doc.destroy(); },
  };
}
