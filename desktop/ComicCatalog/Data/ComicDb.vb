Option Strict On

Imports System.Data
Imports System.Globalization
Imports System.IO
Imports System.Text.RegularExpressions
Imports Microsoft.Data.Sqlite

Namespace Data

    ''' <summary>Everything the app reads from and writes to the SQLite database file.</summary>
    Public Class ComicDb

        Public ReadOnly Property FilePath As String

        Public Sub New(filePath As String)
            Me.FilePath = filePath
            Dim folder = Path.GetDirectoryName(filePath)
            If Not String.IsNullOrEmpty(folder) Then Directory.CreateDirectory(folder)
            Using conn = Open()
                ExecuteScript(conn, ReadResource("schema.sql"))
            End Using
        End Sub

        Private Function Open() As SqliteConnection
            Dim conn As New SqliteConnection(New SqliteConnectionStringBuilder With {.DataSource = FilePath}.ToString())
            conn.Open()
            ExecuteScript(conn, "PRAGMA foreign_keys = ON;")
            Return conn
        End Function

        Private Shared Function ReadResource(name As String) As String
            Dim asm = GetType(ComicDb).Assembly
            Using s = asm.GetManifestResourceStream(name)
                If s Is Nothing Then Throw New InvalidOperationException($"Missing built-in file {name}.")
                Using r As New StreamReader(s)
                    Return r.ReadToEnd()
                End Using
            End Using
        End Function

        Private Shared Sub ExecuteScript(conn As SqliteConnection, sql As String, Optional tx As SqliteTransaction = Nothing)
            Using cmd = conn.CreateCommand()
                cmd.CommandText = sql
                cmd.Transaction = tx
                cmd.ExecuteNonQuery()
            End Using
        End Sub

        Private Shared Function Command(conn As SqliteConnection, sql As String, tx As SqliteTransaction, ParamArray args() As Object) As SqliteCommand
            Dim cmd = conn.CreateCommand()
            cmd.CommandText = sql
            cmd.Transaction = tx
            For i = 0 To args.Length - 1
                cmd.Parameters.AddWithValue($"$p{i}", If(args(i), DBNull.Value))
            Next
            Return cmd
        End Function

        Private Shared Function Scalar(conn As SqliteConnection, sql As String, tx As SqliteTransaction, ParamArray args() As Object) As Object
            Using cmd = Command(conn, sql, tx, args)
                Dim v = cmd.ExecuteScalar()
                Return If(v Is DBNull.Value, Nothing, v)
            End Using
        End Function

        Private Shared Function Query(conn As SqliteConnection, sql As String, ParamArray args() As Object) As DataTable
            Using cmd = Command(conn, sql, Nothing, args)
                Using reader = cmd.ExecuteReader()
                    ' Built by hand: DataTable.Load copies the tables' unique keys, which a joined list breaks.
                    Dim table As New DataTable()
                    For i = 0 To reader.FieldCount - 1
                        table.Columns.Add(reader.GetName(i), GetType(Object))
                    Next
                    Dim values(reader.FieldCount - 1) As Object
                    While reader.Read()
                        reader.GetValues(values)
                        table.Rows.Add(values)
                    End While
                    Return table
                End Using
            End Using
        End Function

        ' ---------- collection ----------

        ''' <summary>Comics you own, filtered by any words in series, title, publisher, issue or barcode.</summary>
        Public Function SearchCollection(text As String) As DataTable
            Dim q = If(text, "").Trim()
            Using conn = Open()
                Return Query(conn,
                    "SELECT collection_id, comic_id, series AS Series, volume AS Volume, issue AS Issue,
                            variant_name AS [Cover / variant], title AS [Story title], publisher AS Publisher,
                            cover_date AS [Cover date], quantity AS Copies, condition AS Condition,
                            price_paid AS [Paid], total_value AS [Value], cover_url
                     FROM v_collection
                     WHERE $p0 = ''
                        OR series LIKE '%' || $p0 || '%' OR title LIKE '%' || $p0 || '%'
                        OR publisher LIKE '%' || $p0 || '%' OR variant_name LIKE '%' || $p0 || '%'
                        OR notes LIKE '%' || $p0 || '%' OR issue = $p0 OR barcode LIKE $p0 || '%'
                     ORDER BY CASE WHEN series LIKE 'The %' THEN substr(series, 5) ELSE series END COLLATE NOCASE,
                              volume, issue_sort, issue, variant, variant_name", q)
            End Using
        End Function

        Public Function GetComic(comicId As Long) As ComicRecord
            Using conn = Open()
                Using cmd = Command(conn, "SELECT * FROM v_collection WHERE comic_id = $p0", Nothing, comicId)
                    Using r = cmd.ExecuteReader()
                        If Not r.Read() Then Return Nothing
                        Return New ComicRecord With {
                            .ComicId = comicId,
                            .Series = Str(r, "series"), .Volume = Str(r, "volume"), .Issue = Str(r, "issue"),
                            .CoverLetter = Str(r, "variant"), .VariantName = Str(r, "variant_name"),
                            .Title = Str(r, "title"), .Publisher = Str(r, "publisher"),
                            .CoverDate = Str(r, "cover_date"), .Format = Str(r, "format"),
                            .Barcode = Str(r, "barcode"), .CoverUrl = Str(r, "cover_url"),
                            .MetronId = If(r.IsDBNull(r.GetOrdinal("metron_id")), CType(Nothing, Long?), r.GetInt64(r.GetOrdinal("metron_id"))),
                            .Quantity = r.GetInt32(r.GetOrdinal("quantity")),
                            .Condition = Str(r, "condition"),
                            .PricePaid = Num(r, "price_paid"), .CurrentValue = Num(r, "current_value"),
                            .PurchaseDate = Str(r, "purchase_date"), .Notes = Str(r, "notes")}
                    End Using
                End Using
            End Using
        End Function

        Private Shared Function Str(r As SqliteDataReader, col As String) As String
            Dim i = r.GetOrdinal(col)
            Return If(r.IsDBNull(i), "", Convert.ToString(r.GetValue(i), CultureInfo.InvariantCulture))
        End Function

        Private Shared Function Num(r As SqliteDataReader, col As String) As Double?
            Dim i = r.GetOrdinal(col)
            Return If(r.IsDBNull(i), CType(Nothing, Double?), r.GetDouble(i))
        End Function

        ''' <summary>Comic you already own with this barcode (the main 12 digits are enough), or Nothing.</summary>
        Public Function FindByBarcode(code As String) As ComicRecord
            Dim p = Barcode.Parse(code)
            If p.Upc = "" Then Return Nothing
            Using conn = Open()
                Dim id = Scalar(conn,
                    "SELECT comic_id FROM v_collection WHERE barcode = $p0 OR ($p1 = '' AND barcode LIKE $p2 || '%') LIMIT 1",
                    Nothing, p.Full, p.Addon, p.Upc)
                If id Is Nothing Then Return Nothing
                Return GetComic(Convert.ToInt64(id, CultureInfo.InvariantCulture))
            End Using
        End Function

        ''' <summary>Adds or updates a comic you own. Returns its comic id.</summary>
        Public Function SaveComic(c As ComicRecord, Optional addCopies As Boolean = False) As Long
            Using conn = Open()
                Using tx = conn.BeginTransaction()
                    Dim id = SaveComic(conn, tx, c, addCopies)
                    tx.Commit()
                    Return id
                End Using
            End Using
        End Function

        Private Function SaveComic(conn As SqliteConnection, tx As SqliteTransaction, c As ComicRecord, addCopies As Boolean) As Long
            If String.IsNullOrWhiteSpace(c.Series) Then Throw New ArgumentException("The series name is required.")

            Dim publisherId As Object = Nothing
            If c.Publisher.Trim() <> "" Then
                Execute(conn, tx, "INSERT OR IGNORE INTO publishers (name) VALUES ($p0)", c.Publisher.Trim())
                publisherId = Scalar(conn, "SELECT id FROM publishers WHERE name = $p0", tx, c.Publisher.Trim())
            End If

            Execute(conn, tx, "INSERT OR IGNORE INTO series (name, volume, start_year, publisher_id) VALUES ($p0, $p1, $p2, $p3)",
                    c.Series.Trim(), c.Volume.Trim(), YearOf(c.Volume, c.CoverDate), publisherId)
            Dim seriesId = Convert.ToInt64(Scalar(conn, "SELECT id FROM series WHERE name = $p0 AND volume = $p1", tx, c.Series.Trim(), c.Volume.Trim()), CultureInfo.InvariantCulture)
            If publisherId IsNot Nothing Then
                Execute(conn, tx, "UPDATE series SET publisher_id = $p0 WHERE id = $p1 AND publisher_id IS NULL", publisherId, seriesId)
            End If

            Dim comicId = c.ComicId
            If comicId = 0 Then
                Dim existing = Scalar(conn,
                    "SELECT id FROM comics WHERE series_id = $p0 AND issue_number = $p1 AND variant = $p2 AND variant_name = $p3",
                    tx, seriesId, c.Issue.Trim(), c.CoverLetter.Trim(), c.VariantName.Trim())
                If existing IsNot Nothing Then comicId = Convert.ToInt64(existing, CultureInfo.InvariantCulture)
            End If

            Dim values As Object() = {seriesId, c.Issue.Trim(), IssueSort(c.Issue), c.CoverLetter.Trim(), c.VariantName.Trim(),
                                      c.Title.Trim(), c.CoverDate.Trim(), c.Format.Trim(), Barcode.OnlyDigits(c.Barcode),
                                      c.MetronId, c.CoverUrl.Trim()}
            If comicId = 0 Then
                Execute(conn, tx, "INSERT INTO comics (series_id, issue_number, issue_sort, variant, variant_name, title, cover_date, format, barcode, metron_id, cover_url)
                                   VALUES ($p0, $p1, $p2, $p3, $p4, $p5, $p6, $p7, $p8, $p9, $p10)", values)
                comicId = Convert.ToInt64(Scalar(conn, "SELECT last_insert_rowid()", tx), CultureInfo.InvariantCulture)
            Else
                Execute(conn, tx, "UPDATE comics SET series_id = $p0, issue_number = $p1, issue_sort = $p2, variant = $p3, variant_name = $p4,
                                   title = $p5, cover_date = $p6, format = $p7, barcode = $p8, metron_id = $p9, cover_url = $p10 WHERE id = $p11",
                        values.Concat({CObj(comicId)}).ToArray())
            End If

            Dim owned = Scalar(conn, "SELECT quantity FROM collection WHERE comic_id = $p0", tx, comicId)
            Dim qty = Math.Max(1, c.Quantity)
            If owned Is Nothing Then
                Execute(conn, tx, "INSERT INTO collection (comic_id, quantity, condition, price_paid, current_value, purchase_date, notes)
                                   VALUES ($p0, $p1, $p2, $p3, $p4, $p5, $p6)",
                        comicId, qty, c.Condition, c.PricePaid, c.CurrentValue, c.PurchaseDate.Trim(), c.Notes.Trim())
            Else
                If addCopies Then qty += Convert.ToInt32(owned, CultureInfo.InvariantCulture)
                Execute(conn, tx, "UPDATE collection SET quantity = $p1, condition = $p2, price_paid = $p3, current_value = $p4,
                                   purchase_date = $p5, notes = $p6 WHERE comic_id = $p0",
                        comicId, qty, c.Condition, c.PricePaid, c.CurrentValue, c.PurchaseDate.Trim(), c.Notes.Trim())
            End If

            ' A comic you now own comes off the wishlist.
            Execute(conn, tx, "DELETE FROM wishlist WHERE comic_id = $p0 OR (comic_id IS NULL AND series_name = $p1 COLLATE NOCASE AND issue_number = $p2)",
                    comicId, c.Series.Trim(), c.Issue.Trim())
            Return comicId
        End Function

        Private Shared Sub Execute(conn As SqliteConnection, tx As SqliteTransaction, sql As String, ParamArray args() As Object)
            Using cmd = Command(conn, sql, tx, args)
                cmd.ExecuteNonQuery()
            End Using
        End Sub

        ''' <summary>Removes comics from your collection (and from the database).</summary>
        Public Sub DeleteComics(comicIds As IEnumerable(Of Long))
            Using conn = Open()
                Using tx = conn.BeginTransaction()
                    For Each id In comicIds
                        Execute(conn, tx, "DELETE FROM comics WHERE id = $p0", id)
                    Next
                    Execute(conn, tx, "DELETE FROM series WHERE id NOT IN (SELECT series_id FROM comics)")
                    tx.Commit()
                End Using
            End Using
        End Sub

        Public Function GetStats() As CollectionStats
            Using conn = Open()
                Using cmd = Command(conn, "SELECT COUNT(*), IFNULL(SUM(quantity), 0), IFNULL(SUM(total_value), 0), IFNULL(SUM(price_paid), 0) FROM v_collection", Nothing)
                    Using r = cmd.ExecuteReader()
                        r.Read()
                        Return New CollectionStats With {.Comics = r.GetInt32(0), .Copies = r.GetInt32(1), .TotalValue = r.GetDouble(2), .TotalPaid = r.GetDouble(3)}
                    End Using
                End Using
            End Using
        End Function

        ' ---------- runs and gaps ----------

        ''' <summary>Series you own, with how many issues are missing between your first and last issue.</summary>
        Public Function SeriesWithGaps() As DataTable
            Using conn = Open()
                Return Query(conn,
                    "WITH runs AS (
                       SELECT series, volume,
                              MIN(CAST(issue_sort AS INTEGER)) AS first, MAX(CAST(issue_sort AS INTEGER)) AS last,
                              COUNT(DISTINCT CAST(issue_sort AS INTEGER)) AS have
                       FROM v_collection
                       WHERE issue_sort IS NOT NULL AND issue_sort = CAST(issue_sort AS INTEGER)
                       GROUP BY series, volume)
                     SELECT series AS Series, volume AS Volume, first AS [First], last AS [Last], have AS [Have],
                            (last - first + 1) - have AS Missing
                     FROM runs WHERE (last - first + 1) > have
                     ORDER BY Missing DESC, series")
            End Using
        End Function

        ''' <summary>Whole-numbered issues missing between the first and last issue you own of a series.</summary>
        Public Function MissingIssues(series As String, volume As String) As List(Of Integer)
            Dim result As New List(Of Integer)
            Using conn = Open()
                Using cmd = Command(conn,
                    "WITH RECURSIVE
                       owned AS (SELECT DISTINCT CAST(issue_sort AS INTEGER) AS n FROM v_collection
                                 WHERE series = $p0 AND volume = $p1 AND issue_sort = CAST(issue_sort AS INTEGER)),
                       run(n) AS (SELECT MIN(n) FROM owned UNION ALL SELECT n + 1 FROM run WHERE n < (SELECT MAX(n) FROM owned))
                     SELECT n FROM run WHERE n IS NOT NULL AND n NOT IN (SELECT n FROM owned)", Nothing, series, If(volume, ""))
                    Using r = cmd.ExecuteReader()
                        While r.Read()
                            result.Add(r.GetInt32(0))
                        End While
                    End Using
                End Using
            End Using
            Return result
        End Function

        ' ---------- wishlist ----------

        Public Function GetWishlist() As DataTable
            Using conn = Open()
                Return Query(conn,
                    "SELECT w.id, w.priority AS Priority, COALESCE(s.name, w.series_name) AS Series,
                            COALESCE(c.issue_number, w.issue_number) AS Issue, w.max_price AS [Max price], w.notes AS Notes
                     FROM wishlist w
                     LEFT JOIN comics c ON c.id = w.comic_id
                     LEFT JOIN series s ON s.id = c.series_id
                     ORDER BY w.priority, Series COLLATE NOCASE, CAST(Issue AS REAL), Issue")
            End Using
        End Function

        Public Sub AddWish(series As String, issue As String, priority As Integer, maxPrice As Double?, notes As String)
            Using conn = Open()
                Execute(conn, Nothing, "INSERT INTO wishlist (series_name, issue_number, priority, max_price, notes) VALUES ($p0, $p1, $p2, $p3, $p4)",
                        series.Trim(), issue.Trim(), Math.Min(3, Math.Max(1, priority)), maxPrice, If(notes, "").Trim())
            End Using
        End Sub

        Public Sub RemoveWishes(ids As IEnumerable(Of Long))
            Using conn = Open()
                For Each id In ids
                    Execute(conn, Nothing, "DELETE FROM wishlist WHERE id = $p0", id)
                Next
            End Using
        End Sub

        ' ---------- import ----------

        ''' <summary>Loads the built-in sample comics. Only allowed into an empty collection.</summary>
        Public Function LoadSampleData() As Boolean
            Using conn = Open()
                If Convert.ToInt64(Scalar(conn, "SELECT COUNT(*) FROM comics", Nothing), CultureInfo.InvariantCulture) > 0 Then Return False
                Using tx = conn.BeginTransaction()
                    ExecuteScript(conn, ReadResource("sample-data.sql"), tx)
                    tx.Commit()
                End Using
                Return True
            End Using
        End Function

        ''' <summary>
        ''' Imports the spreadsheet (CSV) exported from the phone app's Settings screen.
        ''' Comics already in the database are left alone, so importing again doesn't double up.
        ''' Returns (added, skipped).
        ''' </summary>
        Public Function ImportPhoneCsv(csvText As String) As (Added As Integer, Skipped As Integer)
            Dim rows = Csv.Parse(csvText)
            If rows.Count = 0 Then Return (0, 0)
            Dim header = rows(0).Select(Function(h) h.Trim().ToLowerInvariant()).ToList()
            If Not header.Contains("series") OrElse Not header.Contains("issue") Then
                Throw New InvalidDataException("This doesn't look like a spreadsheet exported from the Comic Collection app (no Series and Issue columns).")
            End If
            Dim col = Function(row As List(Of String), name As String) As String
                          Dim i = header.IndexOf(name)
                          Return If(i >= 0 AndAlso i < row.Count, row(i).Trim(), "")
                      End Function

            Dim added = 0, skipped = 0
            Using conn = Open()
                Using tx = conn.BeginTransaction()
                    For Each row In rows.Skip(1)
                        Dim series = col(row, "series")
                        If series = "" Then Continue For
                        Dim code = col(row, "variant code")
                        Dim c As New ComicRecord With {
                            .Series = series, .Volume = col(row, "volume"), .Issue = col(row, "issue"),
                            .CoverLetter = If(code <> "" AndAlso code.All(AddressOf Char.IsLetter), code, ""),
                            .VariantName = col(row, "cover / variant"), .Format = col(row, "format"),
                            .Title = col(row, "story title"), .Publisher = col(row, "publisher"),
                            .CoverDate = col(row, "cover date"), .Condition = col(row, "condition"),
                            .Notes = col(row, "notes"), .Barcode = Barcode.OnlyDigits(col(row, "barcode")),
                            .Quantity = 1}
                        Dim copies As Integer
                        If Integer.TryParse(col(row, "copies"), copies) AndAlso copies > 0 Then c.Quantity = copies
                        Dim metronId As Long
                        If Long.TryParse(col(row, "metron id"), metronId) Then c.MetronId = metronId

                        Dim exists = Scalar(conn,
                            "SELECT c.id FROM comics c JOIN series s ON s.id = c.series_id
                             WHERE s.name = $p0 AND s.volume = $p1 AND c.issue_number = $p2 AND c.variant = $p3 AND c.variant_name = $p4",
                            tx, c.Series, c.Volume, c.Issue, c.CoverLetter, c.VariantName)
                        If exists IsNot Nothing Then
                            skipped += 1
                            Continue For
                        End If
                        Dim comicId = SaveComic(conn, tx, c, False)
                        Dim addedOn = col(row, "date added")
                        If addedOn <> "" Then Execute(conn, tx, "UPDATE collection SET added_at = $p0 WHERE comic_id = $p1", addedOn, comicId)
                        added += 1
                    Next
                    tx.Commit()
                End Using
            End Using
            Return (added, skipped)
        End Function

        ' ---------- helpers ----------

        ''' <summary>The numeric part of an issue number, for sorting: "12" → 12, "1.5" → 1.5, "½" → 0.5, "Annual" → Nothing.</summary>
        Public Shared Function IssueSort(issue As String) As Double?
            Dim s = If(issue, "").Trim()
            If s = "½" OrElse s = "1/2" Then Return 0.5
            Dim m = Regex.Match(s, "^-?\d+(\.\d+)?")
            If Not m.Success Then Return Nothing
            Return Double.Parse(m.Value, CultureInfo.InvariantCulture)
        End Function

        Private Shared Function YearOf(volume As String, coverDate As String) As Object
            For Each s In {volume, coverDate}
                Dim m = Regex.Match(If(s, ""), "\b(19|20)\d{2}\b")
                If m.Success Then Return Integer.Parse(m.Value, CultureInfo.InvariantCulture)
            Next
            Return Nothing
        End Function

    End Class

End Namespace
