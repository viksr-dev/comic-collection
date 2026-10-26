Option Strict On

Imports System.Data
Imports System.Globalization
Imports System.Text.RegularExpressions

Namespace Data

    Partial Public Class ComicDb

        ''' <summary>
        ''' Reads issue numbers typed like "1-12, 15, 20-24" (or "#3 #7"). Returns Nothing if something can't be read,
        ''' or more than 2,000 issues are asked for.
        ''' </summary>
        Public Shared Function ParseIssueList(text As String) As List(Of String)
            Dim result As New List(Of String)
            For Each part In Regex.Split(If(text, ""), "[,;\s]+").Select(Function(p) p.Trim().TrimStart("#"c)).Where(Function(p) p <> "")
                Dim range = Regex.Match(part, "^(\d+)\s*[-–]\s*#?(\d+)$")
                If range.Success Then
                    Dim a = Integer.Parse(range.Groups(1).Value, CultureInfo.InvariantCulture)
                    Dim b = Integer.Parse(range.Groups(2).Value, CultureInfo.InvariantCulture)
                    If b < a OrElse b - a > 2000 Then Return Nothing
                    For n = a To b
                        result.Add(n.ToString(CultureInfo.InvariantCulture))
                    Next
                ElseIf Regex.IsMatch(part, "^\d+(\.\d+)?$|^½$") Then
                    result.Add(If(part = "½", part, part.TrimStart("0"c).PadLeft(1, "0"c)))
                Else
                    Return Nothing
                End If
                If result.Count > 2000 Then Return Nothing
            Next
            Return result.Distinct().ToList()
        End Function

        ''' <summary>
        ''' Adds issues of a series you already collect, without scanning each one: same series, volume, publisher and
        ''' format as the comic given. Issues you already have (any cover) are skipped. Their covers and details are
        ''' found later by Find covers. Returns the comics added.
        ''' </summary>
        Public Function AddIssues(template As ComicRecord, issues As IEnumerable(Of String), Optional condition As String = "",
                                  Optional box As String = "") As List(Of Long)
            Dim added As New List(Of Long)
            Dim have As HashSet(Of String)
            Using conn = Open()
                have = New HashSet(Of String)(
                    Query(conn, "SELECT issue FROM v_collection WHERE series = $p0 AND volume = $p1", template.Series, If(template.Volume, "")).
                        Rows.Cast(Of DataRow)().Select(Function(r) Convert.ToString(r(0), CultureInfo.InvariantCulture).TrimStart("0"c)),
                    StringComparer.OrdinalIgnoreCase)
            End Using
            For Each issue In issues
                If have.Contains(issue.TrimStart("0"c)) Then Continue For
                Dim c As New ComicRecord With {
                    .Series = template.Series, .Volume = If(template.Volume, ""), .Issue = issue, .Publisher = If(template.Publisher, ""),
                    .Format = If(template.Format, ""), .Condition = If(condition, ""), .Location = If(box, ""), .Quantity = 1}
                added.Add(SaveComic(c))
                have.Add(issue.TrimStart("0"c))
            Next
            Return added
        End Function

        ''' <summary>Series you have single issues of whose length on Metron hasn't been looked up in the last month.</summary>
        Public Function SeriesNeedingTotals() As List(Of (Name As String, Volume As String))
            Using conn = Open()
                Return Query(conn,
                    $"SELECT DISTINCT s.name, s.volume FROM series s JOIN comics c ON c.series_id = s.id JOIN collection k ON k.comic_id = c.id
                      WHERE (s.total_checked = '' OR s.total_checked < date('now', '-30 days')) AND NOT {TradeSql("c.format")}
                      ORDER BY s.name COLLATE NOCASE").Rows.Cast(Of DataRow)().
                    Select(Function(r) (CStr(r(0)), CStr(r(1)))).ToList()
            End Using
        End Function

        ''' <summary>Notes how many issues a series has on Metron (0 if Metron doesn't know it), so Missing issues counts to the end.</summary>
        Public Sub SetSeriesTotal(name As String, volume As String, total As Integer)
            Using conn = Open()
                Execute(conn, Nothing, "UPDATE series SET issue_total = $p2, total_checked = date('now') WHERE name = $p0 AND volume = $p1",
                        name, If(volume, ""), total)
            End Using
        End Sub

        ''' <summary>Every series you have comics in, A to Z, with how many: for picking one to merge into.</summary>
        Public Function SeriesList() As List(Of (Name As String, Volume As String, Count As Integer))
            Using conn = Open()
                Return Query(conn, "SELECT series, IFNULL(volume, ''), COUNT(*) FROM v_collection GROUP BY series, volume ORDER BY series COLLATE NOCASE, volume").
                    Rows.Cast(Of DataRow)().Select(Function(r) (CStr(r(0)), CStr(r(1)), Convert.ToInt32(r(2), CultureInfo.InvariantCulture))).ToList()
            End Using
        End Function

        ''' <summary>
        ''' Makes two series one ("The Walking Dead Deluxe (2020)" into "The Walking Dead Deluxe"): every comic moves to the
        ''' second. An issue that's in both is kept once, with the copies added together and its photos, loans and digital
        ''' copy moved across. Wishlist entries by name follow. Returns how many comics were moved or joined.
        ''' </summary>
        Public Function MergeSeries(fromName As String, fromVolume As String, toName As String, toVolume As String) As Integer
            Dim moved = 0
            Using conn = Open()
                Using tx = conn.BeginTransaction()
                    Dim fromId = Scalar(conn, "SELECT id FROM series WHERE name = $p0 AND volume = $p1", tx, fromName, If(fromVolume, ""))
                    Dim toId = Scalar(conn, "SELECT id FROM series WHERE name = $p0 AND volume = $p1", tx, toName, If(toVolume, ""))
                    If fromId Is Nothing OrElse toId Is Nothing OrElse Convert.ToInt64(fromId, CultureInfo.InvariantCulture) = Convert.ToInt64(toId, CultureInfo.InvariantCulture) Then Return 0
                    For Each c In Rows(conn, tx, "SELECT id FROM comics WHERE series_id = $p0", fromId)
                        MoveComic(conn, tx, Convert.ToInt64(c("id"), CultureInfo.InvariantCulture), Convert.ToInt64(toId, CultureInfo.InvariantCulture))
                        moved += 1
                    Next
                    Execute(conn, tx, "UPDATE wishlist SET series_name = $p1 WHERE comic_id IS NULL AND series_name = $p0 COLLATE NOCASE", fromName, toName)
                    Execute(conn, tx, "DELETE FROM series WHERE id = $p0 AND id NOT IN (SELECT series_id FROM comics)", fromId)
                    tx.Commit()
                End Using
            End Using
            Return moved
        End Function

        ''' <summary>
        ''' Moves one comic into another series. If that series already has the same issue and cover, they become one comic
        ''' with the copies added together (its photos, loans and digital copy move across). Returns the comic it ends up as.
        ''' </summary>
        Private Shared Function MoveComic(conn As Microsoft.Data.Sqlite.SqliteConnection, tx As Microsoft.Data.Sqlite.SqliteTransaction,
                                          comicId As Long, toSeriesId As Long) As Long
            Dim c = Rows(conn, tx, "SELECT issue_number, variant, variant_name FROM comics WHERE id = $p0", comicId).FirstOrDefault()
            If c Is Nothing Then Return comicId
            Dim same = Scalar(conn, "SELECT id FROM comics WHERE series_id = $p0 AND issue_number = $p1 AND variant = $p2 AND variant_name = $p3 AND id <> $p4",
                              tx, toSeriesId, c("issue_number"), c("variant"), c("variant_name"), comicId)
            If same Is Nothing Then
                Execute(conn, tx, "UPDATE comics SET series_id = $p1 WHERE id = $p0", comicId, toSeriesId)
                Return comicId
            End If
            ' The same issue in both: one comic, with the copies added together.
            If Scalar(conn, "SELECT 1 FROM collection WHERE comic_id = $p0", tx, same) Is Nothing Then
                Execute(conn, tx, "UPDATE collection SET comic_id = $p1 WHERE comic_id = $p0", comicId, same)
            Else
                Execute(conn, tx, "UPDATE collection SET quantity = quantity + IFNULL((SELECT quantity FROM collection WHERE comic_id = $p0), 0)
                                   WHERE comic_id = $p1", comicId, same)
            End If
            For Each table In {"photos", "loans", "digital_files", "ebay_history"}
                Execute(conn, tx, $"UPDATE OR IGNORE {table} SET comic_id = $p1 WHERE comic_id = $p0", comicId, same)
            Next
            Execute(conn, tx, "DELETE FROM comics WHERE id = $p0", comicId)
            Return Convert.ToInt64(same, CultureInfo.InvariantCulture)
        End Function

        ''' <summary>
        ''' Puts a comic in the right volume of its series (Metron calls a volume by the year it began, like "1998"),
        ''' making that volume if it's new. Marks the volume as checked. Returns the comic it ends up as.
        ''' </summary>
        Public Function SetComicVolume(comicId As Long, volume As String) As Long
            Dim result = comicId
            Using conn = Open()
                Using tx = conn.BeginTransaction()
                    Execute(conn, tx, "UPDATE comics SET volume_checked = 1 WHERE id = $p0", comicId)
                    Dim s = Rows(conn, tx, "SELECT s.id, s.name, s.volume, s.publisher_id, s.follow FROM comics c JOIN series s ON s.id = c.series_id WHERE c.id = $p0", comicId).FirstOrDefault()
                    volume = If(volume, "").Trim()
                    If s IsNot Nothing AndAlso volume <> "" AndAlso Not String.Equals(CStr(s("volume")), volume, StringComparison.OrdinalIgnoreCase) Then
                        Dim targetId = Scalar(conn, "SELECT id FROM series WHERE name = $p0 AND volume = $p1", tx, s("name"), volume)
                        If targetId Is Nothing Then
                            Dim year As Integer
                            Execute(conn, tx, "INSERT INTO series (name, volume, start_year, publisher_id, follow) VALUES ($p0, $p1, $p2, $p3, $p4)",
                                    s("name"), volume, If(volume.Length = 4 AndAlso Integer.TryParse(volume, year), CObj(year), Nothing), s("publisher_id"), s("follow"))
                            targetId = Scalar(conn, "SELECT last_insert_rowid()", tx)
                        End If
                        result = MoveComic(conn, tx, comicId, Convert.ToInt64(targetId, CultureInfo.InvariantCulture))
                        Execute(conn, tx, "DELETE FROM series WHERE id = $p0 AND id NOT IN (SELECT series_id FROM comics)", s("id"))
                    End If
                    tx.Commit()
                End Using
            End Using
            Return result
        End Function

        ''' <summary>
        ''' Comics you own with no volume whose volume hasn't been asked of Metron yet (just one series if given):
        ''' (comic, its Metron issue or Nothing). Ones matched on Metron come first.
        ''' </summary>
        Public Function ComicsNeedingVolumes(Optional seriesName As String = Nothing) As List(Of (Comic As ComicRecord, MetronId As Long?))
            Dim ids As List(Of Long)
            Using conn = Open()
                ids = Query(conn, "SELECT comic_id FROM v_collection v JOIN comics c ON c.id = v.comic_id
                                   WHERE v.volume = '' AND c.volume_checked = 0 AND ($p0 IS NULL OR v.series = $p0)
                                   ORDER BY v.metron_id IS NULL, v.series, v.issue_sort", seriesName).
                    Rows.Cast(Of DataRow)().Select(Function(r) Convert.ToInt64(r(0), CultureInfo.InvariantCulture)).ToList()
            End Using
            Return ids.Select(Function(id) GetComic(id)).Where(Function(c) c IsNot Nothing).Select(Function(c) (c, c.MetronId)).ToList()
        End Function

        ''' <summary>How many comics matched on Metron still have no volume (a quick check for the background jobs).</summary>
        Public Function CountMatchedNeedingVolumes() As Integer
            Using conn = Open()
                Return Convert.ToInt32(Scalar(conn, "SELECT COUNT(*) FROM v_collection v JOIN comics c ON c.id = v.comic_id
                                                     WHERE v.volume = '' AND c.volume_checked = 0 AND c.metron_id IS NOT NULL", Nothing), CultureInfo.InvariantCulture)
            End Using
        End Function

        ''' <summary>
        ''' Lets Find covers try these comics again (ones it looked for before and didn't find, or that are still
        ''' missing a cover date or publisher). Returns how many.
        ''' </summary>
        Public Function ResetCoverChecks(comicIds As IEnumerable(Of Long)) As Integer
            Dim n = 0
            Using conn = Open()
                For Each id In comicIds
                    Using cmd = Command(conn, "UPDATE comics SET cover_checked = 0, price_checked = 0
                                               WHERE id = $p0 AND (cover_url = '' OR metron_id IS NULL OR cover_date = ''
                                                    OR (SELECT publisher_id FROM series WHERE series.id = comics.series_id) IS NULL)", Nothing, id)
                        n += cmd.ExecuteNonQuery()
                    End Using
                Next
            End Using
            Return n
        End Function

        ''' <summary>Notes a comic's volume was looked for, even if Metron didn't know it, so it isn't asked again.</summary>
        Public Sub MarkVolumeChecked(comicId As Long)
            Using conn = Open()
                Execute(conn, Nothing, "UPDATE comics SET volume_checked = 1 WHERE id = $p0", comicId)
            End Using
        End Sub

        ''' <summary>
        ''' A comic added by number alone (no cover, no cover name, not matched on Metron), when the same series and volume
        ''' also has that issue with its details: two rows for one comic, as after merging a series typed in by number into one
        ''' that was scanned. The bare one goes to the bin; the other keeps the larger number of copies (they're the same
        ''' comic, so copies aren't added up), the box if it had none, and any photos and loans.
        ''' Returns how many were removed, the delete (for Undo) and how the kept ones were before.
        ''' </summary>
        Public Function TidyDoubledIssues(seriesName As String) As (Removed As Integer, Batch As String, Before As ComicSnapshot)
            Dim pairs As New List(Of (Bare As Long, Kept As Long))
            Using conn = Open()
                Dim bare = Rows(conn, Nothing,
                    "SELECT c.id, c.series_id, c.issue_number FROM comics c JOIN collection k ON k.comic_id = c.id JOIN series s ON s.id = c.series_id
                     WHERE s.name = $p0 AND c.variant = '' AND c.variant_name = '' AND c.cover_url = '' AND c.metron_id IS NULL", seriesName)
                For Each b In bare
                    Dim kept = Scalar(conn,
                        "SELECT c.id FROM comics c JOIN collection k ON k.comic_id = c.id
                         WHERE c.series_id = $p0 AND c.id <> $p1 AND ltrim(c.issue_number, '0') = ltrim($p2, '0')
                           AND NOT (c.variant = '' AND c.variant_name = '' AND c.cover_url = '' AND c.metron_id IS NULL)
                         ORDER BY c.cover_url <> '' DESC, c.metron_id IS NOT NULL DESC, c.id LIMIT 1", Nothing, b("series_id"), b("id"), b("issue_number"))
                    If kept IsNot Nothing Then pairs.Add((Convert.ToInt64(b("id"), CultureInfo.InvariantCulture), Convert.ToInt64(kept, CultureInfo.InvariantCulture)))
                Next
            End Using
            If pairs.Count = 0 Then Return (0, "", Nothing)
            Dim before = Snapshot(pairs.Select(Function(p) p.Kept))
            Using conn = Open()
                Using tx = conn.BeginTransaction()
                    For Each p In pairs
                        Execute(conn, tx, "UPDATE collection SET quantity = MAX(quantity, IFNULL((SELECT quantity FROM collection WHERE comic_id = $p0), 1)),
                                                             location = CASE WHEN location = '' THEN IFNULL((SELECT location FROM collection WHERE comic_id = $p0), '') ELSE location END
                                           WHERE comic_id = $p1", p.Bare, p.Kept)
                        For Each table In {"photos", "loans"}
                            Execute(conn, tx, $"UPDATE {table} SET comic_id = $p1 WHERE comic_id = $p0", p.Bare, p.Kept)
                        Next
                    Next
                    tx.Commit()
                End Using
            End Using
            Dim batch = DeleteComics(pairs.Select(Function(p) p.Bare).Distinct().ToList())
            Return (pairs.Select(Function(p) p.Bare).Distinct().Count(), batch, before)
        End Function

    End Class

End Namespace
