Option Strict On

Imports System.Data
Imports System.Globalization
Imports System.Net
Imports System.Text

Namespace Data

    ''' <summary>What goes on the share page.</summary>
    Public Class ShareOptions
        ''' <summary>The heading, e.g. "James's comics".</summary>
        Public Property Title As String = "My comics"
        Public Property IncludeCollection As Boolean = True
        Public Property IncludeWishlist As Boolean = True
        ''' <summary>Show what comics are worth. Off by default: it's not something to send everyone.</summary>
        Public Property ShowValues As Boolean
        Public Property ShowCovers As Boolean = True
    End Class

    ''' <summary>
    ''' Makes a single web page (one .html file) of your collection and/or wishlist, to send to friends and family.
    ''' It works offline and without scripts; the search box needs scripts, the covers need the internet.
    ''' </summary>
    Public Module SharePage

        Public Function Build(db As ComicDb, options As ShareOptions, Optional madeOn As Date? = Nothing) As String
            Dim day = If(madeOn, Date.Today)
            Dim owned = If(options.IncludeCollection, db.ShareRows(), Nothing)
            Dim wanted = If(options.IncludeWishlist, db.GetWishlist(), Nothing)
            Dim html As New StringBuilder()
            html.AppendLine("<!doctype html>")
            html.AppendLine("<html lang=""en""><head><meta charset=""utf-8"">")
            html.AppendLine("<meta name=""viewport"" content=""width=device-width, initial-scale=1"">")
            html.AppendLine("<meta name=""robots"" content=""noindex"">")
            html.Append("<title>").Append(E(options.Title)).AppendLine("</title>")
            html.AppendLine("<style>")
            html.AppendLine(Css)
            html.AppendLine("</style></head><body>")
            html.Append("<header><h1>").Append(E(options.Title)).AppendLine("</h1>")
            Dim summary As New List(Of String)
            If owned IsNot Nothing Then
                Dim copies = owned.Rows.Cast(Of DataRow)().Sum(Function(r) Convert.ToInt32(r("quantity"), CultureInfo.InvariantCulture))
                summary.Add($"{owned.Rows.Count:N0} comic{If(owned.Rows.Count = 1, "", "s")}")
                If options.ShowValues Then
                    Dim total = owned.Rows.Cast(Of DataRow)().Sum(Function(r) If(IsDBNull(r("total_value")), 0, Convert.ToDouble(r("total_value"), CultureInfo.InvariantCulture)))
                    summary.Add($"worth about ${total.ToString("N0", CultureInfo.InvariantCulture)}")
                End If
            End If
            If wanted IsNot Nothing Then summary.Add($"{wanted.Rows.Count:N0} on the wishlist")
            summary.Add($"updated {day.ToString("d MMMM yyyy", CultureInfo.InvariantCulture)}")
            html.Append("<p class=""sub"">").Append(E(String.Join(" · ", summary))).AppendLine("</p>")
            If owned IsNot Nothing AndAlso wanted IsNot Nothing Then
                html.AppendLine("<nav><a href=""#collection"">Collection</a><a href=""#wishlist"">Wishlist</a></nav>")
            End If
            html.AppendLine("<input id=""q"" type=""search"" placeholder=""Search…"" hidden>")
            html.AppendLine("</header><main>")

            If owned IsNot Nothing Then
                html.AppendLine("<section id=""collection""><h2>Collection</h2>")
                Dim i = 0
                While i < owned.Rows.Count
                    Dim series = Txt(owned.Rows(i), "series")
                    Dim volume = Txt(owned.Rows(i), "volume")
                    Dim group As New List(Of DataRow)
                    While i < owned.Rows.Count AndAlso Txt(owned.Rows(i), "series").Equals(series, StringComparison.OrdinalIgnoreCase) AndAlso
                          Txt(owned.Rows(i), "volume").Equals(volume, StringComparison.OrdinalIgnoreCase)
                        group.Add(owned.Rows(i))
                        i += 1
                    End While
                    Dim name = series & If(volume <> "", $" ({volume})", "")
                    html.Append("<div class=""series""><h3>").Append(E(name)).Append(" <span>").Append(group.Count).AppendLine("</span></h3>")
                    html.AppendLine(If(options.ShowCovers, "<ul class=""covers"">", "<ul class=""rows"">"))
                    For Each r In group
                        Dim issue = Txt(r, "issue")
                        Dim words As New List(Of String)
                        For Each col In {"variant_name", "title"}
                            If Txt(r, col) <> "" Then words.Add(Txt(r, col))
                        Next
                        Dim details As New List(Of String)
                        If Txt(r, "cover_date") <> "" Then details.Add(Txt(r, "cover_date"))
                        If Txt(r, "condition") <> "" Then details.Add(Txt(r, "condition"))
                        Dim qty = Convert.ToInt32(r("quantity"), CultureInfo.InvariantCulture)
                        If qty > 1 Then details.Add($"{qty} copies")
                        If options.ShowValues AndAlso Not IsDBNull(r("total_value")) Then
                            details.Add("$" & Convert.ToDouble(r("total_value"), CultureInfo.InvariantCulture).ToString("N2", CultureInfo.InvariantCulture))
                        End If
                        html.Append("<li data-s=""").Append(E($"{name} #{issue} {String.Join(" ", words)} {Txt(r, "publisher")}".ToLowerInvariant())).Append(""">")
                        If options.ShowCovers Then
                            Dim cover = Txt(r, "cover_url")
                            If cover.StartsWith("https://", StringComparison.OrdinalIgnoreCase) Then
                                html.Append("<img loading=""lazy"" alt="""" src=""").Append(E(cover)).Append(""">")
                            Else
                                html.Append("<div class=""nocover"">").Append(E(If(issue <> "", "#" & issue, name))).Append("</div>")
                            End If
                        End If
                        html.Append("<b>").Append(E(If(issue <> "", "#" & issue, name))).Append("</b>")
                        If words.Count > 0 Then html.Append("<i>").Append(E(String.Join(" · ", words))).Append("</i>")
                        If details.Count > 0 Then html.Append("<small>").Append(E(String.Join(" · ", details))).Append("</small>")
                        html.AppendLine("</li>")
                    Next
                    html.AppendLine("</ul></div>")
                End While
                If owned.Rows.Count = 0 Then html.AppendLine("<p>Nothing here yet.</p>")
                html.AppendLine("</section>")
            End If

            If wanted IsNot Nothing Then
                html.AppendLine("<section id=""wishlist""><h2>Wishlist</h2>")
                If wanted.Rows.Count = 0 Then
                    html.AppendLine("<p>Nothing on the wishlist right now.</p>")
                Else
                    html.AppendLine("<ul class=""wish"">")
                    For Each r As DataRow In wanted.Rows
                        Dim name = Txt(r, "Series") & If(Txt(r, "Issue") <> "", " #" & Txt(r, "Issue"), "")
                        Dim extra As New List(Of String)
                        If Convert.ToInt32(r("Priority"), CultureInfo.InvariantCulture) = 1 Then extra.Add("most wanted")
                        If Txt(r, "Story arc") <> "" Then extra.Add(Txt(r, "Story arc"))
                        html.Append("<li data-s=""").Append(E($"{name} {String.Join(" ", extra)}".ToLowerInvariant())).Append("""><b>").Append(E(name)).Append("</b>")
                        If extra.Count > 0 Then html.Append("<small>").Append(E(String.Join(" · ", extra))).Append("</small>")
                        html.AppendLine("</li>")
                    Next
                    html.AppendLine("</ul>")
                End If
                html.AppendLine("</section>")
            End If

            html.AppendLine("</main><footer>Made with Comic Catalog</footer>")
            html.AppendLine("<script>")
            html.AppendLine(Script)
            html.AppendLine("</script></body></html>")
            Return html.ToString()
        End Function

        Private Function Txt(r As DataRow, col As String) As String
            Return If(IsDBNull(r(col)), "", Convert.ToString(r(col), CultureInfo.InvariantCulture).Trim())
        End Function

        Private Function E(text As String) As String
            Return WebUtility.HtmlEncode(If(text, ""))
        End Function

        Private Const Css As String = "
:root { color-scheme: dark; --bg: #121419; --panel: #1c1f27; --text: #eef0f4; --muted: #9aa1ad; --accent: #f5c518; }
* { box-sizing: border-box; }
body { margin: 0; background: var(--bg); color: var(--text); font: 15px/1.4 'Segoe UI', system-ui, -apple-system, sans-serif; }
header { padding: 20px 16px 12px; background: var(--panel); }
h1 { margin: 0; color: var(--accent); font-size: 24px; letter-spacing: .5px; }
.sub { margin: 4px 0 10px; color: var(--muted); }
nav a { color: var(--text); margin-right: 16px; text-decoration: none; border-bottom: 2px solid var(--accent); }
#q { width: 100%; max-width: 420px; margin-top: 10px; padding: 9px 12px; border-radius: 8px; border: 1px solid #333846; background: var(--bg); color: var(--text); font-size: 16px; }
main { padding: 8px 16px 32px; max-width: 1400px; margin: 0 auto; }
h2 { color: var(--accent); margin: 24px 0 4px; }
h3 { margin: 18px 0 8px; font-size: 17px; }
h3 span { color: var(--muted); font-weight: normal; font-size: 13px; margin-left: 6px; }
ul { list-style: none; margin: 0; padding: 0; }
.covers { display: grid; grid-template-columns: repeat(auto-fill, minmax(100px, 1fr)); gap: 10px; }
.covers li { background: var(--panel); border-radius: 8px; padding: 6px; display: flex; flex-direction: column; gap: 2px; }
.covers img, .nocover { width: 100%; aspect-ratio: 2 / 3; object-fit: cover; border-radius: 4px; background: #2a2e38; }
.nocover { display: flex; align-items: center; justify-content: center; color: var(--muted); font-size: 20px; }
.rows li, .wish li { background: var(--panel); border-radius: 6px; padding: 8px 12px; margin-bottom: 6px; display: flex; flex-wrap: wrap; gap: 4px 12px; align-items: baseline; }
i { color: var(--text); font-style: normal; font-size: 13px; }
small { color: var(--muted); font-size: 12px; }
footer { color: var(--muted); text-align: center; padding: 24px; font-size: 12px; }
[hidden] { display: none !important; }"

        Private Const Script As String = "
var q = document.getElementById('q');
q.hidden = false;
q.addEventListener('input', function () {
  var words = q.value.toLowerCase().split(/\s+/).filter(Boolean);
  document.querySelectorAll('li[data-s]').forEach(function (li) {
    var s = li.getAttribute('data-s');
    li.hidden = !words.every(function (w) { return s.indexOf(w) >= 0; });
  });
  document.querySelectorAll('.series').forEach(function (g) {
    g.hidden = !g.querySelector('li:not([hidden])');
  });
});"

    End Module

End Namespace
