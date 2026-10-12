Option Strict On

Imports System.Drawing
Imports System.Windows.Forms
Imports ComicCatalog.Data

''' <summary>"What should I read next?": suggests an unread comic, with Something else to see another.</summary>
Public Class ReadNextForm
    Inherits Form

    Private ReadOnly _db As ComicDb
    Private ReadOnly _covers As New CoverCache()
    Private ReadOnly _ideas As List(Of ReadNext.Suggestion)
    Private _index As Integer

    Private ReadOnly _cover As New PictureBox With {.Dock = DockStyle.Fill, .SizeMode = PictureBoxSizeMode.Zoom}
    Private ReadOnly _name As New Label With {.AutoSize = True, .MaximumSize = New Size(380, 0), .Font = New Font("Segoe UI Semibold", 16.0F),
                                              .Margin = New Padding(3, 0, 3, 4)}
    Private ReadOnly _story As New Label With {.AutoSize = True, .MaximumSize = New Size(380, 0), .Tag = Theme.MutedTag, .Font = New Font("Segoe UI", 11.0F)}
    Private ReadOnly _reason As New Label With {.AutoSize = True, .MaximumSize = New Size(380, 0), .Font = New Font("Segoe UI", 10.5F),
                                                .Margin = New Padding(3, 14, 3, 14)}
    Private ReadOnly _count As New Label With {.AutoSize = True, .Tag = Theme.MutedTag}
    Private ReadOnly _another As Button = Ui.MakeButton("Something else", Sub(s, e) Advance())
    Private ReadOnly _markRead As Button = Ui.MakeButton("I've read it", AddressOf OnMarkRead, Theme.PrimaryTag)

    ''' <summary>The comic to show in the collection afterwards (the last one suggested), or 0.</summary>
    Public Property ShowComicId As Long
    ''' <summary>True once something was marked read, so the collection list needs refreshing.</summary>
    Public Property MarkedAny As Boolean

    Public Sub New(db As ComicDb)
        _db = db
        _ideas = ReadNext.Suggest(db.ReadingItems(), New Random())
        Text = "What should I read next?"
        StartPosition = FormStartPosition.CenterParent
        FormBorderStyle = FormBorderStyle.FixedDialog
        MinimizeBox = False
        MaximizeBox = False
        ShowInTaskbar = False
        AutoScaleMode = AutoScaleMode.Font
        Font = New Font("Segoe UI", 9.5F)
        ClientSize = New Size(700, 440)

        Dim info As New FlowLayoutPanel With {.Dock = DockStyle.Fill, .FlowDirection = FlowDirection.TopDown, .WrapContents = False,
                                             .Padding = New Padding(12, 8, 8, 8)}
        Dim showIt = Ui.MakeButton("Show it in my collection", Sub(s, e)
                                                                    ShowComicId = If(Current IsNot Nothing, Current.Item.ComicId, 0L)
                                                                    DialogResult = DialogResult.OK
                                                                End Sub)
        Dim buttons As New FlowLayoutPanel With {.AutoSize = True, .WrapContents = True, .MaximumSize = New Size(400, 0), .Margin = New Padding(0, 6, 0, 0)}
        buttons.Controls.AddRange({_markRead, _another, showIt})
        info.Controls.AddRange({_name, _story, _reason, buttons, _count})

        Dim close = Ui.MakeButton("Close", Sub(s, e) DialogResult = DialogResult.Cancel)
        CancelButton = close
        Dim bottom As New FlowLayoutPanel With {.Dock = DockStyle.Bottom, .FlowDirection = FlowDirection.RightToLeft, .AutoSize = True, .Padding = New Padding(6)}
        bottom.Controls.Add(close)

        Dim root As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 2, .Padding = New Padding(12)}
        root.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 250))
        root.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        root.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        root.Controls.Add(_cover, 0, 0)
        root.Controls.Add(info, 1, 0)
        Controls.Add(root)
        Controls.Add(bottom)
        Theme.Apply(Me)
        _cover.BackColor = Theme.Panel2
        ShowCurrent()
    End Sub

    Protected Overrides Sub OnHandleCreated(e As EventArgs)
        MyBase.OnHandleCreated(e)
        Theme.DarkTitleBar(Me)
    End Sub

    Private ReadOnly Property Current As ReadNext.Suggestion
        Get
            Return If(_index < _ideas.Count, _ideas(_index), Nothing)
        End Get
    End Property

    Private Sub Advance()
        If _ideas.Count = 0 Then Return
        _index = (_index + 1) Mod _ideas.Count
        ShowCurrent()
    End Sub

    Private Async Sub ShowCurrent()
        Dim idea = Current
        _cover.Image = Nothing
        If idea Is Nothing Then
            _name.Text = "You've read everything!"
            _story.Text = ""
            _reason.Text = "Every comic in your collection is marked as read. Time for a trip to the comic shop."
            _count.Text = ""
            _another.Enabled = False
            _markRead.Enabled = False
            Return
        End If
        _name.Text = idea.Item.ToString()
        _story.Text = idea.Item.Title
        _story.Visible = idea.Item.Title <> ""
        _reason.Text = idea.Reason
        _count.Text = $"Idea {_index + 1} of {_ideas.Count}"
        _another.Enabled = _ideas.Count > 1
        _markRead.Enabled = True
        Dim comicId = idea.Item.ComicId
        Dim img = Await _covers.GetImageAsync(comicId, idea.Item.CoverUrl)
        If IsDisposed OrElse Current Is Nothing OrElse Current.Item.ComicId <> comicId Then Return
        _cover.Image = If(img, CoverCache.Placeholder())
    End Sub

    ' Marks it read and moves on to the next idea, which may now be the following issue.
    Private Sub OnMarkRead(sender As Object, e As EventArgs)
        Dim idea = Current
        If idea Is Nothing Then Return
        _db.SetRead({idea.Item.ComicId}, True)
        MarkedAny = True
        ShowComicId = idea.Item.ComicId
        Dim fresh = ReadNext.Suggest(_db.ReadingItems(), New Random())
        ' Keep going with the same series if it has more.
        Dim same = fresh.FindIndex(Function(s) s.Item.Series.Equals(idea.Item.Series, StringComparison.OrdinalIgnoreCase) AndAlso
                                               s.Item.Volume.Equals(idea.Item.Volume, StringComparison.OrdinalIgnoreCase))
        _ideas.Clear()
        _ideas.AddRange(fresh)
        _index = Math.Max(0, same)
        ShowCurrent()
    End Sub

End Class
