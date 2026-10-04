Option Strict On

Imports System.Drawing
Imports System.Globalization
Imports System.Windows.Forms

''' <summary>Small helpers for building the screens in code.</summary>
Friend Module Ui

    Public ReadOnly Conditions As String() = {"", "Mint", "Near Mint", "Very Fine", "Fine", "Very Good", "Good", "Fair", "Poor"}

    Public Function MakeButton(text As String, onClick As EventHandler, Optional style As String = Nothing) As Button
        Dim b As New Button With {.Text = text, .AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .Tag = style,
                                  .MinimumSize = New Size(0, 34), .Padding = New Padding(10, 2, 10, 2), .Margin = New Padding(3, 3, 3, 3)}
        AddHandler b.Click, onClick
        Return b
    End Function

    Public Function MakeLabel(text As String) As Label
        Return New Label With {.Text = text, .AutoSize = True, .Anchor = AnchorStyles.Left, .Margin = New Padding(3, 8, 3, 3)}
    End Function

    Public Function MakeGrid() As DataGridView
        Dim g As New DataGridView With {
            .Dock = DockStyle.Fill, .ReadOnly = True, .AllowUserToAddRows = False, .AllowUserToDeleteRows = False,
            .AllowUserToResizeRows = False, .SelectionMode = DataGridViewSelectionMode.FullRowSelect, .MultiSelect = True,
            .RowHeadersVisible = False, .AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            .BackgroundColor = SystemColors.Window, .BorderStyle = BorderStyle.None}
        g.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(246, 247, 250)
        Return g
    End Function

    ''' <summary>Reads a price typed as "12", "12.50" or "$12.50". Blank means not set.</summary>
    Public Function ParseMoney(text As String, ByRef value As Double?) As Boolean
        Dim t = If(text, "").Trim().TrimStart("$"c).Replace(",", "")
        If t = "" Then
            value = Nothing
            Return True
        End If
        Dim d As Double
        If Double.TryParse(t, NumberStyles.Number, CultureInfo.InvariantCulture, d) AndAlso d >= 0 Then
            value = d
            Return True
        End If
        Return False
    End Function

    Public Function MoneyText(value As Double?) As String
        Return If(value.HasValue, value.Value.ToString("0.00", CultureInfo.InvariantCulture), "")
    End Function

    Public Sub ShowError(owner As IWin32Window, message As String)
        MessageBox.Show(owner, message, "Comic Catalog", MessageBoxButtons.OK, MessageBoxIcon.Warning)
    End Sub

End Module
