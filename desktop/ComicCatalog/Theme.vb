Option Strict On

Imports System.Drawing
Imports System.Runtime.InteropServices
Imports System.Windows.Forms

''' <summary>Dark look with a yellow accent, matching the phone app.</summary>
Friend Module Theme

    Public ReadOnly Bg As Color = Color.FromArgb(20, 22, 28)
    Public ReadOnly Panel As Color = Color.FromArgb(30, 33, 42)
    Public ReadOnly Panel2 As Color = Color.FromArgb(39, 43, 54)
    Public ReadOnly Text As Color = Color.FromArgb(242, 243, 245)
    Public ReadOnly Muted As Color = Color.FromArgb(163, 168, 181)
    Public ReadOnly Accent As Color = Color.FromArgb(255, 204, 51)
    Public ReadOnly AccentText As Color = Color.FromArgb(26, 26, 26)
    Public ReadOnly Danger As Color = Color.FromArgb(255, 107, 107)

    Public Const PrimaryTag As String = "primary"
    Public Const DangerTag As String = "danger"
    Public Const MutedTag As String = "muted"

    ''' <summary>Colours a window and everything in it.</summary>
    Public Sub Apply(root As Control)
        Style(root)
        For Each child As Control In root.Controls
            Apply(child)
        Next
    End Sub

    Private Sub Style(c As Control)
        Dim tag = TryCast(c.Tag, String)
        Select Case True
            Case TypeOf c Is Button
                Dim b = DirectCast(c, Button)
                b.FlatStyle = FlatStyle.Flat
                b.Cursor = Cursors.Hand
                b.FlatAppearance.BorderSize = 1
                If tag = PrimaryTag Then
                    b.BackColor = Accent
                    b.ForeColor = AccentText
                    b.FlatAppearance.BorderColor = Accent
                    b.FlatAppearance.MouseOverBackColor = Color.FromArgb(255, 216, 102)
                    b.Font = New Font(b.Font, FontStyle.Bold)
                Else
                    b.BackColor = Panel
                    b.ForeColor = If(tag = DangerTag, Danger, Text)
                    b.FlatAppearance.BorderColor = Panel2
                    b.FlatAppearance.MouseOverBackColor = Panel2
                End If
            Case TypeOf c Is TextBox
                Dim t = DirectCast(c, TextBox)
                t.BackColor = Panel
                t.ForeColor = Text
                t.BorderStyle = BorderStyle.FixedSingle
            Case TypeOf c Is ComboBox
                Dim cb = DirectCast(c, ComboBox)
                cb.FlatStyle = FlatStyle.Flat
                cb.BackColor = Panel
                cb.ForeColor = Text
            Case TypeOf c Is NumericUpDown, TypeOf c Is ListBox
                c.BackColor = Panel
                c.ForeColor = Text
                If TypeOf c Is ListBox Then DirectCast(c, ListBox).BorderStyle = BorderStyle.None
            Case TypeOf c Is ListView
                c.BackColor = Bg
                c.ForeColor = Text
                DirectCast(c, ListView).BorderStyle = BorderStyle.None
            Case TypeOf c Is DataGridView
                StyleGrid(DirectCast(c, DataGridView))
            Case TypeOf c Is Label
                If tag = MutedTag OrElse c.ForeColor = SystemColors.GrayText Then
                    c.ForeColor = Muted
                ElseIf c.ForeColor = SystemColors.ControlText OrElse c.ForeColor = Color.Empty Then
                    c.ForeColor = Text
                End If
            Case TypeOf c Is PictureBox, TypeOf c Is StatusStrip
                ' Keep their own colours.
            Case Else
                If c.BackColor = SystemColors.Control OrElse c.BackColor = Color.Transparent OrElse TypeOf c Is Form Then c.BackColor = Bg
                c.ForeColor = Text
        End Select
    End Sub

    Public Sub StyleGrid(g As DataGridView)
        g.BackgroundColor = Bg
        g.BorderStyle = BorderStyle.None
        g.GridColor = Panel2
        g.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal
        g.EnableHeadersVisualStyles = False
        g.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None
        g.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing
        g.ColumnHeadersHeight = 36
        g.RowTemplate.Height = 32
        With g.ColumnHeadersDefaultCellStyle
            .BackColor = Panel
            .ForeColor = Muted
            .SelectionBackColor = Panel
            .Font = New Font(g.Font, FontStyle.Bold)
            .Padding = New Padding(6, 0, 6, 0)
        End With
        With g.DefaultCellStyle
            .BackColor = Bg
            .ForeColor = Text
            .SelectionBackColor = Color.FromArgb(70, 60, 25)
            .SelectionForeColor = Accent
            .Padding = New Padding(6, 0, 6, 0)
        End With
        g.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(24, 26, 33)
    End Sub

    ' Dark title bar on Windows 10 (2004+) and Windows 11.
    <DllImport("dwmapi.dll")>
    Private Function DwmSetWindowAttribute(hwnd As IntPtr, attr As Integer, ByRef value As Integer, size As Integer) As Integer
    End Function

    Public Sub DarkTitleBar(f As Form)
        Try
            Dim on1 = 1
            DwmSetWindowAttribute(f.Handle, 20, on1, 4)
        Catch ex As Exception When TypeOf ex Is DllNotFoundException OrElse TypeOf ex Is EntryPointNotFoundException
        End Try
    End Sub

End Module
