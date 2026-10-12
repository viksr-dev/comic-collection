Option Strict On

Imports System.Globalization
Imports System.Net.Http
Imports System.Text.RegularExpressions
Imports System.Xml.Linq

Namespace Data

    ''' <summary>
    ''' The newest videos of a YouTube channel, from the free feed YouTube gives every channel
    ''' (no account or key needed). It lists the latest 15 uploads.
    ''' </summary>
    Public Module YouTubeFeed

        Private ReadOnly AtomNs As XNamespace = "http://www.w3.org/2005/Atom"
        Private ReadOnly YtNs As XNamespace = "http://www.youtube.com/xml/schemas/2015"
        Private ReadOnly MediaNs As XNamespace = "http://search.yahoo.com/mrss/"

        ''' <summary>"@comictom101", "youtube.com/@comictom101" or a channel link → "@comictom101" or "UC…".</summary>
        Public Function CleanChannel(text As String) As String
            Dim t = If(text, "").Trim()
            Dim id = Regex.Match(t, "(UC[\w-]{22})")
            If id.Success Then Return id.Groups(1).Value
            Dim handle = Regex.Match(t, "@([\w.\-]+)")
            If handle.Success Then Return "@" & handle.Groups(1).Value
            t = t.Trim("/"c)
            Return If(Regex.IsMatch(t, "^[\w.\-]+$"), "@" & t, "")
        End Function

        ''' <summary>Finds a channel's id (UC…) from its @name, by reading its YouTube page.</summary>
        Public Async Function ResolveChannelIdAsync(http As HttpClient, channel As String) As Task(Of String)
            Dim clean = CleanChannel(channel)
            If clean.StartsWith("UC", StringComparison.Ordinal) Then Return clean
            If clean = "" Then Throw New InvalidOperationException($"""{channel}"" isn't a YouTube channel name like @comictom101.")
            Using req As New HttpRequestMessage(HttpMethod.Get, $"https://www.youtube.com/{clean}")
                req.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) ComicCatalog")
                req.Headers.TryAddWithoutValidation("Accept-Language", "en")
                ' Skips the cookie consent page shown in some countries.
                req.Headers.TryAddWithoutValidation("Cookie", "CONSENT=YES+1; SOCS=CAI")
                Using res = Await http.SendAsync(req)
                    If res.StatusCode = Net.HttpStatusCode.NotFound Then Throw New InvalidOperationException($"YouTube has no channel called {clean}.")
                    res.EnsureSuccessStatusCode()
                    Dim id = ChannelIdFromPage(Await res.Content.ReadAsStringAsync())
                    If id = "" Then Throw New InvalidOperationException($"Couldn't find {clean} on YouTube.")
                    Return id
                End Using
            End Using
        End Function

        ''' <summary>The channel id written in a channel's page.</summary>
        Public Function ChannelIdFromPage(html As String) As String
            For Each pattern In {"""externalId"":""(UC[\w-]{22})""", "<link rel=""canonical"" href=""https://www\.youtube\.com/channel/(UC[\w-]{22})""",
                                 "channel_id=(UC[\w-]{22})", """channelId"":""(UC[\w-]{22})"""}
                Dim m = Regex.Match(html, pattern)
                If m.Success Then Return m.Groups(1).Value
            Next
            Return ""
        End Function

        Public Function FeedUrl(channelId As String) As String
            Return "https://www.youtube.com/feeds/videos.xml?channel_id=" & Uri.EscapeDataString(channelId)
        End Function

        ''' <summary>The channel's latest videos, newest first.</summary>
        Public Async Function LatestAsync(http As HttpClient, channelId As String) As Task(Of List(Of VideoItem))
            Using res = Await http.GetAsync(FeedUrl(channelId))
                If res.StatusCode = Net.HttpStatusCode.NotFound Then Throw New InvalidOperationException("YouTube couldn't find that channel's videos.")
                res.EnsureSuccessStatusCode()
                Return ParseFeed(Await res.Content.ReadAsStringAsync())
            End Using
        End Function

        ''' <summary>Reads YouTube's video feed (Atom). Shorts are included; they're videos too.</summary>
        Public Function ParseFeed(xml As String) As List(Of VideoItem)
            Dim doc = XDocument.Parse(xml)
            Dim channel = If(doc.Root?.Element(AtomNs + "title")?.Value, "")
            Dim list As New List(Of VideoItem)
            For Each entry In doc.Descendants(AtomNs + "entry")
                Dim id = If(entry.Element(YtNs + "videoId")?.Value, "")
                If id = "" Then Continue For
                Dim published As DateTimeOffset
                DateTimeOffset.TryParse(If(entry.Element(AtomNs + "published")?.Value, ""), CultureInfo.InvariantCulture, DateTimeStyles.None, published)
                Dim thumb = entry.Descendants(MediaNs + "thumbnail").FirstOrDefault()?.Attribute("url")?.Value
                list.Add(New VideoItem With {
                    .VideoId = id,
                    .Channel = If(entry.Element(AtomNs + "author")?.Element(AtomNs + "name")?.Value, channel),
                    .Title = If(entry.Element(AtomNs + "title")?.Value, ""),
                    .Published = published,
                    .Thumbnail = If(thumb, $"https://i.ytimg.com/vi/{id}/hqdefault.jpg")})
            Next
            Return list.OrderByDescending(Function(v) v.Published).ToList()
        End Function

    End Module

End Namespace
