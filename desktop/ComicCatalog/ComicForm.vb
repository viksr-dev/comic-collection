Option Strict On

Imports System.Diagnostics
Imports System.Drawing
Imports System.Windows.Forms
Imports ComicCatalog.Data

''' <summary>Add or edit one comic. Typing a barcode and pressing Look up fills it in from Metron.</summary>
Public Class ComicForm
    Inherits Form

    Private ReadOnly _db As ComicDb
    Private ReadOnly _metron As MetronClient
    Private ReadOnly _record As ComicRecord
    Private ReadOnly _isNew As Boolean

    Private ReadOnly _barcode As New TextBox With {.Width = 220}
    Private ReadOnly _lookupStatus As New Label With {.AutoSize = True, .ForeColor = SystemColors.GrayText, .Margin = New Padding(3, 6, 3, 3)}
    Private ReadOnly _matches As New ListBox With {.Height = 90, .Dock = DockStyle.Fill, .Visible = False, .IntegralHeight = False}
    Private ReadOnly _cover As New PictureBox With {.Size = New Size(200, 300), .SizeMode = PictureBoxSizeMode.Zoom,
                                                    .BorderStyle = BorderStyle.FixedSingle, .BackColor = SystemColors.ControlLight}

    Private ReadOnly _series As New TextBox()
    Private ReadOnly _volume As New TextBox()
    Private ReadOnly _issue As New TextBox()
    Private ReadOnly _variantName As New TextBox()
    Private ReadOnly _title As New TextBox()
    Private ReadOnly _publisher As New TextBox()
    Private ReadOnly _coverDate As New TextBox With {.PlaceholderText = "YYYY-MM"}
    Private ReadOnly _format As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDown}
    Private ReadOnly _condition As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList}
    Private ReadOnly _copies As New NumericUpDown With {.Minimum = 1, .Maximum = 999, .Value = 1, .Width = 70}
    Private ReadOnly _paid As New TextBox With {.PlaceholderText = "for all copies"}
    Private ReadOnly _value As New TextBox With {.PlaceholderText = "per copy"}
    Private ReadOnly _coverPrice As New TextBox With {.PlaceholderText = "filled in by Look up"}
    Private ReadOnly _bought As New TextBox With {.PlaceholderText = "YYYY-MM-DD"}
    Private ReadOnly _notes As New TextBox With {.Multiline = True, .Height = 60, .ScrollBars = ScrollBars.Vertical}

    ''' <summary>Opens maximised when the last edit window was maximised.</summary>
    Public Shared Property StartMaximized As Boolean

    ''' <summary>The comic's id once saved.</summary>
    Public Property SavedComicId As Long

    Public Sub New(db As ComicDb, metron As MetronClient, Optional record As ComicRecord = Nothing, Optional barcode As String = "")
        _db = db
        _metron = metron
        _isNew = record Is Nothing OrElse record.ComicId = 0
        _record = If(record, New ComicRecord())
        If barcode <> "" Then _record.Barcode = Data.Barcode.Parse(barcode).Full

        Text = If(_isNew, "Add comic", $"Edit {_record}")
        StartPosition = FormStartPosition.CenterParent
        FormBorderStyle = FormBorderStyle.Sizable
        MinimizeBox = False
        MaximizeBox = True
        ShowInTaskbar = False
        AutoScaleMode = AutoScaleMode.Font
        Font = New Font("Segoe UI", 9.5F)
        ClientSize = New Size(1000, 620)
        MinimumSize = New Size(760, 480)
        BuildLayout()
        FillFields(_record)
    End Sub

    Private Sub BuildLayout()
        _format.Items.AddRange({"", "Trade Paperback", "Hardcover", "Omnibus", "Graphic Novel", "Magazine"})
        _condition.Items.AddRange(Ui.Conditions)
        For Each tb In {_series, _volume, _issue, _variantName, _title, _publisher, _coverDate, _paid, _value, _coverPrice, _bought, _notes}
            tb.Dock = DockStyle.Fill
        Next
        _format.Dock = DockStyle.Fill
        _condition.Dock = DockStyle.Fill

        ' Barcode row (wraps onto two lines on a narrow window)
        Dim lookupButton = Ui.MakeButton("Look up", AddressOf OnLookupBarcode, Theme.PrimaryTag)
        Dim titleButton = Ui.MakeButton("Search by series and issue", AddressOf OnSearchTitle)
        Dim barcodeRow As New FlowLayoutPanel With {.AutoSize = True, .Dock = DockStyle.Fill, .WrapContents = True}
        barcodeRow.Controls.AddRange({Ui.MakeLabel("Barcode"), _barcode, lookupButton, titleButton})
        AddHandler _barcode.KeyDown, Sub(s, e)
                                          If e.KeyCode = Keys.Enter Then
                                              e.SuppressKeyPress = True
                                              OnLookupBarcode(s, e)
                                          End If
                                      End Sub
        AddHandler _matches.SelectedIndexChanged, Sub(s, e)
                                                      Dim m = TryCast(_matches.SelectedItem, MetronIssue)
                                                      If m IsNot Nothing Then ApplyMatch(m)
                                                  End Sub
        _matches.Dock = DockStyle.Top
        _matches.Height = 84

        ' Fields in two columns, so everything fits without scrolling
        Dim fields As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 4}
        fields.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        fields.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 50))
        fields.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        fields.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 50))
        Dim row = 0
        Dim pair = Sub(label1 As String, c1 As Control, label2 As String, c2 As Control)
                       fields.RowStyles.Add(New RowStyle(SizeType.AutoSize))
                       fields.Controls.Add(Ui.MakeLabel(label1), 0, row)
                       fields.Controls.Add(c1, 1, row)
                       If c2 Is Nothing Then
                           fields.SetColumnSpan(c1, 3)
                       Else
                           fields.Controls.Add(Ui.MakeLabel(label2), 2, row)
                           fields.Controls.Add(c2, 3, row)
                       End If
                       row += 1
                   End Sub
        pair("Series *", _series, Nothing, Nothing)
        pair("Issue #", _issue, "Volume", _volume)
        pair("Story title", _title, Nothing, Nothing)
        pair("Cover / variant", _variantName, "Format", _format)
        pair("Publisher", _publisher, "Cover date", _coverDate)
        pair("Condition", _condition, "Copies", _copies)
        pair("Price paid ($)", _paid, "Value each ($)", _value)
        pair("Cover price ($)", _coverPrice, "Date bought", _bought)
        pair("Notes", _notes, Nothing, Nothing)
        fields.RowStyles.Add(New RowStyle(SizeType.Percent, 100))

        ' Buttons
        Dim save = Ui.MakeButton("Save", AddressOf OnSave, Theme.PrimaryTag)
        Dim cancel = Ui.MakeButton("Cancel", Sub(s, e) DialogResult = DialogResult.Cancel)
        Dim checkValue = Ui.MakeButton("Check value on eBay", AddressOf OnCheckValue)
        Dim buttons As New FlowLayoutPanel With {.Dock = DockStyle.Fill, .FlowDirection = FlowDirection.RightToLeft, .AutoSize = True}
        buttons.Controls.AddRange({cancel, save, checkValue})
        CancelButton = cancel

        ' Right side: barcode, lookup results, fields, buttons
        Dim right As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 1, .Padding = New Padding(8, 0, 0, 0)}
        right.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        right.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        right.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        right.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        right.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        right.Controls.Add(barcodeRow, 0, 0)
        right.Controls.Add(_lookupStatus, 0, 1)
        right.Controls.Add(_matches, 0, 2)
        right.Controls.Add(fields, 0, 3)
        right.Controls.Add(buttons, 0, 4)

        ' Left side: the cover, as big as the window allows
        _cover.Dock = DockStyle.Fill
        _cover.BorderStyle = BorderStyle.None

        Dim root As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 2, .RowCount = 1, .Padding = New Padding(12)}
        root.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 30))
        root.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 70))
        root.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        root.Controls.Add(_cover, 0, 0)
        root.Controls.Add(right, 1, 0)
        Controls.Add(root)
        Theme.Apply(Me)
        _cover.BackColor = Theme.Panel2
        _lookupStatus.ForeColor = Theme.Muted
    End Sub

    Protected Overrides Sub OnHandleCreated(e As EventArgs)
        MyBase.OnHandleCreated(e)
        Theme.DarkTitleBar(Me)
    End Sub

    Protected Overrides Sub OnLoad(e As EventArgs)
        MyBase.OnLoad(e)
        ' Never bigger than the screen, so nothing is cut off on a laptop.
        Dim area = Screen.FromControl(If(Owner, CType(Me, Control))).WorkingArea
        Size = New Size(Math.Min(Width, CInt(area.Width * 0.95)), Math.Min(Height, CInt(area.Height * 0.95)))
        CenterToParent()
        If StartMaximized Then WindowState = FormWindowState.Maximized
    End Sub

    Protected Overrides Sub OnFormClosed(e As FormClosedEventArgs)
        StartMaximized = WindowState = FormWindowState.Maximized
        MyBase.OnFormClosed(e)
    End Sub

    Protected Overrides Sub OnShown(e As EventArgs)
        MyBase.OnShown(e)
        If Not _metron.IsSetUp Then
            _lookupStatus.Text = "Comic lookup isn't set up yet (see the Settings tab), so fill in the details yourself."
        ElseIf _isNew AndAlso _barcode.Text <> "" AndAlso _series.Text = "" Then
            OnLookupBarcode(Me, EventArgs.Empty)
        End If
        If _isNew AndAlso _barcode.Text = "" Then _barcode.Focus() Else _series.Focus()
    End Sub

    Private Sub FillFields(c As ComicRecord)
        _barcode.Text = c.Barcode
        _series.Text = c.Series
        _volume.Text = c.Volume
        _issue.Text = c.Issue & c.CoverLetter
        _variantName.Text = c.VariantName
        _title.Text = c.Title
        _publisher.Text = c.Publisher
        _coverDate.Text = c.CoverDate
        _format.Text = c.Format
        _condition.SelectedItem = If(Ui.Conditions.Contains(c.Condition), c.Condition, "")
        _copies.Value = Math.Max(1, Math.Min(999, c.Quantity))
        _paid.Text = Ui.MoneyText(c.PricePaid)
        _value.Text = Ui.MoneyText(c.CurrentValue)
        _coverPrice.Text = Ui.MoneyText(c.CoverPrice)
        _bought.Text = c.PurchaseDate
        _notes.Text = c.Notes
        ShowCover(c.CoverUrl)
    End Sub

    Private Sub ShowCover(url As String)
        _cover.Image = Nothing
        If Not String.IsNullOrWhiteSpace(url) Then
            Try
                _cover.LoadAsync(url)
            Catch ex As Exception When TypeOf ex Is ArgumentException OrElse TypeOf ex Is InvalidOperationException
            End Try
        End If
    End Sub

    Private Async Sub OnLookupBarcode(sender As Object, e As EventArgs)
        Dim code = Data.Barcode.Parse(_barcode.Text)
        If Not code.IsValid Then
            _lookupStatus.Text = "Type the 12 digits under the barcode, then the small 5 digits to its right (if there are any)."
            Return
        End If
        _barcode.Text = code.Full
        If code.Addon.Length = 5 AndAlso _issue.Text = "" Then _issue.Text = code.Issue
        Await RunLookup(Function() _metron.LookupBarcodeAsync(code), "that barcode")
    End Sub

    Private Async Sub OnSearchTitle(sender As Object, e As EventArgs)
        If _series.Text.Trim() = "" Then
            _lookupStatus.Text = "Type the series name (and issue number) first, then search."
            _series.Focus()
            Return
        End If
        Await RunLookup(Function() _metron.SearchAsync(_series.Text.Trim(), _issue.Text.Trim()), "that series and issue")
    End Sub

    Private Async Function RunLookup(find As Func(Of Task(Of List(Of MetronIssue))), what As String) As Task
        _matches.Visible = False
        _matches.Items.Clear()
        _lookupStatus.Text = "Looking it up on Metron…"
        UseWaitCursor = True
        Try
            Dim results = Await find()
            If results.Count = 0 Then
                _lookupStatus.Text = $"Metron doesn't know {what} yet. Fill in the details yourself, or try searching by series and issue."
            ElseIf results.Count = 1 Then
                ApplyMatch(results(0))
                _lookupStatus.Text = $"Found it: {results(0)}"
            Else
                _lookupStatus.Text = $"Found {results.Count} possible matches. Pick the right one:"
                _matches.Items.AddRange(results.Cast(Of Object)().ToArray())
                _matches.Visible = True
            End If
        Catch ex As Exception
            _lookupStatus.Text = ex.Message
        Finally
            UseWaitCursor = False
        End Try
    End Function

    Private Sub ApplyMatch(m As MetronIssue)
        _series.Text = m.Series
        _volume.Text = m.Volume
        _issue.Text = m.Number
        If m.Title <> "" Then _title.Text = m.Title
        If m.Publisher <> "" Then _publisher.Text = m.Publisher
        If m.CoverDate <> "" Then _coverDate.Text = m.CoverDate
        _record.MetronId = m.MetronId
        _record.CoverUrl = m.CoverUrl
        ShowCover(m.CoverUrl)
        If m.Price.HasValue Then
            _coverPrice.Text = Ui.MoneyText(m.Price)
        ElseIf m.MetronId > 0 Then
            FetchCoverPrice(m.MetronId)
        End If
    End Sub

    ' Search results don't carry the cover price, so it's fetched from the issue's full details.
    Private Async Sub FetchCoverPrice(metronId As Long)
        Try
            Dim full = Await _metron.IssueAsync(metronId)
            If IsDisposed OrElse full Is Nothing OrElse Not full.Price.HasValue Then Return
            If _record.MetronId.GetValueOrDefault() = metronId Then _coverPrice.Text = Ui.MoneyText(full.Price)
        Catch ex As Exception
            ' The cover price is a nice extra; the rest of the lookup already worked.
        End Try
    End Sub

    ' Opens eBay's sold listings for this issue, which show what copies actually sold for.
    Private Sub OnCheckValue(sender As Object, e As EventArgs)
        Dim series = _series.Text.Trim()
        If series = "" Then
            Ui.ShowError(Me, "Fill in the series name (and issue number) first.")
            _series.Focus()
            Return
        End If
        Dim words = series
        If _issue.Text.Trim() <> "" Then words &= " " & _issue.Text.Trim()
        ' A volume year tells apart series that restarted at #1, like Batman (2016).
        If System.Text.RegularExpressions.Regex.IsMatch(_volume.Text.Trim(), "^\d{4}$") Then words &= " " & _volume.Text.Trim()
        If _variantName.Text.Trim() <> "" Then words &= " " & _variantName.Text.Trim()
        Dim url = "https://www.ebay.com/sch/i.html?LH_Sold=1&LH_Complete=1&_nkw=" & Uri.EscapeDataString(words)
        Try
            Process.Start(New ProcessStartInfo(url) With {.UseShellExecute = True})
        Catch ex As Exception
            Ui.ShowError(Me, $"Couldn't open your web browser: {ex.Message}")
        End Try
    End Sub

    Private Sub OnSave(sender As Object, e As EventArgs)
        If _series.Text.Trim() = "" Then
            Ui.ShowError(Me, "Please fill in the series name.")
            _series.Focus()
            Return
        End If
        Dim paid As Double?, value As Double?, coverPrice As Double?
        If Not Ui.ParseMoney(_paid.Text, paid) Then
            Ui.ShowError(Me, "Price paid should be a number, like 12.50.")
            _paid.Focus()
            Return
        End If
        If Not Ui.ParseMoney(_value.Text, value) Then
            Ui.ShowError(Me, "Value should be a number, like 20 or 20.00.")
            _value.Focus()
            Return
        End If

        If Not Ui.ParseMoney(_coverPrice.Text, coverPrice) Then
            Ui.ShowError(Me, "Cover price should be a number, like 3.99.")
            _coverPrice.Focus()
            Return
        End If

        ' "47A" is issue 47, cover A.
        Dim issue = _issue.Text.Trim()
        Dim letter = ""
        Dim m = System.Text.RegularExpressions.Regex.Match(issue, "^(\d+(?:\.\d+)?)([A-Z]{1,2})$")
        If m.Success Then
            issue = m.Groups(1).Value
            letter = m.Groups(2).Value
        End If

        With _record
            .Barcode = Data.Barcode.OnlyDigits(_barcode.Text)
            .Series = _series.Text.Trim()
            .Volume = _volume.Text.Trim()
            .Issue = issue
            .CoverLetter = letter
            .VariantName = _variantName.Text.Trim()
            .Title = _title.Text.Trim()
            .Publisher = _publisher.Text.Trim()
            .CoverDate = _coverDate.Text.Trim()
            .Format = _format.Text.Trim()
            .Condition = CStr(If(_condition.SelectedItem, ""))
            .Quantity = CInt(_copies.Value)
            .PricePaid = paid
            .CurrentValue = value
            .CoverPrice = coverPrice
            .PurchaseDate = _bought.Text.Trim()
            .Notes = _notes.Text.Trim()
        End With
        Try
            SavedComicId = _db.SaveComic(_record, addCopies:=_isNew)
            DialogResult = DialogResult.OK
        Catch ex As Exception
            Ui.ShowError(Me, $"Couldn't save: {ex.Message}")
        End Try
    End Sub

End Class
