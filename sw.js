// Keeps the app working offline. Bump VERSION whenever app files change so
// phones pick up the new version.
const VERSION = 'v9';
const SHELL = [
  './',
  'index.html',
  'manifest.webmanifest',
  'css/app.css',
  'js/app.js',
  'js/backup.js',
  'js/barcode.js',
  'js/clz.js',
  'js/db.js',
  'js/library.js',
  'js/lookup.js',
  'js/scanner.js',
  'js/voice.js',
  'vendor/zxing-reader.js',
  'vendor/zxing_reader.wasm',
  'icons/icon-192.png',
  'icons/icon-512.png',
];

self.addEventListener('install', (event) => {
  event.waitUntil(caches.open(VERSION).then((c) => c.addAll(SHELL)).then(() => self.skipWaiting()));
});

self.addEventListener('activate', (event) => {
  event.waitUntil(
    caches.keys()
      .then((keys) => Promise.all(keys.filter((k) => k !== VERSION).map((k) => caches.delete(k))))
      .then(() => self.clients.claim()),
  );
});

self.addEventListener('fetch', (event) => {
  const url = new URL(event.request.url);
  // Only the app's own files are cached; lookups and cover images go to the network.
  if (event.request.method !== 'GET' || url.origin !== location.origin) return;
  event.respondWith(
    caches.match(event.request, { ignoreSearch: true }).then((hit) => hit || fetch(event.request)),
  );
});
