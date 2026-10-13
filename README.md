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
- **Duplicates:** if you scan or add a comic you already own (on the phone or the computer), or scan the same one twice in a pile, the app asks *Add anyway* or *Cancel*.
- **Collection:** search, sort, and tap any comic to edit or delete it. Tick *I've read it* to mark it read (a ✓ Read shows in the list). Key issues your computer knows about show ★ Key.
- **Missing issues:** choose *Missing issues* above the Collection list to see every series with gaps, counting the comics on your computer too (with *Send scans to your computer* on). Tap one to add the ones you actually have. Tap *Find how long your series are on Metron* and it counts to the end of each series too (so with #1-40 of a 150-issue series, #41-150 show as missing). That needs relay version 12 (see *Updating the relay*). A jump of more than 100 in the numbering (like Daredevil going from #119 back to the old #500) isn't counted as missing. A series with several runs on Metron (like Daredevil) only counts to the end once its volume says which run it is (the year it began, like 2019). Not collecting a series? Tap it and choose *Not collecting this — hide it from Missing issues*.
- **Covers not found?** Tap *Select*, pick the comics (or *All shown*), then *Find covers for…*. It looks them up again by series and issue number, even ones tried before, and says which issues Metron doesn't have (often ones not out yet).
- **Volumes:** *Find details and covers* puts comics into the volume Metron says (by the year it began, like *Daredevil (1998)*), including ones matched before that had no volume.
- **One series under two names** (like *The Walking Dead Deluxe* and *The Walking Dead Deluxe (2020)*): tap either in Missing issues, then *Same series as another? Merge them…*. Its comics on the phone move into the other one; an issue in both is kept once with the copies added together. Do the same on the computer for comics there.
- **Add more issues without scanning:** open a comic and tap *Add more issues…*. Type the issue numbers of that series you have, like `1-12, 15` (the gaps you're missing are filled in for you), and give them a condition and box. Ones you already have are skipped, and *Find details and covers* finds their covers.
- **Deleted by mistake?** Tap *Undo* on the message that pops up. Deleted comics also wait in **Settings › Deleted comics** for 30 days, where *Put back* brings one back.
- **Bigger writing:** Settings › Text size makes the whole app bigger.
- **Trades:** the choice above the Collection list switches between **Single issues**, **Trades & hardcovers** and **Everything**. A comic's **Format** decides where it goes (Trade Paperback, Hardcover, Omnibus, Graphic Novel). Scanning a book's ISBN barcode (starting 978 or 979) sets it to Trade Paperback for you.
- **Read:** digital comics (CBZ, CBR, PDF and EPUB) kept on the phone, so they work with no signal. Tap **Add comics** and pick files, for example from iCloud Drive (set the computer app's Digital comics folder to one in iCloud Drive and the same comics are there). Swipe or tap the sides to turn pages, pinch or double-tap the middle to zoom, or switch to **Webtoon** to scroll down one long strip. **Right to left** is for manga. It remembers your page. The ⋯ on a cover lets you start again, rename it or remove it from the phone. iPhones give web apps a limited amount of space (the Read tab shows how much), so keep the comics you're reading on the phone rather than your whole library.
- **Say it:** tap 🎤 and say something like "Batman 404 near mint". The app fills in the series, issue and condition and searches for it. If the browser has no voice support, use the microphone on the keyboard instead.
- **Computer's collection and wishlist:** with *Send scans to your computer* turned on and the code saved in the desktop app, the desktop app sends back what you own and your wishlist. Scanning in a shop then says "you already own this" (with the box it's in, whether it's a key issue and its eBay estimate, when the desktop app knows them) or "on your wishlist", and the **Wishlist** tab lists your wishlist grouped by story arc. The last copy is kept on the phone, so it works without signal.
- **Changes go both ways:** with *Send scans to your computer* on, changing a comic's box, condition or read on the phone changes it on the computer too, and the other way round. Only what you changed is sent, and a change you made on the phone stays until the computer has it.

## Backup

Your collection is stored on the phone itself. In **Settings**:

- **Export spreadsheet (CSV)** opens your phone's share menu. Choose **OneDrive** to save a copy you can open in Excel.
- **Export full backup** saves everything, which you can bring back with **Restore from a full backup** (on a new phone, for example).

Export regularly. If you uninstall the app or clear Chrome's data, the collection on the phone is deleted with it. The Collection tab reminds you to back up once 5 or more comics have been added or changed and your last backup was over two weeks ago (*Later* waits a week).

## Importing from CLZ Comics

Export your collection from CLZ Comics as a CSV file, then in **Settings** tap **Import CLZ file (CSV)**. The importer reads the series, issue and variant letter (`47A`), cover/variant name, publisher, release date and format, and keeps volume numbers (`Batgirl, Vol. 5`). Importing the same file again skips comics already brought in. When you later scan a comic with the same series and issue number, the app shows "You may already have this".

## Comic lookup

Details come from [Metron](https://metron.cloud) through a small free relay you set up once. See [relay/README.md](relay/README.md).

## How it's built

Plain HTML, CSS and JavaScript with no build step, hosted on GitHub Pages.

- Barcode reading: [zxing-wasm](https://github.com/Sec-ant/zxing-wasm) (MIT), which also reads the 5-digit add-on. The files are kept in `vendor/` so the app works offline.
- Storage: IndexedDB in the browser.
- Comic reader (`js/comicfile.js`, `js/reader.js`, `js/shelf.js`, `js/read.js`): zip-based CBZ and EPUB are read directly (with [fflate](https://github.com/101arrowz/fflate), MIT, for older phones), CBR with [node-unrar-js](https://github.com/YuJianrong/node-unrar.js) (MIT, bundled with esbuild into `vendor/reader/unrar.mjs`), PDF with [PDF.js](https://mozilla.github.io/pdf.js/) 4.10.38 legacy build (Apache-2.0). All in `vendor/reader/`.
- `sw.js` caches the app for offline use. Bump `VERSION` in it whenever app files change.
- `relay/worker.js`: the Cloudflare Worker relay for Metron.
