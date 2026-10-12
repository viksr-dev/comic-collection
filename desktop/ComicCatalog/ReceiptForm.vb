Option Strict On

Imports System.Drawing
Imports System.Globalization
Imports System.Windows.Forms
Imports ComicCatalog.Data

''' <summary>
''' Adds the comics on a shop receipt (a ComicHub invoice PDF, say) to your collection, with what you paid and when.
''' Each one is looked up on Metron for its cover and details first; you can fix anything before adding.
''' </summary>
Public Class ReceiptForm
    Inherits Form

    Private ReadOnly _db As ComicDb
    Private ReadOnly _metron As MetronClient
    Private ReadOnly _receipt As ShopReceipt
    Private ReadOnly _grid As DataGridView = Ui.MakeGrid()
    Private ReadOnly _note As New Label With {.Dock = DockStyle.Top, .AutoSize = False, .Height = 52, .Padding = New Padding(12, 8, 12, 0)}
    Private ReadOnly _status As New Label With {.AutoSize = True, .Padding = New Padding(4, 8, 0, 0), .Tag = Theme.MutedTag}
    Private ReadOnly _add As Button
    Private ReadOnly _matches As New Dictionary(Of Integer, MetronIssue)
    Private _closing As Boolean

    ''' <summary>The comics added, so the Collection can show them.</summary>
    Public ReadOnly Property AddedIds As New List(Of Long)

    Public Sub New(db As ComicDb, metron As MetronClient, receipt As ShopReceipt)
        _db = db
        _metron = metron
        _receipt = receipt
        Text = "Add comics from a shop receipt"
        StartPosition = FormStartPosition.CenterParent
        MinimizeBox = False
        ShowInTaskbar = False
        AutoScaleMode = AutoScaleMode.Font
        Font = New Font("Segoe UI", 9.5F)
        ClientSize = New Size(1000, 440)
        MinimumSize = New Size(700, 300)

        Dim day As Date
        Dim onDay = If(Date.TryParseExact(receipt.OrderDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, day),
                      day.ToString("d MMMM yyyy", CultureInfo.CurrentCulture), "")
        _note.Text = $"{receipt.Shop}" & If(receipt.OrderNumber <> "", $", order {receipt.OrderNumber}", "") & If(onDay <> "", $", {onDay}", "") &
                     If(receipt.Total.HasValue, $", total ${receipt.Total.Value:N2}", "") & "." & vbCrLf &
                     "Prices paid include any postage, shared out between the comics. Fix anything that's wrong, untick what you don't want, then click Add."

        _grid.ReadOnly = False
        _grid.AutoGenerateColumns = False
        _grid.SelectionMode = DataGridViewSelectionMode.CellSelect
        _grid.Columns.Add(New DataGridViewCheckBoxColumn With {.Name = "Add", .HeaderText = "Add", .FillWeight = 38})
        For Each col In {("Series", 170), ("Issue", 42), ("Cover", 45), ("Cover / variant", 120), ("Format", 80), ("Copies", 52), ("Price paid", 55)}
            _grid.Columns.Add(New DataGridViewTextBoxColumn With {.Name = col.Item1, .HeaderText = col.Item1, .FillWeight = col.Item2})
        Next
        _grid.Columns.Add(New DataGridViewTextBoxColumn With {.Name = "Found", .HeaderText = "Found on Metron", .FillWeight = 150, .ReadOnly = True})
        _grid.Columns("Price paid").DefaultCellStyle.Format = "N2"
        For Each item In receipt.Items
            Dim owned = _db.HasReceiptItem(item)
            Dim i = _grid.Rows.Add(Not owned, item.Series, item.Issue, item.CoverLetter, item.VariantName, item.Format, item.Quantity,
                                   item.PricePaid, If(owned, "You already have this one", ""))
            _grid.Rows(i).Tag = item
        Next
        AddHandler _grid.CellValueChanged, AddressOf OnCellChanged
        AddHandler _grid.CurrentCellDirtyStateChanged, Sub(s, e)
                                                           If _grid.IsCurrentCellDirty AndAlso TypeOf _grid.CurrentCell Is DataGridViewCheckBoxCell Then _grid.CommitEdit(DataGridViewDataErrorContexts.Commit)
                                                       End Sub
        AddHandler _grid.DataError, Sub(s, e) e.ThrowException = False

        _add = Ui.MakeButton("Add to collection", AddressOf OnAdd, Theme.PrimaryTag)
        Dim cancel = Ui.MakeButton("Cancel", Sub(s, e) Close())
        CancelButton = cancel
        Dim buttons As New FlowLayoutPanel With {.Dock = DockStyle.Bottom, .AutoSize = True, .Padding = New Padding(8)}
        buttons.Controls.AddRange({_add, cancel, _status})
        Dim body As New Panel With {.Dock = DockStyle.Fill, .Padding = New Padding(12, 4, 12, 4)}
        body.Controls.Add(_grid)
        Controls.Add(body)
        Controls.Add(_note)
        Controls.Add(buttons)
        Theme.Apply(Me)
        UpdateAddButton()
    End Sub

    Protected Overrides Sub OnHandleCreated(e As EventArgs)
        MyBase.OnHandleCreated(e)
        Theme.DarkTitleBar(Me)
    End Sub

    Protected Overrides Sub OnFormClosing(e As FormClosingEventArgs)
        _closing = True
        MyBase.OnFormClosing(e)
    End Sub

    Protected Overrides Async Sub OnShown(e As EventArgs)
        MyBase.OnShown(e)
        If Not _metron.IsSetUp Then
            _status.Text = "Add your relay address in Settings to find covers. Find covers will fill them in later."
            Return
        End If
        Dim found = 0, looked = 0
        Dim todo = _grid.Rows.Cast(Of DataGridViewRow)().Where(Function(r) Cell(r, "Issue") <> "" AndAlso Not ComicDb.IsTrade(Cell(r, "Format"))).ToList()
        For Each row In todo
            If _closing OrElse IsDisposed Then Return
            looked += 1
            _status.Text = $"Finding covers and details {looked} of {todo.Count}…"
            row.Cells("Found").Value = "Looking…"
            Dim match As MetronIssue = Nothing
            Try
                match = Await LookUp(CurrentItem(row))
            Catch ex As Exception
                If _closing OrElse IsDisposed Then Return
                row.Cells("Found").Value = ""
                _status.Text = $"Couldn't reach Metron ({ex.Message}). You can still add them; Find covers will try again later."
                Return
            End Try
            If _closing OrElse IsDisposed Then Return
            If match IsNot Nothing Then
                _matches(row.Index) = match
                found += 1
                row.Cells("Found").Value = $"{match.Series}" & If(match.Volume <> "", $" ({match.Volume})", "") & $" #{match.Number}" &
                                           If(match.Publisher <> "", $", {match.Publisher}", "")
            ElseIf Cell(row, "Found") = "Looking…" Then
                row.Cells("Found").Value = "Not found (Find covers can try again later)"
            End If
        Next
        _status.Text = If(todo.Count = 0, "", $"Found {found} of {todo.Count} on Metron.")
    End Sub

    ''' <summary>Searches by series and issue; if the shop's series name finds nothing, tries its first word.</summary>
    Private Async Function LookUp(item As ReceiptItem) As Task(Of MetronIssue)
        Dim results = Await Search(item.Series, item.Issue)
        Dim match = ShopReceipts.PickMatch(results, item, _receipt.OrderDate)
        If match IsNot Nothing OrElse results.Count > 0 Then Return match
        Dim firstWord = item.Series.Split(" "c).FirstOrDefault(Function(w) w.Length > 2 AndAlso Not w.Equals("The", StringComparison.OrdinalIgnoreCase))
        If firstWord Is Nothing OrElse firstWord = item.Series Then Return Nothing
        Await Task.Delay(3200)
        ' Only take a match with the same name (punctuation aside), e.g. "Gambit Wanted" for "Gambit: Wanted".
        Dim wider = Await Search(firstWord, item.Issue)
        Return ShopReceipts.PickMatch(wider.Where(Function(x) ShopReceipts.Squash(x.Series) = ShopReceipts.Squash(item.Series)), item, _receipt.OrderDate)
    End Function

    Private Async Function Search(series As String, issue As String) As Task(Of List(Of MetronIssue))
        For attempt = 1 To 3
            Dim slowDown = False
            Try
                Dim results = Await _metron.SearchAsync(series, issue)
                Await Task.Delay(3200)
                Return results
            Catch ex As InvalidOperationException When ex.Message.StartsWith("Too many") AndAlso attempt < 3
                slowDown = True
            End Try
            If slowDown Then
                _status.Text = "Metron asked us to slow down, waiting a minute…"
                Await Task.Delay(65000)
            End If
        Next
        Return New List(Of MetronIssue)
    End Function

    Private Shared Function Cell(row As DataGridViewRow, name As String) As String
        Return Convert.ToString(row.Cells(name).Value, CultureInfo.InvariantCulture).Trim()
    End Function

    ''' <summary>The receipt line as it stands in the grid, with any fixes you typed.</summary>
    Private Function CurrentItem(row As DataGridViewRow) As ReceiptItem
        Dim original = DirectCast(row.Tag, ReceiptItem)
        Dim copies As Integer
        Dim price As Double?
        If Not Ui.ParseMoney(Cell(row, "Price paid"), price) Then price = original.PricePaid
        Return New ReceiptItem With {
            .Text = original.Text, .Extra = original.Extra,
            .Series = Cell(row, "Series"), .Issue = Cell(row, "Issue"), .CoverLetter = Cell(row, "Cover"),
            .VariantName = Cell(row, "Cover / variant"), .Format = Cell(row, "Format"),
            .Quantity = If(Integer.TryParse(Cell(row, "Copies"), copies) AndAlso copies > 0, copies, 1),
            .PricePaid = price}
    End Function

    Private Sub OnCellChanged(sender As Object, e As DataGridViewCellEventArgs)
        If e.RowIndex < 0 Then Return
        Dim colName = _grid.Columns(e.ColumnIndex).Name
        ' A different series or issue isn't the comic that was found any more.
        If (colName = "Series" OrElse colName = "Issue") AndAlso _matches.Remove(e.RowIndex) Then
            _grid.Rows(e.RowIndex).Cells("Found").Value = "Changed: Find covers will look it up"
        End If
        If colName = "Add" Then UpdateAddButton()
    End Sub

    Private Function Ticked() As List(Of DataGridViewRow)
        Return _grid.Rows.Cast(Of DataGridViewRow)().Where(Function(r) TypeOf r.Cells("Add").Value Is Boolean AndAlso CBool(r.Cells("Add").Value)).ToList()
    End Function

    Private Sub UpdateAddButton()
        Dim n = Ticked().Count
        _add.Text = If(n = 1, "Add 1 comic", $"Add {n} comics")
        _add.Enabled = n > 0
    End Sub

    Private Sub OnAdd(sender As Object, e As EventArgs)
        _grid.EndEdit()
        Dim rows = Ticked()
        If rows.Any(Function(r) Cell(r, "Series") = "") Then
            Ui.ShowError(Me, "Every comic needs a series name.")
            Return
        End If
        Try
            For Each row In rows
                Dim match As MetronIssue = Nothing
                _matches.TryGetValue(row.Index, match)
                AddedIds.Add(_db.AddFromReceipt(ShopReceipts.ToComic(CurrentItem(row), _receipt, match)))
            Next
        Catch ex As Exception
            Ui.ShowError(Me, $"Couldn't add them: {ex.Message}")
            Return
        End Try
        DialogResult = DialogResult.OK
    End Sub

End Class
