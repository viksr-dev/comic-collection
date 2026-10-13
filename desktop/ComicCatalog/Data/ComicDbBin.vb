Option Strict On

Imports System.Data
Imports System.Globalization
Imports System.Text.Json
Imports Microsoft.Data.Sqlite

Namespace Data

    ''' <summary>
    ''' The bin: deleted comics are kept for <see cref="BinDays"/> days with everything that belonged to them
    ''' (copies, photos, loans, sets, writers and artists…), so a delete can be undone or a comic restored later.
    ''' </summary>
    Partial Public Class ComicDb

        Public Const BinDays As Integer = 30

        ' Tables with a row (or rows) per comic that go in the bin with it. Rows keep their own id only where nothing else points at it.
        Private Shared ReadOnly BinTables As String() = {"collection", "comic_creators", "comic_arcs", "wishlist", "ebay_history", "photos", "loans"}

        Private Shared Sub EnsureBin(conn As SqliteConnection)
            ExecuteScript(conn, "CREATE TABLE IF NOT EXISTS bin (
                                   id          INTEGER PRIMARY KEY,
                                   deleted_at  TEXT NOT NULL DEFAULT (datetime('now')),
                                   batch       TEXT NOT NULL DEFAULT '',
                                   label       TEXT NOT NULL DEFAULT '',
                                   data        TEXT NOT NULL)")
        End Sub

        ''' <summary>
        ''' Moves comics to the bin. Returns a name for this delete, which <see cref="RestoreBatch"/> takes to undo it.
        ''' Photo files stay on disk until the bin is emptied.
        ''' </summary>
        Public Function DeleteComics(comicIds As IEnumerable(Of Long)) As String
            Dim batch = Guid.NewGuid().ToString("N")
            Using conn = Open()
                EnsureBin(conn)
                Using tx = conn.BeginTransaction()
                    For Each id In comicIds
                        Dim snapshot = SnapshotComic(conn, tx, id)
                        If snapshot IsNot Nothing Then
                            Execute(conn, tx, "INSERT INTO bin (batch, label, data) VALUES ($p0, $p1, $p2)",
                                    batch, snapshot.Item1, JsonSerializer.Serialize(snapshot.Item2))
                        End If
                        Execute(conn, tx, "DELETE FROM comics WHERE id = $p0", id)
                    Next
                    Execute(conn, tx, "DELETE FROM series WHERE id NOT IN (SELECT series_id FROM comics)")
                    Execute(conn, tx, "DELETE FROM story_sets WHERE id NOT IN (SELECT set_id FROM set_comics)")
                    tx.Commit()
                End Using
            End Using
            Return batch
        End Function

        ''' <summary>A comic's label ("Batman #404") and every row that belongs to it, or Nothing if it isn't there.</summary>
        Private Shared Function SnapshotComic(conn As SqliteConnection, tx As SqliteTransaction, comicId As Long) As Tuple(Of String, Dictionary(Of String, List(Of Dictionary(Of String, Object))))
            Dim data As New Dictionary(Of String, List(Of Dictionary(Of String, Object)))
            data("comics") = Rows(conn, tx, "SELECT * FROM comics WHERE id = $p0", comicId)
            If data("comics").Count = 0 Then Return Nothing
            data("series") = Rows(conn, tx, "SELECT s.*, p.name AS publisher_name FROM series s LEFT JOIN publishers p ON p.id = s.publisher_id
                                             WHERE s.id = $p0", data("comics")(0)("series_id"))
            For Each table In BinTables
                data(table) = Rows(conn, tx, $"SELECT * FROM {table} WHERE comic_id = $p0", comicId)
            Next
            data("set") = Rows(conn, tx, "SELECT st.* FROM set_comics sc JOIN story_sets st ON st.id = sc.set_id WHERE sc.comic_id = $p0", comicId)
            data("digital_files") = Rows(conn, tx, "SELECT id FROM digital_files WHERE comic_id = $p0", comicId)
            Dim s = data("series").FirstOrDefault()
            Dim c = data("comics")(0)
            Dim label = $"{If(s Is Nothing, "", CStr(s("name")))}{If(s Is Nothing OrElse CStr(s("volume")) = "", "", $" ({s("volume")})")} #{c("issue_number")}"
            If CStr(c("variant_name")) <> "" Then label &= $" {c("variant_name")}"
            Return Tuple.Create(label, data)
        End Function

        Private Shared Function Rows(conn As SqliteConnection, tx As SqliteTransaction, sql As String, ParamArray args() As Object) As List(Of Dictionary(Of String, Object))
            Dim list As New List(Of Dictionary(Of String, Object))
            Using cmd = Command(conn, sql, tx, args)
                Using r = cmd.ExecuteReader()
                    While r.Read()
                        Dim row As New Dictionary(Of String, Object)(StringComparer.OrdinalIgnoreCase)
                        For i = 0 To r.FieldCount - 1
                            row(r.GetName(i)) = If(r.IsDBNull(i), Nothing, r.GetValue(i))
                        Next
                        list.Add(row)
                    End While
                End Using
            End Using
            Return list
        End Function

        ''' <summary>What's in the bin, newest first: id, when it was deleted (local time) and the comic.</summary>
        Public Function BinEntries() As DataTable
            Using conn = Open()
                EnsureBin(conn)
                Return Query(conn, "SELECT id, strftime('%Y-%m-%d %H:%M', deleted_at, 'localtime') AS Deleted, label AS Comic, batch
                                    FROM bin ORDER BY deleted_at DESC, id DESC")
            End Using
        End Function

        ''' <summary>Puts back everything deleted together (one Delete). Returns how many came back.</summary>
        Public Function RestoreBatch(batch As String) As Integer
            Using conn = Open()
                EnsureBin(conn)
                Dim ids = Query(conn, "SELECT id FROM bin WHERE batch = $p0", batch).Rows.Cast(Of DataRow)().
                    Select(Function(r) Convert.ToInt64(r(0), CultureInfo.InvariantCulture)).ToList()
                Return RestoreFromBin(ids)
            End Using
        End Function

        ''' <summary>
        ''' Puts comics back from the bin, with their copies, photos, loans and set. One you've since added again
        ''' is left as it is. Returns how many came back.
        ''' </summary>
        Public Function RestoreFromBin(binIds As IEnumerable(Of Long)) As Integer
            Dim restored = 0
            Using conn = Open()
                EnsureBin(conn)
                Using tx = conn.BeginTransaction()
                    For Each binId In binIds
                        Dim json = Convert.ToString(Scalar(conn, "SELECT data FROM bin WHERE id = $p0", tx, binId), CultureInfo.InvariantCulture)
                        If String.IsNullOrEmpty(json) Then Continue For
                        If RestoreComic(conn, tx, ReadSnapshot(json)) Then restored += 1
                        Execute(conn, tx, "DELETE FROM bin WHERE id = $p0", binId)
                    Next
                    tx.Commit()
                End Using
            End Using
            Return restored
        End Function

        Private Shared Function ReadSnapshot(json As String) As Dictionary(Of String, List(Of Dictionary(Of String, Object)))
            Dim raw = JsonSerializer.Deserialize(Of Dictionary(Of String, List(Of Dictionary(Of String, JsonElement))))(json)
            Dim data As New Dictionary(Of String, List(Of Dictionary(Of String, Object)))
            For Each kv In raw
                data(kv.Key) = kv.Value.Select(Function(row)
                                                   Dim d As New Dictionary(Of String, Object)(StringComparer.OrdinalIgnoreCase)
                                                   For Each cell In row
                                                       d(cell.Key) = FromJson(cell.Value)
                                                   Next
                                                   Return d
                                               End Function).ToList()
            Next
            Return data
        End Function

        Private Shared Function FromJson(e As JsonElement) As Object
            Select Case e.ValueKind
                Case JsonValueKind.Null, JsonValueKind.Undefined : Return Nothing
                Case JsonValueKind.String : Return e.GetString()
                Case JsonValueKind.True : Return 1L
                Case JsonValueKind.False : Return 0L
                Case JsonValueKind.Number
                    Dim n As Long
                    If e.TryGetInt64(n) Then Return n
                    Return e.GetDouble()
                Case Else : Return e.GetRawText()
            End Select
        End Function

        Private Shared Function RestoreComic(conn As SqliteConnection, tx As SqliteTransaction, data As Dictionary(Of String, List(Of Dictionary(Of String, Object)))) As Boolean
            Dim comic = data("comics")(0)
            ' The series (made again if it went with its last comic).
            Dim s = data("series")(0)
            Dim seriesId = Scalar(conn, "SELECT id FROM series WHERE name = $p0 AND volume = $p1", tx, s("name"), s("volume"))
            If seriesId Is Nothing Then
                Dim publisherId As Object = Nothing
                If s("publisher_name") IsNot Nothing Then
                    Execute(conn, tx, "INSERT OR IGNORE INTO publishers (name) VALUES ($p0)", s("publisher_name"))
                    publisherId = Scalar(conn, "SELECT id FROM publishers WHERE name = $p0", tx, s("publisher_name"))
                End If
                Execute(conn, tx, "INSERT INTO series (name, volume, start_year, publisher_id, follow) VALUES ($p0, $p1, $p2, $p3, $p4)",
                        s("name"), s("volume"), s("start_year"), publisherId, s("follow"))
                seriesId = Scalar(conn, "SELECT last_insert_rowid()", tx)
            End If
            ' Added again since it was deleted: leave the one you have.
            If Scalar(conn, "SELECT 1 FROM comics WHERE series_id = $p0 AND issue_number = $p1 AND variant = $p2 AND variant_name = $p3", tx,
                      seriesId, comic("issue_number"), comic("variant"), comic("variant_name")) IsNot Nothing Then Return False
            ' Back under its old number if that's free, so digital comics find it again.
            Dim oldId = Convert.ToInt64(comic("id"), CultureInfo.InvariantCulture)
            Dim keepId = Scalar(conn, "SELECT 1 FROM comics WHERE id = $p0", tx, oldId) Is Nothing
            comic("series_id") = seriesId
            If Not keepId Then comic.Remove("id")
            InsertRow(conn, tx, "comics", comic)
            Dim newId = If(keepId, oldId, Convert.ToInt64(Scalar(conn, "SELECT last_insert_rowid()", tx), CultureInfo.InvariantCulture))
            For Each table In BinTables
                Dim list As List(Of Dictionary(Of String, Object)) = Nothing
                If Not data.TryGetValue(table, list) Then Continue For
                For Each row In list
                    row("comic_id") = newId
                    If table <> "comic_creators" AndAlso table <> "comic_arcs" AndAlso table <> "ebay_history" Then row.Remove("id")
                    ' A writer or artist forgotten since then is skipped.
                    If table = "comic_creators" AndAlso Scalar(conn, "SELECT 1 FROM creators WHERE id = $p0", tx, row("creator_id")) Is Nothing Then Continue For
                    InsertRow(conn, tx, table, row, orIgnore:=True)
                Next
            Next
            Dim sets As List(Of Dictionary(Of String, Object)) = Nothing
            If data.TryGetValue("set", sets) AndAlso sets.Count > 0 Then
                Dim st = sets(0)
                Dim setId = Scalar(conn, "SELECT id FROM story_sets WHERE name = $p0", tx, st("name"))
                If setId Is Nothing Then
                    st.Remove("id")
                    InsertRow(conn, tx, "story_sets", st)
                    setId = Scalar(conn, "SELECT last_insert_rowid()", tx)
                End If
                Execute(conn, tx, "INSERT OR IGNORE INTO set_comics (comic_id, set_id) VALUES ($p0, $p1)", newId, setId)
            End If
            Dim files As List(Of Dictionary(Of String, Object)) = Nothing
            If data.TryGetValue("digital_files", files) Then
                For Each f In files
                    Execute(conn, tx, "UPDATE digital_files SET comic_id = $p0 WHERE id = $p1 AND comic_id IS NULL", newId, f("id"))
                Next
            End If
            Return True
        End Function

        ''' <summary>Inserts a saved row, leaving out any column the table no longer has.</summary>
        Private Shared Sub InsertRow(conn As SqliteConnection, tx As SqliteTransaction, table As String, row As Dictionary(Of String, Object),
                                     Optional orIgnore As Boolean = False)
            Dim existing = Rows(conn, tx, $"SELECT name FROM pragma_table_info('{table}')").Select(Function(r) CStr(r("name"))).
                ToHashSet(StringComparer.OrdinalIgnoreCase)
            Dim cols = row.Keys.Where(Function(k) existing.Contains(k)).ToList()
            Dim sql = $"INSERT {If(orIgnore, "OR IGNORE ", "")}INTO {table} ({String.Join(", ", cols.Select(Function(c) $"[{c}]"))})
                        VALUES ({String.Join(", ", cols.Select(Function(c, i) $"$p{i}"))})"
            Execute(conn, tx, sql, cols.Select(Function(c) row(c)).ToArray())
        End Sub

        ''' <summary>Empties the bin (or just what's been there longer than <paramref name="olderThanDays"/>), deleting photo files nothing uses any more.</summary>
        Public Function EmptyBin(Optional olderThanDays As Integer = 0) As Integer
            Dim photoFiles As New List(Of String)
            Dim removed As Integer
            Using conn = Open()
                EnsureBin(conn)
                Dim cutoff = $"-{olderThanDays} days"
                Dim old = Query(conn, "SELECT id, data FROM bin WHERE $p0 = 0 OR deleted_at < datetime('now', $p1)", olderThanDays, cutoff)
                For Each r As DataRow In old.Rows
                    Dim data = ReadSnapshot(CStr(r("data")))
                    Dim photos As List(Of Dictionary(Of String, Object)) = Nothing
                    If data.TryGetValue("photos", photos) Then photoFiles.AddRange(photos.Select(Function(p) Convert.ToString(p("file_name"), CultureInfo.InvariantCulture)))
                    Execute(conn, Nothing, "DELETE FROM bin WHERE id = $p0", r("id"))
                Next
                removed = old.Rows.Count
                ' A photo file a restored comic still uses stays.
                photoFiles = photoFiles.Where(Function(f) Scalar(conn, "SELECT 1 FROM photos WHERE file_name = $p0", Nothing, f) Is Nothing).ToList()
            End Using
            DeletePhotoFiles(photoFiles)
            Return removed
        End Function

    End Class

End Namespace
