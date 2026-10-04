Option Strict On

Imports System.Globalization
Imports System.Net
Imports System.Net.Http
Imports System.Text.Json

Namespace Data

    ''' <summary>
    ''' Looks comics up on Metron (metron.cloud) through your own relay, the same
    ''' Cloudflare Worker the phone app uses. Your Metron password stays in Cloudflare.
    ''' </summary>
    Public Class MetronClient

        Private Shared ReadOnly Http As New HttpClient With {.Timeout = TimeSpan.FromSeconds(30)}
        Private ReadOnly _relayUrl As String

        Public Sub New(relayUrl As String)
            _relayUrl = If(relayUrl, "").Trim().TrimEnd("/"c)
        End Sub

        Public ReadOnly Property IsSetUp As Boolean
            Get
                Return _relayUrl <> ""
            End Get
        End Property

        Public Async Function TestAsync() As Task
            Dim doc = Await GetAsync("/ping")
            Dim ok As JsonElement
            If Not doc.RootElement.TryGetProperty("ok", ok) OrElse ok.ValueKind <> JsonValueKind.True Then
                Throw New InvalidOperationException("The relay answered, but couldn't reach Metron.")
            End If
        End Function

        ''' <summary>2 or more once the relay sends cover prices; 1 for the first relay.</summary>
        Public Async Function RelayVersionAsync() As Task(Of Integer)
            Dim doc = Await GetAsync("/ping")
            Dim v As JsonElement
            If doc.RootElement.TryGetProperty("version", v) AndAlso v.ValueKind = JsonValueKind.Number Then Return v.GetInt32()
            Return 1
        End Function

        Public Async Function LookupBarcodeAsync(code As ParsedBarcode) As Task(Of List(Of MetronIssue))
            Dim doc = Await GetAsync($"/upc/{Uri.EscapeDataString(code.Full)}?issue={Uri.EscapeDataString(code.Issue)}")
            Return ReadResults(doc)
        End Function

        Public Async Function SearchAsync(series As String, number As String) As Task(Of List(Of MetronIssue))
            Dim q = $"/search?series={Uri.EscapeDataString(series)}"
            If Not String.IsNullOrWhiteSpace(number) Then q &= $"&number={Uri.EscapeDataString(number.Trim())}"
            Return ReadResults(Await GetAsync(q))
        End Function

        ''' <summary>One issue by its Metron number, or Nothing.</summary>
        Public Async Function IssueAsync(metronId As Long) As Task(Of MetronIssue)
            Dim doc = Await GetAsync($"/issue/{metronId}")
            Dim result As JsonElement
            If Not doc.RootElement.TryGetProperty("result", result) OrElse result.ValueKind <> JsonValueKind.Object Then Return Nothing
            Return ToIssue(result)
        End Function

        Private Async Function GetAsync(path As String) As Task(Of JsonDocument)
            If Not IsSetUp Then Throw New InvalidOperationException("Comic lookup isn't set up yet. Add your relay address on the Settings tab.")
            ' The relay remembers answers for a week. Asking with v=2 skips answers saved before
            ' it sent cover prices.
            If Not path.StartsWith("/ping") Then path &= If(path.Contains("?"c), "&", "?") & "v=2"
            Using res = Await Http.GetAsync(_relayUrl & path)
                If res.StatusCode = HttpStatusCode.TooManyRequests Then
                    Throw New InvalidOperationException("Too many lookups in a short time. Wait a minute and try again.")
                End If
                If Not res.IsSuccessStatusCode Then Throw New InvalidOperationException($"Lookup failed ({CInt(res.StatusCode)}).")
                Return JsonDocument.Parse(Await res.Content.ReadAsStringAsync())
            End Using
        End Function

        Private Shared Function ReadResults(doc As JsonDocument) As List(Of MetronIssue)
            Dim list As New List(Of MetronIssue)
            Dim results As JsonElement
            If Not doc.RootElement.TryGetProperty("results", results) OrElse results.ValueKind <> JsonValueKind.Array Then Return list
            For Each r In results.EnumerateArray()
                list.Add(ToIssue(r))
            Next
            Return list
        End Function

        Private Shared Function ToIssue(r As JsonElement) As MetronIssue
            Return New MetronIssue With {
                .MetronId = If(Text(r, "metronId") = "", 0L, Long.Parse(Text(r, "metronId"), CultureInfo.InvariantCulture)),
                .Series = Text(r, "series"), .Volume = Text(r, "volume"), .Number = Text(r, "number"),
                .Title = Text(r, "title"), .Publisher = Text(r, "publisher"),
                .CoverDate = Text(r, "coverDate"), .CoverUrl = Text(r, "coverUrl"), .Price = Price(Text(r, "price"))}
        End Function

        Private Shared Function Price(s As String) As Double?
            Dim d As Double
            If Double.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, d) AndAlso d > 0 Then Return d
            Return Nothing
        End Function

        Private Shared Function Text(e As JsonElement, name As String) As String
            Dim v As JsonElement
            If Not e.TryGetProperty(name, v) Then Return ""
            Select Case v.ValueKind
                Case JsonValueKind.String : Return v.GetString()
                Case JsonValueKind.Number : Return v.GetRawText()
                Case Else : Return ""
            End Select
        End Function

    End Class

End Namespace
