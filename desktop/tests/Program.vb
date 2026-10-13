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

    ''' <summary>Whether a search finds a comic with this issue number, cover letter and text (series, volume, box…).</summary>
    Private Function Finds(query As String, number As String, letter As String, ParamArray text As String()) As Boolean
        Return WordSearch.AllFound(WordSearch.WordsOf(query), text, number, letter)
    End Function

    Function Main(args As String()) As Integer
        ' Barcodes
        Dim b = Barcode.Parse("76194134182800111")
        Check("barcode with 5-digit add-on", b.Upc = "761941341828" AndAlso b.Addon = "00111" AndAlso b.Issue = "1" AndAlso b.Cover = "1")
        Check("barcode typed with a space", Barcode.Parse("761941341828 00521").Issue = "5")
        Check("13-digit code with leading 0", Barcode.Parse("0761941341828").Upc = "761941341828")
        Check("ISBN", Barcode.Parse("9781401235420").Upc = "9781401235420" AndAlso Barcode.Parse("9781401235420").Addon = "" AndAlso Barcode.Parse("9781401235420").IsIsbn)
        Check("ISBN price add-on isn't an issue number", Barcode.Parse("978140123542051499").Issue = "" AndAlso Barcode.Parse("978140123542051499").IsIsbn AndAlso
                                                         Not Barcode.Parse("76194134182800111").IsIsbn)
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

        ' Trade paperbacks and hardcovers are kept apart from single issues
        Check("trade formats recognised", ComicDb.IsTrade("Trade Paperback") AndAlso ComicDb.IsTrade("hardcover") AndAlso ComicDb.IsTrade("TPB") AndAlso
                                          ComicDb.IsTrade("Omnibus") AndAlso Not ComicDb.IsTrade("") AndAlso Not ComicDb.IsTrade("Comic") AndAlso Not ComicDb.IsTrade("Magazine"))
        Dim tpbId = db.SaveComic(New ComicRecord With {.Series = "Daredevil", .Issue = "169", .Format = "Trade Paperback", .Title = "Born Again", .Quantity = 1, .CurrentValue = 30})
        Dim hcId = db.SaveComic(New ComicRecord With {.Series = "Watchmen", .Issue = "1", .Format = "HC", .Quantity = 1})
        Check("trades left out of single issues", db.SearchCollection("", kind:="issues").Rows.Count = 6 AndAlso db.SearchCollection("daredevil", kind:="issues").Rows.Count = 2)
        Check("trades page shows only trades", db.SearchCollection("", kind:="trades").Rows.Count = 2 AndAlso db.SearchCollection("born", kind:="trades").Rows.Count = 1)
        Check("everything shows both", db.SearchCollection("").Rows.Count = 8)
        Check("trade doesn't fill a gap in the run", String.Join(",", db.MissingIssues("Daredevil", "")) = "169" AndAlso db.SeriesWithGaps().Rows.Count = 2)
        Check("totals by kind", db.GetStats("trades").Comics = 2 AndAlso db.GetStats("trades").TotalValue = 30 AndAlso db.GetStats("issues").Comics = 6)
        db.DeleteComics({tpbId, hcId})

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

        ' Boxes from the phone: a new comic lands in its box; ones you have are moved (by Metron issue, barcode, or name)
        Dim boxCsv = "Series,Volume,Issue,Cover / variant,Barcode,Metron ID,Box,Box only" & vbLf &
                     "The Amazing Spider-Man,,47,,,12345,Box 5,yes" & vbLf &
                     "saga,,1,Some other cover,,,Box 5," & vbLf &
                     "Uncanny X-Men,,999,,,,Box 5,yes" & vbLf &
                     "Hellboy,,1,,,,Box 5," & vbLf
        Dim boxed = db.ImportPhoneCsv(boxCsv)
        Check("phone boxes: 2 moved, 1 added, unknown move skipped", boxed.Moved = 2 AndAlso boxed.Added = 1 AndAlso boxed.Skipped = 1, $"{boxed.Moved} {boxed.Added} {boxed.Skipped}")
        Check("moved comic is in its box", db.FindByBarcode("75960620251704711").Location = "Box 5" AndAlso db.FindByBarcode("75960620251704711").Quantity = 2)
        Check("new comic from the phone is in its box", db.SearchCollection("Hellboy").Rows.Count = 1 AndAlso
              db.GetComic(Convert.ToInt64(db.SearchCollection("Hellboy").Rows(0)("comic_id"))).Location = "Box 5")
        Check("moved by name keeps one copy", db.SearchCollection("Chapter One").Rows.Count = 1 AndAlso
              db.GetComic(Convert.ToInt64(db.SearchCollection("Chapter One").Rows(0)("comic_id"))).Location = "Box 5")

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

        ' Digital comics: CBZ, a .cbr that's really a zip, EPUB, PDF, and the RAR/7z reader
        Dim digiDir = IO.Path.Combine(IO.Path.GetTempPath(), $"digital-{Guid.NewGuid():N}")
        IO.Directory.CreateDirectory(digiDir)
        Dim pagePng = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==")
        Dim cbzPath = IO.Path.Combine(digiDir, "Batman_002_(2016)_(digital).cbz")
        Using zip = IO.Compression.ZipFile.Open(cbzPath, IO.Compression.ZipArchiveMode.Create)
            For Each n In {"Batman 002/page10.png", "Batman 002/page2.png", "Batman 002/page1.png", "__MACOSX/._page1.png", "Batman 002/ComicInfo.xml"}
                Using s = zip.CreateEntry(n).Open()
                    s.Write(pagePng, 0, pagePng.Length)
                End Using
            Next
        End Using
        Using book = ComicBook.Open(cbzPath)
            Check("CBZ opens with its pictures in page order", book.Kind = "CBZ" AndAlso book.PageCount = 3)
            Check("CBZ page is the picture file", book.GetPage(0).Encoded IsNot Nothing AndAlso book.GetPage(0).Encoded.Length = pagePng.Length)
        End Using
        Dim cbrPath = IO.Path.Combine(digiDir, "really a zip.cbr")
        File.Copy(cbzPath, cbrPath)
        Using book = ComicBook.Open(cbrPath)
            Check(".cbr that's really a zip still opens", book.PageCount = 3)
        End Using
        Using book As New ArchiveBook(cbrPath)
            Check("RAR/7z reader lists pages", book.PageCount = 3 AndAlso book.GetPage(2).Encoded.Length = pagePng.Length)
        End Using
        Dim epubPath = IO.Path.Combine(digiDir, "Saga 1.epub")
        Using zip = IO.Compression.ZipFile.Open(epubPath, IO.Compression.ZipArchiveMode.Create)
            Dim put = Sub(name As String, data As Byte())
                          Using s = zip.CreateEntry(name).Open()
                              s.Write(data, 0, data.Length)
                          End Using
                      End Sub
            Dim utf = Function(t As String) Text.Encoding.UTF8.GetBytes(t)
            put("mimetype", utf("application/epub+zip"))
            put("META-INF/container.xml", utf("<?xml version=""1.0""?><container xmlns=""urn:oasis:names:tc:opendocument:xmlns:container"" version=""1.0""><rootfiles><rootfile full-path=""OEBPS/content.opf"" media-type=""application/oebps-package+xml""/></rootfiles></container>"))
            put("OEBPS/content.opf", utf("<?xml version=""1.0""?><package xmlns=""http://www.idpf.org/2007/opf"" version=""3.0""><manifest><item id=""p2"" href=""Text/b.xhtml"" media-type=""application/xhtml+xml""/><item id=""p1"" href=""Text/a.xhtml"" media-type=""application/xhtml+xml""/><item id=""i1"" href=""Images/z.png"" media-type=""image/png""/><item id=""i2"" href=""Images/a.png"" media-type=""image/png""/></manifest><spine><itemref idref=""p1""/><itemref idref=""p2""/></spine></package>"))
            put("OEBPS/Text/a.xhtml", utf("<html><body><img src=""../Images/z.png"" alt=""""/></body></html>"))
            put("OEBPS/Text/b.xhtml", utf("<html><body><svg><image xlink:href=""../Images/a.png""/></svg></body></html>"))
            put("OEBPS/Images/z.png", pagePng)
            put("OEBPS/Images/a.png", pagePng)
        End Using
        Using book = ComicBook.Open(epubPath)
            Check("EPUB pages follow the book's order", book.Kind = "EPUB" AndAlso book.PageCount = 2)
        End Using
        Dim comicPdf = IO.Path.Combine(digiDir, "Daredevil 168.pdf")
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community
        QuestPDF.Fluent.GenerateExtensions.GeneratePdf(QuestPDF.Fluent.Document.Create(Sub(doc)
                                            For i = 1 To 2
                                                Dim n = i
                                                QuestPDF.Fluent.PageExtensions.Page(doc, Sub(pg)
                                                           pg.Size(400, 600, QuestPDF.Infrastructure.Unit.Point)
                                                           QuestPDF.Fluent.TextExtensions.Text(pg.Content(), $"Page {n}")
                                                       End Sub)
                                            Next
                                        End Sub), comicPdf)
        Using book = ComicBook.Open(comicPdf)
            Check("PDF opens", book.Kind = "PDF" AndAlso book.PageCount = 2)
            Dim pg = book.GetPage(1)
            Check("PDF page drawn at reading size", pg.Bgra IsNot Nothing AndAlso pg.Height > 2000 AndAlso Math.Abs(pg.Width / pg.Height - 400 / 600) < 0.02 AndAlso pg.Bgra.Length = pg.Width * pg.Height * 4)
            Check("PDF paper is white", pg.Bgra(0) = 255 AndAlso pg.Bgra(3) = 255)
        End Using
        Dim junk = IO.Path.Combine(digiDir, "notes.txt")
        File.WriteAllText(junk, "hi")
        Dim refused = False
        Try
            ComicBook.Open(junk).Dispose()
        Catch ex As InvalidDataException
            refused = True
        End Try
        Check("non-comic file refused", refused)

        ' Copying into the app's folder, and the Digital shelf
        Dim shelf = IO.Path.Combine(digiDir, "shelf")
        Dim kept = DigitalLibrary.CopyIn(cbzPath, shelf)
        Check("file copied into the app's folder", File.Exists(kept) AndAlso IO.Path.GetDirectoryName(kept) = shelf AndAlso File.Exists(cbzPath))
        Check("same file isn't copied twice", DigitalLibrary.CopyIn(cbzPath, shelf) = kept AndAlso IO.Directory.GetFiles(shelf).Length = 1)
        Check("file already in the folder stays put", DigitalLibrary.CopyIn(kept, shelf) = kept)
        Check("tidy title", DigitalLibrary.TitleFromFileName("Batman_002_(2016)_(digital)_(Zone-Empire).cbz") = "Batman 002 (2016)")
        ' Series folders and issue order on the Digital shelf
        Dim sf = Function(t As String) DigitalLibrary.SeriesFromTitle(t)
        Check("series folder from title", sf("Daredevil v1 002") = "Daredevil v1" AndAlso sf("Daredevil v1 103") = "Daredevil v1" AndAlso
                                          sf("Batman 404 (1987)") = "Batman (1987)" AndAlso sf("Webtoon Ep 5") = "Webtoon" AndAlso
                                          sf("Saga #12") = "Saga" AndAlso sf("X-Men 2099 001") = "X-Men 2099" AndAlso sf("Watchmen") = "Watchmen" AndAlso
                                          sf("Saga Vol. 3") = "Saga", $"{sf("Daredevil v1 002")}|{sf("Batman 404 (1987)")}|{sf("Webtoon Ep 5")}|{sf("X-Men 2099 001")}|{sf("Saga Vol. 3")}")
        Check("issue from title", DigitalLibrary.IssueFromTitle("Daredevil v1 003 (1964)").GetValueOrDefault() = 3 AndAlso Not DigitalLibrary.IssueFromTitle("Watchmen").HasValue)
        Dim digiShelf = {New DigitalFile With {.Id = 1, .Title = "Daredevil v1 103"}, New DigitalFile With {.Id = 2, .Title = "Daredevil v1 002", .LastRead = "2026-10-12 10:00:00", .Page = 2, .Pages = 23},
                     New DigitalFile With {.Id = 3, .Title = "Daredevil v1 001", .LastRead = "2026-10-12 09:00:00", .Page = 22, .Pages = 23},
                     New DigitalFile With {.Id = 4, .Title = "Saga 1"}, New DigitalFile With {.Id = 5, .Title = "daredevil V1 088"},
                     New DigitalFile With {.Id = 6, .Title = "Spawn 1", .Folder = "Daredevil v1"}}
        Dim groups = DigitalLibrary.GroupBySeries(digiShelf)
        Check("grouped by series, last read first", groups.Count = 2 AndAlso groups(0).Name = "Daredevil v1" AndAlso groups(1).Files.Count = 1, String.Join(",", groups.Select(Function(g) $"{g.Name}:{g.Files.Count}")))
        Check("folder in issue order", String.Join(",", groups(0).Files.Select(Function(f) f.Id)) = "3,6,2,5,1", String.Join(",", groups(0).Files.Select(Function(f) f.Id)))
        Check("next issue in the folder", DigitalLibrary.NextInSeries(digiShelf, digiShelf(2)).Id = 6 AndAlso DigitalLibrary.NextInSeries(digiShelf, digiShelf(0)) Is Nothing AndAlso
                                          DigitalLibrary.NextInSeries(digiShelf, digiShelf(3)) Is Nothing)
        Dim readSamples = {New ReadItem With {.Series = "Batman", .Issue = "1", .IssueSort = 1, .IsRead = True}, New ReadItem With {.Series = "Batman", .Issue = "2", .IssueSort = 2, .IsRead = True},
                     New ReadItem With {.Series = "Batman", .Issue = "4", .IssueSort = 4, .IsRead = True}, New ReadItem With {.Series = "Saga", .Issue = "5", .IssueSort = 5, .IsRead = True},
                     New ReadItem With {.Series = "Saga", .Issue = "6", .IssueSort = 6, .IsRead = True}, New ReadItem With {.Series = "Saga", .Issue = "7", .IssueSort = 7, .IsRead = True},
                     New ReadItem With {.Series = "Saga", .Issue = "8", .IssueSort = 8}, New ReadItem With {.Series = "Saga", .Issue = "1", .IssueSort = 1, .IsRead = True, .IsTrade = True}}
        Dim longest = ReadingStats.LongestRun(readSamples)
        Check("longest run read", longest IsNot Nothing AndAlso longest.ToString() = "Saga #5-7" AndAlso longest.Length = 3, longest?.ToString())
        Check("series read most", ReadingStats.MostReadSeries(readSamples, 5)(0).Label = "Saga" AndAlso ReadingStats.MostReadSeries(readSamples, 5)(0).Count = 4)
        Check("folder carries on where you are", groups(0).UpTo.Id = 2 AndAlso groups(0).ReadCount = 1)
        Dim fileParts = DigitalLibrary.ParseFileName("Batman_002_(2016)_(digital).cbz")
        Check("file name read as series and issue", fileParts.HasValue AndAlso fileParts.Value.Series = "batman" AndAlso fileParts.Value.Issue = "2" AndAlso fileParts.Value.Year = "2016")
        Check("file name without an issue", Not DigitalLibrary.ParseFileName("Saga Volume One.cbz").HasValue OrElse DigitalLibrary.ParseFileName("Saga Volume One.cbz").Value.Issue <> "")
        Dim digiDbPath = IO.Path.Combine(digiDir, "digital.db")
        Dim ddb As New ComicDb(digiDbPath)
        ddb.LoadSampleData()
        Dim guessed = ddb.GuessComicForFile("Batman_002_(2016)_(digital).cbz")
        Check("file matched to the comic you own", guessed.HasValue AndAlso ddb.GetComic(guessed.Value).Issue = "2" AndAlso ddb.GetComic(guessed.Value).Series = "Batman")
        Check("only comics you own, by series and number", Not ddb.GuessComicForFile("All-Star Batman 1.cbz").HasValue AndAlso Not ddb.GuessComicForFile("Batman 3.cbz").HasValue AndAlso ddb.GuessComicForFile("Daredevil #0168 (1981).cbr").HasValue)
        Check("'The' doesn't matter", ddb.GuessComicForFile("The Saga 001.cbz").HasValue)
        Dim fileId = ddb.AddDigitalFile(kept, "Batman 002 (2016)", "CBZ", 3, guessed)
        Check("same file added again isn't doubled", ddb.AddDigitalFile(kept, "x", "CBZ", 3) = fileId AndAlso ddb.DigitalFiles().Count = 1)
        ddb.SetDigitalFolder({fileId}, "Bat-books")
        Check("moved to a folder you name", ddb.DigitalFiles()(0).SeriesFolder = "Bat-books")
        ddb.SetDigitalFolder({fileId}, "")
        Check("back to the folder from its name", ddb.DigitalFiles()(0).SeriesFolder = "Batman (2016)")
        Dim df = ddb.DigitalFileById(fileId)
        Check("shelf item linked to comic", df.ComicLabel = "Batman (2016) #2" AndAlso df.ProgressText = "New" AndAlso ddb.DigitalForComic(guessed.Value).Id = fileId)
        ddb.SaveReadingPlace(fileId, 1, 3)
        Check("reading place remembered", ddb.DigitalFileById(fileId).ProgressText = "Page 2 of 3" AndAlso ddb.KeepReading(5).Count = 1)
        Check("home page cover rows", ddb.CoverStrip("recent", 3).Rows.Count = 3 AndAlso ddb.CoverStrip("unread", 50).Rows.Count = 6 AndAlso
              ddb.CoverStrip("key", 5).Rows.Count = 0 AndAlso CStr(ddb.CoverStrip("recent", 1).Rows(0)("label")).Contains(" #"))
        ddb.SaveReadingPlace(fileId, 2, 3)
        Check("digital copy shown in the collection", CStr(ddb.SearchCollection("batman").Rows.Cast(Of DataRow)().First(Function(rw) CLng(rw("comic_id")) = guessed.Value)("Digital")) = "Digital")
        ' Reading places shared with the phone
        ddb.SaveReadingPlace(fileId, 1, 3)
        Dim myPlaces = ddb.ReadingPlaces()
        Dim placeKey = DigitalLibrary.PlaceKey(kept)
        Check("reading place keyed by file name and size", myPlaces.Count = 1 AndAlso placeKey.EndsWith("|" & New FileInfo(kept).Length) AndAlso myPlaces(placeKey).Page = 1 AndAlso myPlaces(placeKey).At.EndsWith("Z"))
        Check("older phone place ignored", ddb.ApplyReadingPlaces(New Dictionary(Of String, ReadingPlace) From {{placeKey, New ReadingPlace With {.Page = 0, .Pages = 3, .At = "2020-01-01T00:00:00Z"}}}).Count = 0)
        Check("not finished, not marked read", Not ddb.MarkPaperCopyRead(fileId) AndAlso Not ddb.GetComic(guessed.Value).IsRead)
        Dim phoneNewer = ddb.ApplyReadingPlaces(New Dictionary(Of String, ReadingPlace) From {{placeKey, New ReadingPlace With {.Page = 2, .Pages = 3, .At = "2099-01-01T10:00:00.123Z"}}})
        Check("newer phone place taken up", phoneNewer.Count = 1 AndAlso ddb.DigitalFileById(fileId).Page = 2 AndAlso ddb.DigitalFileById(fileId).LastRead = "2099-01-01 10:00:00")
        Check("finished digital copy marks the paper one read", ddb.MarkPaperCopyRead(fileId) AndAlso ddb.GetComic(guessed.Value).IsRead AndAlso Not ddb.MarkPaperCopyRead(fileId))
        Dim relayPlaces = MetronClient.ParsePlaces("{""ok"":true,""places"":{""a.cbz|10"":{""page"":4,""pages"":20,""at"":""2026-10-12T01:02:03Z""},""bad"":{""page"":1}}}")
        Check("places from the relay read", relayPlaces.Count = 1 AndAlso relayPlaces("a.cbz|10").Page = 4 AndAlso relayPlaces("a.cbz|10").Pages = 20)
        ddb.SetRead({guessed.Value}, False)
        ddb.SaveReadingPlace(fileId, 2, 3)
        Check("last page is finished", ddb.KeepReading(5).Count = 0 AndAlso ddb.DigitalFileById(fileId).Finished AndAlso ddb.DigitalFileById(fileId).ProgressText = "Read")
        ddb.DeleteComics({guessed.Value})
        Check("deleting the comic keeps the file on the shelf", ddb.DigitalFileById(fileId) IsNot Nothing AndAlso Not ddb.DigitalFileById(fileId).ComicId.HasValue)
        Check("finished digital-only comic counts as read", ddb.ReadByMonth(1).Last().Count = 1 AndAlso ddb.ReadThisYear() = 1, $"{ddb.ReadByMonth(1).Last().Count}")
        ddb.RemoveDigitalFile(fileId)
        Check("removed from shelf", ddb.DigitalFiles().Count = 0)

        ' Videos from YouTube channels
        Check("channel from a link", YouTubeFeed.CleanChannel("https://www.youtube.com/@comictom101/videos") = "@comictom101" AndAlso YouTubeFeed.CleanChannel("comictom101") = "@comictom101" AndAlso
              YouTubeFeed.CleanChannel("https://www.youtube.com/channel/UCabcdefghijklmnopqrstuv") = "UCabcdefghijklmnopqrstuv")
        Check("channel id from its page", YouTubeFeed.ChannelIdFromPage("...""externalId"":""UCabcdefghijklmnopqrstuv"",...") = "UCabcdefghijklmnopqrstuv")
        Dim feed = "<?xml version=""1.0"" encoding=""UTF-8""?><feed xmlns:yt=""http://www.youtube.com/xml/schemas/2015"" xmlns:media=""http://search.yahoo.com/mrss/"" xmlns=""http://www.w3.org/2005/Atom"">" &
                   "<title>ComicTom101</title>" &
                   "<entry><id>yt:video:AAA</id><yt:videoId>AAA</yt:videoId><title>Older video</title><author><name>ComicTom101</name></author><published>2026-10-01T10:00:00+00:00</published><media:group><media:thumbnail url=""https://i1.ytimg.com/vi/AAA/hqdefault.jpg"" width=""480"" height=""360""/></media:group></entry>" &
                   "<entry><id>yt:video:BBB</id><yt:videoId>BBB</yt:videoId><title>Top 10 Batman keys &amp; more</title><author><name>ComicTom101</name></author><published>2026-10-10T10:00:00+00:00</published><media:group><media:thumbnail url=""https://i1.ytimg.com/vi/BBB/hqdefault.jpg"" width=""480"" height=""360""/></media:group></entry>" &
                   "</feed>"
        Dim vids = YouTubeFeed.ParseFeed(feed)
        Check("feed read newest first", vids.Count = 2 AndAlso vids(0).VideoId = "BBB" AndAlso vids(0).Title = "Top 10 Batman keys & more" AndAlso vids(0).Channel = "ComicTom101" AndAlso
              vids(0).Thumbnail.EndsWith("/BBB/hqdefault.jpg") AndAlso vids(0).Url = "https://www.youtube.com/watch?v=BBB")
        Check("first look doesn't count as new", ddb.SaveVideos(vids.Skip(1), markSeen:=True) = 0 AndAlso ddb.NewVideoCount() = 0)
        Check("new episode counted", ddb.SaveVideos(vids) = 1 AndAlso ddb.NewVideoCount() = 1 AndAlso ddb.Videos()(0).VideoId = "BBB" AndAlso Not ddb.Videos()(0).Seen)
        ddb.MarkVideosSeen()
        Check("videos seen", ddb.NewVideoCount() = 0 AndAlso ddb.Videos().Count = 2)
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools()
        IO.Directory.Delete(digiDir, recursive:=True)

        ' Updates: GitHub's answer about the newest release
        Dim release = Updater.ParseRelease("{""tag_name"":""desktop-42"",""body"":""Voice and sync\n"",""assets"":[{""name"":""ComicCatalog-windows.zip"",""browser_download_url"":""https://x/zip""},{""name"":""ComicCatalog.exe"",""browser_download_url"":""https://github.com/viksr-dev/comic-collection/releases/download/desktop-42/ComicCatalog.exe""}]}")
        Check("release parsed", release IsNot Nothing AndAlso release.Build = 42 AndAlso release.DownloadUrl.EndsWith("/desktop-42/ComicCatalog.exe") AndAlso release.Notes = "Voice and sync")
        Check("download from anywhere else refused", Updater.ParseRelease("{""tag_name"":""desktop-43"",""assets"":[{""name"":""ComicCatalog.exe"",""browser_download_url"":""https://evil.example/ComicCatalog.exe""}]}") Is Nothing)
        Check("other releases ignored", Updater.ParseRelease("{""tag_name"":""v1.0"",""assets"":[]}") Is Nothing)
        Check("release without the exe ignored", Updater.ParseRelease("{""tag_name"":""desktop-7"",""assets"":[]}") Is Nothing)

        ' Shop receipts: a ComicHub invoice as PDF text (lines as PDFium reads them)
        Dim receiptText = String.Join(vbCrLf, {
            "Sample Comics", "1 Queen St", "Auckland", "Bill To: A Reader", "Deliver to: A Reader", "Order No: 123456", "Order Date:09 Oct, 2026",
            "Item Quantity Points Price Final Price",
            "Walking Dead Deluxe #146 Cover A David Finch", "& Dave Mccaig (Mature)", "1 2.60 $ 9.95 $ 9.95",
            "Batman #14 Cover A Matteo Scalera (Batman Bad", "Seeds)", "1 3.12 $ 11.95 $ 11.95",
            "Marvel Knights Graphic Novel Volume 1 Punisher 1 9.38 $ 35.95 $ 35.95",
            "Gambit Wanted #3 2 3.12 $ 11.95 $ 23.90",
            "Sub Total $ 71.09", "Sales Tax $ 10.66", "Shipping $ 5.00", "Total $ 86.75", "Online - ComicHub $86.75"})
        Dim shopReceipt = ShopReceipts.Parse(receiptText)
        Check("receipt shop, order and date", shopReceipt.Shop = "Sample Comics" AndAlso shopReceipt.OrderNumber = "123456" AndAlso shopReceipt.OrderDate = "2026-10-09",
              $"{shopReceipt.Shop}|{shopReceipt.OrderNumber}|{shopReceipt.OrderDate}")
        Check("receipt items", shopReceipt.Items.Count = 4, String.Join(" / ", shopReceipt.Items.Select(Function(x) x.Text)))
        Dim wd = shopReceipt.Items(0)
        Check("wrapped title split into series, issue and cover", wd.Series = "Walking Dead Deluxe" AndAlso wd.Issue = "146" AndAlso wd.CoverLetter = "A" AndAlso
              wd.VariantName = "David Finch & Dave Mccaig" AndAlso wd.Extra = "", $"{wd.Series}|{wd.Issue}|{wd.CoverLetter}|{wd.VariantName}|{wd.Extra}")
        Check("story arc kept from brackets", shopReceipt.Items(1).Series = "Batman" AndAlso shopReceipt.Items(1).Extra = "Batman Bad Seeds", shopReceipt.Items(1).Extra)
        Dim gn = shopReceipt.Items(2)
        Check("graphic novel is a book", gn.Format = "Graphic Novel" AndAlso gn.Issue = "1" AndAlso gn.Series = "Marvel Knights Graphic Novel Volume 1 Punisher", $"{gn.Format}|{gn.Issue}|{gn.Series}")
        Check("price paid per copy, with postage shared out", If(wd.PricePaid, 0) = 10.56 AndAlso shopReceipt.Items(3).Quantity = 2 AndAlso If(shopReceipt.Items(3).PricePaid, 0) = 12.68,
              $"{wd.PricePaid} {shopReceipt.Items(3).Quantity} {shopReceipt.Items(3).PricePaid}")
        Dim threw = False
        Try
            ShopReceipts.Parse("Just a letter" & vbLf & "Nothing to see")
        Catch ex As InvalidDataException
            threw = True
        End Try
        Check("not a receipt", threw)
        Dim batmen = {New MetronIssue With {.MetronId = 1, .Series = "Batman", .Volume = "2016", .Number = "14", .StoreDate = "2017-01-18"},
                      New MetronIssue With {.MetronId = 2, .Series = "Batman", .Volume = "2025", .Number = "14", .StoreDate = "2026-09-24"},
                      New MetronIssue With {.MetronId = 3, .Series = "Batman", .Volume = "2025", .Number = "15", .StoreDate = "2026-10-22"},
                      New MetronIssue With {.MetronId = 4, .Series = "Batman/Superman", .Volume = "2024", .Number = "14", .StoreDate = "2026-10-01"}}
        Check("receipt matches the Batman out around the order date", If(ShopReceipts.PickMatch(batmen, shopReceipt.Items(1), shopReceipt.OrderDate)?.MetronId, 0) = 2)
        Check("old issue with many series not guessed", ShopReceipts.PickMatch(batmen.Take(2), shopReceipt.Items(1), "2020-01-01") Is Nothing)
        Check("receipt name matches without punctuation", If(ShopReceipts.PickMatch({New MetronIssue With {.MetronId = 9, .Series = "Gambit: Wanted", .Number = "3"}},
              shopReceipt.Items(3), shopReceipt.OrderDate)?.MetronId, 0) = 9)
        Dim receiptDbPath = IO.Path.Combine(IO.Path.GetTempPath(), $"comics-receipt-{Guid.NewGuid():N}.db")
        Dim rdb As New ComicDb(receiptDbPath)
        Dim firstId = rdb.AddFromReceipt(ShopReceipts.ToComic(shopReceipt.Items(1), shopReceipt, batmen(1)))
        Dim saved = rdb.GetComic(firstId)
        Check("receipt comic saved with Metron details", saved.Series = "Batman" AndAlso saved.Volume = "2025" AndAlso If(saved.MetronId, 0) = 2 AndAlso If(saved.PricePaid, 0) = 12.68 AndAlso
              saved.PurchaseDate = "2026-10-09" AndAlso saved.CoverLetter = "A" AndAlso saved.Notes.Contains("Sample Comics") AndAlso saved.Notes.Contains("Bad Seeds"),
              $"{saved.Series}|{saved.Volume}|{saved.PricePaid}|{saved.PurchaseDate}|{saved.Notes}")
        Check("already have it", rdb.HasReceiptItem(shopReceipt.Items(1)) AndAlso Not rdb.HasReceiptItem(wd))
        rdb.SetRead({firstId}, True)
        Dim again = rdb.AddFromReceipt(ShopReceipts.ToComic(shopReceipt.Items(1), shopReceipt, batmen(1)))
        Check("bought again adds a copy, keeps the rest", again = firstId AndAlso rdb.GetComic(firstId).Quantity = 2 AndAlso rdb.GetComic(firstId).IsRead)
        Dim bookId = rdb.AddFromReceipt(ShopReceipts.ToComic(gn, shopReceipt, Nothing))
        Check("receipt book saved as a trade", ComicDb.IsTrade(rdb.GetComic(bookId).Format))
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools()
        File.Delete(receiptDbPath)

        ' Searching the way people type it, the same as the phone app
        Check("word search: daredevil 50 is #50, not #150 or #500", Finds("daredevil 50", "50", "", "Daredevil", "Vol. 2") AndAlso
              Not Finds("daredevil 50", "150", "", "Daredevil", "Vol. 2") AndAlso Not Finds("daredevil 50", "500", "", "Daredevil", "Vol. 2"))
        Check("word search: # and leading zeros", Finds("daredevil #50", "050", "", "Daredevil", "Vol. 1"))
        Check("word search: cover letters", Finds("daredevil 50a", "50", "A", "Daredevil") AndAlso Not Finds("daredevil 50a", "50", "B", "Daredevil") AndAlso
              Finds("daredevil 50", "50", "B", "Daredevil") AndAlso Finds("daredevil 50b", "50B", "", "Daredevil"))
        Check("word search: the", Finds("walking dead 100", "100", "", "The Walking Dead Deluxe") AndAlso Finds("the walking dead", "1", "", "Walking Dead"))
        Check("word search: spiderman and xmen", Finds("spiderman", "1", "", "The Amazing Spider-Man", "Vol. 6") AndAlso Finds("x-men", "1", "", "Uncanny X-Men") AndAlso
              Finds("xmen 1", "1", "", "Uncanny X-Men") AndAlso Finds("Spider-Man", "1", "", "The Amazing Spider-Man"))
        Check("word search: volumes", Finds("harley quinn vol 3", "12", "", "Harley Quinn", "Vol. 3") AndAlso
              Not Finds("harley quinn vol 3", "12", "", "Harley Quinn", "Vol. 1") AndAlso Finds("harley quinn vol 3", "3", "", "Harley Quinn", "Vol. 1"))
        Check("word search: numbers in names and years", Finds("spider-man 2099", "1", "", "Spider-Man 2099") AndAlso Finds("batman 2016", "1", "", "Batman", "2016") AndAlso
              Not Finds("batman 3", "1", "", "Batman", "2016", "2016-03"))
        Check("word search: boxes", Finds("box 5", "1", "", "Batman", "Box 5") AndAlso Not Finds("box 5", "1", "", "Batman", "Box 15"))
        Check("word search: barcodes", Finds("759606202003", "1", "", "Batman", "75960620200300111") AndAlso
              Finds("759606202003 00111", "1", "", "Batman", "75960620200300111") AndAlso Finds("75960620200300111", "1", "", "Batman", "75960620200300111"))
        Check("word search: publisher, and nothing typed", Finds("marvel", "1", "", "Daredevil", "Marvel Comics") AndAlso Finds("", "1", "", "Daredevil") AndAlso
              Finds("  ", "1", "", "Daredevil") AndAlso Not Finds("dc 1", "10", "", "Batman", "DC Comics"))
        Check("word search: 1.50 and ratio variants", Not Finds("daredevil 50", "1.50", "", "Daredevil", "Vol. 4") AndAlso
              Finds("daredevil 50", "1", "", "Daredevil", "Vol. 4", "1:50 Ratio Variant") AndAlso Finds("batman 1.50", "1.50", "", "Batman"))
        Check("word search: annuals", Finds("annual", "Annual 1", "", "Batman") AndAlso Finds("batman annual", "Annual 1", "", "Batman"))
        Check("word search: words in quotes stay together", Finds("""i am gotham""", "1", "", "Batman", "Batman: I Am Gotham") AndAlso
              Not Finds("""i am gotham""", "1", "", "Batman: Gotham Knights") AndAlso Finds("i am gotham", "1", "", "Batman: Gotham Knights") AndAlso
              Finds("""batman: i am""", "1", "", "Batman", "Batman: I Am Gotham") AndAlso Finds("""i am goth", "1", "", "Batman: I Am Gotham") AndAlso
              WordSearch.WordsOf("""""  the Batman #1").SequenceEqual({"batman", "1"}))
        Dim scoreOf = Function(q As String, number As String) WordSearch.Score(WordSearch.WordsOf(q), number)
        Check("word search: typed issue numbers come first", scoreOf("daredevil 50", "50") = 2 AndAlso scoreOf("daredevil 50", "1") = 0 AndAlso
              scoreOf("harley quinn vol 3 1", "1") = 2 AndAlso scoreOf("harley quinn vol 3 1", "3") = 1 AndAlso scoreOf("spiderman", "3") = 0)

        ' The collection list: words, issue numbers first, boxes and the hidden columns for sorting
        Dim findPath = IO.Path.Combine(IO.Path.GetTempPath(), $"comics-find-{Guid.NewGuid():N}.db")
        Dim fdb As New ComicDb(findPath)
        Dim f1 = fdb.SaveComic(New ComicRecord With {.Series = "Daredevil", .Volume = "Vol. 1", .Issue = "1", .VariantName = "1:50 Ratio Variant", .Location = "Box 2", .CurrentValue = 40})
        Dim f2 = fdb.SaveComic(New ComicRecord With {.Series = "Daredevil", .Volume = "Vol. 1", .Issue = "50", .Location = "box 2"})
        Dim f3 = fdb.SaveComic(New ComicRecord With {.Series = "Daredevil", .Volume = "Vol. 1", .Issue = "150", .Location = "Box 10", .CurrentValue = 5})
        Dim f4 = fdb.SaveComic(New ComicRecord With {.Series = "The Amazing Spider-Man", .Volume = "Vol. 6", .Issue = "10"})
        Dim f5 = fdb.SaveComic(New ComicRecord With {.Series = "The Amazing Spider-Man", .Volume = "Vol. 6", .Issue = "2", .Location = " "})
        Dim idsOf = Function(t As DataTable) String.Join(",", t.Rows.Cast(Of DataRow)().Select(Function(r) Convert.ToInt64(r("comic_id"))))
        Check("list: daredevil 50 puts #50 first and skips #150", idsOf(fdb.SearchCollection("daredevil 50")) = $"{f2},{f1}", idsOf(fdb.SearchCollection("daredevil 50")))
        Check("list: spiderman finds Spider-Man, issues in number order", idsOf(fdb.SearchCollection("spiderman")) = $"{f5},{f4}" AndAlso
              idsOf(fdb.SearchCollection("")) = $"{f5},{f4},{f1},{f2},{f3}", idsOf(fdb.SearchCollection("")))
        Check("list: one box (any capitals)", idsOf(fdb.SearchCollection("", box:="Box 2")) = $"{f1},{f2}" AndAlso idsOf(fdb.SearchCollection("", box:="BOX 10")) = $"{f3}" AndAlso
              idsOf(fdb.SearchCollection("daredevil", box:="box 2")) = $"{f1},{f2}")
        Check("list: not in a box", idsOf(fdb.SearchCollection("", box:="")) = $"{f5},{f4}" AndAlso idsOf(fdb.SearchCollection("spider", box:="")) = $"{f5},{f4}")
        Dim boxCounts = fdb.BoxCounts()
        Check("box counts in order, not in a box last", String.Join(",", boxCounts.Select(Function(bc) $"{bc.Name.ToLowerInvariant()}:{bc.Count}")) = "box 2:2,box 10:1,:2",
              String.Join(",", boxCounts.Select(Function(bc) $"{bc.Name}:{bc.Count}")))
        Dim listed = fdb.SearchCollection("")
        Check("list: hidden sort columns", CStr(listed.Rows(0)("_series_sort")) = "Amazing Spider-Man" AndAlso Convert.ToDouble(listed.Rows(1)("_issue_sort")) = 10 AndAlso
              Enumerable.Range(0, listed.Rows.Count).All(Function(i) CInt(listed.Rows(i)("_order")) = i) AndAlso
              Not listed.Columns.Contains("_notes") AndAlso Not listed.Columns.Contains("_letter"))
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools()
        File.Delete(findPath)

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
