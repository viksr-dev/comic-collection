Option Strict On

Imports System.Data
Imports System.Globalization
Imports ClosedXML.Excel
Imports QuestPDF.Fluent
Imports QuestPDF.Helpers
Imports QuestPDF.Infrastructure

Namespace Data

    ''' <summary>The whole collection as an Excel workbook or a PDF, e.g. for insurance.</summary>
    Public Module Exporter

        Private ReadOnly MoneyColumns As String() = {"Cover price", "Paid", "Value each", "Total value"}

        Public Sub ToExcel(db As ComicDb, filePath As String)
            Dim rows = db.ExportList()
            Dim stats = db.GetStats()
            Using book As New XLWorkbook()
                Dim sheet = book.Worksheets.Add("Collection")
                For c = 0 To rows.Columns.Count - 1
                    sheet.Cell(1, c + 1).Value = rows.Columns(c).ColumnName
                Next
                For r = 0 To rows.Rows.Count - 1
                    For c = 0 To rows.Columns.Count - 1
                        Dim v = rows.Rows(r)(c)
                        If v Is DBNull.Value OrElse v Is Nothing Then Continue For
                        Dim cell = sheet.Cell(r + 2, c + 1)
                        If TypeOf v Is Long OrElse TypeOf v Is Double OrElse TypeOf v Is Integer Then
                            cell.Value = Convert.ToDouble(v, CultureInfo.InvariantCulture)
                        Else
                            ' Kept as text, so barcodes and issue numbers like "007" aren't changed.
                            cell.Value = Convert.ToString(v, CultureInfo.InvariantCulture)
                        End If
                    Next
                Next
                Dim last = rows.Rows.Count + 1
                Dim header = sheet.Range(1, 1, 1, rows.Columns.Count)
                header.Style.Font.Bold = True
                header.Style.Fill.BackgroundColor = XLColor.FromArgb(230, 232, 238)
                If rows.Rows.Count > 0 Then sheet.Range(1, 1, last, rows.Columns.Count).SetAutoFilter()
                sheet.SheetView.FreezeRows(1)
                For Each name In MoneyColumns
                    Dim col = rows.Columns.IndexOf(name) + 1
                    sheet.Column(col).Style.NumberFormat.Format = "#,##0.00"
                Next

                ' Totals under the list
                Dim totalRow = last + 2
                sheet.Cell(totalRow, 1).Value = "Total"
                sheet.Cell(totalRow, 1).Style.Font.Bold = True
                For Each name In {"Copies", "Paid", "Total value"}
                    Dim col = rows.Columns.IndexOf(name) + 1
                    Dim letter = sheet.Column(col).ColumnLetter()
                    sheet.Cell(totalRow, col).FormulaA1 = $"SUM({letter}2:{letter}{last})"
                    sheet.Cell(totalRow, col).Style.Font.Bold = True
                Next
                sheet.Columns().AdjustToContents(1, Math.Min(last, 400), 8.0, 60.0)

                ' A summary sheet first, for whoever reads it
                Dim summary = book.Worksheets.Add("Summary", 1)
                summary.Cell(1, 1).Value = "Comic collection"
                summary.Cell(1, 1).Style.Font.Bold = True
                summary.Cell(1, 1).Style.Font.FontSize = 16
                Dim lines As (String, XLCellValue)() = {
                    ("Made on", DateTime.Today.ToString("d MMMM yyyy", CultureInfo.CurrentCulture)),
                    ("Comics", stats.Comics), ("Copies", stats.Copies),
                    ("Total value", stats.TotalValue), ("Total paid", stats.TotalPaid)}
                For i = 0 To lines.Length - 1
                    summary.Cell(i + 3, 1).Value = lines(i).Item1
                    summary.Cell(i + 3, 2).Value = lines(i).Item2
                Next
                summary.Range(6, 2, 7, 2).Style.NumberFormat.Format = "$#,##0.00"

                summary.Cell(10, 1).Value = "Publisher"
                summary.Cell(10, 2).Value = "Comics"
                summary.Cell(10, 3).Value = "Value"
                summary.Range(10, 1, 10, 3).Style.Font.Bold = True
                Dim byPublisher = rows.Rows.Cast(Of DataRow)().
                    GroupBy(Function(r) If(r("Publisher") Is DBNull.Value OrElse CStr(r("Publisher")) = "", "(not known)", CStr(r("Publisher")))).
                    Select(Function(g) (Name:=g.Key, Count:=g.Count(), Value:=g.Sum(Function(r) Money(r("Total value"))))).
                    OrderByDescending(Function(g) g.Value).ThenBy(Function(g) g.Name).ToList()
                For i = 0 To byPublisher.Count - 1
                    summary.Cell(11 + i, 1).Value = byPublisher(i).Name
                    summary.Cell(11 + i, 2).Value = byPublisher(i).Count
                    summary.Cell(11 + i, 3).Value = byPublisher(i).Value
                    summary.Cell(11 + i, 3).Style.NumberFormat.Format = "$#,##0.00"
                Next
                summary.Column(1).Width = 30
                summary.Column(2).Width = 18
                summary.Column(3).Width = 16
                summary.SetTabActive()
                book.SaveAs(filePath)
            End Using
        End Sub

        Public Sub ToPdf(db As ComicDb, filePath As String)
            QuestPDF.Settings.License = LicenseType.Community
            Dim rows = db.ExportList()
            Dim stats = db.GetStats()
            Dim made = DateTime.Today.ToString("d MMMM yyyy", CultureInfo.CurrentCulture)
            Dim columns As (Name As String, Width As Single, Right As Boolean)() = {
                ("Series", 3.2F, False), ("Issue", 0.7F, False), ("Cover / variant", 2.2F, False), ("Publisher", 1.4F, False),
                ("Cover date", 0.9F, False), ("Condition", 1.1F, False), ("Cert number", 1.1F, False),
                ("Copies", 0.6F, True), ("Paid", 0.9F, True), ("Total value", 1.0F, True)}

            Document.Create(
                Sub(doc)
                    doc.Page(
                        Sub(page)
                            page.Size(PageSizes.A4.Landscape())
                            page.Margin(1.2F, Unit.Centimetre)
                            page.DefaultTextStyle(Function(t) t.FontSize(8))
                            page.Header().PaddingBottom(8).Column(
                                Sub(col)
                                    col.Item().Text("Comic collection").FontSize(18).Bold()
                                    col.Item().Text($"Made {made}  ·  {stats.Comics:N0} comics ({stats.Copies:N0} copies)  ·  " &
                                                    $"Total value ${stats.TotalValue:N2}  ·  Total paid ${stats.TotalPaid:N2}").FontSize(10)
                                End Sub)
                            page.Content().Table(
                                Sub(table)
                                    table.ColumnsDefinition(
                                        Sub(defs)
                                            For Each c In columns
                                                defs.RelativeColumn(c.Width)
                                            Next
                                        End Sub)
                                    table.Header(
                                        Sub(head)
                                            For Each c In columns
                                                Dim cell = head.Cell().BorderBottom(1).PaddingVertical(3).PaddingHorizontal(2)
                                                If c.Right Then cell = cell.AlignRight()
                                                cell.Text(c.Name).Bold()
                                            Next
                                        End Sub)
                                    For Each r As DataRow In rows.Rows
                                        For Each c In columns
                                            Dim cell = table.Cell().BorderBottom(0.5F).BorderColor(Colors.Grey.Lighten2).PaddingVertical(2).PaddingHorizontal(2)
                                            If c.Right Then cell = cell.AlignRight()
                                            cell.Text(CellText(r, c.Name))
                                        Next
                                    Next
                                    ' Totals row
                                    For Each c In columns
                                        Dim cell = table.Cell().BorderTop(1).PaddingVertical(3).PaddingHorizontal(2)
                                        If c.Right Then cell = cell.AlignRight()
                                        cell.Text(TotalText(c.Name, stats)).Bold()
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
                End Sub).GeneratePdf(filePath)
        End Sub

        Private Function TotalText(name As String, stats As CollectionStats) As String
            Select Case name
                Case "Series" : Return "Total"
                Case "Copies" : Return stats.Copies.ToString("N0", CultureInfo.CurrentCulture)
                Case "Paid" : Return stats.TotalPaid.ToString("N2", CultureInfo.CurrentCulture)
                Case "Total value" : Return stats.TotalValue.ToString("N2", CultureInfo.CurrentCulture)
                Case Else : Return ""
            End Select
        End Function

        Private Function CellText(r As DataRow, name As String) As String
            Dim v = r(name)
            If v Is DBNull.Value OrElse v Is Nothing Then Return ""
            Select Case name
                Case "Series"
                    Dim vol = Convert.ToString(r("Volume"), CultureInfo.InvariantCulture)
                    Return CStr(v) & If(vol <> "", $" ({vol})", "")
                Case "Paid", "Total value", "Value each", "Cover price"
                    Return Money(v).ToString("N2", CultureInfo.CurrentCulture)
                Case Else
                    Return Convert.ToString(v, CultureInfo.InvariantCulture)
            End Select
        End Function

        Private Function Money(v As Object) As Double
            If v Is DBNull.Value OrElse v Is Nothing Then Return 0
            Return Convert.ToDouble(v, CultureInfo.InvariantCulture)
        End Function

    End Module

End Namespace
