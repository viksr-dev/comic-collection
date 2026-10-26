Option Strict On

Imports System.Data
Imports System.Diagnostics
Imports System.Drawing
Imports System.Globalization
Imports System.IO
Imports System.Windows.Forms
Imports ComicCatalog.Data

Public Class MainForm
    Inherits Form

    Private ReadOnly _settings As AppSettings = AppSettings.Load()
    Private _db As ComicDb

    ' Collection tab
    Private ReadOnly _barcode As New TextBox With {.Width = 220, .PlaceholderText = "type or scan, then Enter"}
    Private ReadOnly _search As New TextBox With {.Width = 220, .PlaceholderText = "e.g. daredevil 50"}
    Private ReadOnly _grid As DataGridView = Ui.MakeGrid()
    Private ReadOnly _covers As New ListView With {.Dock = DockStyle.Fill, .View = View.LargeIcon, .VirtualMode = True,
                                                   .MultiSelect = True, .Visible = False}
    Private ReadOnly _coverImages As New ImageList With {.ColorDepth = ColorDepth.Depth32Bit, .ImageSize = CoverCache.ThumbSize}
    Private ReadOnly _coverIndex As New Dictionary(Of Long, Integer)
    Private ReadOnly _coverLoading As New HashSet(Of Long)
    Private ReadOnly _coverCache As New CoverCache()
    Private ReadOnly _gridThumbs As New Dictionary(Of Long, Image)
    Private ReadOnly _gridPlaceholder As Image = SmallThumb(CoverCache.Placeholder())
    Private ReadOnly _thumbColumn As New DataGridViewImageColumn With {.Name = "CoverThumb", .HeaderText = "", .Width = 40,
        .ImageLayout = DataGridViewImageCellLayout.Zoom, .Resizable = DataGridViewTriState.False,
        .SortMode = DataGridViewColumnSortMode.NotSortable, .AutoSizeMode = DataGridViewAutoSizeColumnMode.None}
    Private _rows As DataTable
    Private _layingOutColumns As Boolean
    Private _columnsChanged As Boolean
    Private _searchSuggestionsKey As String = ""
    ' Undo (Ctrl+Z or the link under the buttons): the last changes, newest on top.
    Private ReadOnly _undoSteps As New List(Of Tuple(Of String, Action))
    Private ReadOnly _undoLink As New LinkLabel With {.AutoSize = True, .Margin = New Padding(8, 18, 3, 3), .Visible = False}
    Private ReadOnly _listButton As Button = Ui.MakeButton("List", Sub(s, e) SetView(False))
    Private ReadOnly _coversButton As Button = Ui.MakeButton("Covers", Sub(s, e) SetView(True))
    Private ReadOnly _findCovers As Button = Ui.MakeButton("Find covers", AddressOf OnFindCovers)
    Private ReadOnly _getScans As Button = Ui.MakeButton("Get scans from phone", Sub(s, e) CollectScans(quiet:=False))
    Private ReadOnly _scanNote As New Label With {.AutoSize = True, .Margin = New Padding(3, 12, 3, 3)}
    Private ReadOnly _scanTimer As New Timer With {.Interval = 120000}
    Private _collecting As Boolean
    Private _findingCovers As Boolean
    Private _stopFinding As Boolean
    Private ReadOnly _statComics As New Label()
    Private ReadOnly _statComicsCaption As New Label()
    ''' <summary>True on the Trades page: the Collection list shows trade paperbacks and hardcovers instead of single issues.</summary>
    Private _tradesView As Boolean
    Private ReadOnly _statCopies As New Label()
    Private ReadOnly _statValue As New Label()
    Private ReadOnly _statPaid As New Label()
    Private ReadOnly _statRead As New Label()
    Private ReadOnly _readFilter As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = 130}
    Private ReadOnly _publisherFilter As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = 160}
    Private _fillingPublishers As Boolean
    ' Boxes list beside the Collection list
    Private ReadOnly _boxesPanel As New Panel With {.Dock = DockStyle.Right, .Width = 200, .Padding = New Padding(12, 0, 0, 0)}
    Private ReadOnly _boxList As New ListBox With {.Dock = DockStyle.Fill, .IntegralHeight = False, .BorderStyle = BorderStyle.None}
    Private ReadOnly _boxesButton As Button = Ui.MakeButton("Boxes", Sub(s, e) SetBoxesVisible(Not _boxesPanel.Visible))
    Private _boxFilter As String
    Private _boxCounts As New List(Of KeyValuePair(Of String, Integer))
    Private _fillingBoxes As Boolean
    Private _dragging As Boolean
    Private _previousSelection As New List(Of Long)
    Private _currentSelection As New List(Of Long)
    Private _dragIds As New List(Of Long)
    Private _dragFrom As Rectangle
    ' "More" menu on the Collection page (also on right-click)
    Private ReadOnly _moreMenu As New ContextMenuStrip()
    Private ReadOnly _ebayItem As New ToolStripMenuItem("Estimate values from eBay")
    Private ReadOnly _ebayButton As Button = Ui.MakeButton("Estimate values from eBay", AddressOf OnEstimateEbay, Theme.PrimaryTag)
    Private ReadOnly _ebayStatus As New Label With {.AutoSize = True, .MaximumSize = New Size(640, 0), .ForeColor = SystemColors.GrayText}
    Private _findingEbay As Boolean
    Private ReadOnly _dealsButton As Button = Ui.MakeButton("eBay deals", AddressOf OnDeals)
    Private ReadOnly _localPerUsd As New NumericUpDown With {.Minimum = 0.1D, .Maximum = 1000, .DecimalPlaces = 2, .Increment = 0.05D, .Width = 80}
    Private _stopEbay As Boolean

    ' Pages and the buttons that switch between them
    Private ReadOnly _pages As New List(Of Control)
    Private ReadOnly _navButtons As New List(Of Button)
    Private ReadOnly _searchTimer As New Timer With {.Interval = 250}

    ' Missing issues tab
    Private ReadOnly _gapsGrid As DataGridView = Ui.MakeGrid()
    Private ReadOnly _missingList As New ListBox With {.Dock = DockStyle.Fill, .SelectionMode = SelectionMode.MultiExtended, .IntegralHeight = False}
    Private ReadOnly _missingTitle As New Label With {.Dock = DockStyle.Top, .Height = 28, .TextAlign = ContentAlignment.MiddleLeft}
    Private ReadOnly _findLengths As Button = Ui.MakeButton("Find how long each series is", AddressOf OnFindLengths)
    Private ReadOnly _showHidden As New Button With {.AutoSize = True, .MinimumSize = New Size(0, 34), .Padding = New Padding(10, 2, 10, 2), .Visible = False}
    Private ReadOnly _lengthsStatus As New Label With {.AutoSize = True, .Margin = New Padding(3, 12, 3, 3), .Tag = Theme.MutedTag}
    Private _findingLengths As Boolean
    Private _stopLengths As Boolean

    ' Wishlist tab
    Private ReadOnly _wishGrid As DataGridView = Ui.MakeGrid()

    ' Stats tab
    Private ReadOnly _statsByValue As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = 190}
    Private ReadOnly _statsNote As New Label With {.AutoSize = True, .Margin = New Padding(12, 10, 3, 3), .Tag = Theme.MutedTag}
    Private ReadOnly _statsCards As New FlowLayoutPanel With {.Dock = DockStyle.Top, .AutoSize = True, .Padding = New Padding(0, 0, 0, 10)}
    Private ReadOnly _chartPublishers As New ChartBox("Publishers")
    Private ReadOnly _chartDecades As New ChartBox("Decades (by cover date)")
    Private ReadOnly _chartSeries As New ChartBox("Top series")
    Private ReadOnly _chartWriters As New ChartBox("Top writers")
    Private ReadOnly _chartArtists As New ChartBox("Top artists")
    Private ReadOnly _chartValuable As New ChartBox("Most valuable comics") With {.ByValue = True}
    Private ReadOnly _chartHistory As New ChartBox("Your collection's value over time")
    Private ReadOnly _chartSpending As New ChartBox("Spent each month (by Date bought)") With {.ByValue = True}
    Private ReadOnly _chartReading As New ChartBox("Comics read each month")
    Private ReadOnly _chartKeys As New ChartBox("Your key issues") With {.ByValue = True}
    Private ReadOnly _chartRising As New ChartBox("Rising on eBay (US$ up on a month or more ago)") With {.ByValue = True}
    Private ReadOnly _budget As New NumericUpDown With {.Maximum = 100000, .Width = 90, .DecimalPlaces = 0, .Increment = 10}
    Private ReadOnly _readGoal As New NumericUpDown With {.Maximum = 1000, .Width = 70}
    Private ReadOnly _yearGoal As New NumericUpDown With {.Maximum = 10000, .Width = 80}
    Private ReadOnly _chartMostRead As New ChartBox("Series you've read most")
    Private _loadingGoals As Boolean

    ' Lent out tab
    Private ReadOnly _loansGrid As DataGridView = Ui.MakeGrid()
    Private ReadOnly _showReturned As New CheckBox With {.Text = "Show ones given back too", .AutoSize = True, .Margin = New Padding(12, 10, 3, 3)}
    Private ReadOnly _loansNote As New Label With {.AutoSize = True, .Margin = New Padding(12, 10, 3, 3), .Tag = Theme.MutedTag}

    ' Sets tab (story arcs and runs)
    Private ReadOnly _setsGrid As DataGridView = Ui.MakeGrid()
    Private ReadOnly _findArcs As Button = Ui.MakeButton("Find story arcs", AddressOf OnFindArcs)
    Private ReadOnly _arcStatus As New Label With {.AutoSize = True, .Margin = New Padding(3, 12, 3, 3), .Tag = Theme.MutedTag}
    Private _findingArcs As Boolean
    Private _stopArcs As Boolean

    ' New releases tab
    Private ReadOnly _releasesGrid As DataGridView = Ui.MakeGrid()
    Private ReadOnly _checkReleases As Button = Ui.MakeButton("Check for new releases", AddressOf OnCheckReleases, Theme.PrimaryTag)
    Private ReadOnly _releaseStatus As New Label With {.AutoSize = True, .Margin = New Padding(3, 12, 3, 3), .Tag = Theme.MutedTag}
    Private _checkingReleases As Boolean
    Private _stopReleases As Boolean

    ' Settings tab
    Private ReadOnly _relayUrl As New TextBox With {.Width = 420, .PlaceholderText = "https://comic-relay.yourname.workers.dev"}
    Private ReadOnly _relayStatus As New Label With {.AutoSize = True, .ForeColor = SystemColors.GrayText}
    Private ReadOnly _dbPath As New Label With {.AutoSize = True, .ForeColor = SystemColors.GrayText}
    Private ReadOnly _syncCode As New TextBox With {.Width = 260, .PlaceholderText = "ABCD-EFGH-JKLM-NPQR", .CharacterCasing = CharacterCasing.Upper}
    Private ReadOnly _syncStatus As New Label With {.AutoSize = True, .ForeColor = SystemColors.GrayText}
    Private ReadOnly _backupStatus As New Label With {.AutoSize = True, .MaximumSize = New Size(640, 0), .ForeColor = SystemColors.GrayText}
    Private ReadOnly _backupTimer As New Timer With {.Interval = 60 * 60 * 1000}
    Private ReadOnly _digitalFolderLabel As New Label With {.AutoSize = True, .ForeColor = SystemColors.GrayText}

    ' Phone gets a copy of the collection and wishlist; jobs that run by themselves; updates
    Private _lastLibrarySent As String = ""
    Private _lastLibrarySentAt As DateTime = DateTime.MinValue
    Private _sendingLibrary As Boolean
    Private ReadOnly _autoTimer As New Timer With {.Interval = 2 * 60 * 1000}
    Private _autoRunning As Boolean
    Private ReadOnly _autoJobs As New CheckBox With {.AutoSize = True, .Text = "Find covers, story arcs and new releases by themselves while the app is open"}
    Private ReadOnly _autoStatus As New Label With {.AutoSize = True, .ForeColor = SystemColors.GrayText}
    Private ReadOnly _updateStatus As New Label With {.AutoSize = True, .ForeColor = SystemColors.GrayText}
    Private _checkingUpdate As Boolean

    ' Banner picture across the top
    Private ReadOnly _banner As New Panel With {.Dock = DockStyle.Top, .Height = 150, .BackColor = Color.FromArgb(20, 22, 28), .Visible = False}
    Private _bannerImage As Image

    Public Sub New()
        Text = "Comic Catalog"
        Theme.TextScale = If({1.0F, 1.15F, 1.3F}.Contains(_settings.TextScale), _settings.TextScale, 1.0F)
        Icon = AppIcon.Load()
        AutoScaleMode = AutoScaleMode.Font
        Font = New Font("Segoe UI", 9.5F)
        ClientSize = New Size(1100, 700)
        MinimumSize = New Size(800, 500)
        StartPosition = FormStartPosition.CenterScreen
        ' Back where it was last time, as long as that's still on a screen.
        Dim was = _settings.MainBounds
        If was IsNot Nothing AndAlso was.Length = 4 AndAlso was(2) >= MinimumSize.Width AndAlso was(3) >= MinimumSize.Height Then
            Dim r As New Rectangle(was(0), was(1), was(2), was(3))
            If Screen.AllScreens.Any(Function(sc) sc.WorkingArea.IntersectsWith(New Rectangle(r.X, r.Y, r.Width, 40))) Then
                StartPosition = FormStartPosition.Manual
                Bounds = r
            End If
        End If

        Dim content As New Panel With {.Dock = DockStyle.Fill, .Padding = New Padding(16, 12, 16, 12)}
        ' Collection and Trades share one page: the list just shows single issues or trades.
        Dim collectionPage = BuildCollectionTab()
        _pages.AddRange({BuildHomeTab(), collectionPage, collectionPage, BuildBoxesTab(), BuildDigitalTab(), BuildVideosTab(), BuildStatsTab(), BuildMissingTab(),
                         BuildWishlistTab(), BuildSetsTab(), BuildReleasesTab(), BuildLoansTab(), BuildSettingsTab()})
        For Each p In _pages.Distinct()
            p.Dock = DockStyle.Fill
            p.Visible = False
            content.Controls.Add(p)
        Next

        ' Side menu: app name and a button for each page, with icons
        Dim side As New Panel With {.Dock = DockStyle.Left, .Width = 214, .BackColor = Theme.Panel, .Padding = New Padding(10, 0, 10, 10)}
        Dim title As New Label With {.Text = "COMIC" & vbCrLf & "CATALOG", .Dock = DockStyle.Top, .Height = 86, .ForeColor = Theme.Accent,
                                     .Font = New Font("Segoe UI Black", 17.0F), .TextAlign = ContentAlignment.MiddleLeft,
                                     .Padding = New Padding(8, 6, 0, 0), .BackColor = Theme.Panel}
        Dim nav As New FlowLayoutPanel With {.Dock = DockStyle.Fill, .FlowDirection = FlowDirection.TopDown, .WrapContents = False,
                                             .AutoScroll = True, .BackColor = Theme.Panel}
        For i = 0 To PageNames.Length - 1
            Dim index = i
            Dim b As New Button With {.Text = "  " & PageNames(i), .Size = New Size(190, 42), .FlatStyle = FlatStyle.Flat,
                                      .Font = New Font("Segoe UI Semibold", 10.5F), .TextAlign = ContentAlignment.MiddleLeft,
                                      .ImageAlign = ContentAlignment.MiddleLeft, .TextImageRelation = TextImageRelation.ImageBeforeText,
                                      .Padding = New Padding(10, 0, 0, 0), .Cursor = Cursors.Hand, .Margin = New Padding(0, 0, 0, 2)}
            b.FlatAppearance.BorderSize = 0
            b.FlatAppearance.MouseOverBackColor = Theme.Panel2
            AddHandler b.Click, Sub(s, e) ShowPage(index)
            _navButtons.Add(b)
            nav.Controls.Add(b)
        Next
        side.Controls.Add(nav)
        side.Controls.Add(title)

        AddHandler _banner.Paint, AddressOf PaintBanner
        AddHandler _banner.Resize, Sub(s, e) _banner.Invalidate()
        Controls.Add(content)
        Controls.Add(_banner)
        Controls.Add(side)
        Theme.Apply(Me)
        _grid.RowTemplate.Height = CInt(50 * Theme.TextScale)
        side.BackColor = Theme.Panel
        nav.BackColor = Theme.Panel
        title.ForeColor = Theme.Accent
        ShowPage(0)
        SetView(False)
        SetBoxesVisible(_settings.ShowBoxes)
    End Sub

    Protected Overrides Sub OnHandleCreated(e As EventArgs)
        MyBase.OnHandleCreated(e)
        Theme.DarkTitleBar(Me)
    End Sub

    ''' <summary>The pages, in the order of the buttons across the top.</summary>
    Private Shared ReadOnly PageNames As String() = {"Home", "Collection", "Trades", "Boxes", "Digital", "Videos", "Stats", "Missing issues", "Wishlist", "Sets", "New releases", "Lent out", "Settings"}
    Private Shared ReadOnly PageIcons As Char() = {Icons.Home, Icons.Library, Icons.Book, Icons.BoxIcon, Icons.Reading, Icons.Video, Icons.Chart, Icons.Search, Icons.Star,
                                                   Icons.List, Icons.Calendar, Icons.People, Icons.Settings}

    Private _currentPage As Integer = -1

    Private Sub ShowPage(name As String)
        ShowPage(Array.IndexOf(PageNames, name))
    End Sub

    Private Sub ShowPage(index As Integer)
        Dim leaving = _currentPage
        _currentPage = index
        For i = 0 To _pages.Count - 1
            _pages(i).Visible = _pages(i) Is _pages(index)
            Dim selected = i = index
            _navButtons(i).ForeColor = If(selected, Theme.Accent, Theme.Muted)
            _navButtons(i).BackColor = If(selected, Theme.Panel2, Theme.Panel)
            _navButtons(i).FlatAppearance.BorderSize = 0
            Dim old = _navButtons(i).Image
            _navButtons(i).Image = Icons.Make(PageIcons(i), If(selected, Theme.Accent, Theme.Muted), 20)
            old?.Dispose()
        Next
        _pages(index).BringToFront()
        If _db Is Nothing Then Return
        ' Videos you've now seen stop being "new" once you move on.
        If leaving >= 0 AndAlso PageNames(leaving) = "Videos" AndAlso index <> leaving Then
            _db.MarkVideosSeen()
            UpdateVideosButton()
        End If
        Select Case PageNames(index)
            Case "Home" : RefreshHome()
            Case "Digital" : RefreshDigital()
            Case "Videos" : RefreshVideos()
            Case "Collection", "Trades"
                Dim trades = PageNames(index) = "Trades"
                If trades <> _tradesView Then
                    _tradesView = trades
                    RefreshCollection()
                End If
                _barcode.Focus()
            Case "Boxes" : RefreshBoxesPage()
            Case "Stats" : RefreshStats()
            Case "Missing issues" : RefreshGaps()
            Case "Wishlist" : RefreshWishlist()
            Case "Sets" : RefreshSets()
            Case "New releases" : RefreshReleases()
            Case "Lent out" : RefreshLoans()
        End Select
    End Sub

    Protected Overrides Sub OnLoad(e As EventArgs)
        MyBase.OnLoad(e)
        If _settings.MainMaximized Then WindowState = FormWindowState.Maximized
        ComicForm.StartMaximized = _settings.EditMaximized
        Try
            _db = New ComicDb(_settings.DatabasePath)
        Catch ex As Exception
            Ui.ShowError(Me, $"Couldn't open the database at {_settings.DatabasePath}: {Ui.Friendly(ex)}")
            Close()
            Return
        End Try
        _relayUrl.Text = _settings.RelayUrl
        _syncCode.Text = _settings.SyncCode
        ShowBanner()
        _dbPath.Text = $"Your collection is saved in {_settings.DatabasePath}"
        ' Series lengths from the first version could be another run's (it guessed the longest); look them up again.
        If _settings.SeriesTotalsVersion < 2 Then
            Try
                _db.ResetSeriesTotals()
                _settings.SeriesTotalsVersion = 2
                SaveSettingsQuietly()
            Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is Microsoft.Data.Sqlite.SqliteException
            End Try
        End If
        ' Comics in the bin longer than 30 days go for good.
        Try
            _db.EmptyBin(olderThanDays:=ComicDb.BinDays)
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is Microsoft.Data.Sqlite.SqliteException
        End Try
        RefreshCollection()
        RefreshHome()
        StartVideos()
        ShowEbayStatus()
        UpdateDealsButton()
        _barcode.Focus()
        StartScanChecks()
        AddHandler _backupTimer.Tick, Sub(s, ev) BackupIfDue()
        _backupTimer.Start()
        BackupIfDue()
        _autoJobs.Checked = _settings.AutoJobs
        AddHandler _autoJobs.CheckedChanged, AddressOf OnAutoJobsChanged
        AddHandler _autoTimer.Tick, Sub(s, ev) RunAutoJobs()
        _autoTimer.Enabled = _settings.AutoJobs
        If Environment.ProcessPath IsNot Nothing Then Updater.CleanUp(Environment.ProcessPath)
        ShowVersion()
        Dim build = Updater.CurrentBuild()
        If build > 0 Then Text = $"Comic Catalog (build {build})"
        If build > 0 AndAlso build > _settings.LastRunBuild Then
            Dim previous = _settings.LastRunBuild
            _settings.LastRunBuild = build
            _settings.Save()
            ' Only after an update, not the very first time the app runs.
            If File.Exists(_settings.DatabasePath) AndAlso Environment.GetEnvironmentVariable("COMICCATALOG_NO_UPDATE") Is Nothing Then
                BeginInvoke(Sub() ShowWhatsNew(build, previous))
            End If
        End If
        If Not EditFirstOnStart AndAlso Environment.GetEnvironmentVariable("COMICCATALOG_NO_UPDATE") Is Nothing Then
            Dim once As New Timer With {.Interval = 8000}
            AddHandler once.Tick, Sub(s, ev)
                                      once.Dispose()
                                      CheckForUpdate(quiet:=True)
                                  End Sub
            once.Start()
        End If
    End Sub

    ' ---------- jobs that run by themselves ----------

    Private Sub OnAutoJobsChanged(sender As Object, e As EventArgs)
        _settings.AutoJobs = _autoJobs.Checked
        _settings.Save()
        _autoTimer.Enabled = _autoJobs.Checked
        _autoStatus.Text = If(_autoJobs.Checked, "On. The first run starts in a couple of minutes.", "Off. Use the buttons on each page instead.")
    End Sub

    ''' <summary>
    ''' Every 15 minutes (the first time 2 minutes after opening): fills in missing covers and prices,
    ''' then story arcs, then checks for new releases once a week. Quiet, and skips anything you've
    ''' already started yourself.
    ''' </summary>
    Private Async Sub RunAutoJobs()
        _autoTimer.Interval = 15 * 60 * 1000
        If _autoRunning OrElse _db Is Nothing OrElse Not _settings.AutoJobs OrElse _settings.RelayUrl = "" Then Return
        If _findingCovers OrElse _findingArcs OrElse _checkingReleases OrElse _findingEbay OrElse _findingLengths Then Return
        _autoRunning = True
        Try
            If _db.ComicsNeedingCovers().Count > 0 Then Await RunFindCovers(quiet:=True)
            If IsDisposed OrElse Not _settings.AutoJobs Then Return
            If _db.ComicsNeedingArcs(withCredits:=True).Count > 0 Then Await RunFindArcs(quiet:=True)
            If IsDisposed OrElse Not _settings.AutoJobs Then Return
            If _db.CountMatchedNeedingVolumes() > 0 Then Await RunSortVolumes(quiet:=True)
            If IsDisposed OrElse Not _settings.AutoJobs Then Return
            If _db.SeriesNeedingTotals().Count > 0 Then Await RunFindLengths(quiet:=True)
            If IsDisposed OrElse Not _settings.AutoJobs Then Return
            If Not _settings.LastReleaseCheck.HasValue OrElse DateTime.Now - _settings.LastReleaseCheck.Value > TimeSpan.FromDays(7) Then
                Await RunCheckReleases(quiet:=True)
            End If
            If IsDisposed OrElse Not _settings.AutoJobs Then Return
            ' eBay allows 5,000 searches a day, so a few hundred at a time is plenty.
            If _db.ComicsNeedingEbay().Count > 0 Then Await RunEbayEstimates(quiet:=True, limit:=300)
            If IsDisposed OrElse Not _settings.AutoJobs Then Return
            If _db.WishesNeedingDealCheck().Count > 0 Then Await RunDealChecks(quiet:=True)
            If IsDisposed OrElse Not _settings.AutoJobs Then Return
            Await RunGradedChecks(limit:=100)
            If Not IsDisposed Then _autoStatus.Text = $"Last ran at {DateTime.Now:t}"
        Catch ex As Exception
            If Not IsDisposed Then _autoStatus.Text = $"Stopped at {DateTime.Now:t}: {Ui.Friendly(ex)}"
        Finally
            _autoRunning = False
        End Try
    End Sub

    ' ---------- updates ----------

    Private Sub ShowVersion()
        Dim build = Updater.CurrentBuild()
        _updateStatus.Text = If(build = 0, "This copy wasn't built by GitHub, so it can't update itself.", $"You have build {build}.")
    End Sub

    Private Async Sub CheckForUpdate(quiet As Boolean)
        If _checkingUpdate OrElse IsDisposed Then Return
        _checkingUpdate = True
        Try
            If Not quiet Then _updateStatus.Text = "Checking…"
            Dim found = Await Updater.CheckAsync()
            If IsDisposed Then Return
            If found Is Nothing Then
                ShowVersion()
                If Not quiet Then _updateStatus.Text &= " That's the newest."
                Return
            End If
            If quiet AndAlso found.Build = _settings.SkippedBuild Then
                _updateStatus.Text = $"Build {found.Build} is ready. Click Check for updates to get it."
                Return
            End If
            Dim notes = If(found.Notes = "", "", vbCrLf & vbCrLf & "What's new:" & vbCrLf & Shorten(found.Notes, 700))
            Dim answer = MessageBox.Show(Me, $"A newer version of Comic Catalog is ready (build {found.Build}, you have {Updater.CurrentBuild()}).{notes}" &
                                         vbCrLf & vbCrLf & "Update now? The app restarts by itself. Your comics aren't touched.",
                                         "Update Comic Catalog", MessageBoxButtons.YesNo, MessageBoxIcon.Information)
            If answer <> DialogResult.Yes Then
                _settings.SkippedBuild = found.Build
                _settings.Save()
                _updateStatus.Text = $"Build {found.Build} is ready. Click Check for updates to get it."
                Return
            End If
            Dim exe = Environment.ProcessPath
            If exe Is Nothing Then Throw New InvalidOperationException("Couldn't find where the app is saved.")
            Await Updater.InstallAsync(found, exe, Sub(pct) BeginInvoke(Sub() _updateStatus.Text = $"Downloading… {pct}%"))
            BackupNow(quiet:=True)
            Process.Start(New ProcessStartInfo With {.FileName = exe, .UseShellExecute = True})
            Close()
        Catch ex As Exception
            If IsDisposed Then Return
            ShowVersion()
            If Not quiet Then Ui.ShowError(Me, $"Couldn't update: {Ui.Friendly(ex)}")
        Finally
            _checkingUpdate = False
        End Try
    End Sub

    ''' <summary>Says the update worked and what it brought.</summary>
    Private Async Sub ShowWhatsNew(build As Integer, previous As Integer)
        Dim notes = Await Updater.NotesForAsync(build)
        If IsDisposed Then Return
        Dim from = If(previous > 0, $" (from build {previous})", "")
        MessageBox.Show(Me, $"Comic Catalog has been updated to build {build}{from}." &
                        If(notes = "", "", vbCrLf & vbCrLf & "What's new:" & vbCrLf & Shorten(notes, 900)),
                        "Updated", MessageBoxButtons.OK, MessageBoxIcon.Information)
    End Sub

    Private Shared Function Shorten(text As String, max As Integer) As String
        Return If(text.Length <= max, text, text.Substring(0, max).TrimEnd() & "…")
    End Function

    ' ---------- backups ----------

    ''' <summary>Makes the day's backup if the last one is more than about a day old. Runs at start and every hour.</summary>
    Private Sub BackupIfDue()
        If _db Is Nothing Then Return
        ' Also notes today's value for the Stats page (once a day is enough, but it's cheap).
        Try
            _db.RecordValueHistory()
        Catch ex As Exception
        End Try
        If _settings.LastBackup.HasValue AndAlso DateTime.Now - _settings.LastBackup.Value < TimeSpan.FromHours(20) Then
            ShowBackupStatus()
            Return
        End If
        BackupNow(quiet:=True)
    End Sub

    Private Sub BackupNow(quiet As Boolean)
        Try
            Dim file = Backups.MakeBackup(_db, _settings.BackupFolderOrDefault())
            Backups.CopyPhotos(_db, _settings.BackupFolderOrDefault())
            _settings.LastBackup = DateTime.Now
            _settings.Save()
            ShowBackupStatus()
            If Not quiet Then MessageBox.Show(Me, $"Backed up to {file}", "Backup")
        Catch ex As Exception
            _backupStatus.Text = $"The last backup didn't work: {Ui.Friendly(ex)}"
            If Not quiet Then Ui.ShowError(Me, $"Couldn't back up: {Ui.Friendly(ex)}")
        End Try
    End Sub

    Private Sub ShowBackupStatus()
        Dim last = If(_settings.LastBackup.HasValue, _settings.LastBackup.Value.ToString("d MMM yyyy 'at' h:mm tt", CultureInfo.CurrentCulture), "not yet")
        _backupStatus.Text = $"Backups go to {_settings.BackupFolderOrDefault()}" & vbCrLf & $"Last backup: {last}"
    End Sub

    Private Sub OnChooseBackupFolder(sender As Object, e As EventArgs)
        Using dlg As New FolderBrowserDialog With {.Description = "Pick a folder for backups (a OneDrive or iCloud Drive folder keeps them off this computer)",
                                                   .UseDescriptionForTitle = True, .SelectedPath = _settings.BackupFolderOrDefault()}
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            _settings.BackupFolder = dlg.SelectedPath
            _settings.Save()
            BackupNow(quiet:=False)
        End Using
    End Sub

    Private Sub OnOpenBackupFolder(sender As Object, e As EventArgs)
        Dim folder = _settings.BackupFolderOrDefault()
        Directory.CreateDirectory(folder)
        Process.Start(New ProcessStartInfo With {.FileName = folder, .UseShellExecute = True})
    End Sub

    Private Sub OnRestoreBackup(sender As Object, e As EventArgs)
        Dim folder = _settings.BackupFolderOrDefault()
        Using dlg As New OpenFileDialog With {.Filter = "Comic Catalog backup (*.db)|*.db", .Title = "Pick the backup to bring back",
                                              .InitialDirectory = If(Directory.Exists(folder), folder, "")}
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            If MessageBox.Show(Me, $"Replace your whole collection with the backup {Path.GetFileName(dlg.FileName)}?" & vbCrLf & vbCrLf &
                               "A copy of what you have now is saved in the backup folder first, so you can change your mind.",
                               "Restore a backup", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) <> DialogResult.OK Then Return
            Try
                Dim safety = Path.Combine(folder, $"comics-before-restore-{DateTime.Now.ToString("yyyy-MM-dd-HHmm", CultureInfo.InvariantCulture)}.db")
                _db.BackupTo(safety)
                _db.RestoreFrom(dlg.FileName)
                ' An older backup is brought up to date, the same as an older database.
                _db = New ComicDb(_settings.DatabasePath)
                Backups.RestorePhotos(_db, folder)
                RefreshCollection()
                MessageBox.Show(Me, "Your collection is back to how it was in that backup." & vbCrLf & vbCrLf &
                                $"What you had a moment ago is saved as {Path.GetFileName(safety)}.", "Restore a backup")
            Catch ex As Exception
                Ui.ShowError(Me, $"Couldn't restore that backup: {Ui.Friendly(ex)}")
            End Try
        End Using
    End Sub

    ' ---------- scans sent from the phone ----------

    Private Sub StartScanChecks(Optional collectNow As Boolean = True)
        Dim syncOn = _settings.SyncCode <> "" AndAlso _settings.RelayUrl <> ""
        _getScans.Visible = syncOn
        _scanNote.Visible = syncOn
        _scanTimer.Enabled = syncOn
        If syncOn AndAlso collectNow Then CollectScans(quiet:=True)
        If syncOn Then SendLibraryIfChanged()
        If syncOn Then PullReadingPlaces()
    End Sub

    ''' <summary>
    ''' Sends the phone a copy of what you own and what's on your wishlist, so scanning in a shop can
    ''' say "you own this". Only sends when something changed. Quiet: needs relay version 6.
    ''' </summary>
    Private Async Sub SendLibraryIfChanged()
        If _sendingLibrary OrElse _db Is Nothing OrElse _settings.SyncCode = "" OrElse _settings.RelayUrl = "" Then Return
        _sendingLibrary = True
        Try
            Dim snapshot = _db.LibrarySnapshot()
            If snapshot = _lastLibrarySent Then Return
            ' Each send is a "write" on Cloudflare's free plan (1,000 a day), and finding covers
            ' changes the list every few seconds, so send at most every 30 minutes.
            If _lastLibrarySent <> "" AndAlso DateTime.Now - _lastLibrarySentAt < TimeSpan.FromMinutes(30) Then Return
            Dim metron = Me.Metron
            If Not Await RelayReady(metron, "send your collection to the phone", 6, quiet:=True) Then Return
            Await metron.SendLibraryAsync(_settings.SyncCode, snapshot)
            _lastLibrarySent = snapshot
            _lastLibrarySentAt = DateTime.Now
        Catch ex As Exception
            ' Tried again on the next tick.
        Finally
            _sendingLibrary = False
        End Try
    End Sub

    ''' <summary>
    ''' Adds comics the phone has sent. Runs every couple of minutes while the app is open (quietly),
    ''' or when you click Get scans from phone.
    ''' </summary>
    Private Async Sub CollectScans(quiet As Boolean)
        If _collecting OrElse _db Is Nothing OrElse _settings.SyncCode = "" Then Return
        _collecting = True
        _getScans.Enabled = False
        Try
            Dim added = Await CollectScansAsync()
            If IsDisposed Then Return
            If added > 0 Then
                _scanNote.Text = $"{added:N0} added or boxed from your phone at {DateTime.Now:t}"
            ElseIf Not quiet Then
                _scanNote.Text = $"No new scans ({DateTime.Now:t})"
            End If
            If Not quiet AndAlso added = 0 Then
                MessageBox.Show(Me, "No new scans waiting. Comics are sent once the phone has looked up their details.", "Scans from phone")
            End If
        Catch ex As Exception
            If IsDisposed Then Return
            _scanNote.Text = "Couldn't check for scans"
            If Not quiet Then Ui.ShowError(Me, Ui.Friendly(ex, sentence:=True))
        Finally
            _collecting = False
            If Not IsDisposed Then _getScans.Enabled = True
        End Try
    End Sub

    Private Async Function CollectScansAsync() As Task(Of Integer)
        Dim added = 0
        Dim metron = Me.Metron
        For Each batch In Await metron.InboxAsync(_settings.SyncCode)
            ' Saved first, then removed from the mailbox, so nothing is lost if the connection drops.
            Dim result = _db.ImportPhoneCsv(batch.Csv)
            added += result.Added + result.Moved
            Await metron.DeleteInboxAsync(_settings.SyncCode, batch.Id)
        Next
        If added > 0 Then RefreshCollection()
        Return added
    End Function

    Private Async Sub OnSaveSyncCode(sender As Object, e As EventArgs)
        Dim code = MetronClient.InboxCode(_syncCode.Text)
        If code <> "" AndAlso code.Length <> 16 Then
            _syncStatus.Text = "The code has 16 letters and numbers. Check it against your phone."
            Return
        End If
        _settings.SyncCode = code
        _settings.Save()
        _syncCode.Text = If(code = "", "", String.Join("-", Enumerable.Range(0, 4).Select(Function(i) code.Substring(i * 4, 4))))
        StartScanChecks(collectNow:=False)
        If code = "" Then
            _syncStatus.Text = "Turned off."
            Return
        End If
        If _settings.RelayUrl = "" Then
            _syncStatus.Text = "Saved. Add your relay address above too, so the app can reach the mailbox."
            Return
        End If
        If _collecting Then Return
        _collecting = True
        _syncStatus.Text = "Checking the mailbox…"
        Try
            Dim added = Await CollectScansAsync()
            _syncStatus.Text = "Connected. " & If(added > 0, $"Added {added:N0} comics from your phone.", "New scans will appear in your collection every couple of minutes while the app is open.")
        Catch ex As Exception
            _syncStatus.Text = $"Saved, but checking failed: {Ui.Friendly(ex)}"
        Finally
            _collecting = False
        End Try
    End Sub

    Private ReadOnly Property Metron As MetronClient
        Get
            Return New MetronClient(_settings.RelayUrl)
        End Get
    End Property

    ' ---------- Collection ----------

    Private Function BuildCollectionTab() As Control
        Dim page As New Panel()
        _barcode.Font = New Font("Segoe UI", 12.0F)
        _search.Font = New Font("Segoe UI", 12.0F)
        ' Two short rows: finding and adding comics, then what to do with the selected ones.
        ' Less-used actions live in the More menu so the list gets the room.
        Dim bar As New FlowLayoutPanel With {.Dock = DockStyle.Top, .AutoSize = True, .Padding = New Padding(0, 0, 0, 2)}
        bar.Controls.AddRange({
            Ui.MakeLabel("Barcode"), _barcode, Ui.MakeButton("Look up and add", AddressOf OnBarcodeEntered, Theme.PrimaryTag),
            Ui.MakeButton("Add by hand", AddressOf OnAddByHand),
            New Label With {.Width = 16},
            Ui.MakeLabel("Search"), _search, _readFilter, _publisherFilter,
            New Label With {.Width = 16},
            _getScans, _scanNote})
        Dim actions As New FlowLayoutPanel With {.Dock = DockStyle.Top, .AutoSize = True, .Padding = New Padding(0, 0, 0, 6)}
        actions.Controls.AddRange({
            _listButton, _coversButton, _coverSize, _boxesButton, _findCovers,
            New Label With {.Width = 16},
            Ui.MakeButton("Edit", AddressOf OnEdit),
            Ui.MakeButton("Put in box…", AddressOf OnSetBox),
            Ui.MakeButton("Read / unread", AddressOf OnToggleRead),
            Ui.MakeButton("Lend…", AddressOf OnLend),
            Ui.MakeButton("Delete", AddressOf OnDelete, Theme.DangerTag),
            Ui.MakeButton("More ▾", Sub(s, e)
                                        Dim b = DirectCast(s, Control)
                                        _moreMenu.Show(b, New Point(0, b.Height))
                                    End Sub)})
        _search.Width = 200
        _barcode.Width = 200
        For Each b In bar.Controls.OfType(Of Button)().Concat(actions.Controls.OfType(Of Button)())
            Dim glyph = ButtonIcon(b.Text)
            If glyph <> ChrW(0) Then Icons.Put(b, glyph)
        Next
        AddHandler _scanTimer.Tick, Sub(s, e)
                                        CollectScans(quiet:=True)
                                        SendLibraryIfChanged()
                                        PullReadingPlaces()
                                    End Sub
        _readFilter.Items.AddRange({"All", "Read", "Not read", "Key issues", "For trade or sale", "Lent out", "No cover", "Signed",
                                    NeedsFixing, "No value"})
        _moreMenu.Items.Add("Edit several at once…", Nothing, Sub(s, e)
                                                              Dim ids = PickedComics()
                                                              If ids IsNot Nothing Then EditMany(ids)
                                                          End Sub)
        _moreMenu.Items.Add("Put in a box or on a shelf…", Nothing, AddressOf OnSetBox)
        _moreMenu.Items.Add("Add more issues of this series…", Nothing, AddressOf OnAddMoreIssues)
        _moreMenu.Items.Add("Look up covers and details again", Nothing, Async Sub(s, e)
                                                                  Dim ids = PickedComics()
                                                                  If ids Is Nothing Then Return
                                                                  If _db.ResetCoverChecks(ids) = 0 Then
                                                                      MessageBox.Show(Me, "Those already have their covers and details.", "Look up again")
                                                                      Return
                                                                  End If
                                                                  Await RunFindCovers(quiet:=False)
                                                              End Sub)
        _moreMenu.Items.Add("Sort this series into volumes", Nothing, Async Sub(s, e)
                                                                          Dim ids = PickedComics()
                                                                          Dim c = If(ids Is Nothing, Nothing, _db.GetComic(ids(0)))
                                                                          If c IsNot Nothing Then Await RunSortVolumes(quiet:=False, seriesName:=c.Series)
                                                                      End Sub)
        _moreMenu.Items.Add("Tidy doubled-up issues in this series", Nothing, Sub(s, e)
                                                                                  Dim ids = PickedComics()
                                                                                  Dim c = If(ids Is Nothing, Nothing, _db.GetComic(ids(0)))
                                                                                  If c IsNot Nothing Then TidyDoubles(c.Series, quiet:=False)
                                                                              End Sub)
        _moreMenu.Items.Add("Merge this series into another…", Nothing, Sub(s, e)
                                                                           Dim ids = PickedComics()
                                                                           Dim c = If(ids Is Nothing, Nothing, _db.GetComic(ids(0)))
                                                                           If c IsNot Nothing Then MergeSeriesFrom(c.Series, c.Volume)
                                                                       End Sub)
        _moreMenu.Items.Add("Make a set…", Nothing, AddressOf OnMakeSet)
        _moreMenu.Items.Add("What should I read next?", Nothing, AddressOf OnReadNext)
        _moreMenu.Items.Add("Add from a shop receipt…", Nothing, AddressOf OnAddFromReceipt)
        _moreMenu.Items.Add("Export list…", Nothing, AddressOf OnExport)
        _moreMenu.Items.Add("Share page…", Nothing, Sub(s, e) OnShare(wishlistOnly:=False))
        _moreMenu.Items.Add("Deleted comics (bin)…", Nothing, AddressOf OnShowBin)
        _moreMenu.Items.Add(New ToolStripSeparator())
        _moreMenu.Items.Add("For trade or sale…", Nothing, AddressOf OnForTrade)
        _moreMenu.Items.Add("Not for trade", Nothing, AddressOf OnNotForTrade)
        _moreMenu.Items.Add("Mark as key issue…", Nothing, AddressOf OnKeyIssue)
        _moreMenu.Items.Add("Rate…", Nothing, AddressOf OnRate)
        _moreMenu.Items.Add(New ToolStripSeparator())
        _moreMenu.Items.Add("Read digital copy", Nothing, AddressOf OnReadDigitalCopy)
        _moreMenu.Items.Add("Add a digital copy…", Nothing, AddressOf OnAddDigitalCopy)
        _moreMenu.Items.Add(New ToolStripSeparator())
        _moreMenu.Items.Add("Insurance report…", Nothing, AddressOf OnInsurance)
        _moreMenu.Items.Add("Print box labels…", Nothing, AddressOf OnBoxLabels)
        _moreMenu.Items.Add("Convention signing list…", Nothing, AddressOf OnSigningList)
        _moreMenu.Items.Add("Worth getting graded?…", Nothing, AddressOf OnWorthGrading)
        AddHandler _ebayItem.Click, AddressOf OnEstimateEbay
        _moreMenu.Items.Add(_ebayItem)
        _moreMenu.Items.Add("Use eBay estimates as values…", Nothing, AddressOf OnUseEbay)
        _moreMenu.Font = New Font("Segoe UI", 10.0F)
        _grid.ContextMenuStrip = _moreMenu
        _covers.ContextMenuStrip = _moreMenu
        ' Right-clicking a row that isn't selected selects just that one, so the menu acts on what you clicked.
        AddHandler _grid.CellMouseDown, Sub(s, e)
                                            If e.Button <> MouseButtons.Right OrElse e.RowIndex < 0 Then Return
                                            If Not _grid.Rows(e.RowIndex).Selected Then
                                                _grid.ClearSelection()
                                                _grid.Rows(e.RowIndex).Selected = True
                                            End If
                                        End Sub
        _readFilter.SelectedIndex = 0
        _readFilter.Font = New Font("Segoe UI", 11.0F)
        AddHandler _readFilter.SelectedIndexChanged, Sub(s, e) RefreshCollection()
        _publisherFilter.Font = New Font("Segoe UI", 11.0F)
        _publisherFilter.Items.Add("All publishers")
        _publisherFilter.SelectedIndex = 0
        AddHandler _publisherFilter.SelectedIndexChanged, Sub(s, e)
                                                               If Not _fillingPublishers Then RefreshCollection()
                                                           End Sub

        ' Totals as big number cards
        Dim cards As New FlowLayoutPanel With {.Dock = DockStyle.Top, .AutoSize = True, .Padding = New Padding(0, 0, 0, 8)}
        cards.Controls.AddRange({StatCard(_statComics, "COMICS", _statComicsCaption, compact:=True), StatCard(_statCopies, "COPIES", compact:=True),
                                 StatCard(_statValue, "COLLECTION VALUE", compact:=True), StatCard(_statPaid, "TOTAL PAID", compact:=True),
                                 StatCard(_statRead, "READ", compact:=True), _undoLink})
        _undoLink.LinkColor = Theme.Accent
        _undoLink.ActiveLinkColor = Theme.Accent
        AddHandler _undoLink.LinkClicked, Sub(s, e) UndoLast()

        _covers.LargeImageList = _coverImages
        SetupCoverSizes()
        AddHandler _covers.RetrieveVirtualItem, AddressOf OnRetrieveCover
        AddHandler _covers.DoubleClick, Sub(s, e) OnEdit(s, e)
        AddHandler _covers.KeyDown, Sub(s, e)
                                        If e.KeyCode = Keys.Delete Then OnDelete(s, e)
                                        If e.KeyCode = Keys.Enter Then OnEdit(s, e)
                                    End Sub

        AddHandler _barcode.KeyDown, Sub(s, e)
                                          If e.KeyCode = Keys.Enter Then
                                              e.SuppressKeyPress = True
                                              OnBarcodeEntered(s, e)
                                          End If
                                      End Sub
        AddHandler _search.TextChanged, Sub(s, e)
                                            _searchTimer.Stop()
                                            _searchTimer.Start()
                                        End Sub
        AddHandler _searchTimer.Tick, Sub(s, e)
                                          _searchTimer.Stop()
                                          RefreshCollection()
                                      End Sub
        _grid.Columns.Add(_thumbColumn)
        AddHandler _grid.CellFormatting, AddressOf OnGridCellFormatting
        AddHandler _grid.ColumnHeaderMouseClick, AddressOf OnGridHeaderClick
        AddHandler _grid.ColumnWidthChanged, AddressOf OnGridColumnWidthChanged
        AddHandler _grid.CellDoubleClick, Sub(s, e)
                                              If e.RowIndex < 0 Then Return
                                              ' Double-clicking "Digital" opens the digital copy to read.
                                              If e.ColumnIndex >= 0 AndAlso _grid.Columns(e.ColumnIndex).Name = "Digital" AndAlso
                                                 CStr(If(_grid.Rows(e.RowIndex).Cells(e.ColumnIndex).Value, "")) <> "" Then
                                                  OnReadDigitalCopy(s, e)
                                              Else
                                                  OnEdit(s, e)
                                              End If
                                          End Sub
        AddHandler _grid.KeyDown, Sub(s, e)
                                      If e.KeyCode = Keys.Delete Then OnDelete(s, e)
                                      If e.KeyCode = Keys.Enter Then
                                          e.SuppressKeyPress = True
                                          OnEdit(s, e)
                                      End If
                                      If e.KeyCode = Keys.R AndAlso e.Modifiers = Keys.None Then
                                          e.SuppressKeyPress = True
                                          OnToggleRead(s, e)
                                      End If
                                  End Sub
        page.Controls.Add(_grid)
        page.Controls.Add(_covers)
        page.Controls.Add(BuildBoxesPanel())
        page.Controls.Add(cards)
        page.Controls.Add(actions)
        page.Controls.Add(bar)
        Return page
    End Function

    Private Shared Function ButtonIcon(text As String) As Char
        Select Case text
            Case "Look up and add", "Add by hand", "From a shop receipt…" : Return Icons.Add
            Case "Edit" : Return Icons.Edit
            Case "Put in box…" : Return Icons.BoxIcon
            Case "Delete" : Return Icons.Delete
            Case "List" : Return Icons.List
            Case "Covers" : Return Icons.Pictures
            Case "Boxes" : Return Icons.BoxIcon
            Case "Make a set" : Return Icons.Library
            Case "Read / unread" : Return Icons.Check
            Case "What should I read next?" : Return Icons.Reading
            Case "Lend…" : Return Icons.People
            Case "Export list…" : Return Icons.Save
            Case "Share page…" : Return Icons.Share
            Case "More ▾" : Return Icons.More
            Case Else : Return ChrW(0)
        End Select
    End Function

    ''' <summary>A number with a caption above it. Compact ones (the Collection page's totals) are shorter, leaving room for the list.</summary>
    Private Function StatCard(value As Label, caption As String, Optional cap As Label = Nothing, Optional compact As Boolean = False) As Control
        Dim card As New Panel With {.Size = If(compact, New Size(190, 52), New Size(210, 72)), .BackColor = Theme.Panel, .Margin = New Padding(0, 0, 10, 0),
                                    .Padding = If(compact, New Padding(12, 5, 8, 4), New Padding(14, 8, 8, 8))}
        value.Text = "0"
        value.AutoSize = False
        value.Dock = DockStyle.Fill
        value.Font = New Font("Segoe UI Semibold", If(compact, 13.0F, 18.0F))
        value.ForeColor = Theme.Text
        value.BackColor = Theme.Panel
        cap = If(cap, New Label())
        cap.Text = caption
        cap.Dock = DockStyle.Top
        cap.Height = If(compact, 16, 18)
        cap.Tag = Theme.MutedTag
        cap.ForeColor = Theme.Muted
        cap.BackColor = Theme.Panel
        cap.Font = New Font("Segoe UI", 8.0F, FontStyle.Bold)
        card.Controls.Add(value)
        card.Controls.Add(cap)
        Return card
    End Function

    ' ---------- Covers view ----------

    Private Sub SetView(covers As Boolean)
        _covers.Visible = covers
        _grid.Visible = Not covers
        _listButton.ForeColor = If(covers, Theme.Text, Theme.Accent)
        _coversButton.ForeColor = If(covers, Theme.Accent, Theme.Text)
        If covers Then _covers.Focus() Else _grid.Focus()
    End Sub

    Private Sub OnRetrieveCover(sender As Object, e As RetrieveVirtualItemEventArgs)
        Dim row = _rows.DefaultView(e.ItemIndex)
        Dim comicId = Convert.ToInt64(row("comic_id"), CultureInfo.InvariantCulture)
        Dim label = $"{row("Series")} #{row("Issue")}"
        Dim index As Integer
        If Not _coverIndex.TryGetValue(comicId, index) Then
            index = 0
            LoadCover(comicId, Convert.ToString(row("cover_url"), CultureInfo.InvariantCulture))
        End If
        e.Item = New ListViewItem(label, index)
    End Sub

    Private Async Sub LoadCover(comicId As Long, url As String)
        If url = "" OrElse Not _coverLoading.Add(comicId) Then Return
        Dim size = _coverImages.ImageSize
        Dim thumb = Await _coverCache.GetThumbnailAsync(comicId, url, size)
        If thumb Is Nothing OrElse IsDisposed Then Return
        ' The cover size was changed while this one loaded: load it again at the new size.
        If size <> _coverImages.ImageSize Then
            thumb.Dispose()
            Return
        End If
        _coverImages.Images.Add(thumb)
        _coverIndex(comicId) = _coverImages.Images.Count - 1
        _gridThumbs(comicId) = SmallThumb(thumb)
        _covers.Invalidate()
        _grid.InvalidateColumn(_thumbColumn.Index)
    End Sub

    ' Small cover at the start of each row in the list view.
    Private Sub OnGridCellFormatting(sender As Object, e As DataGridViewCellFormattingEventArgs)
        If e.ColumnIndex <> _thumbColumn.Index OrElse e.RowIndex < 0 OrElse _rows Is Nothing Then Return
        Dim view = TryCast(_grid.Rows(e.RowIndex).DataBoundItem, DataRowView)
        If view Is Nothing Then Return
        Dim comicId = Convert.ToInt64(view("comic_id"), CultureInfo.InvariantCulture)
        Dim thumb As Image = Nothing
        If _gridThumbs.TryGetValue(comicId, thumb) Then
            e.Value = thumb
        Else
            e.Value = _gridPlaceholder
            LoadCover(comicId, Convert.ToString(view("cover_url"), CultureInfo.InvariantCulture))
        End If
        e.FormattingApplied = True
    End Sub

    Private Shared Function SmallThumb(source As Image) As Image
        Dim small As New Bitmap(32, 48)
        Using g = Graphics.FromImage(small)
            g.InterpolationMode = Drawing2D.InterpolationMode.HighQualityBicubic
            g.DrawImage(source, New Rectangle(0, 0, small.Width, small.Height))
        End Using
        Return small
    End Function

    Private Sub UpdateFindCoversButton()
        If _findingCovers Then Return
        Dim n = _db.ComicsNeedingCovers().Count
        _findCovers.Visible = n > 0
        _findCovers.Text = $"Find covers and prices ({n:N0})"
    End Sub

    ''' <summary>
    ''' Looks up covers and cover prices for comics that don't have them, slowly enough to stay inside
    ''' Metron's limit of about 20 lookups a minute. Click again to stop; it carries on
    ''' where it left off next time.
    ''' </summary>
    Private Async Sub OnFindCovers(sender As Object, e As EventArgs)
        If _findingCovers Then
            _stopFinding = True
            _findCovers.Text = "Stopping…"
            Return
        End If
        Await RunFindCovers(quiet:=False)
    End Sub

    ''' <summary>The work behind Find covers. Quiet (from the background jobs) shows no messages.</summary>
    Private Async Function RunFindCovers(quiet As Boolean) As Task
        If _findingCovers Then Return
        Dim metron = Me.Metron
        If Not metron.IsSetUp Then
            If Not quiet Then Ui.ShowError(Me, "Add your relay address on the Settings tab first.")
            Return
        End If
        ' The first relay didn't send cover prices. Check before marking prices as looked for.
        Dim withPrices = False
        Try
            withPrices = Await metron.RelayVersionAsync() >= 2
        Catch ex As Exception
            If Not quiet Then Ui.ShowError(Me, $"Couldn't reach your relay: {Ui.Friendly(ex)}")
            Return
        End Try
        If Not withPrices AndAlso Not quiet Then
            Dim answer = MessageBox.Show(Me,
                "Your relay needs a small update before it can send cover prices. The steps are under " &
                "'Updating the relay' in relay\README.md on GitHub." & vbCrLf & vbCrLf &
                "Carry on and find just the missing covers for now?",
                "Find covers", MessageBoxButtons.YesNo, MessageBoxIcon.Information)
            If answer <> DialogResult.Yes Then Return
        End If
        Dim todo = _db.ComicsNeedingCovers(withPrices)
        _findingCovers = True
        _stopFinding = False
        Dim found = 0, done = 0, volumesFilled = 0
        Try
            For Each c In todo
                If _stopFinding OrElse IsDisposed Then Exit For
                _findCovers.Text = $"Finding covers {done + 1:N0} of {todo.Count:N0} (click to stop)"
                Dim calls = 1
                Dim match As MetronIssue = Nothing
                For attempt = 1 To 4
                    Dim slowDown = False
                    Try
                        match = Await CoverFinder.FindAsync(metron, c, Sub(n) calls = n, withPrices)
                        Exit For
                    Catch ex As InvalidOperationException When ex.Message.StartsWith("Too many") AndAlso attempt < 4
                        slowDown = True
                    End Try
                    If slowDown Then
                        _findCovers.Text = "Metron asked us to slow down, waiting a minute…"
                        Await Task.Delay(65000)
                    End If
                Next
                If match IsNot Nothing Then
                    _db.SetCover(c.ComicId, match.CoverUrl, If(match.MetronId > 0, match.MetronId, CType(Nothing, Long?)),
                                 match.Price, withPrices, match.CoverDate, match.Publisher)
                    ' No volume yet: it goes in the one Metron says, like Daredevil (1998).
                    If c.Volume = "" AndAlso If(match.Volume, "") <> "" Then
                        _db.SetComicVolume(c.ComicId, match.Volume)
                        volumesFilled += 1
                    End If
                    If match.CoverUrl <> "" OrElse match.Price.HasValue Then found += 1
                    ' Show it straight away in the list and covers view.
                    For Each row As DataRow In _rows.Rows
                        If Convert.ToInt64(row("comic_id"), CultureInfo.InvariantCulture) <> c.ComicId Then Continue For
                        If Convert.ToString(row("cover_url"), CultureInfo.InvariantCulture) = "" Then row("cover_url") = match.CoverUrl
                        If match.Price.HasValue AndAlso row.IsNull("Cover price") Then row("Cover price") = match.Price.Value
                    Next
                    _covers.Invalidate()
                    _grid.InvalidateColumn(_thumbColumn.Index)
                Else
                    _db.SetCover(c.ComicId, "", Nothing, Nothing, withPrices)
                End If
                done += 1
                If done < todo.Count Then Await Task.Delay(If(calls > 1, 6000, 3200))
            Next
            If Not quiet AndAlso Not IsDisposed Then
                MessageBox.Show(Me, $"Found details for {found:N0} of the {done:N0} comics looked up." &
                                If(done < todo.Count, " Click Find covers again to carry on.", ""), "Find covers")
            End If
        Catch ex As Exception
            If Not quiet AndAlso Not IsDisposed Then Ui.ShowError(Me, $"Finding covers stopped: {Ui.Friendly(ex)}")
        Finally
            _findingCovers = False
            If Not IsDisposed Then
                UpdateFindCoversButton()
                If volumesFilled > 0 Then RefreshCollection()
            End If
        End Try
    End Function

    ''' <summary>
    ''' Puts comics with no volume into the volume Metron says, like Daredevil (1998) and Daredevil (2019): by their
    ''' Metron issue, or else their barcode or series, issue and cover date. Just one series if given. Click Find covers to stop.
    ''' </summary>
    Private Async Function RunSortVolumes(quiet As Boolean, Optional seriesName As String = Nothing) As Task
        If _findingCovers Then Return
        Dim metron = Me.Metron
        If Not metron.IsSetUp Then
            If Not quiet Then Ui.ShowError(Me, "Add your relay address on the Settings tab first.")
            Return
        End If
        Dim todo = _db.ComicsNeedingVolumes(seriesName)
        ' In the background only ones matched on Metron are asked about (one quick lookup each).
        If quiet Then todo = todo.Where(Function(t) t.MetronId.HasValue).ToList()
        If todo.Count = 0 Then
            If Not quiet Then MessageBox.Show(Me, "Every comic in that series already has a volume.", "Sort into volumes")
            Return
        End If
        _findingCovers = True
        _stopFinding = False
        _findCovers.Visible = True
        Dim sorted = 0, done = 0
        Try
            For Each t In todo
                If _stopFinding OrElse IsDisposed Then Exit For
                _findCovers.Text = $"Sorting into volumes {done + 1:N0} of {todo.Count:N0} (click to stop)"
                Dim comic = t.Comic
                Dim match = Await WithRetry(Function() CoverFinder.FindAsync(metron, comic, Sub(n)
                                                                                             End Sub), _scanNote)
                If match IsNot Nothing AndAlso If(match.Volume, "") <> "" Then
                    If Not comic.MetronId.HasValue Then
                        _db.SetCover(comic.ComicId, match.CoverUrl, If(match.MetronId > 0, match.MetronId, CType(Nothing, Long?)))
                    End If
                    _db.SetComicVolume(comic.ComicId, match.Volume)
                    sorted += 1
                Else
                    _db.MarkVolumeChecked(comic.ComicId)
                End If
                done += 1
                If done < todo.Count Then Await Task.Delay(3200)
            Next
            If Not quiet AndAlso Not IsDisposed Then
                MessageBox.Show(Me, $"Put {ComicsText(sorted)} into their volumes." &
                                If(done - sorted > 0, $" {done - sorted:N0} couldn't be matched on Metron; give them a volume in the edit window (Ctrl+E edits several at once), or use Merge.", "") &
                                If(done < todo.Count, " Run it again to carry on.", ""), "Sort into volumes")
            End If
        Catch ex As Exception
            If Not quiet AndAlso Not IsDisposed Then Ui.ShowError(Me, $"Sorting into volumes stopped: {Ui.Friendly(ex)}")
        Finally
            _findingCovers = False
            If Not IsDisposed Then
                UpdateFindCoversButton()
                RefreshCollection()
            End If
        End Try
    End Function

    ''' <summary>
    ''' Reloads the Collection list. Picks out <paramref name="selectComicId"/> (a comic just added or saved) if given;
    ''' otherwise the comics that were selected stay selected and the list stays where it was scrolled to.
    ''' </summary>
    Private Sub RefreshCollection(Optional selectComicId As Long = 0)
        If _db Is Nothing Then Return
        Dim read As Boolean? = Nothing
        Dim show = ""
        Select Case _readFilter.SelectedIndex
            Case 1 : read = True
            Case 2 : read = False
            Case 3 : show = "key"
            Case 4 : show = "trade"
            Case 5 : show = "lent"
            Case 6 : show = "nocover"
            Case 7 : show = "signed"
            Case 8 : show = "fix"
            Case 9 : show = "novalue"
        End Select
        ' Something just saved or picked that belongs on the other page (a trade on Collection, say) takes you there.
        If selectComicId > 0 AndAlso (_currentPage < 0 OrElse PageNames(_currentPage) = "Collection" OrElse PageNames(_currentPage) = "Trades") Then
            Dim isTrade = ComicDb.IsTrade(_db.GetComic(selectComicId)?.Format)
            If isTrade <> _tradesView Then ShowPage(If(isTrade, "Trades", "Collection"))
        End If
        Dim kind = If(_tradesView, "trades", "issues")
        Dim keep = If(selectComicId > 0, New List(Of Long) From {selectComicId},
                      If(_rows Is Nothing, New List(Of Long), SelectedComicIds()))
        Dim keepTop = If(_grid.RowCount = 0, 0, Math.Max(0, _grid.FirstDisplayedScrollingRowIndex))
        _layingOutColumns = True
        Try
            Dim publisher = If(_publisherFilter.SelectedIndex > 0, CStr(_publisherFilter.SelectedItem), "")
            _rows = _db.SearchCollection(_search.Text, read, show, kind, publisher, _boxFilter)
            _rows.DefaultView.Sort = SortExpression()
            _grid.DataSource = _rows
            _covers.VirtualListSize = _rows.DefaultView.Count
            _covers.Invalidate()
            _thumbColumn.DisplayIndex = 0
            ' Columns share out the window's width, so a maximised window on a wide screen uses it all.
            ' A column you've dragged wider or narrower keeps its share.
            For Each col As DataGridViewColumn In _grid.Columns
                If col Is _thumbColumn Then Continue For
                col.MinimumWidth = 60
                col.SortMode = DataGridViewColumnSortMode.Programmatic
                Select Case col.Name
                    Case "Series", "Story title" : col.FillWeight = 220
                    Case "Cover / variant", "Publisher", "Set" : col.FillWeight = 140
                    Case "Lent to", "Box", "Trade" : col.FillWeight = 90
                    Case "Signed by", "Missing" : col.FillWeight = 120
                    Case "Key issue" : col.FillWeight = 160
                    Case "Rating", "Digital" : col.FillWeight = 70
                    Case "Read"
                        col.FillWeight = 45
                        col.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter
                    Case Else : col.FillWeight = 80
                End Select
                Dim saved As Single
                If _settings.ColumnWeights.TryGetValue(col.Name, saved) AndAlso saved > 0 Then col.FillWeight = saved
            Next
            For Each colName In {"collection_id", "comic_id", "cover_url", "issue_sort"}
                If _grid.Columns.Contains(colName) Then _grid.Columns(colName).Visible = False
            Next
            ' The eBay column only shows once there are estimates.
            If _grid.Columns.Contains("eBay est.") Then
                _grid.Columns("eBay est.").Visible = _rows.Rows.Cast(Of DataRow)().Any(Function(r) Not r.IsNull("eBay est."))
            End If
            For Each colName In {"Cover price", "Paid", "Value", "eBay est."}
                If _grid.Columns.Contains(colName) Then
                    _grid.Columns(colName).DefaultCellStyle.Format = "N2"
                    _grid.Columns(colName).DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight
                End If
            Next
            ShowSortGlyph()
        Finally
            _layingOutColumns = False
        End Try
        SelectComics(keep, scrollTo:=selectComicId > 0, top:=keepTop)
        UpdateFindCoversButton()
        UpdateSearchSuggestions()
        RefreshPublisherFilter()
        RefreshBoxes()
        Dim s = _db.GetStats(kind)
        _statComicsCaption.Text = If(_tradesView, "TRADES AND HARDCOVERS", "SINGLE ISSUES")
        _statComics.Text = s.Comics.ToString("N0")
        _statCopies.Text = s.Copies.ToString("N0")
        _statValue.Text = "$" & s.TotalValue.ToString("N2")
        _statPaid.Text = "$" & s.TotalPaid.ToString("N2")
        _statRead.Text = $"{s.Read:N0} of {s.Comics:N0}"
    End Sub

    ''' <summary>Selects these comics in the list and covers view, then scrolls to the first one or back to row <paramref name="top"/>.</summary>
    Private Sub SelectComics(ids As List(Of Long), scrollTo As Boolean, top As Integer)
        Dim found As New List(Of Integer)
        Dim wanted As New HashSet(Of Long)(ids)
        For i = 0 To _rows.DefaultView.Count - 1
            If wanted.Contains(Convert.ToInt64(_rows.DefaultView(i)("comic_id"), CultureInfo.InvariantCulture)) Then found.Add(i)
        Next
        _covers.SelectedIndices.Clear()
        If _grid.RowCount = 0 Then Return
        If found.Count > 0 Then
            ' The current row moves with the selection, so Enter edits a selected comic rather than the top one.
            _grid.CurrentCell = _grid.Rows(found(0)).Cells(_thumbColumn.Index)
            _grid.ClearSelection()
            For Each i In found
                _grid.Rows(i).Selected = True
                _covers.SelectedIndices.Add(i)
            Next
        End If
        Try
            _grid.FirstDisplayedScrollingRowIndex = If(scrollTo AndAlso found.Count > 0, found(0), Math.Min(top, _grid.RowCount - 1))
            If scrollTo AndAlso found.Count > 0 AndAlso _covers.Visible Then _covers.EnsureVisible(found(0))
        Catch ex As InvalidOperationException
            ' The list isn't on screen yet (the window is still opening); nothing to scroll.
        End Try
    End Sub

    ' ---------- sorting and column sizes in the Collection list ----------

    ''' <summary>Clicking a heading sorts by it, again sorts the other way, and a third time goes back to series order.</summary>
    Private Sub OnGridHeaderClick(sender As Object, e As DataGridViewCellMouseEventArgs)
        If e.Button <> MouseButtons.Left OrElse e.ColumnIndex < 0 Then Return
        Dim col = _grid.Columns(e.ColumnIndex)
        If col Is _thumbColumn OrElse col.SortMode = DataGridViewColumnSortMode.NotSortable Then Return
        If Not String.Equals(_settings.CollectionSort, col.Name, StringComparison.OrdinalIgnoreCase) Then
            _settings.CollectionSort = col.Name
            _settings.CollectionSortDescending = False
        ElseIf Not _settings.CollectionSortDescending Then
            _settings.CollectionSortDescending = True
        Else
            _settings.CollectionSort = ""
            _settings.CollectionSortDescending = False
        End If
        SaveSettingsQuietly()
        Dim keep = SelectedComicIds()
        _rows.DefaultView.Sort = SortExpression()
        _covers.Invalidate()
        ShowSortGlyph()
        SelectComics(keep, scrollTo:=True, top:=0)
    End Sub

    ''' <summary>
    ''' How the list is sorted. Issue numbers sort as numbers (1, 2, 10, not 1, 10, 2), and comics with
    ''' the same value in the sorted column stay in series and issue order.
    ''' </summary>
    Private Function SortExpression() As String
        Dim name = If(_settings.CollectionSort, "")
        If name = "" OrElse _rows Is Nothing OrElse Not _rows.Columns.Contains(name) Then Return ""
        Dim dir = If(_settings.CollectionSortDescending, " DESC", " ASC")
        Select Case name
            Case "Issue" : Return $"issue_sort{dir}, Issue{dir}, Series"
            Case "Series" : Return $"Series{dir}, Volume{dir}, issue_sort{dir}, Issue{dir}"
            Case Else : Return $"[{name}]{dir}, Series, Volume, issue_sort, Issue"
        End Select
    End Function

    Private Sub ShowSortGlyph()
        For Each col As DataGridViewColumn In _grid.Columns
            If col Is _thumbColumn Then Continue For
            col.HeaderCell.SortGlyphDirection =
                If(Not String.Equals(col.Name, _settings.CollectionSort, StringComparison.OrdinalIgnoreCase), SortOrder.None,
                   If(_settings.CollectionSortDescending, SortOrder.Descending, SortOrder.Ascending))
        Next
    End Sub

    ''' <summary>Remembers each column's share of the width after you drag one, so it's the same next time.</summary>
    Private Sub OnGridColumnWidthChanged(sender As Object, e As DataGridViewColumnEventArgs)
        If _layingOutColumns OrElse e.Column Is _thumbColumn OrElse Not e.Column.Visible Then Return
        For Each col As DataGridViewColumn In _grid.Columns
            If col Is _thumbColumn OrElse Not col.Visible Then Continue For
            _settings.ColumnWeights(col.Name) = col.FillWeight
        Next
        _columnsChanged = True
    End Sub

    Private Sub SaveSettingsQuietly()
        Try
            _settings.Save()
        Catch ex As IOException
        End Try
    End Sub

    ''' <summary>Fills the publisher filter with the publishers you have (keeping the one picked).</summary>
    Private Sub RefreshPublisherFilter()
        Dim names = _db.PublisherNames()
        Dim current = If(_publisherFilter.SelectedIndex > 0, CStr(_publisherFilter.SelectedItem), "")
        If _publisherFilter.Items.Count = names.Count + 1 AndAlso names.Select(Function(n, i) n = CStr(_publisherFilter.Items(i + 1))).All(Function(x) x) Then Return
        _fillingPublishers = True
        _publisherFilter.Items.Clear()
        _publisherFilter.Items.Add("All publishers")
        _publisherFilter.Items.AddRange(names.Cast(Of Object)().ToArray())
        _publisherFilter.SelectedIndex = Math.Max(0, names.IndexOf(current) + 1)
        _fillingPublishers = False
    End Sub

    ''' <summary>Series names as suggestions under the search box (only rebuilt when they change).</summary>
    Private Sub UpdateSearchSuggestions()
        Dim names = _db.SeriesNames()
        Dim key = String.Join(ChrW(1), names)
        If key = _searchSuggestionsKey Then Return
        _searchSuggestionsKey = key
        Dim list As New AutoCompleteStringCollection()
        list.AddRange(names.ToArray())
        _search.AutoCompleteCustomSource = list
        _search.AutoCompleteSource = AutoCompleteSource.CustomSource
        _search.AutoCompleteMode = AutoCompleteMode.Suggest
    End Sub

    Private Function SelectedComicIds() As List(Of Long)
        If _covers.Visible Then
            Return _covers.SelectedIndices.Cast(Of Integer)().
                Select(Function(i) Convert.ToInt64(_rows.DefaultView(i)("comic_id"), CultureInfo.InvariantCulture)).ToList()
        End If
        Return _grid.SelectedRows.Cast(Of DataGridViewRow)().
            Select(Function(r) Convert.ToInt64(r.Cells("comic_id").Value, CultureInfo.InvariantCulture)).ToList()
    End Function

    ''' <summary>Marks the selected comics read, or unread if they're all read already. R does the same.</summary>
    Private Sub OnToggleRead(sender As Object, e As EventArgs)
        Dim ids = SelectedComicIds()
        If ids.Count = 0 Then
            Ui.ShowError(Me, "Select the comics first. Hold Ctrl or Shift to pick several.")
            Return
        End If
        Dim allRead = ids.All(Function(id) If(_db.GetComic(id)?.IsRead, False))
        Dim undo = _db.Snapshot(ids)
        _db.SetRead(ids, Not allRead)
        RememberUndo(undo, $"Marked {ComicsText(ids.Count)} {If(allRead, "not read", "read")}")
        RefreshCollection()
    End Sub

    Private Sub OnExport(sender As Object, e As EventArgs)
        Using dlg As New SaveFileDialog With {
            .Title = "Save a list of your whole collection",
            .Filter = "Excel workbook (*.xlsx)|*.xlsx|PDF for printing or emailing (*.pdf)|*.pdf",
            .FileName = $"Comic collection {DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}",
            .InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)}
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            Try
                UseWaitCursor = True
                If Path.GetExtension(dlg.FileName).Equals(".pdf", StringComparison.OrdinalIgnoreCase) Then
                    Exporter.ToPdf(_db, dlg.FileName)
                Else
                    Exporter.ToExcel(_db, dlg.FileName)
                End If
            Catch ex As IOException
                Ui.ShowError(Me, $"Couldn't save it: {Ui.Friendly(ex)}")
                Return
            Catch ex As Exception
                Ui.ShowError(Me, $"Couldn't save it: {Ui.Friendly(ex)}")
                Return
            Finally
                UseWaitCursor = False
            End Try
            If MessageBox.Show(Me, $"Saved {Path.GetFileName(dlg.FileName)}. Open it now?", "Export list",
                               MessageBoxButtons.YesNo, MessageBoxIcon.Information) = DialogResult.Yes Then
                Process.Start(New ProcessStartInfo With {.FileName = dlg.FileName, .UseShellExecute = True})
            End If
        End Using
    End Sub

    Private Sub OnBarcodeEntered(sender As Object, e As EventArgs)
        Dim code = Barcode.Parse(_barcode.Text)
        If Not code.IsValid Then
            Ui.ShowError(Me, "Type the 12 digits under the barcode, then the small 5 digits to its right (if there are any)." &
                         vbCrLf & vbCrLf & "A USB barcode scanner works too: click in the box and scan.")
            _barcode.Focus()
            Return
        End If

        Dim owned = _db.FindByBarcode(code.Full)
        If owned IsNot Nothing AndAlso (owned.Barcode = code.Full OrElse code.Addon = "") Then
            Dim answer = MessageBox.Show(Me, $"You already have {owned} ({owned.Quantity} {If(owned.Quantity = 1, "copy", "copies")})." &
                                         vbCrLf & vbCrLf & "Add another copy?", "Already in your collection",
                                         MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question)
            If answer = DialogResult.Yes Then
                owned.Quantity = 1
                Dim id = _db.SaveComic(owned, addCopies:=True)
                RefreshCollection(id)
            ElseIf answer = DialogResult.No Then
                EditComic(owned)
            End If
        Else
            Using f As New ComicForm(_db, Metron, barcode:=code.Full)
                If f.ShowDialog(Me) = DialogResult.OK Then RefreshCollection(f.SavedComicId)
            End Using
        End If
        _barcode.Clear()
        _barcode.Focus()
    End Sub

    Private Sub OnAddByHand(sender As Object, e As EventArgs)
        Using f As New ComicForm(_db, Metron, If(_tradesView, New ComicRecord With {.Format = "Trade Paperback"}, Nothing))
            If f.ShowDialog(Me) = DialogResult.OK Then RefreshCollection(f.SavedComicId)
        End Using
    End Sub

    ''' <summary>Set by the build's screenshot step: opens the first comic once the window is up.</summary>
    Public Property EditFirstOnStart As Boolean
    ''' <summary>Set by the build's screenshot step: the page to show once the window is up.</summary>
    Public Property StartPage As String = ""

    Protected Overrides Sub OnFormClosing(e As FormClosingEventArgs)
        MyBase.OnFormClosing(e)
        ' Window size and place (the size it goes back to when maximised), and column widths, for next time.
        Dim r = If(WindowState = FormWindowState.Normal, Bounds, RestoreBounds)
        Dim place = {r.X, r.Y, r.Width, r.Height}
        Dim moved = _settings.MainBounds Is Nothing OrElse Not _settings.MainBounds.SequenceEqual(place)
        If moved OrElse _columnsChanged OrElse _settings.MainMaximized <> (WindowState = FormWindowState.Maximized) OrElse
           _settings.EditMaximized <> ComicForm.StartMaximized Then
            _settings.MainMaximized = WindowState = FormWindowState.Maximized
            _settings.EditMaximized = ComicForm.StartMaximized
            If WindowState <> FormWindowState.Minimized Then _settings.MainBounds = place
            SaveSettingsQuietly()
        End If
    End Sub

    ''' <summary>Ctrl+F searches the collection, Ctrl+N adds a comic by hand and F5 refreshes the page.</summary>
    Protected Overrides Function ProcessCmdKey(ByRef msg As Message, keyData As Keys) As Boolean
        If _db IsNot Nothing Then
            Select Case keyData
                Case Keys.Control Or Keys.F
                    If PageNames(_currentPage) <> "Collection" AndAlso PageNames(_currentPage) <> "Trades" Then ShowPage("Collection")
                    _search.Focus()
                    _search.SelectAll()
                    Return True
                Case Keys.Control Or Keys.N
                    If PageNames(_currentPage) <> "Collection" AndAlso PageNames(_currentPage) <> "Trades" Then ShowPage("Collection")
                    OnAddByHand(Me, EventArgs.Empty)
                    Return True
                Case Keys.Control Or Keys.E, Keys.Control Or Keys.B, Keys.Control Or Keys.R
                    If PageNames(_currentPage) <> "Collection" AndAlso PageNames(_currentPage) <> "Trades" Then Exit Select
                    Select Case keyData And Keys.KeyCode
                        Case Keys.E : OnEdit(Me, EventArgs.Empty)
                        Case Keys.B : OnSetBox(Me, EventArgs.Empty)
                        Case Keys.R : OnToggleRead(Me, EventArgs.Empty)
                    End Select
                    Return True
                Case Keys.Control Or Keys.D1, Keys.Control Or Keys.D2, Keys.Control Or Keys.D3, Keys.Control Or Keys.D4, Keys.Control Or Keys.D5,
                     Keys.Control Or Keys.D6, Keys.Control Or Keys.D7, Keys.Control Or Keys.D8, Keys.Control Or Keys.D9
                    ShowPage((keyData And Keys.KeyCode) - Keys.D1)
                    Return True
                Case Keys.Escape
                    If _search.Focused AndAlso _search.Text <> "" Then
                        _search.Clear()
                        Return True
                    End If
                Case Keys.Control Or Keys.Z
                    ' In a text box Ctrl+Z undoes typing as usual.
                    If TypeOf ActiveControl Is TextBoxBase OrElse (TypeOf ActiveControl Is ComboBox AndAlso
                        DirectCast(ActiveControl, ComboBox).DropDownStyle <> ComboBoxStyle.DropDownList) Then Exit Select
                    UndoLast()
                    Return True
                Case Keys.F5
                    If PageNames(_currentPage) = "Collection" OrElse PageNames(_currentPage) = "Trades" Then
                        RefreshCollection()
                    Else
                        ShowPage(_currentPage)
                    End If
                    Return True
            End Select
        End If
        Return MyBase.ProcessCmdKey(msg, keyData)
    End Function

    Protected Overrides Sub OnShown(e As EventArgs)
        MyBase.OnShown(e)
        If StartPage <> "" AndAlso PageNames.Contains(StartPage) Then ShowPage(StartPage)
        If StartPage = "Videos" Then CheckVideos(quiet:=True)
        If ReaderSample <> "" Then BeginInvoke(Sub() OpenSampleReader(ReaderSample))
        ' A new, empty collection gets the welcome steps (once).
        If Not _settings.WelcomeShown AndAlso _db IsNot Nothing AndAlso StartPage = "" AndAlso Not EditFirstOnStart AndAlso
           Environment.GetEnvironmentVariable("COMICCATALOG_NO_UPDATE") Is Nothing AndAlso _db.GetStats().Comics = 0 Then
            BeginInvoke(Sub() ShowWelcome())
        End If
        If EditFirstOnStart AndAlso _rows IsNot Nothing AndAlso _rows.Rows.Count > 0 Then
            BeginInvoke(Sub() EditComic(_db.GetComic(Convert.ToInt64(_rows.DefaultView(0)("comic_id"), CultureInfo.InvariantCulture))))
        End If
    End Sub

    ''' <summary>Edits the selected comic, or with several selected, changes the same things on all of them.</summary>
    Private Sub OnEdit(sender As Object, e As EventArgs)
        Dim ids = SelectedComicIds()
        If ids.Count = 0 Then Return
        If ids.Count > 1 Then
            EditMany(ids)
            Return
        End If
        Dim c = _db.GetComic(ids(0))
        If c IsNot Nothing Then EditComic(c)
    End Sub

    Private Sub EditMany(ids As List(Of Long))
        Using f As New BulkEditForm(ids.Count, _db.Locations())
            If f.ShowDialog(Me) <> DialogResult.OK Then Return
            Dim undo = _db.Snapshot(ids)
            Try
                _db.EditMany(ids, f.Changes)
            Catch ex As Exception
                Ui.ShowError(Me, $"Couldn't change them: {Ui.Friendly(ex)}")
                Return
            End Try
            RememberUndo(undo, $"Changed {ComicsText(ids.Count)}")
            If f.Changes.Box IsNot Nothing AndAlso f.Changes.Box <> "" Then _settings.LastBox = f.Changes.Box
        End Using
        RefreshCollection()
    End Sub

    Private Sub EditComic(c As ComicRecord)
        Using f As New ComicForm(_db, Metron, c)
            If f.ShowDialog(Me) = DialogResult.OK Then RefreshCollection(f.SavedComicId)
        End Using
    End Sub

    ' ---------- Boxes list beside the Collection list: click one to see what's in it, drag comics onto one to move them ----------

    Private Const AllBoxesItem As String = "All comics"
    ''' <summary>The quick filter for comics missing a cover, cover date or publisher.</summary>
    Private Const NeedsFixing As String = "Needs fixing"
    Private Const NoBoxItem As String = "Not in a box"
    Private Const NewBoxItem As String = "＋ New box…"

    Private Function BuildBoxesPanel() As Control
        _boxesPanel.Controls.Add(_boxList)
        _boxesPanel.Controls.Add(New Label With {.Text = "BOXES  (drag comics onto one)", .Dock = DockStyle.Top, .Height = 24, .Tag = Theme.MutedTag,
                                                 .Font = New Font("Segoe UI", 8.0F, FontStyle.Bold)})
        _boxList.AllowDrop = True
        AddHandler _boxList.SelectedIndexChanged, Sub(s, e)
                                                       If _fillingBoxes OrElse _dragging Then Return
                                                       If BoxItemName(_boxList.SelectedIndex) = NewBoxItem Then
                                                           ' Clicked rather than dropped on: puts the selected comics in a box you name.
                                                           ShowBoxSelection()
                                                           BeginInvoke(Sub() OnSetBox(Me, EventArgs.Empty))
                                                           Return
                                                       End If
                                                       _boxFilter = BoxFilterFor(_boxList.SelectedIndex)
                                                       RefreshCollection()
                                                   End Sub
        AddHandler _boxList.DragOver, AddressOf OnBoxDragOver
        AddHandler _boxList.DragDrop, AddressOf OnBoxDragDrop
        AddHandler _boxList.DragLeave, Sub(s, e) ShowBoxSelection()
        ' Dragging comics out of the list.
        AddHandler _grid.SelectionChanged, Sub(s, e)
                                               _previousSelection = _currentSelection
                                               _currentSelection = If(_rows Is Nothing, New List(Of Long), SelectedComicIds())
                                           End Sub
        AddHandler _grid.CellMouseDown, Sub(s, e)
                                            _dragFrom = Rectangle.Empty
                                            If e.Button <> MouseButtons.Left OrElse e.RowIndex < 0 Then Return
                                            Dim id = Convert.ToInt64(_grid.Rows(e.RowIndex).Cells("comic_id").Value, CultureInfo.InvariantCulture)
                                            ' Clicking one of several selected comics selects just it; dragging still takes them all.
                                            _dragIds = If(_currentSelection.Count > 1 AndAlso _currentSelection.Contains(id), _currentSelection,
                                                          If(_previousSelection.Count > 1 AndAlso _previousSelection.Contains(id), _previousSelection,
                                                             New List(Of Long) From {id}))
                                            Dim size = SystemInformation.DragSize
                                            _dragFrom = New Rectangle(_grid.PointToClient(Cursor.Position) - New Size(size.Width \ 2, size.Height \ 2), size)
                                        End Sub
        AddHandler _grid.MouseMove, Sub(s, e)
                                        If e.Button <> MouseButtons.Left OrElse _dragFrom.IsEmpty OrElse _dragFrom.Contains(e.Location) Then Return
                                        _dragFrom = Rectangle.Empty
                                        If Not _boxesPanel.Visible Then Return
                                        SelectComics(_dragIds, scrollTo:=False, top:=Math.Max(0, _grid.FirstDisplayedScrollingRowIndex))
                                        _grid.DoDragDrop(New DataObject("ComicIds", _dragIds), DragDropEffects.Move)
                                    End Sub
        AddHandler _grid.MouseUp, Sub(s, e) _dragFrom = Rectangle.Empty
        Return _boxesPanel
    End Function

    ''' <summary>Fills the Boxes list (keeping the box being shown).</summary>
    Private Sub RefreshBoxes()
        If _db Is Nothing Then Return
        _fillingBoxes = True
        Try
            _boxCounts = _db.BoxCounts()
            _boxList.BeginUpdate()
            _boxList.Items.Clear()
            _boxList.Items.Add(AllBoxesItem)
            _boxList.Items.Add(NoBoxItem)
            For Each kv In _boxCounts
                _boxList.Items.Add($"{kv.Key}  ({kv.Value:N0})")
            Next
            _boxList.Items.Add(NewBoxItem)
            _boxList.EndUpdate()
            ShowBoxSelection()
        Finally
            _fillingBoxes = False
        End Try
    End Sub

    ''' <summary>Highlights the box being shown (or All comics).</summary>
    Private Sub ShowBoxSelection()
        Dim was = _fillingBoxes
        _fillingBoxes = True
        If _boxFilter Is Nothing Then
            _boxList.SelectedIndex = 0
        ElseIf _boxFilter = "" Then
            _boxList.SelectedIndex = 1
        Else
            Dim i = _boxCounts.FindIndex(Function(kv) String.Equals(kv.Key, _boxFilter, StringComparison.OrdinalIgnoreCase))
            If i < 0 Then
                _boxFilter = Nothing
                _boxList.SelectedIndex = 0
            Else
                _boxList.SelectedIndex = i + 2
            End If
        End If
        _fillingBoxes = was
    End Sub

    ''' <summary>The item's own name: a box's name without its count, or one of the fixed items.</summary>
    Private Function BoxItemName(index As Integer) As String
        If index < 0 OrElse index >= _boxList.Items.Count Then Return ""
        If index >= 2 AndAlso index - 2 < _boxCounts.Count Then Return _boxCounts(index - 2).Key
        Return CStr(_boxList.Items(index))
    End Function

    ''' <summary>The box filter for a list item: Nothing for all comics, "" for ones not in a box, else the box.</summary>
    Private Function BoxFilterFor(index As Integer) As String
        If index <= 0 Then Return Nothing
        If index = 1 Then Return ""
        Return BoxItemName(index)
    End Function

    Private Sub OnBoxDragOver(sender As Object, e As DragEventArgs)
        If Not e.Data.GetDataPresent("ComicIds") Then
            e.Effect = DragDropEffects.None
            Return
        End If
        Dim index = _boxList.IndexFromPoint(_boxList.PointToClient(New Point(e.X, e.Y)))
        ' "All comics" isn't somewhere to put them.
        e.Effect = If(index >= 1, DragDropEffects.Move, DragDropEffects.None)
        _dragging = True
        If index >= 1 Then _boxList.SelectedIndex = index
        _dragging = False
    End Sub

    Private Sub OnBoxDragDrop(sender As Object, e As DragEventArgs)
        Dim ids = TryCast(e.Data.GetData("ComicIds"), List(Of Long))
        Dim index = _boxList.IndexFromPoint(_boxList.PointToClient(New Point(e.X, e.Y)))
        ShowBoxSelection()
        If ids Is Nothing OrElse ids.Count = 0 OrElse index < 1 Then Return
        Dim box = If(index = 1, "", BoxItemName(index))
        If box = NewBoxItem Then
            box = Ui.Ask(Me, "New box", $"Name the new box or shelf for {ComicsText(ids.Count)}, for example Box 9:", "", _db.Locations())
            If String.IsNullOrWhiteSpace(box) Then Return
        End If
        Dim undo = _db.Snapshot(ids)
        _db.SetLocation(ids, box)
        RememberUndo(undo, If(box = "", $"Took {ComicsText(ids.Count)} out of their box", $"Put {ComicsText(ids.Count)} in {box}"))
        If box <> "" Then _settings.LastBox = box
        RefreshCollection()
    End Sub

    Private Sub SetBoxesVisible(visible As Boolean)
        _boxesPanel.Visible = visible
        _boxesButton.ForeColor = If(visible, Theme.Accent, Theme.Text)
        If _settings.ShowBoxes <> visible Then
            _settings.ShowBoxes = visible
            SaveSettingsQuietly()
        End If
        If Not visible AndAlso _boxFilter IsNot Nothing Then
            _boxFilter = Nothing
            RefreshCollection()
        End If
    End Sub

    ' ---------- undo ----------

    Private Shared Function ComicsText(n As Integer) As String
        Return If(n = 1, "1 comic", $"{n:N0} comics")
    End Function

    ''' <summary>Offers Undo for a change made since <paramref name="before"/> was taken.</summary>
    Private Sub RememberUndo(before As ComicSnapshot, what As String)
        RememberUndo(what, Sub() _db.Restore(before))
    End Sub

    Private Sub RememberUndo(what As String, undo As Action)
        _undoSteps.Add(Tuple.Create(what, undo))
        If _undoSteps.Count > 20 Then _undoSteps.RemoveAt(0)
        ShowUndo()
    End Sub

    Private Sub ShowUndo()
        If _undoSteps.Count = 0 Then
            _undoLink.Visible = False
            Return
        End If
        Dim what = _undoSteps(_undoSteps.Count - 1).Item1
        _undoLink.Text = $"{what}  ·  Undo (Ctrl+Z)"
        _undoLink.LinkArea = New LinkArea(what.Length + 5, _undoLink.Text.Length - what.Length - 5)
        _undoLink.Visible = True
    End Sub

    ''' <summary>Undoes the last change (Ctrl+Z). Each press goes one further back.</summary>
    Private Sub UndoLast()
        If _undoSteps.Count = 0 Then Return
        Dim last = _undoSteps(_undoSteps.Count - 1)
        _undoSteps.RemoveAt(_undoSteps.Count - 1)
        Try
            last.Item2.Invoke()
        Catch ex As Exception
            Ui.ShowError(Me, $"Couldn't undo ""{last.Item1}"": {Ui.Friendly(ex)}")
        End Try
        ShowUndo()
        If PageNames(_currentPage) = "Boxes" Then
            RefreshBoxesPage()
        ElseIf PageNames(_currentPage) <> "Collection" AndAlso PageNames(_currentPage) <> "Trades" Then
            ShowPage("Collection")
        End If
        RefreshCollection()
    End Sub

    Private Sub OnShowBin(sender As Object, e As EventArgs)
        Using f As New BinForm(_db)
            f.ShowDialog(Me)
            If f.RestoredComics > 0 Then RefreshCollection()
        End Using
    End Sub

    Private Sub OnDelete(sender As Object, e As EventArgs)
        Dim ids = SelectedComicIds()
        If ids.Count = 0 Then
            Ui.ShowError(Me, "Select the comics to delete first. Hold Ctrl or Shift to select several.")
            Return
        End If
        Dim names = ids.Take(8).Select(Function(id) If(_db.GetComic(id)?.ToString(), "")).ToList()
        If ids.Count > 8 Then names.Add($"…and {ids.Count - 8} more")
        If MessageBox.Show(Me, $"Delete {ComicsText(ids.Count)}? They go to the bin for {ComicDb.BinDays} days, so you can put them back " &
                           "(Undo, or More ▾ › Deleted comics)." & vbCrLf & vbCrLf & String.Join(vbCrLf, names),
                           "Delete comics", MessageBoxButtons.OKCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) <> DialogResult.OK Then Return
        Dim batch = _db.DeleteComics(ids)
        RememberUndo($"Deleted {ComicsText(ids.Count)}", Sub() _db.RestoreBatch(batch))
        RefreshCollection()
    End Sub

    ' ---------- boxes, trading, key issues and the insurance report ----------

    ''' <summary>The selected comics, or Nothing (after saying so) if none are selected.</summary>
    Private Function PickedComics() As List(Of Long)
        Dim ids = SelectedComicIds()
        If ids.Count = 0 Then
            Ui.ShowError(Me, "Select the comics first. Hold Ctrl or Shift to pick several.")
            Return Nothing
        End If
        Return ids
    End Function

    Private Sub OnSetBox(sender As Object, e As EventArgs)
        Dim ids = PickedComics()
        If ids Is Nothing Then Return
        ' Starts with the box they're already in, or else the box used last time (handy when filling one box).
        Dim current = If(_db.GetComic(ids(0))?.Location, "")
        Dim answer = Ui.Ask(Me, "Put in box", $"Which box or shelf {If(ids.Count = 1, "is it", $"are these {ids.Count}")} in? For example Box 3. Leave it blank to clear.",
                            If(current <> "", current, _settings.LastBox), _db.Locations())
        If answer Is Nothing Then Return
        Dim undo = _db.Snapshot(ids)
        _db.SetLocation(ids, answer)
        RememberUndo(undo, If(answer = "", $"Took {ComicsText(ids.Count)} out of their box", $"Put {ComicsText(ids.Count)} in {answer}"))
        If answer <> "" AndAlso answer <> _settings.LastBox Then
            _settings.LastBox = answer
            SaveSettingsQuietly()
        End If
        RefreshCollection()
    End Sub

    Private Sub OnForTrade(sender As Object, e As EventArgs)
        Dim ids = PickedComics()
        If ids Is Nothing Then Return
        Dim current = _db.GetComic(ids(0))?.AskingPrice
        Dim answer = Ui.Ask(Me, "For trade or sale", "Asking price for each copy (leave blank if you'd rather trade or haven't decided):",
                            Ui.MoneyText(current))
        If answer Is Nothing Then Return
        Dim price As Double? = Nothing
        If Not Ui.ParseMoney(answer, price) Then
            Ui.ShowError(Me, "Type the asking price as a number, like 15 or 15.50.")
            Return
        End If
        Dim undo = _db.Snapshot(ids)
        _db.SetForTrade(ids, True, price)
        RememberUndo(undo, $"Marked {ComicsText(ids.Count)} for trade")
        RefreshCollection()
    End Sub

    Private Sub OnNotForTrade(sender As Object, e As EventArgs)
        Dim ids = PickedComics()
        If ids Is Nothing Then Return
        Dim undo = _db.Snapshot(ids)
        _db.SetForTrade(ids, False)
        RememberUndo(undo, $"Marked {ComicsText(ids.Count)} not for trade")
        RefreshCollection()
    End Sub

    Private Sub OnKeyIssue(sender As Object, e As EventArgs)
        Dim ids = PickedComics()
        If ids Is Nothing Then Return
        Dim answer = Ui.Ask(Me, "Key issue", "Why is it a key issue? For example ""1st appearance of Harley Quinn"". Leave it blank to un-mark it.",
                            If(_db.GetComic(ids(0))?.KeyNote, ""))
        If answer Is Nothing Then Return
        Dim undo = _db.Snapshot(ids)
        _db.SetKeyNote(ids, answer)
        RememberUndo(undo, If(answer = "", $"Un-marked {ComicsText(ids.Count)} as key", $"Marked {ComicsText(ids.Count)} as key"))
        RefreshCollection()
    End Sub

    Private Sub OnInsurance(sender As Object, e As EventArgs)
        Using f As New InsuranceForm(_db, _settings)
            f.ShowDialog(Me)
        End Using
    End Sub

    ' ---------- eBay value estimates ----------

    Private Async Sub OnEstimateEbay(sender As Object, e As EventArgs)
        If _findingEbay Then
            _stopEbay = True
            _ebayButton.Text = "Stopping…"
            Return
        End If
        Await RunEbayEstimates(quiet:=False)
    End Sub

    ''' <summary>
    ''' Asks eBay (through the relay) what sellers want for each comic not checked in the last 30 days.
    ''' Quiet (from the background jobs) shows no messages. Click again to stop.
    ''' </summary>
    Private Async Function RunEbayEstimates(quiet As Boolean, Optional limit As Integer = Integer.MaxValue) As Task
        If _findingEbay Then Return
        Dim metron = Me.Metron
        If Not Await RelayReady(metron, "estimate values from eBay", 9, quiet) Then Return
        Try
            If Not Await metron.EbayReadyAsync() Then
                If Not quiet Then MessageBox.Show(Me, "Your relay needs your eBay developer keys first. The steps are under " &
                                                  "'Values from eBay' in relay\README.md on GitHub.", "Values from eBay",
                                                  MessageBoxButtons.OK, MessageBoxIcon.Information)
                _ebayStatus.Text = "Waiting for eBay keys on the relay."
                Return
            End If
        Catch ex As Exception
            If Not quiet Then Ui.ShowError(Me, $"Couldn't reach your relay: {Ui.Friendly(ex)}")
            Return
        End Try
        Dim todo = _db.ComicsNeedingEbay().Take(limit).ToList()
        If todo.Count = 0 Then
            If Not quiet Then MessageBox.Show(Me, "Every comic has been checked on eBay in the last 30 days.", "Values from eBay")
            ShowEbayStatus()
            Return
        End If
        _findingEbay = True
        _stopEbay = False
        Dim found = 0, done = 0
        Dim problem = ""
        Try
            For Each t In todo
                If _stopEbay OrElse IsDisposed Then Exit For
                Dim progress = $"Checking eBay {done + 1:N0} of {todo.Count:N0}"
                _ebayButton.Text = progress & " (click to stop)"
                _ebayItem.Text = progress & " (click to stop)"
                Dim estimate As EbayEstimate
                Try
                    estimate = Await metron.EbayEstimateAsync(t)
                Catch ex As InvalidOperationException When ex.Message.StartsWith("Too many")
                    problem = "eBay's daily limit was reached. It carries on tomorrow."
                    Exit For
                End Try
                _db.SetEbayEstimate(t.ComicId, estimate)
                If estimate.Typical.HasValue Then found += 1
                ' Show it straight away in the list.
                If _rows IsNot Nothing AndAlso _rows.Columns.Contains("eBay est.") Then
                    For Each row As DataRow In _rows.Rows
                        If Convert.ToInt64(row("comic_id"), CultureInfo.InvariantCulture) = t.ComicId Then
                            row("eBay est.") = If(estimate.Typical.HasValue, CObj(estimate.Typical.Value), DBNull.Value)
                        End If
                    Next
                End If
                done += 1
                If done < todo.Count Then Await Task.Delay(1500)
            Next
            If Not quiet AndAlso Not IsDisposed Then
                RefreshCollection()
                MessageBox.Show(Me, $"Found an eBay estimate for {found:N0} of the {done:N0} comics checked." &
                                If(problem <> "", " " & problem, If(done < todo.Count, " Click Estimate values from eBay again to carry on.", "")) &
                                vbCrLf & vbCrLf & "They're in the eBay est. column. To copy them into Value, use ""Use eBay estimates as values"" in the More menu.",
                                "Values from eBay")
            End If
        Catch ex As Exception
            problem = Ui.Friendly(ex, sentence:=True)
            If Not quiet AndAlso Not IsDisposed Then Ui.ShowError(Me, $"Checking eBay stopped: {Ui.Friendly(ex)}")
        Finally
            _findingEbay = False
            If Not IsDisposed Then
                _ebayButton.Text = "Estimate values from eBay"
                _ebayItem.Text = "Estimate values from eBay"
                ShowEbayStatus(problem)
            End If
        End Try
    End Function

    Private Sub ShowEbayStatus(Optional problem As String = "")
        If _db Is Nothing Then Return
        Dim t = _db.EbayTotals()
        _ebayStatus.Text = If(t.Checked = 0, "No comics checked yet.",
                              $"{t.Found:N0} of {t.Checked:N0} comics checked have an estimate, adding up to US${t.Total:N0}.") &
                           If(problem <> "", " " & problem, "")
    End Sub

    Private Sub OnUseEbay(sender As Object, e As EventArgs)
        Dim t = _db.EbayTotals()
        If t.Found = 0 Then
            Ui.ShowError(Me, "There are no eBay estimates yet. Click Estimate values from eBay first.")
            Return
        End If
        Dim answer = MessageBox.Show(Me, $"{t.Found:N0} comics have an eBay estimate." & vbCrLf & vbCrLf &
                                     "Yes: fill in Value only for comics that don't have one yet." & vbCrLf &
                                     "No: replace every value with the eBay estimate, including ones you typed in." & vbCrLf & vbCrLf &
                                     "Estimates are in US dollars and based on asking prices, which usually run a little above what comics sell for.",
                                     "Use eBay estimates as values", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question)
        If answer = DialogResult.Cancel Then Return
        Dim changed = _db.UseEbayEstimates(onlyBlank:=answer = DialogResult.Yes)
        _db.RecordValueHistory()
        RefreshCollection()
        MessageBox.Show(Me, $"Updated the value of {changed:N0} comics.", "Use eBay estimates as values")
    End Sub

    ' ---------- ratings, box labels, signing list, grading ----------

    Private Sub OnRate(sender As Object, e As EventArgs)
        Dim ids = PickedComics()
        If ids Is Nothing Then Return
        Dim first = _db.GetComic(ids(0))
        Dim name = If(ids.Count = 1, first.ToString(), $"{ids.Count} comics")
        Using f As New RateForm(name, first.Rating, If(ids.Count = 1, first.Review, ""))
            If f.ShowDialog(Me) <> DialogResult.OK Then Return
            ' With several picked, a blank review leaves each one's own review alone.
            Dim undo = _db.Snapshot(ids)
            _db.SetRating(ids, f.Stars, If(ids.Count = 1 OrElse f.Review <> "", f.Review, Nothing))
            RememberUndo(undo, $"Rated {ComicsText(ids.Count)}")
        End Using
        RefreshCollection()
    End Sub

    Private Sub OnBoxLabels(sender As Object, e As EventArgs)
        Dim boxes = _db.BoxLabels()
        If boxes.Count = 0 Then
            Ui.ShowError(Me, "No comics are in a box yet. Select some, then use More, then ""Put in a box or on a shelf"".")
            Return
        End If
        Using dlg As New SaveFileDialog With {.Title = "Save the box labels", .Filter = "PDF (*.pdf)|*.pdf",
                                              .FileName = $"Box labels {Date.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.pdf",
                                              .InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)}
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            Try
                UseWaitCursor = True
                Reports.BoxLabelsPdf(boxes, dlg.FileName)
                Process.Start(New ProcessStartInfo With {.FileName = dlg.FileName, .UseShellExecute = True})
            Catch ex As Exception
                Ui.ShowError(Me, $"Couldn't save it: {Ui.Friendly(ex)}")
            Finally
                UseWaitCursor = False
            End Try
        End Using
    End Sub

    Private Sub OnSigningList(sender As Object, e As EventArgs)
        Using f As New SigningForm(_db)
            f.ShowDialog(Me)
        End Using
    End Sub

    Private Sub OnWorthGrading(sender As Object, e As EventArgs)
        Using f As New GradingForm(_db, _settings)
            f.ShowDialog(Me)
        End Using
    End Sub

    ' ---------- wishlist deals ----------

    Private Sub OnWishMax(sender As Object, e As EventArgs)
        Dim ids = _wishGrid.SelectedRows.Cast(Of DataGridViewRow)().
            Select(Function(r) Convert.ToInt64(r.Cells("id").Value, CultureInfo.InvariantCulture)).ToList()
        If ids.Count = 0 Then
            Ui.ShowError(Me, "Select the wishlist comics first. Hold Ctrl or Shift to pick several.")
            Return
        End If
        Dim answer = Ui.Ask(Me, "Most I'd pay", "The most you'd pay for each, in your own dollars (leave blank for no limit and no eBay deals):")
        If answer Is Nothing Then Return
        Dim price As Double? = Nothing
        If Not Ui.ParseMoney(answer, price) Then
            Ui.ShowError(Me, "Type the price as a number, like 15 or 15.50.")
            Return
        End If
        _db.SetWishMaxPrice(ids, price)
        RefreshWishlist()
    End Sub

    Private Sub OnDeals(sender As Object, e As EventArgs)
        Using f As New DealsForm(_db, _settings)
            f.ShowDialog(Me)
        End Using
        RefreshWishlist()
    End Sub

    Private Sub UpdateDealsButton()
        If _db Is Nothing Then Return
        Dim n = _db.DealCount()
        _dealsButton.Text = If(n = 0, "eBay deals", $"eBay deals ({n})")
        _dealsButton.ForeColor = If(n > 0, Theme.Accent, Theme.Text)
        Dim wishIndex = Array.IndexOf(PageNames, "Wishlist")
        _navButtons(wishIndex).Text = If(n = 0, "  Wishlist", $"  Wishlist ({n} deal{If(n = 1, "", "s")})")
    End Sub

    ''' <summary>
    ''' Looks on eBay (once a day each) for wishlist comics with a most-you'd-pay. Max prices are in your
    ''' dollars and eBay's are US$, so they're converted with the rate in Settings.
    ''' </summary>
    Private Async Function RunDealChecks(quiet As Boolean) As Task
        If _findingEbay Then
            If Not quiet Then Ui.ShowError(Me, "The app is checking eBay already. Try again in a few minutes.")
            Return
        End If
        Dim metron = Me.Metron
        If Not Await EbayReady(metron, quiet, 10) Then Return
        Dim todo = _db.WishesNeedingDealCheck()
        If todo.Count = 0 Then
            If Not quiet Then MessageBox.Show(Me, "Every wishlist comic with a most-you'd-pay was checked today. Click ""Most I'd pay…"" to give others one.", "eBay deals")
            Return
        End If
        Dim rate = If(_settings.LocalPerUsd > 0, _settings.LocalPerUsd, 1)
        _findingEbay = True
        Dim added = 0
        Try
            For Each wish In todo
                If IsDisposed Then Exit For
                _ebayStatus.Text = $"Looking for wishlist deals {todo.IndexOf(wish) + 1:N0} of {todo.Count:N0}…"
                Dim target As New EbayTarget With {.Series = wish.Series, .Volume = wish.Volume, .Issue = wish.Issue}
                Dim found As List(Of EbayListing)
                Try
                    found = Await metron.EbayListingsAsync(target, wish.MaxPrice / rate)
                Catch ex As InvalidOperationException When ex.Message.StartsWith("Too many")
                    Exit For
                End Try
                added += _db.SaveDeals(wish.WishId, found)
                Await Task.Delay(1500)
            Next
        Catch ex As Exception
            If Not quiet AndAlso Not IsDisposed Then Ui.ShowError(Me, $"Looking for deals stopped: {Ui.Friendly(ex)}")
        Finally
            _findingEbay = False
            If Not IsDisposed Then
                ShowEbayStatus()
                UpdateDealsButton()
                If _wishGrid.Visible Then RefreshWishlist()
            End If
        End Try
        If IsDisposed Then Return
        If Not quiet Then
            MessageBox.Show(Me, If(added = 0, "No new deals right now.", $"Found {added} new deal{If(added = 1, "", "s")}. Click eBay deals to see them."), "eBay deals")
        ElseIf added > 0 AndAlso Not ContainsFocus Then
            ' Flash the taskbar button so a new deal doesn't go unnoticed.
            FlashWindow(Handle, True)
        End If
    End Function

    <System.Runtime.InteropServices.DllImport("user32.dll")>
    Private Shared Function FlashWindow(hwnd As IntPtr, invert As Boolean) As Boolean
    End Function

    ''' <summary>Checks the relay is new enough and has eBay keys. Says what's missing unless quiet.</summary>
    Private Async Function EbayReady(metron As MetronClient, quiet As Boolean, minVersion As Integer) As Task(Of Boolean)
        If Not Await RelayReady(metron, "use eBay", minVersion, quiet) Then Return False
        Try
            If Await metron.EbayReadyAsync() Then Return True
            If Not quiet Then MessageBox.Show(Me, "Your relay needs your eBay developer keys first. The steps are under " &
                                              "'Values from eBay' in relay\README.md on GitHub.", "Values from eBay",
                                              MessageBoxButtons.OK, MessageBoxIcon.Information)
            _ebayStatus.Text = "Waiting for eBay keys on the relay."
        Catch ex As Exception
            If Not quiet Then Ui.ShowError(Me, $"Couldn't reach your relay: {Ui.Friendly(ex)}")
        End Try
        Return False
    End Function

    ''' <summary>Background only: CGC 9.8 prices for raw comics worth checking, for "Worth getting graded?".</summary>
    Private Async Function RunGradedChecks(limit As Integer) As Task
        If _findingEbay Then Return
        Dim metron = Me.Metron
        If Not Await EbayReady(metron, True, 9) Then Return
        Dim todo = _db.ComicsNeedingGradedCheck().Take(limit).ToList()
        If todo.Count = 0 Then Return
        _findingEbay = True
        Try
            For Each t In todo
                If IsDisposed OrElse Not _settings.AutoJobs Then Exit For
                Dim estimate As EbayEstimate
                Try
                    estimate = Await metron.EbayEstimateAsync(t)
                Catch ex As InvalidOperationException When ex.Message.StartsWith("Too many")
                    Exit For
                End Try
                _db.SetGradedEstimate(t.ComicId, estimate)
                Await Task.Delay(1500)
            Next
        Finally
            _findingEbay = False
        End Try
    End Function

    ' ---------- What should I read next? ----------

    Private Sub OnReadNext(sender As Object, e As EventArgs)
        Using f As New ReadNextForm(_db)
            Dim result = f.ShowDialog(Me)
            If f.MarkedAny OrElse result = DialogResult.OK Then RefreshCollection(f.ShowComicId)
        End Using
    End Sub

    ' ---------- share page ----------

    Private Sub OnShare(wishlistOnly As Boolean)
        Using f As New ShareForm(_db, _settings, wishlistOnly)
            f.ShowDialog(Me)
        End Using
    End Sub

    ' ---------- Lent out ----------

    Private Sub OnLend(sender As Object, e As EventArgs)
        Dim ids = SelectedComicIds()
        If ids.Count = 0 Then
            Ui.ShowError(Me, "Select the comics you're lending first. Hold Ctrl or Shift to pick several.")
            Return
        End If
        Dim what = If(ids.Count = 1, $"Lending {_db.GetComic(ids(0))}", $"Lending {ids.Count} comics")
        Using f As New LendForm(what, _db.Borrowers())
            If f.ShowDialog(Me) <> DialogResult.OK Then Return
            Dim undo = _db.Snapshot(ids)
            _db.LendComics(ids, f.Borrower, f.LentOn, f.Notes)
            RememberUndo(undo, $"Lent {ComicsText(ids.Count)} to {f.Borrower}")
        End Using
        RefreshCollection()
    End Sub

    Private Function BuildLoansTab() As Control
        Dim page As New Panel()
        Dim bar As New FlowLayoutPanel With {.Dock = DockStyle.Top, .AutoSize = True, .Padding = New Padding(4)}
        bar.Controls.AddRange({Ui.MakeButton("Given back", AddressOf OnGiveBack, Theme.PrimaryTag), _showReturned, _loansNote})
        AddHandler _showReturned.CheckedChanged, Sub(s, e) RefreshLoans()
        Dim hint = Ui.MakeLabel("To lend comics, select them on the Collection page and click Lend. Search the collection for someone's name to see what they have.")
        hint.Dock = DockStyle.Top
        hint.Padding = New Padding(4, 0, 4, 8)
        hint.Tag = Theme.MutedTag
        AddHandler _loansGrid.CellDoubleClick, Sub(s, e)
                                                   If e.RowIndex < 0 Then Return
                                                   Dim c = _db.GetComic(Convert.ToInt64(_loansGrid.Rows(e.RowIndex).Cells("comic_id").Value, CultureInfo.InvariantCulture))
                                                   If c IsNot Nothing Then EditComic(c)
                                               End Sub
        page.Controls.Add(_loansGrid)
        page.Controls.Add(hint)
        page.Controls.Add(bar)
        Return page
    End Function

    Private Sub RefreshLoans()
        _loansGrid.DataSource = _db.GetLoans(_showReturned.Checked)
        For Each colName In {"loan_id", "comic_id"}
            If _loansGrid.Columns.Contains(colName) Then _loansGrid.Columns(colName).Visible = False
        Next
        If _loansGrid.Columns.Contains("Comic") Then _loansGrid.Columns("Comic").FillWeight = 180
        Dim out = _db.LentOutCount()
        _loansNote.Text = If(out = 0, "Nothing is lent out right now.", $"{out:N0} comic{If(out = 1, " is", "s are")} lent out.")
    End Sub

    Private Sub OnGiveBack(sender As Object, e As EventArgs)
        Dim ids = _loansGrid.SelectedRows.Cast(Of DataGridViewRow)().
            Where(Function(r) Convert.ToString(r.Cells("Given back").Value, CultureInfo.InvariantCulture) = "").
            Select(Function(r) Convert.ToInt64(r.Cells("loan_id").Value, CultureInfo.InvariantCulture)).ToList()
        If ids.Count = 0 Then
            Ui.ShowError(Me, "Select the comics you've got back first. Hold Ctrl or Shift to pick several.")
            Return
        End If
        _db.ReturnLoans(ids, Date.Today)
        RefreshLoans()
        RefreshCollection()
    End Sub

    ' ---------- Stats ----------

    Private Function BuildStatsTab() As Control
        Dim page As New Panel()
        _statsByValue.Items.AddRange({"Count comics", "Add up their value"})
        _statsByValue.SelectedIndex = 0
        _statsByValue.Font = New Font("Segoe UI", 11.0F)
        AddHandler _statsByValue.SelectedIndexChanged, Sub(s, e) RefreshStats()
        Dim bar As New FlowLayoutPanel With {.Dock = DockStyle.Top, .AutoSize = True, .Padding = New Padding(0, 0, 0, 8)}
        _loadingGoals = True
        _budget.Value = CDec(Math.Min(_budget.Maximum, Math.Max(0, _settings.MonthlyBudget)))
        _readGoal.Value = Math.Min(_readGoal.Maximum, Math.Max(0, _settings.MonthlyReadGoal))
        _yearGoal.Value = Math.Min(_yearGoal.Maximum, Math.Max(0, _settings.YearlyReadGoal))
        _loadingGoals = False
        _budget.Font = New Font("Segoe UI", 11.0F)
        _readGoal.Font = New Font("Segoe UI", 11.0F)
        _yearGoal.Font = New Font("Segoe UI", 11.0F)
        AddHandler _yearGoal.ValueChanged, AddressOf OnGoalsChanged
        AddHandler _budget.ValueChanged, AddressOf OnGoalsChanged
        AddHandler _readGoal.ValueChanged, AddressOf OnGoalsChanged
        bar.Controls.AddRange({Ui.MakeLabel("Bars"), _statsByValue, New Label With {.Width = 16},
                               Ui.MakeLabel("Monthly budget $"), _budget, Ui.MakeLabel("Read each month"), _readGoal,
                               Ui.MakeLabel("Read this year"), _yearGoal, _statsNote})

        Dim charts As New TableLayoutPanel With {.Dock = DockStyle.Top, .AutoSize = True, .ColumnCount = 2}
        charts.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 50))
        charts.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 50))
        Dim order = {_chartHistory, _chartSpending, _chartReading, _chartMostRead, _chartPublishers, _chartDecades, _chartSeries, _chartValuable,
                     _chartKeys, _chartRising, _chartWriters, _chartArtists}
        Dim row = 0, col = 0
        For Each c In order
            charts.RowStyles.Add(New RowStyle(SizeType.Absolute, c.Height + c.Margin.Vertical))
            If c Is _chartHistory Then
                charts.Controls.Add(c, 0, row)
                charts.SetColumnSpan(c, 2)
                row += 1
                Continue For
            End If
            charts.Controls.Add(c, col, row)
            col += 1
            If col = 2 Then
                col = 0
                row += 1
            End If
        Next
        Dim scroll As New Panel With {.Dock = DockStyle.Fill, .AutoScroll = True}
        scroll.Controls.Add(charts)
        page.Controls.Add(scroll)
        page.Controls.Add(_statsCards)
        page.Controls.Add(bar)
        Return page
    End Function

    Private Sub RefreshStats()
        Dim byValue = _statsByValue.SelectedIndex = 1
        For Each c In {_chartPublishers, _chartDecades, _chartSeries, _chartWriters, _chartArtists}
            c.ByValue = byValue
        Next
        _chartPublishers.ShowBars(_db.StatsBy("publisher", 10))
        _chartDecades.ShowBars(_db.StatsBy("decade", 20))
        _chartSeries.ShowBars(_db.StatsBy("series", 10))
        _chartValuable.ShowBars(_db.StatsBy("value", 10))
        _chartValuable.EmptyText = "Give your comics a value (Edit, then Value each) to see your most valuable ones here."
        Dim credits = _db.CreditsChecked()
        Dim waiting = If(credits.Checked = 0,
            "Writers and artists fill in as the app looks up your comics' details in the background (or click Find story arcs on the Sets page). " &
            "This needs the latest relay code.",
            $"Writers and artists are known for {credits.Checked:N0} of the {credits.Matched:N0} comics matched to Metron so far.")
        _chartWriters.EmptyText = waiting
        _chartArtists.EmptyText = waiting
        _chartWriters.ShowBars(_db.StatsBy("writer", 10))
        _chartArtists.ShowBars(_db.StatsBy("artist", 10))
        _db.RecordValueHistory()
        _chartHistory.ShowLine(_db.ValueHistory())
        Dim spending = _db.SpendingByMonth(12)
        Dim reading = _db.ReadByMonth(12)
        _chartSpending.EmptyText = "Fill in Date bought and Paid when you add comics to see your spending here."
        _chartSpending.ShowBars(If(spending.Any(Function(m) m.Count > 0), spending, New List(Of ChartItem)))
        _chartReading.EmptyText = "Mark comics read (R on the Collection page) to see how many you read each month."
        _chartReading.ShowBars(If(reading.Any(Function(m) m.Count > 0), reading, New List(Of ChartItem)))
        Dim readItems = _db.ReadingItems()
        _chartMostRead.EmptyText = "Mark comics read (R on the Collection page) to see the series you've read most."
        _chartMostRead.ShowBars(ReadingStats.MostReadSeries(readItems, 10))
        Dim longest = ReadingStats.LongestRun(readItems)
        _chartKeys.EmptyText = "Mark key issues (right-click a comic, then Mark as key issue) to see them here."
        _chartKeys.ShowBars(_db.StatsBy("keys", 10))
        _chartRising.EmptyText = "Once eBay estimates have been checked twice, a month or more apart, the comics going up most show here."
        _chartRising.ShowBars(_db.RisingComics(10))
        _statsNote.Text = If(credits.Checked > 0 AndAlso credits.Checked < credits.Matched,
                             $"Writers and artists: {credits.Checked:N0} of {credits.Matched:N0} comics looked up so far.", "")

        ' Big numbers across the top
        Dim s = _db.GetStats()
        For Each old As Control In _statsCards.Controls.Cast(Of Control)().ToList()
            old.Dispose()
        Next
        _statsCards.Controls.Clear()
        Dim gain = s.TotalValue - s.TotalPaid
        Dim readPct = If(s.Comics = 0, 0, s.Read * 100.0 / s.Comics)
        For Each card In {(s.Comics.ToString("N0"), "COMICS"), ("$" & s.TotalValue.ToString("N0"), "COLLECTION VALUE"),
                          ("$" & s.TotalPaid.ToString("N0"), "TOTAL PAID"),
                          (If(gain >= 0, "+$", "-$") & Math.Abs(gain).ToString("N0"), If(gain >= 0, "UP ON WHAT YOU PAID", "DOWN ON WHAT YOU PAID")),
                          (readPct.ToString("0") & "%", "READ"), (_db.LentOutCount().ToString("N0"), "LENT OUT"),
                          (MonthText(spending.Last().Value, _settings.MonthlyBudget, "$"), "SPENT THIS MONTH"),
                          (MonthText(reading.Last().Count, _settings.MonthlyReadGoal, ""), "READ THIS MONTH"),
                          (MonthText(_db.ReadThisYear(), _settings.YearlyReadGoal, ""), $"READ IN {Date.Today.Year}"),
                          (If(longest Is Nothing, "-", $"{longest.Length:N0} in a row"), If(longest Is Nothing, "LONGEST RUN READ", Shorten($"LONGEST RUN: {longest}".ToUpperInvariant(), 30))),
                          (_db.KeyIssueCount().ToString("N0"), "KEY ISSUES")}
            Dim value As New Label()
            _statsCards.Controls.Add(StatCard(value, card.Item2))
            value.Text = card.Item1
        Next
        Dim ebay = _db.EbayTotals()
        If ebay.Found > 0 Then
            Dim value As New Label()
            _statsCards.Controls.Add(StatCard(value, "EBAY ESTIMATE (US$)"))
            value.Text = "$" & ebay.Total.ToString("N0")
        End If
        Theme.Apply(_statsCards)
    End Sub

    ' "$45 of $100" against a monthly goal, or just "$45" without one.
    Private Shared Function MonthText(amount As Double, goal As Double, prefix As String) As String
        Dim text = prefix & amount.ToString("N0")
        Return If(goal > 0, $"{text} of {prefix}{goal:N0}", text)
    End Function

    Private Sub OnGoalsChanged(sender As Object, e As EventArgs)
        If _loadingGoals Then Return
        _settings.MonthlyBudget = CDbl(_budget.Value)
        _settings.MonthlyReadGoal = CInt(_readGoal.Value)
        _settings.YearlyReadGoal = CInt(_yearGoal.Value)
        _settings.Save()
        If _db IsNot Nothing Then RefreshStats()
    End Sub

    ' ---------- Missing issues ----------

    Private Function BuildMissingTab() As Control
        Dim page As New Panel()
        Dim split As New SplitContainer With {.Dock = DockStyle.Fill, .SplitterDistance = 600}
        Dim help As New Label With {.Dock = DockStyle.Top, .Height = 28, .TextAlign = ContentAlignment.MiddleLeft,
                                    .Text = "Series with issues missing. Click one to see which. Once a series' length is found on Metron, it counts to the end, not just to your last issue."}
        Dim lengthsBar As New FlowLayoutPanel With {.Dock = DockStyle.Top, .AutoSize = True}
        lengthsBar.Controls.AddRange({_findLengths, _showHidden, _lengthsStatus})
        AddHandler _showHidden.Click, Sub(s, e)
                                          _settings.HiddenMissing.Clear()
                                          SaveSettingsQuietly()
                                          RefreshGaps()
                                      End Sub
        split.Panel1.Controls.Add(_gapsGrid)
        split.Panel1.Controls.Add(help)
        split.Panel1.Controls.Add(lengthsBar)
        Dim wishButton = Ui.MakeButton("Add selected to wishlist", AddressOf OnWishMissing)
        wishButton.Dock = DockStyle.Bottom
        ' For ones you have but haven't scanned: adds them without scanning.
        Dim haveButton = Ui.MakeButton("I have these: add to my collection…", AddressOf OnHaveMissing, Theme.PrimaryTag)
        haveButton.Dock = DockStyle.Bottom
        split.Panel2.Controls.Add(_missingList)
        split.Panel2.Controls.Add(_missingTitle)
        split.Panel2.Controls.Add(wishButton)
        split.Panel2.Controls.Add(haveButton)
        Dim hideButton = Ui.MakeButton("Not collecting this — hide it", Sub(s, e)
                                                                           If _gapsGrid.CurrentRow Is Nothing Then Return
                                                                           Dim series = CStr(_gapsGrid.CurrentRow.Cells("Series").Value)
                                                                           Dim volume = Convert.ToString(_gapsGrid.CurrentRow.Cells("Volume").Value, CultureInfo.InvariantCulture)
                                                                           Dim key = ComicDb.HiddenKey(series, volume)
                                                                           If Not _settings.HiddenMissing.Contains(key) Then _settings.HiddenMissing.Add(key)
                                                                           SaveSettingsQuietly()
                                                                           RememberUndo($"Hid {series}{If(volume <> "", $" ({volume})", "")} from Missing issues",
                                                                                        Sub()
                                                                                            _settings.HiddenMissing.Remove(key)
                                                                                            SaveSettingsQuietly()
                                                                                            RefreshGaps()
                                                                                        End Sub)
                                                                           RefreshGaps()
                                                                       End Sub)
        hideButton.Dock = DockStyle.Bottom
        split.Panel2.Controls.Add(hideButton)
        Dim mergeButton = Ui.MakeButton("Same series as another? Merge them…", Sub(s, e)
                                                                                  If _gapsGrid.CurrentRow Is Nothing Then Return
                                                                                  MergeSeriesFrom(CStr(_gapsGrid.CurrentRow.Cells("Series").Value),
                                                                                                  Convert.ToString(_gapsGrid.CurrentRow.Cells("Volume").Value, CultureInfo.InvariantCulture))
                                                                              End Sub)
        mergeButton.Dock = DockStyle.Bottom
        split.Panel2.Controls.Add(mergeButton)
        AddHandler _gapsGrid.SelectionChanged, Sub(s, e) ShowMissing()
        page.Controls.Add(split)
        Return page
    End Function

    Private Sub RefreshGaps()
        _gapsGrid.DataSource = _db.SeriesWithGaps(_settings.HiddenMissing)
        _showHidden.Visible = _settings.HiddenMissing.Count > 0
        _showHidden.Text = $"Show {_settings.HiddenMissing.Count:N0} hidden"
        _showHidden.Margin = New Padding(3, 3, 3, 3)
        ShowMissing()
        If Not _findingLengths Then
            Dim n = _db.SeriesNeedingTotals().Count
            _lengthsStatus.Text = If(n = 0, "Every series' length has been looked up.", $"{n:N0} series not looked up yet.")
        End If
    End Sub

    Private Async Sub OnFindLengths(sender As Object, e As EventArgs)
        If _findingLengths Then
            _stopLengths = True
            _findLengths.Text = "Stopping…"
            Return
        End If
        Await RunFindLengths(quiet:=False)
    End Sub

    ''' <summary>Looks up how many issues each series has on Metron (relay version 12), so Missing issues counts to the end of each.</summary>
    Private Async Function RunFindLengths(quiet As Boolean) As Task
        If _findingLengths Then Return
        Dim metron = Me.Metron
        If Not Await RelayReady(metron, "find how long each series is", minVersion:=12, quiet:=quiet) Then Return
        Dim todo = _db.SeriesNeedingTotals()
        If todo.Count = 0 Then
            If Not quiet Then MessageBox.Show(Me, "Every series' length has been looked up in the last month.", "Series lengths")
            Return
        End If
        _findingLengths = True
        _stopLengths = False
        Dim found = 0, done = 0
        Try
            For Each t In todo
                If _stopLengths OrElse IsDisposed Then Exit For
                _findLengths.Text = $"Looking up {done + 1:N0} of {todo.Count:N0} (click to stop)"
                Dim name = t.Name, volume = t.Volume
                Dim total = Await WithRetry(Function() metron.SeriesLengthAsync(name, volume), _lengthsStatus)
                _db.SetSeriesTotal(name, volume, total)
                If total > 0 Then found += 1
                done += 1
                If done < todo.Count Then Await Task.Delay(2500)
            Next
            If Not quiet AndAlso Not IsDisposed Then
                MessageBox.Show(Me, $"Found the length of {found:N0} of the {done:N0} series looked up. Missing issues now counts to the end of those." &
                                If(done < todo.Count, " Click the button again to carry on.", ""), "Series lengths")
            End If
        Catch ex As Exception
            If Not quiet AndAlso Not IsDisposed Then Ui.ShowError(Me, $"Finding series lengths stopped: {Ui.Friendly(ex)}")
        Finally
            _findingLengths = False
            If Not IsDisposed Then
                _findLengths.Text = "Find how long each series is"
                If _currentPage >= 0 AndAlso PageNames(_currentPage) = "Missing issues" Then RefreshGaps()
            End If
        End Try
    End Function

    Private Sub ShowMissing()
        _missingList.Items.Clear()
        If _gapsGrid.CurrentRow Is Nothing Then
            _missingTitle.Text = If(_gapsGrid.Rows.Count = 0, "No gaps found in your runs.", "")
            Return
        End If
        Dim series = CStr(_gapsGrid.CurrentRow.Cells("Series").Value)
        Dim volume = Convert.ToString(_gapsGrid.CurrentRow.Cells("Volume").Value, CultureInfo.InvariantCulture)
        Dim missing = _db.MissingIssues(series, volume)
        _missingTitle.Text = $"Missing from {series}{If(volume <> "", $" ({volume})", "")}: {missing.Count}"
        _missingList.Items.AddRange(missing.Select(Function(n) CObj($"#{n}")).ToArray())
    End Sub

    Private Sub OnWishMissing(sender As Object, e As EventArgs)
        If _gapsGrid.CurrentRow Is Nothing OrElse _missingList.SelectedItems.Count = 0 Then
            Ui.ShowError(Me, "Pick a series, then select the missing issues to add (hold Ctrl or Shift for several).")
            Return
        End If
        Dim series = CStr(_gapsGrid.CurrentRow.Cells("Series").Value)
        For Each item In _missingList.SelectedItems
            _db.AddWish(series, CStr(item).TrimStart("#"c), 2, Nothing, "")
        Next
        MessageBox.Show(Me, $"Added {_missingList.SelectedItems.Count} to your wishlist.", "Wishlist")
    End Sub

    ''' <summary>Missing issues page: adds the selected missing issues (or all of them) as ones you have.</summary>
    Private Sub OnHaveMissing(sender As Object, e As EventArgs)
        If _gapsGrid.CurrentRow Is Nothing Then
            Ui.ShowError(Me, "Pick a series on the left first.")
            Return
        End If
        Dim series = CStr(_gapsGrid.CurrentRow.Cells("Series").Value)
        Dim volume = Convert.ToString(_gapsGrid.CurrentRow.Cells("Volume").Value, CultureInfo.InvariantCulture)
        Dim picked = If(_missingList.SelectedItems.Count > 0, _missingList.SelectedItems.Cast(Of Object)(), _missingList.Items.Cast(Of Object)()).
            Select(Function(i) CInt(CStr(i).TrimStart("#"c))).ToList()
        Dim template = _db.GetComic(_db.SearchCollection("", kind:="issues").Rows.Cast(Of DataRow)().
            Where(Function(r) CStr(r("Series")) = series AndAlso Convert.ToString(r("Volume"), CultureInfo.InvariantCulture) = volume).
            Select(Function(r) Convert.ToInt64(r("comic_id"), CultureInfo.InvariantCulture)).FirstOrDefault())
        If template Is Nothing Then Return
        AddIssuesOf(template, IssueRanges(picked))
        RefreshGaps()
    End Sub

    ''' <summary>Collection page: adds more issues of the selected comic's series without scanning them.</summary>
    Private Sub OnAddMoreIssues(sender As Object, e As EventArgs)
        Dim ids = PickedComics()
        If ids Is Nothing Then Return
        Dim template = _db.GetComic(ids(0))
        If template IsNot Nothing Then AddIssuesOf(template, IssueRanges(_db.MissingIssues(template.Series, template.Volume)))
    End Sub

    Private Sub AddIssuesOf(template As ComicRecord, issuesText As String)
        Dim name = $"{template.Series}{If(template.Volume <> "", $" ({template.Volume})", "")}"
        Dim missing = _db.MissingIssues(template.Series, template.Volume)
        Dim haveText = $"You have {_db.SearchCollection("", kind:=If(ComicDb.IsTrade(template.Format), "trades", "issues")).Rows.Cast(Of DataRow)().
            Count(Function(r) CStr(r("Series")) = template.Series AndAlso Convert.ToString(r("Volume"), CultureInfo.InvariantCulture) = template.Volume):N0} of {name}" &
            If(missing.Count > 0, $", with {missing.Count:N0} missing (filled in below).", ".")
        Using f As New AddIssuesForm(name, issuesText, haveText, _db.Locations(), _settings.LastBox)
            If f.ShowDialog(Me) <> DialogResult.OK Then Return
            Dim added As List(Of Long)
            Try
                added = _db.AddIssues(template, f.Issues, f.Condition, f.Box)
            Catch ex As Exception
                Ui.ShowError(Me, $"Couldn't add them: {Ui.Friendly(ex)}")
                Return
            End Try
            If added.Count = 0 Then
                MessageBox.Show(Me, "You already have all of those.", "Add issues", MessageBoxButtons.OK, MessageBoxIcon.Information)
                Return
            End If
            RememberUndo($"Added {ComicsText(added.Count)} of {name}", Sub() _db.DeleteComics(added))
            If f.Box <> "" Then _settings.LastBox = f.Box
            ShowPage(If(ComicDb.IsTrade(template.Format), "Trades", "Collection"))
            _search.Text = $"""{template.Series}"""
            RefreshCollection(added(0))
            UpdateFindCoversButton()
            Dim skipped = f.Issues.Count - added.Count
            MessageBox.Show(Me, $"Added {ComicsText(added.Count)} of {name}.{If(skipped > 0, $" {skipped:N0} you already had were skipped.", "")}" & vbCrLf & vbCrLf &
                            If(_settings.AutoJobs, "Their covers and details are looked up by themselves in the next few minutes.",
                               "Click Find covers and prices to fill in their covers and details."),
                            "Add issues", MessageBoxButtons.OK, MessageBoxIcon.Information)
        End Using
    End Sub

    ''' <summary>
    ''' Moves every comic of one series into another, for one series under two names ("The Walking Dead Deluxe" and
    ''' "The Walking Dead Deluxe (2020)"). Offers the other one with the same name first.
    ''' </summary>
    Private Sub MergeSeriesFrom(name As String, volume As String)
        Dim label = Function(n As String, v As String) $"{n}{If(v <> "", $" ({v})", "")}"
        Dim fromLabel = label(name, volume)
        Dim others = _db.SeriesList().Where(Function(x) Not (x.Name = name AndAlso x.Volume = volume)).ToList()
        If others.Count = 0 Then Return
        Dim bare = Function(n As String) System.Text.RegularExpressions.Regex.Replace(n, "\s*\(\d{4}\)\s*$", "").Trim()
        Dim choices = others.ToDictionary(Function(x) $"{label(x.Name, x.Volume)}  ·  {x.Count:N0} comic{If(x.Count = 1, "", "s")}", Function(x) x)
        Dim best = choices.FirstOrDefault(Function(kv) String.Equals(bare(kv.Value.Name), bare(name), StringComparison.OrdinalIgnoreCase)).Key
        Dim answer = Ui.Ask(Me, "Merge series", $"Move every comic of {fromLabel} into which series? An issue in both is kept once, with the copies added together.",
                            If(best, ""), choices.Keys)
        If String.IsNullOrWhiteSpace(answer) Then Return
        Dim target As (Name As String, Volume As String, Count As Integer) = Nothing
        If Not choices.TryGetValue(answer, target) Then
            Ui.ShowError(Me, "Pick one of the series in the list.")
            Return
        End If
        If MessageBox.Show(Me, $"Move all of {fromLabel} into {label(target.Name, target.Volume)}?{vbCrLf}{vbCrLf}This can't be undone with Ctrl+Z.",
                           "Merge series", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) <> DialogResult.OK Then Return
        Dim moved As Integer
        Try
            moved = _db.MergeSeries(name, volume, target.Name, target.Volume)
        Catch ex As Exception
            Ui.ShowError(Me, $"Couldn't merge them: {Ui.Friendly(ex)}")
            Return
        End Try
        ' Issues added by number that are now next to the same issue with its details become one.
        Dim tidied = TidyDoubles(target.Name, quiet:=True)
        RefreshCollection()
        RefreshGaps()
        MessageBox.Show(Me, $"Moved {ComicsText(moved)} into {label(target.Name, target.Volume)}." &
                        If(tidied > 0, $" {ComicsText(tidied)} that were there twice (once added by number) are now one.", ""),
                        "Merge series", MessageBoxButtons.OK, MessageBoxIcon.Information)
    End Sub

    ''' <summary>Makes issues that are there twice in a series (once added by number, once with details) one. Returns how many.</summary>
    Private Function TidyDoubles(seriesName As String, quiet As Boolean) As Integer
        Dim result As (Removed As Integer, Batch As String, Before As ComicSnapshot)
        Try
            result = _db.TidyDoubledIssues(seriesName)
        Catch ex As Exception
            If Not quiet Then Ui.ShowError(Me, $"Couldn't tidy them: {Ui.Friendly(ex)}")
            Return 0
        End Try
        If result.Removed > 0 Then
            Dim r = result
            RememberUndo($"Tidied {ComicsText(r.Removed)} doubled up in {seriesName}", Sub()
                                                                                        _db.RestoreBatch(r.Batch)
                                                                                        _db.Restore(r.Before)
                                                                                    End Sub)
        End If
        If Not quiet Then
            RefreshCollection()
            MessageBox.Show(Me, If(result.Removed = 0, $"Nothing in {seriesName} is there twice.",
                                   $"{ComicsText(result.Removed)} of {seriesName} were there twice (once added by number, once with details). " &
                                   "Each is now one comic, keeping its details and cover. Ctrl+Z puts them back."),
                            "Tidy doubled-up issues", MessageBoxButtons.OK, MessageBoxIcon.Information)
        End If
        Return result.Removed
    End Function

    ''' <summary>Issue numbers as short runs: 3, 4, 5, 9 gives "3-5, 9".</summary>
    Private Shared Function IssueRanges(numbers As IEnumerable(Of Integer)) As String
        Dim parts As New List(Of String)
        Dim sorted = numbers.Distinct().OrderBy(Function(n) n).ToList()
        Dim i = 0
        While i < sorted.Count
            Dim j = i
            While j + 1 < sorted.Count AndAlso sorted(j + 1) = sorted(j) + 1
                j += 1
            End While
            parts.Add(If(j = i, $"{sorted(i)}", $"{sorted(i)}-{sorted(j)}"))
            i = j + 1
        End While
        Return String.Join(", ", parts)
    End Function

    ' ---------- Wishlist ----------

    ' ---------- story arcs and runs ----------

    Private Function BuildSetsTab() As Control
        Dim page As New Panel()
        Dim bar As New FlowLayoutPanel With {.Dock = DockStyle.Top, .AutoSize = True, .Padding = New Padding(4)}
        bar.Controls.AddRange({Ui.MakeButton("Change name or value", AddressOf OnEditSet, Theme.PrimaryTag),
                               Ui.MakeButton("Check value on eBay", AddressOf OnCheckSetValue),
                               Ui.MakeButton("Add missing to wishlist", AddressOf OnWishMissingFromSets),
                               Ui.MakeButton("Show its comics", AddressOf OnShowSet),
                               Ui.MakeButton("Remove set", AddressOf OnRemoveSet, Theme.DangerTag),
                               New Label With {.Width = 16},
                               _findArcs, _arcStatus})
        Dim hint = Ui.MakeLabel("To make a set, select its comics on the Collection page (hold Ctrl or Shift to pick several), then click Make a set. " &
                                "Find story arcs makes sets for you from Metron, for arcs you have two or more issues of.")
        hint.MaximumSize = New Size(1200, 0)
        hint.Dock = DockStyle.Top
        hint.Padding = New Padding(4, 0, 4, 8)
        hint.Tag = Theme.MutedTag
        AddHandler _setsGrid.CellDoubleClick, Sub(s, e)
                                                  If e.RowIndex >= 0 Then OnEditSet(s, e)
                                              End Sub
        page.Controls.Add(_setsGrid)
        page.Controls.Add(hint)
        page.Controls.Add(bar)
        Return page
    End Function

    Private Sub RefreshSets()
        _setsGrid.DataSource = _db.GetSets()
        For Each colName In {"id", "arc_id"}
            If _setsGrid.Columns.Contains(colName) Then _setsGrid.Columns(colName).Visible = False
        Next
        For Each colName In {"Comics", "Whole arc"}
            If _setsGrid.Columns.Contains(colName) Then
                _setsGrid.Columns(colName).FillWeight = 50
                _setsGrid.Columns(colName).DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter
            End If
        Next
        If _setsGrid.Columns.Contains("Value") Then
            _setsGrid.Columns("Value").DefaultCellStyle.Format = "N2"
            _setsGrid.Columns("Value").DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight
        End If
    End Sub

    Private Sub OnCheckSetValue(sender As Object, e As EventArgs)
        Dim row = SelectedSet()
        If row Is Nothing Then Return
        Dim words = _db.SetSearchWords(Convert.ToInt64(row("id"), CultureInfo.InvariantCulture))
        Process.Start(New ProcessStartInfo With {.UseShellExecute = True,
            .FileName = "https://www.ebay.com/sch/i.html?LH_Sold=1&LH_Complete=1&_nkw=" & Uri.EscapeDataString(words)})
    End Sub

    ''' <summary>
    ''' Looks up which story arcs your comics are part of on Metron (comics with a Metron number, which
    ''' Find covers adds), then makes a set for each arc you have two or more issues of. Slow, to stay inside
    ''' Metron's limits; click again to stop, and it carries on where it left off next time.
    ''' </summary>
    Private Async Sub OnFindArcs(sender As Object, e As EventArgs)
        If _findingArcs Then
            _stopArcs = True
            _findArcs.Text = "Stopping…"
            Return
        End If
        Await RunFindArcs(quiet:=False)
    End Sub

    Private Async Function RunFindArcs(quiet As Boolean) As Task
        If _findingArcs Then Return
        Dim metron = Me.Metron
        If Not Await RelayReady(metron, "find story arcs", quiet:=quiet) Then Return
        ' Relay version 8 also says who wrote and drew each comic, for the Stats page.
        Dim withCredits = False
        Try
            withCredits = Await metron.RelayVersionAsync() >= 8
        Catch ex As Exception
        End Try
        Dim todo = _db.ComicsNeedingArcs(withCredits)
        If todo.Count = 0 AndAlso quiet Then Return
        Dim newArcs = todo.Any(Function(t) t.NeedsArcs) OrElse Not quiet
        _findingArcs = True
        _stopArcs = False
        Try
            For i = 0 To todo.Count - 1
                If _stopArcs OrElse IsDisposed Then Exit For
                _findArcs.Text = $"Looking up {i + 1:N0} of {todo.Count:N0} (click to stop)"
                Dim metronId = todo(i).MetronId
                Dim issue = Await WithRetry(Function() metron.IssueAsync(metronId), _arcStatus)
                _db.SaveArcs(todo(i).ComicId, If(issue?.Arcs, New List(Of MetronArc)), If(withCredits, If(issue?.Credits, New List(Of MetronCredit)), Nothing))
                If i < todo.Count - 1 Then Await Task.Delay(3200)
            Next
            If _stopArcs OrElse IsDisposed OrElse Not newArcs Then Return
            ' How long each arc is, for "have 3 of 4"
            Dim arcs = _db.OwnedArcs()
            Dim totals As New Dictionary(Of Long, Integer)
            For i = 0 To arcs.Count - 1
                If _stopArcs OrElse IsDisposed Then Exit For
                _findArcs.Text = $"Checking arc {i + 1:N0} of {arcs.Count:N0} (click to stop)"
                Dim arcId = arcs(i).ArcId
                totals(arcId) = Await WithRetry(Function() metron.ArcSizeAsync(arcId), _arcStatus)
                If i < arcs.Count - 1 Then Await Task.Delay(3200)
            Next
            If IsDisposed Then Return
            Dim made = _db.MakeArcSets(totals)
            RefreshSets()
            RefreshCollection()
            Dim notMatched = _db.GetStats().Comics - _db.ComicsWithMetronNumber()
            If Not quiet Then MessageBox.Show(Me, $"Made {made:N0} new set{If(made = 1, "", "s")} from story arcs. Give each one a value with Change name or value." &
                            If(notMatched > 0, vbCrLf & vbCrLf & $"{notMatched:N0} comics couldn't be checked because they aren't matched to Metron yet. " &
                               "Find covers and prices on the Collection page matches them; run Find story arcs again afterwards.", ""),
                            "Find story arcs")
        Catch ex As Exception
            If Not quiet AndAlso Not IsDisposed Then Ui.ShowError(Me, $"Finding story arcs stopped: {Ui.Friendly(ex)}")
        Finally
            _findingArcs = False
            If Not IsDisposed Then
                _findArcs.Text = "Find story arcs"
                _arcStatus.Text = ""
            End If
        End Try
    End Function

    ''' <summary>
    ''' Puts the issues missing from the selected story-arc sets on the wishlist, labelled with the arc.
    ''' Works for sets made by Find story arcs, which know their Metron arc.
    ''' </summary>
    Private Async Sub OnWishMissingFromSets(sender As Object, e As EventArgs)
        Dim rows = _setsGrid.SelectedRows.Cast(Of DataGridViewRow)().
            Select(Function(r) TryCast(r.DataBoundItem, DataRowView)).Where(Function(r) r IsNot Nothing).ToList()
        If rows.Count = 0 Then
            Ui.ShowError(Me, "Select the sets first. Hold Ctrl or Shift to pick several, or Ctrl+A for all of them.")
            Return
        End If
        Dim arcRows = rows.Where(Function(r) Not IsDBNull(r("arc_id"))).ToList()
        If arcRows.Count = 0 Then
            Ui.ShowError(Me, "Only sets made by Find story arcs know which issues the arc has. For a set you made yourself, " &
                         "add the missing issues on the Wishlist page.")
            Return
        End If
        Dim metron = Me.Metron
        If Not Await RelayReady(metron, "list every issue in an arc", 5) Then Return
        Dim added = 0
        UseWaitCursor = True
        Try
            For i = 0 To arcRows.Count - 1
                _arcStatus.Text = $"Checking {arcRows(i)("Set")} ({i + 1} of {arcRows.Count})…"
                Dim arcId = Convert.ToInt64(arcRows(i)("arc_id"), CultureInfo.InvariantCulture)
                Dim issues = Await WithRetry(Function() metron.ArcIssuesAsync(arcId), _arcStatus)
                added += _db.WishMissingFromArc(CStr(arcRows(i)("Set")), If(issues, New List(Of MetronIssue)))
                If i < arcRows.Count - 1 Then Await Task.Delay(3200)
            Next
            MessageBox.Show(Me, $"Added {added:N0} missing issue{If(added = 1, "", "s")} to your wishlist, under the arc's name." &
                            If(arcRows.Count < rows.Count, vbCrLf & vbCrLf & "Sets you made yourself were skipped, because the app doesn't know which issues they're missing.", ""),
                            "Wishlist")
        Catch ex As Exception
            If Not IsDisposed Then Ui.ShowError(Me, $"Couldn't finish: {Ui.Friendly(ex)}")
        Finally
            UseWaitCursor = False
            If Not IsDisposed Then _arcStatus.Text = ""
        End Try
    End Sub

    ''' <summary>Checks the relay is set up and new enough (version 4 unless said) for story arcs and new releases.</summary>
    Private Async Function RelayReady(metron As MetronClient, what As String, Optional minVersion As Integer = 4,
                                      Optional quiet As Boolean = False) As Task(Of Boolean)
        If Not metron.IsSetUp Then
            If Not quiet Then Ui.ShowError(Me, "Add your relay address on the Settings tab first.")
            Return False
        End If
        Try
            If Await metron.RelayVersionAsync() >= minVersion Then Return True
            If Not quiet Then MessageBox.Show(Me, $"Your relay needs a small update before the app can {what}. The steps are under " &
                            "'Updating the relay' in relay\README.md on GitHub. It takes a couple of minutes.",
                            "Update your relay", MessageBoxButtons.OK, MessageBoxIcon.Information)
        Catch ex As Exception
            If Not quiet Then Ui.ShowError(Me, $"Couldn't reach your relay: {Ui.Friendly(ex)}")
        End Try
        Return False
    End Function

    ''' <summary>Runs a lookup, waiting a minute and trying again (up to 3 times) when Metron says to slow down.</summary>
    Private Async Function WithRetry(Of TResult)(lookup As Func(Of Task(Of TResult)), status As Label) As Task(Of TResult)
        For attempt = 1 To 3
            Dim slowDown = False
            Try
                Return Await lookup()
            Catch ex As InvalidOperationException When ex.Message.StartsWith("Too many") AndAlso attempt < 3
                slowDown = True
            End Try
            If slowDown Then
                status.Text = "Metron asked us to slow down, waiting a minute…"
                Await Task.Delay(65000)
                status.Text = ""
            End If
        Next
        Return Nothing
    End Function

    Private Function SelectedSet() As DataRowView
        If _setsGrid.CurrentRow Is Nothing Then Return Nothing
        Return TryCast(_setsGrid.CurrentRow.DataBoundItem, DataRowView)
    End Function

    Private Sub OnMakeSet(sender As Object, e As EventArgs)
        Dim ids = SelectedComicIds()
        If ids.Count = 0 Then
            Ui.ShowError(Me, "Select the comics in the arc or run first. Hold Ctrl or Shift to pick several, " &
                         "or search for the series to narrow the list.")
            Return
        End If
        ' Offer the set they're already in, if they share one.
        Dim current = _grid.SelectedRows.Cast(Of DataGridViewRow)().
            Select(Function(r) Convert.ToString(r.Cells("Set").Value, CultureInfo.InvariantCulture)).Distinct().ToList()
        Dim suggested = If(current.Count = 1, current(0), "")
        Using f As New SetForm("Make a set", _db.SetNames(), ids.Count, suggested)
            If f.ShowDialog(Me) <> DialogResult.OK Then Return
            Try
                Dim undo = _db.Snapshot(ids)
                _db.SaveSet(f.SetName, ids, f.TotalValue)
                RememberUndo(undo, $"Made the set {f.SetName}")
            Catch ex As Exception
                Ui.ShowError(Me, $"Couldn't save the set: {Ui.Friendly(ex)}")
                Return
            End Try
        End Using
        RefreshCollection()
    End Sub

    Private Sub OnEditSet(sender As Object, e As EventArgs)
        Dim row = SelectedSet()
        If row Is Nothing Then Return
        Dim id = Convert.ToInt64(row("id"), CultureInfo.InvariantCulture)
        Dim value = If(IsDBNull(row("Value")), CType(Nothing, Double?), Convert.ToDouble(row("Value"), CultureInfo.InvariantCulture))
        Using f As New SetForm("Change set", _db.SetNames(), Convert.ToInt32(row("Comics"), CultureInfo.InvariantCulture), CStr(row("Set")), value)
            If f.ShowDialog(Me) <> DialogResult.OK Then Return
            Try
                _db.UpdateSet(id, f.SetName, f.TotalValue)
            Catch ex As Exception
                Ui.ShowError(Me, $"Couldn't save the set (is that name already used by another set?): {Ui.Friendly(ex)}")
                Return
            End Try
        End Using
        RefreshSets()
        RefreshCollection()
    End Sub

    Private Sub OnShowSet(sender As Object, e As EventArgs)
        Dim row = SelectedSet()
        If row Is Nothing Then Return
        ShowPage(0)
        ' In quotes, so the set's name is found as a whole.
        _search.Text = $"""{CStr(row("Set"))}"""
        RefreshCollection()
    End Sub

    Private Sub OnRemoveSet(sender As Object, e As EventArgs)
        Dim row = SelectedSet()
        If row Is Nothing Then Return
        If MessageBox.Show(Me, $"Remove the set ""{row("Set")}""? The comics stay in your collection with the values they have now.",
                           "Remove set", MessageBoxButtons.YesNo, MessageBoxIcon.Question) <> DialogResult.Yes Then Return
        _db.DeleteSet(Convert.ToInt64(row("id"), CultureInfo.InvariantCulture))
        RefreshSets()
        RefreshCollection()
    End Sub

    Private Function BuildWishlistTab() As Control
        Dim page As New Panel()
        Dim bar As New FlowLayoutPanel With {.Dock = DockStyle.Top, .AutoSize = True, .Padding = New Padding(4)}
        bar.Controls.AddRange({Ui.MakeButton("Add", AddressOf OnAddWish, Theme.PrimaryTag), Ui.MakeButton("Remove", AddressOf OnRemoveWish, Theme.DangerTag),
                               Ui.MakeButton("Most I'd pay…", AddressOf OnWishMax),
                               _dealsButton, Ui.MakeButton("Look for deals now", Async Sub(s, e) Await RunDealChecks(quiet:=False)),
                               Ui.MakeButton("Share page…", Sub(s, e) OnShare(wishlistOnly:=True))})
        Dim hint = Ui.MakeLabel("Priority 1 = must have, 3 = nice to have. Comics come off the list when you add them to your collection. " &
                                "With a most-you'd-pay, the app looks on eBay once a day for one at or under that price.")
        hint.Dock = DockStyle.Top
        hint.Padding = New Padding(4, 0, 4, 8)
        hint.Tag = Theme.MutedTag
        page.Controls.Add(_wishGrid)
        page.Controls.Add(hint)
        page.Controls.Add(bar)
        Return page
    End Function

    Private Sub RefreshWishlist()
        _wishGrid.DataSource = _db.GetWishlist()
        If _wishGrid.Columns.Contains("id") Then _wishGrid.Columns("id").Visible = False
        For Each colName In {"Max price", "eBay deal (US$)"}
            If _wishGrid.Columns.Contains(colName) Then _wishGrid.Columns(colName).DefaultCellStyle.Format = "N2"
        Next
        UpdateDealsButton()
    End Sub

    Private Sub OnAddWish(sender As Object, e As EventArgs)
        Using f As New WishForm()
            If f.ShowDialog(Me) = DialogResult.OK Then
                _db.AddWish(f.SeriesName, f.IssueNumber, f.Priority, f.MaxPrice, f.Notes)
                RefreshWishlist()
            End If
        End Using
    End Sub

    Private Sub OnRemoveWish(sender As Object, e As EventArgs)
        Dim ids = _wishGrid.SelectedRows.Cast(Of DataGridViewRow)().
            Select(Function(r) Convert.ToInt64(r.Cells("id").Value, CultureInfo.InvariantCulture)).ToList()
        If ids.Count = 0 Then Return
        _db.RemoveWishes(ids)
        RefreshWishlist()
    End Sub

    ' ---------- New releases ----------

    Private Function BuildReleasesTab() As Control
        Dim page As New Panel()
        Dim bar As New FlowLayoutPanel With {.Dock = DockStyle.Top, .AutoSize = True, .Padding = New Padding(4)}
        bar.Controls.AddRange({_checkReleases,
                               Ui.MakeButton("Series to follow…", AddressOf OnChooseFollowed),
                               Ui.MakeButton("Add to wishlist", AddressOf OnWishReleases),
                               Ui.MakeButton("Pull list for the shop…", Sub(s, e)
                                                                            Using f As New PullListForm(_db, _settings)
                                                                                f.ShowDialog(Me)
                                                                            End Using
                                                                        End Sub),
                               _releaseStatus})
        Dim hint = Ui.MakeLabel("New issues of the series you follow: out in the last two weeks or coming soon. Dates are US shop dates; " &
                                "NZ shops usually get them the same week.")
        hint.Dock = DockStyle.Top
        hint.Padding = New Padding(4, 0, 4, 8)
        hint.Tag = Theme.MutedTag
        page.Controls.Add(_releasesGrid)
        page.Controls.Add(hint)
        page.Controls.Add(bar)
        Return page
    End Function

    Private Sub RefreshReleases()
        _releasesGrid.DataSource = _db.GetReleases()
        For Each colName In {"metron_id", "cover_url"}
            If _releasesGrid.Columns.Contains(colName) Then _releasesGrid.Columns(colName).Visible = False
        Next
        If _checkingReleases Then Return
        _releaseStatus.Text = If(_settings.LastReleaseCheck.HasValue,
            $"Last checked {_settings.LastReleaseCheck.Value.ToString("d MMM 'at' h:mm tt", CultureInfo.CurrentCulture)}",
            "Not checked yet")
    End Sub

    Private Sub OnChooseFollowed(sender As Object, e As EventArgs)
        Using f As New FollowForm(_db.SeriesToFollow())
            If f.ShowDialog(Me) <> DialogResult.OK Then Return
            For Each c In f.Changes
                _db.SetFollow({c.Id}, c.Follow)
            Next
        End Using
    End Sub

    ''' <summary>Asks Metron about each followed series, a few seconds apart. Click again to stop.</summary>
    Private Async Sub OnCheckReleases(sender As Object, e As EventArgs)
        If _checkingReleases Then
            _stopReleases = True
            _checkReleases.Text = "Stopping…"
            Return
        End If
        Await RunCheckReleases(quiet:=False)
    End Sub

    Private Async Function RunCheckReleases(quiet As Boolean) As Task
        If _checkingReleases Then Return
        Dim metron = Me.Metron
        If Not Await RelayReady(metron, "check for new releases", quiet:=quiet) Then Return
        Dim names = _db.FollowedSeriesNames()
        If names.Count = 0 Then
            If Not quiet Then Ui.ShowError(Me, "You're not following any series yet. Click Series to follow… and tick the ones you're collecting.")
            Return
        End If
        _checkingReleases = True
        _stopReleases = False
        Try
            Dim found = Await ReleaseFinder.FindAsync(metron, names,
                Sub(done)
                    If IsDisposed Then Return
                    _checkReleases.Text = If(done < names.Count, $"Checking {done + 1:N0} of {names.Count:N0} (click to stop)", "Saving…")
                    _releaseStatus.Text = If(done < names.Count, names(done), "")
                End Sub,
                Function() _stopReleases OrElse IsDisposed)
            If IsDisposed Then Return
            _db.SaveReleases(found, replaceAll:=Not _stopReleases)
            If Not _stopReleases Then
                _settings.LastReleaseCheck = DateTime.Now
                _settings.Save()
            End If
        Catch ex As Exception
            If Not quiet AndAlso Not IsDisposed Then Ui.ShowError(Me, $"Checking for new releases stopped: {Ui.Friendly(ex)}")
        Finally
            _checkingReleases = False
            If Not IsDisposed Then
                _checkReleases.Text = "Check for new releases"
                RefreshReleases()
            End If
        End Try
    End Function

    Private Sub OnWishReleases(sender As Object, e As EventArgs)
        Dim rows = _releasesGrid.SelectedRows.Cast(Of DataGridViewRow)().
            Select(Function(r) TryCast(r.DataBoundItem, DataRowView)).Where(Function(r) r IsNot Nothing).ToList()
        If rows.Count = 0 Then
            Ui.ShowError(Me, "Select the issues you want first. Hold Ctrl or Shift to pick several.")
            Return
        End If
        Dim added = 0
        For Each r In rows
            If Convert.ToString(r("Status"), CultureInfo.InvariantCulture) <> "" Then Continue For
            _db.AddWish(CStr(r("Series")), CStr(r("Issue")), 2, Nothing, $"In shops {r("In shops")}")
            added += 1
        Next
        RefreshReleases()
        MessageBox.Show(Me, $"Added {added:N0} to your wishlist." & If(added < rows.Count, " The others you already have or want.", ""), "Wishlist")
    End Sub

    ' ---------- Settings ----------

    Private Function BuildSettingsTab() As Control
        Dim page As New Panel()
        Dim panel As New FlowLayoutPanel With {.Dock = DockStyle.Fill, .FlowDirection = FlowDirection.TopDown, .WrapContents = False,
                                               .Padding = New Padding(12), .AutoScroll = True}
        Dim heading = Function(t As String) New Label With {.Text = t, .AutoSize = True, .Font = New Font(Font.FontFamily, 11.0F, FontStyle.Bold),
                                                             .Margin = New Padding(3, 14, 3, 4)}
        Dim note = Function(t As String) New Label With {.Text = t, .AutoSize = True, .MaximumSize = New Size(640, 0), .ForeColor = SystemColors.GrayText}

        Dim relayRow As New FlowLayoutPanel With {.AutoSize = True, .WrapContents = False}
        relayRow.Controls.AddRange({_relayUrl, Ui.MakeButton("Save and test", AddressOf OnSaveRelay, Theme.PrimaryTag)})

        Dim syncRow As New FlowLayoutPanel With {.AutoSize = True, .WrapContents = False}
        syncRow.Controls.AddRange({_syncCode, Ui.MakeButton("Save", AddressOf OnSaveSyncCode, Theme.PrimaryTag)})

        Dim bannerRow As New FlowLayoutPanel With {.AutoSize = True, .WrapContents = False}
        bannerRow.Controls.AddRange({Ui.MakeButton("Choose banner picture…", AddressOf OnChooseBanner),
                                     Ui.MakeButton("Remove banner", AddressOf OnRemoveBanner)})

        Dim backupRow As New FlowLayoutPanel With {.AutoSize = True, .WrapContents = False}
        backupRow.Controls.AddRange({Ui.MakeButton("Back up now", Sub(s, e) BackupNow(quiet:=False), Theme.PrimaryTag),
                                     Ui.MakeButton("Change folder…", AddressOf OnChooseBackupFolder),
                                     Ui.MakeButton("Open backup folder", AddressOf OnOpenBackupFolder),
                                     Ui.MakeButton("Restore a backup…", AddressOf OnRestoreBackup, Theme.DangerTag)})

        Dim ebayRow As New FlowLayoutPanel With {.AutoSize = True, .WrapContents = False}
        ebayRow.Controls.AddRange({_ebayButton, Ui.MakeButton("Use eBay estimates as values…", AddressOf OnUseEbay)})
        Dim rateRow As New FlowLayoutPanel With {.AutoSize = True, .WrapContents = False}
        rateRow.Controls.AddRange({Ui.MakeLabel("1 US dollar ="), _localPerUsd, Ui.MakeLabel("of your dollars (1 if your prices are in US$, about 1.70 for NZ$)")})
        _localPerUsd.Value = CDec(Math.Max(0.1, Math.Min(1000, _settings.LocalPerUsd)))
        AddHandler _localPerUsd.ValueChanged, Sub(s, e)
                                                   _settings.LocalPerUsd = CDbl(_localPerUsd.Value)
                                                   _settings.Save()
                                               End Sub

        Dim updateRow As New FlowLayoutPanel With {.AutoSize = True, .WrapContents = False}
        updateRow.Controls.AddRange({Ui.MakeButton("Check for updates", Sub(s, e) CheckForUpdate(quiet:=False), Theme.PrimaryTag), _updateStatus})

        Dim openFolder = Ui.MakeButton("Open the folder", Sub(s, e) Process.Start(New ProcessStartInfo With {
                                                                 .FileName = Path.GetDirectoryName(_settings.DatabasePath), .UseShellExecute = True}))

        ' Text size: takes effect the next time the app opens.
        Dim textSize As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = 160}
        textSize.Items.AddRange({"Normal", "Large", "Larger"})
        Dim sizes = {1.0F, 1.15F, 1.3F}
        textSize.SelectedIndex = Math.Max(0, Array.IndexOf(sizes, _settings.TextScale))
        AddHandler textSize.SelectedIndexChanged, Sub(s, e)
                                                      _settings.TextScale = sizes(textSize.SelectedIndex)
                                                      SaveSettingsQuietly()
                                                      If MessageBox.Show(Me, "The new text size shows when the app opens again. Restart it now?", "Text size",
                                                                         MessageBoxButtons.YesNo, MessageBoxIcon.Question) = DialogResult.Yes Then
                                                          Application.Restart()
                                                      End If
                                                  End Sub

        ' Four groups, with links at the top that jump to each.
        Dim section = Function(t As String) New Label With {.Text = t, .AutoSize = True, .Font = New Font(Font.FontFamily, 15.0F, FontStyle.Bold),
                                                             .ForeColor = Theme.Accent, .Margin = New Padding(3, 26, 3, 2)}
        Dim backups = section("Your collection and backups")
        Dim phone = section("Your phone")
        Dim lookups = section("Looking things up")
        Dim looks = section("How the app looks")
        Dim jump As New FlowLayoutPanel With {.AutoSize = True, .WrapContents = True, .MaximumSize = New Size(760, 0), .Margin = New Padding(0, 0, 0, 4)}
        For Each target In {backups, phone, lookups, looks}
            Dim t = target
            Dim link As New LinkLabel With {.Text = t.Text, .AutoSize = True, .Margin = New Padding(3, 6, 14, 3), .LinkColor = Theme.Accent,
                                            .ActiveLinkColor = Theme.Accent, .Font = New Font(Font.FontFamily, 10.5F)}
            AddHandler link.LinkClicked, Sub(s, e) panel.ScrollControlIntoView(t)
            jump.Controls.Add(link)
        Next
        backups.Margin = New Padding(3, 8, 3, 2)

        panel.Controls.AddRange({
            jump,
            backups,
            heading("Getting started"),
            note("The three steps for a new collection: comic lookup, your phone, and bringing in your comics."),
            Ui.MakeButton("Show the getting-started steps…", Sub(s, e) ShowWelcome()),
            heading("Backup"),
            note("Once a day the app saves a copy of your collection. With OneDrive on this computer the copies go there, so they're safe " &
                 "even if this computer dies. The last 30 days are kept."),
            _backupStatus,
            backupRow,
            heading("Deleted comics"),
            note($"Comics you delete wait in the bin for {ComicDb.BinDays} days, with their photos, box and loans, in case you want them back."),
            Ui.MakeButton("Deleted comics (bin)…", AddressOf OnShowBin),
            heading("Bring in your collection from the phone app"),
            note("On your phone: Settings, then Export spreadsheet (CSV). Save it to iCloud or OneDrive, then pick that file here. Comics already here are skipped, so you can import again later."),
            Ui.MakeButton("Import phone app spreadsheet (CSV)…", AddressOf OnImportCsv),
            heading("Comics from a shop receipt"),
            note("Shops on ComicHub (like Heroes for Sale) email a receipt PDF with each order. Pick it here, or use Add from a shop receipt in the Collection page's More menu, " &
                 "to add those comics with what you paid and the order date. Covers and details are found on Metron."),
            Ui.MakeButton("Add from a shop receipt (PDF)…", AddressOf OnAddFromReceipt),
            heading("Try it out"),
            note("Fills an empty collection with a few sample comics so you can see how everything works. Delete them when you're done."),
            Ui.MakeButton("Load sample comics", AddressOf OnLoadSamples),
            heading("Database file"),
            _dbPath,
            note("This is a normal SQLite file. You can open it in DB Browser for SQLite to run your own queries."),
            openFolder,
            phone,
            heading("Scans from your phone"),
            note("On your phone, open Settings, find ""Send scans to your computer"" and tap Turn on. Type the code it shows here. " &
                 "While this app is open it collects new scans every couple of minutes, once the phone has looked up their details."),
            syncRow, _syncStatus,
            note("With a code saved, the phone also gets a copy of your collection and wishlist, so scanning in a shop tells you if you already own a comic or want it. " &
                 "A box, condition or read changed on either one reaches the other."),
            lookups,
            heading("Comic lookup"),
            note("Barcode lookups use the same relay address as the phone app (Settings on your phone shows it). Your Metron password stays in Cloudflare."),
            relayRow, _relayStatus,
            heading("Do things by themselves"),
            note("While the app is open it finds covers and prices for new comics, sorts them into story arcs, checks for new releases once a week, " &
                 "and (once your relay has eBay keys) keeps eBay value estimates up to date. " &
                 "It runs slowly in the background because Metron allows about one lookup every few seconds."),
            _autoJobs, _autoStatus,
            heading("Values from eBay"),
            note("Estimates what each comic is worth from what sellers are asking on eBay right now (US dollars). It's free, but your relay needs " &
                 "your own eBay developer keys first: see ""Values from eBay"" in relay\README.md on GitHub. Estimates show in the eBay est. column; " &
                 "nothing changes your values until you click Use eBay estimates as values."),
            ebayRow, _ebayStatus,
            note("Wishlist deals compare eBay's US$ prices with the most you'd pay in your own dollars, using this rate:"),
            rateRow,
            heading("Updates"),
            note("The app checks for a newer version each time it opens and asks before updating."),
            updateRow,
            looks,
            heading("Text size"),
            note("Makes the writing, buttons and lists bigger, easier to read on a big screen."),
            textSize,
            heading("Banner picture"),
            note("Pick any picture from your computer to show across the top of the app. A copy is kept in your Comic Catalog folder, so it stays on this computer."),
            bannerRow,
            heading("Digital comics"),
            note("CBZ, CBR, PDF and EPUB comics you add on the Digital page are copied into this folder, so they're always here to read. " &
                 "They're not in the daily backup (they're big); pick a OneDrive folder if you'd like them kept safe off this computer. " &
                 "Changing the folder only affects comics you add after that."),
            _digitalFolderLabel,
            Ui.MakeButton("Change folder…", AddressOf OnChooseDigitalFolder),
            heading("Keyboard shortcuts"),
            note("Ctrl+F search · Ctrl+N add a comic · Ctrl+E edit · Ctrl+B put in box · Ctrl+R read / unread · Delete delete · Ctrl+Z undo · F5 refresh · " &
                 "Ctrl+1 to Ctrl+9 go to Home, Collection, Trades, Boxes, Digital, Videos, Stats, Missing issues and Wishlist · Esc clears the search")})
        page.Controls.Add(panel)
        Return page
    End Function

    Private Async Sub OnSaveRelay(sender As Object, e As EventArgs)
        Dim url = _relayUrl.Text.Trim()
        If url <> "" AndAlso Not url.StartsWith("http", StringComparison.OrdinalIgnoreCase) Then url = "https://" & url
        _relayUrl.Text = url
        _settings.RelayUrl = url
        _settings.Save()
        If url = "" Then
            StartScanChecks()
            _relayStatus.Text = "Lookup turned off."
            Return
        End If
        _relayStatus.Text = "Testing…"
        StartScanChecks()
        Try
            Await New MetronClient(url).TestAsync()
            _relayStatus.Text = "Connected to Metron. Barcode lookups will now fill in comic details."
        Catch ex As Exception
            _relayStatus.Text = $"Saved, but the test failed: {Ui.Friendly(ex)}"
        End Try
    End Sub

    ''' <summary>Adds the comics on a shop receipt PDF (ComicHub shops such as Heroes for Sale email these).</summary>
    Private Sub OnAddFromReceipt(sender As Object, e As EventArgs)
        Using dlg As New OpenFileDialog With {.Filter = "Receipt (*.pdf)|*.pdf|All files (*.*)|*.*", .Title = "Pick the receipt or invoice PDF from the shop"}
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            Dim receipt As ShopReceipt
            Try
                receipt = ShopReceipts.Parse(ShopReceipts.PdfText(File.ReadAllBytes(dlg.FileName)))
            Catch ex As Exception
                Ui.ShowError(Me, $"Couldn't read that receipt: {Ui.Friendly(ex)}")
                Return
            End Try
            Using f As New ReceiptForm(_db, Metron, receipt)
                If f.ShowDialog(Me) <> DialogResult.OK OrElse f.AddedIds.Count = 0 Then Return
                RefreshCollection(f.AddedIds.Last())
                SendLibraryIfChanged()
                MessageBox.Show(Me, $"Added {f.AddedIds.Count:N0} comics from {receipt.Shop}.", "Shop receipt")
            End Using
        End Using
    End Sub

    Private Sub OnImportCsv(sender As Object, e As EventArgs)
        Using dlg As New OpenFileDialog With {.Filter = "Spreadsheet (*.csv)|*.csv|All files (*.*)|*.*", .Title = "Pick the spreadsheet exported from the phone app"}
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            Try
                UseWaitCursor = True
                Dim result = _db.ImportPhoneCsv(File.ReadAllText(dlg.FileName))
                RefreshCollection()
                MessageBox.Show(Me, $"Imported {result.Added:N0} comics." & If(result.Moved > 0, $" {result.Moved:N0} you already had were put in their box.", "") &
                                If(result.Skipped > 0, $" {result.Skipped:N0} were already here and were skipped.", ""), "Import")
            Catch ex As Exception
                Ui.ShowError(Me, $"Couldn't import that file: {Ui.Friendly(ex)}")
            Finally
                UseWaitCursor = False
            End Try
        End Using
    End Sub

    ' ---------- Banner ----------

    Private Sub ShowBanner()
        _bannerImage?.Dispose()
        _bannerImage = Nothing
        If _settings.BannerPath <> "" AndAlso File.Exists(_settings.BannerPath) Then
            Try
                ' Read into memory so the file isn't kept locked.
                Using ms As New MemoryStream(File.ReadAllBytes(_settings.BannerPath)), img = Image.FromStream(ms)
                    _bannerImage = New Bitmap(img)
                End Using
            Catch ex As Exception When TypeOf ex Is ArgumentException OrElse TypeOf ex Is IOException OrElse TypeOf ex Is OutOfMemoryException
                _bannerImage = Nothing
            End Try
        End If
        _banner.Visible = _bannerImage IsNot Nothing
        _banner.Invalidate()
    End Sub

    ''' <summary>Fills the banner with the picture, cropping the edges rather than squashing it.</summary>
    Private Sub PaintBanner(sender As Object, e As PaintEventArgs)
        If _bannerImage Is Nothing Then Return
        Dim box = _banner.ClientRectangle
        Dim scale = Math.Max(box.Width / CDbl(_bannerImage.Width), box.Height / CDbl(_bannerImage.Height))
        Dim w = CInt(_bannerImage.Width * scale), h = CInt(_bannerImage.Height * scale)
        e.Graphics.InterpolationMode = Drawing2D.InterpolationMode.HighQualityBicubic
        e.Graphics.DrawImage(_bannerImage, New Rectangle((box.Width - w) \ 2, (box.Height - h) \ 2, w, h))
    End Sub

    Private Sub OnChooseBanner(sender As Object, e As EventArgs)
        Using dlg As New OpenFileDialog With {.Filter = "Pictures (*.jpg;*.jpeg;*.png;*.bmp;*.gif)|*.jpg;*.jpeg;*.png;*.bmp;*.gif",
                                              .Title = "Pick a picture for the top of the app"}
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            Try
                Using test = Image.FromFile(dlg.FileName)
                End Using
                Directory.CreateDirectory(AppSettings.Folder)
                Dim target = Path.Combine(AppSettings.Folder, "banner" & Path.GetExtension(dlg.FileName).ToLowerInvariant())
                If _settings.BannerPath <> "" AndAlso _settings.BannerPath <> target AndAlso File.Exists(_settings.BannerPath) Then File.Delete(_settings.BannerPath)
                File.Copy(dlg.FileName, target, overwrite:=True)
                _settings.BannerPath = target
                _settings.Save()
                ShowBanner()
            Catch ex As Exception When TypeOf ex Is OutOfMemoryException OrElse TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
                Ui.ShowError(Me, "Couldn't use that picture. Try a JPG or PNG file.")
            End Try
        End Using
    End Sub

    Private Sub OnRemoveBanner(sender As Object, e As EventArgs)
        _settings.BannerPath = ""
        _settings.Save()
        ShowBanner()
    End Sub

    ''' <summary>The three getting-started steps, then whichever one was picked.</summary>
    Private Sub ShowWelcome()
        If Not _settings.WelcomeShown Then
            _settings.WelcomeShown = True
            SaveSettingsQuietly()
        End If
        Dim choice As String
        Using f As New WelcomeForm(Metron.IsSetUp, _settings.SyncCode <> "")
            If f.ShowDialog(Me) <> DialogResult.OK Then Return
            choice = f.Choice
        End Using
        Select Case choice
            Case "relay"
                ShowPage("Settings")
                _relayUrl.Focus()
            Case "phone"
                ShowPage("Settings")
                _syncCode.Focus()
            Case "import" : OnImportCsv(Me, EventArgs.Empty)
            Case "samples" : OnLoadSamples(Me, EventArgs.Empty)
            Case "add"
                ShowPage("Collection")
                OnAddByHand(Me, EventArgs.Empty)
        End Select
    End Sub

    Private Sub OnLoadSamples(sender As Object, e As EventArgs)
        If _db.LoadSampleData() Then
            RefreshCollection()
            MessageBox.Show(Me, "Added a few sample comics. Have a look on the Collection tab.", "Sample comics")
        Else
            Ui.ShowError(Me, "Sample comics can only go into an empty collection.")
        End If
    End Sub

End Class
