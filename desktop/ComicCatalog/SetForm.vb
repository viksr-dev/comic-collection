Option Strict On

Imports System.Drawing
Imports System.Windows.Forms

''' <summary>Names a story arc or run (a new one or one you already have) and gives it one value.</summary>
Public Class SetForm
    Inherits Form

    Private ReadOnly _name As New ComboBox With {.Dock = DockStyle.Fill, .DropDownStyle = ComboBoxStyle.DropDown}
    Private ReadOnly _value As New TextBox With {.Dock = DockStyle.Fill, .PlaceholderText = "for the whole set; leave blank to keep values"}

    Public ReadOnly Property SetName As String
        Get
            Return _name.Text.Trim()
        End Get
    End Property

    Public Property TotalValue As Double?

    ''' <param name="existing">Names of sets you already have, offered in the list.</param>
    ''' <param name="comicCount">How many comics the set will hold, shown so you can check the selection.</param>
    Public Sub New(title As String, existing As IEnumerable(Of String), comicCount As Integer,
                   Optional name As String = "", Optional value As Double? = Nothing)
        Text = title
        StartPosition = FormStartPosition.CenterParent
        FormBorderStyle = FormBorderStyle.FixedDialog
        MinimizeBox = False
        MaximizeBox = False
        ShowInTaskbar = False
        AutoScaleMode = AutoScaleMode.Font
        Font = New Font("Segoe UI", 9.5F)
        ClientSize = New Size(480, 210)
        _name.Items.AddRange(existing.Cast(Of Object)().ToArray())
        _name.Text = name
        _value.Text = Ui.MoneyText(value)

        Dim grid As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 2, .Padding = New Padding(10)}
        grid.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        grid.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        grid.Controls.Add(Ui.MakeLabel("Story arc or run *"))
        grid.Controls.Add(_name)
        grid.Controls.Add(Ui.MakeLabel("Value of the set ($)"))
        grid.Controls.Add(_value)
        Dim note = Ui.MakeLabel($"{comicCount:N0} comic{If(comicCount = 1, "", "s")}. The value is split evenly between them.")
        note.Tag = Theme.MutedTag
        grid.Controls.Add(note)
        grid.SetColumnSpan(note, 2)

        Dim ok = Ui.MakeButton("Save", AddressOf OnOk, Theme.PrimaryTag)
        Dim cancel = Ui.MakeButton("Cancel", Sub(s, e) DialogResult = DialogResult.Cancel)
        Dim buttons As New FlowLayoutPanel With {.Dock = DockStyle.Bottom, .FlowDirection = FlowDirection.RightToLeft, .AutoSize = True, .Padding = New Padding(6)}
        buttons.Controls.AddRange({cancel, ok})
        AcceptButton = ok
        CancelButton = cancel
        Controls.Add(grid)
        Controls.Add(buttons)
        Theme.Apply(Me)
    End Sub

    Protected Overrides Sub OnHandleCreated(e As EventArgs)
        MyBase.OnHandleCreated(e)
        Theme.DarkTitleBar(Me)
    End Sub

    Private Sub OnOk(sender As Object, e As EventArgs)
        If SetName = "" Then
            Ui.ShowError(Me, "Give the set a name, like ""Batman: Year One"".")
            _name.Focus()
            Return
        End If
        Dim v As Double?
        If Not Ui.ParseMoney(_value.Text, v) Then
            Ui.ShowError(Me, "The value should be a number, like 400 or 400.00.")
            _value.Focus()
            Return
        End If
        TotalValue = v
        DialogResult = DialogResult.OK
    End Sub

End Class
