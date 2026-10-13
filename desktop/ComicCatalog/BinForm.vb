Option Strict On

Imports System.Data
Imports System.Drawing
Imports System.Globalization
Imports System.Windows.Forms
Imports ComicCatalog.Data

''' <summary>Comics you deleted in the last 30 days, to put back or get rid of for good.</summary>
Public Class BinForm
    Inherits Form

    Private ReadOnly _db As ComicDb
    Private ReadOnly _grid As DataGridView = Ui.MakeGrid()
    Private ReadOnly _note As New Label With {.Dock = DockStyle.Top, .AutoSize = False, .Height = 46, .Padding = New Padding(12, 8, 12, 0), .Tag = Theme.MutedTag}

    ''' <summary>How many comics were put back, so the Collection list knows to refresh.</summary>
    Public Property RestoredComics As Integer

    Public Sub New(db As ComicDb)
        _db = db
        Text = "Deleted comics (bin)"
        StartPosition = FormStartPosition.CenterParent
        MinimizeBox = False
        ShowInTaskbar = False
        AutoScaleMode = AutoScaleMode.Font
        Font = New Font("Segoe UI", 9.5F)
        ClientSize = New Size(640, 420)
        MinimumSize = New Size(480, 300)

        Dim restore = Ui.MakeButton("Put back", AddressOf OnRestore, Theme.PrimaryTag)
        Dim empty = Ui.MakeButton("Empty the bin…", AddressOf OnEmpty, Theme.DangerTag)
        Dim close = Ui.MakeButton("Close", Sub(s, e) Me.Close())
        CancelButton = close
        Dim buttons As New FlowLayoutPanel With {.Dock = DockStyle.Bottom, .AutoSize = True, .Padding = New Padding(8)}
        buttons.Controls.AddRange({restore, empty, close})
        Dim body As New Panel With {.Dock = DockStyle.Fill, .Padding = New Padding(12, 4, 12, 4)}
        body.Controls.Add(_grid)
        AddHandler _grid.CellDoubleClick, Sub(s, e)
                                              If e.RowIndex >= 0 Then OnRestore(s, e)
                                          End Sub
        Controls.Add(body)
        Controls.Add(_note)
        Controls.Add(buttons)
        Theme.Apply(Me)
        ShowBin()
    End Sub

    Protected Overrides Sub OnHandleCreated(e As EventArgs)
        MyBase.OnHandleCreated(e)
        Theme.DarkTitleBar(Me)
    End Sub

    Private Sub ShowBin()
        Dim rows = _db.BinEntries()
        _grid.DataSource = rows
        For Each colName In {"id", "batch"}
            If _grid.Columns.Contains(colName) Then _grid.Columns(colName).Visible = False
        Next
        If _grid.Columns.Contains("Comic") Then _grid.Columns("Comic").FillWeight = 240
        _note.Text = If(rows.Rows.Count = 0,
            "The bin is empty. Comics you delete wait here for 30 days in case you want them back.",
            $"Deleted comics wait here for {ComicDb.BinDays} days, with their photos, box and loans. Select some and click Put back.")
    End Sub

    Private Sub OnRestore(sender As Object, e As EventArgs)
        Dim ids = _grid.SelectedRows.Cast(Of DataGridViewRow)().
            Select(Function(r) Convert.ToInt64(DirectCast(r.DataBoundItem, DataRowView)("id"), CultureInfo.InvariantCulture)).ToList()
        If ids.Count = 0 Then Return
        Dim back = _db.RestoreFromBin(ids)
        RestoredComics += back
        If back < ids.Count Then
            MessageBox.Show(Me, $"{ids.Count - back} of them were already back in your collection (added again since), so they were left as they are.",
                            "Put back", MessageBoxButtons.OK, MessageBoxIcon.Information)
        End If
        ShowBin()
    End Sub

    Private Sub OnEmpty(sender As Object, e As EventArgs)
        If _grid.RowCount = 0 Then Return
        If MessageBox.Show(Me, $"Delete the {_grid.RowCount} comic{If(_grid.RowCount = 1, "", "s")} in the bin for good? This can't be undone.",
                           "Empty the bin", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) <> DialogResult.OK Then Return
        _db.EmptyBin()
        ShowBin()
    End Sub

End Class
