Option Strict On

Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.IO
Imports System.Net.Http
Imports System.Threading
Imports ComicCatalog.Data

''' <summary>Downloads comic covers once and keeps them in Documents\Comic Catalog\covers.</summary>
Friend Class CoverCache

    Private Shared ReadOnly Http As New HttpClient With {.Timeout = TimeSpan.FromSeconds(20)}
    Private Shared ReadOnly Gate As New SemaphoreSlim(4)
    Private ReadOnly _folder As String = Path.Combine(AppSettings.Folder, "covers")

    Public Shared ReadOnly ThumbSize As New Size(110, 165)

    ''' <summary>A thumbnail of the cover, or Nothing if there isn't one.</summary>
    Public Async Function GetThumbnailAsync(comicId As Long, url As String, Optional size As Size = Nothing) As Task(Of Image)
        Dim data = Await GetBytesAsync(comicId, url)
        If data Is Nothing Then Return Nothing
        Dim box = If(size.IsEmpty, ThumbSize, size)
        Try
            Return Await Task.Run(Function() MakeThumb(data, box))
        Catch ex As ArgumentException
            Return Nothing
        End Try
    End Function

    ''' <summary>The cover at full size, or Nothing if there isn't one.</summary>
    Public Async Function GetImageAsync(comicId As Long, url As String) As Task(Of Image)
        Dim data = Await GetBytesAsync(comicId, url)
        If data Is Nothing Then Return Nothing
        Try
            Using ms As New MemoryStream(data), src = Image.FromStream(ms)
                Return New Bitmap(src)
            End Using
        Catch ex As ArgumentException
            Return Nothing
        End Try
    End Function

    ' Downloaded the first time, then read from the covers folder.
    Private Async Function GetBytesAsync(comicId As Long, url As String) As Task(Of Byte())
        If String.IsNullOrWhiteSpace(url) Then Return Nothing
        Dim file = Path.Combine(_folder, $"{comicId}.img")
        Try
            If Not IO.File.Exists(file) Then
                Await Gate.WaitAsync()
                Try
                    Dim bytes = Await Http.GetByteArrayAsync(url)
                    Directory.CreateDirectory(_folder)
                    Await IO.File.WriteAllBytesAsync(file, bytes)
                Finally
                    Gate.Release()
                End Try
            End If
            Return Await IO.File.ReadAllBytesAsync(file)
        Catch ex As Exception When TypeOf ex Is HttpRequestException OrElse TypeOf ex Is IOException OrElse
                                   TypeOf ex Is TaskCanceledException OrElse TypeOf ex Is ArgumentException OrElse
                                   TypeOf ex Is InvalidOperationException
            Return Nothing
        End Try
    End Function

    Private Shared Function MakeThumb(data As Byte(), size As Size) As Image
        Using ms As New MemoryStream(data), src = Image.FromStream(ms)
            Return Fit(src, size)
        End Using
    End Function

    ''' <summary>Draws a picture centred on a card-sized canvas (the cover thumbnail size unless another is given).</summary>
    Public Shared Function Fit(src As Image, Optional size As Size = Nothing) As Image
        If size.IsEmpty Then size = ThumbSize
        Dim bmp As New Bitmap(size.Width, size.Height)
        Using g = Graphics.FromImage(bmp)
            g.Clear(Theme.Panel2)
            g.InterpolationMode = InterpolationMode.HighQualityBicubic
            g.PixelOffsetMode = PixelOffsetMode.HighQuality
            Dim scale = Math.Min(size.Width / CDbl(src.Width), size.Height / CDbl(src.Height))
            Dim w = CInt(src.Width * scale), h = CInt(src.Height * scale)
            g.DrawImage(src, (size.Width - w) \ 2, (size.Height - h) \ 2, w, h)
        End Using
        Return bmp
    End Function

    ''' <summary>Grey card shown while a cover loads, or when there isn't one.</summary>
    Public Shared Function Placeholder(Optional size As Size = Nothing, Optional text As String = "No cover") As Image
        If size.IsEmpty Then size = ThumbSize
        Dim bmp As New Bitmap(size.Width, size.Height)
        Using g = Graphics.FromImage(bmp), f As New Font("Segoe UI", 9), b As New SolidBrush(Theme.Muted)
            g.Clear(Theme.Panel2)
            Dim sf As New StringFormat With {.Alignment = StringAlignment.Center, .LineAlignment = StringAlignment.Center}
            g.DrawString(text, f, b, New RectangleF(0, 0, size.Width, size.Height), sf)
        End Using
        Return bmp
    End Function

    ''' <summary>Cover sizes for the Covers view.</summary>
    Public Shared ReadOnly Sizes As Size() = {New Size(110, 165), New Size(150, 225), New Size(200, 300)}

End Class
