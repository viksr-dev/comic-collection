Option Strict On

Imports System.Data
Imports System.Globalization
Imports System.IO
Imports System.Text.RegularExpressions
Imports Microsoft.Data.Sqlite

Namespace Data

    ''' <summary>Everything the app reads from and writes to the SQLite database file.</summary>
    Partial Public Class ComicDb

        Public ReadOnly Property FilePath As String

        Public Sub New(filePath As String)
            Me.FilePath = filePath
            Dim folder = Path.GetDirectoryName(filePath)
            If Not String.IsNullOrEmpty(folder) Then Directory.CreateDirectory(folder)
            Using conn = Open()
                ' Databases made by earlier versions are missing some columns. They're added
                ' first, and the list view rebuilt, so schema.sql finds everything it expects.
                If Scalar(conn, "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = 'comics'", Nothing) IsNot Nothing Then
                    Dim added = False
                    For Each col In {"comics cover_checked INTEGER NOT NULL DEFAULT 0", "comics cover_price REAL",
                                     "comics price_checked INTEGER NOT NULL DEFAULT 0",
                                     "collection graded_by TEXT NOT NULL DEFAULT ''", "collection grade TEXT NOT NULL DEFAULT ''",
                                     "collection grade_label TEXT NOT NULL DEFAULT ''", "collection cert_number TEXT NOT NULL DEFAULT ''",
                                     "collection is_read INTEGER NOT NULL DEFAULT 0", "series follow INTEGER",
                                     "comics arcs_checked INTEGER NOT NULL DEFAULT 0", "story_sets arc_id INTEGER", "story_sets arc_total INTEGER",
                                     "wishlist story_arc TEXT NOT NULL DEFAULT ''", "comics credits_checked INTEGER NOT NULL DEFAULT 0",
                                     "comics key_note TEXT NOT NULL DEFAULT ''", "collection read_on TEXT NOT NULL DEFAULT ''",
                                     "collection location TEXT NOT NULL DEFAULT ''", "collection for_trade INTEGER NOT NULL DEFAULT 0",
                                     "collection asking_price REAL", "collection ebay_value REAL",
                                     "collection ebay_count INTEGER NOT NULL DEFAULT 0", "collection ebay_checked TEXT NOT NULL DEFAULT ''",
                                     "collection ebay_graded REAL", "collection graded_checked TEXT NOT NULL DEFAULT ''",
                                     "collection rating INTEGER NOT NULL DEFAULT 0", "collection review TEXT NOT NULL DEFAULT ''",
                                     "wishlist deal_checked TEXT NOT NULL DEFAULT ''", "digital_files folder TEXT NOT NULL DEFAULT ''"}
                        Dim parts = col.Split(" "c, 2)
                        Dim colName = parts(1).Split(" "c)(0)
                        If Scalar(conn, "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $p0", Nothing, parts(0)) Is Nothing Then Continue For
                        If Scalar(conn, $"SELECT 1 FROM pragma_table_info('{parts(0)}') WHERE name = $p0", Nothing, colName) Is Nothing Then
                            ExecuteScript(conn, $"ALTER TABLE {parts(0)} ADD COLUMN {parts(1)}")
                            added = True
                        End If
                    Next
                    If added Then ExecuteScript(conn, "DROP VIEW IF EXISTS v_collection")
                End If
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

        ''' <summary>
        ''' SQL that's true when the format column is a collected edition rather than a single issue:
        ''' trade paperbacks, hardcovers, omnibuses, graphic novels and the like.
        ''' </summary>
        Public Shared Function TradeSql(Optional column As String = "format") As String
            Dim f = $"IFNULL({column}, '')"
            Return $"({f} LIKE '%trade%' OR {f} LIKE '%paperback%' OR {f} LIKE '%hardcover%' OR {f} LIKE '%hard cover%'
                      OR {f} LIKE '%hardback%' OR {f} LIKE '%graphic novel%' OR {f} LIKE '%omnibus%' OR {f} LIKE '%compendium%'
                      OR {f} LIKE '%collected%' OR {f} LIKE '%deluxe edition%' OR {f} LIKE '%absolute edition%'
                      OR upper(trim({f})) IN ('TPB', 'TP', 'HC', 'HB', 'GN', 'OGN'))"
        End Function

        ''' <summary>True for a trade paperback, hardcover or other collected edition (the same test as TradeSql).</summary>
        Public Shared Function IsTrade(format As String) As Boolean
            Dim f = If(format, "").Trim().ToLowerInvariant()
            If f = "" Then Return False
            If {"tpb", "tp", "hc", "hb", "gn", "ogn"}.Contains(f) Then Return True
            Return {"trade", "paperback", "hardcover", "hard cover", "hardback", "graphic novel", "omnibus", "compendium",
                    "collected", "deluxe edition", "absolute edition"}.Any(Function(w) f.Contains(w))
        End Function

        ''' <summary>An extra AND for queries on v_collection: "issues" leaves trades out, "trades" keeps only trades.</summary>
        Private Shared Function KindFilter(kind As String) As String
            Select Case kind
                Case "issues" : Return $" AND NOT {TradeSql()}"
                Case "trades" : Return $" AND {TradeSql()}"
                Case Else : Return ""
            End Select
        End Function

        ''' <summary>
        ''' Comics you own, found by the words typed (see WordSearch: "daredevil 50", "spiderman") in series, volume,
        ''' title, publisher, cover, issue, barcode, box, key issue note, set, notes or who it's lent to. Comics whose
        ''' issue number was typed come first. read: Nothing for all, True for only the ones you've read, False for only
        ''' unread ones. show: "" for all, "key" for key issues, "trade" for ones for trade or sale (and spares), "lent"
        ''' for ones lent out. kind: "" for everything, "issues" for single issues only, "trades" for trade paperbacks and
        ''' hardcovers only. box: Nothing for every box, "" for comics not in a box, otherwise just that box or shelf.
        ''' The hidden columns _series_sort (series without "The"), _issue_sort (issue as a number), _score (2 or 1 when
        ''' the issue number was typed) and _order (the list's order) are for sorting.
        ''' </summary>
        Public Function SearchCollection(text As String, Optional read As Boolean? = Nothing, Optional show As String = "",
                                         Optional kind As String = "", Optional box As String = Nothing) As DataTable
            Dim words = WordSearch.WordsOf(text)
            Dim table As DataTable
            Using conn = Open()
                table = Query(conn,
                    $"SELECT collection_id, comic_id, series AS Series, volume AS Volume, issue AS Issue,
                            variant_name AS [Cover / variant], title AS [Story title], publisher AS Publisher,
                            cover_date AS [Cover date], quantity AS Copies,
                            CASE WHEN graded_by <> '' THEN trim(graded_by || ' ' || grade) ELSE condition END AS Condition,
                            CASE WHEN is_read THEN '✓' ELSE '' END AS [Read],
                            CASE WHEN EXISTS (SELECT 1 FROM digital_files d WHERE d.comic_id = v_collection.comic_id) THEN 'Digital' ELSE '' END AS Digital,
                            substr('★★★★★', 1, rating) AS Rating,
                            cover_price AS [Cover price], price_paid AS [Paid], total_value AS [Value], ebay_value AS [eBay est.],
                            key_note AS [Key issue], location AS Box,
                            CASE WHEN for_trade THEN 'For trade' WHEN quantity > 1 THEN (quantity - 1) || ' spare' ELSE '' END AS Trade,
                            (SELECT st.name FROM set_comics sc JOIN story_sets st ON st.id = sc.set_id WHERE sc.comic_id = v_collection.comic_id) AS [Set],
                            (SELECT l.borrower FROM loans l WHERE l.comic_id = v_collection.comic_id AND l.returned_on = '' ORDER BY l.id DESC LIMIT 1) AS [Lent to],
                            cover_url,
                            CASE WHEN series LIKE 'The %' THEN substr(series, 5) ELSE series END AS _series_sort,
                            issue_sort AS _issue_sort,
                            variant AS _letter, notes AS _notes, barcode AS _barcode, graded_by AS _graded, format AS _format
                     FROM v_collection
                     WHERE ($p1 IS NULL OR is_read = $p1){KindFilter(kind)}
                       AND ($p2 = '' OR ($p2 = 'key' AND key_note <> '') OR ($p2 = 'trade' AND (for_trade OR quantity > 1))
                            OR ($p2 = 'lent' AND EXISTS (SELECT 1 FROM loans l WHERE l.comic_id = v_collection.comic_id AND l.returned_on = '')))
                       AND ($p0 IS NULL OR trim(IFNULL(location, '')) = $p0 COLLATE NOCASE)
                     ORDER BY CASE WHEN series LIKE 'The %' THEN substr(series, 5) ELSE series END COLLATE NOCASE,
                              volume, issue_sort, issue, variant, variant_name",
                    If(box Is Nothing, Nothing, CObj(box.Trim())), If(read.HasValue, CObj(If(read.Value, 1, 0)), Nothing), If(show, ""))
            End Using
            table.Columns.Add("_score", GetType(Integer))
            table.Columns.Add("_order", GetType(Integer))
            Dim cell = Function(r As DataRow, column As String) Convert.ToString(r(column), CultureInfo.InvariantCulture)
            For Each row In table.Rows.Cast(Of DataRow)().ToList()
                Dim number = cell(row, "Issue"), letter = cell(row, "_letter")
                Dim found = WordSearch.AllFound(words,
                    {cell(row, "Series"), cell(row, "Volume"), cell(row, "Story title"), cell(row, "Publisher"), cell(row, "Cover / variant"),
                     cell(row, "Cover date"), cell(row, "Key issue"), cell(row, "Box"), cell(row, "Set"), cell(row, "Lent to"),
                     cell(row, "_notes"), cell(row, "_barcode"), cell(row, "_graded"), cell(row, "_format")}, number, letter)
                If found Then
                    row("_score") = WordSearch.Score(words, number, letter)
                Else
                    table.Rows.Remove(row)
                End If
            Next
            For Each helper In {"_letter", "_notes", "_barcode", "_graded", "_format"}
                table.Columns.Remove(helper)
            Next
            ' The comics whose issue number was typed come first ("daredevil 50" puts #50 above Daredevil (1950)).
            If table.Rows.Cast(Of DataRow)().Any(Function(r) CInt(r("_score")) > 0) Then
                Dim order = 0
                For Each row As DataRow In table.Rows
                    row("_order") = order
                    order += 1
                Next
                table.DefaultView.Sort = "[_score] DESC, [_order] ASC"
                table = table.DefaultView.ToTable()
            End If
            For i = 0 To table.Rows.Count - 1
                table.Rows(i)("_order") = i
            Next
            table.AcceptChanges()
            Return table
        End Function

        ''' <summary>
        ''' Every box and shelf with how many comics are in it, in order ("Box 2" before "Box 10"), and how many
        ''' aren't in one (Name ""). kind as for SearchCollection.
        ''' </summary>
        Public Function BoxCounts(Optional kind As String = "") As List(Of (Name As String, Count As Integer))
            Dim rows As DataTable
            Using conn = Open()
                rows = Query(conn, $"SELECT trim(IFNULL(location, '')) AS box, COUNT(*) AS n FROM v_collection WHERE 1 = 1{KindFilter(kind)}
                                    GROUP BY trim(IFNULL(location, '')) COLLATE NOCASE")
            End Using
            Return rows.Rows.Cast(Of DataRow)().
                Select(Function(r) (Name:=CStr(r("box")), Count:=Convert.ToInt32(r("n"), CultureInfo.InvariantCulture))).
                OrderBy(Function(b) If(b.Name = "", 1, 0)).ThenBy(Function(b) NaturalKey(b.Name), StringComparer.OrdinalIgnoreCase).ToList()
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
                            .Barcode = Str(r, "barcode"), .CoverUrl = Str(r, "cover_url"), .CoverPrice = Num(r, "cover_price"),
                            .MetronId = If(r.IsDBNull(r.GetOrdinal("metron_id")), CType(Nothing, Long?), r.GetInt64(r.GetOrdinal("metron_id"))),
                            .Quantity = r.GetInt32(r.GetOrdinal("quantity")),
                            .Condition = Str(r, "condition"),
                            .GradedBy = Str(r, "graded_by"), .Grade = Str(r, "grade"),
                            .GradeLabel = Str(r, "grade_label"), .CertNumber = Str(r, "cert_number"),
                            .PricePaid = Num(r, "price_paid"), .CurrentValue = Num(r, "current_value"),
                            .PurchaseDate = Str(r, "purchase_date"), .Notes = Str(r, "notes"),
                            .IsRead = r.GetInt64(r.GetOrdinal("is_read")) <> 0, .ReadOn = Str(r, "read_on"),
                            .Location = Str(r, "location"), .ForTrade = r.GetInt64(r.GetOrdinal("for_trade")) <> 0,
                            .AskingPrice = Num(r, "asking_price"), .KeyNote = Str(r, "key_note"),
                            .EbayValue = Num(r, "ebay_value"), .EbayCount = CInt(r.GetInt64(r.GetOrdinal("ebay_count"))),
                            .EbayChecked = Str(r, "ebay_checked"),
                            .Rating = CInt(r.GetInt64(r.GetOrdinal("rating"))), .Review = Str(r, "review")}
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
                                      c.MetronId, c.CoverUrl.Trim(), c.CoverPrice, c.KeyNote.Trim()}
            If comicId = 0 Then
                Execute(conn, tx, "INSERT INTO comics (series_id, issue_number, issue_sort, variant, variant_name, title, cover_date, format, barcode, metron_id, cover_url, cover_price, key_note)
                                   VALUES ($p0, $p1, $p2, $p3, $p4, $p5, $p6, $p7, $p8, $p9, $p10, $p11, $p12)", values)
                comicId = Convert.ToInt64(Scalar(conn, "SELECT last_insert_rowid()", tx), CultureInfo.InvariantCulture)
            Else
                Execute(conn, tx, "UPDATE comics SET series_id = $p0, issue_number = $p1, issue_sort = $p2, variant = $p3, variant_name = $p4,
                                   title = $p5, cover_date = $p6, format = $p7, barcode = $p8, metron_id = $p9, cover_url = $p10, cover_price = $p11,
                                   key_note = $p12 WHERE id = $p13",
                        values.Concat({CObj(comicId)}).ToArray())
            End If

            Dim owned = Scalar(conn, "SELECT quantity FROM collection WHERE comic_id = $p0", tx, comicId)
            Dim qty = Math.Max(1, c.Quantity)
            If owned Is Nothing Then
                Execute(conn, tx, "INSERT INTO collection (comic_id, quantity, condition, price_paid, current_value, purchase_date, notes,
                                                          graded_by, grade, grade_label, cert_number, is_read,
                                                          read_on, location, for_trade, asking_price, rating, review)
                                   VALUES ($p0, $p1, $p2, $p3, $p4, $p5, $p6, $p7, $p8, $p9, $p10, $p11,
                                           CASE WHEN $p11 THEN $p12 ELSE '' END, $p13, $p14, $p15, $p16, $p17)",
                        comicId, qty, c.Condition, c.PricePaid, c.CurrentValue, c.PurchaseDate.Trim(), c.Notes.Trim(),
                        c.GradedBy.Trim(), c.Grade.Trim(), c.GradeLabel.Trim(), c.CertNumber.Trim(), c.IsRead,
                        c.ReadOn.Trim(), c.Location.Trim(), c.ForTrade, c.AskingPrice, Math.Max(0, Math.Min(5, c.Rating)), c.Review.Trim())
            Else
                If addCopies Then qty += Convert.ToInt32(owned, CultureInfo.InvariantCulture)
                Execute(conn, tx, "UPDATE collection SET quantity = $p1, condition = $p2, price_paid = $p3, current_value = $p4,
                                   purchase_date = $p5, notes = $p6, graded_by = $p7, grade = $p8, grade_label = $p9,
                                   cert_number = $p10, is_read = $p11,
                                   read_on = CASE WHEN NOT $p11 THEN '' WHEN read_on <> '' THEN read_on ELSE $p12 END,
                                   location = $p13, for_trade = $p14, asking_price = $p15, rating = $p16, review = $p17 WHERE comic_id = $p0",
                        comicId, qty, c.Condition, c.PricePaid, c.CurrentValue, c.PurchaseDate.Trim(), c.Notes.Trim(),
                        c.GradedBy.Trim(), c.Grade.Trim(), c.GradeLabel.Trim(), c.CertNumber.Trim(), c.IsRead,
                        c.ReadOn.Trim(), c.Location.Trim(), c.ForTrade, c.AskingPrice, Math.Max(0, Math.Min(5, c.Rating)), c.Review.Trim())
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
            Dim photoFiles As List(Of String)
            Using conn = Open()
                photoFiles = PhotoFilesFor(conn, comicIds)
                Using tx = conn.BeginTransaction()
                    For Each id In comicIds
                        Execute(conn, tx, "DELETE FROM comics WHERE id = $p0", id)
                    Next
                    Execute(conn, tx, "DELETE FROM series WHERE id NOT IN (SELECT series_id FROM comics)")
                    Execute(conn, tx, "DELETE FROM story_sets WHERE id NOT IN (SELECT set_id FROM set_comics)")
                    tx.Commit()
                End Using
            End Using
            DeletePhotoFiles(photoFiles)
        End Sub

        ''' <summary>Marks comics as read (noting the day, for reading goals) or not read.</summary>
        Public Sub SetRead(comicIds As IEnumerable(Of Long), isRead As Boolean, Optional day As Date? = Nothing)
            Dim readOn = If(day, Date.Today).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            Using conn = Open()
                Using tx = conn.BeginTransaction()
                    For Each id In comicIds
                        Execute(conn, tx, "UPDATE collection SET read_on = CASE WHEN NOT $p1 THEN '' WHEN is_read AND read_on <> '' THEN read_on ELSE $p2 END,
                                           is_read = $p1 WHERE comic_id = $p0", id, isRead, readOn)
                    Next
                    tx.Commit()
                End Using
            End Using
        End Sub

        ''' <summary>Totals for everything, or with kind "issues" or "trades" for just single issues or just trades.</summary>
        Public Function GetStats(Optional kind As String = "") As CollectionStats
            Using conn = Open()
                Using cmd = Command(conn, $"SELECT COUNT(*), IFNULL(SUM(quantity), 0), IFNULL(SUM(total_value), 0), IFNULL(SUM(price_paid), 0), IFNULL(SUM(is_read), 0) FROM v_collection WHERE 1 = 1{KindFilter(kind)}", Nothing)
                    Using r = cmd.ExecuteReader()
                        r.Read()
                        Return New CollectionStats With {.Comics = r.GetInt32(0), .Copies = r.GetInt32(1), .TotalValue = r.GetDouble(2), .TotalPaid = r.GetDouble(3), .Read = r.GetInt32(4)}
                    End Using
                End Using
            End Using
        End Function

        ' ---------- story arcs and runs ----------

        ''' <summary>
        ''' Puts comics in a set (made if new), moving them out of any set they were in. With a total value,
        ''' the set's value is changed and split evenly across its comics. Returns the set's id.
        ''' </summary>
        Public Function SaveSet(name As String, comicIds As IEnumerable(Of Long), totalValue As Double?) As Long
            If String.IsNullOrWhiteSpace(name) Then Throw New ArgumentException("The set needs a name.")
            Using conn = Open()
                Using tx = conn.BeginTransaction()
                    Execute(conn, tx, "INSERT OR IGNORE INTO story_sets (name) VALUES ($p0)", name.Trim())
                    Dim setId = Convert.ToInt64(Scalar(conn, "SELECT id FROM story_sets WHERE name = $p0", tx, name.Trim()), CultureInfo.InvariantCulture)
                    Dim affected As New HashSet(Of Long) From {setId}
                    For Each id In comicIds
                        Dim old = Scalar(conn, "SELECT set_id FROM set_comics WHERE comic_id = $p0", tx, id)
                        If old IsNot Nothing Then affected.Add(Convert.ToInt64(old, CultureInfo.InvariantCulture))
                        Execute(conn, tx, "INSERT OR REPLACE INTO set_comics (comic_id, set_id) VALUES ($p0, $p1)", id, setId)
                    Next
                    If totalValue.HasValue Then Execute(conn, tx, "UPDATE story_sets SET total_value = $p1 WHERE id = $p0", setId, totalValue)
                    For Each s In affected
                        SpreadSetValue(conn, tx, s)
                    Next
                    Execute(conn, tx, "DELETE FROM story_sets WHERE id NOT IN (SELECT set_id FROM set_comics)")
                    tx.Commit()
                    Return setId
                End Using
            End Using
        End Function

        ''' <summary>Renames a set and/or changes its value (split across its comics again).</summary>
        Public Sub UpdateSet(setId As Long, name As String, totalValue As Double?)
            Using conn = Open()
                Using tx = conn.BeginTransaction()
                    Execute(conn, tx, "UPDATE story_sets SET name = $p1, total_value = $p2 WHERE id = $p0", setId, name.Trim(), totalValue)
                    SpreadSetValue(conn, tx, setId)
                    tx.Commit()
                End Using
            End Using
        End Sub

        ''' <summary>Removes the set; its comics keep the values they have.</summary>
        Public Sub DeleteSet(setId As Long)
            Using conn = Open()
                Execute(conn, Nothing, "DELETE FROM story_sets WHERE id = $p0", setId)
            End Using
        End Sub

        ''' <summary>Takes comics out of their sets, sharing each set's value among the comics left.</summary>
        Public Sub RemoveFromSets(comicIds As IEnumerable(Of Long))
            Using conn = Open()
                Using tx = conn.BeginTransaction()
                    Dim affected As New HashSet(Of Long)
                    For Each id In comicIds
                        Dim old = Scalar(conn, "SELECT set_id FROM set_comics WHERE comic_id = $p0", tx, id)
                        If old Is Nothing Then Continue For
                        affected.Add(Convert.ToInt64(old, CultureInfo.InvariantCulture))
                        Execute(conn, tx, "DELETE FROM set_comics WHERE comic_id = $p0", id)
                    Next
                    For Each s In affected
                        SpreadSetValue(conn, tx, s)
                    Next
                    Execute(conn, tx, "DELETE FROM story_sets WHERE id NOT IN (SELECT set_id FROM set_comics)")
                    tx.Commit()
                End Using
            End Using
        End Sub

        Private Shared Sub SpreadSetValue(conn As SqliteConnection, tx As SqliteTransaction, setId As Long)
            Execute(conn, tx,
                "UPDATE collection
                 SET current_value = (SELECT total_value FROM story_sets WHERE id = $p0) /
                                     (SELECT COUNT(*) FROM set_comics WHERE set_id = $p0)
                 WHERE comic_id IN (SELECT comic_id FROM set_comics WHERE set_id = $p0)
                   AND (SELECT total_value FROM story_sets WHERE id = $p0) IS NOT NULL", setId)
        End Sub

        ''' <summary>Your sets, with how many comics each has and which series they're from.</summary>
        Public Function GetSets() As DataTable
            Using conn = Open()
                Return Query(conn,
                    "SELECT st.id, st.name AS [Set], COUNT(sc.comic_id) AS Comics,
                            (SELECT group_concat(name, ', ') FROM (SELECT DISTINCT s.name FROM set_comics x
                                JOIN comics c ON c.id = x.comic_id JOIN series s ON s.id = c.series_id WHERE x.set_id = st.id)) AS Series,
                            st.arc_total AS [Whole arc], st.total_value AS Value, st.arc_id
                     FROM story_sets st LEFT JOIN set_comics sc ON sc.set_id = st.id
                     GROUP BY st.id ORDER BY st.name")
            End Using
        End Function

        Public Function SetNames() As List(Of String)
            Return GetSets().Rows.Cast(Of DataRow)().Select(Function(r) CStr(r("Set"))).ToList()
        End Function

        ' ---------- story arcs found on Metron ----------

        ''' <summary>
        ''' Comics you own that Metron knows (they have a Metron number) whose story arcs haven't been looked up,
        ''' or, with withCredits, whose writers and artists haven't been either. NeedsArcs says which.
        ''' </summary>
        Public Function ComicsNeedingArcs(Optional withCredits As Boolean = False) As List(Of (ComicId As Long, MetronId As Long, NeedsArcs As Boolean))
            Dim list As New List(Of (ComicId As Long, MetronId As Long, NeedsArcs As Boolean))
            Using conn = Open()
                Using cmd = Command(conn,
                    "SELECT c.id, c.metron_id, c.arcs_checked = 0 FROM comics c JOIN collection col ON col.comic_id = c.id
                     WHERE c.metron_id IS NOT NULL AND (c.arcs_checked = 0 OR ($p0 = 1 AND c.credits_checked = 0))
                     ORDER BY c.arcs_checked, c.id", Nothing, If(withCredits, 1, 0))
                    Using r = cmd.ExecuteReader()
                        While r.Read()
                            list.Add((r.GetInt64(0), r.GetInt64(1), r.GetInt64(2) <> 0))
                        End While
                    End Using
                End Using
            End Using
            Return list
        End Function

        ''' <summary>How many comics you own are matched to Metron (have a Metron number).</summary>
        Public Function ComicsWithMetronNumber() As Integer
            Using conn = Open()
                Return Convert.ToInt32(Scalar(conn, "SELECT COUNT(*) FROM v_collection WHERE metron_id IS NOT NULL", Nothing), CultureInfo.InvariantCulture)
            End Using
        End Function

        ''' <summary>
        ''' Saves the story arcs a comic is part of (none is fine) so it isn't looked up again.
        ''' With credits (from relay version 8), also saves who wrote and drew it.
        ''' </summary>
        Public Sub SaveArcs(comicId As Long, arcs As IEnumerable(Of MetronArc), Optional credits As IEnumerable(Of MetronCredit) = Nothing)
            Using conn = Open()
                Using tx = conn.BeginTransaction()
                    Execute(conn, tx, "DELETE FROM comic_arcs WHERE comic_id = $p0", comicId)
                    For Each a In arcs
                        Execute(conn, tx, "INSERT OR IGNORE INTO comic_arcs (comic_id, arc_id, arc_name) VALUES ($p0, $p1, $p2)", comicId, a.Id, a.Name.Trim())
                    Next
                    Execute(conn, tx, "UPDATE comics SET arcs_checked = 1 WHERE id = $p0", comicId)
                    If credits IsNot Nothing Then
                        Execute(conn, tx, "DELETE FROM comic_creators WHERE comic_id = $p0", comicId)
                        For Each cr In credits
                            If String.IsNullOrWhiteSpace(cr.Name) Then Continue For
                            Execute(conn, tx, "INSERT OR IGNORE INTO creators (name) VALUES ($p0)", cr.Name.Trim())
                            Dim creatorId = Scalar(conn, "SELECT id FROM creators WHERE name = $p0", tx, cr.Name.Trim())
                            For Each role In If(cr.Roles.Count = 0, New List(Of String) From {""}, cr.Roles)
                                Execute(conn, tx, "INSERT OR IGNORE INTO comic_creators (comic_id, creator_id, role) VALUES ($p0, $p1, $p2)",
                                        comicId, creatorId, role.Trim())
                            Next
                        Next
                        Execute(conn, tx, "UPDATE comics SET credits_checked = 1 WHERE id = $p0", comicId)
                    End If
                    tx.Commit()
                End Using
            End Using
        End Sub

        ''' <summary>Story arcs you own at least two issues of: (arc id, name, how many you own), most owned first.</summary>
        Public Function OwnedArcs() As List(Of (ArcId As Long, Name As String, Owned As Integer))
            Dim list As New List(Of (ArcId As Long, Name As String, Owned As Integer))
            Using conn = Open()
                Using cmd = Command(conn,
                    "SELECT a.arc_id, MAX(a.arc_name), COUNT(*) FROM comic_arcs a JOIN collection col ON col.comic_id = a.comic_id
                     GROUP BY a.arc_id HAVING COUNT(*) >= 2 ORDER BY COUNT(*) DESC, MAX(a.arc_name)", Nothing)
                    Using r = cmd.ExecuteReader()
                        While r.Read()
                            list.Add((r.GetInt64(0), r.GetString(1), r.GetInt32(2)))
                        End While
                    End Using
                End Using
            End Using
            Return list
        End Function

        ''' <summary>
        ''' Makes a set for each story arc you own two or more issues of (or adds to the set already made for it).
        ''' A comic is in one set at most, so comics already in a set stay where they are; bigger arcs are
        ''' filled first. arcTotals gives each arc's full length where known. Returns how many sets were made.
        ''' </summary>
        Public Function MakeArcSets(arcTotals As IDictionary(Of Long, Integer)) As Integer
            Dim made = 0
            Dim arcs = OwnedArcs()
            Using conn = Open()
                Using tx = conn.BeginTransaction()
                    For Each arc In arcs
                        Dim setIdObj = Scalar(conn, "SELECT id FROM story_sets WHERE arc_id = $p0", tx, arc.ArcId)
                        If setIdObj Is Nothing Then setIdObj = Scalar(conn, "SELECT id FROM story_sets WHERE name = $p0", tx, arc.Name)
                        Dim free = Convert.ToInt64(Scalar(conn,
                            "SELECT COUNT(*) FROM comic_arcs a JOIN collection col ON col.comic_id = a.comic_id
                             WHERE a.arc_id = $p0 AND a.comic_id NOT IN (SELECT comic_id FROM set_comics)", tx, arc.ArcId), CultureInfo.InvariantCulture)
                        ' A new set needs at least two comics that aren't in another set already.
                        If setIdObj Is Nothing AndAlso free < 2 Then Continue For
                        If setIdObj Is Nothing Then
                            Execute(conn, tx, "INSERT INTO story_sets (name) VALUES ($p0)", arc.Name)
                            setIdObj = Scalar(conn, "SELECT last_insert_rowid()", tx)
                            made += 1
                        End If
                        Dim setId = Convert.ToInt64(setIdObj, CultureInfo.InvariantCulture)
                        Dim total As Integer
                        Execute(conn, tx, "UPDATE story_sets SET arc_id = COALESCE(arc_id, $p1), arc_total = COALESCE($p2, arc_total) WHERE id = $p0",
                                setId, arc.ArcId, If(arcTotals IsNot Nothing AndAlso arcTotals.TryGetValue(arc.ArcId, total) AndAlso total > 0, CObj(total), Nothing))
                        Execute(conn, tx,
                            "INSERT INTO set_comics (comic_id, set_id)
                             SELECT a.comic_id, $p1 FROM comic_arcs a JOIN collection col ON col.comic_id = a.comic_id
                             WHERE a.arc_id = $p0 AND a.comic_id NOT IN (SELECT comic_id FROM set_comics)", arc.ArcId, setId)
                        SpreadSetValue(conn, tx, setId)
                    Next
                    tx.Commit()
                End Using
            End Using
            Return made
        End Function

        ''' <summary>Words for an eBay search for a set: its name and, for one series, the issue range ("Batman 404-407").</summary>
        Public Function SetSearchWords(setId As Long) As String
            Using conn = Open()
                Dim name = Convert.ToString(Scalar(conn, "SELECT name FROM story_sets WHERE id = $p0", Nothing, setId), CultureInfo.InvariantCulture)
                Dim info = Query(conn,
                    "SELECT COUNT(DISTINCT s.name) AS series_count, MIN(s.name) AS series, MIN(c.issue_sort) AS first, MAX(c.issue_sort) AS last
                     FROM set_comics x JOIN comics c ON c.id = x.comic_id JOIN series s ON s.id = c.series_id WHERE x.set_id = $p0", setId)
                Dim words = If(name, "")
                If info.Rows.Count = 1 AndAlso Convert.ToInt64(info.Rows(0)("series_count"), CultureInfo.InvariantCulture) = 1 AndAlso Not info.Rows(0).IsNull("first") Then
                    Dim series = CStr(info.Rows(0)("series"))
                    Dim first = Convert.ToDouble(info.Rows(0)("first"), CultureInfo.InvariantCulture)
                    Dim last = Convert.ToDouble(info.Rows(0)("last"), CultureInfo.InvariantCulture)
                    If Not words.StartsWith(series, StringComparison.OrdinalIgnoreCase) Then words = series & " " & words
                    words &= " " & first.ToString(CultureInfo.InvariantCulture) & If(last > first, "-" & last.ToString(CultureInfo.InvariantCulture), "")
                End If
                Return words.Replace(":", " ").Replace("  ", " ").Trim()
            End Using
        End Function

        ' ---------- covers ----------

        ''' <summary>
        ''' Comics you own that have no cover picture (or, with withPrices, no cover price) and
        ''' haven't been looked for yet.
        ''' </summary>
        Public Function ComicsNeedingCovers(Optional withPrices As Boolean = True) As List(Of ComicRecord)
            Dim list As New List(Of ComicRecord)
            Using conn = Open()
                Using cmd = Command(conn,
                    "SELECT v.comic_id, v.series, v.volume, v.issue, v.cover_date, v.barcode, v.metron_id
                     FROM v_collection v JOIN comics c ON c.id = v.comic_id
                     WHERE (v.cover_url = '' AND c.cover_checked = 0)
                        OR ($p0 AND c.cover_price IS NULL AND c.price_checked = 0)
                     ORDER BY v.metron_id IS NULL, v.series, v.issue_sort", Nothing, withPrices)
                    Using r = cmd.ExecuteReader()
                        While r.Read()
                            list.Add(New ComicRecord With {
                                .ComicId = r.GetInt64(0), .Series = Str(r, "series"), .Volume = Str(r, "volume"),
                                .Issue = Str(r, "issue"), .CoverDate = Str(r, "cover_date"), .Barcode = Str(r, "barcode"),
                                .MetronId = If(r.IsDBNull(6), CType(Nothing, Long?), r.GetInt64(6))})
                        End While
                    End Using
                End Using
            End Using
            Return list
        End Function

        ''' <summary>
        ''' Saves what "Find covers" found, and notes it was looked for so it isn't looked for again.
        ''' A cover or price you already have is kept. priceChecked is False when the relay can't send prices yet.
        ''' </summary>
        Public Sub SetCover(comicId As Long, url As String, metronId As Long?,
                            Optional coverPrice As Double? = Nothing, Optional priceChecked As Boolean = False)
            Using conn = Open()
                Execute(conn, Nothing,
                    "UPDATE comics SET cover_url = CASE WHEN cover_url = '' THEN $p1 ELSE cover_url END,
                                       metron_id = COALESCE(metron_id, $p2), cover_checked = 1,
                                       cover_price = COALESCE(cover_price, $p3),
                                       price_checked = CASE WHEN $p4 THEN 1 ELSE price_checked END
                     WHERE id = $p0",
                    comicId, If(url, ""), metronId, coverPrice, priceChecked)
            End Using
        End Sub

        ' ---------- runs and gaps ----------

        ''' <summary>Series you own, with how many issues are missing between your first and last issue.</summary>
        Public Function SeriesWithGaps() As DataTable
            Using conn = Open()
                Return Query(conn,
                    $"WITH runs AS (
                       SELECT series, volume,
                              MIN(CAST(issue_sort AS INTEGER)) AS first, MAX(CAST(issue_sort AS INTEGER)) AS last,
                              COUNT(DISTINCT CAST(issue_sort AS INTEGER)) AS have
                       FROM v_collection
                       WHERE issue_sort IS NOT NULL AND issue_sort = CAST(issue_sort AS INTEGER){KindFilter("issues")}
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
                    $"WITH RECURSIVE
                       owned AS (SELECT DISTINCT CAST(issue_sort AS INTEGER) AS n FROM v_collection
                                 WHERE series = $p0 AND volume = $p1 AND issue_sort = CAST(issue_sort AS INTEGER){KindFilter("issues")}),
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
                    "SELECT w.id, w.priority AS Priority, w.story_arc AS [Story arc], COALESCE(s.name, w.series_name) AS Series,
                            COALESCE(c.issue_number, w.issue_number) AS Issue, w.max_price AS [Max price], w.notes AS Notes,
                            (SELECT MIN(d.price) FROM ebay_deals d WHERE d.wish_id = w.id AND NOT d.dismissed) AS [eBay deal (US$)]
                     FROM wishlist w
                     LEFT JOIN comics c ON c.id = w.comic_id
                     LEFT JOIN series s ON s.id = c.series_id
                     ORDER BY w.priority, w.story_arc = '', w.story_arc COLLATE NOCASE, Series COLLATE NOCASE, CAST(Issue AS REAL), Issue")
            End Using
        End Function

        Public Sub AddWish(series As String, issue As String, priority As Integer, maxPrice As Double?, notes As String,
                           Optional storyArc As String = "")
            Using conn = Open()
                Execute(conn, Nothing, "INSERT INTO wishlist (series_name, issue_number, priority, max_price, notes, story_arc) VALUES ($p0, $p1, $p2, $p3, $p4, $p5)",
                        series.Trim(), issue.Trim(), Math.Min(3, Math.Max(1, priority)), maxPrice, If(notes, "").Trim(), If(storyArc, "").Trim())
            End Using
        End Sub

        ''' <summary>
        ''' Puts the issues of a story arc you don't have on the wishlist, labelled with the arc.
        ''' Issues you own (matched by Metron number, or series and issue number) or already want are skipped.
        ''' Returns how many were added.
        ''' </summary>
        Public Function WishMissingFromArc(arcName As String, arcIssues As IEnumerable(Of MetronIssue)) As Integer
            Dim added = 0
            Using conn = Open()
                Using tx = conn.BeginTransaction()
                    For Each i In arcIssues
                        If String.IsNullOrWhiteSpace(i.Series) Then Continue For
                        Dim owned = Scalar(conn,
                            "SELECT 1 FROM comics c JOIN series s ON s.id = c.series_id JOIN collection col ON col.comic_id = c.id
                             WHERE ($p0 > 0 AND c.metron_id = $p0) OR (s.name = $p1 AND c.issue_number = $p2 AND (s.volume = $p3 OR s.volume = '' OR $p3 = ''))
                             LIMIT 1", tx, i.MetronId, i.Series.Trim(), i.Number.Trim(), i.Volume.Trim())
                        If owned IsNot Nothing Then Continue For
                        Dim wanted = Scalar(conn, "SELECT 1 FROM wishlist WHERE series_name = $p0 COLLATE NOCASE AND issue_number = $p1 LIMIT 1",
                                            tx, i.Series.Trim(), i.Number.Trim())
                        If wanted IsNot Nothing Then Continue For
                        Execute(conn, tx, "INSERT INTO wishlist (series_name, issue_number, priority, notes, story_arc) VALUES ($p0, $p1, 2, $p2, $p3)",
                                i.Series.Trim(), i.Number.Trim(), If(i.Volume <> "", $"Volume {i.Volume}", ""), arcName.Trim())
                        added += 1
                    Next
                    tx.Commit()
                End Using
            End Using
            Return added
        End Function

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
        ''' Comics already in the database are left alone, so importing again doesn't double up, except that a
        ''' Box column puts one you already have in that box (the phone's "Box or shelf" and fill-a-box scanning).
        ''' Rows marked "Box only" just move a comic and are never added. Returns (added, skipped, moved).
        ''' </summary>
        Public Function ImportPhoneCsv(csvText As String) As (Added As Integer, Skipped As Integer, Moved As Integer)
            Dim rows = Csv.Parse(csvText)
            If rows.Count = 0 Then Return (0, 0, 0)
            Dim header = rows(0).Select(Function(h) h.Trim().ToLowerInvariant()).ToList()
            If Not header.Contains("series") OrElse Not header.Contains("issue") Then
                Throw New InvalidDataException("This doesn't look like a spreadsheet exported from the Comic Collection app (no Series and Issue columns).")
            End If
            Dim col = Function(row As List(Of String), name As String) As String
                          Dim i = header.IndexOf(name)
                          Return If(i >= 0 AndAlso i < row.Count, row(i).Trim(), "")
                      End Function

            Dim added = 0, skipped = 0, moved = 0
            Using conn = Open()
                Using tx = conn.BeginTransaction()
                    For Each row In rows.Skip(1)
                        Dim series = col(row, "series")
                        If series = "" Then Continue For
                        Dim box = col(row, "box")
                        Dim boxOnly = col(row, "box only") <> ""
                        Dim code = col(row, "variant code")
                        Dim c As New ComicRecord With {
                            .Series = series, .Volume = col(row, "volume"), .Issue = col(row, "issue"),
                            .CoverLetter = If(code <> "" AndAlso code.All(AddressOf Char.IsLetter), code, ""),
                            .VariantName = col(row, "cover / variant"), .Format = col(row, "format"),
                            .Title = col(row, "story title"), .Publisher = col(row, "publisher"),
                            .CoverDate = col(row, "cover date"), .Condition = col(row, "condition"),
                            .Notes = col(row, "notes"), .Barcode = Barcode.OnlyDigits(col(row, "barcode")),
                            .CoverUrl = col(row, "cover image"),
                            .Quantity = 1}
                        Dim copies As Integer
                        If Integer.TryParse(col(row, "copies"), copies) AndAlso copies > 0 Then c.Quantity = copies
                        Dim metronId As Long
                        If Long.TryParse(col(row, "metron id"), metronId) Then c.MetronId = metronId

                        Dim exists = Scalar(conn,
                            "SELECT c.id FROM comics c JOIN series s ON s.id = c.series_id
                             WHERE s.name = $p0 AND s.volume = $p1 AND c.issue_number = $p2 AND c.variant = $p3 AND c.variant_name = $p4",
                            tx, c.Series, c.Volume, c.Issue, c.CoverLetter, c.VariantName)
                        If exists Is Nothing AndAlso box <> "" Then exists = FindForBox(conn, tx, c)
                        If exists IsNot Nothing Then
                            If box <> "" Then
                                Execute(conn, tx, "UPDATE collection SET location = $p0 WHERE comic_id = $p1", box, exists)
                                moved += 1
                            Else
                                skipped += 1
                            End If
                            Continue For
                        End If
                        If boxOnly Then
                            skipped += 1
                            Continue For
                        End If
                        c.Location = box
                        Dim comicId = SaveComic(conn, tx, c, False)
                        Dim addedOn = col(row, "date added")
                        If addedOn <> "" Then Execute(conn, tx, "UPDATE collection SET added_at = $p0 WHERE comic_id = $p1", addedOn, comicId)
                        added += 1
                    Next
                    tx.Commit()
                End Using
            End Using
            Return (added, skipped, moved)
        End Function

        ''' <summary>The comic you own that a phone row means, by Metron issue, barcode, then series, volume and issue (any cover).</summary>
        Private Shared Function FindForBox(conn As SqliteConnection, tx As SqliteTransaction, c As ComicRecord) As Object
            If c.MetronId.HasValue Then
                Dim id = Scalar(conn, "SELECT c.id FROM comics c JOIN collection k ON k.comic_id = c.id WHERE c.metron_id = $p0 LIMIT 1", tx, c.MetronId.Value)
                If id IsNot Nothing Then Return id
            End If
            Dim code = Barcode.OnlyDigits(c.Barcode)
            If code.Length > 12 Then
                Dim id = Scalar(conn, "SELECT c.id FROM comics c JOIN collection k ON k.comic_id = c.id WHERE c.barcode = $p0 LIMIT 1", tx, code)
                If id IsNot Nothing Then Return id
            End If
            Return Scalar(conn,
                "SELECT c.id FROM comics c JOIN series s ON s.id = c.series_id JOIN collection k ON k.comic_id = c.id
                 WHERE s.name = $p0 COLLATE NOCASE AND ($p1 = '' OR s.volume = $p1) AND c.issue_number = $p2 COLLATE NOCASE
                 ORDER BY c.variant, c.variant_name LIMIT 1", tx, c.Series, c.Volume, c.Issue)
        End Function

        ' ---------- read, exported and backed up ----------

        ''' <summary>Everything you own, with plain column names, for the insurance list.</summary>
        Public Function ExportList() As DataTable
            Using conn = Open()
                Return Query(conn,
                    "SELECT series AS Series, volume AS Volume, issue AS Issue, CASE WHEN variant_name <> '' THEN variant_name ELSE variant END AS [Cover / variant],
                            title AS [Story title], publisher AS Publisher, cover_date AS [Cover date], format AS Format,
                            quantity AS Copies,
                            CASE WHEN graded_by <> '' THEN trim(graded_by || ' ' || grade) ELSE condition END AS Condition,
                            cert_number AS [Cert number],
                            (SELECT st.name FROM set_comics sc JOIN story_sets st ON st.id = sc.set_id WHERE sc.comic_id = v_collection.comic_id) AS [Set],
                            cover_price AS [Cover price], price_paid AS Paid, current_value AS [Value each], total_value AS [Total value],
                            CASE WHEN is_read THEN 'Yes' ELSE '' END AS [Read], barcode AS Barcode, notes AS Notes
                     FROM v_collection
                     ORDER BY CASE WHEN series LIKE 'The %' THEN substr(series, 5) ELSE series END COLLATE NOCASE,
                              volume, issue_sort, issue, variant, variant_name")
            End Using
        End Function

        ''' <summary>Copies the whole database to another file. Safe while the app is using it.</summary>
        Public Sub BackupTo(target As String)
            Dim folder = Path.GetDirectoryName(target)
            If Not String.IsNullOrEmpty(folder) Then Directory.CreateDirectory(folder)
            ' Written under another name first, so a half-written copy never replaces a good one.
            Dim temp = target & ".part"
            If File.Exists(temp) Then File.Delete(temp)
            Using conn = Open(), dest As New SqliteConnection(New SqliteConnectionStringBuilder With {.DataSource = temp, .Pooling = False}.ToString())
                dest.Open()
                conn.BackupDatabase(dest)
            End Using
            File.Move(temp, target, overwrite:=True)
        End Sub

        ''' <summary>Replaces everything with a copy made by BackupTo.</summary>
        Public Sub RestoreFrom(source As String)
            Using src As New SqliteConnection(New SqliteConnectionStringBuilder With {.DataSource = source, .Mode = SqliteOpenMode.ReadOnly, .Pooling = False}.ToString())
                Try
                    src.Open()
                    If Scalar(src, "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = 'collection'", Nothing) Is Nothing Then
                        Throw New InvalidDataException("That file isn't a Comic Catalog backup.")
                    End If
                Catch ex As SqliteException
                    Throw New InvalidDataException("That file isn't a Comic Catalog backup.", ex)
                End Try
                Using conn = Open()
                    src.BackupDatabase(conn)
                End Using
            End Using
        End Sub

        ' ---------- sent to the phone ----------

        ''' <summary>
        ''' What you own and want, as compact JSON for the phone app:
        ''' {"owned": [[series, volume, issue, barcode, metronId, box, key issue note, eBay estimate], ...],
        '''  "wishlist": [[series, issue, story arc], ...]}.
        ''' Leaves out the time, so the same collection always gives the same text.
        ''' </summary>
        Public Function LibrarySnapshot() As String
            Using conn = Open(), ms As New MemoryStream()
                Using w As New System.Text.Json.Utf8JsonWriter(ms)
                    w.WriteStartObject()
                    w.WriteStartArray("owned")
                    Using cmd = Command(conn, "SELECT series, volume, issue, barcode, metron_id, location, key_note, ebay_value FROM v_collection ORDER BY comic_id", Nothing)
                        Using r = cmd.ExecuteReader()
                            While r.Read()
                                w.WriteStartArray()
                                For i = 0 To 3
                                    w.WriteStringValue(If(r.IsDBNull(i), "", Convert.ToString(r.GetValue(i), CultureInfo.InvariantCulture)))
                                Next
                                If r.IsDBNull(4) Then w.WriteNullValue() Else w.WriteNumberValue(r.GetInt64(4))
                                w.WriteStringValue(r.GetString(5))
                                w.WriteStringValue(r.GetString(6))
                                If r.IsDBNull(7) Then w.WriteNullValue() Else w.WriteNumberValue(Math.Round(r.GetDouble(7), 2))
                                w.WriteEndArray()
                            End While
                        End Using
                    End Using
                    w.WriteEndArray()
                    w.WriteStartArray("wishlist")
                    For Each row As DataRow In GetWishlist().Rows
                        w.WriteStartArray()
                        For Each col In {"Series", "Issue", "Story arc"}
                            w.WriteStringValue(If(row.IsNull(col), "", Convert.ToString(row(col), CultureInfo.InvariantCulture)))
                        Next
                        w.WriteEndArray()
                    Next
                    w.WriteEndArray()
                    w.WriteEndObject()
                End Using
                Return System.Text.Encoding.UTF8.GetString(ms.ToArray())
            End Using
        End Function

        ' ---------- new releases ----------

        ''' <summary>
        ''' Your series and whether you follow them for new releases. Until you choose, a series is
        ''' followed when you have an issue with a cover date in the last two years.
        ''' </summary>
        Public Function SeriesToFollow() As DataTable
            Using conn = Open()
                Return Query(conn,
                    "SELECT s.id, s.name AS Series, s.volume AS Volume, COUNT(c.id) AS Have, MAX(c.cover_date) AS Latest,
                            COALESCE(s.follow, MAX(c.cover_date) >= strftime('%Y-%m', 'now', '-2 years')) AS Follow
                     FROM series s JOIN comics c ON c.series_id = s.id JOIN collection col ON col.comic_id = c.id
                     GROUP BY s.id
                     ORDER BY CASE WHEN s.name LIKE 'The %' THEN substr(s.name, 5) ELSE s.name END COLLATE NOCASE, s.volume")
            End Using
        End Function

        Public Sub SetFollow(seriesIds As IEnumerable(Of Long), follow As Boolean)
            Using conn = Open()
                Using tx = conn.BeginTransaction()
                    For Each id In seriesIds
                        Execute(conn, tx, "UPDATE series SET follow = $p1 WHERE id = $p0", id, follow)
                    Next
                    tx.Commit()
                End Using
            End Using
        End Sub

        ''' <summary>Names of the series you follow, each once (a new volume of a series counts too).</summary>
        Public Function FollowedSeriesNames() As List(Of String)
            Return SeriesToFollow().Rows.Cast(Of DataRow)().
                Where(Function(r) Convert.ToInt64(r("Follow"), CultureInfo.InvariantCulture) <> 0).
                Select(Function(r) CStr(r("Series"))).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
        End Function

        ''' <summary>Saves releases just found. With replaceAll, ones not found this time are dropped.</summary>
        Public Sub SaveReleases(found As IEnumerable(Of MetronIssue), replaceAll As Boolean)
            Using conn = Open()
                Using tx = conn.BeginTransaction()
                    If replaceAll Then Execute(conn, tx, "DELETE FROM releases")
                    For Each r In found
                        Execute(conn, tx, "INSERT OR REPLACE INTO releases (metron_id, series, volume, issue_number, title, store_date, cover_url)
                                           VALUES ($p0, $p1, $p2, $p3, $p4, $p5, $p6)",
                                r.MetronId, r.Series, r.Volume, r.Number, r.Title, r.StoreDate, r.CoverUrl)
                    Next
                    tx.Commit()
                End Using
            End Using
        End Sub

        ''' <summary>Issues out in the last fortnight or coming soon, and whether you have them or want them.</summary>
        Public Function GetReleases() As DataTable
            Using conn = Open()
                Return Query(conn,
                    "SELECT r.metron_id, r.store_date AS [In shops], r.series AS Series, r.volume AS Volume, r.issue_number AS Issue,
                            r.title AS [Story title],
                            CASE WHEN EXISTS (SELECT 1 FROM comics c JOIN series s ON s.id = c.series_id JOIN collection col ON col.comic_id = c.id
                                              WHERE c.metron_id = r.metron_id
                                                 OR (s.name = r.series AND c.issue_number = r.issue_number AND (s.volume = r.volume OR s.volume = '' OR r.volume = '')))
                                 THEN 'Have it'
                                 WHEN EXISTS (SELECT 1 FROM wishlist w WHERE w.series_name = r.series COLLATE NOCASE AND w.issue_number = r.issue_number)
                                 THEN 'On wishlist' ELSE '' END AS Status,
                            r.cover_url
                     FROM releases r
                     WHERE r.store_date >= date('now', '-14 days')
                     ORDER BY r.store_date, r.series COLLATE NOCASE, CAST(r.issue_number AS REAL), r.issue_number")
            End Using
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
