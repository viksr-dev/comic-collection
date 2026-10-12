Option Strict On

Imports System.Data
Imports System.Globalization
Imports System.Text.RegularExpressions

Namespace Data

    ''' <summary>A box or shelf and what's in it, for its label.</summary>
    Public Class BoxContents
        Public Property Name As String = ""
        Public Property Count As Integer
        ''' <summary>One line per series, e.g. "Batman (2016) #1-5, 7, 9-12".</summary>
        Public Property Lines As New List(Of String)
    End Class

    ''' <summary>A wishlist comic with a most-you'd-pay, ready for an eBay search.</summary>
    Public Class DealTarget
        Public Property WishId As Long
        Public Property Series As String = ""
        Public Property Volume As String = ""
        Public Property Issue As String = ""
        Public Property MaxPrice As Double
    End Class

    ''' <summary>One eBay listing.</summary>
    Public Class EbayListing
        Public Property ItemId As String = ""
        Public Property Title As String = ""
        Public Property Price As Double
        Public Property Url As String = ""
    End Class

    Partial Public Class ComicDb

        ' ---------- ratings and reviews ----------

        ''' <summary>Gives comics 1 to 5 stars (0 clears it). A review of Nothing leaves the old one as it is.</summary>
        Public Sub SetRating(comicIds As IEnumerable(Of Long), stars As Integer, Optional review As String = Nothing)
            UpdateEach(comicIds, "UPDATE collection SET rating = $p1, review = IFNULL($p2, review) WHERE comic_id = $p0",
                       Math.Max(0, Math.Min(5, stars)), If(review Is Nothing, Nothing, CObj(review.Trim())))
        End Sub

        ''' <summary>"★★★★" for 4 stars, "" for none.</summary>
        Public Shared Function Stars(rating As Integer) As String
            Return New String("★"c, Math.Max(0, Math.Min(5, rating)))
        End Function

        ' ---------- box labels ----------

        ''' <summary>
        ''' Every box and shelf with what's in it, one line per series with runs of issues joined up
        ''' ("Batman #1-5, 7"). Boxes in natural order ("Box 2" before "Box 10").
        ''' </summary>
        Public Function BoxLabels() As List(Of BoxContents)
            Dim rows As DataTable
            Using conn = Open()
                rows = Query(conn,
                    "SELECT location, series, IFNULL(volume, '') AS volume, issue, issue_sort, quantity FROM v_collection WHERE location <> ''
                     ORDER BY CASE WHEN series LIKE 'The %' THEN substr(series, 5) ELSE series END COLLATE NOCASE, volume, issue_sort, issue")
            End Using
            Dim boxes As New List(Of BoxContents)
            For Each box In rows.Rows.Cast(Of DataRow)().GroupBy(Function(r) CStr(r("location")), StringComparer.OrdinalIgnoreCase).
                    OrderBy(Function(g) NaturalKey(g.Key), StringComparer.OrdinalIgnoreCase)
                Dim item As New BoxContents With {.Name = box.Key, .Count = box.Sum(Function(r) Convert.ToInt32(r("quantity"), CultureInfo.InvariantCulture))}
                For Each run In box.GroupBy(Function(r) (CStr(r("series")), CStr(r("volume"))))
                    Dim name = run.Key.Item1 & If(run.Key.Item2 <> "", $" ({run.Key.Item2})", "")
                    Dim issues = run.Select(Function(r) (Issue:=Convert.ToString(r("issue"), CultureInfo.InvariantCulture),
                                                         Sort:=If(r.IsNull("issue_sort"), CType(Nothing, Double?), Convert.ToDouble(r("issue_sort"), CultureInfo.InvariantCulture)))).
                                     Where(Function(i) i.Issue <> "").ToList()
                    item.Lines.Add(If(issues.Count = 0, name, $"{name} #{IssueRanges(issues)}"))
                Next
                boxes.Add(item)
            Next
            Return boxes
        End Function

        ''' <summary>"1-5, 7, 9-12, Annual 1" from a list of issues. Whole numbers next to each other are joined.</summary>
        Public Shared Function IssueRanges(issues As IEnumerable(Of (Issue As String, Sort As Double?))) As String
            Dim parts As New List(Of String)
            Dim whole = issues.Where(Function(i) i.Sort.HasValue AndAlso i.Sort.Value = Math.Floor(i.Sort.Value) AndAlso Regex.IsMatch(i.Issue, "^\d+$")).
                               Select(Function(i) CLng(i.Sort.Value)).Distinct().OrderBy(Function(n) n).ToList()
            Dim i0 = 0
            While i0 < whole.Count
                Dim i1 = i0
                While i1 + 1 < whole.Count AndAlso whole(i1 + 1) = whole(i1) + 1
                    i1 += 1
                End While
                parts.Add(If(i1 > i0, $"{whole(i0)}-{whole(i1)}", whole(i0).ToString(CultureInfo.InvariantCulture)))
                i0 = i1 + 1
            End While
            Dim wholeText = New HashSet(Of String)(whole.Select(Function(n) n.ToString(CultureInfo.InvariantCulture)))
            For Each other In issues.Select(Function(i) i.Issue).Distinct(StringComparer.OrdinalIgnoreCase)
                If Not (Regex.IsMatch(other, "^\d+$") AndAlso wholeText.Contains(Long.Parse(other, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture))) Then parts.Add(other)
            Next
            Return String.Join(", ", parts)
        End Function

        ' ---------- convention signing list ----------

        ''' <summary>Everyone who wrote or drew comics you own, with how many, most first.</summary>
        Public Function CreatorsYouOwn() As List(Of ChartItem)
            Using conn = Open()
                Return Query(conn,
                    "SELECT cr.name, COUNT(DISTINCT cc.comic_id) AS n FROM comic_creators cc
                     JOIN creators cr ON cr.id = cc.creator_id JOIN collection col ON col.comic_id = cc.comic_id
                     GROUP BY cr.id ORDER BY n DESC, cr.name COLLATE NOCASE").Rows.Cast(Of DataRow)().
                    Select(Function(r) New ChartItem With {.Label = CStr(r("name")), .Count = Convert.ToInt32(r("n"), CultureInfo.InvariantCulture)}).ToList()
            End Using
        End Function

        ''' <summary>
        ''' Your comics by any of these people (guests at a convention), to bring along for signing.
        ''' Names match whatever the case, spacing or full stops ("J Scott Campbell" finds "J. Scott Campbell").
        ''' Columns: Guest, Comic, Role, Box, Key issue, Value. Best first for each guest.
        ''' </summary>
        Public Function SigningList(guests As IEnumerable(Of String)) As DataTable
            Dim wanted As New Dictionary(Of String, String)
            For Each g In guests
                Dim key = NameKey(g)
                If key <> "" AndAlso Not wanted.ContainsKey(key) Then wanted(key) = g.Trim()
            Next
            Dim result As New DataTable()
            For Each col In {"Guest", "Comic", "Role", "Box", "Key issue"}
                result.Columns.Add(col, GetType(String))
            Next
            result.Columns.Add("Value", GetType(Double))
            If wanted.Count = 0 Then Return result
            Dim rows As DataTable
            Using conn = Open()
                rows = Query(conn,
                    $"SELECT cr.name AS creator, {ComicLabelSql} AS comic, group_concat(DISTINCT cc.role) AS roles, v.location, v.key_note, v.total_value,
                             v.series, v.issue_sort
                      FROM comic_creators cc JOIN creators cr ON cr.id = cc.creator_id JOIN v_collection v ON v.comic_id = cc.comic_id
                      GROUP BY cr.id, v.comic_id")
            End Using
            For Each r In rows.Rows.Cast(Of DataRow)().
                    Where(Function(x) wanted.ContainsKey(NameKey(CStr(x("creator"))))).
                    OrderBy(Function(x) CStr(x("creator")), StringComparer.OrdinalIgnoreCase).
                    ThenByDescending(Function(x) CStr(x("key_note")) <> "").
                    ThenByDescending(Function(x) If(x.IsNull("total_value"), 0.0, Convert.ToDouble(x("total_value"), CultureInfo.InvariantCulture))).
                    ThenBy(Function(x) CStr(x("series")), StringComparer.OrdinalIgnoreCase).
                    ThenBy(Function(x) If(x.IsNull("issue_sort"), 0.0, Convert.ToDouble(x("issue_sort"), CultureInfo.InvariantCulture)))
                result.Rows.Add(CStr(r("creator")), CStr(r("comic")), Convert.ToString(r("roles"), CultureInfo.InvariantCulture).Replace(",", ", "),
                                CStr(r("location")), CStr(r("key_note")), r("total_value"))
            Next
            Return result
        End Function

        ''' <summary>Names from pasted text: one per line, or separated by commas, semicolons or bullets.</summary>
        Public Shared Function GuestNames(text As String) As List(Of String)
            Return If(text, "").Split({vbCr, vbLf, ",", ";", "•", "|"}, StringSplitOptions.RemoveEmptyEntries).
                Select(Function(n) n.Trim()).Where(Function(n) n.Length > 2).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
        End Function

        ''' <summary>"j scott campbell" for "J. Scott  Campbell".</summary>
        Public Shared Function NameKey(name As String) As String
            Return Regex.Replace(Regex.Replace(If(name, "").ToLowerInvariant(), "[.,]", " "), "\s+", " ").Trim()
        End Function

        ' ---------- wishlist deals on eBay ----------

        ''' <summary>Wishlist comics with a most-you'd-pay that haven't been searched for today.</summary>
        Public Function WishesNeedingDealCheck(Optional today As Date? = Nothing) As List(Of DealTarget)
            Dim day = If(today, Date.Today).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            Using conn = Open()
                Return Query(conn,
                    "SELECT w.id, COALESCE(s.name, w.series_name) AS series, IFNULL(s.volume, '') AS volume,
                            COALESCE(c.issue_number, w.issue_number) AS issue, w.max_price
                     FROM wishlist w LEFT JOIN comics c ON c.id = w.comic_id LEFT JOIN series s ON s.id = c.series_id
                     WHERE w.max_price > 0 AND w.deal_checked < $p0 AND COALESCE(c.issue_number, w.issue_number) <> ''
                     ORDER BY w.deal_checked, w.priority", day).Rows.Cast(Of DataRow)().
                    Select(Function(r) New DealTarget With {
                        .WishId = Convert.ToInt64(r("id"), CultureInfo.InvariantCulture), .Series = CStr(r("series")),
                        .Volume = Convert.ToString(r("volume"), CultureInfo.InvariantCulture), .Issue = CStr(r("issue")),
                        .MaxPrice = Convert.ToDouble(r("max_price"), CultureInfo.InvariantCulture)}).ToList()
            End Using
        End Function

        ''' <summary>
        ''' Saves what eBay has now for a wishlist comic. Listings no longer there (sold or ended) are dropped,
        ''' and ones you said no to stay hidden. Returns how many are new.
        ''' </summary>
        Public Function SaveDeals(wishId As Long, listings As IEnumerable(Of EbayListing), Optional day As Date? = Nothing) As Integer
            Dim d = If(day, Date.Today).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            Dim added = 0
            Using conn = Open()
                Dim ids = listings.Select(Function(l) l.ItemId).ToList()
                Dim existing = Query(conn, "SELECT id, item_id FROM ebay_deals WHERE wish_id = $p0 AND NOT dismissed", wishId)
                Using tx = conn.BeginTransaction()
                    For Each old As DataRow In existing.Rows
                        If Not ids.Contains(CStr(old("item_id"))) Then Execute(conn, tx, "DELETE FROM ebay_deals WHERE id = $p0", old("id"))
                    Next
                    For Each l In listings
                        Using cmd = Command(conn, "INSERT OR IGNORE INTO ebay_deals (wish_id, item_id, title, price, url, found_on) VALUES ($p0, $p1, $p2, $p3, $p4, $p5)",
                                            tx, wishId, l.ItemId, l.Title, l.Price, l.Url, d)
                            added += cmd.ExecuteNonQuery()
                        End Using
                        Execute(conn, tx, "UPDATE ebay_deals SET price = $p2, title = $p3 WHERE wish_id = $p0 AND item_id = $p1", wishId, l.ItemId, l.Price, l.Title)
                    Next
                    Execute(conn, tx, "UPDATE wishlist SET deal_checked = $p1 WHERE id = $p0", wishId, d)
                    tx.Commit()
                End Using
            End Using
            Return added
        End Function

        ''' <summary>Deals you haven't said no to. Columns: id, Comic, Price, Max price, Listing, Found, url.</summary>
        Public Function OpenDeals() As DataTable
            Using conn = Open()
                Return Query(conn,
                    "SELECT d.id, COALESCE(s.name, w.series_name) || ' #' || COALESCE(c.issue_number, w.issue_number) AS Comic,
                            d.price AS Price, w.max_price AS [Max price], d.title AS Listing, d.found_on AS Found, d.url
                     FROM ebay_deals d JOIN wishlist w ON w.id = d.wish_id
                     LEFT JOIN comics c ON c.id = w.comic_id LEFT JOIN series s ON s.id = c.series_id
                     WHERE NOT d.dismissed ORDER BY d.found_on DESC, d.price")
            End Using
        End Function

        Public Sub DismissDeals(dealIds As IEnumerable(Of Long))
            Using conn = Open()
                For Each id In dealIds
                    Execute(conn, Nothing, "UPDATE ebay_deals SET dismissed = 1 WHERE id = $p0", id)
                Next
            End Using
        End Sub

        ''' <summary>Changes the most you'd pay (your dollars) for wishlist comics, and searches again soon.</summary>
        Public Sub SetWishMaxPrice(wishIds As IEnumerable(Of Long), maxPrice As Double?)
            Using conn = Open()
                For Each id In wishIds
                    Execute(conn, Nothing, "UPDATE wishlist SET max_price = $p1, deal_checked = '' WHERE id = $p0", id, maxPrice)
                    If Not maxPrice.HasValue Then Execute(conn, Nothing, "DELETE FROM ebay_deals WHERE wish_id = $p0 AND NOT dismissed", id)
                Next
            End Using
        End Sub

        Public Function DealCount() As Integer
            Using conn = Open()
                Return Convert.ToInt32(Scalar(conn, "SELECT COUNT(*) FROM ebay_deals WHERE NOT dismissed", Nothing), CultureInfo.InvariantCulture)
            End Using
        End Function

        ' ---------- worth getting graded? ----------

        ''' <summary>
        ''' Raw (unslabbed) comics with an eBay estimate of at least minRaw whose CGC 9.8 price hasn't been
        ''' checked in maxAgeDays. Cheaper comics are never worth grading, so they're skipped.
        ''' </summary>
        Public Function ComicsNeedingGradedCheck(Optional minRaw As Double = 15, Optional maxAgeDays As Integer = 60,
                                                 Optional today As Date? = Nothing) As List(Of EbayTarget)
            Dim cutoff = If(today, Date.Today).AddDays(-maxAgeDays).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            Using conn = Open()
                Return Query(conn,
                    "SELECT comic_id, series, IFNULL(volume, '') AS volume, issue, variant_name FROM v_collection
                     WHERE graded_by = '' AND issue <> '' AND ebay_value >= $p0 AND (graded_checked = '' OR graded_checked < $p1)
                     ORDER BY graded_checked, ebay_value DESC", minRaw, cutoff).Rows.Cast(Of DataRow)().
                    Select(Function(r) New EbayTarget With {
                        .ComicId = Convert.ToInt64(r("comic_id"), CultureInfo.InvariantCulture), .Series = CStr(r("series")),
                        .Volume = Convert.ToString(r("volume"), CultureInfo.InvariantCulture), .Issue = CStr(r("issue")),
                        .VariantName = CStr(r("variant_name")), .GradedBy = "CGC", .Grade = "9.8"}).ToList()
            End Using
        End Function

        Public Sub SetGradedEstimate(comicId As Long, estimate As EbayEstimate, Optional day As Date? = Nothing)
            UpdateEach({comicId}, "UPDATE collection SET ebay_graded = $p1, graded_checked = $p2 WHERE comic_id = $p0",
                       estimate.Typical, If(day, Date.Today).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
        End Sub

        ''' <summary>
        ''' Raw comics with both prices, best gain first: what a CGC 9.8 copy goes for, less the raw price and
        ''' the cost of grading. Columns: comic_id, Comic, Condition, Raw, CGC 9.8, Gain.
        ''' </summary>
        Public Function WorthGrading(gradingCost As Double) As DataTable
            Using conn = Open()
                Return Query(conn,
                    $"SELECT v.comic_id, {ComicLabelSql} AS Comic, v.condition AS Condition, v.ebay_value AS Raw, v.ebay_graded AS [CGC 9.8],
                             round(v.ebay_graded - v.ebay_value - $p0, 2) AS Gain
                      FROM v_collection v WHERE v.graded_by = '' AND v.ebay_value IS NOT NULL AND v.ebay_graded IS NOT NULL
                      ORDER BY Gain DESC", gradingCost)
            End Using
        End Function

        ' ---------- rising on eBay ----------

        ''' <summary>
        ''' Comics whose eBay estimate went up most since an estimate at least minDays before the latest one.
        ''' Label like "Batman #404", Value is the rise in US$ and Count the rise in percent.
        ''' </summary>
        Public Function RisingComics(Optional top As Integer = 10, Optional minDays As Integer = 20) As List(Of ChartItem)
            Dim rows As DataTable
            Using conn = Open()
                rows = Query(conn,
                    $"WITH latest AS (SELECT comic_id, MAX(day) AS day FROM ebay_history GROUP BY comic_id)
                      SELECT {ComicLabelSql} AS label, now.value AS now_value,
                             (SELECT h.value FROM ebay_history h WHERE h.comic_id = l.comic_id AND h.day <= date(l.day, '-' || $p0 || ' days')
                              ORDER BY h.day DESC LIMIT 1) AS before_value
                      FROM latest l JOIN ebay_history now ON now.comic_id = l.comic_id AND now.day = l.day
                      JOIN v_collection v ON v.comic_id = l.comic_id", minDays)
            End Using
            Return rows.Rows.Cast(Of DataRow)().Where(Function(r) Not r.IsNull("before_value")).
                Select(Function(r)
                           Dim now = Convert.ToDouble(r("now_value"), CultureInfo.InvariantCulture)
                           Dim before = Convert.ToDouble(r("before_value"), CultureInfo.InvariantCulture)
                           Return New ChartItem With {.Label = CStr(r("label")), .Value = Math.Round(now - before, 2),
                                                      .Count = If(before > 0, CInt(Math.Round((now - before) * 100 / before)), 0)}
                       End Function).
                Where(Function(c) c.Value > 0).OrderByDescending(Function(c) c.Value).Take(top).ToList()
        End Function

    End Class

End Namespace
