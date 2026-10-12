Option Strict On

Imports System.Data
Imports System.Globalization
Imports System.IO
Imports Microsoft.Data.Sqlite

Namespace Data

    ''' <summary>One bar on a Stats chart: a name, how many comics, and what they're worth.</summary>
    Public Class ChartItem
        Public Property Label As String = ""
        Public Property Count As Integer
        Public Property Value As Double
    End Class

    ''' <summary>The collection's size and value on one day.</summary>
    Public Class ValuePoint
        Public Property Day As Date
        Public Property Comics As Integer
        Public Property TotalValue As Double
        Public Property TotalPaid As Double
    End Class

    ''' <summary>One of your own photos of a comic. The file is in ComicDb.PhotosFolder.</summary>
    Public Class PhotoInfo
        Public Property Id As Long
        Public Property ComicId As Long
        Public Property FileName As String = ""
        Public Property Caption As String = ""
    End Class

    ''' <summary>A comic you own, with just what "What should I read next?" needs.</summary>
    Public Class ReadItem
        Public Property ComicId As Long
        Public Property Series As String = ""
        Public Property Volume As String = ""
        Public Property Issue As String = ""
        Public Property IssueSort As Double?
        Public Property Title As String = ""
        Public Property CoverUrl As String = ""
        Public Property IsRead As Boolean

        Public ReadOnly Property SeriesName As String
            Get
                Return Series & If(Volume <> "", $" ({Volume})", "")
            End Get
        End Property

        Public Overrides Function ToString() As String
            Return If(Issue <> "", $"{SeriesName} #{Issue}", SeriesName)
        End Function
    End Class

    ' Stats, reading, lending, photos and the share page.
    Partial Public Class ComicDb

        ''' <summary>"Batman (2016) #12" in SQL, from v_collection aliased as v.</summary>
        Private Const ComicLabelSql As String =
            "v.series || CASE WHEN v.volume <> '' THEN ' (' || v.volume || ')' ELSE '' END || CASE WHEN v.issue <> '' THEN ' #' || v.issue ELSE '' END"

        ' Metron's names for the jobs people do on a comic.
        Private Const WriterRoles As String = "'writer', 'story', 'script', 'plot', 'co-writer'"
        Private Const ArtistRoles As String = "'artist', 'penciller', 'pencils', 'illustrator', 'layouts', 'breakdowns'"

        ' ---------- stats ----------

        ''' <summary>
        ''' Comics counted (and valued) by: "publisher", "decade", "series", "writer", "artist", "keys" (your key issues), or "value"
        ''' (your most valuable comics, where Count is copies). Biggest first, except decades, which go in order.
        ''' </summary>
        Public Function StatsBy(kind As String, Optional top As Integer = 10) As List(Of ChartItem)
            Dim sql As String
            Select Case kind
                Case "publisher"
                    sql = "SELECT IFNULL(NULLIF(v.publisher, ''), 'Unknown'), COUNT(*), IFNULL(SUM(v.total_value), 0)
                           FROM v_collection v GROUP BY 1 ORDER BY 2 DESC, 1"
                Case "decade"
                    sql = "SELECT CASE WHEN v.cover_date GLOB '[12][0-9][0-9][0-9]*' THEN (CAST(substr(v.cover_date, 1, 4) AS INTEGER) / 10 * 10) || 's'
                                       ELSE 'Unknown' END AS d, COUNT(*), IFNULL(SUM(v.total_value), 0)
                           FROM v_collection v GROUP BY d ORDER BY d = 'Unknown', d"
                Case "series"
                    sql = "SELECT v.series, COUNT(*), IFNULL(SUM(v.total_value), 0) FROM v_collection v GROUP BY v.series ORDER BY 2 DESC, 1"
                Case "writer", "artist"
                    sql = $"SELECT cr.name, COUNT(*), IFNULL(SUM(v.total_value), 0)
                            FROM (SELECT DISTINCT comic_id, creator_id FROM comic_creators
                                  WHERE lower(role) IN ({If(kind = "writer", WriterRoles, ArtistRoles)})) cc
                            JOIN creators cr ON cr.id = cc.creator_id
                            JOIN v_collection v ON v.comic_id = cc.comic_id
                            GROUP BY cr.id ORDER BY 2 DESC, 1"
                Case "keys"
                    sql = $"SELECT {ComicLabelSql} || ' · ' || v.key_note, v.quantity, IFNULL(v.total_value, 0)
                            FROM v_collection v WHERE v.key_note <> '' ORDER BY v.total_value DESC, 1"
                Case "value"
                    sql = $"SELECT {ComicLabelSql} || CASE WHEN v.variant_name <> '' THEN ' · ' || v.variant_name ELSE '' END,
                                   v.quantity, v.total_value
                            FROM v_collection v WHERE v.total_value > 0 ORDER BY v.total_value DESC, 1"
                Case Else
                    Throw New ArgumentException($"Unknown kind of stats: {kind}")
            End Select
            Dim list As New List(Of ChartItem)
            Using conn = Open()
                Using cmd = Command(conn, sql & $" LIMIT {Math.Max(1, top)}", Nothing)
                    Using r = cmd.ExecuteReader()
                        While r.Read()
                            list.Add(New ChartItem With {
                                .Label = Convert.ToString(r.GetValue(0), CultureInfo.InvariantCulture),
                                .Count = Convert.ToInt32(r.GetValue(1), CultureInfo.InvariantCulture),
                                .Value = Convert.ToDouble(r.GetValue(2), CultureInfo.InvariantCulture)})
                        End While
                    End Using
                End Using
            End Using
            Return list
        End Function

        ''' <summary>How many comics you own have had their writers and artists looked up, and how many are matched to Metron.</summary>
        Public Function CreditsChecked() As (Checked As Integer, Matched As Integer)
            Using conn = Open()
                Dim checkedCount = Scalar(conn, "SELECT COUNT(*) FROM comics c JOIN collection col ON col.comic_id = c.id WHERE c.credits_checked = 1", Nothing)
                Dim matched = Scalar(conn, "SELECT COUNT(*) FROM v_collection WHERE metron_id IS NOT NULL", Nothing)
                Return (Convert.ToInt32(checkedCount, CultureInfo.InvariantCulture), Convert.ToInt32(matched, CultureInfo.InvariantCulture))
            End Using
        End Function

        ''' <summary>Notes today's size and value of the collection (replacing a note made earlier today).</summary>
        Public Sub RecordValueHistory(Optional day As Date? = Nothing)
            Using conn = Open()
                Execute(conn, Nothing,
                    "INSERT OR REPLACE INTO value_history (day, comics, total_value, total_paid)
                     SELECT $p0, COUNT(*), IFNULL(SUM(total_value), 0), IFNULL(SUM(price_paid), 0) FROM v_collection",
                    If(day, Date.Today).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
            End Using
        End Sub

        ''' <summary>Every day noted by RecordValueHistory, oldest first.</summary>
        Public Function ValueHistory() As List(Of ValuePoint)
            Dim list As New List(Of ValuePoint)
            Using conn = Open()
                Using cmd = Command(conn, "SELECT day, comics, total_value, total_paid FROM value_history ORDER BY day", Nothing)
                    Using r = cmd.ExecuteReader()
                        While r.Read()
                            Dim d As Date
                            If Not Date.TryParseExact(r.GetString(0), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, d) Then Continue While
                            list.Add(New ValuePoint With {.Day = d, .Comics = r.GetInt32(1), .TotalValue = r.GetDouble(2), .TotalPaid = r.GetDouble(3)})
                        End While
                    End Using
                End Using
            End Using
            Return list
        End Function

        ' ---------- what to read next ----------

        ''' <summary>Everything you own, with whether you've read it, for ReadNext.Suggest.</summary>
        Public Function ReadingItems() As List(Of ReadItem)
            Dim list As New List(Of ReadItem)
            Using conn = Open()
                Using cmd = Command(conn, "SELECT comic_id, series, volume, issue, issue_sort, title, cover_url, is_read FROM v_collection ORDER BY comic_id", Nothing)
                    Using r = cmd.ExecuteReader()
                        While r.Read()
                            list.Add(New ReadItem With {
                                .ComicId = r.GetInt64(0), .Series = Str(r, "series"), .Volume = Str(r, "volume"), .Issue = Str(r, "issue"),
                                .IssueSort = Num(r, "issue_sort"), .Title = Str(r, "title"), .CoverUrl = Str(r, "cover_url"),
                                .IsRead = r.GetInt64(7) <> 0})
                        End While
                    End Using
                End Using
            End Using
            Return list
        End Function

        ' ---------- lending ----------

        ''' <summary>Notes that comics are lent to someone. A comic already out is marked back first.</summary>
        Public Sub LendComics(comicIds As IEnumerable(Of Long), borrower As String, lentOn As Date, Optional notes As String = "")
            If String.IsNullOrWhiteSpace(borrower) Then Throw New ArgumentException("Who are you lending it to?")
            Dim day = lentOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            Using conn = Open()
                Using tx = conn.BeginTransaction()
                    For Each id In comicIds
                        Execute(conn, tx, "UPDATE loans SET returned_on = $p1 WHERE comic_id = $p0 AND returned_on = ''", id, day)
                        Execute(conn, tx, "INSERT INTO loans (comic_id, borrower, lent_on, notes) VALUES ($p0, $p1, $p2, $p3)",
                                id, borrower.Trim(), day, If(notes, "").Trim())
                    Next
                    tx.Commit()
                End Using
            End Using
        End Sub

        ''' <summary>Marks loans as given back.</summary>
        Public Sub ReturnLoans(loanIds As IEnumerable(Of Long), returnedOn As Date)
            Dim day = returnedOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            Using conn = Open()
                Using tx = conn.BeginTransaction()
                    For Each id In loanIds
                        Execute(conn, tx, "UPDATE loans SET returned_on = $p1 WHERE id = $p0 AND returned_on = ''", id, day)
                    Next
                    tx.Commit()
                End Using
            End Using
        End Sub

        ''' <summary>Comics lent out (and, with includeReturned, ones given back), the ones still out first.</summary>
        Public Function GetLoans(includeReturned As Boolean) As DataTable
            Using conn = Open()
                Return Query(conn,
                    $"SELECT l.id AS loan_id, l.comic_id, l.borrower AS [Lent to], {ComicLabelSql} AS Comic, v.title AS [Story title],
                             l.lent_on AS [Lent on],
                             CASE WHEN l.returned_on = '' THEN CAST(julianday('now', 'localtime') - julianday(l.lent_on) AS INTEGER) END AS [Days out],
                             l.returned_on AS [Given back], l.notes AS Notes
                      FROM loans l JOIN v_collection v ON v.comic_id = l.comic_id
                      WHERE $p0 = 1 OR l.returned_on = ''
                      ORDER BY l.returned_on <> '', l.returned_on DESC, l.borrower COLLATE NOCASE, l.lent_on, Comic",
                    If(includeReturned, 1, 0))
            End Using
        End Function

        ''' <summary>Everyone you've lent to, most recent first.</summary>
        Public Function Borrowers() As List(Of String)
            Using conn = Open()
                Return Query(conn, "SELECT borrower FROM loans GROUP BY borrower ORDER BY MAX(id) DESC").Rows.Cast(Of DataRow)().
                    Select(Function(r) CStr(r(0))).ToList()
            End Using
        End Function

        ''' <summary>How many comics are lent out right now.</summary>
        Public Function LentOutCount() As Integer
            Using conn = Open()
                Return Convert.ToInt32(Scalar(conn, "SELECT COUNT(*) FROM loans l JOIN collection col ON col.comic_id = l.comic_id WHERE l.returned_on = ''", Nothing),
                                       CultureInfo.InvariantCulture)
            End Using
        End Function

        ' ---------- your own photos ----------

        ''' <summary>Where photo files are kept: a Photos folder next to the database.</summary>
        Public ReadOnly Property PhotosFolder As String
            Get
                Return Path.Combine(Path.GetDirectoryName(Path.GetFullPath(FilePath)), "Photos")
            End Get
        End Property

        ''' <summary>Notes a photo already saved in PhotosFolder. Returns its id.</summary>
        Public Function AddPhoto(comicId As Long, fileName As String, Optional caption As String = "") As Long
            Using conn = Open()
                Execute(conn, Nothing, "INSERT INTO photos (comic_id, file_name, caption) VALUES ($p0, $p1, $p2)", comicId, Path.GetFileName(fileName), If(caption, "").Trim())
                Return Convert.ToInt64(Scalar(conn, "SELECT last_insert_rowid()", Nothing), CultureInfo.InvariantCulture)
            End Using
        End Function

        Public Function GetPhotos(comicId As Long) As List(Of PhotoInfo)
            Dim list As New List(Of PhotoInfo)
            Using conn = Open()
                Using cmd = Command(conn, "SELECT id, comic_id, file_name, caption FROM photos WHERE comic_id = $p0 ORDER BY id", Nothing, comicId)
                    Using r = cmd.ExecuteReader()
                        While r.Read()
                            list.Add(New PhotoInfo With {.Id = r.GetInt64(0), .ComicId = r.GetInt64(1), .FileName = r.GetString(2), .Caption = r.GetString(3)})
                        End While
                    End Using
                End Using
            End Using
            Return list
        End Function

        Public Sub SetPhotoCaption(photoId As Long, caption As String)
            Using conn = Open()
                Execute(conn, Nothing, "UPDATE photos SET caption = $p1 WHERE id = $p0", photoId, If(caption, "").Trim())
            End Using
        End Sub

        ''' <summary>Forgets a photo and deletes its file.</summary>
        Public Sub RemovePhoto(photoId As Long)
            Dim fileName As String
            Using conn = Open()
                fileName = Convert.ToString(Scalar(conn, "SELECT file_name FROM photos WHERE id = $p0", Nothing, photoId), CultureInfo.InvariantCulture)
                Execute(conn, Nothing, "DELETE FROM photos WHERE id = $p0", photoId)
            End Using
            DeletePhotoFiles({fileName})
        End Sub

        ''' <summary>File names of every photo, for backing them up.</summary>
        Public Function AllPhotoFiles() As List(Of String)
            Using conn = Open()
                Return Query(conn, "SELECT file_name FROM photos ORDER BY id").Rows.Cast(Of DataRow)().Select(Function(r) CStr(r(0))).ToList()
            End Using
        End Function

        Private Function PhotoFilesFor(conn As SqliteConnection, comicIds As IEnumerable(Of Long)) As List(Of String)
            Dim files As New List(Of String)
            For Each id In comicIds
                Using cmd = Command(conn, "SELECT file_name FROM photos WHERE comic_id = $p0", Nothing, id)
                    Using r = cmd.ExecuteReader()
                        While r.Read()
                            files.Add(r.GetString(0))
                        End While
                    End Using
                End Using
            Next
            Return files
        End Function

        Private Sub DeletePhotoFiles(fileNames As IEnumerable(Of String))
            For Each name In fileNames
                If String.IsNullOrEmpty(name) Then Continue For
                Try
                    Dim full = Path.Combine(PhotosFolder, Path.GetFileName(name))
                    If File.Exists(full) Then File.Delete(full)
                Catch ex As IOException
                Catch ex As UnauthorizedAccessException
                End Try
            Next
        End Sub

        ' ---------- boxes, trading and key issues (several comics at once) ----------

        ''' <summary>Puts comics in a box or on a shelf (blank takes them out of one).</summary>
        Public Sub SetLocation(comicIds As IEnumerable(Of Long), location As String)
            UpdateEach(comicIds, "UPDATE collection SET location = $p1 WHERE comic_id = $p0", If(location, "").Trim())
        End Sub

        ''' <summary>Every box and shelf used so far, in order ("Box 2" before "Box 10").</summary>
        Public Function Locations() As List(Of String)
            Using conn = Open()
                Return Query(conn, "SELECT DISTINCT location FROM collection WHERE location <> ''").Rows.Cast(Of DataRow)().
                    Select(Function(r) CStr(r(0))).OrderBy(Function(l) NaturalKey(l), StringComparer.OrdinalIgnoreCase).ToList()
            End Using
        End Function

        ''' <summary>Marks comics as for trade or sale (with an asking price each, if given), or not.</summary>
        Public Sub SetForTrade(comicIds As IEnumerable(Of Long), forTrade As Boolean, Optional askingPrice As Double? = Nothing)
            UpdateEach(comicIds, "UPDATE collection SET for_trade = $p1, asking_price = CASE WHEN $p1 THEN IFNULL($p2, asking_price) END WHERE comic_id = $p0",
                       forTrade, askingPrice)
        End Sub

        ''' <summary>Marks comics as key issues with a note, e.g. "1st appearance of Harley Quinn". Blank un-marks them.</summary>
        Public Sub SetKeyNote(comicIds As IEnumerable(Of Long), note As String)
            UpdateEach(comicIds, "UPDATE comics SET key_note = $p1 WHERE id = $p0", If(note, "").Trim())
        End Sub

        Private Sub UpdateEach(comicIds As IEnumerable(Of Long), sql As String, ParamArray extra() As Object)
            Using conn = Open()
                Using tx = conn.BeginTransaction()
                    For Each id In comicIds
                        Execute(conn, tx, sql, {CObj(id)}.Concat(extra).ToArray())
                    Next
                    tx.Commit()
                End Using
            End Using
        End Sub

        ' "Box 10" sorts after "Box 9".
        Private Shared Function NaturalKey(s As String) As String
            Return System.Text.RegularExpressions.Regex.Replace(s, "\d+", Function(m) m.Value.PadLeft(8, "0"c))
        End Function

        ''' <summary>How many key issues you own.</summary>
        Public Function KeyIssueCount() As Integer
            Using conn = Open()
                Return Convert.ToInt32(Scalar(conn, "SELECT COUNT(*) FROM v_collection WHERE key_note <> ''", Nothing), CultureInfo.InvariantCulture)
            End Using
        End Function

        ' ---------- spending and reading by month ----------

        ''' <summary>
        ''' What you paid for comics bought in each of the last few months (by Date bought), oldest first.
        ''' Labels are like "Oct 2026". Count is how many comics.
        ''' </summary>
        Public Function SpendingByMonth(months As Integer, Optional today As Date? = Nothing) As List(Of ChartItem)
            Return ByMonth(months, If(today, Date.Today),
                "SELECT substr(purchase_date, 1, 7), COUNT(*), IFNULL(SUM(price_paid), 0) FROM v_collection
                 WHERE purchase_date >= $p0 AND purchase_date GLOB '[12][0-9][0-9][0-9]-[01][0-9]*' GROUP BY 1")
        End Function

        ''' <summary>How many comics you marked read in each of the last few months, oldest first.</summary>
        Public Function ReadByMonth(months As Integer, Optional today As Date? = Nothing) As List(Of ChartItem)
            Return ByMonth(months, If(today, Date.Today),
                "SELECT substr(read_on, 1, 7), COUNT(*), 0 FROM v_collection WHERE is_read AND read_on >= $p0 GROUP BY 1")
        End Function

        Private Function ByMonth(months As Integer, today As Date, sql As String) As List(Of ChartItem)
            Dim first = New Date(today.Year, today.Month, 1).AddMonths(-(Math.Max(1, months) - 1))
            Dim found As New Dictionary(Of String, ChartItem)
            Using conn = Open()
                Using cmd = Command(conn, sql, Nothing, first.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
                    Using r = cmd.ExecuteReader()
                        While r.Read()
                            found(r.GetString(0)) = New ChartItem With {.Count = Convert.ToInt32(r.GetValue(1), CultureInfo.InvariantCulture),
                                                                       .Value = Convert.ToDouble(r.GetValue(2), CultureInfo.InvariantCulture)}
                        End While
                    End Using
                End Using
            End Using
            Dim list As New List(Of ChartItem)
            For i = 0 To Math.Max(1, months) - 1
                Dim m = first.AddMonths(i)
                Dim item As ChartItem = Nothing
                If Not found.TryGetValue(m.ToString("yyyy-MM", CultureInfo.InvariantCulture), item) Then item = New ChartItem()
                item.Label = m.ToString("MMM yyyy", CultureInfo.InvariantCulture)
                list.Add(item)
            Next
            Return list
        End Function

        ' ---------- eBay value estimates ----------

        ''' <summary>
        ''' Comics never checked on eBay, then ones last checked more than maxAgeDays ago (oldest first).
        ''' </summary>
        Public Function ComicsNeedingEbay(Optional maxAgeDays As Integer = 30, Optional today As Date? = Nothing) As List(Of EbayTarget)
            Dim cutoff = If(today, Date.Today).AddDays(-maxAgeDays).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            Dim list As New List(Of EbayTarget)
            Using conn = Open()
                For Each r As DataRow In Query(conn,
                    "SELECT comic_id, series, IFNULL(volume, '') AS volume, issue, variant_name, graded_by, grade FROM v_collection
                     WHERE issue <> '' AND (ebay_checked = '' OR ebay_checked < $p0)
                     ORDER BY ebay_checked, series COLLATE NOCASE, issue_sort", cutoff).Rows
                    list.Add(New EbayTarget With {
                        .ComicId = Convert.ToInt64(r("comic_id"), CultureInfo.InvariantCulture),
                        .Series = Convert.ToString(r("series"), CultureInfo.InvariantCulture),
                        .Volume = Convert.ToString(r("volume"), CultureInfo.InvariantCulture),
                        .Issue = Convert.ToString(r("issue"), CultureInfo.InvariantCulture),
                        .VariantName = Convert.ToString(r("variant_name"), CultureInfo.InvariantCulture),
                        .GradedBy = Convert.ToString(r("graded_by"), CultureInfo.InvariantCulture),
                        .Grade = Convert.ToString(r("grade"), CultureInfo.InvariantCulture)})
                Next
            End Using
            Return list
        End Function

        ''' <summary>Saves what eBay said for a comic (no value when too few listings matched).</summary>
        Public Sub SetEbayEstimate(comicId As Long, estimate As EbayEstimate, Optional day As Date? = Nothing)
            UpdateEach({comicId}, "UPDATE collection SET ebay_value = $p1, ebay_count = $p2, ebay_checked = $p3 WHERE comic_id = $p0",
                       estimate.Typical, estimate.Count, If(day, Date.Today).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
        End Sub

        ''' <summary>
        ''' Copies eBay estimates into Value. onlyBlank leaves alone any value you've already filled in.
        ''' Returns how many comics changed.
        ''' </summary>
        Public Function UseEbayEstimates(onlyBlank As Boolean) As Integer
            Using conn = Open()
                Using cmd = Command(conn,
                    "UPDATE collection SET current_value = ebay_value
                     WHERE ebay_value IS NOT NULL AND (NOT $p0 OR current_value IS NULL OR current_value = 0)
                       AND IFNULL(current_value, -1) <> ebay_value", Nothing, onlyBlank)
                    Return cmd.ExecuteNonQuery()
                End Using
            End Using
        End Function

        ''' <summary>How many comics have been checked on eBay, how many got an estimate, and those estimates added up.</summary>
        Public Function EbayTotals() As (Checked As Integer, Found As Integer, Total As Double)
            Using conn = Open()
                Dim r = Query(conn, "SELECT COUNT(*) AS checked, COUNT(ebay_value) AS found, IFNULL(SUM(ebay_value * quantity), 0) AS total
                                     FROM v_collection WHERE ebay_checked <> ''").Rows(0)
                Return (Convert.ToInt32(r("checked"), CultureInfo.InvariantCulture), Convert.ToInt32(r("found"), CultureInfo.InvariantCulture),
                        Convert.ToDouble(r("total"), CultureInfo.InvariantCulture))
            End Using
        End Function

        ' ---------- trade list, pull list and insurance report ----------

        ''' <summary>Comics marked for trade or sale, and spare copies of others, for the share page.</summary>
        Public Function TradeRows() As DataTable
            Using conn = Open()
                Return Query(conn,
                    "SELECT series, volume, issue, variant_name, title, cover_url, key_note,
                            CASE WHEN graded_by <> '' THEN trim(graded_by || ' ' || grade) ELSE condition END AS condition,
                            CASE WHEN for_trade THEN quantity ELSE quantity - 1 END AS available, for_trade, asking_price
                     FROM v_collection
                     WHERE for_trade OR quantity > 1
                     ORDER BY CASE WHEN series LIKE 'The %' THEN substr(series, 5) ELSE series END COLLATE NOCASE,
                              volume, issue_sort, issue, variant, variant_name")
            End Using
        End Function

        ''' <summary>New releases of series you follow, in shops between two days, that you don't own yet.</summary>
        Public Function PullList(fromDay As Date, toDay As Date) As DataTable
            Dim all = GetReleases()
            Dim f = fromDay.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), t = toDay.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            Dim keep = all.Clone()
            For Each r As DataRow In all.Rows
                Dim day = Convert.ToString(r("In shops"), CultureInfo.InvariantCulture)
                If String.CompareOrdinal(day, f) >= 0 AndAlso String.CompareOrdinal(day, t) <= 0 AndAlso CStr(r("Status")) <> "Have it" Then keep.ImportRow(r)
            Next
            Return keep
        End Function

        ''' <summary>Everything worth at least minValue in total (0 for all), with what an insurer wants to know.</summary>
        Public Function InsuranceRows(minValue As Double) As DataTable
            Using conn = Open()
                Return Query(conn,
                    $"SELECT v.comic_id, {ComicLabelSql} AS comic, v.variant_name, v.title, v.publisher, v.cover_date, v.quantity,
                             v.condition, v.graded_by, v.grade, v.grade_label, v.cert_number, v.price_paid, v.current_value, v.total_value,
                             v.purchase_date, v.location, v.key_note, v.barcode
                      FROM v_collection v
                      WHERE IFNULL(v.total_value, 0) >= $p0
                      ORDER BY CASE WHEN v.series LIKE 'The %' THEN substr(v.series, 5) ELSE v.series END COLLATE NOCASE,
                               v.volume, v.issue_sort, v.issue, v.variant, v.variant_name", minValue)
            End Using
        End Function

        ' ---------- share page ----------

        ''' <summary>What you own, for the share page, in the same order as the Collection list.</summary>
        Public Function ShareRows() As DataTable
            Using conn = Open()
                Return Query(conn,
                    "SELECT series, volume, issue, variant_name, title, publisher, cover_date, cover_url, quantity, key_note,
                            CASE WHEN graded_by <> '' THEN trim(graded_by || ' ' || grade) ELSE condition END AS condition,
                            total_value
                     FROM v_collection
                     ORDER BY CASE WHEN series LIKE 'The %' THEN substr(series, 5) ELSE series END COLLATE NOCASE,
                              volume, issue_sort, issue, variant, variant_name")
            End Using
        End Function

    End Class

    ''' <summary>Picks something from your unread comics.</summary>
    Public Module ReadNext

        Public Class Suggestion
            Public Property Item As ReadItem
            ''' <summary>Why it was picked, e.g. "Next in Batman (2016): you've read up to #12."</summary>
            Public Property Reason As String = ""
        End Class

        ''' <summary>
        ''' Suggestions, best first: the very next issue of runs you're part-way through, then other unread
        ''' issues of runs you've started, then the first issue of series you haven't started. Each group is
        ''' shuffled, so asking again gives something different. One suggestion per series.
        ''' </summary>
        Public Function Suggest(items As IEnumerable(Of ReadItem), rng As Random) As List(Of Suggestion)
            Dim nextUp As New List(Of Suggestion), started As New List(Of Suggestion), fresh As New List(Of Suggestion)
            For Each run In items.GroupBy(Function(i) (i.Series.ToUpperInvariant(), i.Volume.ToUpperInvariant()))
                Dim read = run.Where(Function(i) i.IsRead).ToList()
                ' Another cover of an issue you've read counts as read.
                Dim readIssues = New HashSet(Of String)(read.Select(Function(i) i.Issue), StringComparer.OrdinalIgnoreCase)
                Dim unread = run.Where(Function(i) Not i.IsRead AndAlso Not readIssues.Contains(i.Issue)).
                    OrderBy(Function(i) If(i.IssueSort.HasValue, 0, 1)).ThenBy(Function(i) i.IssueSort.GetValueOrDefault()).
                    ThenBy(Function(i) i.Issue, StringComparer.OrdinalIgnoreCase).ThenBy(Function(i) i.ComicId).ToList()
                If unread.Count = 0 Then Continue For
                Dim name = unread(0).SeriesName
                If read.Count = 0 Then
                    Dim owned = run.Select(Function(i) i.Issue).Distinct(StringComparer.OrdinalIgnoreCase).Count()
                    fresh.Add(New Suggestion With {.Item = unread(0),
                        .Reason = If(owned = 1, $"Something new: you haven't read your {name} yet.",
                                     $"Start something new: you have {owned} issues of {name} and haven't read any yet.")})
                    Continue For
                End If
                Dim sorted = read.Where(Function(i) i.IssueSort.HasValue).ToList()
                If sorted.Count = 0 Then
                    started.Add(New Suggestion With {.Item = unread(0), .Reason = $"Carry on with {name}: you've read {read.Count} of the ones you own."})
                    Continue For
                End If
                Dim lastRead = sorted.Max(Function(i) i.IssueSort.Value)
                Dim lastIssue = sorted.First(Function(i) i.IssueSort.Value = lastRead).Issue
                Dim pick = unread.FirstOrDefault(Function(i) i.IssueSort.HasValue AndAlso i.IssueSort.Value > lastRead)
                If pick Is Nothing Then
                    started.Add(New Suggestion With {.Item = unread(0), .Reason = $"Catch up on {name}: you skipped this one."})
                ElseIf pick.IssueSort.Value - lastRead <= 1 Then
                    nextUp.Add(New Suggestion With {.Item = pick, .Reason = $"Next in {name}: you've read up to #{lastIssue}."})
                Else
                    started.Add(New Suggestion With {.Item = pick, .Reason = $"Carry on with {name}: you've read up to #{lastIssue} (you don't have the ones in between)."})
                End If
            Next
            Dim result As New List(Of Suggestion)
            For Each group In {nextUp, started, fresh}
                result.AddRange(group.OrderBy(Function(s) rng.Next()))
            Next
            Return result
        End Function

    End Module

End Namespace
