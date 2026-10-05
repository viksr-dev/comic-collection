Option Strict On

Imports System.Globalization

Namespace Data

    ''' <summary>Finds issues of the series you follow that are just out or coming soon.</summary>
    Public Module ReleaseFinder

        ''' <summary>Releases from this many days ago onwards are shown, so last week's are still there.</summary>
        Public Const DaysBack As Integer = 14

        ''' <summary>
        ''' Asks Metron about each series in turn, a few seconds apart to stay inside its limit of
        ''' about 20 lookups a minute. progress gets how many series are done; stopRequested ends it early.
        ''' </summary>
        Public Async Function FindAsync(metron As MetronClient, seriesNames As IList(Of String),
                                        progress As Action(Of Integer), stopRequested As Func(Of Boolean)) As Task(Of List(Of MetronIssue))
            Dim since = DateTime.Today.AddDays(-DaysBack).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            Dim found As New Dictionary(Of Long, MetronIssue)
            For i = 0 To seriesNames.Count - 1
                If stopRequested() Then Exit For
                progress(i)
                Dim results As List(Of MetronIssue) = Nothing
                For attempt = 1 To 3
                    Dim slowDown = False
                    Try
                        results = Await metron.ReleasesAsync(seriesNames(i), since)
                        Exit For
                    Catch ex As InvalidOperationException When ex.Message.StartsWith("Too many") AndAlso attempt < 3
                        slowDown = True
                    End Try
                    If slowDown Then Await Task.Delay(65000)
                Next
                For Each r In MatchingSeries(results, seriesNames(i), since)
                    found(r.MetronId) = r
                Next
                If i < seriesNames.Count - 1 Then Await Task.Delay(3200)
            Next
            progress(seriesNames.Count)
            Return found.Values.ToList()
        End Function

        ''' <summary>Metron's series search also matches longer names ("Batman" finds "Batman Beyond"); keeps the exact series.</summary>
        Public Function MatchingSeries(results As IEnumerable(Of MetronIssue), seriesName As String, since As String) As IEnumerable(Of MetronIssue)
            Return If(results, Enumerable.Empty(Of MetronIssue)()).
                Where(Function(r) r.MetronId > 0 AndAlso String.Equals(r.Series.Trim(), seriesName.Trim(), StringComparison.OrdinalIgnoreCase) AndAlso
                                  r.StoreDate <> "" AndAlso String.CompareOrdinal(r.StoreDate, since) >= 0)
        End Function

    End Module

End Namespace
