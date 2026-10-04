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
    Public Async Function GetThumbnailAsync(comicId As Long, url As String) As Task(Of Image)
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
            Dim data = Await IO.File.ReadAllBytesAsync(file)
            Return Await Task.Run(Function() MakeThumb(data))
        Catch ex As Exception When TypeOf ex Is HttpRequestException OrElse TypeOf ex Is IOException OrElse
                                   TypeOf ex Is TaskCanceledException OrElse TypeOf ex Is ArgumentException
            Return Nothing
        End Try
    End Function

    Private Shared Function MakeThumb(data As Byte()) As Image
        Using ms As New MemoryStream(data), src = Image.FromStream(ms)
            Return Fit(src)
        End Using
    End Function

    ''' <summary>Draws a picture centred on a card-sized canvas.</summary>
    Public Shared Function Fit(src As Image) As Image
        Dim bmp As New Bitmap(ThumbSize.Width, ThumbSize.Height)
        Using g = Graphics.FromImage(bmp)
            g.Clear(Theme.Panel2)
            g.InterpolationMode = InterpolationMode.HighQualityBicubic
            Dim scale = Math.Min(ThumbSize.Width / CDbl(src.Width), ThumbSize.Height / CDbl(src.Height))
            Dim w = CInt(src.Width * scale), h = CInt(src.Height * scale)
            g.DrawImage(src, (ThumbSize.Width - w) \ 2, (ThumbSize.Height - h) \ 2, w, h)
        End Using
        Return bmp
    End Function

    ''' <summary>Grey card shown while a cover loads, or when there isn't one.</summary>
    Public Shared Function Placeholder() As Image
        Dim bmp As New Bitmap(ThumbSize.Width, ThumbSize.Height)
        Using g = Graphics.FromImage(bmp), f As New Font("Segoe UI", 9), b As New SolidBrush(Theme.Muted)
            g.Clear(Theme.Panel2)
            Dim sf As New StringFormat With {.Alignment = StringAlignment.Center, .LineAlignment = StringAlignment.Center}
            g.DrawString("No cover", f, b, New RectangleF(0, 0, ThumbSize.Width, ThumbSize.Height), sf)
        End Using
        Return bmp
    End Function

End Class
