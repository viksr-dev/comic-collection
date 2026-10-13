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
                    For Each c In Rows(conn, tx, "SELECT id, issue_number, variant, variant_name FROM comics WHERE series_id = $p0", fromId)
                        Dim same = Scalar(conn, "SELECT id FROM comics WHERE series_id = $p0 AND issue_number = $p1 AND variant = $p2 AND variant_name = $p3",
                                          tx, toId, c("issue_number"), c("variant"), c("variant_name"))
                        If same Is Nothing Then
                            Execute(conn, tx, "UPDATE comics SET series_id = $p1 WHERE id = $p0", c("id"), toId)
                        Else
                            ' The same issue in both: one comic, with the copies added together.
                            If Scalar(conn, "SELECT 1 FROM collection WHERE comic_id = $p0", tx, same) Is Nothing Then
                                Execute(conn, tx, "UPDATE collection SET comic_id = $p1 WHERE comic_id = $p0", c("id"), same)
                            Else
                                Execute(conn, tx, "UPDATE collection SET quantity = quantity + IFNULL((SELECT quantity FROM collection WHERE comic_id = $p0), 0)
                                                   WHERE comic_id = $p1", c("id"), same)
                            End If
                            For Each table In {"photos", "loans", "digital_files", "ebay_history"}
                                Execute(conn, tx, $"UPDATE OR IGNORE {table} SET comic_id = $p1 WHERE comic_id = $p0", c("id"), same)
                            Next
                            Execute(conn, tx, "DELETE FROM comics WHERE id = $p0", c("id"))
                        End If
                        moved += 1
                    Next
                    Execute(conn, tx, "UPDATE wishlist SET series_name = $p1 WHERE comic_id IS NULL AND series_name = $p0 COLLATE NOCASE", fromName, toName)
                    Execute(conn, tx, "DELETE FROM series WHERE id = $p0 AND id NOT IN (SELECT series_id FROM comics)", fromId)
                    tx.Commit()
                End Using
            End Using
            Return moved
        End Function

    End Class

End Namespace
