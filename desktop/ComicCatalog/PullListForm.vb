Option Strict On

Imports System.Diagnostics
Imports System.Data
Imports System.Drawing
Imports System.Globalization
Imports System.IO
Imports System.Windows.Forms
Imports ComicCatalog.Data

''' <summary>The list of upcoming issues to ask your comic shop to put aside: copy it, email it or print it.</summary>
Public Class PullListForm
    Inherits Form

    Private ReadOnly _db As ComicDb
    Private ReadOnly _settings As AppSettings
    Private ReadOnly _period As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = 220}
    Private ReadOnly _text As New TextBox With {.Dock = DockStyle.Fill, .Multiline = True, .ReadOnly = True, .ScrollBars = ScrollBars.Vertical,
                                               .Font = New Font("Segoe UI", 10.5F)}
    Private ReadOnly _shopEmail As New TextBox With {.Width = 240, .PlaceholderText = "shop's email (optional)"}
    Private _rows As DataTable
    Private ReadOnly _ranges As New List(Of (Name As String, From As Date, Until As Date))

    Public Sub New(db As ComicDb, settings As AppSettings)
        _db = db
        _settings = settings
        Text = "Pull list for your comic shop"
        StartPosition = FormStartPosition.CenterParent
        MinimizeBox = False
        ShowInTaskbar = False
        AutoScaleMode = AutoScaleMode.Font
        Font = New Font("Segoe UI", 9.5F)
        ClientSize = New Size(640, 520)
        MinimumSize = New Size(520, 400)

        Dim today = Date.Today
        Dim nextMonth = New Date(today.Year, today.Month, 1).AddMonths(1)
        _ranges.Add(("The next 4 weeks", today, today.AddDays(28)))
        _ranges.Add(($"Next month ({nextMonth.ToString("MMMM", CultureInfo.CurrentCulture)})", nextMonth, nextMonth.AddMonths(1).AddDays(-1)))
        _ranges.Add(("Everything coming up", today, today.AddYears(1)))
        _period.Items.AddRange(_ranges.Select(Function(r) CObj(r.Name)).ToArray())
        _period.SelectedIndex = 0
        AddHandler _period.SelectedIndexChanged, Sub(s, e) ShowList()
        _shopEmail.Text = settings.ShopEmail

        Dim top As New FlowLayoutPanel With {.Dock = DockStyle.Top, .AutoSize = True, .Padding = New Padding(8, 8, 8, 0)}
        top.Controls.AddRange({Ui.MakeLabel("Issues in shops"), _period})
        Dim hint As New Label With {.Dock = DockStyle.Top, .AutoSize = False, .Height = 40, .Padding = New Padding(12, 4, 12, 0), .Tag = Theme.MutedTag,
            .Text = "New issues of the series you follow that you don't have yet. Click Check for new releases first to bring it up to date."}
        Dim body As New Panel With {.Dock = DockStyle.Fill, .Padding = New Padding(12, 4, 12, 4)}
        body.Controls.Add(_text)

        Dim copy = Ui.MakeButton("Copy", AddressOf OnCopy, Theme.PrimaryTag)
        Dim email = Ui.MakeButton("Email it…", AddressOf OnEmail)
        Dim print = Ui.MakeButton("Save as PDF to print…", AddressOf OnPdf)
        Dim close = Ui.MakeButton("Close", Sub(s, e) Me.Close())
        CancelButton = close
        Dim buttons As New FlowLayoutPanel With {.Dock = DockStyle.Bottom, .AutoSize = True, .Padding = New Padding(8)}
        buttons.Controls.AddRange({copy, _shopEmail, email, print, close})
        Controls.Add(body)
        Controls.Add(hint)
        Controls.Add(top)
        Controls.Add(buttons)
        Theme.Apply(Me)
        ShowList()
    End Sub

    Protected Overrides Sub OnHandleCreated(e As EventArgs)
        MyBase.OnHandleCreated(e)
        Theme.DarkTitleBar(Me)
    End Sub

    Private Sub ShowList()
        Dim range = _ranges(Math.Max(0, _period.SelectedIndex))
        _rows = _db.PullList(range.From, range.Until)
        _text.Text = If(_rows.Rows.Count = 0,
            "Nothing found for those dates." & vbCrLf & vbCrLf & "Check for new releases on the New releases page, or pick Series to follow there.",
            Reports.PullListText(_rows).Replace(vbLf, vbCrLf).Replace(vbCr & vbCrLf, vbCrLf))
    End Sub

    Private Function HasList() As Boolean
        If _rows IsNot Nothing AndAlso _rows.Rows.Count > 0 Then Return True
        Ui.ShowError(Me, "There's nothing on the list for those dates.")
        Return False
    End Function

    Private Sub OnCopy(sender As Object, e As EventArgs)
        If Not HasList() Then Return
        Clipboard.SetText(_text.Text)
        MessageBox.Show(Me, "Copied. Paste it into an email, a text or Messenger to your shop.", "Pull list")
    End Sub

    Private Sub OnEmail(sender As Object, e As EventArgs)
        If Not HasList() Then Return
        _settings.ShopEmail = _shopEmail.Text.Trim()
        Try
            _settings.Save()
        Catch ex As IOException
        End Try
        Dim url = $"mailto:{Uri.EscapeDataString(_shopEmail.Text.Trim())}?subject={Uri.EscapeDataString("Pull list")}&body={Uri.EscapeDataString(_text.Text)}"
        Try
            Process.Start(New ProcessStartInfo(url) With {.UseShellExecute = True})
        Catch ex As Exception
            Ui.ShowError(Me, $"Couldn't open your email app, so use Copy and paste it instead. ({Ui.Friendly(ex)})")
        End Try
    End Sub

    Private Sub OnPdf(sender As Object, e As EventArgs)
        If Not HasList() Then Return
        Using dlg As New SaveFileDialog With {.Title = "Save the pull list", .Filter = "PDF (*.pdf)|*.pdf",
                                              .FileName = $"Pull list {Date.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.pdf",
                                              .InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)}
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            Try
                Reports.PullListPdf(_rows, dlg.FileName, "Pull list")
                Process.Start(New ProcessStartInfo(dlg.FileName) With {.UseShellExecute = True})
            Catch ex As Exception
                Ui.ShowError(Me, $"Couldn't save it: {Ui.Friendly(ex)}")
            End Try
        End Using
    End Sub

End Class
