# Comic Catalog for Windows

A Visual Basic desktop app for your comic collection, with its own SQLite database.
Type in (or scan) a barcode and it pulls up the issue from Metron, the same way the phone app does.

## What it does

- **Home**: the first page. Big totals, digital comics you're part-way through (*Keep reading*), the newest videos, and rows of covers: just added, waiting to be read, and your key issues. Click any cover to open it. The menu down the left side goes to every page.
- **Digital comics** (Digital page): *Add comic files…* (or drag files onto the page) adds CBZ, CBR, CB7, PDF and EPUB comics. *Add a folder…* adds every comic in a folder. Each is copied into `Documents\Comic Catalog\Digital comics` (change it in Settings), so it's always here to read even if the download is moved or deleted. Files named like "Batman 404 (1987).cbz" are matched to that comic in your collection. Click a cover to read; right-click to link it to a comic, rename it or remove it. From the Collection page, the More menu has *Read digital copy* and *Add a digital copy…*.
- **Comic reader**: opens where you left off. *One page* and *Two pages* modes turn pages with a smooth slide (click the left or right side, or use the arrow keys); *Webtoon* is one long strip you scroll down smoothly with the mouse wheel, Space or by dragging. *Fit width* fills the screen's width, *Right to left* is for manga. Keys: W webtoon, D two pages, F fit width, M manga, Home/End, F11 or double-click for full screen, Esc to leave. Finish a comic that's linked to your collection and it offers to mark it read.
- **Videos**: the newest videos from YouTube channels you pick (starts with @comictom101). New episodes appear by themselves while the app is open (it checks every hour), and the Videos button says how many are new. Click one to watch it on YouTube. Add more channels with commas between them.
- **Covers view sizes**: next to the *Covers* button, choose small, medium or large covers.
- **Collection**: everything you own, with search. Type a barcode and press Enter to add a comic. If you already have it, it offers to add another copy. Double-click a comic to edit it. Select several (hold Ctrl or Shift) and press Delete to remove them. The comics you selected stay selected after a change, so you can do several things to them in a row. Click a column heading to sort by it (issue numbers go 1, 2, 10), again to sort the other way, and a third time to go back to series order. The app remembers the sort, how wide you dragged each column, and the window's size and place.
- **Shortcuts**: Ctrl+F searches your collection, Ctrl+N adds a comic by hand, Ctrl+E edits, Ctrl+B puts in a box, Ctrl+R marks read or not, Ctrl+Z undoes, F5 refreshes the page, Ctrl+1 to Ctrl+9 go to the pages down the left and Esc clears the search. Typing a series name (in Search or the add window) suggests series you already have. Settings lists them all.
- **Undo**: after a change (delete, read, box, trade, key issue, rating, lending, a set, or editing several) a link under the buttons says what was done with *Undo*. Ctrl+Z does the same, and each press goes one step further back.
- **Deleted comics go to a bin** for 30 days, with their photos, box, loans and set. *Undo* puts them straight back, or *More ▾ › Deleted comics (bin)* (also in Settings) puts back any of them later or empties the bin.
- **Edit several at once**: select several comics and click *Edit* (or *More ▾ › Edit several at once*) to give them all the same condition, value, date bought, box, format, publisher or read. Blank boxes are left as they are on each comic.
- **Filters**: next to Search, show just read, unread, key issues, for trade, lent out or *No cover*, and just one publisher.
- **Boxes list**: down the right of the Collection page. Click a box to see what's in it (or *Not in a box*), and drag comics from the list onto a box to move them; drop them on *New box…* to name a new one. The *Boxes* button hides or shows it.
- **Barcode lookup**: fills in series, issue, story title, publisher, cover date and the cover picture. If Metron doesn't know the barcode, use "Search by series and issue" in the add window.
- **Trades**: trade paperbacks, hardcovers, omnibuses and graphic novels get their own page, kept apart from single issues on Collection. A comic's Format decides which page it's on, and an ISBN barcode fills in Trade Paperback for you. Trades don't count towards Missing issues.
- **Missing issues**: series with gaps between the first and last issue you own, and exactly which issues are missing. You can send missing issues straight to your wishlist. If you have them but can't be bothered scanning them, select them and click *I have these: add to my collection* (with none selected it takes them all). You can also type more, like `41-60`, and give them a condition and box. *More ▾ › Add more issues of this series* on the Collection page does the same for the selected comic's series. Covers and details are filled in afterwards by *Find covers*. *Find how long each series is* (on the Missing issues page, and by itself in the background) asks Metron how many issues each series has, so issues after the last one you own show as missing too. It needs relay version 12.
- **Wishlist**: comics you want, with a priority and the most you'd pay. A comic comes off the list when you add it to your collection.
- **Totals** at the bottom: comics, copies, total value and total paid.
- **Import** the spreadsheet exported from the phone app, so you don't have to type in your collection again.
- **Find covers and prices**: fetches cover pictures and original cover prices for comics that don't have them yet (for example ones imported from the phone app). It goes slowly to stay within Metron's limits; click it again to stop, and it carries on later. Cover prices need the updated relay (see [Updating the relay](../relay/README.md#updating-the-relay)).
- **Scans from your phone**: turn on *Send scans to your computer* in the phone app's Settings and type its code into Settings here. While the app is open, new scans arrive every couple of minutes (or click *Get scans from phone*). The relay needs a one-time setup for this; see [Sending scans to your computer](../relay/README.md#sending-scans-to-your-computer).
- **Sets (story arcs and runs)**: select the comics of an arc or run on the Collection page (hold Ctrl or Shift to pick several), choose *Make a set…* in the More menu, name it (like "Batman: Year One") and give it one value. The value is split evenly across the comics. Change it later on the *Sets* page.
- **Find story arcs** (Sets page): looks up on Metron which story arcs your comics are part of and makes a set for each arc you have two or more issues of, showing how long the whole arc is. Only comics matched to Metron can be checked, so run *Find covers and prices* first. *Check value on eBay* on the Sets page opens sold listings for the whole set, and *Add missing to wishlist* puts the arc's issues you don't have on the wishlist under the arc's name (needs relay version 5).
- **New releases**: new issues of the series you follow, out in the last two weeks or coming soon, with *Add to wishlist*. *Series to follow* picks which series are checked (ones you have a recent issue of are ticked to start with). Needs relay version 4.
- **Read or not read**: select comics and click *Read / unread* (or press R). The list has a Read column, a filter next to Search, and a READ total.
- **Export list**: saves your whole collection as an Excel workbook or a PDF, with totals, for insurance or selling.
- **Recently changed** (Home): the comics you changed last, so you can check what you've just done. Covers, prices and eBay estimates the app fills in by itself don't count.
- **Settings** are in four groups (your collection and backups, your phone, looking things up, how the app looks), with links at the top. *Text size* makes everything bigger (Normal, Large or Larger).
- **Getting started**: the first time the app opens with no comics, three steps help you set up lookup, connect your phone and bring in your comics. Settings has them again.
- **Messages in plain words**: when something goes wrong (no internet, a file open in another program, a full disk), the message says what to do about it.
- **Daily backup**: once a day a copy of the database goes to OneDrive (or Documents\Comic Catalog\Backups without OneDrive). The last 30 are kept. Settings has *Back up now*, *Change folder* and *Restore a backup*.
- **Graded comics**: in the edit window, set *Graded (slab)* to CGC, CBCS or PGX and fill in the grade, label and certificate number. The list shows "CGC 9.8" in the Condition column, searching "cgc" finds them, and *Verify* opens CGC's certificate lookup.
- **Check value on eBay** (in the edit window): opens eBay's sold listings for that issue, so you can see what copies actually sold for and type a value in.
- **Phone knows your collection**: with a sync code saved (see *Scans from your phone*), the app sends the phone a list of what you own and your wishlist whenever it changes. Scanning in a shop then says if you already have a comic or want it. Needs relay version 6.
- **Does things by itself**: while the app is open it finds covers and prices for new comics, sorts them into story arcs and checks for new releases once a week. Turn it off in Settings under *Do things by themselves*.
- **Updates itself**: each time it opens, the app checks GitHub for a newer version and asks before installing it. It restarts by itself; your comics aren't touched. Settings has *Check for updates*.
- **Stats**: charts of your comics by publisher, decade, series, writer and artist, your most valuable comics, and how the collection's value changes over time (the app notes it each day it's open). *Bars* switches between counting comics and adding up their value. Writers and artists fill in as the app looks up your comics in the background; this needs relay version 8.
- **What should I read next?** (Collection page, More menu): suggests an unread comic, starting with the next issue of runs you're part-way through, then series you haven't started. *I've read it* marks it and moves on, *Something else* shows another idea.
- **Your own photos**: in the edit window, *Add photos…* keeps photos of your copy (front, back, signature, damage, receipt) with the comic. Click one to open it; right-click to label or remove it. They're saved in `Documents\Comic Catalog\Photos` and copied with the daily backup. iPhone photos need to be JPEG (in iCloud for Windows, turn on *Download High Efficiency files as JPEG*).
- **Lending**: select comics, click *Lend…* and say who to. The Collection list has a *Lent to* column, searching a name shows what that person has, and the *Lent out* page lists everything that's out with *Given back*.
- **Share page**: *Share page…* (the Collection page's More menu, or the Wishlist page) makes one web page file of your collection and/or wishlist to email, text or share from OneDrive. It opens on any phone and has a search box. Values are left out unless you tick them. Nothing is put online.
- **Put in box…** (Collection page, or Ctrl+B): puts the selected comics in a box or on a shelf. It starts with the box you used last, so filling a box is quick. The list has a Box column and searching "Box 3" shows what's in it.
- **More menu** (Collection page, or right-click comics): *Make a set*, *What should I read next?*, *Add from a shop receipt*, *Export list*, *Share page*, *For trade or sale* with an asking price, *Mark as key issue* with a note like "1st appearance of Harley Quinn", *Insurance report*, and the eBay estimates below. The filter next to Search also shows just key issues, comics for trade (including spare copies) or comics lent out. The edit window has the same fields.
- **Insurance report**: a PDF of every comic (or just ones worth more than an amount you choose) with its details, cover and your own photos, and the total value. Handy for your insurer.
- **Spending and reading goals** (Stats page): set a monthly budget and how many comics you'd like to read a month. Cards show this month against them, and charts show the last 12 months. Spending uses *Date bought* (new comics start with today's date); reading counts comics marked read from now on.
- **Pull list for the shop** (New releases page): the upcoming issues of series you follow that you don't have yet, ready to copy, email to your comic shop or print.
- **Trade list on the share page**: tick *Comics for trade or sale* to add a section listing them (and spare copies) with asking prices.
- **Values from eBay**: once your relay has your free eBay developer keys (see [Values from eBay](../relay/README.md#values-from-ebay-optional)), *Estimate values from eBay* (More menu or Settings) checks each comic's current eBay asking prices and shows the typical one, in US dollars, in an *eBay est.* column. It runs by itself in the background too and re-checks each comic monthly. Nothing changes your values until you click *Use eBay estimates as values*, which can fill in just the blank ones or replace them all.
- **Ratings and reviews**: give a comic 1 to 5 stars and a few words (More menu, *Rate…*, or the edit window). *What should I read next?* asks for stars when you mark one read, and suggests series you rated highly first and ones you didn't like last.
- **Box labels** (More menu): a PDF with a big label for each box, listing what's in it ("Batman (2016) #1-12, 15"). Two to a page; cut along the line.
- **Convention signing list** (More menu): paste a convention's guest list and see which of your comics they wrote or drew, key issues first, with the box each is in. Copy it or print it. The monthly convention update lists the comic guests for NZ and Australian shows.
- **Wishlist deals from eBay**: give wishlist comics a most-you'd-pay (*Most I'd pay…* on the Wishlist page). Once a day the app looks on eBay for Buy It Now listings at or under that price, and *eBay deals* lists them with a link to each. Wishlist prices are in your own dollars; set the exchange rate under Settings, *Values from eBay*. Needs relay version 10 with eBay keys.
- **Worth getting graded?** (More menu): compares what a raw copy and a CGC 9.8 copy of each comic sell for on eBay, less the grading cost you enter, and lists the ones that would pay off.
- **Rising on eBay** (Stats page): the comics whose eBay estimates went up most since a month or more ago.
- **Banner picture**: pick any picture from your computer (Settings) to show across the top. It stays on your computer.

A USB barcode scanner works too. Click in the Barcode box and scan, because the scanner types the digits and presses Enter for you.

## Getting the app

### Easiest: download it ready to run

1. On GitHub, open the repository and click the **Actions** tab.
2. Click the latest **Desktop app** run with a green tick.
3. Scroll down to **Artifacts** and click **ComicCatalog-windows** to download a zip.
4. Unzip it and double-click **ComicCatalog.exe**. If Windows says "Windows protected your PC", click **More info**, then **Run anyway**. It says that because the app isn't signed, not because something is wrong.

You only need to do this once. After that the app updates itself: each build on the main branch is also published under **Releases** on GitHub, and the app offers to install it when it opens. Keep ComicCatalog.exe in a folder you can write to (like Documents or the Desktop, not Program Files) so it can replace itself.

### Or open it in Visual Studio (to change the code)

1. Install **Visual Studio 2022 Community** (free). In the installer, tick **.NET desktop development**.
2. Open `desktop/ComicCatalog/ComicCatalog.vbproj`.
3. Press **F5** to run it.

## First run

1. Go to **Settings** and paste your relay address, the same one the phone app uses (`https://comic-relay.….workers.dev`). Click **Save and test**.
2. To bring in your collection from the phone: on your phone, open **Settings** in the phone app and tap **Export spreadsheet (CSV)**. Save it to iCloud or OneDrive. Then, on the computer, click **Import phone app spreadsheet (CSV)…** and pick that file.
3. Or click **Load sample comics** to try it out first.

Your collection is saved in `Documents\Comic Catalog\comics.db`. Copy that file somewhere safe as a backup.

## The database

The `database` folder has:

| File | What it is |
| --- | --- |
| `schema.sql` | Builds the tables: publishers, series, comics, creators, collection, wishlist, sets, photos, loans, value history, and a `v_collection` view that joins them |
| `sample-data.sql` | A few sample comics, creators and wishlist entries |
| `sample-comics.db` | A ready-made database with the sample comics in it |
| `queries.sql` | Handy queries: total value, value by series, missing issues in a run, series with gaps, wishlist, comics worth more than you paid, top creators |

You can open any of the `.db` files in [DB Browser for SQLite](https://sqlitebrowser.org/) (free) to look around or run the queries yourself.

To build a fresh database by hand: `sqlite3 comics.db < schema.sql`, then `sqlite3 comics.db < sample-data.sql` if you want the samples.

## For developers

- `ComicCatalog/` is the WinForms app (.NET 10, Visual Basic). The screens are built in code, in `MainForm.vb` (Home, Digital and Videos pages in `MainForm.Pages.vb`), `ComicForm.vb` and `WishForm.vb`. The reader is `ReaderForm.vb` and `ReaderCanvas.vb`.
- Digital comics are opened by `Data/ComicBook.vb`: zip-based CBZ and EPUB with .NET, RAR and 7z with SharpCompress, PDF with PDFium (Docnet.Core).
- `ComicCatalog/Data/` is the database and lookup code, with no Windows dependencies.
- `tests/` checks the database code and runs anywhere: `dotnet run --project desktop/tests/DataTests.vbproj`.
