Option Strict On

Imports System.Globalization
Imports System.IO
Imports System.Text
Imports System.Text.RegularExpressions
Imports Docnet.Core
Imports Docnet.Core.Models

Namespace Data

    ''' <summary>One line of a shop receipt, worked out into series, issue and cover.</summary>
    Public Class ReceiptItem
        ''' <summary>The line as the shop wrote it, e.g. "Batman #14 Cover A Matteo Scalera (Batman Bad Seeds)".</summary>
        Public Property Text As String = ""
        Public Property Quantity As Integer = 1
        ''' <summary>What one copy cost, with any postage shared out, so they add up to the receipt's total (ComicHub prices already include GST).</summary>
        Public Property PricePaid As Double?
        Public Property Series As String = ""
        Public Property Issue As String = ""
        Public Property CoverLetter As String = ""
        Public Property VariantName As String = ""
        ''' <summary>"Graphic Novel", "Trade Paperback" and so on for books, "" for single issues.</summary>
        Public Property Format As String = ""
        ''' <summary>Anything else in brackets, e.g. a story arc ("Batman Bad Seeds"). "Mature" is left out.</summary>
        Public Property Extra As String = ""
    End Class

    ''' <summary>A comic shop receipt or invoice, such as the PDFs ComicHub shops email (Heroes for Sale and others).</summary>
    Public Class ShopReceipt
        Public Property Shop As String = ""
        Public Property OrderNumber As String = ""
        ''' <summary>"yyyy-MM-dd", or "" if the receipt doesn't say.</summary>
        Public Property OrderDate As String = ""
        Public Property SubTotal As Double?
        Public Property Total As Double?
        Public Property Items As New List(Of ReceiptItem)
    End Class

    Public Module ShopReceipts

        ''' <summary>The text of a PDF, page after page.</summary>
        Public Function PdfText(bytes As Byte()) As String
            Dim sb As New StringBuilder()
            Using reader = DocLib.Instance.GetDocReader(bytes, New PageDimensions(1.0))
                For i = 0 To reader.GetPageCount() - 1
                    Using page = reader.GetPageReader(i)
                        sb.AppendLine(page.GetText())
                    End Using
                Next
            End Using
            Return sb.ToString()
        End Function

        ' A line ends an item when it finishes with: quantity, (points,) $ price, $ line total.
        Private ReadOnly ItemEnd As New Regex(
            "^(?:(?<text>.*\S)\s+)?(?<qty>\d{1,3})\s+(?:\d+(?:\.\d+)?\s+)?\$\s?(?<price>[\d,]+\.\d{2})\s+\$\s?(?<final>[\d,]+\.\d{2})$",
            RegexOptions.Compiled)

        ''' <summary>Reads a receipt's text. Throws if no comics can be found in it.</summary>
        Public Function Parse(text As String) As ShopReceipt
            Dim receipt As New ShopReceipt()
            Dim lines = If(text, "").Replace(vbCr, vbLf).Split(vbLf(0)).Select(Function(l) Regex.Replace(l, "\s+", " ").Trim()).Where(Function(l) l <> "").ToList()
            If lines.Count = 0 Then Throw New InvalidDataException("There's no text in this file. If it's a photo or scan of a receipt, ask the shop for the emailed PDF.")
            receipt.Shop = lines(0)

            Dim m = Regex.Match(String.Join(vbLf, lines), "Order\s*(?:No|Number|#)\.?:?\s*(\S+)", RegexOptions.IgnoreCase)
            If m.Success Then receipt.OrderNumber = m.Groups(1).Value
            m = Regex.Match(String.Join(vbLf, lines), "(?:Order\s*)?Date\s*:?\s*(\d{1,2})(?:st|nd|rd|th)?\s+([A-Za-z]{3})[a-z]*\.?,?\s+(\d{4})", RegexOptions.IgnoreCase)
            Dim day As Date
            If m.Success AndAlso Date.TryParseExact($"{m.Groups(1).Value} {m.Groups(2).Value} {m.Groups(3).Value}", "d MMM yyyy",
                                                   CultureInfo.InvariantCulture, DateTimeStyles.None, day) Then
                receipt.OrderDate = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            End If

            Dim inItems = Not lines.Any(Function(l) IsHeader(l))
            Dim pending As New List(Of String)
            Dim lineTotals As New List(Of Double)
            For Each line In lines
                If Not inItems Then
                    inItems = IsHeader(line)
                    Continue For
                End If
                Dim money = Regex.Match(line, "^(Sub\s*Total|Sales\s*Tax|GST|Tax|Total|Shipping|Postage|Freight)\b.*?\$\s?([\d,]+\.\d{2})", RegexOptions.IgnoreCase)
                If money.Success Then
                    Dim amount = ToMoney(money.Groups(2).Value)
                    Select Case Regex.Replace(money.Groups(1).Value.ToLowerInvariant(), "\s", "")
                        Case "subtotal" : receipt.SubTotal = amount
                        Case "total" : If Not receipt.Total.HasValue Then receipt.Total = amount
                    End Select
                    pending.Clear()
                    Continue For
                End If
                Dim item = ItemEnd.Match(line)
                If Not item.Success Then
                    pending.Add(line)
                    Continue For
                End If
                ' A long title wraps onto the lines above its numbers.
                Dim words = String.Join(" ", pending.Concat({item.Groups("text").Value}).Where(Function(w) w <> ""))
                pending.Clear()
                If words = "" Then Continue For
                Dim qty = Math.Max(1, Integer.Parse(item.Groups("qty").Value, CultureInfo.InvariantCulture))
                Dim final = ToMoney(item.Groups("final").Value)
                Dim r = SplitTitle(words)
                r.Quantity = qty
                receipt.Items.Add(r)
                lineTotals.Add(final)
            Next
            If receipt.Items.Count = 0 Then Throw New InvalidDataException("Couldn't find any comics in this receipt.")

            ' Share any postage out, so the prices paid add up to what you actually paid.
            Dim sum = lineTotals.Sum()
            Dim scale = If(receipt.Total.HasValue AndAlso sum > 0 AndAlso receipt.Total.Value >= sum, receipt.Total.Value / sum, 1.0)
            For i = 0 To receipt.Items.Count - 1
                receipt.Items(i).PricePaid = Math.Round(lineTotals(i) * scale / receipt.Items(i).Quantity, 2)
            Next
            Return receipt
        End Function

        Private Function IsHeader(line As String) As Boolean
            Return Regex.IsMatch(line, "^(Item|Product|Description|Title)\b.*\b(Qty|Quantity)\b.*\bPrice\b", RegexOptions.IgnoreCase)
        End Function

        Private Function ToMoney(text As String) As Double
            Return Double.Parse(text.Replace(",", ""), NumberStyles.Number, CultureInfo.InvariantCulture)
        End Function

        ''' <summary>"Walking Dead Deluxe #146 Cover A David Finch (Mature)" into series, issue and cover.</summary>
        Public Function SplitTitle(text As String) As ReceiptItem
            Dim r As New ReceiptItem With {.Text = text.Trim()}
            Dim extras As New List(Of String)
            Dim plain = Regex.Replace(r.Text, "\(([^)]*)\)", Function(p)
                                                                 Dim inside = p.Groups(1).Value.Trim()
                                                                 If inside <> "" AndAlso Not Regex.IsMatch(inside, "^(Mature|MR|Mature Readers|Res|Net)$", RegexOptions.IgnoreCase) Then extras.Add(inside)
                                                                 Return " "
                                                             End Function)
            plain = Regex.Replace(plain, "\s+", " ").Trim()
            r.Extra = String.Join("; ", extras)

            Dim m = Regex.Match(plain, "^(?<series>.+?)\s*#\s*(?<issue>[0-9][0-9.]*[A-Za-z]?|½|[A-Za-z]{1,3}\b)(?:\s+(?<rest>.*))?$")
            If m.Success Then
                r.Series = m.Groups("series").Value.Trim()
                r.Issue = m.Groups("issue").Value
                Dim rest = m.Groups("rest").Value.Trim()
                Dim cover = Regex.Match(rest, "^(?:Cover|Cvr)\s+(?<letter>[A-Z]{1,2})\b\s*(?<name>.*)$", RegexOptions.IgnoreCase)
                If cover.Success Then
                    r.CoverLetter = cover.Groups("letter").Value.ToUpperInvariant()
                    r.VariantName = cover.Groups("name").Value.Trim()
                Else
                    r.VariantName = rest
                End If
                Return r
            End If

            ' Books: no issue number, but often a volume.
            Dim formats = {("\bOmnibus\b", "Omnibus"), ("\b(Hardcover|HC)\b", "Hardcover"), ("\b(Graphic Novel|OGN|GN)\b", "Graphic Novel"),
                           ("\b(Trade Paperback|TPB|TP)\b", "Trade Paperback")}
            For Each f In formats
                If Regex.IsMatch(plain, f.Item1, RegexOptions.IgnoreCase) Then
                    r.Format = f.Item2
                    Exit For
                End If
            Next
            Dim volume = Regex.Match(plain, "\bVol(?:ume)?\.?\s*(\d+)\b", RegexOptions.IgnoreCase)
            If volume.Success Then
                r.Issue = volume.Groups(1).Value
                If r.Format = "" Then r.Format = "Trade Paperback"
            End If
            r.Series = plain
            Return r
        End Function

        Friend Function Squash(name As String) As String
            Dim s = Regex.Replace(If(name, "").ToLowerInvariant(), "[^a-z0-9]", "")
            Return If(s.StartsWith("the"), s.Substring(3), s)
        End Function

        ''' <summary>
        ''' The Metron issue a receipt line is, from a search by series and issue: the one with the same issue number
        ''' (and series name), picking the one in shops closest before the order date when a name like Batman has many series.
        ''' Nothing if it can't tell.
        ''' </summary>
        Public Function PickMatch(results As IEnumerable(Of MetronIssue), item As ReceiptItem, orderDate As String) As MetronIssue
            Dim num = Function(n As String) Regex.Replace(If(n, "").Trim().ToLowerInvariant(), "^0+(?=\d)", "")
            Dim m = results.Where(Function(x) num(x.Number) = num(item.Issue)).ToList()
            Dim sameName = m.Where(Function(x) Squash(x.Series) = Squash(item.Series)).ToList()
            If sameName.Count > 0 Then m = sameName
            If m.Count <= 1 Then Return m.FirstOrDefault()
            Dim ordered As Date
            If Not Date.TryParseExact(orderDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, ordered) Then Return Nothing
            ' New comics are bought within a few months of coming out (pre-orders a little before).
            Dim recent = m.Select(Function(x)
                                      Dim d As Date
                                      Dim ok = Date.TryParseExact(x.StoreDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, d)
                                      Return (Issue:=x, Day:=If(ok, d, Date.MinValue))
                                  End Function).
                          Where(Function(x) x.Day >= ordered.AddDays(-120) AndAlso x.Day <= ordered.AddDays(21)).
                          OrderByDescending(Function(x) x.Day).ToList()
            Return If(recent.Count > 0, recent(0).Issue, Nothing)
        End Function

        ''' <summary>A comic to save from a receipt line, with Metron's details when it was found.</summary>
        Public Function ToComic(item As ReceiptItem, receipt As ShopReceipt, match As MetronIssue) As ComicRecord
            Dim note = $"Bought from {receipt.Shop}" & If(receipt.OrderNumber <> "", $", order {receipt.OrderNumber}", "") &
                       If(item.Extra <> "", $" ({item.Extra})", "")
            Dim c As New ComicRecord With {
                .Series = item.Series, .Issue = item.Issue, .CoverLetter = item.CoverLetter, .VariantName = item.VariantName,
                .Format = item.Format, .Quantity = item.Quantity, .PricePaid = item.PricePaid, .PurchaseDate = receipt.OrderDate,
                .Notes = note}
            If match IsNot Nothing Then
                c.Series = match.Series
                c.Volume = match.Volume
                c.Issue = If(match.Number <> "", match.Number, item.Issue)
                c.Title = match.Title
                c.Publisher = match.Publisher
                c.CoverDate = match.CoverDate
                c.CoverUrl = match.CoverUrl
                If match.MetronId > 0 Then c.MetronId = match.MetronId
            End If
            Return c
        End Function

    End Module

    Partial Public Class ComicDb

        ''' <summary>
        ''' Adds a comic bought from a shop. If you already have that exact comic, it's just another copy
        ''' (nothing else about the one you have changes). Returns its comic id.
        ''' </summary>
        Public Function AddFromReceipt(c As ComicRecord) As Long
            Using conn = Open()
                Using tx = conn.BeginTransaction()
                    Dim existing = Scalar(conn,
                        "SELECT c.id FROM comics c JOIN series s ON s.id = c.series_id JOIN collection k ON k.comic_id = c.id
                         WHERE s.name = $p0 AND s.volume = $p1 AND c.issue_number = $p2 AND c.variant = $p3 AND c.variant_name = $p4",
                        tx, c.Series.Trim(), c.Volume.Trim(), c.Issue.Trim(), c.CoverLetter.Trim(), c.VariantName.Trim())
                    Dim id As Long
                    If existing IsNot Nothing Then
                        id = Convert.ToInt64(existing, CultureInfo.InvariantCulture)
                        Execute(conn, tx, "UPDATE collection SET quantity = quantity + $p1 WHERE comic_id = $p0", id, Math.Max(1, c.Quantity))
                    Else
                        id = SaveComic(conn, tx, c, False)
                    End If
                    tx.Commit()
                    Return id
                End Using
            End Using
        End Function

        ''' <summary>Whether you already have this receipt line (same series name and issue, ignoring "The" and punctuation).</summary>
        Public Function HasReceiptItem(item As ReceiptItem) As Boolean
            If item.Issue = "" Then Return False
            Using conn = Open()
                Dim rows = Query(conn, "SELECT series FROM v_collection WHERE issue = $p0 COLLATE NOCASE", item.Issue.Trim())
                Return rows.Rows.Cast(Of System.Data.DataRow)().Any(Function(row) ShopReceipts.Squash(Convert.ToString(row(0), CultureInfo.InvariantCulture)) = ShopReceipts.Squash(item.Series))
            End Using
        End Function

    End Class

End Namespace
