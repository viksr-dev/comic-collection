Option Strict On

Imports System.Drawing
Imports System.Windows.Forms

''' <summary>Small window for adding a comic to the wishlist.</summary>
Public Class WishForm
    Inherits Form

    Private ReadOnly _series As New TextBox With {.Dock = DockStyle.Fill}
    Private ReadOnly _issue As New TextBox With {.Dock = DockStyle.Fill}
    Private ReadOnly _priority As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList, .Dock = DockStyle.Fill}
    Private ReadOnly _maxPriceBox As New TextBox With {.Dock = DockStyle.Fill, .PlaceholderText = "optional"}
    Private ReadOnly _notes As New TextBox With {.Dock = DockStyle.Fill}

    Public ReadOnly Property SeriesName As String
        Get
            Return _series.Text.Trim()
        End Get
    End Property
    Public ReadOnly Property IssueNumber As String
        Get
            Return _issue.Text.Trim()
        End Get
    End Property
    Public ReadOnly Property Priority As Integer
        Get
            Return _priority.SelectedIndex + 1
        End Get
    End Property
    Public Property MaxPrice As Double?
    Public ReadOnly Property Notes As String
        Get
            Return _notes.Text.Trim()
        End Get
    End Property

    Public Sub New()
        Text = "Add to wishlist"
        StartPosition = FormStartPosition.CenterParent
        FormBorderStyle = FormBorderStyle.FixedDialog
        MinimizeBox = False
        MaximizeBox = False
        ShowInTaskbar = False
        AutoScaleMode = AutoScaleMode.Font
        Font = New Font("Segoe UI", 9.5F)
        ClientSize = New Size(420, 250)
        _priority.Items.AddRange({"1 - must have", "2 - want", "3 - nice to have"})
        _priority.SelectedIndex = 1

        Dim grid As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 2, .Padding = New Padding(10)}
        grid.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        grid.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        For Each pair In {("Series *", CType(_series, Control)), ("Issue #", _issue), ("Priority", _priority), ("Max price ($)", _maxPriceBox), ("Notes", _notes)}
            grid.Controls.Add(Ui.MakeLabel(pair.Item1))
            grid.Controls.Add(pair.Item2)
        Next
        Dim ok = Ui.MakeButton("Add", AddressOf OnOk)
        Dim cancel = Ui.MakeButton("Cancel", Sub(s, e) DialogResult = DialogResult.Cancel)
        Dim buttons As New FlowLayoutPanel With {.Dock = DockStyle.Bottom, .FlowDirection = FlowDirection.RightToLeft, .AutoSize = True, .Padding = New Padding(6)}
        buttons.Controls.AddRange({cancel, ok})
        AcceptButton = ok
        CancelButton = cancel
        Controls.Add(grid)
        Controls.Add(buttons)
    End Sub

    Private Sub OnOk(sender As Object, e As EventArgs)
        If SeriesName = "" Then
            Ui.ShowError(Me, "Please fill in the series name.")
            _series.Focus()
            Return
        End If
        Dim price As Double?
        If Not Ui.ParseMoney(_maxPriceBox.Text, price) Then
            Ui.ShowError(Me, "Max price should be a number, like 15 or 15.00.")
            _maxPriceBox.Focus()
            Return
        End If
        MaxPrice = price
        DialogResult = DialogResult.OK
    End Sub

End Class
