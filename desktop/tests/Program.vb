Option Strict On

Imports System.Data
Imports System.IO
Imports ComicCatalog.Data

Module Program

    Private failures As Integer

    Private Sub Check(name As String, ok As Boolean, Optional detail As String = "")
        Console.WriteLine($"{If(ok, "PASS", "FAIL")}  {name}{If(detail <> "", "  (" & detail & ")", "")}")
        If Not ok Then failures += 1
    End Sub

    Function Main(args As String()) As Integer
        ' Barcodes
        Dim b = Barcode.Parse("76194134182800111")
        Check("barcode with 5-digit add-on", b.Upc = "761941341828" AndAlso b.Addon = "00111" AndAlso b.Issue = "1" AndAlso b.Cover = "1")
        Check("barcode typed with a space", Barcode.Parse("761941341828 00521").Issue = "5")
        Check("13-digit code with leading 0", Barcode.Parse("0761941341828").Upc = "761941341828")
        Check("ISBN", Barcode.Parse("9781401235420").Upc = "9781401235420" AndAlso Barcode.Parse("9781401235420").Addon = "")
        Check("issue sort", ComicDb.IssueSort("12").GetValueOrDefault() = 12 AndAlso ComicDb.IssueSort("½").GetValueOrDefault() = 0.5 AndAlso Not ComicDb.IssueSort("Annual").HasValue)

        Dim path = IO.Path.Combine(IO.Path.GetTempPath(), $"comics-test-{Guid.NewGuid():N}.db")
        Dim db As New ComicDb(path)
        Check("sample data loads", db.LoadSampleData())
        Check("sample data refuses a second load", Not db.LoadSampleData())
        Dim stats = db.GetStats()
        Check("stats", stats.Comics = 6 AndAlso stats.Copies = 7 AndAlso stats.TotalValue = 240, $"{stats.Comics} comics, {stats.Copies} copies, {stats.TotalValue}")

        Dim all = db.SearchCollection("")
        Check("list sorted by series", all.Rows.Count = 6 AndAlso CStr(all.Rows(0)("Series")) = "Batman", CStr(all.Rows(0)("Series")))
        Check("search by text", db.SearchCollection("dare").Rows.Count = 2)
        Check("search by issue", db.SearchCollection("168").Rows.Count = 1)

        Check("missing Daredevil issues", String.Join(",", db.MissingIssues("Daredevil", "")) = "169")
        Check("series with gaps", db.SeriesWithGaps().Rows.Count = 2)
        Check("wishlist", db.GetWishlist().Rows.Count = 2)

        ' Add a comic by barcode, as the app does after a lookup
        Dim id = db.SaveComic(New ComicRecord With {.Series = "Daredevil", .Issue = "169", .Publisher = "Marvel", .Barcode = "07114601690000169",
                                                    .Condition = "Fine", .PricePaid = 20, .CurrentValue = 25})
        Check("adding fills the gap", db.MissingIssues("Daredevil", "").Count = 0)
        Check("adding removes it from the wishlist", db.GetWishlist().Rows.Count = 1)
        Check("find by barcode", db.FindByBarcode("071146016900 00169")?.Issue = "169")
        Check("find by main barcode only", db.FindByBarcode("071146016900")?.Issue = "169")

        ' Adding the same comic again adds a copy
        db.SaveComic(New ComicRecord With {.Series = "Daredevil", .Issue = "169", .Quantity = 1}, addCopies:=True)
        Check("same comic again adds a copy", db.GetComic(id).Quantity = 2)

        ' Edit
        Dim c = db.GetComic(id)
        c.Title = "Elektra Lives"
        c.Notes = "Signed"
        db.SaveComic(c)
        Check("edit keeps one row", db.SearchCollection("daredevil").Rows.Count = 3 AndAlso db.GetComic(id).Title = "Elektra Lives")

        ' Delete
        db.DeleteComics({id})
        Check("delete", db.GetComic(id) Is Nothing AndAlso db.SearchCollection("daredevil").Rows.Count = 2)

        ' Import from the phone app's CSV export
        Dim csv = ChrW(&HFEFF) & "Series,Volume,Issue,Cover / variant,Format,Story title,Publisher,Cover date,Variant code,Printing,Copies,Condition,Notes,Barcode,Metron ID,Date added" & vbCrLf &
                  "Uncanny X-Men,,141,,,""Days of Future Past"",Marvel,1981-01,,,1,Fine,""Has a crease, back cover"",,," & "2026-10-03" & vbCrLf &
                  "The Amazing Spider-Man,Vol. 7,47,Cover A,,,Marvel,2024-06,A,,2,,'-note,759606202517 04711,12345,2026-10-03" & vbCrLf &
                  "Saga,,1,,,Chapter One,Image,2012-03,,,1,,,,," & vbCrLf
        Dim r1 = db.ImportPhoneCsv(csv)
        Check("phone import adds new comics, skips one already there", r1.Added = 2 AndAlso r1.Skipped = 1, $"{r1.Added} added, {r1.Skipped} skipped")
        Dim r2 = db.ImportPhoneCsv(csv)
        Check("phone import again skips them", r2.Added = 0 AndAlso r2.Skipped = 3, $"{r2.Added} added, {r2.Skipped} skipped")
        Dim spidey = db.FindByBarcode("75960620251704711")
        Check("imported details", spidey IsNot Nothing AndAlso spidey.Volume = "Vol. 7" AndAlso spidey.Quantity = 2 AndAlso spidey.CoverLetter = "A" AndAlso spidey.MetronId.GetValueOrDefault() = 12345 AndAlso spidey.Notes = "-note")
        Dim xmen = db.SearchCollection("crease")
        Check("quoted comma in notes", xmen.Rows.Count = 1)
        Check("'The' ignored when sorting", db.SearchCollection("").Rows.Cast(Of DataRow)().Select(Function(rw) CStr(rw("Series"))).ToList().IndexOf("The Amazing Spider-Man") = 0)

        Try
            db.ImportPhoneCsv("a,b" & vbLf & "1,2")
            Check("rejects other CSVs", False)
        Catch ex As InvalidDataException
            Check("rejects other CSVs", True)
        End Try

        ' Metron lookups through a stand-in relay
        Dim port = 18000 + New Random().Next(1000)
        Using listener As New Net.HttpListener()
            listener.Prefixes.Add($"http://localhost:{port}/")
            listener.Start()
            Dim seen As New List(Of String)
            Dim serve = Threading.Tasks.Task.Run(Sub()
                For i = 1 To 4
                    Dim ctx = listener.GetContext()
                    seen.Add(ctx.Request.Url.PathAndQuery)
                    Dim body = If(ctx.Request.Url.AbsolutePath = "/ping", "{""ok"":true,""version"":2}",
                               If(ctx.Request.Url.AbsolutePath.StartsWith("/upc/"),
                                  "{""results"":[{""metronId"":42,""series"":""Batman"",""volume"":""2016"",""number"":""1"",""title"":""I Am Gotham"",""publisher"":""DC Comics"",""coverDate"":""2016-08"",""coverUrl"":""https://example.com/c.jpg"",""price"":""3.99""}]}",
                                  "{""results"":[]}"))
                    Dim bytes = Text.Encoding.UTF8.GetBytes(body)
                    ctx.Response.ContentType = "application/json"
                    ctx.Response.OutputStream.Write(bytes, 0, bytes.Length)
                    ctx.Response.Close()
                Next
            End Sub)
            Dim metron As New MetronClient($"http://localhost:{port}/")
            metron.TestAsync().GetAwaiter().GetResult()
            Check("relay test", True)
            Dim found = metron.LookupBarcodeAsync(Barcode.Parse("761941341828 00111")).GetAwaiter().GetResult()
            Check("barcode lookup", found.Count = 1 AndAlso found(0).MetronId = 42 AndAlso found(0).Series = "Batman" AndAlso found(0).CoverDate = "2016-08" AndAlso found(0).Price.GetValueOrDefault() = 3.99)
            Dim none = metron.SearchAsync("Nothing Comics", "1").GetAwaiter().GetResult()
            Check("title search with no results", none.Count = 0)
            Check("relay version", metron.RelayVersionAsync().GetAwaiter().GetResult() = 2)
            serve.Wait(5000)
            Check("relay paths", seen.Count = 4 AndAlso seen(1) = "/upc/76194134182800111?issue=1&v=5" AndAlso seen(2) = "/search?series=Nothing%20Comics&number=1&v=5" AndAlso seen(3) = "/ping", String.Join(" ", seen))
        End Using
        ' Scans sent from the phone: collect a batch, then remove it from the mailbox
        Dim inboxPort = port + 1000
        Using listener As New Net.HttpListener()
            listener.Prefixes.Add($"http://localhost:{inboxPort}/")
            listener.Start()
            Dim seen As New List(Of String)
            Dim serve = Threading.Tasks.Task.Run(Sub()
                For i = 1 To 2
                    Dim ctx = listener.GetContext()
                    seen.Add($"{ctx.Request.HttpMethod} {ctx.Request.Url.AbsolutePath}")
                    Dim body = If(ctx.Request.HttpMethod = "GET",
                        "{""ok"":true,""batches"":[{""id"":""1700000000000-ab12cd34"",""csv"":""\ufeffSeries,Volume,Issue,Copies,Barcode\r\nInvincible,2003,1,1,607396624008 00111\r\nBatman,2016,1,1,\r\n""}]}",
                        "{""ok"":true}")
                    Dim bytes = Text.Encoding.UTF8.GetBytes(body)
                    ctx.Response.ContentType = "application/json"
                    ctx.Response.OutputStream.Write(bytes, 0, bytes.Length)
                    ctx.Response.Close()
                Next
            End Sub)
            Dim relay As New MetronClient($"http://localhost:{inboxPort}")
            Dim batches = relay.InboxAsync("abcd-efgh-jklm-npqr").GetAwaiter().GetResult()
            Check("mailbox batches read", batches.Count = 1 AndAlso batches(0).Csv.Contains("Invincible"))
            Dim got = db.ImportPhoneCsv(batches(0).Csv)
            Check("scans from the phone are added, ones already here skipped", got.Added = 1 AndAlso got.Skipped = 1, $"{got.Added} added, {got.Skipped} skipped")
            relay.DeleteInboxAsync("abcd-efgh-jklm-npqr", batches(0).Id).GetAwaiter().GetResult()
            serve.Wait(5000)
            Check("mailbox paths", seen.Count = 2 AndAlso seen(0) = "GET /inbox/ABCDEFGHJKLMNPQR" AndAlso seen(1) = "DELETE /inbox/ABCDEFGHJKLMNPQR/1700000000000-ab12cd34", String.Join(" | ", seen))
            Dim inv = db.SearchCollection("invincible")
            Check("phone scan has its barcode", inv.Rows.Count = 1 AndAlso db.GetComic(Convert.ToInt64(inv.Rows(0)("comic_id"))).Barcode = "60739662400800111")
            db.DeleteComics({Convert.ToInt64(inv.Rows(0)("comic_id"))})
        End Using

        Try
            Call New MetronClient("").LookupBarcodeAsync(Barcode.Parse("761941341828")).GetAwaiter().GetResult()
            Check("lookup without relay explains", False)
        Catch ex As InvalidOperationException
            Check("lookup without relay explains", ex.Message.Contains("Settings"))
        End Try

        ' Covers
        Dim needing = db.ComicsNeedingCovers(False)
        Check("comics needing covers", needing.Count = db.GetStats().Comics, $"{needing.Count}")
        Dim withId = needing.First(Function(x) x.MetronId.HasValue)
        Check("comics with a Metron number come first", needing(0).MetronId.HasValue AndAlso withId.Series = "The Amazing Spider-Man")
        db.SetCover(withId.ComicId, "https://example.com/asm.jpg", Nothing)
        Dim gone = db.ComicsNeedingCovers(False)
        Check("found cover is saved", db.GetComic(withId.ComicId).CoverUrl = "https://example.com/asm.jpg" AndAlso db.GetComic(withId.ComicId).MetronId.GetValueOrDefault() = 12345)
        db.SetCover(gone(0).ComicId, "", Nothing)
        Check("not-found comics aren't looked for again", db.ComicsNeedingCovers(False).Count = needing.Count - 2)
        Check("comics with a cover still need a price", db.ComicsNeedingCovers(True).Count = needing.Count)
        db.SetCover(withId.ComicId, "https://example.com/other.jpg", Nothing, 3.99, True)
        Dim priced = db.GetComic(withId.ComicId)
        Check("cover price is saved, cover kept", priced.CoverPrice.GetValueOrDefault() = 3.99 AndAlso priced.CoverUrl = "https://example.com/asm.jpg")
        Check("priced comics aren't looked for again", db.ComicsNeedingCovers(True).Count = needing.Count - 1)
        Check("cover price in the list", db.SearchCollection("").Rows.Cast(Of DataRow)().Any(Function(rw) Not rw.IsNull("Cover price")))
        priced.CoverPrice = 4.99
        db.SaveComic(priced)
        Check("cover price can be edited", db.GetComic(withId.ComicId).CoverPrice.GetValueOrDefault() = 4.99)
        ' Story arcs and runs valued as a set
        Dim bats = db.SearchCollection("batman").Rows.Cast(Of DataRow)().Select(Function(rw) Convert.ToInt64(rw("comic_id"))).ToList()
        Dim setId = db.SaveSet("Batman: I Am Gotham", bats, 300)
        Dim batRows = db.SearchCollection("batman").Rows.Cast(Of DataRow)().ToList()
        Check("set value is split across its comics", bats.Count = 3 AndAlso bats.All(Function(cid) db.GetComic(cid).CurrentValue.GetValueOrDefault() = 100))
        Check("set shown in the list and found by search", CStr(batRows(0)("Set")) = "Batman: I Am Gotham" AndAlso db.SearchCollection("i am gotham").Rows.Count = 3)
        Dim sets = db.GetSets()
        Check("sets listed with counts", sets.Rows.Count = 1 AndAlso Convert.ToInt32(sets.Rows(0)("Comics")) = 3 AndAlso CStr(sets.Rows(0)("Series")) = "Batman")
        db.SaveSet("Batman #1 alone", {bats(0)}, Nothing)
        Check("moving a comic re-splits the old set", db.GetComic(bats(1)).CurrentValue.GetValueOrDefault() = 150 AndAlso db.GetSets().Rows.Count = 2)
        db.UpdateSet(setId, "Gotham", 400)
        Check("changing a set's value re-splits it", db.GetComic(bats(2)).CurrentValue.GetValueOrDefault() = 200 AndAlso db.SetNames().Contains("Gotham"))
        db.RemoveFromSets({bats(0)})
        Check("empty sets are tidied away", Not db.SetNames().Contains("Batman #1 alone"))
        db.DeleteSet(setId)
        Check("removing a set keeps the comics and values", db.GetSets().Rows.Count = 0 AndAlso db.GetComic(bats(2)).CurrentValue.GetValueOrDefault() = 200)

        ' Graded (slabbed) comics
        Dim slab = db.GetComic(withId.ComicId)
        slab.GradedBy = "CGC" : slab.Grade = "9.8" : slab.GradeLabel = "Universal (blue)" : slab.CertNumber = "1234567001"
        db.SaveComic(slab)
        Dim slabbed = db.GetComic(withId.ComicId)
        Check("grading is saved", slabbed.GradedBy = "CGC" AndAlso slabbed.Grade = "9.8" AndAlso slabbed.GradeLabel = "Universal (blue)" AndAlso slabbed.CertNumber = "1234567001")
        Dim cgcRows = db.SearchCollection("cgc")
        Check("graded comics found by searching CGC, shown with their grade", cgcRows.Rows.Count = 1 AndAlso CStr(cgcRows.Rows(0)("Condition")) = "CGC 9.8")
        Dim picks As New List(Of MetronIssue) From {
            New MetronIssue With {.Number = "1", .CoverDate = "1940-04"}, New MetronIssue With {.Number = "1", .CoverDate = "2011-11"},
            New MetronIssue With {.Number = "10", .CoverDate = "2012-08"}}
        Check("title match picks by cover date", CoverFinder.PickTitleMatch(New ComicRecord With {.Issue = "1", .CoverDate = "2011-11"}, picks)?.CoverDate = "2011-11")
        Check("title match gives up when unsure", CoverFinder.PickTitleMatch(New ComicRecord With {.Issue = "1"}, picks) Is Nothing)
        Check("title match ignores leading zeros", CoverFinder.PickTitleMatch(New ComicRecord With {.Issue = "010"}, picks)?.Number = "10")

        ' Read or not read
        Dim readIds = db.SearchCollection("batman").Rows.Cast(Of DataRow)().Select(Function(rw) Convert.ToInt64(rw("comic_id"))).ToList()
        db.SetRead(readIds.Take(2), True)
        Check("marking read", db.GetComic(readIds(0)).IsRead AndAlso Not db.GetComic(readIds(2)).IsRead AndAlso db.GetStats().Read = 2)
        Check("read shown in the list", CStr(db.SearchCollection("batman").Rows(0)("Read")) = "✓")
        Check("filter read and unread", db.SearchCollection("", True).Rows.Count = 2 AndAlso db.SearchCollection("", False).Rows.Count = db.GetStats().Comics - 2)
        Dim reread = db.GetComic(readIds(2))
        reread.IsRead = True
        db.SaveComic(reread)
        Check("read saved from the edit window", db.GetComic(readIds(2)).IsRead)
        db.SetRead({readIds(2)}, False)
        Check("marking unread", Not db.GetComic(readIds(2)).IsRead)

        ' Insurance list
        Dim xlsx = IO.Path.Combine(IO.Path.GetTempPath(), $"comics-{Guid.NewGuid():N}.xlsx")
        Dim pdf = IO.Path.ChangeExtension(xlsx, ".pdf")
        Exporter.ToExcel(db, xlsx)
        Using book As New ClosedXML.Excel.XLWorkbook(xlsx)
            Dim sheet = book.Worksheet("Collection")
            Check("Excel list has every comic", sheet.Cell(1, 1).GetString() = "Series" AndAlso sheet.Column(1).CellsUsed().Count() = db.GetStats().Comics + 2,
                  $"{sheet.Column(1).CellsUsed().Count()}")
            Check("Excel summary has the total value", book.Worksheet("Summary").Cell(6, 2).GetDouble() = db.GetStats().TotalValue)
        End Using
        Exporter.ToPdf(db, pdf)
        Dim pdfHead = File.ReadAllBytes(pdf).Take(4).ToArray()
        Check("PDF list is made", System.Text.Encoding.ASCII.GetString(pdfHead) = "%PDF" AndAlso New FileInfo(pdf).Length > 2000, $"{New FileInfo(pdf).Length} bytes")
        If args.Length > 1 Then File.Copy(pdf, args(1), overwrite:=True)
        File.Delete(xlsx)
        File.Delete(pdf)

        ' Backups
        Dim backupDir = IO.Path.Combine(IO.Path.GetTempPath(), $"comic-backups-{Guid.NewGuid():N}")
        Directory.CreateDirectory(backupDir)
        For d = 1 To Backups.KeepCount + 2
            File.WriteAllText(IO.Path.Combine(backupDir, $"comics-2020-01-{d:00}.db"), "old")
        Next
        Dim backupFile = Backups.MakeBackup(db, backupDir)
        Check("backup made and old ones tidied", File.Exists(backupFile) AndAlso Directory.GetFiles(backupDir, "comics-*.db").Length = Backups.KeepCount AndAlso
              Not File.Exists(IO.Path.Combine(backupDir, "comics-2020-01-01.db")))
        Dim comicsBefore = db.GetStats().Comics
        db.DeleteComics({readIds(0)})
        db.RestoreFrom(backupFile)
        Check("restore brings deleted comics back", db.GetStats().Comics = comicsBefore AndAlso db.GetComic(readIds(0)) IsNot Nothing)
        Try
            db.RestoreFrom(IO.Path.Combine(backupDir, "comics-2020-01-30.db"))
            Check("restore refuses a file that isn't a backup", False)
        Catch ex As InvalidDataException
            Check("restore refuses a file that isn't a backup", db.GetStats().Comics = comicsBefore)
        End Try
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools()
        Directory.Delete(backupDir, True)

        ' New releases
        Dim follow = db.SeriesToFollow()
        Check("series to follow listed", follow.Rows.Count > 0 AndAlso follow.Columns.Contains("Follow"))
        Dim batmanSeries = follow.Rows.Cast(Of DataRow)().Where(Function(rw) CStr(rw("Series")) = "Batman").Select(Function(rw) Convert.ToInt64(rw("id"))).ToList()
        db.SetFollow(batmanSeries, True)
        Check("following a series", db.FollowedSeriesNames().Contains("Batman"))
        db.SetFollow(batmanSeries, False)
        Check("not following a series", Not db.FollowedSeriesNames().Contains("Batman"))
        Dim soon = DateTime.Today.AddDays(5).ToString("yyyy-MM-dd")
        Dim upcoming = ReleaseFinder.MatchingSeries({
            New MetronIssue With {.MetronId = 900, .Series = "Batman", .Volume = "2016", .Number = "160", .StoreDate = soon},
            New MetronIssue With {.MetronId = 901, .Series = "Batman Beyond", .Number = "5", .StoreDate = soon},
            New MetronIssue With {.MetronId = 902, .Series = "Batman", .Number = "1", .StoreDate = "1940-04-25"},
            New MetronIssue With {.MetronId = 903, .Series = "batman", .Number = "161", .StoreDate = ""}},
            "Batman", DateTime.Today.AddDays(-14).ToString("yyyy-MM-dd")).ToList()
        Check("releases keep the exact series and recent dates", upcoming.Count = 1 AndAlso upcoming(0).MetronId = 900)
        db.SaveReleases(upcoming, True)
        db.AddWish("Batman", "160", 2, Nothing, "")
        Dim rel = db.GetReleases()
        Check("releases listed, with wishlist status", rel.Rows.Count = 1 AndAlso CStr(rel.Rows(0)("Status")) = "On wishlist" AndAlso CStr(rel.Rows(0)("In shops")) = soon)
        db.SaveReleases({New MetronIssue With {.MetronId = 904, .Series = "Batman", .Number = "162", .StoreDate = soon}}, False)
        Check("a partial check keeps earlier releases", db.GetReleases().Rows.Count = 2)
        db.SaveReleases(Array.Empty(Of MetronIssue)(), True)
        Check("a full check replaces them", db.GetReleases().Rows.Count = 0)

        ' Story arcs found on Metron
        Dim arcBats = db.SearchCollection("batman").Rows.Cast(Of DataRow)().Select(Function(rw) Convert.ToInt64(rw("comic_id"))).ToList()
        For i = 0 To arcBats.Count - 1
            db.SetCover(arcBats(i), "", 5000 + i)
        Next
        Dim needArcs = db.ComicsNeedingArcs()
        Check("comics needing arcs have Metron numbers", needArcs.Count >= 3 AndAlso needArcs.Any(Function(x) x.MetronId = 5001))
        Dim yearOne As New MetronArc With {.Id = 77, .Name = "I Am Gotham"}
        db.SaveArcs(arcBats(0), {yearOne, New MetronArc With {.Id = 78, .Name = "Solo story"}})
        db.SaveArcs(arcBats(1), {yearOne})
        db.SaveArcs(arcBats(2), Array.Empty(Of MetronArc)())
        Check("checked comics aren't looked up again", db.ComicsNeedingArcs().Count = needArcs.Count - 3)
        Check("only arcs with two or more owned issues", db.OwnedArcs().Count = 1 AndAlso db.OwnedArcs()(0).Owned = 2)
        Check("arc sets made", db.MakeArcSets(New Dictionary(Of Long, Integer) From {{77, 6}}) = 1)
        Dim arcSet = db.GetSets().Rows(0)
        Check("arc set has its comics and the arc's length", CStr(arcSet("Set")) = "I Am Gotham" AndAlso Convert.ToInt32(arcSet("Comics")) = 2 AndAlso Convert.ToInt32(arcSet("Whole arc")) = 6)
        db.SaveArcs(arcBats(2), {yearOne})
        Check("running again adds newly found issues, no duplicate set", db.MakeArcSets(Nothing) = 0 AndAlso db.GetSets().Rows.Count = 1 AndAlso
              Convert.ToInt32(db.GetSets().Rows(0)("Comics")) = 3 AndAlso Convert.ToInt32(db.GetSets().Rows(0)("Whole arc")) = 6)
        Dim arcSetId = Convert.ToInt64(db.GetSets().Rows(0)("id"))
        Check("eBay words for a set", db.SetSearchWords(arcSetId) = "Batman I Am Gotham 1-4", db.SetSearchWords(arcSetId))
        db.DeleteSet(arcSetId)

        ' Missing issues of an arc go on the wishlist under the arc
        Dim wishBefore = db.GetWishlist().Rows.Count
        Dim arcList As New List(Of MetronIssue) From {
            New MetronIssue With {.MetronId = 5000, .Series = "Batman", .Volume = "2016", .Number = "1"},
            New MetronIssue With {.MetronId = 0, .Series = "Batman", .Volume = "2016", .Number = "2"},
            New MetronIssue With {.MetronId = 6003, .Series = "Batman", .Volume = "2016", .Number = "3"},
            New MetronIssue With {.MetronId = 6005, .Series = "Batman", .Volume = "2016", .Number = "5"},
            New MetronIssue With {.MetronId = 6006, .Series = "Detective Comics", .Volume = "2016", .Number = "934"}}
        Check("missing arc issues added to wishlist", db.WishMissingFromArc("I Am Gotham", arcList) = 3)
        Dim wishRows = db.GetWishlist().Rows.Cast(Of DataRow)().Where(Function(rw) CStr(rw("Story arc")) = "I Am Gotham").ToList()
        Check("wishlist shows the arc", wishRows.Count = 3 AndAlso db.GetWishlist().Rows.Count = wishBefore + 3)
        Check("running again adds nothing twice", db.WishMissingFromArc("I Am Gotham", arcList) = 0)

        ' What the phone is sent: owned comics and the wishlist, the same text each time until something changes
        Dim snap = db.LibrarySnapshot()
        Using doc = Text.Json.JsonDocument.Parse(snap)
            Dim owned = doc.RootElement.GetProperty("owned")
            Dim wish = doc.RootElement.GetProperty("wishlist")
            Check("library snapshot lists owned comics", owned.GetArrayLength() = db.GetStats().Comics, $"{owned.GetArrayLength()}")
            Check("library snapshot lists the wishlist with arcs", wish.GetArrayLength() = db.GetWishlist().Rows.Count AndAlso
                  wish.EnumerateArray().Any(Function(w) w(2).GetString() = "I Am Gotham"))
            Check("library snapshot rows have 5 parts", owned.EnumerateArray().All(Function(o) o.GetArrayLength() = 5))
        End Using
        Check("library snapshot is stable", db.LibrarySnapshot() = snap)

        ' Updates: GitHub's answer about the newest release
        Dim release = Updater.ParseRelease("{""tag_name"":""desktop-42"",""body"":""Voice and sync\n"",""assets"":[{""name"":""ComicCatalog-windows.zip"",""browser_download_url"":""https://x/zip""},{""name"":""ComicCatalog.exe"",""browser_download_url"":""https://github.com/viksr-dev/comic-collection/releases/download/desktop-42/ComicCatalog.exe""}]}")
        Check("release parsed", release IsNot Nothing AndAlso release.Build = 42 AndAlso release.DownloadUrl.EndsWith("/desktop-42/ComicCatalog.exe") AndAlso release.Notes = "Voice and sync")
        Check("download from anywhere else refused", Updater.ParseRelease("{""tag_name"":""desktop-43"",""assets"":[{""name"":""ComicCatalog.exe"",""browser_download_url"":""https://evil.example/ComicCatalog.exe""}]}") Is Nothing)
        Check("other releases ignored", Updater.ParseRelease("{""tag_name"":""v1.0"",""assets"":[]}") Is Nothing)
        Check("release without the exe ignored", Updater.ParseRelease("{""tag_name"":""desktop-7"",""assets"":[]}") Is Nothing)

        ' An older database without the cover_checked column gets it added
        Dim oldPath = IO.Path.Combine(IO.Path.GetTempPath(), $"comics-old-{Guid.NewGuid():N}.db")
        Using conn As New Microsoft.Data.Sqlite.SqliteConnection($"Data Source={oldPath}")
            conn.Open()
            Using cmd = conn.CreateCommand()
                cmd.CommandText = "CREATE TABLE comics (id INTEGER PRIMARY KEY, series_id INTEGER NOT NULL, issue_number TEXT NOT NULL DEFAULT '', issue_sort REAL, variant TEXT NOT NULL DEFAULT '', variant_name TEXT NOT NULL DEFAULT '', title TEXT NOT NULL DEFAULT '', cover_date TEXT NOT NULL DEFAULT '', format TEXT NOT NULL DEFAULT '', barcode TEXT NOT NULL DEFAULT '', metron_id INTEGER, cover_url TEXT NOT NULL DEFAULT '', UNIQUE (series_id, issue_number, variant, variant_name))"
                cmd.ExecuteNonQuery()
            End Using
        End Using
        Dim oldDb As New ComicDb(oldPath)
        oldDb.LoadSampleData()
        Check("older database is upgraded", oldDb.ComicsNeedingCovers().Count = 6 AndAlso oldDb.SearchCollection("").Columns.Contains("Cover price") AndAlso oldDb.GetComic(Convert.ToInt64(oldDb.SearchCollection("").Rows(0)("comic_id"))).GradedBy = "")
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools()
        File.Delete(oldPath)

        ' The file can be read by the plain SQL queries too
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools()
        If args.Length > 0 Then File.Copy(path, args(0), overwrite:=True)
        File.Delete(path)

        Console.WriteLine(If(failures = 0, "All checks passed.", $"{failures} check(s) failed."))
        Return If(failures = 0, 0, 1)
    End Function

End Module
