Option Strict On

Imports System.Drawing
Imports System.Globalization
Imports System.Windows.Forms
Imports ComicCatalog.Data

''' <summary>Changes the same things on several comics at once. Anything left blank stays as it is.</summary>
Public Class BulkEditForm
    Inherits Form

    Private Const Keep As String = "(leave as it is)"
    Private Const TakeOut As String = "(take them out of their box)"

    Private ReadOnly _condition As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList, .Dock = DockStyle.Fill}
    Private ReadOnly _value As New TextBox With {.Dock = DockStyle.Fill, .PlaceholderText = "leave blank to keep each one's"}
    Private ReadOnly _bought As New TextBox With {.Dock = DockStyle.Fill, .PlaceholderText = "YYYY-MM-DD, or blank to keep"}
    Private ReadOnly _box As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDown, .Dock = DockStyle.Fill}
    Private ReadOnly _format As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDown, .Dock = DockStyle.Fill}
    Private ReadOnly _publisher As New TextBox With {.Dock = DockStyle.Fill, .PlaceholderText = "leave blank to keep (changes the whole series)"}
    Private ReadOnly _read As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList, .Dock = DockStyle.Fill}

    ''' <summary>What to change, once OK is clicked.</summary>
    Public ReadOnly Property Changes As New BulkChanges()

    Public Sub New(count As Integer, boxes As IEnumerable(Of String))
        Text = $"Change {count:N0} comics"
        StartPosition = FormStartPosition.CenterParent
        FormBorderStyle = FormBorderStyle.FixedDialog
        MinimizeBox = False
        MaximizeBox = False
        ShowInTaskbar = False
        AutoScaleMode = AutoScaleMode.Font
        Font = New Font("Segoe UI", 9.5F)
        ClientSize = New Size(520, 400)

        _condition.Items.Add(Keep)
        _condition.Items.AddRange(Ui.Conditions.Select(Function(c) If(c = "", "Not set", c)).Cast(Of Object)().ToArray())
        _condition.SelectedIndex = 0
        _box.Items.Add(TakeOut)
        _box.Items.AddRange(boxes.Cast(Of Object)().ToArray())
        _format.Items.AddRange({"Single issue", "Trade Paperback", "Hardcover", "Omnibus", "Graphic Novel", "Magazine"})
        _read.Items.AddRange({Keep, "Read", "Not read"})
        _read.SelectedIndex = 0

        Dim fields As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 2, .Padding = New Padding(12, 8, 12, 0), .AutoScroll = True}
        fields.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        fields.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        Dim row = 0
        For Each pair In {Tuple.Create("Condition", CType(_condition, Control)), Tuple.Create("Value each ($)", CType(_value, Control)),
                          Tuple.Create("Date bought", CType(_bought, Control)), Tuple.Create("Box or shelf", CType(_box, Control)),
                          Tuple.Create("Format", CType(_format, Control)), Tuple.Create("Publisher", CType(_publisher, Control)),
                          Tuple.Create("Read", CType(_read, Control))}
            fields.RowStyles.Add(New RowStyle(SizeType.AutoSize))
            fields.Controls.Add(Ui.MakeLabel(pair.Item1), 0, row)
            fields.Controls.Add(pair.Item2, 1, row)
            row += 1
        Next
        Dim note As New Label With {.Dock = DockStyle.Top, .AutoSize = False, .Height = 40, .Padding = New Padding(12, 10, 12, 0), .Tag = Theme.MutedTag,
                                    .Text = "Fill in only what you want to change. Blank boxes stay as they are on each comic. Ctrl+Z undoes it afterwards."}
        Dim ok = Ui.MakeButton("Change them", AddressOf OnOk, Theme.PrimaryTag)
        Dim cancel = Ui.MakeButton("Cancel", Sub(s, e) DialogResult = DialogResult.Cancel)
        Dim buttons As New FlowLayoutPanel With {.Dock = DockStyle.Bottom, .FlowDirection = FlowDirection.RightToLeft, .AutoSize = True, .Padding = New Padding(6)}
        buttons.Controls.AddRange({cancel, ok})
        AcceptButton = ok
        CancelButton = cancel
        Controls.Add(fields)
        Controls.Add(note)
        Controls.Add(buttons)
        Theme.Apply(Me)
    End Sub

    Protected Overrides Sub OnHandleCreated(e As EventArgs)
        MyBase.OnHandleCreated(e)
        Theme.DarkTitleBar(Me)
    End Sub

    Private Sub OnOk(sender As Object, e As EventArgs)
        If _condition.SelectedIndex > 0 Then Changes.Condition = If(CStr(_condition.SelectedItem) = "Not set", "", CStr(_condition.SelectedItem))
        If _value.Text.Trim() <> "" Then
            Dim v As Double? = Nothing
            If Not Ui.ParseMoney(_value.Text, v) Then
                Ui.ShowError(Me, "Type the value as a number, like 15 or 15.50.")
                _value.Focus()
                Return
            End If
            Changes.ValueEach = v
        End If
        If _bought.Text.Trim() <> "" Then
            Dim d As Date
            If Not Date.TryParseExact(_bought.Text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, d) Then
                Ui.ShowError(Me, "Type the date bought like 2026-10-13 (year, month, day).")
                _bought.Focus()
                Return
            End If
            Changes.DateBought = d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
        End If
        If _box.Text.Trim() = TakeOut Then
            Changes.Box = ""
        ElseIf _box.Text.Trim() <> "" Then
            Changes.Box = _box.Text.Trim()
        End If
        If _format.Text.Trim() <> "" Then Changes.Format = If(_format.Text.Trim() = "Single issue", "", _format.Text.Trim())
        If _publisher.Text.Trim() <> "" Then Changes.Publisher = _publisher.Text.Trim()
        If _read.SelectedIndex > 0 Then Changes.IsRead = _read.SelectedIndex = 1
        DialogResult = If(Changes.IsEmpty, DialogResult.Cancel, DialogResult.OK)
    End Sub

End Class
