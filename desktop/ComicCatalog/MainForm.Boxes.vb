Option Strict On

Imports System.Data
Imports System.Drawing
Imports System.Globalization
Imports System.Windows.Forms
Imports ComicCatalog.Data

' The Boxes page: every box and shelf down the left, and what's in the one you click on the right.
Partial Public Class MainForm

    Private ReadOnly _boxesGrid As DataGridView = Ui.MakeGrid()
    Private ReadOnly _boxComicsGrid As DataGridView = Ui.MakeGrid()
    Private ReadOnly _boxTitle As New Label With {.Dock = DockStyle.Top, .Height = 34, .TextAlign = ContentAlignment.MiddleLeft,
                                                  .Font = New Font("Segoe UI Semibold", 12.0F)}
    Private ReadOnly _boxesNote As New Label With {.AutoSize = True, .Margin = New Padding(12, 12, 3, 3), .Tag = Theme.MutedTag}
    ' The box on each row of the boxes list ("" for the "Not in a box" row at the end).
    Private _boxRowNames As New List(Of String)
    Private _shownBox As String
    Private _fillingBoxesPage As Boolean

    Private Function BuildBoxesTab() As Control
        Dim page As New Panel()
        Dim bar As New FlowLayoutPanel With {.Dock = DockStyle.Top, .AutoSize = True, .Padding = New Padding(0, 0, 0, 6)}
        bar.Controls.AddRange({Ui.MakeButton("＋ Add a box", AddressOf OnAddBox, Theme.PrimaryTag),
                               Ui.MakeButton("Rename…", AddressOf OnRenameBox),
                               Ui.MakeButton("Remove box", AddressOf OnRemoveBox),
                               Ui.MakeButton("Print box labels", AddressOf OnBoxLabels),
                               _boxesNote})
        Dim help As New Label With {.Dock = DockStyle.Top, .Height = 28, .TextAlign = ContentAlignment.MiddleLeft, .Tag = Theme.MutedTag,
                                    .Text = "Click a box to see what's in it. Double-click a comic to open it."}
        Dim split As New SplitContainer With {.Dock = DockStyle.Fill, .SplitterDistance = 420}
        split.Panel1.Controls.Add(_boxesGrid)
        _boxesGrid.MultiSelect = False
        AddHandler _boxesGrid.SelectionChanged, Sub(s, e)
                                                    If _fillingBoxesPage OrElse _boxesGrid.CurrentRow Is Nothing Then Return
                                                    ShowBoxContents(BoxOnRow(_boxesGrid.CurrentRow.Index))
                                                End Sub
        AddHandler _boxesGrid.CellDoubleClick, Sub(s, e) If e.RowIndex >= 0 AndAlso BoxOnRow(e.RowIndex) <> "" Then OnRenameBox(s, e)

        Dim moveButton = Ui.MakeButton("Move selected to another box…", AddressOf OnMoveBoxComics, Theme.PrimaryTag)
        Dim outButton = Ui.MakeButton("Take selected out of the box", AddressOf OnTakeOutOfBox)
        Dim buttons As New FlowLayoutPanel With {.Dock = DockStyle.Bottom, .AutoSize = True, .Padding = New Padding(0, 6, 0, 0)}
        buttons.Controls.AddRange({moveButton, outButton})
        split.Panel2.Padding = New Padding(10, 0, 0, 0)
        split.Panel2.Controls.Add(_boxComicsGrid)
        split.Panel2.Controls.Add(_boxTitle)
        split.Panel2.Controls.Add(buttons)
        AddHandler _boxComicsGrid.CellDoubleClick, Sub(s, e)
                                                       If e.RowIndex < 0 Then Return
                                                       Dim c = _db.GetComic(Convert.ToInt64(_boxComicsGrid.Rows(e.RowIndex).Cells("comic_id").Value, CultureInfo.InvariantCulture))
                                                       If c Is Nothing Then Return
                                                       Using f As New ComicForm(_db, Metron, c)
                                                           If f.ShowDialog(Me) = DialogResult.OK Then
                                                               RefreshCollection()
                                                               RefreshBoxesPage()
                                                           End If
                                                       End Using
                                                   End Sub

        page.Controls.Add(split)
        page.Controls.Add(help)
        page.Controls.Add(bar)
        Return page
    End Function

    Private Function BoxOnRow(index As Integer) As String
        Return If(index >= 0 AndAlso index < _boxRowNames.Count, _boxRowNames(index), Nothing)
    End Function

    ''' <summary>Fills the boxes list, keeping the box being shown (or showing <paramref name="selectBox"/>).</summary>
    Private Sub RefreshBoxesPage(Optional selectBox As String = Nothing)
        If _db Is Nothing Then Return
        Dim want = If(selectBox, _shownBox)
        Dim table = _db.BoxSummaries()
        Dim loose = _db.NotInBoxCount()
        _boxRowNames = table.Rows.Cast(Of DataRow)().Select(Function(r) CStr(r("Box"))).ToList()
        If loose > 0 Then
            table.Rows.Add("Not in a box", loose, DBNull.Value, DBNull.Value)
            _boxRowNames.Add("")
        End If
        _fillingBoxesPage = True
        Try
            _boxesGrid.DataSource = table
            If _boxesGrid.Columns.Contains("Box") Then _boxesGrid.Columns("Box").FillWeight = 200
            If _boxesGrid.Columns.Contains("Value") Then _boxesGrid.Columns("Value").DefaultCellStyle.Format = "N2"
            For Each colName In {"Comics", "Series", "Value"}
                If _boxesGrid.Columns.Contains(colName) Then _boxesGrid.Columns(colName).DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight
            Next
            If table.Rows.Count > 0 Then
                Dim i = If(want Is Nothing, -1, _boxRowNames.FindIndex(Function(n) String.Equals(n, want, StringComparison.OrdinalIgnoreCase)))
                If i < 0 Then i = 0
                _boxesGrid.CurrentCell = _boxesGrid.Rows(i).Cells("Box")
            End If
        Finally
            _fillingBoxesPage = False
        End Try
        Dim boxes = _boxRowNames.Where(Function(n) n <> "").Count()
        _boxesNote.Text = If(boxes = 0, "No boxes yet. Add one, then put comics in it here or on the Collection page.",
                             $"{boxes:N0} box{If(boxes = 1, "", "es")}")
        ShowBoxContents(If(table.Rows.Count = 0, Nothing, BoxOnRow(_boxesGrid.CurrentRow.Index)))
    End Sub

    Private Sub ShowBoxContents(box As String)
        _shownBox = box
        If box Is Nothing Then
            _boxComicsGrid.DataSource = Nothing
            _boxTitle.Text = ""
            Return
        End If
        Dim rows = _db.BoxComics(box)
        _boxComicsGrid.DataSource = rows
        If _boxComicsGrid.Columns.Contains("comic_id") Then _boxComicsGrid.Columns("comic_id").Visible = False
        If _boxComicsGrid.Columns.Contains("Series") Then _boxComicsGrid.Columns("Series").FillWeight = 220
        If _boxComicsGrid.Columns.Contains("Value") Then _boxComicsGrid.Columns("Value").DefaultCellStyle.Format = "N2"
        Dim copies = rows.Rows.Cast(Of DataRow)().Sum(Function(r) Convert.ToInt32(r("Copies"), CultureInfo.InvariantCulture))
        _boxTitle.Text = $"{If(box = "", "Not in a box", box)}  ·  {If(copies = 0, "empty", ComicsText(copies))}"
    End Sub

    Private Function SelectedBoxComicIds() As List(Of Long)
        Return _boxComicsGrid.SelectedRows.Cast(Of DataGridViewRow)().
            Select(Function(r) Convert.ToInt64(r.Cells("comic_id").Value, CultureInfo.InvariantCulture)).ToList()
    End Function

    Private Sub OnAddBox(sender As Object, e As EventArgs)
        Dim name = Ui.Ask(Me, "Add a box", "Name the new box or shelf, for example Box 9 or Shelf 2:", NextBoxName())
        If String.IsNullOrWhiteSpace(name) Then Return
        If Not _db.AddBox(name) Then
            Ui.ShowError(Me, $"There's already a box called {name.Trim()}.")
            RefreshBoxesPage(name.Trim())
            Return
        End If
        RememberUndo($"Added {name.Trim()}", Sub() _db.RemoveBox(name))
        RefreshBoxesPage(name.Trim())
        RefreshCollection()
    End Sub

    ''' <summary>"Box 10" when the last box is "Box 9", else "".</summary>
    Private Function NextBoxName() As String
        Dim last = _db.Locations().LastOrDefault(Function(n) System.Text.RegularExpressions.Regex.IsMatch(n, "\d+\s*$"))
        If last Is Nothing Then Return If(_db.Locations().Count = 0, "Box 1", "")
        Dim m = System.Text.RegularExpressions.Regex.Match(last, "(\d+)\s*$")
        Return last.Substring(0, m.Index) & (Long.Parse(m.Groups(1).Value, CultureInfo.InvariantCulture) + 1).ToString(CultureInfo.InvariantCulture)
    End Function

    Private Sub OnRenameBox(sender As Object, e As EventArgs)
        Dim box = _shownBox
        If String.IsNullOrEmpty(box) Then
            Ui.ShowError(Me, "Click the box to rename first.")
            Return
        End If
        Dim name = Ui.Ask(Me, "Rename box", $"New name for {box}:", box)
        If String.IsNullOrWhiteSpace(name) OrElse name.Trim() = box Then Return
        name = name.Trim()
        Dim existed = _db.Locations().Any(Function(n) String.Equals(n, name, StringComparison.OrdinalIgnoreCase))
        If existed AndAlso Not String.Equals(name, box, StringComparison.OrdinalIgnoreCase) AndAlso
           MessageBox.Show(Me, $"There's already a box called {name}. Put everything from {box} in it?", "Rename box",
                           MessageBoxButtons.OKCancel, MessageBoxIcon.Question) <> DialogResult.OK Then Return
        Dim ids = _db.BoxComicIds(box)
        Dim before = _db.Snapshot(ids)
        _db.RenameBox(box, name)
        RememberUndo($"Renamed {box} to {name}", Sub()
                                                      _db.Restore(before)
                                                      If Not existed Then _db.RemoveBox(name)
                                                      If ids.Count = 0 Then _db.AddBox(box)
                                                  End Sub)
        RefreshBoxesPage(name)
        RefreshCollection()
    End Sub

    Private Sub OnRemoveBox(sender As Object, e As EventArgs)
        Dim box = _shownBox
        If String.IsNullOrEmpty(box) Then
            Ui.ShowError(Me, "Click the box to remove first.")
            Return
        End If
        Dim ids = _db.BoxComicIds(box)
        If ids.Count > 0 AndAlso MessageBox.Show(Me, $"Remove {box}? The {ComicsText(ids.Count)} in it stay in your collection, just not in a box.",
                                                 "Remove box", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) <> DialogResult.OK Then Return
        Dim before = _db.Snapshot(ids)
        _db.RemoveBox(box)
        RememberUndo($"Removed {box}", Sub()
                                           _db.Restore(before)
                                           If ids.Count = 0 Then _db.AddBox(box)
                                       End Sub)
        _shownBox = Nothing
        RefreshBoxesPage()
        RefreshCollection()
    End Sub

    Private Sub OnMoveBoxComics(sender As Object, e As EventArgs)
        Dim ids = SelectedBoxComicIds()
        If ids.Count = 0 Then
            Ui.ShowError(Me, "Select the comics to move first. Hold Ctrl or Shift to pick several.")
            Return
        End If
        Dim box = Ui.Ask(Me, "Move to another box", $"Which box or shelf should the {ComicsText(ids.Count)} go in?",
                         If(_settings.LastBox <> _shownBox, _settings.LastBox, ""), _db.Locations())
        If String.IsNullOrWhiteSpace(box) Then Return
        box = box.Trim()
        Dim undo = _db.Snapshot(ids)
        _db.SetLocation(ids, box)
        RememberUndo(undo, $"Put {ComicsText(ids.Count)} in {box}")
        _settings.LastBox = box
        RefreshBoxesPage()
        RefreshCollection()
    End Sub

    Private Sub OnTakeOutOfBox(sender As Object, e As EventArgs)
        Dim ids = SelectedBoxComicIds()
        If ids.Count = 0 OrElse String.IsNullOrEmpty(_shownBox) Then
            Ui.ShowError(Me, "Select the comics to take out first. Hold Ctrl or Shift to pick several.")
            Return
        End If
        Dim undo = _db.Snapshot(ids)
        _db.SetLocation(ids, "")
        RememberUndo(undo, $"Took {ComicsText(ids.Count)} out of {_shownBox}")
        RefreshBoxesPage()
        RefreshCollection()
    End Sub

End Class
