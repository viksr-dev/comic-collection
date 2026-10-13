Option Strict On

Imports System.Data
Imports System.Globalization
Imports System.IO
Imports System.Text.RegularExpressions

Namespace Data

    ''' <summary>Where you got to in a digital comic, as the phone and computer share it through the relay.</summary>
    Public Class ReadingPlace
        Public Property Page As Integer
        Public Property Pages As Integer
        ''' <summary>When, as an ISO time in UTC (e.g. "2026-10-12T17:55:20Z"). The newer place wins.</summary>
        Public Property At As String = ""
    End Class

    ''' <summary>A series folder on the Digital shelf.</summary>
    Public Class DigitalGroup
        Public Property Name As String = ""
        ''' <summary>Its comics in issue order.</summary>
        Public Property Files As New List(Of DigitalFile)

        Public ReadOnly Property ReadCount As Integer
            Get
                Return Files.Where(Function(f) f.Finished).Count()
            End Get
        End Property

        ''' <summary>The one to carry on with: the first you've started but not finished, else the first unread, else the first.</summary>
        Public ReadOnly Property UpTo As DigitalFile
            Get
                Return If(Files.FirstOrDefault(Function(f) f.LastRead <> "" AndAlso Not f.Finished),
                          If(Files.FirstOrDefault(Function(f) Not f.Finished), Files.FirstOrDefault()))
            End Get
        End Property
    End Class

    ''' <summary>A digital comic file on the Digital shelf.</summary>
    Public Class DigitalFile
        Public Property Id As Long
        Public Property FilePath As String = ""
        Public Property Title As String = ""
        Public Property Kind As String = ""
        Public Property Pages As Integer
        ''' <summary>The page you're up to (0 = the first).</summary>
        Public Property Page As Integer
        Public Property ComicId As Long?
        ''' <summary>The comic it's linked to, e.g. "Batman (1940) #404", or "".</summary>
        Public Property ComicLabel As String = ""
        Public Property LastRead As String = ""
        ''' <summary>The folder you put it in yourself, or "" to go by its title.</summary>
        Public Property Folder As String = ""

        ''' <summary>The series folder it's shown in: the one you picked, or the series from its title.</summary>
        Public ReadOnly Property SeriesFolder As String
            Get
                Return If(Folder <> "", Folder, DigitalLibrary.SeriesFromTitle(Title))
            End Get
        End Property

        ''' <summary>The issue number from its title, for putting a series in order (Nothing if there isn't one).</summary>
        Public ReadOnly Property IssueNumber As Double?
            Get
                Return DigitalLibrary.IssueFromTitle(Title)
            End Get
        End Property

        Public ReadOnly Property Finished As Boolean
            Get
                Return Pages > 0 AndAlso Page >= Pages - 1
            End Get
        End Property

        ''' <summary>"New", "Page 5 of 22" or "Read" once you've reached the last page.</summary>
        Public ReadOnly Property ProgressText As String
            Get
                If LastRead = "" Then Return "New"
                If Finished Then Return "Read"
                Return $"Page {Page + 1} of {Pages}"
            End Get
        End Property
    End Class

    ''' <summary>A video from a YouTube channel.</summary>
    Public Class VideoItem
        Public Property VideoId As String = ""
        Public Property Channel As String = ""
        Public Property Title As String = ""
        Public Property Published As DateTimeOffset
        Public Property Thumbnail As String = ""
        Public Property Seen As Boolean

        Public ReadOnly Property Url As String
            Get
                Return "https://www.youtube.com/watch?v=" & VideoId
            End Get
        End Property
    End Class

    ''' <summary>The folder digital comics are copied into, so they're always there to read.</summary>
    Public Module DigitalLibrary

        Public ReadOnly Property DefaultFolder As String
            Get
                Return Path.Combine(AppSettings.Folder, "Digital comics")
            End Get
        End Property

        ''' <summary>
        ''' Copies a comic file into the folder and returns where it went. A file already in the folder stays put;
        ''' the same file added twice isn't copied twice; a different file with the same name gets " (2)" added.
        ''' </summary>
        Public Function CopyIn(source As String, folder As String) As String
            Directory.CreateDirectory(folder)
            Dim full = Path.GetFullPath(source)
            If String.Equals(Path.GetDirectoryName(full), Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) Then Return full
            Dim name = Path.GetFileNameWithoutExtension(full)
            Dim ext = Path.GetExtension(full)
            Dim target = Path.Combine(folder, name & ext)
            Dim n = 2
            While File.Exists(target)
                If New FileInfo(target).Length = New FileInfo(full).Length AndAlso SameStart(target, full) Then Return target
                target = Path.Combine(folder, $"{name} ({n}){ext}")
                n += 1
            End While
            File.Copy(full, target)
            Return target
        End Function

        Private Function SameStart(a As String, b As String) As Boolean
            Dim bufA(65535) As Byte, bufB(65535) As Byte
            Using fa = File.OpenRead(a), fb = File.OpenRead(b)
                Dim na = fa.Read(bufA, 0, bufA.Length), nb = fb.Read(bufB, 0, bufB.Length)
                Return na = nb AndAlso bufA.AsSpan(0, na).SequenceEqual(bufB.AsSpan(0, nb))
            End Using
        End Function

        ''' <summary>A tidy title from a file name: "Batman_404_(1987)_(digital).cbz" → "Batman 404 (1987)".</summary>
        Public Function TitleFromFileName(fileName As String) As String
            Dim t = Path.GetFileNameWithoutExtension(fileName).Replace("_"c, " "c)
            ' Scanner and release-group tags like "(digital)" or "(Zone-Empire)", but keep a year.
            t = Regex.Replace(t, "\((?!\d{4}\))[^)]*\)|\[[^\]]*\]", "")
            Return Regex.Replace(t, "\s+", " ").Trim()
        End Function

        ''' <summary>
        ''' The series part of a comic's title, for grouping: "Daredevil v1 003 (1964)" gives "Daredevil v1 (1964)",
        ''' "Saga 012" gives "Saga", "Webtoon Ep 5" gives "Webtoon". A title with no number is its own series.
        ''' The phone app's Read tab uses the same rule.
        ''' </summary>
        Public Function SeriesFromTitle(title As String) As String
            Dim t = If(title, "").Trim()
            Dim year = Regex.Match(t, "\((\d{4})\)").Groups(1).Value
            Dim name = Regex.Replace(t, "\([^)]*\)|\[[^\]]*\]", " ")
            ' The last number on its own is the issue; the series is what comes before it.
            Dim m = IssueAtEnd.Match(name)
            If m.Success AndAlso m.Groups(1).Value.Trim() <> "" Then name = m.Groups(1).Value
            ' Words that only lead into the number: "Ep 5", "Issue 12", "Vol. 2", "Chapter 3".
            name = LeadIn.Replace(name, "")
            name = Regex.Replace(name, "\s+", " ").Trim()
            If name = "" Then name = Regex.Replace(Regex.Replace(t, "\(\d{4}\)", ""), "\s+", " ").Trim()
            Return If(year <> "", $"{name} ({year})", name)
        End Function

        ''' <summary>The issue number in a title: the last number on its own, outside brackets ("Daredevil v1 003 (1964)" gives 3).</summary>
        Public Function IssueFromTitle(title As String) As Double?
            Dim name = Regex.Replace(If(title, ""), "\([^)]*\)|\[[^\]]*\]", " ")
            Dim m = IssueAtEnd.Match(name)
            Dim n As Double
            If m.Success AndAlso Double.TryParse(m.Groups(2).Value, Globalization.NumberStyles.Float, Globalization.CultureInfo.InvariantCulture, n) Then Return n
            Return Nothing
        End Function

        Private ReadOnly IssueAtEnd As New Regex("^(.*?)\s*#?\s*\b(\d+(?:\.\d+)?)\b(?!.*\b\d+\b)")
        Private ReadOnly LeadIn As New Regex("(?:[\s\-:,#]+(?:ep|episode|issue|no|nr|number|chapter|ch|part|pt|book|vol|volume|tome)\.?)*[\s\-:,#.]*$", RegexOptions.IgnoreCase)

        ''' <summary>A series' comics in issue order: numbered ones by number, then the rest by title.</summary>
        Public Function InIssueOrder(files As IEnumerable(Of DigitalFile)) As List(Of DigitalFile)
            Return files.OrderBy(Function(f) If(f.IssueNumber.HasValue, 0, 1)).ThenBy(Function(f) f.IssueNumber.GetValueOrDefault()).
                ThenBy(Function(f) f.Title, StringComparer.CurrentCultureIgnoreCase).ThenBy(Function(f) f.Id).ToList()
        End Function

        ''' <summary>How the phone and the computer know they have the same comic: "file name|size", or "" if the file's gone.</summary>
        Public Function PlaceKey(filePath As String) As String
            Try
                Dim info As New FileInfo(filePath)
                Return If(info.Exists, $"{info.Name.ToLowerInvariant()}|{info.Length}", "")
            Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException OrElse TypeOf ex Is ArgumentException
                Return ""
            End Try
        End Function

        ''' <summary>"2026-10-12 17:55:20" (as saved here, UTC) to "2026-10-12T17:55:20Z", and back.</summary>
        Public Function ToIsoTime(saved As String) As String
            Return If(String.IsNullOrEmpty(saved), "", saved.Replace(" "c, "T"c) & "Z")
        End Function

        Public Function FromIsoTime(iso As String) As String
            Dim t = If(iso, "").Replace("T"c, " "c)
            Return If(t.Length >= 19, t.Substring(0, 19), t.TrimEnd("Z"c))
        End Function

        ''' <summary>The comic after this one in its series folder, or Nothing at the end of the folder.</summary>
        Public Function NextInSeries(files As IEnumerable(Of DigitalFile), current As DigitalFile) As DigitalFile
            Dim run = InIssueOrder(files.Where(Function(f) String.Equals(f.SeriesFolder, current.SeriesFolder, StringComparison.CurrentCultureIgnoreCase)))
            Dim i = run.FindIndex(Function(f) f.Id = current.Id)
            Return If(i >= 0 AndAlso i < run.Count - 1, run(i + 1), Nothing)
        End Function

        ''' <summary>
        ''' The shelf grouped into series folders, each in issue order. The series you read most recently comes first,
        ''' then the ones added most recently. A series with only one comic is a group of one.
        ''' </summary>
        Public Function GroupBySeries(files As IEnumerable(Of DigitalFile)) As List(Of DigitalGroup)
            Return files.GroupBy(Function(f) f.SeriesFolder, StringComparer.CurrentCultureIgnoreCase).
                Select(Function(g) New DigitalGroup With {.Name = g.First().SeriesFolder, .Files = InIssueOrder(g)}).
                OrderByDescending(Function(g) g.Files.Max(Function(f) f.LastRead)).
                ThenByDescending(Function(g) g.Files.Max(Function(f) f.Id)).ToList()
        End Function

        ''' <summary>Reads "Batman 404 (1987)" as series "batman", issue "404", year "1987". Nothing if there's no issue number.</summary>
        Public Function ParseFileName(fileName As String) As (Series As String, Issue As String, Year As String)?
            Dim name = Path.GetFileNameWithoutExtension(fileName).Replace("_"c, " "c).Replace("."c, " "c)
            Dim year = Regex.Match(name, "\((\d{4})\)").Groups(1).Value
            name = Regex.Replace(name, "\([^)]*\)|\[[^\]]*\]", " ")
            Dim m = Regex.Match(name, "^(.*?)\s*(?:#|\b)(\d+(?:\.\d+)?)\b(?!.*\b\d+\b)")
            If Not m.Success Then Return Nothing
            Dim series = Regex.Replace(m.Groups(1).Value, "[^\p{L}\p{N}' ]", " ")
            series = Regex.Replace(series, "\s+", " ").Trim().ToLowerInvariant()
            If series = "" Then Return Nothing
            Dim issue = m.Groups(2).Value.TrimStart("0"c)
            If issue = "" OrElse issue.StartsWith("."c) Then issue = "0" & issue
            Return (series, issue, year)
        End Function

    End Module

    Partial Public Class ComicDb

        ' ---------- digital comics ----------

        ''' <summary>Puts a file on the Digital shelf (or finds it if it's already there) and returns its id.</summary>
        Public Function AddDigitalFile(filePath As String, title As String, kind As String, pages As Integer, Optional comicId As Long? = Nothing) As Long
            Using conn = Open()
                Execute(conn, Nothing,
                    "INSERT INTO digital_files (path, title, kind, pages, comic_id) VALUES ($p0, $p1, $p2, $p3, $p4)
                     ON CONFLICT(path) DO UPDATE SET pages = excluded.pages, kind = excluded.kind, comic_id = IFNULL(digital_files.comic_id, excluded.comic_id)",
                    filePath, title, kind, pages, If(comicId.HasValue, CObj(comicId.Value), Nothing))
                Return CLng(Scalar(conn, "SELECT id FROM digital_files WHERE path = $p0", Nothing, filePath))
            End Using
        End Function

        Private Const DigitalSelect As String =
            "SELECT d.id, d.path, d.title, d.kind, d.pages, d.page, d.comic_id, d.last_read,
                    d.folder,
                    CASE WHEN c.id IS NULL THEN '' ELSE s.name || CASE WHEN IFNULL(s.volume, '') <> '' THEN ' (' || s.volume || ')' ELSE '' END || ' #' || c.issue_number END AS label
             FROM digital_files d
             LEFT JOIN comics c ON c.id = d.comic_id
             LEFT JOIN series s ON s.id = c.series_id"

        ''' <summary>Everything on the Digital shelf, the ones you read most recently first, then newest added.</summary>
        Public Function DigitalFiles() As List(Of DigitalFile)
            Using conn = Open()
                Return ToDigital(Query(conn, DigitalSelect & " ORDER BY d.last_read DESC, d.id DESC"))
            End Using
        End Function

        Public Function DigitalFileById(id As Long) As DigitalFile
            Using conn = Open()
                Return ToDigital(Query(conn, DigitalSelect & " WHERE d.id = $p0", id)).FirstOrDefault()
            End Using
        End Function

        ''' <summary>The digital copy of a comic you own, or Nothing.</summary>
        Public Function DigitalForComic(comicId As Long) As DigitalFile
            Using conn = Open()
                Return ToDigital(Query(conn, DigitalSelect & " WHERE d.comic_id = $p0 ORDER BY d.id DESC LIMIT 1", comicId)).FirstOrDefault()
            End Using
        End Function

        Private Shared Function ToDigital(t As DataTable) As List(Of DigitalFile)
            Return t.Rows.Cast(Of DataRow)().Select(Function(r) New DigitalFile With {
                .Id = CLng(r("id")), .FilePath = CStr(r("path")), .Title = CStr(r("title")), .Kind = CStr(r("kind")),
                .Pages = CInt(CLng(r("pages"))), .Page = CInt(CLng(r("page"))),
                .ComicId = If(IsDBNull(r("comic_id")), CType(Nothing, Long?), CLng(r("comic_id"))),
                .ComicLabel = CStr(r("label")), .LastRead = CStr(r("last_read")), .Folder = CStr(r("folder"))}).ToList()
        End Function

        ''' <summary>Where you are in every comic you've started (or just one), keyed for the relay.</summary>
        Public Function ReadingPlaces(Optional onlyId As Long? = Nothing) As Dictionary(Of String, ReadingPlace)
            Dim result As New Dictionary(Of String, ReadingPlace)
            For Each f In DigitalFiles().Where(Function(x) x.LastRead <> "" AndAlso (Not onlyId.HasValue OrElse x.Id = onlyId.Value))
                Dim key = DigitalLibrary.PlaceKey(f.FilePath)
                If key <> "" Then result(key) = New ReadingPlace With {.Page = f.Page, .Pages = f.Pages, .At = DigitalLibrary.ToIsoTime(f.LastRead)}
            Next
            Return result
        End Function

        ''' <summary>Takes up places read on the phone that are newer than here. Returns the comics that moved.</summary>
        Public Function ApplyReadingPlaces(places As IDictionary(Of String, ReadingPlace)) As List(Of DigitalFile)
            Dim moved As New List(Of DigitalFile)
            If places Is Nothing OrElse places.Count = 0 Then Return moved
            For Each f In DigitalFiles()
                Dim p As ReadingPlace = Nothing
                If Not places.TryGetValue(DigitalLibrary.PlaceKey(f.FilePath), p) Then Continue For
                If String.CompareOrdinal(p.At, DigitalLibrary.ToIsoTime(f.LastRead)) <= 0 Then Continue For
                Dim pages = If(p.Pages > 0, p.Pages, f.Pages)
                Dim page = Math.Max(0, Math.Min(p.Page, pages - 1))
                Using conn = Open()
                    Execute(conn, Nothing, "UPDATE digital_files SET page = $p1, pages = $p2, last_read = $p3 WHERE id = $p0",
                            f.Id, page, pages, DigitalLibrary.FromIsoTime(p.At))
                End Using
                f.Page = page
                f.Pages = pages
                f.LastRead = DigitalLibrary.FromIsoTime(p.At)
                moved.Add(f)
            Next
            Return moved
        End Function

        ''' <summary>
        ''' Once a digital comic is finished, its paper copy in your collection (if it's linked to one) is marked read.
        ''' Returns True if that changed anything.
        ''' </summary>
        Public Function MarkPaperCopyRead(digitalId As Long) As Boolean
            Dim f = DigitalFileById(digitalId)
            If f Is Nothing OrElse Not f.ComicId.HasValue OrElse Not f.Finished Then Return False
            Dim c = GetComic(f.ComicId.Value)
            If c Is Nothing OrElse c.IsRead Then Return False
            SetRead({c.ComicId}, True)
            Return True
        End Function

        ''' <summary>Puts digital comics in a folder of your choosing; "" goes back to the folder from their titles.</summary>
        Public Sub SetDigitalFolder(ids As IEnumerable(Of Long), folder As String)
            Using conn = Open()
                Using tx = conn.BeginTransaction()
                    For Each id In ids
                        Execute(conn, tx, "UPDATE digital_files SET folder = $p1 WHERE id = $p0", id, If(folder, "").Trim())
                    Next
                    tx.Commit()
                End Using
            End Using
        End Sub

        ''' <summary>Remembers the page you're up to.</summary>
        Public Sub SaveReadingPlace(id As Long, page As Integer, pages As Integer)
            Using conn = Open()
                Execute(conn, Nothing, "UPDATE digital_files SET page = $p1, pages = $p2, last_read = datetime('now') WHERE id = $p0",
                        id, Math.Max(0, page), pages)
            End Using
        End Sub

        ''' <summary>Links a digital file to a comic you own (Nothing unlinks it).</summary>
        Public Sub LinkDigitalFile(id As Long, comicId As Long?)
            Using conn = Open()
                Execute(conn, Nothing, "UPDATE digital_files SET comic_id = $p1 WHERE id = $p0", id, If(comicId.HasValue, CObj(comicId.Value), Nothing))
            End Using
        End Sub

        Public Sub RenameDigitalFile(id As Long, title As String)
            Using conn = Open()
                Execute(conn, Nothing, "UPDATE digital_files SET title = $p1 WHERE id = $p0", id, title.Trim())
            End Using
        End Sub

        ''' <summary>Every comic you own by a name to pick from, e.g. "Batman (2016) #1" (variants named too).</summary>
        Public Function CollectionLabels() As Dictionary(Of String, Long)
            Dim labels As New Dictionary(Of String, Long)(StringComparer.OrdinalIgnoreCase)
            Using conn = Open()
                For Each r As DataRow In Query(conn,
                        "SELECT comic_id, series || CASE WHEN IFNULL(volume, '') <> '' THEN ' (' || volume || ')' ELSE '' END || ' #' || issue ||
                                CASE WHEN variant_name <> '' THEN ' - ' || variant_name ELSE '' END AS label
                         FROM v_collection ORDER BY series COLLATE NOCASE, volume, issue_sort, issue").Rows
                    labels.TryAdd(CStr(r("label")), CLng(r("comic_id")))
                Next
            End Using
            Return labels
        End Function

        ''' <summary>Takes a file off the shelf (the file itself is left alone; the app deletes it if asked).</summary>
        Public Sub RemoveDigitalFile(id As Long)
            Using conn = Open()
                Execute(conn, Nothing, "DELETE FROM digital_files WHERE id = $p0", id)
            End Using
        End Sub

        ''' <summary>
        ''' The comic in your collection a file name is most likely to be, e.g. "Batman 404 (1987).cbz" is
        ''' Batman #404, the 1987 volume if you have more than one. Nothing when there's no clear match.
        ''' </summary>
        Public Function GuessComicForFile(fileName As String) As Long?
            Dim parsed = DigitalLibrary.ParseFileName(fileName)
            If Not parsed.HasValue Then Return Nothing
            Dim p = parsed.Value
            Dim rows As DataTable
            Using conn = Open()
                rows = Query(conn, "SELECT comic_id, series, IFNULL(volume, '') AS volume, issue FROM v_collection")
            End Using
            Dim plain = Function(s As String) Regex.Replace(Regex.Replace(s.ToLowerInvariant(), "[^\p{L}\p{N}' ]", " "), "\s+", " ").Trim()
            Dim noThe = Function(s As String) If(s.StartsWith("the ", StringComparison.Ordinal), s.Substring(4), s)
            Dim matches = rows.Rows.Cast(Of DataRow)().Where(Function(r)
                                                                   Dim issue = CStr(r("issue")).Trim().TrimStart("0"c)
                                                                   If issue = "" Then issue = "0"
                                                                   Return issue = p.Issue AndAlso noThe(plain(CStr(r("series")))) = noThe(p.Series)
                                                               End Function).ToList()
            If matches.Count > 1 AndAlso p.Year <> "" Then
                Dim byYear = matches.Where(Function(r) CStr(r("volume")).Contains(p.Year)).ToList()
                If byYear.Count > 0 Then matches = byYear
            End If
            If matches.Select(Function(r) CLng(r("comic_id"))).Distinct().Count() <> 1 Then Return Nothing
            Return CLng(matches(0)("comic_id"))
        End Function

        ' ---------- home page ----------

        ''' <summary>
        ''' Comics for a row of covers on the Home page: "recent" (just added), "unread" (waiting to be read,
        ''' newest first) or "key" (key issues, most valuable first). Columns comic_id, label, key_note, cover_url.
        ''' </summary>
        Public Function CoverStrip(which As String, limit As Integer) As DataTable
            Dim where = "1", order = "added_at DESC, comic_id DESC"
            Select Case which
                Case "unread" : where = "is_read = 0"
                Case "changed"
                    where = "changed_at <> ''"
                    order = "changed_at DESC, comic_id DESC"
                Case "key"
                    where = "key_note <> ''"
                    order = "IFNULL(current_value, 0) DESC, added_at DESC"
            End Select
            Using conn = Open()
                Return Query(conn,
                    $"SELECT comic_id, series || ' #' || issue AS label, key_note, cover_url
                      FROM v_collection WHERE {where} ORDER BY {order} LIMIT $p0", limit)
            End Using
        End Function

        ''' <summary>Digital comics started but not finished, most recently read first.</summary>
        Public Function KeepReading(limit As Integer) As List(Of DigitalFile)
            Using conn = Open()
                Return ToDigital(Query(conn, DigitalSelect & " WHERE d.last_read <> '' AND d.page < d.pages - 1 ORDER BY d.last_read DESC LIMIT $p0", limit))
            End Using
        End Function

        ' ---------- videos ----------

        ''' <summary>Saves videos from a channel's feed and returns how many weren't seen before.</summary>
        Public Function SaveVideos(videos As IEnumerable(Of VideoItem), Optional markSeen As Boolean = False) As Integer
            Dim added = 0
            Using conn = Open()
                Using tx = conn.BeginTransaction()
                    For Each v In videos
                        If Scalar(conn, "SELECT 1 FROM videos WHERE video_id = $p0", tx, v.VideoId) Is Nothing Then
                            Execute(conn, tx, "INSERT INTO videos (video_id, channel, title, published, thumbnail, seen) VALUES ($p0, $p1, $p2, $p3, $p4, $p5)",
                                    v.VideoId, v.Channel, v.Title, v.Published.ToString("o", CultureInfo.InvariantCulture), v.Thumbnail, If(markSeen, 1, 0))
                            If Not markSeen Then added += 1
                        Else
                            Execute(conn, tx, "UPDATE videos SET title = $p1, thumbnail = $p2 WHERE video_id = $p0", v.VideoId, v.Title, v.Thumbnail)
                        End If
                    Next
                    tx.Commit()
                End Using
            End Using
            Return added
        End Function

        ''' <summary>The newest videos first.</summary>
        Public Function Videos(Optional limit As Integer = 30) As List(Of VideoItem)
            Using conn = Open()
                Return Query(conn, "SELECT * FROM videos ORDER BY published DESC LIMIT $p0", limit).Rows.Cast(Of DataRow)().
                    Select(Function(r) New VideoItem With {
                        .VideoId = CStr(r("video_id")), .Channel = CStr(r("channel")), .Title = CStr(r("title")),
                        .Published = ParseDate(CStr(r("published"))), .Thumbnail = CStr(r("thumbnail")), .Seen = CLng(r("seen")) <> 0}).ToList()
            End Using
        End Function

        Private Shared Function ParseDate(s As String) As DateTimeOffset
            Dim d As DateTimeOffset
            Return If(DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, d), d, DateTimeOffset.MinValue)
        End Function

        Public Function NewVideoCount() As Integer
            Using conn = Open()
                Return CInt(CLng(Scalar(conn, "SELECT COUNT(*) FROM videos WHERE seen = 0", Nothing)))
            End Using
        End Function

        Public Sub MarkVideosSeen()
            Using conn = Open()
                Execute(conn, Nothing, "UPDATE videos SET seen = 1 WHERE seen = 0")
            End Using
        End Sub

        ''' <summary>Forgets all the videos (when the channels change; they're fetched again).</summary>
        Public Sub ClearVideos()
            Using conn = Open()
                Execute(conn, Nothing, "DELETE FROM videos")
            End Using
        End Sub

    End Class

End Namespace
