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
    Private ReadOnly _search As New TextBox With {.Width = 220, .PlaceholderText = "series, title, publisher…"}
    Private ReadOnly _grid As DataGridView = Ui.MakeGrid()
    Private ReadOnly _covers As New ListView With {.Dock = DockStyle.Fill, .View = View.LargeIcon, .VirtualMode = True,
                                                   .MultiSelect = True, .Visible = False}
    Private ReadOnly _coverImages As New ImageList With {.ColorDepth = ColorDepth.Depth32Bit, .ImageSize = CoverCache.ThumbSize}
    Private ReadOnly _coverIndex As New Dictionary(Of Long, Integer)
    Private ReadOnly _coverLoading As New HashSet(Of Long)
    Private ReadOnly _coverCache As New CoverCache()
    Private ReadOnly _gridThumbs As New Dictionary(Of Long, Image)
    Private ReadOnly _gridPlaceholder As Image = SmallThumb(CoverCache.Placeholder())
    Private ReadOnly _thumbColumn As New DataGridViewImageColumn With {.Name = "CoverThumb", .HeaderText = "", .Width = 52,
        .ImageLayout = DataGridViewImageCellLayout.Zoom, .Resizable = DataGridViewTriState.False,
        .SortMode = DataGridViewColumnSortMode.NotSortable, .AutoSizeMode = DataGridViewAutoSizeColumnMode.None}
    Private _rows As DataTable
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
    Private ReadOnly _statCopies As New Label()
    Private ReadOnly _statValue As New Label()
    Private ReadOnly _statPaid As New Label()
    Private ReadOnly _statRead As New Label()
    Private ReadOnly _readFilter As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = 130}
    ' "More" menu on the Collection page (also on right-click)
    Private ReadOnly _moreMenu As New ContextMenuStrip()
    Private ReadOnly _ebayItem As New ToolStripMenuItem("Estimate values from eBay")
    Private ReadOnly _ebayButton As Button = Ui.MakeButton("Estimate values from eBay", AddressOf OnEstimateEbay, Theme.PrimaryTag)
    Private ReadOnly _ebayStatus As New Label With {.AutoSize = True, .MaximumSize = New Size(640, 0), .ForeColor = SystemColors.GrayText}
    Private _findingEbay As Boolean
    Private _stopEbay As Boolean

    ' Pages and the buttons that switch between them
    Private ReadOnly _pages As New List(Of Control)
    Private ReadOnly _navButtons As New List(Of Button)
    Private ReadOnly _searchTimer As New Timer With {.Interval = 250}

    ' Missing issues tab
    Private ReadOnly _gapsGrid As DataGridView = Ui.MakeGrid()
    Private ReadOnly _missingList As New ListBox With {.Dock = DockStyle.Fill, .SelectionMode = SelectionMode.MultiExtended, .IntegralHeight = False}
    Private ReadOnly _missingTitle As New Label With {.Dock = DockStyle.Top, .Height = 28, .TextAlign = ContentAlignment.MiddleLeft}

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
    Private ReadOnly _budget As New NumericUpDown With {.Maximum = 100000, .Width = 90, .DecimalPlaces = 0, .Increment = 10}
    Private ReadOnly _readGoal As New NumericUpDown With {.Maximum = 1000, .Width = 70}
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
        Icon = AppIcon.Load()
        AutoScaleMode = AutoScaleMode.Font
        Font = New Font("Segoe UI", 9.5F)
        ClientSize = New Size(1100, 700)
        MinimumSize = New Size(800, 500)
        StartPosition = FormStartPosition.CenterScreen

        Dim content As New Panel With {.Dock = DockStyle.Fill, .Padding = New Padding(16, 12, 16, 12)}
        _pages.AddRange({BuildCollectionTab(), BuildStatsTab(), BuildMissingTab(), BuildWishlistTab(), BuildSetsTab(), BuildReleasesTab(),
                         BuildLoansTab(), BuildSettingsTab()})
        For Each p In _pages
            p.Dock = DockStyle.Fill
            p.Visible = False
            content.Controls.Add(p)
        Next

        ' Header: app name and the page buttons
        Dim header As New Panel With {.Dock = DockStyle.Top, .Height = 58, .BackColor = Theme.Panel, .Padding = New Padding(16, 0, 16, 0)}
        Dim title As New Label With {.Text = "COMIC CATALOG", .AutoSize = True, .Dock = DockStyle.Left, .ForeColor = Theme.Accent,
                                     .Font = New Font("Segoe UI Black", 15.0F), .TextAlign = ContentAlignment.MiddleLeft,
                                     .Padding = New Padding(0, 14, 24, 0)}
        Dim nav As New FlowLayoutPanel With {.Dock = DockStyle.Fill, .WrapContents = False, .Padding = New Padding(0, 10, 0, 0)}
        For i = 0 To PageNames.Length - 1
            Dim index = i
            Dim b As New Button With {.Text = PageNames(i), .AutoSize = True, .FlatStyle = FlatStyle.Flat, .Height = 38,
                                      .Font = New Font("Segoe UI Semibold", 10.5F), .Padding = New Padding(10, 0, 10, 0),
                                      .Cursor = Cursors.Hand, .Margin = New Padding(0, 0, 4, 0)}
            b.FlatAppearance.BorderSize = 0
            b.FlatAppearance.MouseOverBackColor = Theme.Panel2
            AddHandler b.Click, Sub(s, e) ShowPage(index)
            _navButtons.Add(b)
            nav.Controls.Add(b)
        Next
        header.Controls.Add(nav)
        header.Controls.Add(title)

        AddHandler _banner.Paint, AddressOf PaintBanner
        AddHandler _banner.Resize, Sub(s, e) _banner.Invalidate()
        Controls.Add(content)
        Controls.Add(_banner)
        Controls.Add(header)
        Theme.Apply(Me)
        _grid.RowTemplate.Height = 66
        header.BackColor = Theme.Panel
        nav.BackColor = Theme.Panel
        For Each b In _navButtons
            b.BackColor = Theme.Panel
        Next
        ShowPage(0)
        SetView(False)
    End Sub

    Protected Overrides Sub OnHandleCreated(e As EventArgs)
        MyBase.OnHandleCreated(e)
        Theme.DarkTitleBar(Me)
    End Sub

    ''' <summary>The pages, in the order of the buttons across the top.</summary>
    Private Shared ReadOnly PageNames As String() = {"Collection", "Stats", "Missing issues", "Wishlist", "Sets", "New releases", "Lent out", "Settings"}

    Private Sub ShowPage(name As String)
        ShowPage(Array.IndexOf(PageNames, name))
    End Sub

    Private Sub ShowPage(index As Integer)
        For i = 0 To _pages.Count - 1
            _pages(i).Visible = (i = index)
            _navButtons(i).ForeColor = If(i = index, Theme.Accent, Theme.Muted)
            _navButtons(i).FlatAppearance.BorderSize = 0
        Next
        _pages(index).BringToFront()
        If _db Is Nothing Then Return
        Select Case PageNames(index)
            Case "Collection" : _barcode.Focus()
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
            Ui.ShowError(Me, $"Couldn't open the database at {_settings.DatabasePath}: {ex.Message}")
            Close()
            Return
        End Try
        _relayUrl.Text = _settings.RelayUrl
        _syncCode.Text = _settings.SyncCode
        ShowBanner()
        _dbPath.Text = $"Your collection is saved in {_settings.DatabasePath}"
        RefreshCollection()
        ShowEbayStatus()
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
        If _findingCovers OrElse _findingArcs OrElse _checkingReleases OrElse _findingEbay Then Return
        _autoRunning = True
        Try
            If _db.ComicsNeedingCovers().Count > 0 Then Await RunFindCovers(quiet:=True)
            If IsDisposed OrElse Not _settings.AutoJobs Then Return
            If _db.ComicsNeedingArcs(withCredits:=True).Count > 0 Then Await RunFindArcs(quiet:=True)
            If IsDisposed OrElse Not _settings.AutoJobs Then Return
            If Not _settings.LastReleaseCheck.HasValue OrElse DateTime.Now - _settings.LastReleaseCheck.Value > TimeSpan.FromDays(7) Then
                Await RunCheckReleases(quiet:=True)
            End If
            If IsDisposed OrElse Not _settings.AutoJobs Then Return
            ' eBay allows 5,000 searches a day, so a few hundred at a time is plenty.
            If _db.ComicsNeedingEbay().Count > 0 Then Await RunEbayEstimates(quiet:=True, limit:=300)
            If Not IsDisposed Then _autoStatus.Text = $"Last ran at {DateTime.Now:t}"
        Catch ex As Exception
            If Not IsDisposed Then _autoStatus.Text = $"Stopped at {DateTime.Now:t}: {ex.Message}"
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
            If Not quiet Then Ui.ShowError(Me, $"Couldn't update: {ex.Message}")
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
            _backupStatus.Text = $"The last backup didn't work: {ex.Message}"
            If Not quiet Then Ui.ShowError(Me, $"Couldn't back up: {ex.Message}")
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
                Ui.ShowError(Me, $"Couldn't restore that backup: {ex.Message}")
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
                _scanNote.Text = $"{added:N0} added from your phone at {DateTime.Now:t}"
            ElseIf Not quiet Then
                _scanNote.Text = $"No new scans ({DateTime.Now:t})"
            End If
            If Not quiet AndAlso added = 0 Then
                MessageBox.Show(Me, "No new scans waiting. Comics are sent once the phone has looked up their details.", "Scans from phone")
            End If
        Catch ex As Exception
            If IsDisposed Then Return
            _scanNote.Text = "Couldn't check for scans"
            If Not quiet Then Ui.ShowError(Me, ex.Message)
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
            added += _db.ImportPhoneCsv(batch.Csv).Added
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
            _syncStatus.Text = $"Saved, but checking failed: {ex.Message}"
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
        Dim bar As New FlowLayoutPanel With {.Dock = DockStyle.Top, .AutoSize = True, .Padding = New Padding(0, 0, 0, 8)}
        bar.Controls.AddRange({
            Ui.MakeLabel("Barcode"), _barcode, Ui.MakeButton("Look up and add", AddressOf OnBarcodeEntered, Theme.PrimaryTag),
            New Label With {.Width = 16},
            Ui.MakeLabel("Search"), _search, _readFilter,
            New Label With {.Width = 16},
            _listButton, _coversButton, _findCovers,
            New Label With {.Width = 16},
            Ui.MakeButton("Add by hand", AddressOf OnAddByHand),
            Ui.MakeButton("Edit", AddressOf OnEdit),
            Ui.MakeButton("Delete", AddressOf OnDelete, Theme.DangerTag),
            Ui.MakeButton("Make a set", AddressOf OnMakeSet),
            Ui.MakeButton("Read / unread", AddressOf OnToggleRead),
            Ui.MakeButton("What should I read next?", AddressOf OnReadNext),
            Ui.MakeButton("Lend…", AddressOf OnLend),
            Ui.MakeButton("Export list…", AddressOf OnExport),
            Ui.MakeButton("Share page…", Sub(s, e) OnShare(wishlistOnly:=False)),
            Ui.MakeButton("More ▾", Sub(s, e)
                                        Dim b = DirectCast(s, Control)
                                        _moreMenu.Show(b, New Point(0, b.Height))
                                    End Sub),
            New Label With {.Width = 16},
            _getScans, _scanNote})
        AddHandler _scanTimer.Tick, Sub(s, e)
                                        CollectScans(quiet:=True)
                                        SendLibraryIfChanged()
                                    End Sub
        _readFilter.Items.AddRange({"All comics", "Read", "Not read", "Key issues", "For trade", "Lent out"})
        _moreMenu.Items.Add("Put in a box or on a shelf…", Nothing, AddressOf OnSetBox)
        _moreMenu.Items.Add("For trade or sale…", Nothing, AddressOf OnForTrade)
        _moreMenu.Items.Add("Not for trade", Nothing, AddressOf OnNotForTrade)
        _moreMenu.Items.Add("Mark as key issue…", Nothing, AddressOf OnKeyIssue)
        _moreMenu.Items.Add(New ToolStripSeparator())
        _moreMenu.Items.Add("Insurance report…", Nothing, AddressOf OnInsurance)
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

        ' Totals as big number cards
        Dim cards As New FlowLayoutPanel With {.Dock = DockStyle.Top, .AutoSize = True, .Padding = New Padding(0, 0, 0, 10)}
        cards.Controls.AddRange({StatCard(_statComics, "COMICS"), StatCard(_statCopies, "COPIES"),
                                 StatCard(_statValue, "COLLECTION VALUE"), StatCard(_statPaid, "TOTAL PAID"),
                                 StatCard(_statRead, "READ")})

        _covers.LargeImageList = _coverImages
        _coverImages.Images.Add(CoverCache.Placeholder())
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
        AddHandler _grid.CellDoubleClick, Sub(s, e)
                                              If e.RowIndex >= 0 Then OnEdit(s, e)
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
        page.Controls.Add(cards)
        page.Controls.Add(bar)
        Return page
    End Function

    Private Function StatCard(value As Label, caption As String) As Control
        Dim card As New Panel With {.Size = New Size(210, 72), .BackColor = Theme.Panel, .Margin = New Padding(0, 0, 12, 0),
                                    .Padding = New Padding(14, 8, 8, 8)}
        value.Text = "0"
        value.AutoSize = False
        value.Dock = DockStyle.Fill
        value.Font = New Font("Segoe UI Semibold", 18.0F)
        value.ForeColor = Theme.Text
        value.BackColor = Theme.Panel
        Dim cap As New Label With {.Text = caption, .Dock = DockStyle.Top, .Height = 18, .Tag = Theme.MutedTag,
                                   .ForeColor = Theme.Muted, .BackColor = Theme.Panel, .Font = New Font("Segoe UI", 8.0F, FontStyle.Bold)}
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
        Dim row = _rows.Rows(e.ItemIndex)
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
        Dim thumb = Await _coverCache.GetThumbnailAsync(comicId, url)
        If thumb Is Nothing OrElse IsDisposed Then Return
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
        Dim small As New Bitmap(44, 66)
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
            If Not quiet Then Ui.ShowError(Me, $"Couldn't reach your relay: {ex.Message}")
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
        Dim found = 0, done = 0
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
                                 match.Price, withPrices)
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
            If Not quiet AndAlso Not IsDisposed Then Ui.ShowError(Me, $"Finding covers stopped: {ex.Message}")
        Finally
            _findingCovers = False
            If Not IsDisposed Then UpdateFindCoversButton()
        End Try
    End Function

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
        End Select
        _rows = _db.SearchCollection(_search.Text, read, show)
        _grid.DataSource = _rows
        _covers.VirtualListSize = _rows.Rows.Count
        _covers.Invalidate()
        _thumbColumn.DisplayIndex = 0
        ' Columns share out the window's width, so a maximised window on a wide screen uses it all.
        For Each col As DataGridViewColumn In _grid.Columns
            If col Is _thumbColumn Then Continue For
            col.MinimumWidth = 60
            Select Case col.Name
                Case "Series", "Story title" : col.FillWeight = 220
                Case "Cover / variant", "Publisher", "Set" : col.FillWeight = 140
                Case "Lent to", "Box", "Trade" : col.FillWeight = 90
                Case "Key issue" : col.FillWeight = 160
                Case "Read"
                    col.FillWeight = 45
                    col.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter
                Case Else : col.FillWeight = 80
            End Select
        Next
        For Each colName In {"collection_id", "comic_id", "cover_url"}
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
        If selectComicId > 0 Then
            For Each row As DataGridViewRow In _grid.Rows
                If Convert.ToInt64(row.Cells("comic_id").Value, CultureInfo.InvariantCulture) = selectComicId Then
                    _grid.ClearSelection()
                    row.Selected = True
                    _grid.FirstDisplayedScrollingRowIndex = row.Index
                    Exit For
                End If
            Next
        End If
        UpdateFindCoversButton()
        Dim s = _db.GetStats()
        _statComics.Text = s.Comics.ToString("N0")
        _statCopies.Text = s.Copies.ToString("N0")
        _statValue.Text = "$" & s.TotalValue.ToString("N2")
        _statPaid.Text = "$" & s.TotalPaid.ToString("N2")
        _statRead.Text = $"{s.Read:N0} of {s.Comics:N0}"
    End Sub

    Private Function SelectedComicIds() As List(Of Long)
        If _covers.Visible Then
            Return _covers.SelectedIndices.Cast(Of Integer)().
                Select(Function(i) Convert.ToInt64(_rows.Rows(i)("comic_id"), CultureInfo.InvariantCulture)).ToList()
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
        _db.SetRead(ids, Not allRead)
        RefreshCollection(ids(0))
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
                Ui.ShowError(Me, $"Couldn't save it. If that file is open in Excel or a PDF viewer, close it and try again. ({ex.Message})")
                Return
            Catch ex As Exception
                Ui.ShowError(Me, $"Couldn't save it: {ex.Message}")
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
        Using f As New ComicForm(_db, Metron)
            If f.ShowDialog(Me) = DialogResult.OK Then RefreshCollection(f.SavedComicId)
        End Using
    End Sub

    ''' <summary>Set by the build's screenshot step: opens the first comic once the window is up.</summary>
    Public Property EditFirstOnStart As Boolean
    ''' <summary>Set by the build's screenshot step: the page to show once the window is up.</summary>
    Public Property StartPage As String = ""

    Protected Overrides Sub OnFormClosing(e As FormClosingEventArgs)
        MyBase.OnFormClosing(e)
        If _settings.MainMaximized <> (WindowState = FormWindowState.Maximized) OrElse _settings.EditMaximized <> ComicForm.StartMaximized Then
            _settings.MainMaximized = WindowState = FormWindowState.Maximized
            _settings.EditMaximized = ComicForm.StartMaximized
            Try
                _settings.Save()
            Catch ex As IO.IOException
            End Try
        End If
    End Sub

    Protected Overrides Sub OnShown(e As EventArgs)
        MyBase.OnShown(e)
        If StartPage <> "" AndAlso PageNames.Contains(StartPage) Then ShowPage(StartPage)
        If EditFirstOnStart AndAlso _rows IsNot Nothing AndAlso _rows.Rows.Count > 0 Then
            BeginInvoke(Sub() EditComic(_db.GetComic(Convert.ToInt64(_rows.Rows(0)("comic_id"), CultureInfo.InvariantCulture))))
        End If
    End Sub

    Private Sub OnEdit(sender As Object, e As EventArgs)
        Dim ids = SelectedComicIds()
        If ids.Count = 0 Then Return
        Dim c = _db.GetComic(ids(0))
        If c IsNot Nothing Then EditComic(c)
    End Sub

    Private Sub EditComic(c As ComicRecord)
        Using f As New ComicForm(_db, Metron, c)
            If f.ShowDialog(Me) = DialogResult.OK Then RefreshCollection(f.SavedComicId)
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
        If MessageBox.Show(Me, $"Delete {ids.Count} comic{If(ids.Count = 1, "", "s")}? This can't be undone." & vbCrLf & vbCrLf & String.Join(vbCrLf, names),
                           "Delete comics", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) <> DialogResult.OK Then Return
        _db.DeleteComics(ids)
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
        Dim answer = Ui.Ask(Me, "Box or shelf", $"Where are {If(ids.Count = 1, "it", $"these {ids.Count}")} kept? For example Box 3. Leave it blank to clear.",
                            If(_db.GetComic(ids(0))?.Location, ""), _db.Locations())
        If answer Is Nothing Then Return
        _db.SetLocation(ids, answer)
        RefreshCollection(ids(0))
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
        _db.SetForTrade(ids, True, price)
        RefreshCollection(ids(0))
    End Sub

    Private Sub OnNotForTrade(sender As Object, e As EventArgs)
        Dim ids = PickedComics()
        If ids Is Nothing Then Return
        _db.SetForTrade(ids, False)
        RefreshCollection(ids(0))
    End Sub

    Private Sub OnKeyIssue(sender As Object, e As EventArgs)
        Dim ids = PickedComics()
        If ids Is Nothing Then Return
        Dim answer = Ui.Ask(Me, "Key issue", "Why is it a key issue? For example ""1st appearance of Harley Quinn"". Leave it blank to un-mark it.",
                            If(_db.GetComic(ids(0))?.KeyNote, ""))
        If answer Is Nothing Then Return
        _db.SetKeyNote(ids, answer)
        RefreshCollection(ids(0))
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
            If Not quiet Then Ui.ShowError(Me, $"Couldn't reach your relay: {ex.Message}")
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
            problem = ex.Message
            If Not quiet AndAlso Not IsDisposed Then Ui.ShowError(Me, $"Checking eBay stopped: {ex.Message}")
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
            _db.LendComics(ids, f.Borrower, f.LentOn, f.Notes)
        End Using
        RefreshCollection(ids(0))
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
        _loadingGoals = False
        _budget.Font = New Font("Segoe UI", 11.0F)
        _readGoal.Font = New Font("Segoe UI", 11.0F)
        AddHandler _budget.ValueChanged, AddressOf OnGoalsChanged
        AddHandler _readGoal.ValueChanged, AddressOf OnGoalsChanged
        bar.Controls.AddRange({Ui.MakeLabel("Bars"), _statsByValue, New Label With {.Width = 16},
                               Ui.MakeLabel("Monthly budget $"), _budget, Ui.MakeLabel("Read each month"), _readGoal, _statsNote})

        Dim charts As New TableLayoutPanel With {.Dock = DockStyle.Top, .AutoSize = True, .ColumnCount = 2}
        charts.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 50))
        charts.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 50))
        Dim order = {_chartHistory, _chartSpending, _chartReading, _chartPublishers, _chartDecades, _chartSeries, _chartValuable,
                     _chartKeys, _chartWriters, _chartArtists}
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
        _chartKeys.EmptyText = "Mark key issues (right-click a comic, then Mark as key issue) to see them here."
        _chartKeys.ShowBars(_db.StatsBy("keys", 10))
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
        _settings.Save()
        If _db IsNot Nothing Then RefreshStats()
    End Sub

    ' ---------- Missing issues ----------

    Private Function BuildMissingTab() As Control
        Dim page As New Panel()
        Dim split As New SplitContainer With {.Dock = DockStyle.Fill, .SplitterDistance = 600}
        Dim help As New Label With {.Dock = DockStyle.Top, .Height = 28, .TextAlign = ContentAlignment.MiddleLeft,
                                    .Text = "Series with gaps between the first and last issue you own. Click one to see what's missing."}
        split.Panel1.Controls.Add(_gapsGrid)
        split.Panel1.Controls.Add(help)
        Dim wishButton = Ui.MakeButton("Add selected to wishlist", AddressOf OnWishMissing, Theme.PrimaryTag)
        wishButton.Dock = DockStyle.Bottom
        split.Panel2.Controls.Add(_missingList)
        split.Panel2.Controls.Add(_missingTitle)
        split.Panel2.Controls.Add(wishButton)
        AddHandler _gapsGrid.SelectionChanged, Sub(s, e) ShowMissing()
        page.Controls.Add(split)
        Return page
    End Function

    Private Sub RefreshGaps()
        _gapsGrid.DataSource = _db.SeriesWithGaps()
        ShowMissing()
    End Sub

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
            If Not quiet AndAlso Not IsDisposed Then Ui.ShowError(Me, $"Finding story arcs stopped: {ex.Message}")
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
            If Not IsDisposed Then Ui.ShowError(Me, $"Couldn't finish: {ex.Message}")
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
            If Not quiet Then Ui.ShowError(Me, $"Couldn't reach your relay: {ex.Message}")
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
                _db.SaveSet(f.SetName, ids, f.TotalValue)
            Catch ex As Exception
                Ui.ShowError(Me, $"Couldn't save the set: {ex.Message}")
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
                Ui.ShowError(Me, $"Couldn't save the set (is that name already used by another set?): {ex.Message}")
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
        _search.Text = CStr(row("Set"))
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
                               Ui.MakeButton("Share page…", Sub(s, e) OnShare(wishlistOnly:=True)),
                               Ui.MakeLabel("Priority 1 = must have, 3 = nice to have. Comics come off the list when you add them to your collection.")})
        page.Controls.Add(_wishGrid)
        page.Controls.Add(bar)
        Return page
    End Function

    Private Sub RefreshWishlist()
        _wishGrid.DataSource = _db.GetWishlist()
        If _wishGrid.Columns.Contains("id") Then _wishGrid.Columns("id").Visible = False
        If _wishGrid.Columns.Contains("Max price") Then _wishGrid.Columns("Max price").DefaultCellStyle.Format = "N2"
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
            If Not quiet AndAlso Not IsDisposed Then Ui.ShowError(Me, $"Checking for new releases stopped: {ex.Message}")
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

        Dim updateRow As New FlowLayoutPanel With {.AutoSize = True, .WrapContents = False}
        updateRow.Controls.AddRange({Ui.MakeButton("Check for updates", Sub(s, e) CheckForUpdate(quiet:=False), Theme.PrimaryTag), _updateStatus})

        Dim openFolder = Ui.MakeButton("Open the folder", Sub(s, e) Process.Start(New ProcessStartInfo With {
                                                                 .FileName = Path.GetDirectoryName(_settings.DatabasePath), .UseShellExecute = True}))

        panel.Controls.AddRange({
            heading("Comic lookup"),
            note("Barcode lookups use the same relay address as the phone app (Settings on your phone shows it). Your Metron password stays in Cloudflare."),
            relayRow, _relayStatus,
            heading("Scans from your phone"),
            note("On your phone, open Settings, find ""Send scans to your computer"" and tap Turn on. Type the code it shows here. " &
                 "While this app is open it collects new scans every couple of minutes, once the phone has looked up their details."),
            syncRow, _syncStatus,
            note("With a code saved, the phone also gets a copy of your collection and wishlist, so scanning in a shop tells you if you already own a comic or want it."),
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
            heading("Updates"),
            note("The app checks for a newer version each time it opens and asks before updating."),
            updateRow,
            heading("Bring in your collection from the phone app"),
            note("On your phone: Settings, then Export spreadsheet (CSV). Save it to iCloud or OneDrive, then pick that file here. Comics already here are skipped, so you can import again later."),
            Ui.MakeButton("Import phone app spreadsheet (CSV)…", AddressOf OnImportCsv),
            heading("Try it out"),
            note("Fills an empty collection with a few sample comics so you can see how everything works. Delete them when you're done."),
            Ui.MakeButton("Load sample comics", AddressOf OnLoadSamples),
            heading("Banner picture"),
            note("Pick any picture from your computer to show across the top of the app. A copy is kept in your Comic Catalog folder, so it stays on this computer."),
            bannerRow,
            heading("Backup"),
            note("Once a day the app saves a copy of your collection. With OneDrive on this computer the copies go there, so they're safe " &
                 "even if this computer dies. The last 30 days are kept."),
            _backupStatus,
            backupRow,
            heading("Database file"),
            _dbPath,
            note("This is a normal SQLite file. You can open it in DB Browser for SQLite to run your own queries."),
            openFolder})
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
            _relayStatus.Text = $"Saved, but the test failed: {ex.Message}"
        End Try
    End Sub

    Private Sub OnImportCsv(sender As Object, e As EventArgs)
        Using dlg As New OpenFileDialog With {.Filter = "Spreadsheet (*.csv)|*.csv|All files (*.*)|*.*", .Title = "Pick the spreadsheet exported from the phone app"}
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            Try
                UseWaitCursor = True
                Dim result = _db.ImportPhoneCsv(File.ReadAllText(dlg.FileName))
                RefreshCollection()
                MessageBox.Show(Me, $"Imported {result.Added:N0} comics." & If(result.Skipped > 0, $" {result.Skipped:N0} were already here and were skipped.", ""), "Import")
            Catch ex As Exception
                Ui.ShowError(Me, $"Couldn't import that file: {ex.Message}")
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

    Private Sub OnLoadSamples(sender As Object, e As EventArgs)
        If _db.LoadSampleData() Then
            RefreshCollection()
            MessageBox.Show(Me, "Added a few sample comics. Have a look on the Collection tab.", "Sample comics")
        Else
            Ui.ShowError(Me, "Sample comics can only go into an empty collection.")
        End If
    End Sub

End Class
