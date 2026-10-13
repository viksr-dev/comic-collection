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

    End Class

End Namespace
