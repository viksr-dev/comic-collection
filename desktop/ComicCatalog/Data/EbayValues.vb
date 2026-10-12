Option Strict On

Imports System.Globalization
Imports System.Net
Imports System.Net.Http
Imports System.Text.Json
Imports System.Text.RegularExpressions

Namespace Data

    ''' <summary>
    ''' A rough value from what sellers are asking on eBay right now (US dollars, one copy).
    ''' Sold prices would be better, but eBay only gives those to its business partners.
    ''' </summary>
    Public Class EbayEstimate
        ''' <summary>How many listings matched. Under 3 isn't enough to go on, so Typical is then Nothing.</summary>
        Public Property Count As Integer
        ''' <summary>The middle asking price.</summary>
        Public Property Typical As Double?
        Public Property Low As Double?
        Public Property High As Double?
    End Class

    ''' <summary>A comic you own, with what's needed to look it up on eBay.</summary>
    Public Class EbayTarget
        Public Property ComicId As Long
        Public Property Series As String = ""
        Public Property Volume As String = ""
        Public Property Issue As String = ""
        Public Property VariantName As String = ""
        Public Property GradedBy As String = ""
        Public Property Grade As String = ""

        Public ReadOnly Property Words As String
            Get
                Return EbayValues.SearchWords(Series, Issue, Volume, VariantName, GradedBy, Grade)
            End Get
        End Property
    End Class

    Public Module EbayValues

        ''' <summary>How many listings an estimate needs before it's used.</summary>
        Public Const MinListings As Integer = 3

        ''' <summary>
        ''' Words for an eBay search for one comic, e.g. "Batman 404 1987" or "Batman 423 CGC 9.8".
        ''' A volume year tells apart series that restarted at #1, and a slab sells for far more than a raw copy.
        ''' </summary>
        Public Function SearchWords(series As String, issue As String, volume As String, variantName As String,
                                    gradedBy As String, grade As String) As String
            Dim words = If(series, "").Trim()
            If If(issue, "").Trim() <> "" Then words &= " " & issue.Trim()
            If Regex.IsMatch(If(volume, "").Trim(), "^\d{4}$") Then words &= " " & volume.Trim()
            If If(variantName, "").Trim() <> "" Then words &= " " & variantName.Trim()
            If If(gradedBy, "").Trim() <> "" Then words &= $" {gradedBy.Trim()} {If(grade, "").Trim()}".TrimEnd()
            Return words.Replace(":", " ").Replace("  ", " ").Trim()
        End Function

        ''' <summary>
        ''' Asks the relay (version 9, with eBay keys added) for asking prices of a comic.
        ''' Only listings naming the same issue number count, and raw copies and slabs aren't mixed.
        ''' </summary>
        Public Async Function EstimateAsync(http As HttpClient, relayUrl As String, target As EbayTarget) As Task(Of EbayEstimate)
            Dim url = $"{relayUrl.Trim().TrimEnd("/"c)}/ebay?q={Uri.EscapeDataString(SearchWords(target.Series, target.Issue, target.Volume, target.VariantName, "", ""))}" &
                      $"&number={Uri.EscapeDataString(target.Issue.Trim())}"
            If target.GradedBy.Trim() <> "" Then
                url &= $"&grader={Uri.EscapeDataString(target.GradedBy.Trim())}&grade={Uri.EscapeDataString(target.Grade.Trim())}"
            End If
            Using res = Await http.GetAsync(url)
                Dim body = Await res.Content.ReadAsStringAsync()
                If res.StatusCode = HttpStatusCode.TooManyRequests Then
                    Throw New InvalidOperationException("Too many lookups in a short time. Wait a minute and try again.")
                End If
                If res.StatusCode = HttpStatusCode.NotFound Then
                    Throw New InvalidOperationException("Your relay needs updating for eBay values. See ""Updating the relay"" in relay\README.md.")
                End If
                If Not res.IsSuccessStatusCode Then
                    Dim message = $"eBay lookup failed ({CInt(res.StatusCode)})."
                    Try
                        Using doc = JsonDocument.Parse(body)
                            Dim err As JsonElement
                            If doc.RootElement.TryGetProperty("error", err) AndAlso err.ValueKind = JsonValueKind.String Then message = err.GetString()
                        End Using
                    Catch ex As JsonException
                    End Try
                    Throw New InvalidOperationException(message)
                End If
                Return Parse(body)
            End Using
        End Function

        ''' <summary>Reads the relay's answer: {"count": 7, "median": 12.5, "low": 9, "high": 18}.</summary>
        Public Function Parse(json As String) As EbayEstimate
            Using doc = JsonDocument.Parse(json)
                Dim r = doc.RootElement
                Dim e As New EbayEstimate With {.Count = CInt(If(Num(r, "count"), 0))}
                If e.Count >= MinListings Then
                    e.Typical = Num(r, "median")
                    e.Low = Num(r, "low")
                    e.High = Num(r, "high")
                End If
                Return e
            End Using
        End Function

        Private Function Num(r As JsonElement, name As String) As Double?
            Dim v As JsonElement
            If Not r.TryGetProperty(name, v) Then Return Nothing
            If v.ValueKind = JsonValueKind.Number Then Return v.GetDouble()
            Dim d As Double
            If v.ValueKind = JsonValueKind.String AndAlso Double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, d) Then Return d
            Return Nothing
        End Function

    End Module

End Namespace
