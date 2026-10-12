Option Strict On

Imports System.Diagnostics
Imports System.Drawing
Imports System.Globalization
Imports System.IO
Imports System.Windows.Forms
Imports ComicCatalog.Data

''' <summary>Makes a web page of your collection or wishlist to send to friends and family.</summary>
Public Class ShareForm
    Inherits Form

    Private ReadOnly _db As ComicDb
    Private ReadOnly _settings As AppSettings
    Private ReadOnly _title As New TextBox With {.Dock = DockStyle.Fill}
    Private ReadOnly _collection As New CheckBox With {.Text = "My collection", .AutoSize = True, .Checked = True}
    Private ReadOnly _wishlist As New CheckBox With {.Text = "My wishlist (handy for presents)", .AutoSize = True, .Checked = True}
    Private ReadOnly _covers As New CheckBox With {.Text = "Show covers", .AutoSize = True, .Checked = True}
    Private ReadOnly _values As New CheckBox With {.Text = "Show what comics are worth", .AutoSize = True}

    Public Sub New(db As ComicDb, settings As AppSettings, Optional wishlistOnly As Boolean = False)
        _db = db
        _settings = settings
        Text = "Make a share page"
        StartPosition = FormStartPosition.CenterParent
        FormBorderStyle = FormBorderStyle.FixedDialog
        MinimizeBox = False
        MaximizeBox = False
        ShowInTaskbar = False
        AutoScaleMode = AutoScaleMode.Font
        Font = New Font("Segoe UI", 9.5F)
        ClientSize = New Size(520, 360)
        _title.Text = If(settings.ShareTitle <> "", settings.ShareTitle, "My comics")
        _collection.Checked = Not wishlistOnly

        Dim help As New Label With {.Dock = DockStyle.Top, .AutoSize = False, .Height = 64, .Padding = New Padding(12, 10, 12, 0), .Tag = Theme.MutedTag,
            .Text = "Makes one web page file you can email or text to people, or share from OneDrive. It opens on any phone or computer. " &
                    "Nothing is put online, and it doesn't change as your collection does: make a new one to update it."}
        Dim grid As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 2, .Padding = New Padding(10, 4, 10, 4)}
        grid.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        grid.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        grid.Controls.Add(Ui.MakeLabel("Page title"))
        grid.Controls.Add(_title)
        Dim include As New FlowLayoutPanel With {.FlowDirection = FlowDirection.TopDown, .AutoSize = True, .WrapContents = False}
        include.Controls.AddRange({_collection, _wishlist, _covers, _values})
        grid.Controls.Add(Ui.MakeLabel("Include"))
        grid.Controls.Add(include)
        Dim save = Ui.MakeButton("Save the page…", AddressOf OnSave, Theme.PrimaryTag)
        Dim cancel = Ui.MakeButton("Cancel", Sub(s, e) DialogResult = DialogResult.Cancel)
        Dim buttons As New FlowLayoutPanel With {.Dock = DockStyle.Bottom, .FlowDirection = FlowDirection.RightToLeft, .AutoSize = True, .Padding = New Padding(6)}
        buttons.Controls.AddRange({cancel, save})
        AcceptButton = save
        CancelButton = cancel
        Controls.Add(grid)
        Controls.Add(help)
        Controls.Add(buttons)
        Theme.Apply(Me)
    End Sub

    Protected Overrides Sub OnHandleCreated(e As EventArgs)
        MyBase.OnHandleCreated(e)
        Theme.DarkTitleBar(Me)
    End Sub

    ''' <summary>OneDrive when this computer has it (easy to share a link from), otherwise Documents.</summary>
    Private Shared Function StartFolder() As String
        For Each envName In {"OneDriveConsumer", "OneDrive"}
            Dim oneDrive = Environment.GetEnvironmentVariable(envName)
            If Not String.IsNullOrEmpty(oneDrive) AndAlso Directory.Exists(oneDrive) Then Return oneDrive
        Next
        Return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
    End Function

    Private Sub OnSave(sender As Object, e As EventArgs)
        If Not _collection.Checked AndAlso Not _wishlist.Checked Then
            Ui.ShowError(Me, "Tick your collection, your wishlist, or both.")
            Return
        End If
        Dim title = If(_title.Text.Trim() = "", "My comics", _title.Text.Trim())
        Dim what = If(_collection.Checked AndAlso Not _wishlist.Checked, "collection", If(_wishlist.Checked AndAlso Not _collection.Checked, "wishlist", "comics"))
        Dim safeName = String.Concat(title.Where(Function(ch) Not Path.GetInvalidFileNameChars().Contains(ch)))
        Using dlg As New SaveFileDialog With {
            .Title = "Save the share page", .Filter = "Web page (*.html)|*.html",
            .FileName = $"{If(safeName <> "", safeName, "Comics")} {what} {Date.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.html",
            .InitialDirectory = StartFolder()}
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            Try
                Dim page = SharePage.Build(_db, New ShareOptions With {
                    .Title = title, .IncludeCollection = _collection.Checked, .IncludeWishlist = _wishlist.Checked,
                    .ShowCovers = _covers.Checked, .ShowValues = _values.Checked})
                File.WriteAllText(dlg.FileName, page, New System.Text.UTF8Encoding(False))
            Catch ex As Exception
                Ui.ShowError(Me, $"Couldn't save it: {ex.Message}")
                Return
            End Try
            _settings.ShareTitle = title
            Try
                _settings.Save()
            Catch ex As IOException
            End Try
            Dim answer = MessageBox.Show(Me, $"Saved {Path.GetFileName(dlg.FileName)}." & vbCrLf & vbCrLf &
                "To send it: attach the file to an email or message, or right-click it in OneDrive and choose Share." & vbCrLf & vbCrLf &
                "Open it now to have a look?", "Share page", MessageBoxButtons.YesNo, MessageBoxIcon.Information)
            If answer = DialogResult.Yes Then
                Try
                    Process.Start(New ProcessStartInfo(dlg.FileName) With {.UseShellExecute = True})
                Catch ex As Exception
                    Ui.ShowError(Me, $"Couldn't open it: {ex.Message}")
                End Try
            End If
            DialogResult = DialogResult.OK
        End Using
    End Sub

End Class
