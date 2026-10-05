Option Strict On

Imports System.Data
Imports System.Drawing
Imports System.Globalization
Imports System.Windows.Forms

''' <summary>Ticks the series to check for new releases.</summary>
Public Class FollowForm
    Inherits Form

    Private ReadOnly _list As New CheckedListBox With {.Dock = DockStyle.Fill, .CheckOnClick = True, .IntegralHeight = False}
    Private ReadOnly _ids As New List(Of Long)
    Private ReadOnly _was As New List(Of Boolean)

    ''' <summary>Series whose tick changed: (id, follow).</summary>
    Public ReadOnly Property Changes As New List(Of (Id As Long, Follow As Boolean))

    Public Sub New(series As DataTable)
        Text = "Series to follow"
        StartPosition = FormStartPosition.CenterParent
        MinimizeBox = False
        ShowInTaskbar = False
        AutoScaleMode = AutoScaleMode.Font
        Font = New Font("Segoe UI", 9.5F)
        ClientSize = New Size(560, 600)
        MinimumSize = New Size(420, 360)

        For Each r As DataRow In series.Rows
            Dim vol = Convert.ToString(r("Volume"), CultureInfo.InvariantCulture)
            Dim latest = Convert.ToString(r("Latest"), CultureInfo.InvariantCulture)
            Dim follow = Convert.ToInt64(r("Follow"), CultureInfo.InvariantCulture) <> 0
            _ids.Add(Convert.ToInt64(r("id"), CultureInfo.InvariantCulture))
            _was.Add(follow)
            _list.Items.Add($"{r("Series")}{If(vol <> "", $" ({vol})", "")}   ·   {r("Have")} owned" &
                            If(latest <> "", $", latest {latest}", ""), follow)
        Next

        Dim note = Ui.MakeLabel("Ticked series are checked for new issues. Ones you have a recent issue of are ticked to start with.")
        note.Dock = DockStyle.Top
        note.MaximumSize = New Size(540, 0)
        note.Padding = New Padding(4, 0, 4, 8)
        note.Tag = Theme.MutedTag

        Dim ok = Ui.MakeButton("Save", AddressOf OnOk, Theme.PrimaryTag)
        Dim cancel = Ui.MakeButton("Cancel", Sub(s, e) DialogResult = DialogResult.Cancel)
        Dim all = Ui.MakeButton("Tick all", Sub(s, e) TickAll(True))
        Dim none = Ui.MakeButton("Untick all", Sub(s, e) TickAll(False))
        Dim buttons As New FlowLayoutPanel With {.Dock = DockStyle.Bottom, .FlowDirection = FlowDirection.RightToLeft, .AutoSize = True, .Padding = New Padding(6)}
        buttons.Controls.AddRange({cancel, ok, none, all})
        CancelButton = cancel

        Dim body As New Panel With {.Dock = DockStyle.Fill, .Padding = New Padding(10)}
        body.Controls.Add(_list)
        body.Controls.Add(note)
        Controls.Add(body)
        Controls.Add(buttons)
        Theme.Apply(Me)
    End Sub

    Protected Overrides Sub OnHandleCreated(e As EventArgs)
        MyBase.OnHandleCreated(e)
        Theme.DarkTitleBar(Me)
    End Sub

    Private Sub TickAll(follow As Boolean)
        For i = 0 To _list.Items.Count - 1
            _list.SetItemChecked(i, follow)
        Next
    End Sub

    Private Sub OnOk(sender As Object, e As EventArgs)
        For i = 0 To _ids.Count - 1
            Dim now = _list.GetItemChecked(i)
            If now <> _was(i) Then Changes.Add((_ids(i), now))
        Next
        DialogResult = DialogResult.OK
    End Sub

End Class
