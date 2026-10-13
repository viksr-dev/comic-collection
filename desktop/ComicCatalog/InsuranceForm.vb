Option Strict On

Imports System.Diagnostics
Imports System.Drawing
Imports System.Globalization
Imports System.IO
Imports System.Windows.Forms
Imports ComicCatalog.Data

''' <summary>Makes the insurance report: a PDF with covers, your photos, grades and values.</summary>
Public Class InsuranceForm
    Inherits Form

    Private ReadOnly _db As ComicDb
    Private ReadOnly _settings As AppSettings
    Private ReadOnly _owner As New TextBox With {.Dock = DockStyle.Fill, .PlaceholderText = "your name (optional)"}
    Private ReadOnly _minValue As New TextBox With {.Width = 90, .PlaceholderText = "0"}
    Private ReadOnly _covers As New CheckBox With {.Text = "Covers", .AutoSize = True, .Checked = True}
    Private ReadOnly _photos As New CheckBox With {.Text = "Your own photos", .AutoSize = True, .Checked = True}
    Private ReadOnly _status As New Label With {.AutoSize = True, .Tag = Theme.MutedTag, .Margin = New Padding(3, 10, 3, 3)}

    Public Sub New(db As ComicDb, settings As AppSettings)
        _db = db
        _settings = settings
        Text = "Insurance report"
        StartPosition = FormStartPosition.CenterParent
        FormBorderStyle = FormBorderStyle.FixedDialog
        MinimizeBox = False
        MaximizeBox = False
        ShowInTaskbar = False
        AutoScaleMode = AutoScaleMode.Font
        Font = New Font("Segoe UI", 9.5F)
        ClientSize = New Size(520, 330)
        _owner.Text = settings.InsuranceOwner

        Dim help As New Label With {.Dock = DockStyle.Top, .AutoSize = False, .Height = 58, .Padding = New Padding(12, 10, 12, 0), .Tag = Theme.MutedTag,
            .Text = "A PDF listing each comic with its cover, your photos, condition or grade, certificate number, what you paid and what it's worth. " &
                    "Keep a copy with your insurance papers, somewhere other than this computer."}
        Dim grid As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 2, .Padding = New Padding(10, 4, 10, 4)}
        grid.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        grid.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        Dim minRow As New FlowLayoutPanel With {.AutoSize = True, .WrapContents = False, .Margin = New Padding(0)}
        minRow.Controls.AddRange({_minValue, New Label With {.Text = "(leave blank for every comic)", .AutoSize = True, .Tag = Theme.MutedTag, .Margin = New Padding(6, 8, 3, 3)}})
        Dim pics As New FlowLayoutPanel With {.AutoSize = True, .WrapContents = False, .Margin = New Padding(0)}
        pics.Controls.AddRange({_covers, _photos})
        For Each pair In {("Owner", CType(_owner, Control)), ("Only comics worth at least $", minRow), ("Pictures", pics)}
            grid.Controls.Add(Ui.MakeLabel(pair.Item1))
            grid.Controls.Add(pair.Item2)
        Next
        grid.Controls.Add(New Label())
        grid.Controls.Add(_status)
        Dim save = Ui.MakeButton("Make the report…", AddressOf OnSave, Theme.PrimaryTag)
        Dim cancel = Ui.MakeButton("Close", Sub(s, e) DialogResult = DialogResult.Cancel)
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

    Private Async Sub OnSave(sender As Object, e As EventArgs)
        Dim minValue As Double?
        If Not Ui.ParseMoney(_minValue.Text, minValue) Then
            Ui.ShowError(Me, "The minimum value should be a number, like 20.")
            _minValue.Focus()
            Return
        End If
        Using dlg As New SaveFileDialog With {.Title = "Save the insurance report", .Filter = "PDF (*.pdf)|*.pdf",
                                              .FileName = $"Comic collection insurance {Date.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.pdf",
                                              .InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)}
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            _settings.InsuranceOwner = _owner.Text.Trim()
            Try
                _settings.Save()
            Catch ex As IOException
            End Try
            Dim options As New InsuranceOptions With {
                .MinValue = minValue.GetValueOrDefault(), .Owner = _owner.Text.Trim(), .IncludeCovers = _covers.Checked,
                .IncludePhotos = _photos.Checked, .CoversFolder = Path.Combine(AppSettings.Folder, "covers")}
            Dim file = dlg.FileName
            UseWaitCursor = True
            Enabled = False
            _status.Text = "Making the report. With a big collection this takes a minute or two…"
            Try
                Dim count = Await Task.Run(Function() Reports.InsurancePdf(_db, file, options))
                _status.Text = $"Done: {count:N0} comics."
                If MessageBox.Show(Me, $"Saved {Path.GetFileName(file)} with {count:N0} comics. Open it now?", "Insurance report",
                                   MessageBoxButtons.YesNo, MessageBoxIcon.Information) = DialogResult.Yes Then
                    Process.Start(New ProcessStartInfo(file) With {.UseShellExecute = True})
                End If
            Catch ex As IOException
                _status.Text = ""
                Ui.ShowError(Me, $"Couldn't save it. If that file is open in a PDF viewer, close it and try again. ({Ui.Friendly(ex)})")
            Catch ex As Exception
                _status.Text = ""
                Ui.ShowError(Me, $"Couldn't make the report: {Ui.Friendly(ex)}")
            Finally
                Enabled = True
                UseWaitCursor = False
            End Try
        End Using
    End Sub

End Class
