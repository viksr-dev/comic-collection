Option Strict On

Imports System.Data
Imports System.Diagnostics
Imports System.Drawing
Imports System.Globalization
Imports System.Windows.Forms
Imports ComicCatalog.Data

''' <summary>eBay listings of wishlist comics at or under the most you'd pay.</summary>
Public Class DealsForm
    Inherits Form

    Private ReadOnly _db As ComicDb
    Private ReadOnly _settings As AppSettings
    Private ReadOnly _grid As DataGridView = Ui.MakeGrid()
    Private ReadOnly _note As New Label With {.Dock = DockStyle.Top, .AutoSize = False, .Height = 46, .Padding = New Padding(12, 8, 12, 0), .Tag = Theme.MutedTag}

    ''' <summary>True if any deal was dismissed, so the wishlist needs refreshing.</summary>
    Public Property Changed As Boolean

    Public Sub New(db As ComicDb, settings As AppSettings)
        _db = db
        _settings = settings
        Text = "eBay deals on your wishlist"
        StartPosition = FormStartPosition.CenterParent
        MinimizeBox = False
        ShowInTaskbar = False
        AutoScaleMode = AutoScaleMode.Font
        Font = New Font("Segoe UI", 9.5F)
        ClientSize = New Size(900, 460)
        MinimumSize = New Size(600, 300)

        Dim open = Ui.MakeButton("Open on eBay", AddressOf OnOpen, Theme.PrimaryTag)
        Dim no = Ui.MakeButton("Not interested", AddressOf OnDismiss)
        Dim close = Ui.MakeButton("Close", Sub(s, e) Me.Close())
        CancelButton = close
        Dim buttons As New FlowLayoutPanel With {.Dock = DockStyle.Bottom, .AutoSize = True, .Padding = New Padding(8)}
        buttons.Controls.AddRange({open, no, close})
        Dim body As New Panel With {.Dock = DockStyle.Fill, .Padding = New Padding(12, 4, 12, 4)}
        body.Controls.Add(_grid)
        AddHandler _grid.CellDoubleClick, Sub(s, e)
                                              If e.RowIndex >= 0 Then OnOpen(s, e)
                                          End Sub
        Controls.Add(body)
        Controls.Add(_note)
        Controls.Add(buttons)
        Theme.Apply(Me)
        ShowDeals()
    End Sub

    Protected Overrides Sub OnHandleCreated(e As EventArgs)
        MyBase.OnHandleCreated(e)
        Theme.DarkTitleBar(Me)
    End Sub

    Private Sub ShowDeals()
        Dim deals = _db.OpenDeals()
        Dim rate = If(_settings.LocalPerUsd > 0, _settings.LocalPerUsd, 1)
        If rate <> 1 Then
            deals.Columns.Add("In your $", GetType(Double))
            For Each r As DataRow In deals.Rows
                r("In your $") = Math.Round(Convert.ToDouble(r("Price"), CultureInfo.InvariantCulture) * rate, 2)
            Next
            deals.Columns("In your $").SetOrdinal(deals.Columns("Price").Ordinal + 1)
        End If
        deals.Columns("Price").ColumnName = "Price (US$)"
        _grid.DataSource = deals
        For Each colName In {"id", "url"}
            If _grid.Columns.Contains(colName) Then _grid.Columns(colName).Visible = False
        Next
        For Each colName In {"Price (US$)", "In your $", "Max price"}
            If _grid.Columns.Contains(colName) Then
                _grid.Columns(colName).DefaultCellStyle.Format = "N2"
                _grid.Columns(colName).FillWeight = 60
            End If
        Next
        If _grid.Columns.Contains("Listing") Then _grid.Columns("Listing").FillWeight = 260
        _note.Text = If(deals.Rows.Count = 0,
            "No deals right now. The app looks on eBay once a day for wishlist comics that have a most-you'd-pay.",
            "Buy It Now listings at or under the most you'd pay (before postage). Check the photos and the seller before buying. " &
            "Listings that sell or end disappear by themselves.")
    End Sub

    Private Function SelectedRows() As List(Of DataRowView)
        Return _grid.SelectedRows.Cast(Of DataGridViewRow)().Select(Function(r) TryCast(r.DataBoundItem, DataRowView)).Where(Function(r) r IsNot Nothing).ToList()
    End Function

    Private Sub OnOpen(sender As Object, e As EventArgs)
        For Each r In SelectedRows().Take(5)
            Dim url = Convert.ToString(r("url"), CultureInfo.InvariantCulture)
            If url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) Then Process.Start(New ProcessStartInfo(url) With {.UseShellExecute = True})
        Next
    End Sub

    Private Sub OnDismiss(sender As Object, e As EventArgs)
        Dim ids = SelectedRows().Select(Function(r) Convert.ToInt64(r("id"), CultureInfo.InvariantCulture)).ToList()
        If ids.Count = 0 Then Return
        _db.DismissDeals(ids)
        Changed = True
        ShowDeals()
    End Sub

End Class
