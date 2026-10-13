Option Strict On

Imports System.Data
Imports System.Diagnostics
Imports System.Drawing
Imports System.Globalization
Imports System.IO
Imports System.Windows.Forms
Imports ComicCatalog.Data

''' <summary>Convention signing list: paste the guests, see which of your comics they made and where they are.</summary>
Public Class SigningForm
    Inherits Form

    Private ReadOnly _db As ComicDb
    Private ReadOnly _guests As New TextBox With {.Dock = DockStyle.Fill, .Multiline = True, .ScrollBars = ScrollBars.Vertical,
                                                  .PlaceholderText = "Paste the guest list here, one name per line or separated by commas"}
    Private ReadOnly _grid As DataGridView = Ui.MakeGrid()
    Private ReadOnly _status As New Label With {.AutoSize = True, .Tag = Theme.MutedTag, .Margin = New Padding(8, 10, 3, 3)}
    Private _rows As DataTable

    Public Sub New(db As ComicDb)
        _db = db
        Text = "Convention signing list"
        StartPosition = FormStartPosition.CenterParent
        MinimizeBox = False
        ShowInTaskbar = False
        AutoScaleMode = AutoScaleMode.Font
        Font = New Font("Segoe UI", 9.5F)
        ClientSize = New Size(960, 540)
        MinimumSize = New Size(700, 380)

        Dim help As New Label With {.Dock = DockStyle.Top, .AutoSize = False, .Height = 46, .Padding = New Padding(12, 8, 12, 0), .Tag = Theme.MutedTag,
            .Text = "Copy the guest list from the convention's website (the monthly convention update has the main comic guests). " &
                    "Writers and artists come from Metron, so comics still waiting for their details won't show up yet."}
        Dim find = Ui.MakeButton("Find my comics", Sub(s, e) Search(), Theme.PrimaryTag)
        Dim people = Ui.MakeButton("Who's in my collection?", AddressOf OnPeople)
        Dim leftButtons As New FlowLayoutPanel With {.Dock = DockStyle.Bottom, .AutoSize = True}
        leftButtons.Controls.AddRange({find, people})
        Dim left As New Panel With {.Dock = DockStyle.Fill, .Padding = New Padding(0, 0, 8, 0)}
        left.Controls.Add(_guests)
        left.Controls.Add(leftButtons)

        Dim split As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 2, .Padding = New Padding(12, 4, 12, 4)}
        split.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 30))
        split.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 70))
        split.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        split.Controls.Add(left, 0, 0)
        split.Controls.Add(_grid, 1, 0)

        Dim copy = Ui.MakeButton("Copy", AddressOf OnCopy)
        Dim pdf = Ui.MakeButton("Save as PDF to print…", AddressOf OnPdf)
        Dim close = Ui.MakeButton("Close", Sub(s, e) Me.Close())
        CancelButton = close
        Dim buttons As New FlowLayoutPanel With {.Dock = DockStyle.Bottom, .AutoSize = True, .Padding = New Padding(8)}
        buttons.Controls.AddRange({copy, pdf, close, _status})
        Controls.Add(split)
        Controls.Add(help)
        Controls.Add(buttons)
        Theme.Apply(Me)
    End Sub

    Protected Overrides Sub OnHandleCreated(e As EventArgs)
        MyBase.OnHandleCreated(e)
        Theme.DarkTitleBar(Me)
    End Sub

    Private Sub Search()
        Dim names = ComicDb.GuestNames(_guests.Text)
        If names.Count = 0 Then
            Ui.ShowError(Me, "Paste the guest names on the left first.")
            Return
        End If
        _rows = _db.SigningList(names)
        _grid.DataSource = _rows
        If _grid.Columns.Contains("Value") Then _grid.Columns("Value").DefaultCellStyle.Format = "N2"
        If _grid.Columns.Contains("Comic") Then _grid.Columns("Comic").FillWeight = 160
        Dim found = _rows.Rows.Cast(Of DataRow)().Select(Function(r) CStr(r("Guest"))).Distinct().Count()
        _status.Text = If(_rows.Rows.Count = 0, $"None of the {names.Count} guests made any of your comics (that Metron knows of).",
                          $"{_rows.Rows.Count:N0} comics by {found} of the {names.Count} guests.")
    End Sub

    Private Function HasList() As Boolean
        If _rows IsNot Nothing AndAlso _rows.Rows.Count > 0 Then Return True
        Ui.ShowError(Me, "Paste the guests and click Find my comics first.")
        Return False
    End Function

    Private Sub OnCopy(sender As Object, e As EventArgs)
        If Not HasList() Then Return
        Clipboard.SetText(Reports.SigningListText(_rows))
        MessageBox.Show(Me, "Copied. Paste it into Notes or a message to yourself.", "Signing list")
    End Sub

    Private Sub OnPdf(sender As Object, e As EventArgs)
        If Not HasList() Then Return
        Using dlg As New SaveFileDialog With {.Title = "Save the signing list", .Filter = "PDF (*.pdf)|*.pdf",
                                              .FileName = $"Signing list {Date.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.pdf",
                                              .InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)}
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            Try
                Reports.SigningListPdf(_rows, dlg.FileName, "Comics to get signed")
                Process.Start(New ProcessStartInfo(dlg.FileName) With {.UseShellExecute = True})
            Catch ex As Exception
                Ui.ShowError(Me, $"Couldn't save it: {Ui.Friendly(ex)}")
            End Try
        End Using
    End Sub

    ' The writers and artists you have most of, to help spot them on a guest list.
    Private Sub OnPeople(sender As Object, e As EventArgs)
        Dim top = _db.CreatorsYouOwn().Take(40).ToList()
        If top.Count = 0 Then
            Ui.ShowError(Me, "No writers or artists known yet. They fill in as the app looks up your comics in the background.")
            Return
        End If
        MessageBox.Show(Me, String.Join(vbCrLf, top.Select(Function(c) $"{c.Label} ({c.Count})")), "Who you have most comics by")
    End Sub

End Class
