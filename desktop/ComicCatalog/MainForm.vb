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
    Private _rows As DataTable
    Private ReadOnly _listButton As Button = Ui.MakeButton("List", Sub(s, e) SetView(False))
    Private ReadOnly _coversButton As Button = Ui.MakeButton("Covers", Sub(s, e) SetView(True))
    Private ReadOnly _statComics As New Label()
    Private ReadOnly _statCopies As New Label()
    Private ReadOnly _statValue As New Label()
    Private ReadOnly _statPaid As New Label()

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

    ' Settings tab
    Private ReadOnly _relayUrl As New TextBox With {.Width = 420, .PlaceholderText = "https://comic-relay.yourname.workers.dev"}
    Private ReadOnly _relayStatus As New Label With {.AutoSize = True, .ForeColor = SystemColors.GrayText}
    Private ReadOnly _dbPath As New Label With {.AutoSize = True, .ForeColor = SystemColors.GrayText}

    ' Banner picture across the top
    Private ReadOnly _banner As New Panel With {.Dock = DockStyle.Top, .Height = 150, .BackColor = Color.FromArgb(20, 22, 28), .Visible = False}
    Private _bannerImage As Image

    Public Sub New()
        Text = "Comic Catalog"
        AutoScaleMode = AutoScaleMode.Font
        Font = New Font("Segoe UI", 9.5F)
        ClientSize = New Size(1100, 700)
        MinimumSize = New Size(800, 500)
        StartPosition = FormStartPosition.CenterScreen

        Dim content As New Panel With {.Dock = DockStyle.Fill, .Padding = New Padding(16, 12, 16, 12)}
        _pages.AddRange({BuildCollectionTab(), BuildMissingTab(), BuildWishlistTab(), BuildSettingsTab()})
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
        Dim names = {"Collection", "Missing issues", "Wishlist", "Settings"}
        For i = 0 To names.Length - 1
            Dim index = i
            Dim b As New Button With {.Text = names(i), .AutoSize = True, .FlatStyle = FlatStyle.Flat, .Height = 38,
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

    Private Sub ShowPage(index As Integer)
        For i = 0 To _pages.Count - 1
            _pages(i).Visible = (i = index)
            _navButtons(i).ForeColor = If(i = index, Theme.Accent, Theme.Muted)
            _navButtons(i).FlatAppearance.BorderSize = 0
        Next
        _pages(index).BringToFront()
        If _db Is Nothing Then Return
        Select Case index
            Case 0 : _barcode.Focus()
            Case 1 : RefreshGaps()
            Case 2 : RefreshWishlist()
        End Select
    End Sub

    Protected Overrides Sub OnLoad(e As EventArgs)
        MyBase.OnLoad(e)
        Try
            _db = New ComicDb(_settings.DatabasePath)
        Catch ex As Exception
            Ui.ShowError(Me, $"Couldn't open the database at {_settings.DatabasePath}: {ex.Message}")
            Close()
            Return
        End Try
        _relayUrl.Text = _settings.RelayUrl
        ShowBanner()
        _dbPath.Text = $"Your collection is saved in {_settings.DatabasePath}"
        RefreshCollection()
        _barcode.Focus()
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
            Ui.MakeLabel("Search"), _search,
            New Label With {.Width = 16},
            _listButton, _coversButton,
            New Label With {.Width = 16},
            Ui.MakeButton("Add by hand", AddressOf OnAddByHand),
            Ui.MakeButton("Edit", AddressOf OnEdit),
            Ui.MakeButton("Delete", AddressOf OnDelete, Theme.DangerTag)})

        ' Totals as big number cards
        Dim cards As New FlowLayoutPanel With {.Dock = DockStyle.Top, .AutoSize = True, .Padding = New Padding(0, 0, 0, 10)}
        cards.Controls.AddRange({StatCard(_statComics, "COMICS"), StatCard(_statCopies, "COPIES"),
                                 StatCard(_statValue, "COLLECTION VALUE"), StatCard(_statPaid, "TOTAL PAID")})

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
        AddHandler _grid.CellDoubleClick, Sub(s, e)
                                              If e.RowIndex >= 0 Then OnEdit(s, e)
                                          End Sub
        AddHandler _grid.KeyDown, Sub(s, e)
                                      If e.KeyCode = Keys.Delete Then OnDelete(s, e)
                                      If e.KeyCode = Keys.Enter Then
                                          e.SuppressKeyPress = True
                                          OnEdit(s, e)
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
        _covers.Invalidate()
    End Sub

    Private Sub RefreshCollection(Optional selectComicId As Long = 0)
        If _db Is Nothing Then Return
        _rows = _db.SearchCollection(_search.Text)
        _grid.DataSource = _rows
        _covers.VirtualListSize = _rows.Rows.Count
        _covers.Invalidate()
        For Each colName In {"collection_id", "comic_id", "cover_url"}
            If _grid.Columns.Contains(colName) Then _grid.Columns(colName).Visible = False
        Next
        For Each colName In {"Paid", "Value"}
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
        Dim s = _db.GetStats()
        _statComics.Text = s.Comics.ToString("N0")
        _statCopies.Text = s.Copies.ToString("N0")
        _statValue.Text = "$" & s.TotalValue.ToString("N2")
        _statPaid.Text = "$" & s.TotalPaid.ToString("N2")
    End Sub

    Private Function SelectedComicIds() As List(Of Long)
        If _covers.Visible Then
            Return _covers.SelectedIndices.Cast(Of Integer)().
                Select(Function(i) Convert.ToInt64(_rows.Rows(i)("comic_id"), CultureInfo.InvariantCulture)).ToList()
        End If
        Return _grid.SelectedRows.Cast(Of DataGridViewRow)().
            Select(Function(r) Convert.ToInt64(r.Cells("comic_id").Value, CultureInfo.InvariantCulture)).ToList()
    End Function

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

    Private Function BuildWishlistTab() As Control
        Dim page As New Panel()
        Dim bar As New FlowLayoutPanel With {.Dock = DockStyle.Top, .AutoSize = True, .Padding = New Padding(4)}
        bar.Controls.AddRange({Ui.MakeButton("Add", AddressOf OnAddWish, Theme.PrimaryTag), Ui.MakeButton("Remove", AddressOf OnRemoveWish, Theme.DangerTag),
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

        Dim bannerRow As New FlowLayoutPanel With {.AutoSize = True, .WrapContents = False}
        bannerRow.Controls.AddRange({Ui.MakeButton("Choose banner picture…", AddressOf OnChooseBanner),
                                     Ui.MakeButton("Remove banner", AddressOf OnRemoveBanner)})

        Dim openFolder = Ui.MakeButton("Open the folder", Sub(s, e) Process.Start(New ProcessStartInfo With {
                                                                 .FileName = Path.GetDirectoryName(_settings.DatabasePath), .UseShellExecute = True}))

        panel.Controls.AddRange({
            heading("Comic lookup"),
            note("Barcode lookups use the same relay address as the phone app (Settings on your phone shows it). Your Metron password stays in Cloudflare."),
            relayRow, _relayStatus,
            heading("Bring in your collection from the phone app"),
            note("On your phone: Settings, then Export spreadsheet (CSV). Save it to iCloud or OneDrive, then pick that file here. Comics already here are skipped, so you can import again later."),
            Ui.MakeButton("Import phone app spreadsheet (CSV)…", AddressOf OnImportCsv),
            heading("Try it out"),
            note("Fills an empty collection with a few sample comics so you can see how everything works. Delete them when you're done."),
            Ui.MakeButton("Load sample comics", AddressOf OnLoadSamples),
            heading("Banner picture"),
            note("Pick any picture from your computer to show across the top of the app. A copy is kept in your Comic Catalog folder, so it stays on this computer."),
            bannerRow,
            heading("Database file"),
            _dbPath,
            note("This is a normal SQLite file. Copy it somewhere safe as a backup, or open it in DB Browser for SQLite to run your own queries."),
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
            _relayStatus.Text = "Lookup turned off."
            Return
        End If
        _relayStatus.Text = "Testing…"
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
