Option Strict On

Imports System.Data
Imports System.Diagnostics
Imports System.Drawing
Imports System.Globalization
Imports System.Windows.Forms
Imports ComicCatalog.Data

''' <summary>"Worth getting graded?": raw comics where a CGC 9.8 copy sells for more than the raw price plus grading.</summary>
Public Class GradingForm
    Inherits Form

    Private ReadOnly _db As ComicDb
    Private ReadOnly _settings As AppSettings
    Private ReadOnly _grid As DataGridView = Ui.MakeGrid()
    Private ReadOnly _cost As New NumericUpDown With {.Maximum = 1000, .Width = 80, .Increment = 5}
    Private ReadOnly _onlyGains As New CheckBox With {.Text = "Only ones that would pay off", .AutoSize = True, .Checked = True, .Margin = New Padding(16, 8, 3, 3)}
    Private ReadOnly _status As New Label With {.AutoSize = True, .Tag = Theme.MutedTag, .Margin = New Padding(16, 8, 3, 3)}

    Public Sub New(db As ComicDb, settings As AppSettings)
        _db = db
        _settings = settings
        Text = "Worth getting graded?"
        StartPosition = FormStartPosition.CenterParent
        MinimizeBox = False
        ShowInTaskbar = False
        AutoScaleMode = AutoScaleMode.Font
        Font = New Font("Segoe UI", 9.5F)
        ClientSize = New Size(820, 480)
        MinimumSize = New Size(600, 320)
        _cost.Value = CDec(Math.Min(1000, Math.Max(0, settings.GradingCost)))
        AddHandler _cost.ValueChanged, Sub(s, e)
                                           _settings.GradingCost = CDbl(_cost.Value)
                                           Try
                                               _settings.Save()
                                           Catch ex As IO.IOException
                                           End Try
                                           ShowList()
                                       End Sub
        AddHandler _onlyGains.CheckedChanged, Sub(s, e) ShowList()

        Dim help As New Label With {.Dock = DockStyle.Top, .AutoSize = False, .Height = 62, .Padding = New Padding(12, 8, 12, 0), .Tag = Theme.MutedTag,
            .Text = "Compares eBay asking prices (US$) for your raw copy with a CGC 9.8 copy, less what grading costs. A 9.8 needs a near-perfect " &
                    "copy: sharp corners, no spine ticks, flat and clean. Anything less sells for a lot less, so only send ones in great shape."}
        Dim bar As New FlowLayoutPanel With {.Dock = DockStyle.Top, .AutoSize = True, .Padding = New Padding(8, 4, 8, 4)}
        bar.Controls.AddRange({Ui.MakeLabel("Grading costs (US$, with postage)"), _cost, _onlyGains, _status})
        Dim body As New Panel With {.Dock = DockStyle.Fill, .Padding = New Padding(12, 4, 12, 4)}
        body.Controls.Add(_grid)
        Dim sold = Ui.MakeButton("See CGC 9.8 sales on eBay", AddressOf OnSold, Theme.PrimaryTag)
        Dim close = Ui.MakeButton("Close", Sub(s, e) Me.Close())
        CancelButton = close
        Dim buttons As New FlowLayoutPanel With {.Dock = DockStyle.Bottom, .AutoSize = True, .Padding = New Padding(8)}
        buttons.Controls.AddRange({sold, close})
        Controls.Add(body)
        Controls.Add(bar)
        Controls.Add(help)
        Controls.Add(buttons)
        Theme.Apply(Me)
        ShowList()
    End Sub

    Protected Overrides Sub OnHandleCreated(e As EventArgs)
        MyBase.OnHandleCreated(e)
        Theme.DarkTitleBar(Me)
    End Sub

    Private Sub ShowList()
        Dim rows = _db.WorthGrading(CDbl(_cost.Value))
        Dim all = rows.Rows.Count
        If _onlyGains.Checked Then
            For Each r In rows.Rows.Cast(Of DataRow)().Where(Function(x) Convert.ToDouble(x("Gain"), CultureInfo.InvariantCulture) <= 0).ToList()
                rows.Rows.Remove(r)
            Next
        End If
        _grid.DataSource = rows
        If _grid.Columns.Contains("comic_id") Then _grid.Columns("comic_id").Visible = False
        For Each colName In {"Raw", "CGC 9.8", "Gain"}
            If _grid.Columns.Contains(colName) Then
                _grid.Columns(colName).DefaultCellStyle.Format = "N2"
                _grid.Columns(colName).DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight
                _grid.Columns(colName).FillWeight = 60
            End If
        Next
        Dim waiting = _db.ComicsNeedingGradedCheck().Count
        _status.Text = If(all = 0 AndAlso waiting = 0,
                          "Needs eBay estimates first (Estimate values from eBay).",
                          $"{rows.Rows.Count:N0} shown of {all:N0} compared." & If(waiting > 0, $" {waiting:N0} still to check (runs in the background).", ""))
    End Sub

    ' eBay's sold listings show what slabs really went for, which beats asking prices.
    Private Sub OnSold(sender As Object, e As EventArgs)
        If _grid.CurrentRow Is Nothing Then Return
        Dim comic = Convert.ToString(_grid.CurrentRow.Cells("Comic").Value, CultureInfo.InvariantCulture).Replace("#", "")
        Process.Start(New ProcessStartInfo("https://www.ebay.com/sch/i.html?LH_Sold=1&LH_Complete=1&_nkw=" & Uri.EscapeDataString(comic & " CGC 9.8")) With {.UseShellExecute = True})
    End Sub

End Class
