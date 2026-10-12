Option Strict On

Imports System.Data
Imports System.Globalization
Imports System.IO
Imports System.Text.RegularExpressions

Namespace Data

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

        Public ReadOnly Property Finished As Boolean
            Get
                Return Pages > 0 AndAlso Page >= Pages - 1
            End Get
        End Property

        ''' <summary>"New", "Page 5 of 22" or "Finished".</summary>
        Public ReadOnly Property ProgressText As String
            Get
                If LastRead = "" Then Return "New"
                If Finished Then Return "Finished"
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
                .ComicLabel = CStr(r("label")), .LastRead = CStr(r("last_read"))}).ToList()
        End Function

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
