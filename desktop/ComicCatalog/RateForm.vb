Option Strict On

Imports System.Drawing
Imports System.Windows.Forms

''' <summary>Stars (1 to 5) and a few words on what you thought of a comic.</summary>
Public Class RateForm
    Inherits Form

    Private ReadOnly _starButtons As New List(Of Button)
    Private ReadOnly _review As New TextBox With {.Dock = DockStyle.Fill, .Multiline = True, .ScrollBars = ScrollBars.Vertical,
                                                  .PlaceholderText = "What did you think? (optional)"}
    Private _stars As Integer

    Public ReadOnly Property Stars As Integer
        Get
            Return _stars
        End Get
    End Property

    Public ReadOnly Property Review As String
        Get
            Return _review.Text.Trim()
        End Get
    End Property

    Public Sub New(comicName As String, Optional stars As Integer = 0, Optional review As String = "")
        Text = "Rate it"
        StartPosition = FormStartPosition.CenterParent
        FormBorderStyle = FormBorderStyle.FixedDialog
        MinimizeBox = False
        MaximizeBox = False
        ShowInTaskbar = False
        AutoScaleMode = AutoScaleMode.Font
        Font = New Font("Segoe UI", 9.5F)
        ClientSize = New Size(440, 250)
        _review.Text = If(review, "")

        Dim heading As New Label With {.Dock = DockStyle.Top, .Height = 34, .TextAlign = ContentAlignment.MiddleLeft,
                                       .Font = New Font("Segoe UI Semibold", 12.0F), .Text = comicName, .AutoEllipsis = True}
        Dim row As New FlowLayoutPanel With {.Dock = DockStyle.Top, .AutoSize = True, .WrapContents = False}
        For i = 1 To 5
            Dim n = i
            Dim b As New Button With {.Text = "☆", .Width = 44, .Height = 40, .FlatStyle = FlatStyle.Flat,
                                      .Font = New Font("Segoe UI Symbol", 18.0F), .Cursor = Cursors.Hand, .Margin = New Padding(0, 0, 2, 0)}
            b.FlatAppearance.BorderSize = 0
            AddHandler b.Click, Sub(s, e) SetStars(If(_stars = n, 0, n))
            _starButtons.Add(b)
            row.Controls.Add(b)
        Next
        Dim body As New Panel With {.Dock = DockStyle.Fill, .Padding = New Padding(12, 6, 12, 0)}
        body.Controls.Add(_review)
        body.Controls.Add(row)
        body.Controls.Add(heading)

        Dim save = Ui.MakeButton("Save", Sub(s, e) DialogResult = DialogResult.OK, Theme.PrimaryTag)
        Dim skip = Ui.MakeButton("Skip", Sub(s, e) DialogResult = DialogResult.Cancel)
        Dim buttons As New FlowLayoutPanel With {.Dock = DockStyle.Bottom, .FlowDirection = FlowDirection.RightToLeft, .AutoSize = True, .Padding = New Padding(6)}
        buttons.Controls.AddRange({skip, save})
        AcceptButton = save
        CancelButton = skip
        Controls.Add(body)
        Controls.Add(buttons)
        Theme.Apply(Me)
        SetStars(stars)
    End Sub

    Protected Overrides Sub OnHandleCreated(e As EventArgs)
        MyBase.OnHandleCreated(e)
        Theme.DarkTitleBar(Me)
    End Sub

    ' Clicking the star you already picked clears it.
    Private Sub SetStars(n As Integer)
        _stars = Math.Max(0, Math.Min(5, n))
        For i = 0 To _starButtons.Count - 1
            _starButtons(i).Text = If(i < _stars, "★", "☆")
            _starButtons(i).ForeColor = If(i < _stars, Theme.Accent, Theme.Muted)
        Next
    End Sub

End Class
