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
            Check("relay paths", seen.Count = 4 AndAlso seen(1) = "/upc/76194134182800111?issue=1&v=8" AndAlso seen(2) = "/search?series=Nothing%20Comics&number=1&v=8" AndAlso seen(3) = "/ping", String.Join(" ", seen))
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
            Check("library snapshot rows have 8 parts", owned.EnumerateArray().All(Function(o) o.GetArrayLength() = 8))
        End Using
        Check("library snapshot is stable", db.LibrarySnapshot() = snap)

        ' Writers and artists come with full issue details from relay version 8
        Using doc = System.Text.Json.JsonDocument.Parse("{""metronId"":9,""series"":""Daredevil"",""number"":""1"",""credits"":[{""name"":""Frank Miller"",""roles"":[""Writer"",""Artist""]},{""name"":""Lynn Varley"",""roles"":[""Colorist""]},{""roles"":[""Writer""]}]}")
            Dim parsed = MetronClient.ToIssue(doc.RootElement)
            Check("credits parsed", parsed.Credits.Count = 2 AndAlso parsed.Credits(0).Name = "Frank Miller" AndAlso parsed.Credits(0).Roles.Count = 2)
        End Using
        Dim extraPath = IO.Path.Combine(IO.Path.GetTempPath(), $"comics-extra-{Guid.NewGuid():N}.db")
        Dim xdb As New ComicDb(extraPath)
        Dim ids As New Dictionary(Of String, Long)
        For Each spec In {"Saga|1|2012-03|Image|1", "Saga|2|2012-04|Image|1", "Saga|3|2012-05|Image|0", "Saga|5|2012-08|Image|0",
                          "Batman|404|1987-02|DC|1", "Batman|405|1987-03|DC|0", "Batman|407|1987-05|DC|0",
                          "Watchmen|1|1986-09|DC|0", "Watchmen|2|1986-10|DC|0", "Hellboy|1||Dark Horse|0"}
            Dim f = spec.Split("|"c)
            Dim cid = xdb.SaveComic(New ComicRecord With {.Series = f(0), .Issue = f(1), .CoverDate = f(2), .Publisher = f(3), .CurrentValue = 10, .PricePaid = 4})
            ids($"{f(0)} {f(1)}") = cid
            If f(4) = "1" Then xdb.SetRead({cid}, True)
        Next
        ' Another cover of Saga #2 you haven't read doesn't count as unread.
        xdb.SaveComic(New ComicRecord With {.Series = "Saga", .Issue = "2", .VariantName = "Variant", .CoverDate = "2012-04", .Publisher = "Image"})

        ' Stats
        Dim pubs = xdb.StatsBy("publisher")
        Check("stats by publisher", pubs(0).Label = "DC" AndAlso pubs(0).Count = 5 AndAlso pubs(0).Value = 50, $"{pubs(0).Label} {pubs(0).Count} {pubs(0).Value}")
        Dim decades = xdb.StatsBy("decade")
        Check("stats by decade, in order, unknown last", String.Join(",", decades.Select(Function(d) $"{d.Label}={d.Count}")) = "1980s=5,2010s=5,Unknown=1",
              String.Join(",", decades.Select(Function(d) $"{d.Label}={d.Count}")))
        Check("stats by series", xdb.StatsBy("series", 2).Select(Function(d) d.Label).SequenceEqual({"Saga", "Batman"}))
        Check("most valuable comics", xdb.StatsBy("value", 3).Count = 3 AndAlso xdb.StatsBy("value", 3)(0).Value = 10)
        Check("no writers until looked up", xdb.StatsBy("writer").Count = 0)
        xdb.SetCover(ids("Watchmen 1"), "", 101)
        xdb.SetCover(ids("Watchmen 2"), "", 102)
        Check("credits are looked up with arcs", xdb.ComicsNeedingArcs(withCredits:=True).Count = 2)
        Dim moore As New MetronCredit With {.Name = "Alan Moore", .Roles = New List(Of String) From {"Writer"}}
        Dim gibbons As New MetronCredit With {.Name = "Dave Gibbons", .Roles = New List(Of String) From {"Artist", "Letterer"}}
        xdb.SaveArcs(ids("Watchmen 1"), Array.Empty(Of MetronArc)(), {moore, gibbons})
        Check("arcs done but credits still wanted", xdb.ComicsNeedingArcs(withCredits:=False).Count = 1 AndAlso xdb.ComicsNeedingArcs(withCredits:=True).Count = 1)
        xdb.SaveArcs(ids("Watchmen 2"), Array.Empty(Of MetronArc)())
        Dim leftover = xdb.ComicsNeedingArcs(withCredits:=True)
        Check("comic needing only credits is marked so", leftover.Count = 1 AndAlso Not leftover(0).NeedsArcs)
        xdb.SaveArcs(ids("Watchmen 2"), Array.Empty(Of MetronArc)(), {moore, gibbons})
        Check("stats by writer", xdb.StatsBy("writer").Count = 1 AndAlso xdb.StatsBy("writer")(0).Label = "Alan Moore" AndAlso xdb.StatsBy("writer")(0).Count = 2)
        Check("stats by artist", xdb.StatsBy("artist").Count = 1 AndAlso xdb.StatsBy("artist")(0).Label = "Dave Gibbons")
        Check("credits checked count", xdb.CreditsChecked().Checked = 2 AndAlso xdb.CreditsChecked().Matched = 2)

        ' Value history
        xdb.RecordValueHistory(New Date(2026, 10, 1))
        xdb.SaveComic(New ComicRecord With {.Series = "Spawn", .Issue = "1", .CurrentValue = 30})
        xdb.RecordValueHistory(New Date(2026, 10, 2))
        xdb.RecordValueHistory(New Date(2026, 10, 2))
        Dim hist = xdb.ValueHistory()
        Check("value history, one per day", hist.Count = 2 AndAlso hist(0).TotalValue = 100 AndAlso hist(1).TotalValue = 130 AndAlso hist(1).Comics = 12,
              String.Join(",", hist.Select(Function(h) $"{h.Day:yyyy-MM-dd}={h.TotalValue}/{h.Comics}")))

        ' What to read next
        Dim readPicks = ReadNext.Suggest(xdb.ReadingItems(), New Random(1))
        Dim pickText = String.Join(" | ", readPicks.Select(Function(p) p.Item.ToString()))
        Check("read next: next issues of started runs first", readPicks.Take(2).Select(Function(p) p.Item.ToString()).OrderBy(Function(t) t).SequenceEqual({"Batman #405", "Saga #3"}), pickText)
        Check("read next: then new series, one each", readPicks.Count = 5 AndAlso readPicks.Skip(2).Select(Function(p) p.Item.ToString()).OrderBy(Function(t) t).SequenceEqual({"Hellboy #1", "Spawn #1", "Watchmen #1"}), pickText)
        Check("read next says why", readPicks.First(Function(p) p.Item.Series = "Batman").Reason.Contains("#404"))
        xdb.SetRead({ids("Saga 3")}, True)
        Dim sagaNext = ReadNext.Suggest(xdb.ReadingItems(), New Random(2)).First(Function(p) p.Item.Series = "Saga")
        Check("read next skips a gap and says so", sagaNext.Item.Issue = "5" AndAlso sagaNext.Reason.Contains("in between"), sagaNext.Reason)

        ' Lending
        xdb.LendComics({ids("Batman 404"), ids("Batman 405")}, "Dave", New Date(2026, 10, 1))
        Check("lent comics listed", xdb.GetLoans(False).Rows.Count = 2 AndAlso xdb.LentOutCount() = 2)
        Check("lent to shows in the collection", xdb.SearchCollection("Dave").Rows.Count = 2 AndAlso
              CStr(xdb.SearchCollection("Dave").Rows(0)("Lent to")) = "Dave")
        Dim loanId = Convert.ToInt64(xdb.GetLoans(False).Rows(0)("loan_id"))
        xdb.ReturnLoans({loanId}, New Date(2026, 10, 5))
        Check("given back", xdb.GetLoans(False).Rows.Count = 1 AndAlso xdb.GetLoans(True).Rows.Count = 2 AndAlso xdb.SearchCollection("Dave").Rows.Count = 1)
        xdb.LendComics({ids("Batman 405")}, "Sam", New Date(2026, 10, 6))
        Check("lending again closes the earlier loan", xdb.GetLoans(False).Rows.Count = 1 AndAlso CStr(xdb.GetLoans(False).Rows(0)("Lent to")) = "Sam")
        Check("borrowers, most recent first", xdb.Borrowers().SequenceEqual({"Sam", "Dave"}))

        ' Photos
        IO.Directory.CreateDirectory(xdb.PhotosFolder)
        Dim photoFile = IO.Path.Combine(xdb.PhotosFolder, "test-front.jpg")
        File.WriteAllText(photoFile, "not really a picture")
        Dim photoId = xdb.AddPhoto(ids("Hellboy 1"), photoFile, "Front")
        Check("photo saved", xdb.GetPhotos(ids("Hellboy 1")).Count = 1 AndAlso xdb.GetPhotos(ids("Hellboy 1"))(0).Caption = "Front" AndAlso xdb.AllPhotoFiles().Count = 1)
        xdb.SetPhotoCaption(photoId, "Signed")
        Check("photo caption changed", xdb.GetPhotos(ids("Hellboy 1"))(0).Caption = "Signed")
        xdb.DeleteComics({ids("Hellboy 1")})
        Check("deleting a comic deletes its photos", Not File.Exists(photoFile) AndAlso xdb.AllPhotoFiles().Count = 0)

        ' Share page
        Dim page = SharePage.Build(xdb, New ShareOptions With {.Title = "James's <comics>"}, New Date(2026, 10, 12))
        Check("share page has the collection and wishlist", page.Contains("<h3>Saga (") = False AndAlso page.Contains("<h3>Saga <span>5</span>") AndAlso
              page.Contains("id=""wishlist""") AndAlso page.Contains("12 October 2026"), page.Substring(0, Math.Min(400, page.Length)))
        Check("share page escapes text", page.Contains("James&#39;s &lt;comics&gt;") AndAlso Not page.Contains("<comics>"))
        Check("share page leaves out values unless asked", Not page.Contains("worth about") AndAlso Not page.Contains("$10.00"))
        Dim valued = SharePage.Build(xdb, New ShareOptions With {.IncludeWishlist = False, .ShowValues = True, .ShowCovers = False})
        Check("share page with values, no wishlist", valued.Contains("worth about $") AndAlso valued.Contains("$10.00") AndAlso Not valued.Contains("id=""wishlist""") AndAlso
              valued.Contains("class=""rows"""))
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools()
        File.Delete(extraPath)
        IO.Directory.Delete(xdb.PhotosFolder, recursive:=False)

        ' Boxes, trading, key issues, spending, reading goals, insurance report and pull list
        Dim morePath = IO.Path.Combine(IO.Path.GetTempPath(), $"comics-more-{Guid.NewGuid():N}.db")
        Dim mdb As New ComicDb(morePath)
        Dim m1 = mdb.SaveComic(New ComicRecord With {.Series = "X-Men", .Issue = "1", .PricePaid = 50, .PurchaseDate = "2026-09-03", .CurrentValue = 900,
                                                     .KeyNote = "1st appearance of the X-Men", .Location = "Box 10", .Condition = "Good"})
        Dim m2 = mdb.SaveComic(New ComicRecord With {.Series = "X-Men", .Issue = "2", .PricePaid = 20, .PurchaseDate = "2026-10-01", .CurrentValue = 30, .Quantity = 3})
        Dim m3 = mdb.SaveComic(New ComicRecord With {.Series = "Spawn", .Issue = "1", .PricePaid = 5, .PurchaseDate = "2026-10-11", .CurrentValue = 8,
                                                     .IsRead = True, .ReadOn = "2026-10-05"})
        Dim m4 = mdb.SaveComic(New ComicRecord With {.Series = "Spawn", .Issue = "2", .IsRead = True})
        Check("key note saved", mdb.GetComic(m1).KeyNote = "1st appearance of the X-Men" AndAlso mdb.KeyIssueCount() = 1)
        Check("box saved", mdb.GetComic(m1).Location = "Box 10")
        mdb.SetLocation({m2, m3}, "Box 9")
        Check("boxes in natural order", mdb.Locations().SequenceEqual({"Box 9", "Box 10"}), String.Join(",", mdb.Locations()))
        Check("search by box", mdb.SearchCollection("box 9").Rows.Count = 2)
        Check("search by key note", mdb.SearchCollection("appearance").Rows.Count = 1)
        Check("key issues filter", mdb.SearchCollection("", Nothing, "key").Rows.Count = 1)
        Check("collection shows key and box", CStr(mdb.SearchCollection("", Nothing, "key").Rows(0)("Box")) = "Box 10")
        Check("spares count as trade", mdb.SearchCollection("", Nothing, "trade").Rows.Count = 1 AndAlso
              CStr(mdb.SearchCollection("", Nothing, "trade").Rows(0)("Trade")) = "2 spare")
        mdb.SetForTrade({m4}, True, 12.5)
        Check("marked for trade", mdb.GetComic(m4).ForTrade AndAlso mdb.GetComic(m4).AskingPrice.GetValueOrDefault() = 12.5 AndAlso mdb.TradeRows().Rows.Count = 2)
        mdb.SetForTrade({m4}, False)
        Check("unmarked for trade", Not mdb.GetComic(m4).ForTrade AndAlso Not mdb.GetComic(m4).AskingPrice.HasValue AndAlso mdb.TradeRows().Rows.Count = 1)
        mdb.SetKeyNote({m2}, "Cameo")
        mdb.SetKeyNote({m2}, "")
        Check("key note cleared", mdb.KeyIssueCount() = 1)
        Check("key issues by value", mdb.StatsBy("keys")(0).Label.Contains("1st appearance") AndAlso mdb.StatsBy("keys")(0).Value = 900)

        ' Spending by Date bought
        Dim spend = mdb.SpendingByMonth(3, New Date(2026, 10, 12))
        Check("spending by month", spend.Count = 3 AndAlso spend(0).Label = "Aug 2026" AndAlso spend(0).Value = 0 AndAlso spend(1).Value = 50 AndAlso
              spend(2).Value = 25 AndAlso spend(2).Count = 2, String.Join(",", spend.Select(Function(s) $"{s.Label}={s.Value}/{s.Count}")))

        ' Reading: the day it's read is noted; imported reads without a day aren't counted
        Check("read day kept from the record", mdb.GetComic(m3).ReadOn = "2026-10-05" AndAlso mdb.GetComic(m4).ReadOn = "")
        mdb.SetRead({m1}, True, New Date(2026, 10, 12))
        mdb.SetRead({m3}, True, New Date(2026, 10, 12))
        Check("marking read again keeps the first day", mdb.GetComic(m3).ReadOn = "2026-10-05" AndAlso mdb.GetComic(m1).ReadOn = "2026-10-12")
        Dim reads = mdb.ReadByMonth(2, New Date(2026, 10, 12))
        Check("read this month", reads.Count = 2 AndAlso reads(1).Label = "Oct 2026" AndAlso reads(1).Count = 2, String.Join(",", reads.Select(Function(s) $"{s.Label}={s.Count}")))
        mdb.SetRead({m1}, False)
        Check("unread clears the day", mdb.GetComic(m1).ReadOn = "" AndAlso mdb.ReadByMonth(1, New Date(2026, 10, 12))(0).Count = 1)
        Dim edited = mdb.GetComic(m1)
        edited.IsRead = True
        edited.ReadOn = "2026-10-12"
        mdb.SaveComic(edited)
        Check("ticking read in the edit window notes the day", mdb.GetComic(m1).ReadOn = "2026-10-12")

        ' Trade list on the share page
        Dim tradePage = SharePage.Build(mdb, New ShareOptions With {.IncludeCollection = False, .IncludeWishlist = False, .IncludeTrade = True})
        Check("share page trade list", tradePage.Contains("id=""trade""") AndAlso tradePage.Contains("2 available") AndAlso Not tradePage.Contains("id=""collection"""))
        Dim keyPage = SharePage.Build(mdb, New ShareOptions())
        Check("share page shows key issues", keyPage.Contains("★ 1st appearance of the X-Men"))

        ' Insurance report, with a cover, a photo, and a damaged photo that's skipped
        Dim coversDir = IO.Path.Combine(IO.Path.GetTempPath(), $"covers-{Guid.NewGuid():N}")
        IO.Directory.CreateDirectory(coversDir)
        Dim png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==")
        File.WriteAllBytes(IO.Path.Combine(coversDir, $"{m1}.img"), png)
        IO.Directory.CreateDirectory(mdb.PhotosFolder)
        File.WriteAllBytes(IO.Path.Combine(mdb.PhotosFolder, "good.png"), png)
        File.WriteAllText(IO.Path.Combine(mdb.PhotosFolder, "bad.jpg"), "not a picture")
        mdb.AddPhoto(m1, "good.png", "Front")
        mdb.AddPhoto(m1, "bad.jpg", "Back")
        Dim pdfPath = IO.Path.Combine(IO.Path.GetTempPath(), $"insurance-{Guid.NewGuid():N}.pdf")
        Dim inReport = Reports.InsurancePdf(mdb, pdfPath, New InsuranceOptions With {.CoversFolder = coversDir, .Owner = "Test"})
        Check("insurance report made", inReport = 4 AndAlso File.Exists(pdfPath) AndAlso New FileInfo(pdfPath).Length > 1000, $"{inReport}")
        Check("insurance report can leave out cheap comics", Reports.InsurancePdf(mdb, pdfPath, New InsuranceOptions With {.MinValue = 50}) = 2)

        ' Pull list for the shop
        mdb.SaveReleases({New MetronIssue With {.MetronId = 1, .Series = "X-Men", .Number = "3", .Title = "Doom", .StoreDate = Date.Today.AddDays(5).ToString("yyyy-MM-dd")},
                          New MetronIssue With {.MetronId = 2, .Series = "X-Men", .Number = "2", .StoreDate = Date.Today.AddDays(5).ToString("yyyy-MM-dd")},
                          New MetronIssue With {.MetronId = 3, .Series = "Spawn", .Number = "9", .StoreDate = Date.Today.AddDays(60).ToString("yyyy-MM-dd")}}, True)
        Dim pull = mdb.PullList(Date.Today, Date.Today.AddDays(30))
        Check("pull list skips ones you have and later ones", pull.Rows.Count = 1 AndAlso CStr(pull.Rows(0)("Issue")) = "3")
        Dim pullText = Reports.PullListText(pull, "James")
        Check("pull list text", pullText.Contains("- X-Men #3: Doom") AndAlso pullText.Contains("Thanks, James"), pullText)
        Reports.PullListPdf(pull, pdfPath, "Pull list")
        Check("pull list PDF", New FileInfo(pdfPath).Length > 500)

        ' eBay value estimates
        Check("eBay search words", EbayValues.SearchWords("Batman", "404", "1987", "", "", "") = "Batman 404 1987" AndAlso
              EbayValues.SearchWords("Batman: Year One", "1", "", "", "CGC", "9.8") = "Batman Year One 1 CGC 9.8")
        Check("eBay needs a few listings", Not EbayValues.Parse("{""count"":2,""median"":5}").Typical.HasValue)
        Dim est = EbayValues.Parse("{""ok"":true,""count"":7,""median"":12.5,""low"":9,""high"":18}")
        Check("eBay estimate read", est.Count = 7 AndAlso est.Typical.GetValueOrDefault() = 12.5 AndAlso est.High.GetValueOrDefault() = 18)
        Dim ebayDay = New Date(2026, 10, 12)
        mdb.SetEbayEstimate(m2, est, ebayDay)
        mdb.SetEbayEstimate(m3, EbayValues.Parse("{""count"":1,""median"":3}"), New Date(2026, 9, 1))
        Dim ebayTodo = mdb.ComicsNeedingEbay(30, ebayDay)
        Check("eBay: unchecked first, then old checks", ebayTodo.Count = 3 AndAlso ebayTodo.Take(2).All(Function(t) t.ComicId = m1 OrElse t.ComicId = m4) AndAlso
              ebayTodo(2).ComicId = m3, String.Join(",", ebayTodo.Select(Function(t) t.ComicId)))
        Dim totals = mdb.EbayTotals()
        Check("eBay totals", totals.Checked = 2 AndAlso totals.Found = 1 AndAlso totals.Total = 37.5, $"{totals}")
        Check("eBay estimate on the comic", mdb.GetComic(m2).EbayValue.GetValueOrDefault() = 12.5 AndAlso mdb.GetComic(m2).EbayCount = 7 AndAlso
              mdb.GetComic(m2).EbayChecked = "2026-10-12")
        Check("eBay column in the list", mdb.SearchCollection("X-Men").Rows.Cast(Of DataRow)().Any(Function(r) Not r.IsNull("eBay est.") AndAlso CDbl(r("eBay est.")) = 12.5))
        Check("eBay estimates don't replace typed values unless asked", mdb.UseEbayEstimates(onlyBlank:=True) = 0 AndAlso mdb.GetComic(m2).CurrentValue.GetValueOrDefault() = 30)
        mdb.SetEbayEstimate(m4, est, ebayDay)
        Check("eBay estimates fill blank values", mdb.UseEbayEstimates(onlyBlank:=True) = 1 AndAlso mdb.GetComic(m4).CurrentValue.GetValueOrDefault() = 12.5)
        Check("eBay estimates can replace all values", mdb.UseEbayEstimates(onlyBlank:=False) = 1 AndAlso mdb.GetComic(m2).CurrentValue.GetValueOrDefault() = 12.5)

        ' Ratings and reviews
        mdb.SetRating({m3}, 5, "Loved it")
        mdb.SetRating({m3}, 4)
        Check("rating saved, review kept", mdb.GetComic(m3).Rating = 4 AndAlso mdb.GetComic(m3).Review = "Loved it")
        Check("rating column", mdb.SearchCollection("Spawn").Rows.Cast(Of DataRow)().Any(Function(r) CStr(r("Rating")) = "★★★★"))
        Dim rated = mdb.GetComic(m4)
        rated.Rating = 2
        rated.Review = "Meh"
        mdb.SaveComic(rated)
        Check("rating saved from the edit window", mdb.GetComic(m4).Rating = 2 AndAlso mdb.GetComic(m4).Review = "Meh")
        Dim liked = {New ReadItem With {.ComicId = 1, .Series = "Loved", .Issue = "1", .IssueSort = 1, .IsRead = True, .Rating = 5},
                     New ReadItem With {.ComicId = 2, .Series = "Loved", .Issue = "2", .IssueSort = 2},
                     New ReadItem With {.ComicId = 3, .Series = "Hated", .Issue = "1", .IssueSort = 1, .IsRead = True, .Rating = 1},
                     New ReadItem With {.ComicId = 4, .Series = "Hated", .Issue = "2", .IssueSort = 2},
                     New ReadItem With {.ComicId = 5, .Series = "Unrated", .Issue = "1", .IssueSort = 1, .IsRead = True},
                     New ReadItem With {.ComicId = 6, .Series = "Unrated", .Issue = "2", .IssueSort = 2}}
        Dim orderOk = Enumerable.Range(1, 10).All(Function(seed)
                                                      Dim ideas = ReadNext.Suggest(liked, New Random(seed))
                                                      Return ideas.Select(Function(i) i.Item.Series).SequenceEqual({"Loved", "Unrated", "Hated"})
                                                  End Function)
        Check("well-rated series suggested first, disliked last", orderOk)
        Check("suggestion mentions the stars", ReadNext.Suggest(liked, New Random(1))(0).Reason.Contains("rated it 5 stars"))

        ' Box labels
        Check("issue ranges", ComicDb.IssueRanges({("1", CType(1, Double?)), ("2", 2), ("3", 3), ("5", 5), ("7", 7), ("8", 8), ("Annual 1", Nothing), ("3", 3)}) = "1-3, 5, 7-8, Annual 1",
              ComicDb.IssueRanges({("1", CType(1, Double?)), ("2", 2), ("3", 3), ("5", 5), ("7", 7), ("8", 8), ("Annual 1", Nothing)}))
        Dim labels = mdb.BoxLabels()
        Check("box labels in order with contents", labels.Count = 2 AndAlso labels(0).Name = "Box 9" AndAlso labels(0).Count = 4 AndAlso
              labels(0).Lines.SequenceEqual({"Spawn #1", "X-Men #2"}) AndAlso labels(1).Lines.SequenceEqual({"X-Men #1"}),
              String.Join(" | ", labels.Select(Function(lb) $"{lb.Name}:{lb.Count}:{String.Join(";", lb.Lines)}")))
        Reports.BoxLabelsPdf(labels, pdfPath)
        Check("box labels PDF", New FileInfo(pdfPath).Length > 500)

        ' Convention signing list
        mdb.SaveArcs(m1, Array.Empty(Of MetronArc)(), {New MetronCredit With {.Name = "Stan Lee", .Roles = New List(Of String) From {"Writer"}},
                                                      New MetronCredit With {.Name = "Jack Kirby", .Roles = New List(Of String) From {"Artist", "Cover"}}})
        mdb.SaveArcs(m2, Array.Empty(Of MetronArc)(), {New MetronCredit With {.Name = "Jack Kirby", .Roles = New List(Of String) From {"Artist"}}})
        Check("guest names from pasted text", ComicDb.GuestNames("Jack Kirby, J. Scott Campbell" & vbCrLf & "• Stan Lee" & vbLf & "Jack Kirby").Count = 3)
        Check("name matching ignores dots and spaces", ComicDb.NameKey("J. Scott  Campbell") = ComicDb.NameKey("j scott campbell"))
        Dim signing = mdb.SigningList(ComicDb.GuestNames("jack  kirby" & vbLf & "Nobody Here"))
        Check("signing list finds comics, key issues first", signing.Rows.Count = 2 AndAlso CStr(signing.Rows(0)("Comic")) = "X-Men #1" AndAlso
              CStr(signing.Rows(0)("Box")) = "Box 10" AndAlso CStr(signing.Rows(0)("Role")).Contains("Cover"))
        Check("creators you own", mdb.CreatorsYouOwn()(0).Label = "Jack Kirby" AndAlso mdb.CreatorsYouOwn()(0).Count = 2)
        Dim signText = Reports.SigningListText(signing)
        Check("signing list text", signText.StartsWith("Jack Kirby (2)") AndAlso signText.Contains("- X-Men #1 (Box 10, 1st appearance of the X-Men)"), signText)
        Reports.SigningListPdf(signing, pdfPath, "Comics to get signed")
        Check("signing list PDF", New FileInfo(pdfPath).Length > 500)

        ' Wishlist deals
        mdb.AddWish("Batman", "404", 1, 30, "")
        mdb.AddWish("Batman", "405", 2, Nothing, "")
        Dim dealTargets = mdb.WishesNeedingDealCheck(ebayDay)
        Check("only wishes with a max price are searched", dealTargets.Count = 1 AndAlso dealTargets(0).Issue = "404" AndAlso dealTargets(0).MaxPrice = 30)
        Dim dealsFound = EbayValues.ParseListings("{""ok"":true,""items"":[{""id"":""v1|1|0"",""title"":""Batman #404"",""price"":12.5,""url"":""https://www.ebay.com/itm/1""},{""id"":""v1|2|0"",""title"":""Batman 404 VF"",""price"":20,""url"":""https://www.ebay.com/itm/2""}]}")
        Check("listings read", dealsFound.Count = 2 AndAlso dealsFound(0).Price = 12.5 AndAlso dealsFound(1).Url.EndsWith("/2"))
        Dim wishId = dealTargets(0).WishId
        Check("new deals saved", mdb.SaveDeals(wishId, dealsFound, ebayDay) = 2 AndAlso mdb.DealCount() = 2)
        Check("searched once a day", mdb.WishesNeedingDealCheck(ebayDay).Count = 0 AndAlso mdb.WishesNeedingDealCheck(ebayDay.AddDays(1)).Count = 1)
        Check("wishlist shows the best deal", mdb.GetWishlist().Rows.Cast(Of DataRow)().Any(Function(r) Not r.IsNull("eBay deal (US$)") AndAlso CDbl(r("eBay deal (US$)")) = 12.5))
        mdb.DismissDeals({Convert.ToInt64(mdb.OpenDeals().Rows.Cast(Of DataRow)().First(Function(r) CDbl(r("Price")) = 12.5)("id"))})
        Check("not interested hides a deal", mdb.DealCount() = 1)
        Check("seen deals aren't new again, gone ones are dropped, dismissed stay hidden",
              mdb.SaveDeals(wishId, {dealsFound(0)}, ebayDay.AddDays(1)) = 0 AndAlso mdb.DealCount() = 0)
        mdb.SetWishMaxPrice({wishId}, Nothing)
        Check("no max price, no searching", mdb.WishesNeedingDealCheck(ebayDay.AddDays(2)).Count = 0)

        ' Worth getting graded? and rising on eBay
        Dim pricey = EbayValues.Parse("{""count"":5,""median"":40}")
        mdb.SetEbayEstimate(m1, pricey, ebayDay)
        Dim gradeTodo = mdb.ComicsNeedingGradedCheck(15, 60, ebayDay)
        Check("graded check for raw comics worth it", gradeTodo.Count = 1 AndAlso gradeTodo(0).ComicId = m1 AndAlso gradeTodo(0).Words = "X-Men 1 CGC 9.8",
              String.Join(",", gradeTodo.Select(Function(t) t.Words)))
        mdb.SetGradedEstimate(m1, EbayValues.Parse("{""count"":4,""median"":300}"), ebayDay)
        Dim worth = mdb.WorthGrading(60)
        Check("worth grading gain", worth.Rows.Count = 1 AndAlso CDbl(worth.Rows(0)("Gain")) = 200 AndAlso mdb.ComicsNeedingGradedCheck(15, 60, ebayDay).Count = 0)
        mdb.SetEbayEstimate(m1, EbayValues.Parse("{""count"":5,""median"":55}"), ebayDay.AddDays(10))
        Check("no rise until a month apart", mdb.RisingComics().Count = 0)
        mdb.SetEbayEstimate(m1, EbayValues.Parse("{""count"":5,""median"":60}"), ebayDay.AddDays(30))
        Dim rising = mdb.RisingComics()
        Check("rising on eBay", rising.Count = 1 AndAlso rising(0).Label = "X-Men #1" AndAlso rising(0).Value = 5 AndAlso rising(0).Count = 9,
              String.Join(",", rising.Select(Function(r) $"{r.Label}={r.Value}/{r.Count}")))

        ' The phone's copy of the library carries box, key issue and eBay estimate
        Using phoneSnap = System.Text.Json.JsonDocument.Parse(mdb.LibrarySnapshot())
            Dim first = phoneSnap.RootElement.GetProperty("owned").EnumerateArray().First(Function(a) a(0).GetString() = "X-Men" AndAlso a(2).GetString() = "1")
            Check("library snapshot has box, key and eBay", first.GetArrayLength() = 8 AndAlso first(5).GetString() = "Box 10" AndAlso
                  first(6).GetString() = "1st appearance of the X-Men" AndAlso first(7).GetDouble() = 60)
        End Using
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools()
        File.Delete(morePath)
        File.Delete(pdfPath)
        IO.Directory.Delete(coversDir, recursive:=True)
        IO.Directory.Delete(mdb.PhotosFolder, recursive:=True)

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
