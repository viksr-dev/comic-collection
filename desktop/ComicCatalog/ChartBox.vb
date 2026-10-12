Option Strict On

Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Globalization
Imports System.Windows.Forms
Imports ComicCatalog.Data

''' <summary>A card on the Stats page: a bar chart (Items), or a line over time (Points).</summary>
Friend Class ChartBox
    Inherits Control

    Private _items As New List(Of ChartItem)
    Private _points As List(Of ValuePoint)

    ''' <summary>Bars measure value in dollars instead of how many comics.</summary>
    Public Property ByValue As Boolean
    ''' <summary>Shown when there's nothing to draw.</summary>
    Public Property EmptyText As String = "Nothing to show yet."

    Public Sub New(title As String)
        Text = title
        DoubleBuffered = True
        SetStyle(ControlStyles.ResizeRedraw Or ControlStyles.OptimizedDoubleBuffer Or ControlStyles.AllPaintingInWmPaint Or ControlStyles.UserPaint, True)
        BackColor = Theme.Panel
        ForeColor = Theme.Text
        Font = New Font("Segoe UI", 9.5F)
        Height = 330
        Margin = New Padding(0, 0, 12, 12)
        Dock = DockStyle.Fill
    End Sub

    Public Sub ShowBars(items As List(Of ChartItem))
        _items = If(items, New List(Of ChartItem))
        _points = Nothing
        Invalidate()
    End Sub

    Public Sub ShowLine(points As List(Of ValuePoint))
        _points = If(points, New List(Of ValuePoint))
        Invalidate()
    End Sub

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        MyBase.OnPaint(e)
        Dim g = e.Graphics
        g.SmoothingMode = SmoothingMode.AntiAlias
        g.TextRenderingHint = Drawing.Text.TextRenderingHint.ClearTypeGridFit
        Using titleFont As New Font("Segoe UI Semibold", 11.0F)
            TextRenderer.DrawText(g, Text, titleFont, New Point(14, 10), Theme.Text)
        End Using
        Dim area As New Rectangle(14, 44, Width - 28, Height - 58)
        If area.Width < 60 OrElse area.Height < 40 Then Return
        If _points IsNot Nothing Then
            DrawLine(g, area)
        Else
            DrawBars(g, area)
        End If
    End Sub

    Private Sub DrawEmpty(g As Graphics, area As Rectangle, message As String)
        TextRenderer.DrawText(g, message, Font, area, Theme.Muted,
                              TextFormatFlags.WordBreak Or TextFormatFlags.Left Or TextFormatFlags.Top)
    End Sub

    Private Function Amount(item As ChartItem) As Double
        Return If(ByValue, item.Value, item.Count)
    End Function

    Private Function AmountText(v As Double) As String
        Return If(ByValue, "$" & v.ToString("N0", CultureInfo.CurrentCulture), v.ToString("N0", CultureInfo.CurrentCulture))
    End Function

    Private Sub DrawBars(g As Graphics, area As Rectangle)
        Dim shown = _items.Where(Function(i) Amount(i) > 0).ToList()
        If shown.Count = 0 Then
            DrawEmpty(g, area, EmptyText)
            Return
        End If
        Dim rowH = Math.Min(28, area.Height \ shown.Count)
        If rowH < 14 Then
            shown = shown.Take(Math.Max(1, area.Height \ 14)).ToList()
            rowH = area.Height \ shown.Count
        End If
        Dim labelW = CInt(area.Width * 0.38)
        Dim valueW = 78
        Dim barMax = Math.Max(10, area.Width - labelW - valueW - 8)
        Dim most = shown.Max(Function(i) Amount(i))
        Using barBrush As New SolidBrush(Theme.Accent), trackBrush As New SolidBrush(Theme.Panel2)
            For n = 0 To shown.Count - 1
                Dim y = area.Top + n * rowH
                Dim item = shown(n)
                TextRenderer.DrawText(g, item.Label, Font, New Rectangle(area.Left, y, labelW - 6, rowH), Theme.Text,
                                      TextFormatFlags.VerticalCenter Or TextFormatFlags.EndEllipsis Or TextFormatFlags.SingleLine)
                Dim barH = Math.Max(6, rowH - 10)
                Dim barY = y + (rowH - barH) \ 2
                g.FillRectangle(trackBrush, area.Left + labelW, barY, barMax, barH)
                Dim w = CInt(Math.Max(2, barMax * Amount(item) / most))
                g.FillRectangle(barBrush, area.Left + labelW, barY, w, barH)
                TextRenderer.DrawText(g, AmountText(Amount(item)), Font, New Rectangle(area.Left + labelW + barMax + 6, y, valueW, rowH), Theme.Muted,
                                      TextFormatFlags.VerticalCenter Or TextFormatFlags.Left Or TextFormatFlags.SingleLine)
            Next
        End Using
    End Sub

    Private Sub DrawLine(g As Graphics, area As Rectangle)
        If _points.Count < 2 Then
            Dim now = If(_points.Count = 1, $"Today: {_points(0).Comics:N0} comics worth ${_points(0).TotalValue:N0}." & vbCrLf & vbCrLf, "")
            DrawEmpty(g, area, now & "The app notes your collection's value each day it's open, so this line grows from today. " &
                      "Come back in a few days to see it.")
            Return
        End If
        Dim legendH = 22
        Dim axisW = 70
        Dim plot As New Rectangle(area.Left + axisW, area.Top + legendH, area.Width - axisW - 8, area.Height - legendH - 24)
        If plot.Width < 40 OrElse plot.Height < 30 Then Return
        Dim first = _points(0).Day, last = _points(_points.Count - 1).Day
        Dim days = Math.Max(1, (last - first).TotalDays)
        Dim top = Math.Max(1, _points.Max(Function(p) Math.Max(p.TotalValue, p.TotalPaid))) * 1.1
        Dim xOf = Function(d As Date) CSng(plot.Left + plot.Width * (d - first).TotalDays / days)
        Dim yOf = Function(v As Double) CSng(plot.Bottom - plot.Height * v / top)

        ' Grid lines with dollar amounts
        Using gridPen As New Pen(Theme.Panel2)
            For i = 0 To 4
                Dim v = top * i / 4
                Dim y = yOf(v)
                g.DrawLine(gridPen, plot.Left, y, plot.Right, y)
                TextRenderer.DrawText(g, "$" & v.ToString("N0", CultureInfo.CurrentCulture), Font,
                                      New Rectangle(area.Left, CInt(y) - 10, axisW - 6, 20), Theme.Muted,
                                      TextFormatFlags.Right Or TextFormatFlags.VerticalCenter Or TextFormatFlags.SingleLine)
            Next
        End Using
        For Each d In {first, last}
            Dim flags = If(d = first, TextFormatFlags.Left, TextFormatFlags.Right)
            Dim box = If(d = first, New Rectangle(plot.Left, plot.Bottom + 4, 140, 20), New Rectangle(plot.Right - 140, plot.Bottom + 4, 140, 20))
            TextRenderer.DrawText(g, d.ToString("d MMM yyyy", CultureInfo.CurrentCulture), Font, box, Theme.Muted, flags Or TextFormatFlags.SingleLine)
        Next

        Dim valueLine = _points.Select(Function(p) New PointF(xOf(p.Day), yOf(p.TotalValue))).ToArray()
        Dim paidLine = _points.Select(Function(p) New PointF(xOf(p.Day), yOf(p.TotalPaid))).ToArray()
        ' Soft fill under the value line
        Using fill As New SolidBrush(Color.FromArgb(40, Theme.Accent)), path As New GraphicsPath()
            path.AddLines(valueLine)
            path.AddLine(valueLine.Last(), New PointF(valueLine.Last().X, plot.Bottom))
            path.AddLine(New PointF(valueLine.Last().X, plot.Bottom), New PointF(valueLine(0).X, plot.Bottom))
            path.CloseFigure()
            g.FillPath(fill, path)
        End Using
        Using paidPen As New Pen(Theme.Muted, 1.6F) With {.DashStyle = DashStyle.Dash}, valuePen As New Pen(Theme.Accent, 2.6F)
            g.DrawLines(paidPen, paidLine)
            g.DrawLines(valuePen, valueLine)
            ' Legend
            g.DrawLine(valuePen, plot.Left, area.Top + 9, plot.Left + 22, area.Top + 9)
            TextRenderer.DrawText(g, $"Value (now ${_points.Last().TotalValue:N0})", Font, New Point(plot.Left + 28, area.Top), Theme.Text)
            Dim paidX = plot.Left + 230
            g.DrawLine(paidPen, paidX, area.Top + 9, paidX + 22, area.Top + 9)
            TextRenderer.DrawText(g, $"What you paid (${_points.Last().TotalPaid:N0})", Font, New Point(paidX + 28, area.Top), Theme.Muted)
        End Using
    End Sub

End Class
