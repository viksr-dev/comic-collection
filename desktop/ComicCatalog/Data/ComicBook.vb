Option Strict On

Imports System.IO
Imports System.IO.Compression
Imports System.Text.RegularExpressions
Imports System.Xml.Linq
Imports Docnet.Core
Imports Docnet.Core.Models
Imports SharpCompress.Archives

Namespace Data

    ''' <summary>One page, either as a picture file (JPEG, PNG…) or as raw pixels (from a PDF).</summary>
    Public Class PageImage
        ''' <summary>The picture file's bytes, or Nothing for raw pixels.</summary>
        Public Property Encoded As Byte()
        ''' <summary>Raw pixels, 4 bytes each (blue, green, red, alpha), row by row. Nothing for a picture file.</summary>
        Public Property Bgra As Byte()
        Public Property Width As Integer
        Public Property Height As Integer
    End Class

    ''' <summary>
    ''' A digital comic: CBZ, CBR, CB7, PDF or EPUB. The file's contents decide how it's read, not its name,
    ''' so a .cbr that's really a zip (common) still opens. Safe to use from more than one thread.
    ''' </summary>
    Public MustInherit Class ComicBook
        Implements IDisposable

        Protected ReadOnly Gate As New Object()

        Public MustOverride ReadOnly Property PageCount As Integer
        Protected MustOverride Function ReadPage(index As Integer) As PageImage

        Public ReadOnly Property FilePath As String
        Public ReadOnly Property Kind As String

        Protected Sub New(path As String, kind As String)
            FilePath = path
            Me.Kind = kind
        End Sub

        Public Function GetPage(index As Integer) As PageImage
            If index < 0 OrElse index >= PageCount Then Throw New ArgumentOutOfRangeException(NameOf(index))
            SyncLock Gate
                Return ReadPage(index)
            End SyncLock
        End Function

        Public Shared ReadOnly Extensions As String() = {".cbz", ".cbr", ".cb7", ".pdf", ".epub", ".zip", ".rar", ".7z"}

        Public Shared ReadOnly Property FileFilter As String
            Get
                Return "Comic files (*.cbz;*.cbr;*.cb7;*.pdf;*.epub)|*.cbz;*.cbr;*.cb7;*.pdf;*.epub;*.zip;*.rar;*.7z|All files (*.*)|*.*"
            End Get
        End Property

        ''' <summary>Opens a comic file. Throws InvalidDataException if it isn't one, or has no pages.</summary>
        Public Shared Function Open(path As String) As ComicBook
            Dim head(7) As Byte
            Using f = File.OpenRead(path)
                f.ReadAtLeast(head, 8, throwOnEndOfStream:=False)
            End Using
            Dim book As ComicBook
            If head(0) = &H25 AndAlso head(1) = &H50 AndAlso head(2) = &H44 AndAlso head(3) = &H46 Then        ' %PDF
                book = New PdfBook(path)
            ElseIf head(0) = &H50 AndAlso head(1) = &H4B Then                                                    ' PK (zip: CBZ or EPUB)
                book = ZipBook.OpenZip(path)
            ElseIf (head(0) = &H52 AndAlso head(1) = &H61 AndAlso head(2) = &H72 AndAlso head(3) = &H21) OrElse  ' Rar!
                   (head(0) = &H37 AndAlso head(1) = &H7A AndAlso head(2) = &HBC AndAlso head(3) = &HAF) Then     ' 7z
                book = New ArchiveBook(path)
            Else
                Throw New InvalidDataException("That doesn't look like a comic file (CBZ, CBR, PDF or EPUB).")
            End If
            If book.PageCount = 0 Then
                book.Dispose()
                Throw New InvalidDataException("No pages found in that file.")
            End If
            Return book
        End Function

        Protected Shared ReadOnly ImageExtensions As String() = {".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp"}

        Protected Shared Function IsImageName(name As String) As Boolean
            Dim fileName = Path.GetFileName(name)
            If fileName.StartsWith(".", StringComparison.Ordinal) OrElse name.Contains("__MACOSX", StringComparison.OrdinalIgnoreCase) Then Return False
            Return ImageExtensions.Contains(Path.GetExtension(name).ToLowerInvariant())
        End Function

        ''' <summary>Sorts "page2.jpg" before "page10.jpg", folders first by name.</summary>
        Public Shared Function NaturalOrder(names As IEnumerable(Of String)) As List(Of String)
            Return names.OrderBy(Function(n) Regex.Replace(n.Replace("\"c, "/"c).ToLowerInvariant(), "\d+", Function(m) m.Value.PadLeft(10, "0"c)), StringComparer.Ordinal).ToList()
        End Function

        Protected Shared Function ReadAll(s As Stream) As Byte()
            Using ms As New MemoryStream()
                s.CopyTo(ms)
                Return ms.ToArray()
            End Using
        End Function

        Public Sub Dispose() Implements IDisposable.Dispose
            SyncLock Gate
                Close()
            End SyncLock
            GC.SuppressFinalize(Me)
        End Sub

        Protected MustOverride Sub Close()
    End Class

    ''' <summary>CBZ (a zip of pictures) or EPUB (pictures in the book's reading order).</summary>
    Friend Class ZipBook
        Inherits ComicBook

        Private ReadOnly _zip As ZipArchive
        Private ReadOnly _pages As List(Of ZipArchiveEntry)

        Private Sub New(path As String, kind As String, zip As ZipArchive, pages As List(Of ZipArchiveEntry))
            MyBase.New(path, kind)
            _zip = zip
            _pages = pages
        End Sub

        Friend Shared Function OpenZip(path As String) As ZipBook
            Dim zip = ZipFile.OpenRead(path)
            Try
                Dim byName = zip.Entries.Where(Function(e) e.Length > 0).GroupBy(Function(e) e.FullName.Replace("\"c, "/"c), StringComparer.OrdinalIgnoreCase).
                    ToDictionary(Function(g) g.Key, Function(g) g.First(), StringComparer.OrdinalIgnoreCase)
                If byName.ContainsKey("META-INF/container.xml") Then
                    Dim spine = EpubPages(byName)
                    If spine.Count > 0 Then Return New ZipBook(path, "EPUB", zip, spine)
                End If
                Dim images = NaturalOrder(byName.Keys.Where(Function(n) IsImageName(n))).Select(Function(n) byName(n)).ToList()
                Return New ZipBook(path, If(byName.ContainsKey("META-INF/container.xml"), "EPUB", "CBZ"), zip, images)
            Catch
                zip.Dispose()
                Throw
            End Try
        End Function

        ' The pictures in an EPUB, in reading order: each spine page's pictures, or the spine item itself when it's a picture.
        Private Shared Function EpubPages(byName As Dictionary(Of String, ZipArchiveEntry)) As List(Of ZipArchiveEntry)
            Dim pages As New List(Of ZipArchiveEntry)
            Dim seen As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            Try
                Dim container = LoadXml(byName("META-INF/container.xml"))
                Dim opfPath = container.Descendants().FirstOrDefault(Function(e) e.Name.LocalName = "rootfile")?.Attribute("full-path")?.Value
                If opfPath Is Nothing OrElse Not byName.ContainsKey(opfPath) Then Return pages
                Dim opf = LoadXml(byName(opfPath))
                Dim baseDir = If(Path.GetDirectoryName(opfPath), "").Replace("\"c, "/"c)
                Dim manifest = opf.Descendants().Where(Function(e) e.Name.LocalName = "item").
                    Where(Function(e) e.Attribute("id") IsNot Nothing AndAlso e.Attribute("href") IsNot Nothing).
                    GroupBy(Function(e) e.Attribute("id").Value).ToDictionary(Function(g) g.Key, Function(g) Resolve(baseDir, g.First().Attribute("href").Value))
                For Each itemref In opf.Descendants().Where(Function(e) e.Name.LocalName = "itemref")
                    Dim href As String = Nothing
                    If Not manifest.TryGetValue(If(itemref.Attribute("idref")?.Value, ""), href) OrElse Not byName.ContainsKey(href) Then Continue For
                    If IsImageName(href) Then
                        If seen.Add(href) Then pages.Add(byName(href))
                        Continue For
                    End If
                    ' A page of the book: the pictures it shows (<img src> or SVG <image href>).
                    Dim html As String
                    Using r As New StreamReader(byName(href).Open())
                        html = r.ReadToEnd()
                    End Using
                    Dim pageDir = If(Path.GetDirectoryName(href), "").Replace("\"c, "/"c)
                    For Each m As Match In Regex.Matches(html, "<(?:img|image)\b[^>]*?\b(?:src|xlink:href|href)\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase)
                        Dim pic = Resolve(pageDir, m.Groups(1).Value)
                        If byName.ContainsKey(pic) AndAlso IsImageName(pic) AndAlso seen.Add(pic) Then pages.Add(byName(pic))
                    Next
                Next
            Catch ex As Exception When TypeOf ex Is Xml.XmlException OrElse TypeOf ex Is InvalidDataException
            End Try
            Return pages
        End Function

        Private Shared Function LoadXml(entry As ZipArchiveEntry) As XDocument
            Using s = entry.Open()
                Return XDocument.Load(s)
            End Using
        End Function

        ' "OEBPS/Text" + "../Images/p1.jpg" → "OEBPS/Images/p1.jpg"
        Private Shared Function Resolve(baseDir As String, href As String) As String
            Dim clean = Uri.UnescapeDataString(href.Split("#"c)(0))
            Dim parts As New List(Of String)(If(baseDir = "", Array.Empty(Of String)(), baseDir.Split("/"c)))
            For Each part In clean.Split("/"c)
                If part = ".." Then
                    If parts.Count > 0 Then parts.RemoveAt(parts.Count - 1)
                ElseIf part <> "." AndAlso part <> "" Then
                    parts.Add(part)
                End If
            Next
            Return String.Join("/", parts)
        End Function

        Public Overrides ReadOnly Property PageCount As Integer
            Get
                Return _pages.Count
            End Get
        End Property

        Protected Overrides Function ReadPage(index As Integer) As PageImage
            Using s = _pages(index).Open()
                Return New PageImage With {.Encoded = ReadAll(s)}
            End Using
        End Function

        Protected Overrides Sub Close()
            _zip.Dispose()
        End Sub
    End Class

    ''' <summary>CBR (RAR) or CB7 (7-Zip).</summary>
    Friend Class ArchiveBook
        Inherits ComicBook

        Private ReadOnly _archive As IArchive
        Private ReadOnly _pages As List(Of IArchiveEntry)

        Friend Sub New(path As String)
            MyBase.New(path, If(path.EndsWith(".cb7", StringComparison.OrdinalIgnoreCase) OrElse path.EndsWith(".7z", StringComparison.OrdinalIgnoreCase), "CB7", "CBR"))
            _archive = ArchiveFactory.OpenArchive(path, Nothing)
            Dim byName = _archive.Entries.Where(Function(e) Not e.IsDirectory AndAlso e.Key IsNot Nothing AndAlso IsImageName(e.Key)).
                GroupBy(Function(e) e.Key, StringComparer.OrdinalIgnoreCase).ToDictionary(Function(g) g.Key, Function(g) g.First(), StringComparer.OrdinalIgnoreCase)
            _pages = NaturalOrder(byName.Keys).Select(Function(n) byName(n)).ToList()
        End Sub

        Public Overrides ReadOnly Property PageCount As Integer
            Get
                Return _pages.Count
            End Get
        End Property

        Protected Overrides Function ReadPage(index As Integer) As PageImage
            Using s = _pages(index).OpenEntryStream()
                Return New PageImage With {.Encoded = ReadAll(s)}
            End Using
        End Function

        Protected Overrides Sub Close()
            _archive.Dispose()
        End Sub
    End Class

    ''' <summary>PDF, drawn page by page with PDFium (the engine Chrome uses).</summary>
    Friend Class PdfBook
        Inherits ComicBook

        ' PDFium can only do one thing at a time across the whole app.
        Private Shared ReadOnly PdfGate As New Object()
        ''' <summary>Pages are drawn so the longer side is about this many pixels: sharp on a big screen, quick to draw.</summary>
        Private Const TargetPixels As Integer = 2200

        Private ReadOnly _bytes As Byte()
        Private ReadOnly _count As Integer

        Friend Sub New(path As String)
            MyBase.New(path, "PDF")
            _bytes = File.ReadAllBytes(path)
            SyncLock PdfGate
                Using reader = DocLib.Instance.GetDocReader(_bytes, New PageDimensions(1.0))
                    _count = reader.GetPageCount()
                End Using
            End SyncLock
        End Sub

        Public Overrides ReadOnly Property PageCount As Integer
            Get
                Return _count
            End Get
        End Property

        Protected Overrides Function ReadPage(index As Integer) As PageImage
            SyncLock PdfGate
                ' Measure the page at normal size, then draw it at the size we want.
                Dim w, h As Integer
                Using reader = DocLib.Instance.GetDocReader(_bytes, New PageDimensions(1.0))
                    Using page = reader.GetPageReader(index)
                        w = page.GetPageWidth()
                        h = page.GetPageHeight()
                    End Using
                End Using
                Dim scale = If(Math.Max(w, h) > 0, TargetPixels / Math.Max(w, h), 1.0)
                Using reader = DocLib.Instance.GetDocReader(_bytes, New PageDimensions(scale))
                    Using page = reader.GetPageReader(index)
                        Dim pixels = page.GetImage()
                        Dim pw = page.GetPageWidth(), ph = page.GetPageHeight()
                        ' PDFium leaves the paper see-through; put it on white.
                        For i = 0 To pixels.Length - 4 Step 4
                            Dim a = CInt(pixels(i + 3))
                            If a = 255 Then Continue For
                            For c = 0 To 2
                                pixels(i + c) = CByte((CInt(pixels(i + c)) * a + 255 * (255 - CInt(a))) \ 255)
                            Next
                            pixels(i + 3) = 255
                        Next
                        Return New PageImage With {.Bgra = pixels, .Width = pw, .Height = ph}
                    End Using
                End Using
            End SyncLock
        End Function

        Protected Overrides Sub Close()
        End Sub
    End Class

End Namespace
