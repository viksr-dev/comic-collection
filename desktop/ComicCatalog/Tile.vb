Option Strict On

Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Windows.Forms

''' <summary>
''' A picture card: a cover (or video thumbnail) with a title under it, and optionally a line of detail,
''' a reading progress bar and a badge like "NEW". Lights up under the mouse.
''' </summary>
Friend Class Tile
    Inherits Control

    Private _picture As Image
    Private _hover As Boolean
    Private ReadOnly _pictureSize As Size

    Public Property Caption As String = ""
    Public Property Detail As String = ""
    ''' <summary>A short word in a yellow tag on the corner, e.g. "NEW". Blank for none.</summary>
    Public Property Badge As String = ""
    ''' <summary>0 to 1 for a progress bar under the picture; below 0 for none.</summary>
    Public Property Progress As Double = -1
    ''' <summary>Whatever the tile stands for (a comic id, a video, a file).</summary>
    Public Property Item As Object

    Public Sub New(pictureSize As Size, Optional placeholderText As String = "")
        _pictureSize = pictureSize
        SetStyle(ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer Or ControlStyles.UserPaint Or
                 ControlStyles.ResizeRedraw Or ControlStyles.SupportsTransparentBackColor, True)
        Size = New Size(pictureSize.Width + 12, pictureSize.Height + 66)
        Margin = New Padding(0, 0, 10, 10)
        Cursor = Cursors.Hand
        BackColor = Theme.Bg
        ForeColor = Theme.Text
        Font = New Font("Segoe UI Semibold", 9.5F)
        PlaceholderText = placeholderText
    End Sub

    Public Property PlaceholderText As String

    ''' <summary>The picture, drawn to fit. The tile doesn't own it unless OwnsPicture is set.</summary>
    Public Property Picture As Image
        Get
            Return _picture
        End Get
        Set(value As Image)
            If OwnsPicture AndAlso _picture IsNot Nothing AndAlso _picture IsNot value Then _picture.Dispose()
            _picture = value
            Invalidate()
        End Set
    End Property

    Public Property OwnsPicture As Boolean

    Protected Overrides Sub OnMouseEnter(e As EventArgs)
        MyBase.OnMouseEnter(e)
        _hover = True
        Invalidate()
    End Sub

    Protected Overrides Sub OnMouseLeave(e As EventArgs)
        MyBase.OnMouseLeave(e)
        _hover = False
        Invalidate()
    End Sub

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        Dim g = e.Graphics
        g.SmoothingMode = SmoothingMode.AntiAlias
        g.InterpolationMode = InterpolationMode.HighQualityBicubic
        g.Clear(If(Parent IsNot Nothing, Parent.BackColor, Theme.Bg))
        If _hover Then
            Using b As New SolidBrush(Theme.Panel2), path = Rounded(New Rectangle(0, 0, Width - 1, Height - 1), 8)
                g.FillPath(b, path)
            End Using
        End If
        Dim pic As New Rectangle(6, 6, _pictureSize.Width, _pictureSize.Height)
        Using b As New SolidBrush(Theme.Panel), path = Rounded(pic, 6)
            g.FillPath(b, path)
        End Using
        If _picture IsNot Nothing Then
            Dim scale = Math.Min(pic.Width / CDbl(_picture.Width), pic.Height / CDbl(_picture.Height))
            Dim w = CInt(_picture.Width * scale), h = CInt(_picture.Height * scale)
            Dim target As New Rectangle(pic.X + (pic.Width - w) \ 2, pic.Y + (pic.Height - h) \ 2, w, h)
            Using path = Rounded(target, 6)
                Dim saved = g.Clip
                g.SetClip(path)
                g.DrawImage(_picture, target)
                g.Clip = saved
            End Using
        ElseIf PlaceholderText <> "" Then
            Using f As New Font("Segoe UI", 9), b As New SolidBrush(Theme.Muted),
                  sf As New StringFormat With {.Alignment = StringAlignment.Center, .LineAlignment = StringAlignment.Center}
                g.DrawString(PlaceholderText, f, b, pic, sf)
            End Using
        End If
        If _hover Then
            Using p As New Pen(Theme.Accent, 2), path = Rounded(pic, 6)
                g.DrawPath(p, path)
            End Using
        End If
        If Progress >= 0 Then
            Dim bar As New Rectangle(pic.X + 8, pic.Bottom - 12, pic.Width - 16, 5)
            Using bg As New SolidBrush(Color.FromArgb(200, 20, 22, 28)), fg As New SolidBrush(Theme.Accent)
                g.FillRectangle(bg, bar)
                g.FillRectangle(fg, New Rectangle(bar.X, bar.Y, CInt(bar.Width * Math.Min(1, Progress)), bar.Height))
            End Using
        End If
        If Badge <> "" Then
            Using f As New Font("Segoe UI", 7.5F, FontStyle.Bold)
                Dim size = g.MeasureString(Badge, f)
                Dim r As New RectangleF(pic.X + 6, pic.Y + 6, size.Width + 10, size.Height + 4)
                Using b As New SolidBrush(Theme.Accent), t As New SolidBrush(Theme.AccentText), path = Rounded(Rectangle.Round(r), 4)
                    g.FillPath(b, path)
                    g.DrawString(Badge, f, t, r.X + 5, r.Y + 2)
                End Using
            End Using
        End If
        Dim textArea As New RectangleF(6, pic.Bottom + 6, pic.Width, 36)
        Using sf As New StringFormat With {.Trimming = StringTrimming.EllipsisCharacter}, b As New SolidBrush(If(_hover, Theme.Accent, Theme.Text))
            g.DrawString(Caption, Font, b, textArea, sf)
        End Using
        If Detail <> "" Then
            Using f As New Font("Segoe UI", 8.5F), b As New SolidBrush(Theme.Muted),
                  sf As New StringFormat With {.Trimming = StringTrimming.EllipsisCharacter, .FormatFlags = StringFormatFlags.NoWrap}
                g.DrawString(Detail, f, b, New RectangleF(6, pic.Bottom + 42, pic.Width, 18), sf)
            End Using
        End If
    End Sub

    Private Shared Function Rounded(r As Rectangle, radius As Integer) As GraphicsPath
        Dim path As New GraphicsPath()
        Dim d = radius * 2
        If r.Width < d OrElse r.Height < d Then
            path.AddRectangle(r)
            Return path
        End If
        path.AddArc(r.X, r.Y, d, d, 180, 90)
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90)
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90)
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90)
        path.CloseFigure()
        Return path
    End Function

    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing AndAlso OwnsPicture AndAlso _picture IsNot Nothing Then
            _picture.Dispose()
            _picture = Nothing
        End If
        MyBase.Dispose(disposing)
    End Sub

