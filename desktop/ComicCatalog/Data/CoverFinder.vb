Option Strict On

Imports System.Text.RegularExpressions

Namespace Data

    ''' <summary>Finds the Metron issue (and so the cover) for a comic you own.</summary>
    Public Module CoverFinder

        ''' <summary>
        ''' Tries the comic's Metron number first, then its barcode, then its series and issue number.
        ''' Returns Nothing when it can't tell which issue it is. callsMade says how many lookups were needed.
        ''' With wantPrice, a search match is looked up once more, since only full details carry the cover price.
        ''' </summary>
        Public Async Function FindAsync(metron As MetronClient, c As ComicRecord, callsMade As Action(Of Integer),
                                        Optional wantPrice As Boolean = False) As Task(Of MetronIssue)
            If c.MetronId.HasValue AndAlso c.MetronId.Value > 0 Then
                callsMade(1)
                Return Await metron.IssueAsync(c.MetronId.Value)
            End If
            If c.Barcode <> "" Then
                Dim code = Barcode.Parse(c.Barcode)
                If code.IsValid Then
                    callsMade(2)
                    Dim found = Await metron.LookupBarcodeAsync(code)
                    If found.Count = 1 Then Return found(0)
                    If found.Count > 1 Then Return Nothing
                End If
            End If
            If c.Series = "" OrElse c.Issue = "" Then Return Nothing
            callsMade(1)
            Dim match = PickTitleMatch(c, Await metron.SearchAsync(c.Series, c.Issue))
            If match Is Nothing OrElse Not wantPrice OrElse match.Price.HasValue OrElse match.MetronId <= 0 Then Return match
            callsMade(2)
            Return If(Await metron.IssueAsync(match.MetronId), match)
        End Function

        ''' <summary>The one search result that matches the issue number (and cover date if needed), or Nothing.</summary>
        Public Function PickTitleMatch(c As ComicRecord, results As List(Of MetronIssue)) As MetronIssue
            Dim num = Function(n As String) Regex.Replace(If(n, "").Trim().ToLowerInvariant(), "^0+(?=\d)", "")
            Dim m = results.Where(Function(r) num(r.Number) = num(c.Issue)).ToList()
            Dim d = If(c.CoverDate, "")
            If m.Count > 1 AndAlso d.Length >= 4 Then
                Dim sameMonth = m.Where(Function(r) d.Length >= 7 AndAlso r.CoverDate = d.Substring(0, 7)).ToList()
                Dim sameYear = m.Where(Function(r) r.CoverDate.StartsWith(d.Substring(0, 4))).ToList()
                m = If(sameMonth.Count > 0, sameMonth, If(sameYear.Count > 0, sameYear, m))
            End If
            Return If(m.Count = 1, m(0), Nothing)
        End Function

    End Module

End Namespace
