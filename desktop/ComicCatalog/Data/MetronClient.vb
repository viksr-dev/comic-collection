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

        ''' <summary>Whether the relay has eBay keys added (relay version 9).</summary>
        Public Async Function EbayReadyAsync() As Task(Of Boolean)
            Dim doc = Await GetAsync("/ping")
            Dim v As JsonElement
            Return doc.RootElement.TryGetProperty("ebay", v) AndAlso v.ValueKind = JsonValueKind.True
        End Function

        ''' <summary>A value estimate from eBay asking prices. Needs relay version 9 with eBay keys.</summary>
        Public Function EbayEstimateAsync(target As EbayTarget) As Task(Of EbayEstimate)
            Return EbayValues.EstimateAsync(Http, _relayUrl, target)
        End Function

        ''' <summary>Cheapest eBay listings of a comic at or under a price (US$). Needs relay version 10.</summary>
        Public Function EbayListingsAsync(target As EbayTarget, maxUsd As Double) As Task(Of List(Of EbayListing))
            Return EbayValues.ListingsAsync(Http, _relayUrl, target, maxUsd)
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

        ''' <summary>Issues of a series in shops on or after a day ("YYYY-MM-DD"). Needs relay version 4.</summary>
        Public Async Function ReleasesAsync(series As String, since As String) As Task(Of List(Of MetronIssue))
            Return ReadResults(Await GetAsync($"/releases?series={Uri.EscapeDataString(series)}&after={Uri.EscapeDataString(since)}"))
        End Function

        ''' <summary>How many issues a story arc has on Metron. Needs relay version 4.</summary>
        ''' <summary>Every issue in a story arc. Needs relay version 5.</summary>
        Public Async Function ArcIssuesAsync(arcId As Long) As Task(Of List(Of MetronIssue))
            Return ReadResults(Await GetAsync($"/arc/{arcId}"))
        End Function

        ''' <summary>
        ''' How many issues the series has on Metron: the one for this volume (its start year or number), else the only
        ''' one with that name, else the longest. 0 if Metron doesn't know it. Needs relay version 12.
        ''' </summary>
        Public Async Function SeriesLengthAsync(name As String, volume As String) As Task(Of Integer)
            Dim doc = Await GetAsync($"/series?name={Uri.EscapeDataString(name)}")
            Dim results As JsonElement
            If Not doc.RootElement.TryGetProperty("results", results) OrElse results.ValueKind <> JsonValueKind.Array Then Return 0
            Dim found = results.EnumerateArray().Select(Function(r)
                                                             Dim count As JsonElement, year As JsonElement, vol As JsonElement
                                                             Return (Count:=If(r.TryGetProperty("issueCount", count) AndAlso count.ValueKind = JsonValueKind.Number, count.GetInt32(), 0),
                                                                     Year:=If(r.TryGetProperty("yearBegan", year) AndAlso year.ValueKind = JsonValueKind.Number, year.GetInt32().ToString(CultureInfo.InvariantCulture), ""),
                                                                     Vol:=If(r.TryGetProperty("volume", vol) AndAlso vol.ValueKind <> JsonValueKind.Null, vol.ToString(), ""))
                                                         End Function).ToList()
            If found.Count = 0 Then Return 0
            Dim v = System.Text.RegularExpressions.Regex.Replace(If(volume, "").Trim().ToLowerInvariant(), "^vol(ume)?\.?\s*", "")
            Dim same = found.Where(Function(f) v <> "" AndAlso (f.Year = v OrElse f.Vol = v)).ToList()
            If same.Count > 0 Then Return same(0).Count
            Return found.Max(Function(f) f.Count)
        End Function

        Public Async Function ArcSizeAsync(arcId As Long) As Task(Of Integer)
            Dim doc = Await GetAsync($"/arc/{arcId}")
            Dim count As JsonElement
            If doc.RootElement.TryGetProperty("count", count) AndAlso count.ValueKind = JsonValueKind.Number Then Return count.GetInt32()
            Return 0
        End Function

        ' ---------- scans sent from the phone ----------

        ''' <summary>Batches of comics the phone has left in the relay's mailbox, each as the phone app's CSV.</summary>
        Public Async Function InboxAsync(syncCode As String) As Task(Of List(Of (Id As String, Csv As String)))
            Dim list As New List(Of (Id As String, Csv As String))
            Using res = Await Http.GetAsync($"{_relayUrl}/inbox/{InboxCode(syncCode)}")
                CheckInbox(res)
                Using doc = JsonDocument.Parse(Await res.Content.ReadAsStringAsync())
                    Dim batches As JsonElement
                    If doc.RootElement.TryGetProperty("batches", batches) AndAlso batches.ValueKind = JsonValueKind.Array Then
                        For Each b In batches.EnumerateArray()
                            list.Add((Text(b, "id"), Text(b, "csv")))
                        Next
                    End If
                End Using
            End Using
            Return list
        End Function

        ''' <summary>Removes a batch from the mailbox once its comics are saved here.</summary>
        Public Async Function DeleteInboxAsync(syncCode As String, id As String) As Task
            Using res = Await Http.DeleteAsync($"{_relayUrl}/inbox/{InboxCode(syncCode)}/{Uri.EscapeDataString(id)}")
                CheckInbox(res)
            End Using
        End Function

        ''' <summary>Leaves the list of what you own and want for the phone (replacing the last one). Needs relay version 6.</summary>
        Public Async Function SendLibraryAsync(syncCode As String, json As String) As Task
            Using content As New StringContent(json, System.Text.Encoding.UTF8, "application/json")
                Using res = Await Http.PutAsync($"{_relayUrl}/library/{InboxCode(syncCode)}", content)
                    CheckInbox(res)
                End Using
            End Using
        End Function

        ''' <summary>
        ''' The page you're up to in each digital comic, kept by the relay (version 11) so the phone and computer carry on
        ''' from the same place. Sends the places given first. Nothing if the relay can't keep them (older relay).
        ''' </summary>
        Public Async Function SyncPlacesAsync(syncCode As String, Optional send As Dictionary(Of String, ReadingPlace) = Nothing) As Task(Of Dictionary(Of String, ReadingPlace))
            Dim url = $"{_relayUrl}/places/{InboxCode(syncCode)}"
            Dim res As HttpResponseMessage
            If send IsNot Nothing Then
                Dim body = JsonSerializer.Serialize(send.ToDictionary(Function(kv) kv.Key, Function(kv) New With {.page = kv.Value.Page, .pages = kv.Value.Pages, .at = kv.Value.At}))
                Using content As New StringContent(body, System.Text.Encoding.UTF8, "text/plain")
                    res = Await Http.PostAsync(url, content)
                End Using
            Else
                res = Await Http.GetAsync(url)
            End If
            Using res
                If Not res.IsSuccessStatusCode Then Return Nothing
                Return ParsePlaces(Await res.Content.ReadAsStringAsync())
            End Using
        End Function

        Friend Shared Function ParsePlaces(json As String) As Dictionary(Of String, ReadingPlace)
            Dim result As New Dictionary(Of String, ReadingPlace)
            Using doc = JsonDocument.Parse(json)
                Dim places As JsonElement
                If Not doc.RootElement.TryGetProperty("places", places) OrElse places.ValueKind <> JsonValueKind.Object Then Return Nothing
                For Each p In places.EnumerateObject()
                    Dim page As JsonElement, pages As JsonElement, at As JsonElement
                    If p.Value.ValueKind <> JsonValueKind.Object OrElse Not p.Value.TryGetProperty("at", at) OrElse at.ValueKind <> JsonValueKind.String Then Continue For
                    result(p.Name) = New ReadingPlace With {
                        .Page = If(p.Value.TryGetProperty("page", page) AndAlso page.ValueKind = JsonValueKind.Number, page.GetInt32(), 0),
                        .Pages = If(p.Value.TryGetProperty("pages", pages) AndAlso pages.ValueKind = JsonValueKind.Number, pages.GetInt32(), 0),
                        .At = at.GetString()}
                Next
            End Using
            Return result
        End Function

        ''' <summary>"ABCD-EFGH-…" as typed, down to the letters and digits the relay expects.</summary>
        Public Shared Function InboxCode(syncCode As String) As String
            Return New String(If(syncCode, "").ToUpperInvariant().Where(Function(ch) Char.IsAsciiLetterOrDigit(ch)).ToArray())
        End Function

        Private Sub CheckInbox(res As HttpResponseMessage)
            If res.StatusCode = HttpStatusCode.NotImplemented Then
                Throw New InvalidOperationException("Your relay's mailbox isn't set up yet. See ""Sending scans to your computer"" in relay\README.md.")
            End If
            If res.StatusCode = HttpStatusCode.NotFound Then
                Throw New InvalidOperationException("Your relay needs updating to receive scans. See ""Updating the relay"" in relay\README.md.")
            End If
            If res.StatusCode = HttpStatusCode.BadRequest Then Throw New InvalidOperationException("That sync code doesn't look right. Check it against your phone.")
            If Not res.IsSuccessStatusCode Then Throw New InvalidOperationException($"The relay answered {CInt(res.StatusCode)}.")
        End Sub

        Private Async Function GetAsync(path As String) As Task(Of JsonDocument)
            If Not IsSetUp Then Throw New InvalidOperationException("Comic lookup isn't set up yet. Add your relay address on the Settings tab.")
            ' The relay remembers answers for a week. Asking with v=8 skips answers saved before
            ' it sent cover prices, story arcs, whole arc lists and writers and artists.
            If Not path.StartsWith("/ping") Then path &= If(path.Contains("?"c), "&", "?") & "v=8"
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

        Friend Shared Function ToIssue(r As JsonElement) As MetronIssue
            Dim issue = New MetronIssue With {
                .MetronId = If(Text(r, "metronId") = "", 0L, Long.Parse(Text(r, "metronId"), CultureInfo.InvariantCulture)),
                .Series = Text(r, "series"), .Volume = Text(r, "volume"), .Number = Text(r, "number"),
                .Title = Text(r, "title"), .Publisher = Text(r, "publisher"),
                .CoverDate = Text(r, "coverDate"), .CoverUrl = Text(r, "coverUrl"), .Price = Price(Text(r, "price")),
                .StoreDate = Text(r, "storeDate")}
            Dim arcs As JsonElement
            If r.TryGetProperty("arcs", arcs) AndAlso arcs.ValueKind = JsonValueKind.Array Then
                For Each a In arcs.EnumerateArray()
                    Dim id As Long
                    If Long.TryParse(Text(a, "id"), NumberStyles.Integer, CultureInfo.InvariantCulture, id) AndAlso Text(a, "name") <> "" Then
                        issue.Arcs.Add(New MetronArc With {.Id = id, .Name = Text(a, "name")})
                    End If
                Next
            End If
            Dim credits As JsonElement
            If r.TryGetProperty("credits", credits) AndAlso credits.ValueKind = JsonValueKind.Array Then
                For Each c In credits.EnumerateArray()
                    If Text(c, "name") = "" Then Continue For
                    Dim credit As New MetronCredit With {.Name = Text(c, "name")}
                    Dim roles As JsonElement
                    If c.TryGetProperty("roles", roles) AndAlso roles.ValueKind = JsonValueKind.Array Then
                        For Each role In roles.EnumerateArray()
                            If role.ValueKind = JsonValueKind.String AndAlso role.GetString() <> "" Then credit.Roles.Add(role.GetString())
                        Next
                    End If
                    issue.Credits.Add(credit)
                Next
            End If
            Return issue
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