End Class

''' <summary>Small icons from the Windows icon font (Segoe Fluent Icons on Windows 11, Segoe MDL2 Assets on Windows 10).</summary>
Friend Module Icons

    Public Const Home As Char = ChrW(&HE80F)
    Public Const Library As Char = ChrW(&HE8F1)
    Public Const Book As Char = ChrW(&HE82D)
    Public Const Reading As Char = ChrW(&HE736)
    Public Const Video As Char = ChrW(&HE714)
    Public Const Chart As Char = ChrW(&HE9D2)
    Public Const Search As Char = ChrW(&HE721)
    Public Const Star As Char = ChrW(&HE734)
    Public Const List As Char = ChrW(&HE8FD)
    Public Const Calendar As Char = ChrW(&HE787)
    Public Const People As Char = ChrW(&HE716)
    Public Const Settings As Char = ChrW(&HE713)
    Public Const Add As Char = ChrW(&HE710)
    Public Const Edit As Char = ChrW(&HE70F)
    Public Const Delete As Char = ChrW(&HE74D)
    Public Const Pictures As Char = ChrW(&HE8B9)
    Public Const Folder As Char = ChrW(&HE8B7)
    Public Const Play As Char = ChrW(&HE768)
    Public Const Refresh As Char = ChrW(&HE72C)
    Public Const Save As Char = ChrW(&HE74E)
    Public Const Share As Char = ChrW(&HE72D)
    Public Const More As Char = ChrW(&HE712)
    Public Const Check As Char = ChrW(&HE73E)

    Private _family As FontFamily
    Private _looked As Boolean

    ''' <summary>The icon font, or Nothing on a Windows without one (then buttons just have words).</summary>
    Private ReadOnly Property Family As FontFamily
        Get
            If Not _looked Then
                _looked = True
                Using installed As New Drawing.Text.InstalledFontCollection()
                    For Each name In {"Segoe Fluent Icons", "Segoe MDL2 Assets"}
                        _family = installed.Families.FirstOrDefault(Function(f) f.Name = name)
                        If _family IsNot Nothing Then Exit For
                    Next
                End Using
            End If
            Return _family
        End Get
    End Property

    ''' <summary>An icon drawn as a little picture, or Nothing if Windows has no icon font.</summary>
    Public Function Make(glyph As Char, color As Color, Optional pixels As Integer = 18) As Image
        If Family Is Nothing Then Return Nothing
        Dim bmp As New Bitmap(pixels, pixels)
        Using g = Graphics.FromImage(bmp), f As New Font(Family, pixels * 0.72F, GraphicsUnit.Pixel), b As New SolidBrush(color),
              sf As New StringFormat With {.Alignment = StringAlignment.Center, .LineAlignment = StringAlignment.Center}
            g.TextRenderingHint = Drawing.Text.TextRenderingHint.AntiAliasGridFit
            g.DrawString(glyph.ToString(), f, b, New RectangleF(0, 0, pixels, pixels), sf)
        End Using
        Return bmp
    End Function

    ''' <summary>Puts an icon before a button's words.</summary>
    Public Function Put(b As ButtonBase, glyph As Char, Optional color As Color = Nothing) As ButtonBase
        Dim c = If(color.IsEmpty, If(TryCast(b.Tag, String) = Theme.PrimaryTag, Theme.AccentText, Theme.Text), color)
        Dim img = Make(glyph, c)
        If img Is Nothing Then Return b
        b.Image = img
        b.ImageAlign = ContentAlignment.MiddleLeft
        b.TextImageRelation = TextImageRelation.ImageBeforeText
        Return b
    End Function

End Module
