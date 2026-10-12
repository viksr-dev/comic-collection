Option Strict On

Imports System.Diagnostics
Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Drawing.Imaging
Imports System.IO
Imports System.Runtime.InteropServices
Imports System.Threading
Imports System.Windows.Forms
Imports ComicCatalog.Data

''' <summary>
''' Draws the pages of a digital comic: one page or two side by side with a sliding page turn, or
''' "webtoon" style as one long strip you scroll down smoothly. Pages are prepared at screen size on a
''' background thread, a few ahead of where you are, so turning and scrolling don't stutter.
''' </summary>
Friend Class ReaderCanvas
    Inherits Control

    Private ReadOnly _book As ComicBook
    Private ReadOnly _count As Integer
    ' Each page's full size once it's been opened (Size.Empty until then).
    Private ReadOnly _sizes As Size()
    Private ReadOnly _failed As New HashSet(Of Integer)

    ' Pages at screen size (only touched on the window's thread), with the box each was made to fit.
    Private ReadOnly _scaled As New Dictionary(Of Integer, Bitmap)
    Private ReadOnly _scaledBox As New Dictionary(Of Integer, Size)
    Private ReadOnly _lastUsed As New Dictionary(Of Integer, Long)
    Private _useTick As Long
    Private Const MaxScaled As Integer = 24

    ' Work for the background thread: at most one request per page, the most wanted first.
    Private ReadOnly _queueLock As New Object()
    Private ReadOnly _queue As New List(Of KeyValuePair(Of Integer, Size))
    Private _workerRunning As Boolean
    ' The page the background thread is working on now, so it isn't asked for twice.
    Private _working As KeyValuePair(Of Integer, Size) = New KeyValuePair(Of Integer, Size)(-1, Size.Empty)
    ' Full-size pages, only touched by the background thread.
    Private ReadOnly _originals As New Dictionary(Of Integer, Bitmap)
    Private ReadOnly _originalOrder As New LinkedList(Of Integer)
    Private Const MaxOriginals As Integer = 6
    Private _closed As Boolean

    Private _mode As String = "page"
    Private _fitWidth As Boolean
    Private _manga As Boolean
    ' Page modes: the first page of what's showing, and how far down a tall (fit width) page you are.
    Private _page As Integer
    Private _panY As Double
    Private _panTarget As Double
    ' Webtoon: how far down the strip you are, and where smooth scrolling is heading.
    Private _scrollY As Double
    Private _scrollTarget As Double
    Private _lastWebtoonPage As Integer = -1

    ' Page turn animation
    Private ReadOnly _timer As New System.Windows.Forms.Timer With {.Interval = 15}
    Private _animFrom As Integer = -1
    Private _animDir As Integer
    Private ReadOnly _animClock As New Stopwatch()
    Private Const TurnMs As Double = 230
    Private _lastWheelTurn As DateTime = DateTime.MinValue

    ' Dragging the strip in webtoon mode
    Private _dragFrom As Point?
    Private _dragStartY As Double
    Private _dragged As Boolean

    Private _hintUntil As DateTime = DateTime.Now.AddSeconds(4)

    Private ReadOnly _backColor As Color = Color.FromArgb(14, 14, 17)
    Private Const DefaultAspect As Double = 1.54
    Private Const WebtoonColumn As Integer = 900

    ''' <summary>The page you're on changed (by turning, scrolling or the slider).</summary>
    Public Event PageChanged As EventHandler
    ''' <summary>A click in the middle: show or hide the bar.</summary>
    Public Event MiddleClicked As EventHandler
    Public Event DoubleClicked As EventHandler
    ''' <summary>Raised once, the first time the last page is on screen.</summary>
    Public Event EndReached As EventHandler

    Public Sub New(book As ComicBook)
        _book = book
        _count = book.PageCount
        ReDim _sizes(_count - 1)
        SetStyle(ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer Or ControlStyles.UserPaint Or
                 ControlStyles.ResizeRedraw Or ControlStyles.Selectable, True)
        BackColor = _backColor
        Cursor = Cursors.Hand
        AddHandler _timer.Tick, AddressOf OnTick
        _timer.Start()
    End Sub

    Public ReadOnly Property PageCount As Integer
        Get
            Return _count
        End Get
    End Property

    Private _reachedEnd As Boolean

    ''' <summary>True once the last page has been on screen.</summary>
    Public Property ReachedEnd As Boolean
        Get
            Return _reachedEnd
        End Get
        Set(value As Boolean)
            Dim first = value AndAlso Not _reachedEnd
            _reachedEnd = value
            If first Then RaiseEvent EndReached(Me, EventArgs.Empty)
        End Set
    End Property

    ''' <summary>"page" (one page), "two" (two side by side) or "webtoon" (one long strip).</summary>
    Public Property Mode As String
        Get
            Return _mode
        End Get
        Set(value As String)
            Dim current = CurrentPage
            _mode = If(value = "two" OrElse value = "webtoon", value, "page")
            _animFrom = -1
            JumpTo(current, animate:=False)
        End Set
    End Property

    ''' <summary>Pages fill the width (scroll down a tall page) instead of fitting whole on screen.</summary>
    Public Property FitWidth As Boolean
        Get
            Return _fitWidth
        End Get
        Set(value As Boolean)
            Dim current = CurrentPage
            _fitWidth = value
            _panY = 0 : _panTarget = 0
            If _mode = "webtoon" Then JumpTo(current, animate:=False)
            Invalidate()
        End Set
    End Property

    ''' <summary>Manga: read right to left, so the next page is on the left.</summary>
    Public Property Manga As Boolean
        Get
            Return _manga
        End Get
        Set(value As Boolean)
            _manga = value
            Invalidate()
        End Set
    End Property

    ''' <summary>The page you're reading (the first of two side by side; the one in view in webtoon mode).</summary>
    Public ReadOnly Property CurrentPage As Integer
        Get
            If _mode <> "webtoon" Then Return _page
            Return WebtoonPageAt(_scrollY + ClientSize.Height / 3.0)
        End Get
    End Property

    ''' <summary>The last page showing, e.g. the right-hand one of two.</summary>
    Public ReadOnly Property LastVisiblePage As Integer
        Get
            If _mode = "webtoon" Then Return WebtoonPageAt(_scrollY + ClientSize.Height - 1)
            Dim pages = SpreadPages(_page)
            Return pages(pages.Count - 1)
        End Get
    End Property

    ' ---------- moving around ----------

    Public Sub JumpTo(index As Integer, animate As Boolean)
        index = Math.Max(0, Math.Min(_count - 1, index))
        If _mode = "webtoon" Then
            _scrollY = Math.Min(WebtoonTop(index), MaxScroll())
            _scrollTarget = _scrollY
        Else
            TurnTo(SpreadStart(index), animate)
        End If
        PagesChanged()
    End Sub

    ''' <summary>Forward: the next page, or further down a tall page or the strip.</summary>
    Public Sub NextPage()
        Dim h = ClientSize.Height
        If _mode = "webtoon" Then
            ScrollBy(h * 0.85)
        ElseIf _fitWidth AndAlso _panTarget < MaxPan() - 1 Then
            _panTarget = Math.Min(MaxPan(), _panTarget + h * 0.85)
        Else
            Dim nextStart = _page + SpreadPages(_page).Count
            If nextStart < _count Then TurnTo(nextStart, animate:=True)
        End If
    End Sub

    Public Sub PreviousPage()
        Dim h = ClientSize.Height
        If _mode = "webtoon" Then
            ScrollBy(-h * 0.85)
        ElseIf _fitWidth AndAlso _panTarget > 1 Then
            _panTarget = Math.Max(0, _panTarget - h * 0.85)
        ElseIf _page > 0 Then
            TurnTo(SpreadStart(_page - 1), animate:=True)
            If _fitWidth Then
                ' Coming back to a tall page starts at its bottom, where you left it.
                _panY = Double.MaxValue : _panTarget = Double.MaxValue
            End If
        End If
    End Sub

    ''' <summary>The arrow pointing left: back, or forward for manga.</summary>
    Public Sub LeftPressed()
        If _manga Then NextPage() Else PreviousPage()
    End Sub

    Public Sub RightPressed()
        If _manga Then PreviousPage() Else NextPage()
    End Sub

    Public Sub ScrollBy(pixels As Double)
        If _mode = "webtoon" Then
            _scrollTarget = Math.Max(0, Math.Min(MaxScroll(), _scrollTarget + pixels))
        ElseIf _fitWidth Then
            _panTarget = Math.Max(0, Math.Min(MaxPan(), _panTarget + pixels))
        End If
    End Sub

    Public Sub FirstPage()
        JumpTo(0, animate:=_mode <> "webtoon")
    End Sub

    Public Sub LastPage()
        JumpTo(_count - 1, animate:=_mode <> "webtoon")
    End Sub

    Private Sub TurnTo(start As Integer, animate As Boolean)
        If start = _page AndAlso _animFrom < 0 Then
            Invalidate()
            Return
        End If
        If animate AndAlso IsHandleCreated AndAlso Visible Then
            _animFrom = _page
            _animDir = Math.Sign(start - _page)
            _animClock.Restart()
        Else
            _animFrom = -1
        End If
        _page = start
        _panY = 0 : _panTarget = 0
        PagesChanged()
    End Sub

    Private Sub PagesChanged()
        If LastVisiblePage >= _count - 1 Then ReachedEnd = True
        Prefetch()
        Invalidate()
        RaiseEvent PageChanged(Me, EventArgs.Empty)
    End Sub

    ' ---------- which pages show where ----------

    Private Function SpreadStart(index As Integer) As Integer
        If _mode <> "two" OrElse index <= 0 Then Return Math.Max(0, index)
        ' The cover is on its own, then pages go in pairs: 1-2, 3-4…
        Return If(index Mod 2 = 1, index, index - 1)
    End Function

    Private Function SpreadPages(start As Integer) As List(Of Integer)
        Dim pages As New List(Of Integer) From {start}
        If _mode = "two" AndAlso start > 0 AndAlso start + 1 < _count AndAlso Not IsWide(start) AndAlso Not IsWide(start + 1) Then pages.Add(start + 1)
        Return pages
    End Function

    ' A double-page picture already shows both pages, so it's shown on its own.
    Private Function IsWide(index As Integer) As Boolean
        Dim s = _sizes(index)
        Return Not s.IsEmpty AndAlso s.Width > s.Height
    End Function

    Private Function Aspect(index As Integer) As Double
        Dim s = _sizes(index)
        Return If(s.IsEmpty OrElse s.Width = 0, DefaultAspect, s.Height / CDbl(s.Width))
    End Function

    ''' <summary>The box a page is fitted into: (width, 0) means fill that width.</summary>
    Private Function PageBox(pagesInSpread As Integer) As Size
        Dim w = Math.Max(1, ClientSize.Width), h = Math.Max(1, ClientSize.Height)
        If pagesInSpread = 2 Then w \= 2
        Return If(_fitWidth, New Size(w, 0), New Size(w, h))
    End Function

    Private Shared Function FitSize(full As Size, box As Size) As Size
        If box.Height = 0 Then Return New Size(box.Width, Math.Max(1, CInt(Math.Round(full.Height * box.Width / CDbl(full.Width)))))
        Dim scale = Math.Min(box.Width / CDbl(full.Width), box.Height / CDbl(full.Height))
        Return New Size(Math.Max(1, CInt(Math.Round(full.Width * scale))), Math.Max(1, CInt(Math.Round(full.Height * scale))))
    End Function

    Private Function DisplaySize(index As Integer, box As Size) As Size
        Dim full = If(_sizes(index).IsEmpty, New Size(1000, CInt(1000 * DefaultAspect)), _sizes(index))
        Return FitSize(full, box)
    End Function

    Private Function MaxPan() As Double
        If Not _fitWidth OrElse _mode = "webtoon" Then Return 0
        Dim pages = SpreadPages(_page)
        Dim box = PageBox(pages.Count)
        Return Math.Max(0, pages.Max(Function(p) DisplaySize(p, box).Height) - ClientSize.Height)
    End Function

    Private Function WebtoonWidth() As Integer
        Return If(_fitWidth, Math.Max(1, ClientSize.Width), Math.Max(1, Math.Min(ClientSize.Width, WebtoonColumn)))
    End Function

    Private Function WebtoonHeight(index As Integer) As Double
        Return WebtoonWidth() * Aspect(index)
    End Function

    Private Function WebtoonTop(index As Integer) As Double
        Dim y = 0.0
        For i = 0 To index - 1
            y += WebtoonHeight(i)
        Next
        Return y
    End Function

    Private Function WebtoonPageAt(y As Double) As Integer
        Dim top = 0.0
        For i = 0 To _count - 1
            top += WebtoonHeight(i)
            If y < top Then Return i
        Next
        Return _count - 1
    End Function

    Private Function MaxScroll() As Double
        Return Math.Max(0, WebtoonTop(_count) - ClientSize.Height)
    End Function

    ' ---------- drawing ----------

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        Dim g = e.Graphics
        g.Clear(_backColor)
        If _count = 0 Then Return
        g.PixelOffsetMode = PixelOffsetMode.Half
        If _mode = "webtoon" Then
            PaintWebtoon(g)
        Else
            Dim w = ClientSize.Width
            If _animFrom >= 0 Then
                Dim t = Math.Min(1.0, _animClock.Elapsed.TotalMilliseconds / TurnMs)
                Dim eased = 1 - Math.Pow(1 - t, 3)
                Dim visualDir = If(_manga, -_animDir, _animDir)
                PaintSpread(g, _animFrom, -visualDir * eased * w, 0)
                PaintSpread(g, _page, visualDir * (1 - eased) * w, 0)
            Else
                PaintSpread(g, _page, 0, _panY)
            End If
        End If
        PaintHint(g)
    End Sub

    Private Sub PaintSpread(g As Graphics, start As Integer, dx As Double, panY As Double)
        Dim pages = SpreadPages(start)
        If _manga Then pages.Reverse()
        Dim box = PageBox(pages.Count)
        Dim w = ClientSize.Width, h = ClientSize.Height
        For i = 0 To pages.Count - 1
            Dim index = pages(i)
            Dim size = DisplaySize(index, box)
            Dim x As Double
            If pages.Count = 1 Then
                x = (w - size.Width) / 2.0
            ElseIf i = 0 Then
                x = w / 2.0 - size.Width
            Else
                x = w / 2.0
            End If
            Dim y = If(size.Height < h, (h - size.Height) / 2.0, -panY)
            DrawPage(g, index, box, New Rectangle(CInt(x + dx), CInt(y), size.Width, size.Height))
        Next
    End Sub

    Private Sub PaintWebtoon(g As Graphics)
        Dim colW = WebtoonWidth()
        Dim box As New Size(colW, 0)
        Dim x = (ClientSize.Width - colW) \ 2
        Dim top = 0.0
        For i = 0 To _count - 1
            Dim ph = WebtoonHeight(i)
            If top + ph > _scrollY AndAlso top < _scrollY + ClientSize.Height Then
                ' Rounded so neighbouring pages meet without a gap.
                Dim y1 = CInt(Math.Floor(top - _scrollY)), y2 = CInt(Math.Floor(top + ph - _scrollY))
                DrawPage(g, i, box, New Rectangle(x, y1, colW, Math.Max(1, y2 - y1)))
            End If
            If top > _scrollY + ClientSize.Height Then Exit For
            top += ph
        Next
    End Sub

    Private Sub DrawPage(g As Graphics, index As Integer, box As Size, target As Rectangle)
        Dim bmp As Bitmap = Nothing
        If _scaled.TryGetValue(index, bmp) Then
            _useTick += 1
            _lastUsed(index) = _useTick
            If _scaledBox(index) = box AndAlso bmp.Width = target.Width AndAlso bmp.Height = target.Height Then
                g.InterpolationMode = InterpolationMode.NearestNeighbor
            Else
                ' Made for another window size: stretch it until the sharp one is ready.
                g.InterpolationMode = InterpolationMode.Bilinear
                Request(index, box, urgent:=True)
            End If
            g.DrawImage(bmp, target)
            Return
        End If
        Using b As New SolidBrush(Color.FromArgb(28, 29, 34))
            g.FillRectangle(b, target)
        End Using
        Dim message = If(_failed.Contains(index), $"Page {index + 1} can't be shown (the picture type isn't supported).", "Loading…")
        Using f As New Font("Segoe UI", 11), b As New SolidBrush(Theme.Muted),
              sf As New StringFormat With {.Alignment = StringAlignment.Center, .LineAlignment = StringAlignment.Center}
            Dim visible = Rectangle.Intersect(target, ClientRectangle)
            g.DrawString(message, f, b, If(visible.IsEmpty, target, visible), sf)
        End Using
        If Not _failed.Contains(index) Then Request(index, box, urgent:=True)
    End Sub

    Private Sub PaintHint(g As Graphics)
        If DateTime.Now > _hintUntil Then Return
        Dim text = If(_mode = "webtoon", "Scroll or use ↑ ↓ and Space  ·  W: page mode  ·  F11: full screen",
                      "Click the sides or use ← →  ·  W: webtoon scrolling  ·  F11: full screen")
        Using f As New Font("Segoe UI", 10.5F)
            Dim size = g.MeasureString(text, f)
            Dim r As New RectangleF((ClientSize.Width - size.Width) / 2 - 14, ClientSize.Height - size.Height - 34, size.Width + 28, size.Height + 14)
            Using bg As New SolidBrush(Color.FromArgb(200, 30, 33, 42)), fg As New SolidBrush(Theme.Text)
                g.FillRectangle(bg, r)
                g.DrawString(text, f, fg, r.X + 14, r.Y + 7)
            End Using
        End Using
    End Sub

    Private Sub OnTick(sender As Object, e As EventArgs)
        Dim dirty = False
        If _animFrom >= 0 Then
            If _animClock.Elapsed.TotalMilliseconds >= TurnMs Then _animFrom = -1
            dirty = True
        End If
        If _mode = "webtoon" Then
            _scrollTarget = Math.Max(0, Math.Min(MaxScroll(), _scrollTarget))
            If Math.Abs(_scrollTarget - _scrollY) > 0.5 Then
                _scrollY += (_scrollTarget - _scrollY) * 0.22
                dirty = True
            ElseIf _scrollY <> _scrollTarget Then
                _scrollY = _scrollTarget
                dirty = True
            End If
            If dirty Then
                Dim p = CurrentPage
                If p <> _lastWebtoonPage Then
                    _lastWebtoonPage = p
                    PagesChanged()
                ElseIf LastVisiblePage >= _count - 1 Then
                    ReachedEnd = True
                End If
            End If
        ElseIf _fitWidth Then
            Dim max = MaxPan()
            _panTarget = Math.Max(0, Math.Min(max, _panTarget))
            _panY = Math.Min(_panY, max)
            If Math.Abs(_panTarget - _panY) > 0.5 Then
                _panY += (_panTarget - _panY) * 0.22
                dirty = True
            ElseIf _panY <> _panTarget Then
                _panY = _panTarget
                dirty = True
            End If
        End If
        If _hintUntil <> DateTime.MinValue AndAlso DateTime.Now > _hintUntil Then
            _hintUntil = DateTime.MinValue
            dirty = True
        End If
        If dirty Then Invalidate()
    End Sub

    ' ---------- mouse ----------

    Protected Overrides Sub OnMouseWheel(e As MouseEventArgs)
        MyBase.OnMouseWheel(e)
        Dim notches = e.Delta / 120.0
        If _mode = "webtoon" Then
            ScrollBy(-notches * Math.Max(90, ClientSize.Height * 0.2))
            Return
        End If
        If _fitWidth Then
            Dim before = _panTarget
            ScrollBy(-notches * Math.Max(90, ClientSize.Height * 0.2))
            If before <> _panTarget Then Return
        End If
        ' A touchpad sends lots of little scrolls; one page turn per flick.
        If DateTime.Now - _lastWheelTurn < TimeSpan.FromMilliseconds(350) Then Return
        _lastWheelTurn = DateTime.Now
        If notches < 0 Then NextPage() Else PreviousPage()
    End Sub

    Protected Overrides Sub OnMouseDown(e As MouseEventArgs)
        MyBase.OnMouseDown(e)
        Focus()
        If e.Button = MouseButtons.Left AndAlso _mode = "webtoon" Then
            _dragFrom = e.Location
            _dragStartY = _scrollY
            _dragged = False
        End If
    End Sub

    Protected Overrides Sub OnMouseMove(e As MouseEventArgs)
        MyBase.OnMouseMove(e)
        If _dragFrom.HasValue AndAlso e.Button = MouseButtons.Left Then
            Dim dy = e.Y - _dragFrom.Value.Y
            If Math.Abs(dy) > 4 Then _dragged = True
            If _dragged Then
                _scrollY = Math.Max(0, Math.Min(MaxScroll(), _dragStartY - dy))
                _scrollTarget = _scrollY
                Invalidate()
            End If
        End If
    End Sub

    Protected Overrides Sub OnMouseUp(e As MouseEventArgs)
        MyBase.OnMouseUp(e)
        _dragFrom = Nothing
    End Sub

    Protected Overrides Sub OnMouseClick(e As MouseEventArgs)
        MyBase.OnMouseClick(e)
        If e.Button <> MouseButtons.Left Then Return
        If _dragged Then
            _dragged = False
            Return
        End If
        Dim third = ClientSize.Width / 3.0
        If _mode = "webtoon" OrElse (e.X >= third AndAlso e.X <= 2 * third) Then
            RaiseEvent MiddleClicked(Me, EventArgs.Empty)
        ElseIf e.X < third Then
            LeftPressed()
        Else
            RightPressed()
        End If
    End Sub

    Protected Overrides Sub OnMouseDoubleClick(e As MouseEventArgs)
        MyBase.OnMouseDoubleClick(e)
        RaiseEvent DoubleClicked(Me, EventArgs.Empty)
    End Sub

    Protected Overrides Sub OnResize(e As EventArgs)
        Dim current = CurrentPage
        MyBase.OnResize(e)
        ' Keep the same page in view when the strip's width changes.
        If _mode = "webtoon" AndAlso _count > 0 Then
            _scrollY = Math.Min(WebtoonTop(current), MaxScroll())
            _scrollTarget = _scrollY
        End If
        Invalidate()
    End Sub

    ' ---------- getting pages ready ----------

    ''' <summary>Gets the next few pages (and the one before) ready while you read.</summary>
    Private Sub Prefetch()
        If _count = 0 OrElse Not IsHandleCreated Then Return
        If _mode = "webtoon" Then
            Dim box As New Size(WebtoonWidth(), 0)
            Dim last = LastVisiblePage
            For i = last + 1 To Math.Min(_count - 1, last + 3)
                Request(i, box, urgent:=False)
            Next
            If CurrentPage > 0 Then Request(CurrentPage - 1, box, urgent:=False)
        Else
            Dim nextStart = _page + SpreadPages(_page).Count
            For Each start In {nextStart, nextStart + 1, nextStart + 2}
                If start < _count Then Request(start, PageBox(SpreadPages(SpreadStart(start)).Count), urgent:=False)
            Next
            If _page > 0 Then
                Dim prev = SpreadStart(_page - 1)
                Dim prevPages = SpreadPages(prev)
                For Each p In prevPages
                    Request(p, PageBox(prevPages.Count), urgent:=False)
                Next
            End If
        End If
    End Sub

    Private Sub Request(index As Integer, box As Size, urgent As Boolean)
        If _closed OrElse box.Width <= 0 Then Return
        Dim have As Size
        If _scaledBox.TryGetValue(index, have) AndAlso have = box Then Return
        Dim startWorker = False
        SyncLock _queueLock
            If _working.Key = index AndAlso _working.Value = box Then Return
            Dim existing = _queue.FindIndex(Function(q) q.Key = index)
            If existing >= 0 Then
                If Not urgent AndAlso _queue(existing).Value = box Then Return
                _queue.RemoveAt(existing)
            End If
            Dim item = New KeyValuePair(Of Integer, Size)(index, box)
            If urgent Then _queue.Insert(0, item) Else _queue.Add(item)
            If Not _workerRunning Then
                _workerRunning = True
                startWorker = True
            End If
        End SyncLock
        If startWorker Then Task.Run(AddressOf Work)
    End Sub

    Private Sub Work()
        Do
            Dim item As KeyValuePair(Of Integer, Size)
            SyncLock _queueLock
                _working = New KeyValuePair(Of Integer, Size)(-1, Size.Empty)
                If _closed OrElse _queue.Count = 0 Then
                    _workerRunning = False
                    Return
                End If
                item = _queue(0)
                _queue.RemoveAt(0)
                _working = item
            End SyncLock
            Dim index = item.Key
            Try
                Dim original = GetOriginal(index)
                Dim fullSize = original.Size
                Dim size = FitSize(fullSize, item.Value)
                Dim bmp As New Bitmap(size.Width, size.Height, PixelFormat.Format32bppPArgb)
                Using g = Graphics.FromImage(bmp)
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality
                    g.CompositingQuality = CompositingQuality.HighQuality
                    Using wrap As New ImageAttributes()
                        wrap.SetWrapMode(WrapMode.TileFlipXY)
                        g.DrawImage(original, New Rectangle(0, 0, size.Width, size.Height), 0, 0, fullSize.Width, fullSize.Height, GraphicsUnit.Pixel, wrap)
                    End Using
                End Using
                If Not Deliver(Sub() StoreScaled(index, item.Value, bmp, fullSize)) Then bmp.Dispose()
            Catch ex As Exception
                If _closed Then Continue Do
                Deliver(Sub()
                            _failed.Add(index)
                            Invalidate()
                        End Sub)
            End Try
        Loop
    End Sub

    Private Function Deliver(action As Action) As Boolean
        If _closed OrElse IsDisposed OrElse Not IsHandleCreated Then Return False
        Try
            BeginInvoke(action)
            Return True
        Catch ex As Exception When TypeOf ex Is InvalidOperationException OrElse TypeOf ex Is ObjectDisposedException
            Return False
        End Try
    End Function

    ' Background thread only.
    Private Function GetOriginal(index As Integer) As Bitmap
        Dim bmp As Bitmap = Nothing
        If _originals.TryGetValue(index, bmp) Then
            _originalOrder.Remove(index)
            _originalOrder.AddFirst(index)
            Return bmp
        End If
        bmp = Decode(_book.GetPage(index))
        _originals(index) = bmp
        _originalOrder.AddFirst(index)
        While _originalOrder.Count > MaxOriginals
            Dim oldest = _originalOrder.Last.Value
            _originalOrder.RemoveLast()
            _originals(oldest).Dispose()
            _originals.Remove(oldest)
        End While
        Return bmp
    End Function

    Friend Shared Function Decode(page As PageImage) As Bitmap
        If page.Encoded IsNot Nothing Then
            Using ms As New MemoryStream(page.Encoded), img = Image.FromStream(ms)
                Return New Bitmap(img)
            End Using
        End If
        Dim bmp As New Bitmap(page.Width, page.Height, PixelFormat.Format32bppArgb)
        Dim data = bmp.LockBits(New Rectangle(0, 0, page.Width, page.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb)
        Try
            For y = 0 To page.Height - 1
                Marshal.Copy(page.Bgra, y * page.Width * 4, data.Scan0 + y * data.Stride, page.Width * 4)
            Next
        Finally
            bmp.UnlockBits(data)
        End Try
        Return bmp
    End Function

    Private Sub StoreScaled(index As Integer, box As Size, bmp As Bitmap, fullSize As Size)
        If _closed Then
            bmp.Dispose()
            Return
        End If
        Dim currentBefore = CurrentPage
        Dim oldHeight = WebtoonHeight(index)
        Dim sizeChanged = _sizes(index) <> fullSize
        _sizes(index) = fullSize
        ' In webtoon mode, a page above you that turned out taller or shorter than guessed would make the strip jump.
        If sizeChanged AndAlso _mode = "webtoon" AndAlso index < currentBefore Then
            Dim delta = WebtoonHeight(index) - oldHeight
            _scrollY += delta
            _scrollTarget += delta
        End If
        Dim old As Bitmap = Nothing
        If _scaled.TryGetValue(index, old) Then old.Dispose()
        _scaled(index) = bmp
        _scaledBox(index) = box
        _useTick += 1
        _lastUsed(index) = _useTick
        While _scaled.Count > MaxScaled
            Dim oldest = _lastUsed.OrderBy(Function(kv) kv.Value).First().Key
            _scaled(oldest).Dispose()
            _scaled.Remove(oldest)
            _scaledBox.Remove(oldest)
            _lastUsed.Remove(oldest)
        End While
        Invalidate()
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing Then
            _closed = True
            _timer.Dispose()
            For Each b In _scaled.Values
                b.Dispose()
            Next
            _scaled.Clear()
            ' The originals belong to the background thread; it stops at its next step and they're left to the garbage collector.
        End If
        MyBase.Dispose(disposing)
    End Sub

End Class
