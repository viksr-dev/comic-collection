Option Strict On

Imports System.Drawing
Imports System.Windows.Forms

''' <summary>Small window for lending comics to someone: who, when, and a note.</summary>
Public Class LendForm
    Inherits Form

    Private ReadOnly _who As New ComboBox With {.Dock = DockStyle.Fill, .DropDownStyle = ComboBoxStyle.DropDown}
    Private ReadOnly _when As New DateTimePicker With {.Dock = DockStyle.Fill, .Format = DateTimePickerFormat.Long}
    Private ReadOnly _notes As New TextBox With {.Dock = DockStyle.Fill, .PlaceholderText = "optional, e.g. back after the holidays"}

    Public ReadOnly Property Borrower As String
        Get
            Return _who.Text.Trim()
        End Get
    End Property
    Public ReadOnly Property LentOn As Date
        Get
            Return _when.Value.Date
        End Get
    End Property
    Public ReadOnly Property Notes As String
        Get
            Return _notes.Text.Trim()
        End Get
    End Property

    Public Sub New(what As String, knownBorrowers As IEnumerable(Of String))
        Text = "Lend comics"
        StartPosition = FormStartPosition.CenterParent
        FormBorderStyle = FormBorderStyle.FixedDialog
        MinimizeBox = False
        MaximizeBox = False
        ShowInTaskbar = False
        AutoScaleMode = AutoScaleMode.Font
        Font = New Font("Segoe UI", 9.5F)
        ClientSize = New Size(460, 230)
        _who.Items.AddRange(knownBorrowers.Cast(Of Object)().ToArray())
        _when.Value = Date.Today

        Dim heading As New Label With {.Text = what, .Dock = DockStyle.Top, .AutoSize = False, .Height = 40, .Padding = New Padding(12, 10, 12, 0),
                                       .AutoEllipsis = True}
        Dim grid As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 2, .Padding = New Padding(10)}
        grid.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        grid.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        For Each pair In {("Lent to *", CType(_who, Control)), ("On", _when), ("Notes", _notes)}
            grid.Controls.Add(Ui.MakeLabel(pair.Item1))
            grid.Controls.Add(pair.Item2)
        Next
        Dim ok = Ui.MakeButton("Lend", AddressOf OnOk, Theme.PrimaryTag)
        Dim cancel = Ui.MakeButton("Cancel", Sub(s, e) DialogResult = DialogResult.Cancel)
        Dim buttons As New FlowLayoutPanel With {.Dock = DockStyle.Bottom, .FlowDirection = FlowDirection.RightToLeft, .AutoSize = True, .Padding = New Padding(6)}
        buttons.Controls.AddRange({cancel, ok})
        AcceptButton = ok
        CancelButton = cancel
        Controls.Add(grid)
        Controls.Add(heading)
        Controls.Add(buttons)
        Theme.Apply(Me)
    End Sub

    Protected Overrides Sub OnHandleCreated(e As EventArgs)
        MyBase.OnHandleCreated(e)
        Theme.DarkTitleBar(Me)
    End Sub

    Private Sub OnOk(sender As Object, e As EventArgs)
        If Borrower = "" Then
            Ui.ShowError(Me, "Who are you lending it to?")
            _who.Focus()
            Return
        End If
        DialogResult = DialogResult.OK
    End Sub

End Class
