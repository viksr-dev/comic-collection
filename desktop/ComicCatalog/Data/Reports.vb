Option Strict On

Imports System.Data
Imports System.Globalization
Imports System.IO
Imports System.Text
Imports QuestPDF.Fluent
Imports QuestPDF.Helpers
Imports QuestPDF.Infrastructure

Namespace Data

    ''' <summary>What goes in the insurance report.</summary>
    Public Class InsuranceOptions
        ''' <summary>Leave out comics worth less than this in total (0 for everything).</summary>
        Public Property MinValue As Double
        ''' <summary>Name on the front page, e.g. "James Atkinson". Optional.</summary>
        Public Property Owner As String = ""
        Public Property IncludeCovers As Boolean = True
        Public Property IncludePhotos As Boolean = True
        ''' <summary>Where downloaded covers are kept ("&lt;comic id&gt;.img").</summary>
        Public Property CoversFolder As String = ""
    End Class

    ''' <summary>The insurance report (a PDF with covers, your photos, grades and values) and the shop pull list.</summary>
    Public Module Reports

        ''' <summary>Makes the insurance report. Returns how many comics are in it.</summary>
        Public Function InsurancePdf(db As ComicDb, filePath As String, options As InsuranceOptions, Optional madeOn As Date? = Nothing) As Integer
            QuestPDF.Settings.License = LicenseType.Community
            Dim rows = db.InsuranceRows(options.MinValue)
            Dim made = If(madeOn, Date.Today).ToString("d MMMM yyyy", CultureInfo.InvariantCulture)
            Dim total = rows.Rows.Cast(Of DataRow)().Sum(Function(r) Money(r("total_value")))
            Dim paid = rows.Rows.Cast(Of DataRow)().Sum(Function(r) Money(r("price_paid")))
            Dim copies = rows.Rows.Cast(Of DataRow)().Sum(Function(r) Convert.ToInt32(r("quantity"), CultureInfo.InvariantCulture))
            Dim graded = rows.Rows.Cast(Of DataRow)().Count(Function(r) Txt(r, "graded_by") <> "")
            Dim keys = rows.Rows.Cast(Of DataRow)().Count(Function(r) Txt(r, "key_note") <> "")
            Dim withPhotos = 0

            ' Pictures are read now, so a damaged file is skipped instead of stopping the whole report.
            Dim pictures As New Dictionary(Of Long, List(Of (Image As Image, Caption As String)))
            For Each r As DataRow In rows.Rows
                Dim id = Convert.ToInt64(r("comic_id"), CultureInfo.InvariantCulture)
                Dim list As New List(Of (Image As Image, Caption As String))
                If options.IncludeCovers AndAlso options.CoversFolder <> "" Then
                    Dim cover = LoadImage(Path.Combine(options.CoversFolder, $"{id}.img"))
                    If cover IsNot Nothing Then list.Add((cover, "Cover"))
                End If
                If options.IncludePhotos Then
                    Dim photos = db.GetPhotos(id)
                    If photos.Count > 0 Then withPhotos += 1
                    For Each p In photos.Take(4)
                        Dim img = LoadImage(Path.Combine(db.PhotosFolder, p.FileName))
                        If img IsNot Nothing Then list.Add((img, If(p.Caption <> "", p.Caption, "Photo")))
                    Next
                End If
                pictures(id) = list
            Next

            Document.Create(
                Sub(doc)
                    doc.Page(
                        Sub(page)
                            page.Size(PageSizes.A4)
                            page.Margin(1.5F, Unit.Centimetre)
                            page.DefaultTextStyle(Function(t) t.FontSize(9))
                            page.Header().PaddingBottom(10).Column(
                                Sub(col)
                                    col.Item().Text("Comic collection: insurance record").FontSize(18).Bold()
                                    If options.Owner.Trim() <> "" Then col.Item().Text($"Owner: {options.Owner.Trim()}").FontSize(10)
                                    col.Item().Text($"Made {made}").FontSize(10)
                                End Sub)
                            page.Content().Column(
                                Sub(col)
                                    col.Spacing(6)
                                    ' Summary first, for whoever reads it
                                    col.Item().Background(Colors.Grey.Lighten4).Padding(10).Column(
                                        Sub(sum)
                                            sum.Item().Text($"{rows.Rows.Count:N0} comics ({copies:N0} copies)" &
                                                            If(options.MinValue > 0, $", each worth at least ${options.MinValue:N2}", "")).Bold()
                                            sum.Item().Text($"Total value: ${total:N2}    Total paid: ${paid:N2}")
                                            sum.Item().Text($"Professionally graded: {graded:N0}    Key issues: {keys:N0}    With photos: {withPhotos:N0}")
                                            sum.Item().PaddingTop(4).Text("Values are the owner's own estimates, from recent sales where known.").FontSize(8).Italic()
                                        End Sub)
                                    For Each r As DataRow In rows.Rows
                                        Dim id = Convert.ToInt64(r("comic_id"), CultureInfo.InvariantCulture)
                                        col.Item().ShowEntire().BorderBottom(0.5F).BorderColor(Colors.Grey.Lighten2).PaddingVertical(6).Row(
                                            Sub(row)
                                                row.Spacing(8)
                                                row.RelativeItem(3).Column(Sub(c) Details(c, r))
                                                For Each pic In pictures(id)
                                                    Dim p = pic
                                                    row.ConstantItem(62).Column(
                                                        Sub(c)
                                                            c.Item().Height(90).AlignCenter().Image(p.Image).FitArea()
                                                            c.Item().AlignCenter().Text(p.Caption).FontSize(7).FontColor(Colors.Grey.Darken1)
                                                        End Sub)
                                                Next
                                            End Sub)
                                    Next
                                End Sub)
                            page.Footer().AlignCenter().Text(
                                Sub(t)
                                    t.Span("Page ")
                                    t.CurrentPageNumber()
                                    t.Span(" of ")
                                    t.TotalPages()
                                End Sub)
                        End Sub)
                End Sub).WithSettings(New DocumentSettings With {.ImageRasterDpi = 150, .ImageCompressionQuality = ImageCompressionQuality.Medium}).
                GeneratePdf(filePath)
            Return rows.Rows.Count
        End Function

        Private Sub Details(c As ColumnDescriptor, r As DataRow)
            Dim name = Txt(r, "comic")
            If Txt(r, "variant_name") <> "" Then name &= $" ({Txt(r, "variant_name")})"
            c.Item().Text(name).Bold().FontSize(10)
            If Txt(r, "key_note") <> "" Then c.Item().Text($"Key issue: {Txt(r, "key_note")}").FontColor(Colors.Orange.Darken3)
            Dim lines As New List(Of String)
            Dim about = String.Join(" · ", {Txt(r, "title"), Txt(r, "publisher"), Txt(r, "cover_date")}.Where(Function(s) s <> ""))
            If about <> "" Then lines.Add(about)
            If Txt(r, "graded_by") <> "" Then
                lines.Add($"Graded {Txt(r, "graded_by")} {Txt(r, "grade")}" & If(Txt(r, "grade_label") <> "", $", {Txt(r, "grade_label")}", "") &
                          If(Txt(r, "cert_number") <> "", $", certificate {Txt(r, "cert_number")}", ""))
            ElseIf Txt(r, "condition") <> "" Then
                lines.Add($"Condition: {Txt(r, "condition")}")
            End If
            Dim qty = Convert.ToInt32(r("quantity"), CultureInfo.InvariantCulture)
            Dim prices As New List(Of String)
            If qty > 1 Then prices.Add($"{qty} copies")
            If Not IsDBNull(r("current_value")) Then prices.Add($"Value ${Money(r("current_value")):N2}{If(qty > 1, " each", "")}")
            If Not IsDBNull(r("price_paid")) Then prices.Add($"Paid ${Money(r("price_paid")):N2}")
            If Txt(r, "purchase_date") <> "" Then prices.Add($"Bought {Txt(r, "purchase_date")}")
            If prices.Count > 0 Then lines.Add(String.Join(" · ", prices))
            Dim where As New List(Of String)
            If Txt(r, "location") <> "" Then where.Add($"Kept in {Txt(r, "location")}")
            If Txt(r, "barcode") <> "" Then where.Add($"Barcode {Txt(r, "barcode")}")
            If where.Count > 0 Then lines.Add(String.Join(" · ", where))
            For Each line In lines
                c.Item().Text(line)
            Next
        End Sub

        ' A picture QuestPDF can use, or Nothing if the file is missing or isn't a picture.
        Private Function LoadImage(file As String) As Image
            Try
                If Not IO.File.Exists(file) Then Return Nothing
                Return Image.FromBinaryData(IO.File.ReadAllBytes(file))
            Catch ex As Exception
                ' QuestPDF says "not a supported image" in different ways; any of them means skip it.
                Return Nothing
            End Try
        End Function

        ' ---------- pull list for the comic shop ----------

        ''' <summary>The pull list as plain text, ready to paste into an email or message.</summary>
        Public Function PullListText(rows As DataTable, Optional who As String = "") As String
            Dim text As New StringBuilder()
            text.AppendLine("Hi! Could you please put these aside for me:")
            text.AppendLine()
            Dim lastDay = ""
            For Each r As DataRow In rows.Rows
                Dim day = Txt(r, "In shops")
                If day <> lastDay Then
                    Dim d As Date
                    Dim heading = If(Date.TryParseExact(day, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, d),
                                     "Out " & d.ToString("ddd d MMM", CultureInfo.InvariantCulture), "Date not known")
                    If lastDay <> "" Then text.AppendLine()
                    text.AppendLine(heading)
                    lastDay = day
                End If
                Dim name = Txt(r, "Series") & If(Txt(r, "Volume") <> "", $" ({Txt(r, "Volume")})", "") & $" #{Txt(r, "Issue")}"
                text.AppendLine("- " & name & If(Txt(r, "Story title") <> "", $": {Txt(r, "Story title")}", ""))
            Next
            text.AppendLine()
            text.AppendLine("Thanks" & If(who.Trim() <> "", ", " & who.Trim(), "!"))
            Return text.ToString()
        End Function

        ''' <summary>The pull list as a one-page PDF to print.</summary>
        Public Sub PullListPdf(rows As DataTable, filePath As String, title As String)
            QuestPDF.Settings.License = LicenseType.Community
            Document.Create(
                Sub(doc)
                    doc.Page(
                        Sub(page)
                            page.Size(PageSizes.A4)
                            page.Margin(2, Unit.Centimetre)
                            page.DefaultTextStyle(Function(t) t.FontSize(11))
                            page.Header().PaddingBottom(10).Text(title).FontSize(18).Bold()
                            page.Content().Table(
                                Sub(table)
                                    table.ColumnsDefinition(
                                        Sub(defs)
                                            defs.ConstantColumn(24)
                                            defs.RelativeColumn(1.2F)
                                            defs.RelativeColumn(3)
                                            defs.RelativeColumn(0.7F)
                                            defs.RelativeColumn(3)
                                        End Sub)
                                    table.Header(
                                        Sub(head)
                                            For Each h In {"", "In shops", "Series", "Issue", "Story"}
                                                head.Cell().BorderBottom(1).PaddingVertical(4).Text(h).Bold()
                                            Next
                                        End Sub)
                                    For Each r As DataRow In rows.Rows
                                        Dim series = Txt(r, "Series") & If(Txt(r, "Volume") <> "", $" ({Txt(r, "Volume")})", "")
                                        ' A box to tick at the shop
                                        table.Cell().PaddingVertical(4).Width(12).Height(12).Border(1)
                                        For Each v In {Txt(r, "In shops"), series, "#" & Txt(r, "Issue"), Txt(r, "Story title")}
                                            table.Cell().BorderBottom(0.5F).BorderColor(Colors.Grey.Lighten2).PaddingVertical(4).Text(v)
                                        Next
                                    Next
                                End Sub)
                        End Sub)
                End Sub).GeneratePdf(filePath)
        End Sub

        ''' <summary>
        ''' Labels for the ends of your boxes: the box's name in big letters, how many comics, and what's in it.
        ''' Two to a page (cut along the line). Very full boxes list their first 30 series.
        ''' </summary>
        Public Sub BoxLabelsPdf(boxes As IEnumerable(Of BoxContents), filePath As String)
            QuestPDF.Settings.License = LicenseType.Community
            Const MaxLines = 30
            Document.Create(
                Sub(doc)
                    doc.Page(
                        Sub(page)
                            page.Size(PageSizes.A4)
                            page.Margin(1.5F, Unit.Centimetre)
                            page.DefaultTextStyle(Function(t) t.FontSize(10))
                            page.Content().Column(
                                Sub(col)
                                    col.Spacing(18)
                                    For Each box In boxes
                                        col.Item().ShowEntire().Border(2).Padding(14).Column(
                                            Sub(label)
                                                label.Item().Text(box.Name).FontSize(40).Bold()
                                                label.Item().PaddingBottom(8).Text($"{box.Count:N0} comic{If(box.Count = 1, "", "s")}").FontSize(13).FontColor(Colors.Grey.Darken1)
                                                For Each line In box.Lines.Take(MaxLines)
                                                    label.Item().Text(line).ClampLines(1)
                                                Next
                                                If box.Lines.Count > MaxLines Then
                                                    label.Item().Text($"…and {box.Lines.Count - MaxLines} more series").Italic()
                                                End If
                                            End Sub)
                                    Next
                                End Sub)
                        End Sub)
                End Sub).GeneratePdf(filePath)
        End Sub

        ''' <summary>The signing list as plain text, grouped by guest, ready to copy.</summary>
        Public Function SigningListText(rows As DataTable) As String
            Dim sb As New StringBuilder()
            For Each guest In rows.Rows.Cast(Of DataRow)().GroupBy(Function(r) Txt(r, "Guest"))
                If sb.Length > 0 Then sb.AppendLine()
                sb.AppendLine($"{guest.Key} ({guest.Count()})")
                For Each r In guest
                    Dim extra = {Txt(r, "Box"), Txt(r, "Key issue")}.Where(Function(x) x <> "").ToList()
                    sb.AppendLine($"- {Txt(r, "Comic")}" & If(extra.Count > 0, $" ({String.Join(", ", extra)})", ""))
                Next
            Next
            Return sb.ToString()
        End Function

        ''' <summary>The signing list as a PDF with a tick box per comic, grouped by guest.</summary>
        Public Sub SigningListPdf(rows As DataTable, filePath As String, title As String)
            QuestPDF.Settings.License = LicenseType.Community
            Document.Create(
                Sub(doc)
                    doc.Page(
                        Sub(page)
                            page.Size(PageSizes.A4)
                            page.Margin(2, Unit.Centimetre)
                            page.DefaultTextStyle(Function(t) t.FontSize(11))
                            page.Header().PaddingBottom(10).Text(title).FontSize(18).Bold()
                            page.Content().Column(
                                Sub(col)
                                    For Each guest In rows.Rows.Cast(Of DataRow)().GroupBy(Function(r) Txt(r, "Guest"))
                                        col.Item().PaddingTop(10).PaddingBottom(4).Text($"{guest.Key} ({guest.Count()})").FontSize(14).Bold()
                                        For Each r In guest
                                            col.Item().BorderBottom(0.5F).BorderColor(Colors.Grey.Lighten2).PaddingVertical(4).Row(
                                                Sub(row)
                                                    row.ConstantItem(20).AlignMiddle().Width(12).Height(12).Border(1)
                                                    row.RelativeItem(3).Text(Txt(r, "Comic") & If(Txt(r, "Key issue") <> "", " - key issue: " & Txt(r, "Key issue"), ""))
                                                    row.RelativeItem(1).Text(Txt(r, "Box")).FontColor(Colors.Grey.Darken1)
                                                End Sub)
                                        Next
                                    Next
                                End Sub)
                        End Sub)
                End Sub).GeneratePdf(filePath)
        End Sub

        Private Function Txt(r As DataRow, col As String) As String
            If Not r.Table.Columns.Contains(col) OrElse IsDBNull(r(col)) Then Return ""
            Return Convert.ToString(r(col), CultureInfo.InvariantCulture).Trim()
        End Function

        Private Function Money(v As Object) As Double
            If v Is DBNull.Value OrElse v Is Nothing Then Return 0
            Return Convert.ToDouble(v, CultureInfo.InvariantCulture)
        End Function

    End Module

End Namespace
