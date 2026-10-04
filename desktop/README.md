# Comic Catalog for Windows

A Visual Basic desktop app for your comic collection, with its own SQLite database.
Type in (or scan) a barcode and it pulls up the issue from Metron, the same way the phone app does.

## What it does

- **Collection**: everything you own, with search. Type a barcode and press Enter to add a comic. If you already have it, it offers to add another copy. Double-click a comic to edit it. Select several (hold Ctrl or Shift) and press Delete to remove them.
- **Barcode lookup**: fills in series, issue, story title, publisher, cover date and the cover picture. If Metron doesn't know the barcode, use "Search by series and issue" in the add window.
- **Missing issues**: series with gaps between the first and last issue you own, and exactly which issues are missing. You can send missing issues straight to your wishlist.
- **Wishlist**: comics you want, with a priority and the most you'd pay. A comic comes off the list when you add it to your collection.
- **Totals** at the bottom: comics, copies, total value and total paid.
- **Import** the spreadsheet exported from the phone app, so you don't have to type in your collection again.
- **Find covers and prices**: fetches cover pictures and original cover prices for comics that don't have them yet (for example ones imported from the phone app). It goes slowly to stay within Metron's limits; click it again to stop, and it carries on later. Cover prices need the updated relay (see [Updating the relay](../relay/README.md#updating-the-relay)).
- **Scans from your phone**: turn on *Send scans to your computer* in the phone app's Settings and type its code into Settings here. While the app is open, new scans arrive every couple of minutes (or click *Get scans from phone*). The relay needs a one-time setup for this; see [Sending scans to your computer](../relay/README.md#sending-scans-to-your-computer).
- **Graded comics**: in the edit window, set *Graded (slab)* to CGC, CBCS or PGX and fill in the grade, label and certificate number. The list shows "CGC 9.8" in the Condition column, searching "cgc" finds them, and *Verify* opens CGC's certificate lookup.
- **Check value on eBay** (in the edit window): opens eBay's sold listings for that issue, so you can see what copies actually sold for and type a value in.
- **Banner picture**: pick any picture from your computer (Settings) to show across the top. It stays on your computer.

A USB barcode scanner works too. Click in the Barcode box and scan, because the scanner types the digits and presses Enter for you.

## Getting the app

### Easiest: download it ready to run

1. On GitHub, open the repository and click the **Actions** tab.
2. Click the latest **Desktop app** run with a green tick.
3. Scroll down to **Artifacts** and click **ComicCatalog-windows** to download a zip.
4. Unzip it and double-click **ComicCatalog.exe**. If Windows says "Windows protected your PC", click **More info**, then **Run anyway**. It says that because the app isn't signed, not because something is wrong.

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
| `schema.sql` | Builds the tables: publishers, series, comics, creators, collection, wishlist, and a `v_collection` view that joins them |
| `sample-data.sql` | A few sample comics, creators and wishlist entries |
| `sample-comics.db` | A ready-made database with the sample comics in it |
| `queries.sql` | Handy queries: total value, value by series, missing issues in a run, series with gaps, wishlist, comics worth more than you paid, top creators |

You can open any of the `.db` files in [DB Browser for SQLite](https://sqlitebrowser.org/) (free) to look around or run the queries yourself.

To build a fresh database by hand: `sqlite3 comics.db < schema.sql`, then `sqlite3 comics.db < sample-data.sql` if you want the samples.

## For developers

- `ComicCatalog/` is the WinForms app (.NET 8, Visual Basic). The screens are built in code, in `MainForm.vb`, `ComicForm.vb` and `WishForm.vb`.
- `ComicCatalog/Data/` is the database and lookup code, with no Windows dependencies.
- `tests/` checks the database code and runs anywhere: `dotnet run --project desktop/tests/DataTests.vbproj`.
