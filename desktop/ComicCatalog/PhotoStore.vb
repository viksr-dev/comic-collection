Option Strict On

Imports System.Drawing
Imports System.Drawing.Imaging
Imports System.Globalization
Imports System.IO
Imports System.Runtime.InteropServices

''' <summary>Copies your own photos of a comic into the Photos folder, turned the right way up and at a sensible size.</summary>
Friend Module PhotoStore

    ''' <summary>Longest side of a saved photo. Plenty to read a signature or check a corner.</summary>
    Private Const MaxSide As Integer = 2400

    Public Const FileFilter As String = "Photos (*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.heic)|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.heic|All files (*.*)|*.*"

    ''' <summary>
    ''' Saves a copy of a photo as a JPEG in folder, named after the comic. Returns the new file's name.
    ''' Throws InvalidDataException for a file Windows can't read (like an iPhone HEIC photo).
    ''' </summary>
    Public Function SaveCopy(source As String, folder As String, comicId As Long) As String
        Dim img As Image
        Try
            Using fs = File.OpenRead(source)
                img = Image.FromStream(fs)
            End Using
        Catch ex As Exception When TypeOf ex Is ArgumentException OrElse TypeOf ex Is OutOfMemoryException OrElse TypeOf ex Is ExternalException
            Throw New InvalidDataException(If(source.EndsWith(".heic", StringComparison.OrdinalIgnoreCase),
                $"{Path.GetFileName(source)} is an iPhone HEIC photo, which the app can't open. In iCloud for Windows, turn on " &
                """Download High Efficiency files as JPEG"", or on the iPhone choose Settings > Camera > Formats > Most Compatible.",
                $"{Path.GetFileName(source)} isn't a photo the app can open. JPEG and PNG work."), ex)
        End Try
        Using img
            TurnUpright(img)
            Dim scale = Math.Min(1.0, MaxSide / CDbl(Math.Max(img.Width, img.Height)))
            Dim w = Math.Max(1, CInt(img.Width * scale)), h = Math.Max(1, CInt(img.Height * scale))
            Using bmp As New Bitmap(w, h)
                Using g = Graphics.FromImage(bmp)
                    g.InterpolationMode = Drawing2D.InterpolationMode.HighQualityBicubic
                    g.Clear(Color.White)
                    g.DrawImage(img, 0, 0, w, h)
                End Using
                Directory.CreateDirectory(folder)
                Dim name = $"{comicId}-{DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture)}.jpg"
                Dim codec = ImageCodecInfo.GetImageEncoders().First(Function(c) c.MimeType = "image/jpeg")
                Using params As New EncoderParameters(1)
                    params.Param(0) = New EncoderParameter(Encoder.Quality, 88L)
                    bmp.Save(Path.Combine(folder, name), codec, params)
                End Using
                Return name
            End Using
        End Using
    End Function

    ''' <summary>Phone photos are stored sideways with a note saying which way is up; this applies it.</summary>
    Private Sub TurnUpright(img As Image)
        Const OrientationId As Integer = &H112
        If Not img.PropertyIdList.Contains(OrientationId) Then Return
        Dim value = img.GetPropertyItem(OrientationId).Value
        If value Is Nothing OrElse value.Length = 0 Then Return
        Select Case value(0)
            Case 2 : img.RotateFlip(RotateFlipType.RotateNoneFlipX)
            Case 3 : img.RotateFlip(RotateFlipType.Rotate180FlipNone)
            Case 4 : img.RotateFlip(RotateFlipType.Rotate180FlipX)
            Case 5 : img.RotateFlip(RotateFlipType.Rotate90FlipX)
            Case 6 : img.RotateFlip(RotateFlipType.Rotate90FlipNone)
            Case 7 : img.RotateFlip(RotateFlipType.Rotate270FlipX)
            Case 8 : img.RotateFlip(RotateFlipType.Rotate270FlipNone)
        End Select
    End Sub

    ''' <summary>A small copy for the edit window, or Nothing if the file is missing.</summary>
    Public Function Thumbnail(file As String, size As Size) As Image
        Try
            Using fs = IO.File.OpenRead(file), src = Image.FromStream(fs)
                Dim scale = Math.Min(size.Width / CDbl(src.Width), size.Height / CDbl(src.Height))
                Dim bmp As New Bitmap(Math.Max(1, CInt(src.Width * scale)), Math.Max(1, CInt(src.Height * scale)))
                Using g = Graphics.FromImage(bmp)
                    g.InterpolationMode = Drawing2D.InterpolationMode.HighQualityBicubic
                    g.DrawImage(src, 0, 0, bmp.Width, bmp.Height)
                End Using
                Return bmp
            End Using
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is ArgumentException OrElse TypeOf ex Is UnauthorizedAccessException
            Return Nothing
        End Try
    End Function

End Module
