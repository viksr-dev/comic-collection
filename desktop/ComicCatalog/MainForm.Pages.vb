Option Strict On

Imports System.Data
Imports System.Diagnostics
Imports System.Drawing
Imports System.Drawing.Imaging
Imports System.Globalization
Imports System.IO
Imports System.Net.Http
Imports System.Threading
Imports System.Windows.Forms
Imports ComicCatalog.Data

' The Home, Digital and Videos pages.
Partial Public Class MainForm

    Private Shared ReadOnly WebHttp As New HttpClient With {.Timeout = TimeSpan.FromSeconds(25)}
    Private Shared ReadOnly HomeCover As New Size(130, 195)
    Private Shared ReadOnly ShelfCover As New Size(150, 225)
    Private Shared ReadOnly VideoThumb As New Size(288, 162)

    ' Home
    Private ReadOnly _home As New FlowLayoutPanel With {.FlowDirection = FlowDirection.TopDown, .WrapContents = False, .AutoScroll = True}

    ' Digital shelf
    Private ReadOnly _digitalFlow As New FlowLayoutPanel With {.Dock = DockStyle.Fill, .AutoScroll = True}
    Private ReadOnly _digitalSearch As New TextBox With {.Width = 220, .PlaceholderText = "search your digital comics", .Font = New Font("Segoe UI", 12.0F)}
    Private ReadOnly _digitalStatus As New Label With {.AutoSize = True, .Margin = New Padding(12, 12, 3, 3), .Tag = Theme.MutedTag}
    Private ReadOnly _digitalMenu As New ContextMenuStrip()
    Private ReadOnly _folderMenu As New ContextMenuStrip()
    ''' <summary>The series folder open on the Digital page, or "" for the whole shelf.</summary>
    Private _openFolder As String = ""
    Private ReadOnly _folderBar As New FlowLayoutPanel With {.Dock = DockStyle.Top, .AutoSize = True, .Padding = New Padding(0, 0, 0, 8), .Visible = False}
    Private ReadOnly _folderTitle As New Label With {.AutoSize = True, .Font = New Font("Segoe UI Semibold", 15.0F), .Margin = New Padding(12, 4, 3, 3)}
    Private _addingDigital As Boolean
    Private Shared ReadOnly DigitalCoverGate As New SemaphoreSlim(2)

    ' Videos
    Private ReadOnly _videoFlow As New FlowLayoutPanel With {.Dock = DockStyle.Fill, .AutoScroll = True}
    Private ReadOnly _videoChannels As New TextBox With {.Width = 320, .PlaceholderText = "@comictom101, @anotherchannel"}
    Private ReadOnly _videoStatus As New Label With {.AutoSize = True, .Margin = New Padding(12, 12, 3, 3), .Tag = Theme.MutedTag}
    Private ReadOnly _videoTimer As New System.Windows.Forms.Timer With {.Interval = 60 * 1000}
    Private _checkingVideos As Boolean

    ' Covers view size
    Private ReadOnly _coverSize As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = 130, .Font = New Font("Segoe UI", 11.0F)}

    ' ---------- Home ----------

    Private Function BuildHomeTab() As Control
        Dim page As New Panel()
        _home.Dock = DockStyle.Fill
        AddHandler _home.Resize, Sub(s, e) SizeHomeRows()
        page.Controls.Add(_home)
        Return page
    End Function

    Private Sub SizeHomeRows()
        Dim w = Math.Max(300, _home.ClientSize.Width - 8)
        For Each c As Control In _home.Controls
            c.Width = w
        Next
    End Sub

    ''' <summary>Rebuilds the Home page: totals, comics to carry on reading, newest videos and rows of covers.</summary>
    Private Sub RefreshHome()
        If _db Is Nothing Then Return
        _home.SuspendLayout()
        For Each c In _home.Controls.Cast(Of Control)().ToList()
            c.Dispose()
        Next
        _home.Controls.Clear()

        Dim s = _db.GetStats()
        Dim hello As New Label With {.Text = Greeting(), .AutoSize = True, .Font = New Font("Segoe UI Semibold", 22.0F), .ForeColor = Theme.Text,
                                     .Margin = New Padding(0, 0, 0, 2)}
        Dim sub1 As New Label With {.AutoSize = True, .ForeColor = Theme.Muted, .Font = New Font("Segoe UI", 11.0F), .Margin = New Padding(2, 0, 0, 12),
                                    .Text = $"{s.Comics:N0} comics worth ${s.TotalValue:N2}. You've read {s.Read:N0} of them."}
        _home.Controls.Add(hello)
        _home.Controls.Add(sub1)

        Dim cards As New FlowLayoutPanel With {.AutoSize = True, .WrapContents = True, .Margin = New Padding(0, 0, 0, 14)}
        Dim digital = _db.DigitalFiles()
        Dim newVideos = _db.NewVideoCount()
        Dim trades = _db.GetStats("trades").Comics
        cards.Controls.Add(HomeCard("COMICS", (s.Comics - trades).ToString("N0"), Icons.Library, Sub() ShowPage("Collection")))
        If trades > 0 Then cards.Controls.Add(HomeCard("TRADES", trades.ToString("N0"), Icons.Book, Sub() ShowPage("Trades")))
        cards.Controls.Add(HomeCard("COLLECTION VALUE", "$" & s.TotalValue.ToString("N0"), Icons.Chart, Sub() ShowPage("Stats")))
        cards.Controls.Add(HomeCard("READ", If(s.Comics = 0, "0%", $"{100.0 * s.Read / s.Comics:0}%"), Icons.Check, Sub() ShowPage("Stats")))
        cards.Controls.Add(HomeCard("DIGITAL COMICS", digital.Count.ToString("N0"), Icons.Reading, Sub() ShowPage("Digital")))
        cards.Controls.Add(HomeCard("NEW VIDEOS", newVideos.ToString("N0"), Icons.Video, Sub() ShowPage("Videos")))
        _home.Controls.Add(cards)

        ' Carry on reading
        Dim reading = _db.KeepReading(10)
        If reading.Count > 0 Then
            AddHomeRow("Keep reading", reading.Select(Function(f) DigitalTile(f, HomeCover)).Cast(Of Control)().ToList(), "All digital comics", Sub() ShowPage("Digital"))
        ElseIf digital.Count = 0 Then
            AddHomeNote("Digital comics", "Add CBZ, CBR, PDF or EPUB comics on the Digital page to read them here.", "Go to Digital", Sub() ShowPage("Digital"))
        End If

        ' Newest videos
        Dim videos = _db.Videos(4)
        If videos.Count > 0 Then
            AddHomeRow("Latest videos", videos.Select(Function(v) VideoTile(v)).Cast(Of Control)().ToList(), "All videos", Sub() ShowPage("Videos"))
        End If

        AddCoverRow("Just added", _db.CoverStrip("recent", 14))
        AddCoverRow("Recently changed", _db.CoverStrip("changed", 14))
        AddCoverRow("Waiting to be read", _db.CoverStrip("unread", 14))
        AddCoverRow("Your key issues", _db.CoverStrip("key", 14))
        _home.ResumeLayout()
        SizeHomeRows()
    End Sub

    Private Shared Function Greeting() As String
        Dim h = DateTime.Now.Hour
        Return If(h < 12, "Good morning", If(h < 18, "Good afternoon", "Good evening"))
    End Function

    Private Function HomeCard(caption As String, value As String, glyph As Char, onClick As Action) As Control
        Dim card As New Panel With {.Size = New Size(200, 84), .BackColor = Theme.Panel, .Margin = New Padding(0, 0, 12, 12), .Cursor = Cursors.Hand}
        Dim icon As New PictureBox With {.Size = New Size(32, 32), .Location = New Point(14, 26), .Image = Icons.Make(glyph, Theme.Accent, 32),
                                         .BackColor = Theme.Panel, .SizeMode = PictureBoxSizeMode.CenterImage}
        Dim cap As New Label With {.Text = caption, .Location = New Point(56, 14), .AutoSize = True, .ForeColor = Theme.Muted, .BackColor = Theme.Panel,
                                   .Font = New Font("Segoe UI", 8.0F, FontStyle.Bold)}
        Dim val As New Label With {.Text = value, .Location = New Point(54, 32), .AutoSize = True, .ForeColor = Theme.Text, .BackColor = Theme.Panel,
                                   .Font = New Font("Segoe UI Semibold", 19.0F)}
        card.Controls.AddRange({icon, cap, val})
        For Each c As Control In New Control() {card, icon, cap, val}
            AddHandler c.Click, Sub(sender, e) onClick()
            AddHandler c.MouseEnter, Sub(sender, e) card.BackColor = Theme.Panel2
            AddHandler c.MouseLeave, Sub(sender, e) card.BackColor = Theme.Panel
        Next
        AddHandler card.BackColorChanged, Sub(sender, e)
                                               For Each c As Control In card.Controls
                                                   c.BackColor = card.BackColor
                                               Next
                                           End Sub
        Return card
    End Function

    Private Function RowHeading(title As String, linkText As String, onLink As Action) As Control
        Dim head As New FlowLayoutPanel With {.AutoSize = True, .WrapContents = False, .Margin = New Padding(0, 6, 0, 4)}
        head.Controls.Add(New Label With {.Text = title, .AutoSize = True, .Font = New Font("Segoe UI Semibold", 14.0F), .ForeColor = Theme.Text,
                                          .Margin = New Padding(0, 0, 12, 0)})
        If linkText <> "" Then
            Dim link As New LinkLabel With {.Text = linkText & " ›", .AutoSize = True, .LinkColor = Theme.Accent, .ActiveLinkColor = Theme.Text,
                                            .LinkBehavior = LinkBehavior.HoverUnderline, .Margin = New Padding(0, 8, 0, 0), .Font = New Font("Segoe UI", 10.0F)}
            AddHandler link.LinkClicked, Sub(s, e) onLink()
            head.Controls.Add(link)
        End If
        Return head
    End Function

    ''' <summary>A heading and a row of tiles that scrolls sideways.</summary>
    Private Sub AddHomeRow(title As String, tiles As List(Of Control), linkText As String, onLink As Action)
        If tiles.Count = 0 Then Return
        _home.Controls.Add(RowHeading(title, linkText, onLink))
        Dim row As New FlowLayoutPanel With {.WrapContents = False, .AutoScroll = True, .Margin = New Padding(0, 0, 0, 10),
                                             .Height = tiles.Max(Function(t) t.Height) + SystemInformation.HorizontalScrollBarHeight + 6}
        row.Controls.AddRange(tiles.ToArray())
        _home.Controls.Add(row)
    End Sub

    Private Sub AddHomeNote(title As String, text As String, linkText As String, onLink As Action)
        _home.Controls.Add(RowHeading(title, linkText, onLink))
        _home.Controls.Add(New Label With {.Text = text, .AutoSize = True, .ForeColor = Theme.Muted, .Margin = New Padding(2, 0, 0, 16)})
    End Sub

    Private Sub AddCoverRow(title As String, rows As DataTable)
        Dim tiles As New List(Of Control)
        For Each r As DataRow In rows.Rows
            Dim comicId = CLng(r("comic_id"))
            Dim t As New Tile(HomeCover, "No cover") With {.Caption = CStr(r("label")), .Detail = CStr(r("key_note")), .Item = comicId, .OwnsPicture = True}
            AddHandler t.Click, Sub(s, e)
                                    Dim c = _db.GetComic(comicId)
                                    If c IsNot Nothing Then EditComic(c)
                                    RefreshHome()
                                End Sub
            LoadTileCover(t, comicId, CStr(r("cover_url")))
            tiles.Add(t)
        Next
        AddHomeRow(title, tiles, If(title = "Your key issues", "", "Collection"), Sub() ShowPage("Collection"))
    End Sub

    Private Async Sub LoadTileCover(t As Tile, comicId As Long, url As String)
        If url = "" Then Return
        Dim img = Await _coverCache.GetThumbnailAsync(comicId, url, HomeCover)
        If img Is Nothing Then Return
        If t.IsDisposed Then
            img.Dispose()
            Return
        End If
        t.Picture = img
    End Sub

    ' ---------- Digital shelf ----------

    Private Function BuildDigitalTab() As Control
        Dim page As New Panel()
        Dim bar As New FlowLayoutPanel With {.Dock = DockStyle.Top, .AutoSize = True, .Padding = New Padding(0, 0, 0, 8)}
        Dim addFiles = Ui.MakeButton("Add comic files…", AddressOf OnAddDigitalFiles, Theme.PrimaryTag)
        Dim addFolder = Ui.MakeButton("Add a folder…", AddressOf OnAddDigitalFolder)
        Dim openFolder = Ui.MakeButton("Open the folder", Sub(s, e)
                                                              Dim folder = _settings.DigitalFolderOrDefault()
                                                              Directory.CreateDirectory(folder)
                                                              Process.Start(New ProcessStartInfo With {.FileName = folder, .UseShellExecute = True})
                                                          End Sub)
        bar.Controls.AddRange({addFiles, addFolder, New Label With {.Width = 16}, Ui.MakeLabel("Search"), _digitalSearch, openFolder, _digitalStatus})
        Icons.Put(addFiles, Icons.Add)
        Icons.Put(addFolder, Icons.Folder)
        Icons.Put(openFolder, Icons.Folder)
        AddHandler _digitalSearch.TextChanged, Sub(s, e) RefreshDigital()

        _digitalMenu.Font = New Font("Segoe UI", 10.0F)
        _digitalMenu.Items.Add("Read", Nothing, Sub(s, e) WithMenuFile(AddressOf OpenDigital))
        _digitalMenu.Items.Add("Start again from page 1", Nothing, Sub(s, e) WithMenuFile(Sub(f)
                                                                                                _db.SaveReadingPlace(f.Id, 0, f.Pages)
                                                                                                f.Page = 0
                                                                                                f.LastRead = ""
                                                                                                OpenDigital(f)
                                                                                            End Sub))
        _digitalMenu.Items.Add("Link to a comic in my collection…", Nothing, Sub(s, e) WithMenuFile(AddressOf OnLinkDigital))
        _digitalMenu.Items.Add("Rename…", Nothing, Sub(s, e) WithMenuFile(AddressOf OnRenameDigital))
        _digitalMenu.Items.Add("Move to folder…", Nothing, Sub(s, e) WithMenuFile(AddressOf OnMoveDigital))
        _digitalMenu.Items.Add("Show the file", Nothing, Sub(s, e) WithMenuFile(Sub(f)
                                                                                     If File.Exists(f.FilePath) Then Process.Start("explorer.exe", $"/select,""{f.FilePath}""")
                                                                                 End Sub))
        _digitalMenu.Items.Add(New ToolStripSeparator())
        _digitalMenu.Items.Add("Remove…", Nothing, Sub(s, e) WithMenuFile(AddressOf OnRemoveDigital))

        ' Comic files dragged onto the shelf are added.
        _digitalFlow.AllowDrop = True
        AddHandler _digitalFlow.DragEnter, Sub(s, e)
                                               If e.Data.GetDataPresent(DataFormats.FileDrop) Then e.Effect = DragDropEffects.Copy
                                           End Sub
        AddHandler _digitalFlow.DragDrop, Sub(s, e)
                                              Dim files = TryCast(e.Data.GetData(DataFormats.FileDrop), String())
                                              If files IsNot Nothing Then AddDigital(ExpandComicFiles(files), Nothing)
                                          End Sub
        ' A series folder: a way back to the whole shelf, and the series name.
        Dim back = Ui.MakeButton("All comics", Sub(s, e) ShowFolder(""))
        Icons.Put(back, Icons.Back)
        _folderBar.Controls.AddRange({back, _folderTitle})
        _folderMenu.Font = New Font("Segoe UI", 10.0F)
        _folderMenu.Items.Add("Open", Nothing, Sub(s, e) WithMenuFolder(Sub(g) ShowFolder(g.Name)))
        _folderMenu.Items.Add("Carry on reading", Nothing, Sub(s, e) WithMenuFolder(Sub(g) OpenDigital(g.UpTo)))
        _folderMenu.Items.Add("Rename folder…", Nothing, Sub(s, e) WithMenuFolder(AddressOf OnRenameFolder))

        page.Controls.Add(_digitalFlow)
        page.Controls.Add(_folderBar)
        page.Controls.Add(bar)
        Return page
    End Function

    Private Sub WithMenuFile(action As Action(Of DigitalFile))
        Dim t = TryCast(_digitalMenu.SourceControl, Tile)
        Dim f = TryCast(t?.Item, DigitalFile)
        If f IsNot Nothing Then action(f)
    End Sub

    Private Sub WithMenuFolder(action As Action(Of DigitalGroup))
        Dim t = TryCast(_folderMenu.SourceControl, Tile)
        Dim g = TryCast(t?.Item, DigitalGroup)
        If g IsNot Nothing Then action(g)
    End Sub

    Private Sub ShowFolder(name As String)
        _openFolder = name
        _digitalFlow.AutoScrollPosition = Point.Empty
        RefreshDigital()
    End Sub

    ''' <summary>
    ''' The shelf: a folder for each series (in issue order inside), comics on their own where a series has just one.
    ''' Searching lists every match, by series and issue.
    ''' </summary>
    Private Sub RefreshDigital()
        If _db Is Nothing Then Return
        Dim words = _digitalSearch.Text.Trim()
        Dim all = _db.DigitalFiles()
        Dim files = all.Where(Function(f) words = "" OrElse
                                  (f.Title & " " & f.SeriesFolder & " " & f.ComicLabel & " " & Path.GetFileName(f.FilePath)).Contains(words, StringComparison.OrdinalIgnoreCase)).ToList()
        Dim tiles As New List(Of Control)
        If _openFolder <> "" AndAlso Not all.Any(Function(f) String.Equals(f.SeriesFolder, _openFolder, StringComparison.CurrentCultureIgnoreCase)) Then _openFolder = ""
        If words <> "" Then
            For Each g In DigitalLibrary.GroupBySeries(files).OrderBy(Function(x) x.Name, StringComparer.CurrentCultureIgnoreCase)
                tiles.AddRange(g.Files.Select(Function(f) DigitalTile(f, ShelfCover)))
            Next
        ElseIf _openFolder <> "" Then
            files = DigitalLibrary.InIssueOrder(files.Where(Function(f) String.Equals(f.SeriesFolder, _openFolder, StringComparison.CurrentCultureIgnoreCase)))
            tiles.AddRange(files.Select(Function(f) DigitalTile(f, ShelfCover)))
        Else
            For Each g In DigitalLibrary.GroupBySeries(files)
                tiles.Add(If(g.Files.Count = 1, DigitalTile(g.Files(0), ShelfCover), FolderTile(g, ShelfCover)))
            Next
        End If
        _folderBar.Visible = _openFolder <> "" AndAlso words = ""
        _folderTitle.Text = _openFolder
        _digitalFlow.SuspendLayout()
        For Each c In _digitalFlow.Controls.Cast(Of Control)().ToList()
            c.Dispose()
        Next
        _digitalFlow.Controls.Clear()
        If files.Count = 0 Then
            _digitalFlow.Controls.Add(New Label With {
                .AutoSize = True, .ForeColor = Theme.Muted, .Font = New Font("Segoe UI", 11.0F), .Margin = New Padding(4, 20, 0, 0),
                .Text = If(words <> "", "Nothing matches that search.",
                           "No digital comics yet." & vbCrLf & vbCrLf &
                           "Click Add comic files (or drag them here) to add CBZ, CBR, PDF or EPUB comics." & vbCrLf &
                           "They're copied into the app's own folder, so they're always here to read.")})
        Else
            _digitalFlow.Controls.AddRange(tiles.ToArray())
        End If
        _digitalFlow.ResumeLayout()
        If Not _addingDigital Then _digitalStatus.Text = If(files.Count = 0, "", $"{files.Count:N0} digital comic{If(files.Count = 1, "", "s")}")
    End Sub

    ''' <summary>A series folder: the cover of the issue you're up to, how many there are and how many you've read.</summary>
    Private Function FolderTile(g As DigitalGroup, size As Size) As Tile
        Dim unread = g.Files.Where(Function(f) f.LastRead = "").Count()
        Dim t As New Tile(size, "") With {
            .Caption = g.Name,
            .Detail = $"{g.Files.Count} comics  ·  {If(g.ReadCount = 0, "none read yet", $"{g.ReadCount} read")}",
            .Badge = If(unread = 0, "", $"{unread} NEW"),
            .Progress = If(g.ReadCount > 0, g.ReadCount / CDbl(g.Files.Count), -1),
            .Stacked = True, .Item = g, .OwnsPicture = True, .ContextMenuStrip = _folderMenu}
        AddHandler t.Click, Sub(s, e)
                                If TryCast(e, MouseEventArgs)?.Button <> MouseButtons.Right Then ShowFolder(g.Name)
                            End Sub
        LoadDigitalCover(t, g.UpTo, size)
        Return t
    End Function

    Private Sub OnRenameFolder(g As DigitalGroup)
        Dim answer = Ui.Ask(Me, "Rename folder", "Folder name:", g.Name)
        If String.IsNullOrWhiteSpace(answer) Then Return
        _db.SetDigitalFolder(g.Files.Select(Function(f) f.Id), answer)
        If String.Equals(_openFolder, g.Name, StringComparison.CurrentCultureIgnoreCase) Then _openFolder = answer.Trim()
        RefreshDigital()
    End Sub

    Private Sub OnMoveDigital(f As DigitalFile)
        Dim folders = _db.DigitalFiles().Select(Function(x) x.SeriesFolder).Distinct(StringComparer.CurrentCultureIgnoreCase).
            OrderBy(Function(x) x, StringComparer.CurrentCultureIgnoreCase).ToList()
        Dim answer = Ui.Ask(Me, "Move to folder", "Folder (pick one or type a new name; leave it empty to go by the comic's name):", f.SeriesFolder, folders)
        If answer Is Nothing Then Return
        Dim auto = DigitalLibrary.SeriesFromTitle(f.Title)
        _db.SetDigitalFolder({f.Id}, If(String.Equals(answer.Trim(), auto, StringComparison.CurrentCultureIgnoreCase), "", answer))
        RefreshDigital()
    End Sub

    Private Function DigitalTile(f As DigitalFile, size As Size) As Tile
        Dim t As New Tile(size, f.Kind) With {
            .Caption = If(f.Title <> "", f.Title, Path.GetFileNameWithoutExtension(f.FilePath)),
            .Detail = f.ProgressText & If(f.ComicLabel <> "", "  ·  in your collection", ""),
            .Badge = If(f.LastRead = "", "NEW", If(f.Finished, "READ", "")),
            .Progress = If(f.LastRead <> "" AndAlso f.Pages > 0 AndAlso Not f.Finished, (f.Page + 1) / CDbl(f.Pages), -1),
            .Item = f, .OwnsPicture = True, .ContextMenuStrip = _digitalMenu}
        AddHandler t.Click, Sub(s, e)
                                If TryCast(e, MouseEventArgs)?.Button <> MouseButtons.Right Then OpenDigital(f)
                            End Sub
        LoadDigitalCover(t, f, size)
        Return t
    End Function

    Private Async Sub LoadDigitalCover(t As Tile, f As DigitalFile, size As Size)
        Dim img = Await DigitalCoverAsync(f, size)
        If img Is Nothing Then Return
        If t.IsDisposed Then
            img.Dispose()
            Return
        End If
        t.Picture = img
    End Sub

    Private Shared ReadOnly Property DigitalCoversFolder As String
        Get
            Return Path.Combine(AppSettings.Folder, "digital-covers")
        End Get
    End Property

    ''' <summary>The first page of a digital comic as a cover, made once and kept in Comic Catalog\digital-covers.</summary>
    Private Shared Function DigitalCoverAsync(f As DigitalFile, size As Size) As Task(Of Image)
        Dim file = Path.Combine(DigitalCoversFolder, $"{f.Id}.jpg")
        Return Task.Run(Function() As Image
                            Try
                                If Not IO.File.Exists(file) Then
                                    DigitalCoverGate.Wait()
                                    Try
                                        If Not IO.File.Exists(file) Then
                                            Using book = ComicBook.Open(f.FilePath), first = ReaderCanvas.Decode(book.GetPage(0)),
                                                  thumb = CoverCache.Fit(first, New Size(300, 450))
                                                Directory.CreateDirectory(DigitalCoversFolder)
                                                thumb.Save(file, ImageFormat.Jpeg)
                                            End Using
                                        End If
                                    Finally
                                        DigitalCoverGate.Release()
                                    End Try
                                End If
                                Using ms As New MemoryStream(IO.File.ReadAllBytes(file)), src = Image.FromStream(ms)
                                    Return CoverCache.Fit(src, size)
                                End Using
                            Catch ex As Exception
                                Return Nothing
                            End Try
                        End Function)
    End Function

    Private Sub OnAddDigitalFiles(sender As Object, e As EventArgs)
        Using dlg As New OpenFileDialog With {.Filter = ComicBook.FileFilter, .Multiselect = True, .Title = "Pick digital comics to add"}
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            AddDigital(dlg.FileNames.ToList(), Nothing)
        End Using
    End Sub

    Private Sub OnAddDigitalFolder(sender As Object, e As EventArgs)
        Using dlg As New FolderBrowserDialog With {.Description = "Pick a folder of digital comics (folders inside it are included)", .UseDescriptionForTitle = True}
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            Dim files = ExpandComicFiles({dlg.SelectedPath})
            If files.Count = 0 Then
                Ui.ShowError(Me, "No CBZ, CBR, PDF or EPUB files in that folder.")
                Return
            End If
            Dim bytes = files.Sum(Function(f) New FileInfo(f).Length)
            If MessageBox.Show(Me, $"Add {files.Count:N0} comic{If(files.Count = 1, "", "s")} ({bytes / 1024.0 / 1024 / 1024:0.0} GB)?" & vbCrLf & vbCrLf &
                               $"A copy of each goes in {_settings.DigitalFolderOrDefault()}, so they're always here to read even if the originals move.",
                               "Add digital comics", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) <> DialogResult.OK Then Return
            AddDigital(files, Nothing)
        End Using
    End Sub

    ''' <summary>The comic files among some files and folders (folders searched all the way down).</summary>
    Private Shared Function ExpandComicFiles(paths As IEnumerable(Of String)) As List(Of String)
        Dim found As New List(Of String)
        For Each p In paths
            If Directory.Exists(p) Then
                Try
                    found.AddRange(Directory.EnumerateFiles(p, "*", SearchOption.AllDirectories).
                                   Where(Function(f) ComicBook.Extensions.Contains(Path.GetExtension(f).ToLowerInvariant())).OrderBy(Function(f) f))
                Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
                End Try
            ElseIf File.Exists(p) Then
                found.Add(p)
            End If
        Next
        Return found
    End Function

    ''' <summary>
    ''' Copies comic files into the app's folder and puts them on the shelf, matched to comics you own by
    ''' their names. With forComic, they're all linked to that comic. One file added is opened straight away.
    ''' </summary>
    Private Async Sub AddDigital(files As List(Of String), forComic As Long?)
        If files.Count = 0 Then Return
        If _addingDigital Then
            Ui.ShowError(Me, "Still adding the last lot. Try again when it's finished.")
            Return
        End If
        _addingDigital = True
        Dim folder = _settings.DigitalFolderOrDefault()
        Dim added As New List(Of Long)
        Dim matched = 0
        Dim problems As New List(Of String)
        Try
            For i = 0 To files.Count - 1
                Dim source = files(i)
                _digitalStatus.Text = $"Adding {i + 1:N0} of {files.Count:N0}: {Path.GetFileName(source)}"
                Try
                    Dim info = Await Task.Run(Function()
                                                  Dim kept = DigitalLibrary.CopyIn(source, folder)
                                                  Using book = ComicBook.Open(kept)
                                                      Return (Path:=kept, Kind:=book.Kind, Pages:=book.PageCount)
                                                  End Using
                                              End Function)
                    Dim comicId = If(forComic, _db.GuessComicForFile(source))
                    If comicId.HasValue Then matched += 1
                    added.Add(_db.AddDigitalFile(info.Path, DigitalLibrary.TitleFromFileName(source), info.Kind, info.Pages, comicId))
                Catch ex As Exception
                    problems.Add($"{Path.GetFileName(source)}: {Ui.Friendly(ex)}")
                End Try
            Next
        Finally
            _addingDigital = False
        End Try
        If IsDisposed Then Return
        If _pages(Array.IndexOf(PageNames, "Digital")).Visible Then RefreshDigital()
        _digitalStatus.Text = $"Added {added.Count:N0}" & If(matched > 0 AndAlso Not forComic.HasValue, $" ({matched:N0} matched to comics you own)", "") & "."
        If problems.Count > 0 Then
            Ui.ShowError(Me, $"{problems.Count:N0} couldn't be added:" & vbCrLf & vbCrLf & String.Join(vbCrLf, problems.Take(12)) &
                         If(problems.Count > 12, vbCrLf & $"…and {problems.Count - 12:N0} more", ""))
        End If
        If added.Count = 1 AndAlso files.Count = 1 Then
            Dim f = _db.DigitalFileById(added(0))
            If f IsNot Nothing Then OpenDigital(f)
        End If
    End Sub

    ''' <summary>
    ''' Opens the reader where you left off and remembers the place. Finishing a comic marks the paper copy in your
    ''' collection read (when it's linked to one), and "Next issue" carries straight on with the next in its folder.
    ''' </summary>
    Private Sub OpenDigital(f As DigitalFile)
        Dim current = f
        Do While current IsNot Nothing
            current = ReadDigital(current)
        Loop
        If _pages(Array.IndexOf(PageNames, "Digital")).Visible Then RefreshDigital()
        If _pages(Array.IndexOf(PageNames, "Home")).Visible Then RefreshHome()
    End Sub

    ''' <summary>Tells the phone where you got to (quietly; it's sent again next time if the relay can't be reached).</summary>
    Private Async Sub SendReadingPlaces()
        If _db Is Nothing OrElse _settings.SyncCode = "" OrElse _settings.RelayUrl = "" Then Return
        Try
            Dim places = Await Metron.SyncPlacesAsync(_settings.SyncCode, _db.ReadingPlaces())
            If places IsNot Nothing AndAlso Not IsDisposed Then TakeUpReadingPlaces(places)
        Catch ex As Exception
        End Try
    End Sub

    Private _pullingPlaces As Boolean

    ''' <summary>Picks up where you got to on the phone. Runs with the check for phone scans every couple of minutes.</summary>
    Private Async Sub PullReadingPlaces()
        If _pullingPlaces OrElse _db Is Nothing OrElse _settings.SyncCode = "" OrElse _settings.RelayUrl = "" Then Return
        If _db.DigitalFiles().Count = 0 Then Return
        _pullingPlaces = True
        Try
            Dim places = Await Metron.SyncPlacesAsync(_settings.SyncCode)
            If places IsNot Nothing AndAlso Not IsDisposed Then TakeUpReadingPlaces(places)
        Catch ex As Exception
        Finally
            _pullingPlaces = False
        End Try
    End Sub

    Private Sub TakeUpReadingPlaces(places As Dictionary(Of String, ReadingPlace))
        Dim moved = _db.ApplyReadingPlaces(places)
        If moved.Count = 0 Then Return
        ' Comics finished on the phone count as read here too.
        Dim markedRead = False
        For Each f In moved
            If _db.MarkPaperCopyRead(f.Id) Then markedRead = True
        Next
        If markedRead Then RefreshCollection()
        If _pages(Array.IndexOf(PageNames, "Digital")).Visible Then RefreshDigital()
        If _pages(Array.IndexOf(PageNames, "Home")).Visible Then RefreshHome()
    End Sub

    ''' <summary>Reads one comic. Returns the next issue when you asked to go on to it, otherwise Nothing.</summary>
    Private Function ReadDigital(f As DigitalFile) As DigitalFile
        If Not File.Exists(f.FilePath) Then
            If MessageBox.Show(Me, $"The file isn't there any more:{vbCrLf}{f.FilePath}{vbCrLf}{vbCrLf}Take it off the shelf?", "Digital comics",
                               MessageBoxButtons.YesNo, MessageBoxIcon.Warning) = DialogResult.Yes Then
                _db.RemoveDigitalFile(f.Id)
            End If
            Return Nothing
        End If
        Dim book As ComicBook
        Try
            book = ComicBook.Open(f.FilePath)
        Catch ex As Exception
            Ui.ShowError(Me, $"Couldn't open {Path.GetFileName(f.FilePath)}: {Ui.Friendly(ex)}")
            Return Nothing
        End Try
        Dim start = If(f.Finished, 0, f.Page)
        Dim title = If(f.Title <> "", f.Title, Path.GetFileNameWithoutExtension(f.FilePath))
        Dim nextOne = DigitalLibrary.NextInSeries(_db.DigitalFiles(), f)
        Dim reachedEnd As Boolean, wantsNext As Boolean
        Using reader As New ReaderForm(book, title, start, _settings, If(nextOne?.Title, ""))
            reader.ShowDialog(Me)
            _db.SaveReadingPlace(f.Id, If(reader.WantsNext, reader.PageCount - 1, reader.CurrentPage), reader.PageCount)
            reachedEnd = reader.ReachedEnd OrElse reader.WantsNext
            wantsNext = reader.WantsNext
        End Using
        SendReadingPlaces()
        If reachedEnd AndAlso _db.MarkPaperCopyRead(f.Id) Then RefreshCollection()
        Return If(wantsNext, nextOne, Nothing)
    End Function

    Private Sub OnLinkDigital(f As DigitalFile)
        Dim labels = _db.CollectionLabels()
        Dim current = labels.FirstOrDefault(Function(kv) f.ComicId.HasValue AndAlso kv.Value = f.ComicId.Value).Key
        Dim answer = Ui.Ask(Me, "Link to a comic", "Which comic in your collection is this? Start typing to find it. Leave it blank to unlink.",
                            If(current, ""), labels.Keys)
        If answer Is Nothing Then Return
        If answer = "" Then
            _db.LinkDigitalFile(f.Id, Nothing)
        Else
            Dim id As Long
            If Not labels.TryGetValue(answer, id) Then
                Ui.ShowError(Me, "Pick a comic from the list (it only has comics in your collection).")
                Return
            End If
            _db.LinkDigitalFile(f.Id, id)
        End If
        RefreshDigital()
    End Sub

    Private Sub OnRenameDigital(f As DigitalFile)
        Dim answer = Ui.Ask(Me, "Rename", "Name shown on the shelf:", f.Title)
        If String.IsNullOrWhiteSpace(answer) Then Return
        _db.RenameDigitalFile(f.Id, answer)
        RefreshDigital()
    End Sub

    Private Sub OnRemoveDigital(f As DigitalFile)
        Dim inFolder = String.Equals(Path.GetDirectoryName(f.FilePath), Path.GetFullPath(_settings.DigitalFolderOrDefault()).TrimEnd(Path.DirectorySeparatorChar),
                                     StringComparison.OrdinalIgnoreCase)
        Dim answer = MessageBox.Show(Me, $"Take {f.Title} off the shelf?" &
                                     If(inFolder, vbCrLf & vbCrLf & "Its copy in the app's folder is deleted too (the original you added from isn't touched).", ""),
                                     "Remove", MessageBoxButtons.OKCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2)
        If answer <> DialogResult.OK Then Return
        _db.RemoveDigitalFile(f.Id)
        Try
            If inFolder AndAlso File.Exists(f.FilePath) Then File.Delete(f.FilePath)
            Dim cover = Path.Combine(DigitalCoversFolder, $"{f.Id}.jpg")
            If File.Exists(cover) Then File.Delete(cover)
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
        End Try
        RefreshDigital()
    End Sub

    Private Sub OnChooseDigitalFolder(sender As Object, e As EventArgs)
        Using dlg As New FolderBrowserDialog With {.Description = "Pick a folder for digital comics", .UseDescriptionForTitle = True,
                                                   .SelectedPath = _settings.DigitalFolderOrDefault()}
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            _settings.DigitalFolder = dlg.SelectedPath
            _settings.Save()
            ShowDigitalFolder()
        End Using
    End Sub

    Private Sub ShowDigitalFolder()
        _digitalFolderLabel.Text = $"Digital comics are kept in {_settings.DigitalFolderOrDefault()}"
    End Sub

    ' From the Collection page: the selected comic's digital copy.
    Private Sub OnReadDigitalCopy(sender As Object, e As EventArgs)
        Dim ids = PickedComics()
        If ids Is Nothing Then Return
        Dim f = _db.DigitalForComic(ids(0))
        If f Is Nothing Then
            If MessageBox.Show(Me, "There's no digital copy of that comic yet. Add one now?", "Read digital copy",
                               MessageBoxButtons.YesNo, MessageBoxIcon.Question) = DialogResult.Yes Then OnAddDigitalCopy(sender, e)
            Return
        End If
        OpenDigital(f)
    End Sub

    Private Sub OnAddDigitalCopy(sender As Object, e As EventArgs)
        Dim ids = PickedComics()
        If ids Is Nothing Then Return
        Using dlg As New OpenFileDialog With {.Filter = ComicBook.FileFilter, .Title = $"Pick the digital copy of {_db.GetComic(ids(0))}"}
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            AddDigital(New List(Of String) From {dlg.FileName}, ids(0))
        End Using
    End Sub

    ' ---------- Videos ----------

    Private Function BuildVideosTab() As Control
        Dim page As New Panel()
        Dim bar As New FlowLayoutPanel With {.Dock = DockStyle.Top, .AutoSize = True, .Padding = New Padding(0, 0, 0, 8)}
        Dim check = Ui.MakeButton("Check for new videos", Sub(s, e) CheckVideos(quiet:=False), Theme.PrimaryTag)
        Dim save = Ui.MakeButton("Save channels", AddressOf OnSaveChannels)
        bar.Controls.AddRange({check, New Label With {.Width = 16}, Ui.MakeLabel("YouTube channels"), _videoChannels, save, _videoStatus})
        Icons.Put(check, Icons.Refresh)
        Icons.Put(save, Icons.Save)
        _videoChannels.Font = New Font("Segoe UI", 11.0F)
        page.Controls.Add(_videoFlow)
        page.Controls.Add(bar)
        AddHandler _videoTimer.Tick, Sub(s, e)
                                         _videoTimer.Interval = 60 * 60 * 1000
                                         If Not _settings.LastVideoCheck.HasValue OrElse DateTime.Now - _settings.LastVideoCheck.Value > TimeSpan.FromMinutes(55) Then
                                             CheckVideos(quiet:=True)
                                         End If
                                     End Sub
        Return page
    End Function

    ''' <summary>Called once the database is open: shows the channels and starts the hourly check.</summary>
    Private Sub StartVideos()
        _videoChannels.Text = String.Join(", ", ChannelList())
        ShowDigitalFolder()
        UpdateVideosButton()
        _videoTimer.Interval = 20 * 1000
        _videoTimer.Start()
    End Sub

    Private Function ChannelList() As List(Of String)
        Return If(_settings.VideoChannels, "").Split({","c, ";"c, ControlChars.Lf, ControlChars.Cr, " "c}, StringSplitOptions.RemoveEmptyEntries).
            Select(Function(c) YouTubeFeed.CleanChannel(c)).Where(Function(c) c <> "").Distinct(StringComparer.OrdinalIgnoreCase).ToList()
    End Function

    Private Sub OnSaveChannels(sender As Object, e As EventArgs)
        Dim typed = _videoChannels.Text.Split({","c, ";"c, " "c}, StringSplitOptions.RemoveEmptyEntries).
            Select(Function(c) YouTubeFeed.CleanChannel(c)).ToList()
        If typed.Any(Function(c) c = "") Then
            Ui.ShowError(Me, "Type each channel as its @name, like @comictom101, or paste its YouTube link. Put commas between them.")
            Return
        End If
        Dim before = ChannelList()
        _settings.VideoChannels = String.Join(", ", typed.Distinct(StringComparer.OrdinalIgnoreCase))
        _settings.Save()
        _videoChannels.Text = _settings.VideoChannels
        ' Videos from channels you've taken off are cleared, then the rest are fetched again.
        If before.Except(ChannelList(), StringComparer.OrdinalIgnoreCase).Any() Then
            For Each gone In before.Except(ChannelList(), StringComparer.OrdinalIgnoreCase)
                _settings.ChannelIds.Remove(gone)
            Next
            _settings.Save()
            _db.ClearVideos()
        End If
        CheckVideos(quiet:=False)
    End Sub

    ''' <summary>
    ''' Gets the newest videos of each channel. A channel's first check only fills the page; after that,
    ''' new uploads are counted on the Videos button. Runs every hour while the app is open.
    ''' </summary>
    Private Async Sub CheckVideos(quiet As Boolean)
        If _checkingVideos OrElse _db Is Nothing Then Return
        _checkingVideos = True
        Dim problems As New List(Of String)
        Dim added = 0
        Try
            _videoStatus.Text = "Checking…"
            For Each channel In ChannelList()
                Try
                    Dim id As String = Nothing
                    Dim firstLook = Not _settings.ChannelIds.TryGetValue(channel, id)
                    If firstLook Then
                        id = Await YouTubeFeed.ResolveChannelIdAsync(WebHttp, channel)
                        _settings.ChannelIds(channel) = id
                        _settings.Save()
                    End If
                    Dim videos = Await YouTubeFeed.LatestAsync(WebHttp, id)
                    added += _db.SaveVideos(videos, markSeen:=firstLook)
                Catch ex As Exception
                    problems.Add($"{channel}: {Ui.Friendly(ex)}")
                End Try
            Next
            _settings.LastVideoCheck = DateTime.Now
            _settings.Save()
        Finally
            _checkingVideos = False
        End Try
        If IsDisposed Then Return
        _videoStatus.Text = If(problems.Count > 0, "Couldn't check " & String.Join("; ", problems),
                               $"Checked at {DateTime.Now:t}." & If(added > 0, $" {added} new.", ""))
        If problems.Count > 0 AndAlso Not quiet Then Ui.ShowError(Me, String.Join(vbCrLf, problems))
        UpdateVideosButton()
        If _pages(Array.IndexOf(PageNames, "Videos")).Visible Then RefreshVideos()
        If _pages(Array.IndexOf(PageNames, "Home")).Visible AndAlso added > 0 Then RefreshHome()
    End Sub

    Private Sub UpdateVideosButton()
        If _db Is Nothing Then Return
        Dim n = _db.NewVideoCount()
        Dim index = Array.IndexOf(PageNames, "Videos")
        _navButtons(index).Text = If(n = 0, "  Videos", $"  Videos ({n} new)")
    End Sub

    Private Sub RefreshVideos()
        If _db Is Nothing Then Return
        Dim videos = _db.Videos(60)
        _videoFlow.SuspendLayout()
        For Each c In _videoFlow.Controls.Cast(Of Control)().ToList()
            c.Dispose()
        Next
        _videoFlow.Controls.Clear()
        If videos.Count = 0 Then
            _videoFlow.Controls.Add(New Label With {
                .AutoSize = True, .ForeColor = Theme.Muted, .Font = New Font("Segoe UI", 11.0F), .Margin = New Padding(4, 20, 0, 0),
                .Text = If(_checkingVideos, "Getting the newest videos…",
                           "No videos yet. Click Check for new videos. New episodes appear here by themselves while the app is open.")})
        Else
            _videoFlow.Controls.AddRange(videos.Select(Function(v) VideoTile(v)).Cast(Of Control)().ToArray())
        End If
        _videoFlow.ResumeLayout()
    End Sub

    Private Function VideoTile(v As VideoItem) As Tile
        Dim t As New Tile(VideoThumb, "▶") With {
            .Caption = v.Title,
            .Detail = $"{v.Channel}  ·  {AgoText(v.Published)}",
            .Badge = If(v.Seen, "", "NEW"), .Item = v, .OwnsPicture = True}
        AddHandler t.Click, Sub(s, e) Process.Start(New ProcessStartInfo With {.FileName = v.Url, .UseShellExecute = True})
        LoadVideoThumb(t, v)
        Return t
    End Function

    Private Shared Function AgoText(posted As DateTimeOffset) As String
        If posted = DateTimeOffset.MinValue Then Return ""
        Dim span = DateTimeOffset.Now - posted
        If span.TotalHours < 1 Then Return "just now"
        If span.TotalHours < 24 Then Return $"{CInt(span.TotalHours)} hour{If(CInt(span.TotalHours) = 1, "", "s")} ago"
        If span.TotalDays < 2 Then Return "yesterday"
        If span.TotalDays < 30 Then Return $"{CInt(span.TotalDays)} days ago"
        Return posted.LocalDateTime.ToString("d MMM yyyy", CultureInfo.CurrentCulture)
    End Function

    Private Async Sub LoadVideoThumb(t As Tile, v As VideoItem)
        Dim img = Await VideoThumbAsync(v)
        If img Is Nothing Then Return
        If t.IsDisposed Then
            img.Dispose()
            Return
        End If
        t.Picture = img
    End Sub

    ''' <summary>A video's picture, downloaded once to Comic Catalog\video-thumbs, cropped to 16:9.</summary>
    Private Shared Async Function VideoThumbAsync(v As VideoItem) As Task(Of Image)
        If v.Thumbnail = "" Then Return Nothing
        Dim folder = Path.Combine(AppSettings.Folder, "video-thumbs")
        Dim file = Path.Combine(folder, $"{v.VideoId}.jpg")
        Try
            If Not IO.File.Exists(file) Then
                Dim bytes = Await WebHttp.GetByteArrayAsync(v.Thumbnail)
                Directory.CreateDirectory(folder)
                Await IO.File.WriteAllBytesAsync(file, bytes)
            End If
            Return Await Task.Run(Function() As Image
                                      Using ms As New MemoryStream(IO.File.ReadAllBytes(file)), src = Image.FromStream(ms)
                                          Return Crop(src, VideoThumb)
                                      End Using
                                  End Function)
        Catch ex As Exception
            Return Nothing
        End Try
    End Function

    ''' <summary>Fills the size, cutting off the edges (YouTube's pictures have black bars top and bottom).</summary>
    Private Shared Function Crop(src As Image, size As Size) As Image
        Dim bmp As New Bitmap(size.Width, size.Height)
        Dim want = size.Width / CDbl(size.Height)
        Dim sw = src.Width, sh = CInt(src.Width / want)
        If sh > src.Height Then
            sh = src.Height
            sw = CInt(sh * want)
        End If
        Using g = Graphics.FromImage(bmp)
            g.InterpolationMode = Drawing2D.InterpolationMode.HighQualityBicubic
            g.PixelOffsetMode = Drawing2D.PixelOffsetMode.HighQuality
            g.DrawImage(src, New Rectangle(0, 0, size.Width, size.Height), New Rectangle((src.Width - sw) \ 2, (src.Height - sh) \ 2, sw, sh), GraphicsUnit.Pixel)
        End Using
        Return bmp
    End Function

    ''' <summary>Set by the build's screenshot step: opens the reader on a made-up comic in this mode ("page", "two" or "webtoon"), or fills the Digital shelf ("shelf", or "folder" to open a series folder), or shows a sample shop receipt ("receipt").</summary>
    Public Property ReaderSample As String = ""

    Private Sub OpenSampleReader(mode As String)
        If mode = "receipt" Then
            ' A made-up shop receipt, as Add from a shop receipt shows it.
            Dim sampleReceipt = ShopReceipts.Parse(String.Join(vbLf, {
                "Sample Comics", "Order No: 123456", "Order Date:09 Oct, 2026", "Item Quantity Points Price Final Price",
                "Sample Hero #4 Cover A Jane Artist (Mature)", "1 2.60 $ 9.95 $ 9.95",
                "Another Story #2 Cover B Sam Painter (Big Event)", "2 3.12 $ 11.95 $ 23.90",
                "Sample Hero Volume 1 Graphic Novel 1 9.38 $ 35.95 $ 35.95",
                "Sub Total $ 60.83", "Sales Tax $ 9.12", "Total $ 69.80"}))
            Dim form As New ReceiptForm(_db, Metron, sampleReceipt)
            form.Show(Me)
            Return
        End If
        If mode = "shelf" OrElse mode = "folder" Then
            ' A shelf with a series folder (one issue read) and a comic on its own.
            Dim folder = Path.Combine(Path.GetTempPath(), "Comic Catalog shelf")
            Directory.CreateDirectory(folder)
            For Each sample In {"Sample Hero v1 003 (1964)", "Sample Hero v1 001 (1964)", "Sample Hero v1 002 (1964)", "Sample Hero v1 010 (1964)", "Another Story 1"}
                Dim f = Path.Combine(folder, sample & ".cbz")
                MakeSampleComic(f)
                Dim id = _db.AddDigitalFile(f, sample, "CBZ", 6)
                If sample.Contains("001") Then _db.SaveReadingPlace(id, 5, 6)
            Next
            ShowPage("Digital")
            If mode = "folder" Then ShowFolder("Sample Hero v1 (1964)")
            Return
        End If
        Dim file = Path.Combine(Path.GetTempPath(), "Comic Catalog sample.cbz")
        MakeSampleComic(file)
        _settings.ReaderMode = mode
        Dim reader As New ReaderForm(ComicBook.Open(file), "Sample comic", 1, _settings)
        reader.Show(Me)
    End Sub

    Private Shared Sub MakeSampleComic(file As String)
        If IO.File.Exists(file) Then IO.File.Delete(file)
        Dim colors = {Color.FromArgb(200, 60, 50), Color.FromArgb(40, 90, 170), Color.FromArgb(230, 180, 40), Color.FromArgb(50, 140, 90)}
        Using zip = IO.Compression.ZipFile.Open(file, IO.Compression.ZipArchiveMode.Create)
            For i = 0 To 5
                Using bmp As New Bitmap(800, 1230), g = Graphics.FromImage(bmp), big As New Font("Segoe UI Black", 72), small As New Font("Segoe UI", 20)
                    g.Clear(Color.White)
                    Using b As New SolidBrush(colors(i Mod colors.Length))
                        g.FillRectangle(b, 40, 40, 720, 520)
                        g.FillRectangle(b, 40, 600, 340, 590)
                        g.FillRectangle(b, 420, 600, 340, 590)
                    End Using
                    g.DrawString($"PAGE {i + 1}", big, Brushes.White, 80, 200)
                    g.DrawString("Sample comic for the screenshot", small, Brushes.White, 90, 340)
                    Using ms As New MemoryStream()
                        bmp.Save(ms, ImageFormat.Png)
                        Using entry = zip.CreateEntry($"page{i + 1:00}.png").Open()
                            ms.Position = 0
                            ms.CopyTo(entry)
                        End Using
                    End Using
                End Using
            Next
        End Using
    End Sub

    ' ---------- bigger covers ----------

    Private Sub SetupCoverSizes()
        _coverSize.Items.AddRange({"Small covers", "Medium covers", "Large covers"})
        _coverSize.SelectedIndex = Math.Max(0, Math.Min(2, _settings.CoverSize))
        ApplyCoverSize()
        AddHandler _coverSize.SelectedIndexChanged, Sub(s, e)
                                                        _settings.CoverSize = _coverSize.SelectedIndex
                                                        _settings.Save()
                                                        ApplyCoverSize()
                                                        SetView(True)
                                                    End Sub
    End Sub

    ''' <summary>Changes the size of the Covers view; covers come back quickly from the covers folder.</summary>
    Private Sub ApplyCoverSize()
        Dim size = CoverCache.Sizes(Math.Max(0, _coverSize.SelectedIndex))
        _coverImages.Images.Clear()
        _coverImages.ImageSize = size
        _coverImages.Images.Add(CoverCache.Placeholder(size))
        _coverIndex.Clear()
        _coverLoading.Clear()
        _covers.Invalidate()
    End Sub

End Class
