Option Strict On

Imports System.Drawing
Imports System.Windows.Forms
Imports ComicCatalog.Data

''' <summary>
''' Reads a digital comic. One page or two side by side with a smooth page turn, or webtoon style
''' (one long strip you scroll down). Keys: ← → turn, ↑ ↓ and Space scroll, Home/End, W webtoon,
''' D two pages, F fit width, M manga (right to left), F11 full screen, Esc to leave.
''' </summary>
Friend Class ReaderForm
    Inherits Form

    Private ReadOnly _book As ComicBook
    Private ReadOnly _settings As AppSettings
    Private ReadOnly _title As String
    Private ReadOnly _canvas As ReaderCanvas
    Private ReadOnly _bar As New Panel With {.Dock = DockStyle.Bottom, .Height = 52, .Padding = New Padding(8, 6, 8, 6)}
    Private ReadOnly _slider As New TrackBar With {.Dock = DockStyle.Fill, .TickStyle = TickStyle.None, .Minimum = 0, .LargeChange = 1, .SmallChange = 1}
    Private ReadOnly _pageLabel As New Label With {.AutoSize = True, .Margin = New Padding(10, 10, 10, 3)}
    Private ReadOnly _mode As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = 190, .Margin = New Padding(3, 6, 3, 3)}
    Private ReadOnly _fitWidth As New CheckBox With {.Text = "Fit width", .AutoSize = True, .Margin = New Padding(10, 9, 3, 3)}
    Private ReadOnly _manga As New CheckBox With {.Text = "Right to left (manga)", .AutoSize = True, .Margin = New Padding(10, 9, 3, 3)}
    Private _fullScreen As Boolean
    Private _wasMaximized As Boolean
    Private _loading As Boolean
    Private _closedPage As Integer = -1

    Private Shared ReadOnly ModeKeys As String() = {"page", "two", "webtoon"}

    ''' <summary>Opens the reader at a page. The reader closes the book when it's done.</summary>
    Public Sub New(book As ComicBook, title As String, startPage As Integer, settings As AppSettings)
        _book = book
        _settings = settings
        _title = title
        _canvas = New ReaderCanvas(book) With {.Dock = DockStyle.Fill}
        Text = title
        Icon = AppIcon.Load()
        AutoScaleMode = AutoScaleMode.Font
        Font = New Font("Segoe UI", 9.5F)
        ClientSize = New Size(1000, 860)
        MinimumSize = New Size(520, 420)
        StartPosition = FormStartPosition.CenterParent
        KeyPreview = True

        _mode.Items.AddRange({"One page", "Two pages", "Webtoon (scroll down)"})
        Dim prev = Ui.MakeButton("◀", Sub(s, e) _canvas.LeftPressed())
        Dim nxt = Ui.MakeButton("▶", Sub(s, e) _canvas.RightPressed())
        Dim full = Ui.MakeButton("Full screen", Sub(s, e) ToggleFullScreen())
        Dim left As New FlowLayoutPanel With {.Dock = DockStyle.Left, .AutoSize = True, .WrapContents = False}
        left.Controls.AddRange({prev, nxt, _pageLabel})
        Dim right As New FlowLayoutPanel With {.Dock = DockStyle.Right, .AutoSize = True, .WrapContents = False}
        right.Controls.AddRange({_mode, _fitWidth, _manga, full})
        _bar.Controls.Add(_slider)
        _bar.Controls.Add(left)
        _bar.Controls.Add(right)
        Controls.Add(_canvas)
        Controls.Add(_bar)
        Theme.Apply(Me)
        _bar.BackColor = Theme.Panel
        left.BackColor = Theme.Panel
        right.BackColor = Theme.Panel
        _slider.BackColor = Theme.Panel
        _canvas.BackColor = Color.FromArgb(14, 14, 17)

        _loading = True
        _slider.Maximum = Math.Max(0, book.PageCount - 1)
        _canvas.Mode = If(ModeKeys.Contains(settings.ReaderMode), settings.ReaderMode, "page")
        _canvas.FitWidth = settings.ReaderFitWidth
        _canvas.Manga = settings.ReaderRightToLeft
        _mode.SelectedIndex = Array.IndexOf(ModeKeys, _canvas.Mode)
        _fitWidth.Checked = _canvas.FitWidth
        _manga.Checked = _canvas.Manga
        ApplyManga()
        _loading = False

        AddHandler _mode.SelectedIndexChanged, Sub(s, e) SetMode(ModeKeys(Math.Max(0, _mode.SelectedIndex)))
        AddHandler _fitWidth.CheckedChanged, Sub(s, e)
                                                 _canvas.FitWidth = _fitWidth.Checked
                                                 SaveChoices()
                                                 _canvas.Focus()
                                             End Sub
        AddHandler _manga.CheckedChanged, Sub(s, e)
                                              _canvas.Manga = _manga.Checked
                                              ApplyManga()
                                              SaveChoices()
                                              _canvas.Focus()
                                          End Sub
        AddHandler _slider.Scroll, Sub(s, e) _canvas.JumpTo(_slider.Value, animate:=False)
        AddHandler _canvas.PageChanged, Sub(s, e) ShowPlace()
        AddHandler _canvas.MiddleClicked, Sub(s, e) If _fullScreen Then _bar.Visible = Not _bar.Visible
        AddHandler _canvas.DoubleClicked, Sub(s, e) ToggleFullScreen()
        AddHandler _canvas.MouseMove, Sub(s, e)
                                          ' In full screen the bar comes back when the mouse goes to the bottom.
                                          If _fullScreen AndAlso Not _bar.Visible AndAlso e.Y > _canvas.Height - 40 Then _bar.Visible = True
                                      End Sub
        AddHandler Shown, Sub(s, e)
                              _canvas.JumpTo(startPage, animate:=False)
                              ShowPlace()
                              _canvas.Focus()
                          End Sub
        If settings.MainMaximized Then WindowState = FormWindowState.Maximized
    End Sub

    Protected Overrides Sub OnHandleCreated(e As EventArgs)
        MyBase.OnHandleCreated(e)
        Theme.DarkTitleBar(Me)
    End Sub

    ''' <summary>Where you got to, to save when the reader closes.</summary>
    Public ReadOnly Property CurrentPage As Integer
        Get
            Return If(_closedPage >= 0, _closedPage, _canvas.CurrentPage)
        End Get
    End Property

    Public ReadOnly Property PageCount As Integer
        Get
            Return _canvas.PageCount
        End Get
    End Property

    ''' <summary>True if you got to the last page.</summary>
    Public ReadOnly Property ReachedEnd As Boolean
        Get
            Return _canvas.ReachedEnd
        End Get
    End Property

    Private Sub SetMode(mode As String)
        _canvas.Mode = mode
        If Not _loading Then
            Dim index = Array.IndexOf(ModeKeys, mode)
            If _mode.SelectedIndex <> index Then _mode.SelectedIndex = index
            SaveChoices()
        End If
        ShowPlace()
        _canvas.Focus()
    End Sub

    Private Sub ApplyManga()
        _slider.RightToLeft = If(_canvas.Manga, RightToLeft.Yes, RightToLeft.No)
        _slider.RightToLeftLayout = _canvas.Manga
    End Sub

    Private Sub SaveChoices()
        If _loading Then Return
        _settings.ReaderMode = _canvas.Mode
        _settings.ReaderFitWidth = _canvas.FitWidth
        _settings.ReaderRightToLeft = _canvas.Manga
        _settings.Save()
    End Sub

    Private Sub ShowPlace()
        Dim first = _canvas.CurrentPage, last = _canvas.LastVisiblePage
        Dim pages = If(last > first AndAlso _canvas.Mode = "two", $"{first + 1}-{last + 1}", $"{first + 1}")
        _pageLabel.Text = $"Page {pages} of {_canvas.PageCount}"
        _slider.Value = Math.Max(_slider.Minimum, Math.Min(_slider.Maximum, first))
        Text = $"{_title} - page {pages} of {_canvas.PageCount}"
    End Sub

    Private Sub ToggleFullScreen()
        If _fullScreen Then
            _fullScreen = False
            FormBorderStyle = FormBorderStyle.Sizable
            WindowState = If(_wasMaximized, FormWindowState.Maximized, FormWindowState.Normal)
            _bar.Visible = True
        Else
            _fullScreen = True
            _wasMaximized = WindowState = FormWindowState.Maximized
            ' Leaving maximised first lets the window cover the taskbar.
            WindowState = FormWindowState.Normal
            FormBorderStyle = FormBorderStyle.None
            WindowState = FormWindowState.Maximized
            _bar.Visible = False
        End If
        _canvas.Focus()
    End Sub

    Protected Overrides Function ProcessCmdKey(ByRef msg As Message, keyData As Keys) As Boolean
        If _mode.DroppedDown Then Return MyBase.ProcessCmdKey(msg, keyData)
        Dim smallStep = Math.Max(80, _canvas.Height * 0.15)
        Select Case keyData
            Case Keys.Right : _canvas.RightPressed()
            Case Keys.Left : _canvas.LeftPressed()
            Case Keys.PageDown, Keys.Space, Keys.Enter : _canvas.NextPage()
            Case Keys.PageUp, Keys.Shift Or Keys.Space, Keys.Back : _canvas.PreviousPage()
            Case Keys.Down
                If _canvas.Mode = "webtoon" OrElse _canvas.FitWidth Then _canvas.ScrollBy(smallStep) Else _canvas.NextPage()
            Case Keys.Up
                If _canvas.Mode = "webtoon" OrElse _canvas.FitWidth Then _canvas.ScrollBy(-smallStep) Else _canvas.PreviousPage()
            Case Keys.Home : _canvas.FirstPage()
            Case Keys.End : _canvas.LastPage()
            Case Keys.F11, Keys.F11 Or Keys.Alt : ToggleFullScreen()
            Case Keys.W : SetMode(If(_canvas.Mode = "webtoon", "page", "webtoon"))
            Case Keys.D : SetMode(If(_canvas.Mode = "two", "page", "two"))
            Case Keys.F : _fitWidth.Checked = Not _fitWidth.Checked
            Case Keys.M : _manga.Checked = Not _manga.Checked
            Case Keys.Escape
                If _fullScreen Then ToggleFullScreen() Else Close()
            Case Else
                Return MyBase.ProcessCmdKey(msg, keyData)
        End Select
        Return True
    End Function

    Protected Overrides Sub OnFormClosed(e As FormClosedEventArgs)
        MyBase.OnFormClosed(e)
        _closedPage = _canvas.CurrentPage
        _canvas.Dispose()
        _book.Dispose()
    End Sub

End Class
