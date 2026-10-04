Option Strict On

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
    Private ReadOnly _bought As New TextBox With {.PlaceholderText = "YYYY-MM-DD"}
    Private ReadOnly _notes As New TextBox With {.Multiline = True, .Height = 60, .ScrollBars = ScrollBars.Vertical}

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
        MaximizeBox = False
        ShowInTaskbar = False
        AutoScaleMode = AutoScaleMode.Font
        Font = New Font("Segoe UI", 9.5F)
        ClientSize = New Size(760, 640)
        MinimumSize = New Size(700, 560)
        BuildLayout()
        FillFields(_record)
    End Sub

    Private Sub BuildLayout()
        _format.Items.AddRange({"", "Trade Paperback", "Hardcover", "Omnibus", "Graphic Novel", "Magazine"})
        _condition.Items.AddRange(Ui.Conditions)
        For Each tb In {_series, _volume, _issue, _variantName, _title, _publisher, _coverDate, _paid, _value, _bought, _notes}
            tb.Dock = DockStyle.Fill
        Next
        _format.Dock = DockStyle.Fill
        _condition.Dock = DockStyle.Fill

        ' Barcode row
        Dim lookupButton = Ui.MakeButton("Look up", AddressOf OnLookupBarcode)
        Dim titleButton = Ui.MakeButton("Search by series and issue", AddressOf OnSearchTitle)
        Dim barcodeRow As New FlowLayoutPanel With {.AutoSize = True, .Dock = DockStyle.Fill, .WrapContents = False}
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

        ' Fields
        Dim fields As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 2, .AutoScroll = True}
        fields.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        fields.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        Dim add = Sub(label As String, c As Control)
                      fields.Controls.Add(Ui.MakeLabel(label))
                      fields.Controls.Add(c)
                  End Sub
        add("Series *", _series)
        add("Volume", _volume)
        add("Issue #", _issue)
        add("Cover / variant", _variantName)
        add("Story title", _title)
        add("Publisher", _publisher)
        add("Cover date", _coverDate)
        add("Format", _format)
        add("Condition", _condition)
        add("Copies", _copies)
        add("Price paid ($)", _paid)
        add("Value each ($)", _value)
        add("Date bought", _bought)
        add("Notes", _notes)

        Dim body As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 2, .RowCount = 1}
        body.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        body.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 220))
        body.Controls.Add(fields, 0, 0)
        Dim coverPanel As New Panel With {.Dock = DockStyle.Fill, .Padding = New Padding(10, 4, 4, 4)}
        coverPanel.Controls.Add(_cover)
        body.Controls.Add(coverPanel, 1, 0)

        ' Buttons
        Dim save = Ui.MakeButton("Save", AddressOf OnSave)
        Dim cancel = Ui.MakeButton("Cancel", Sub(s, e) DialogResult = DialogResult.Cancel)
        Dim buttons As New FlowLayoutPanel With {.Dock = DockStyle.Fill, .FlowDirection = FlowDirection.RightToLeft, .AutoSize = True}
        buttons.Controls.AddRange({cancel, save})
        CancelButton = cancel

        Dim root As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 1, .Padding = New Padding(10)}
        root.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        root.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        root.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        root.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        root.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        root.Controls.Add(barcodeRow)
        root.Controls.Add(_lookupStatus)
        root.Controls.Add(_matches)
        root.Controls.Add(body)
        root.Controls.Add(buttons)
        Controls.Add(root)
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
    End Sub

    Private Sub OnSave(sender As Object, e As EventArgs)
        If _series.Text.Trim() = "" Then
            Ui.ShowError(Me, "Please fill in the series name.")
            _series.Focus()
            Return
        End If
        Dim paid As Double?, value As Double?
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
