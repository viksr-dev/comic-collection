# Comic Collection

A phone app for cataloguing a comic collection by scanning barcodes.

**Open it:** https://viksr-dev.github.io/comic-collection/

## Installing on an Android phone

1. Open the link above in **Chrome**.
2. Tap the **⋮** menu, then **Add to Home screen**, then **Install**.
3. It now opens from its own icon, full screen, and works without internet. Only comic lookups need a connection.

## Using it

- **Scan:** point the camera at the barcode on the cover. Most modern comics have a main barcode plus a small 5-digit code on its right: the first three digits are the issue number, then the cover variant, then the printing. If the phone can't read the small code, type it in.
- **No barcode** (older comics): use **Search by title**, or fill in the details yourself.
- **Duplicates:** if you scan a comic you already own, the app tells you and offers to count another copy.
- **Collection:** search, sort, and tap any comic to edit or delete it.
- **Say it:** tap 🎤 and say something like "Batman 404 near mint". The app fills in the series, issue and condition and searches for it. If the browser has no voice support, use the microphone on the keyboard instead.
- **Computer's collection and wishlist:** with *Send scans to your computer* turned on and the code saved in the desktop app, the desktop app sends back what you own and your wishlist. Scanning in a shop then says "you already own this" or "on your wishlist", and the **Wishlist** tab lists your wishlist grouped by story arc. The last copy is kept on the phone, so it works without signal.

## Backup

Your collection is stored on the phone itself. In **Settings**:

- **Export spreadsheet (CSV)** opens your phone's share menu. Choose **OneDrive** to save a copy you can open in Excel.
- **Export full backup** saves everything, which you can bring back with **Restore from a full backup** (on a new phone, for example).

Export regularly. If you uninstall the app or clear Chrome's data, the collection on the phone is deleted with it.

## Importing from CLZ Comics

Export your collection from CLZ Comics as a CSV file, then in **Settings** tap **Import CLZ file (CSV)**. The importer reads the series, issue and variant letter (`47A`), cover/variant name, publisher, release date and format, and keeps volume numbers (`Batgirl, Vol. 5`). Importing the same file again skips comics already brought in. When you later scan a comic with the same series and issue number, the app shows "You may already have this".

## Comic lookup

Details come from [Metron](https://metron.cloud) through a small free relay you set up once. See [relay/README.md](relay/README.md).

## How it's built

Plain HTML, CSS and JavaScript with no build step, hosted on GitHub Pages.

- Barcode reading: [zxing-wasm](https://github.com/Sec-ant/zxing-wasm) (MIT), which also reads the 5-digit add-on. The files are kept in `vendor/` so the app works offline.
- Storage: IndexedDB in the browser.
- `sw.js` caches the app for offline use. Bump `VERSION` in it whenever app files change.
- `relay/worker.js`: the Cloudflare Worker relay for Metron.
