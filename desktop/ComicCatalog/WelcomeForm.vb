Option Strict On

Imports System.Drawing
Imports System.Windows.Forms

''' <summary>The first time the app opens with no comics: three steps to get going.</summary>
Public Class WelcomeForm
    Inherits Form

    ''' <summary>What was picked: "relay", "phone", "import", "samples", "add", or "" for none.</summary>
    Public Property Choice As String = ""

    Public Sub New(relayDone As Boolean, phoneDone As Boolean)
        Text = "Welcome to Comic Catalog"
        StartPosition = FormStartPosition.CenterParent
        FormBorderStyle = FormBorderStyle.FixedDialog
        MinimizeBox = False
        MaximizeBox = False
        ShowInTaskbar = False
        AutoScaleMode = AutoScaleMode.Font
        Font = New Font("Segoe UI", 10.0F)
        ClientSize = New Size(620, 470)

        Dim body As New FlowLayoutPanel With {.Dock = DockStyle.Fill, .FlowDirection = FlowDirection.TopDown, .WrapContents = False,
                                              .Padding = New Padding(20, 14, 20, 8), .AutoScroll = True}
        body.Controls.Add(New Label With {.Text = "Let's get your comics in", .AutoSize = True, .ForeColor = Theme.Accent,
                                          .Font = New Font("Segoe UI", 16.0F, FontStyle.Bold), .Margin = New Padding(0, 0, 0, 6)})
        body.Controls.Add(New Label With {.AutoSize = True, .MaximumSize = New Size(570, 0), .Tag = Theme.MutedTag, .Margin = New Padding(0, 0, 0, 10),
                                          .Text = "Three steps. You can do them in any order, skip any of them, and come back to this from Settings."})
        AddStep(body, "1", "Set up comic lookup", If(relayDone, "Done. Barcodes are looked up on Metron.",
                "Paste your relay address (the same one the phone app uses) so typing or scanning a barcode fills in the details and cover."),
                {("Set up lookup", "relay")})
        AddStep(body, "2", "Connect your phone", If(phoneDone, "Done. Scans from your phone arrive here.",
                "Turn on ""Send scans to your computer"" in the phone app's Settings and type its code here. Then comics you scan on the phone show up here."),
                {("Connect phone", "phone")})
        AddStep(body, "3", "Bring in your comics", "Import the spreadsheet exported from the phone app, add a comic yourself, or look around with a few sample comics first.",
                {("Import from the phone app…", "import"), ("Add a comic", "add"), ("Try sample comics", "samples")})

        Dim close = Ui.MakeButton("I'll look around myself", Sub(s, e) DialogResult = DialogResult.Cancel)
        Dim buttons As New FlowLayoutPanel With {.Dock = DockStyle.Bottom, .FlowDirection = FlowDirection.RightToLeft, .AutoSize = True, .Padding = New Padding(8)}
        buttons.Controls.Add(close)
        CancelButton = close
        Controls.Add(body)
        Controls.Add(buttons)
        Theme.Apply(Me)
    End Sub

    Private Sub AddStep(body As Control, number As String, title As String, text As String, actions As (Label As String, Choice As String)())
        Dim card As New TableLayoutPanel With {.ColumnCount = 2, .AutoSize = True, .Width = 570, .BackColor = Theme.Panel,
                                               .Padding = New Padding(10), .Margin = New Padding(0, 0, 0, 10)}
        card.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 44))
        card.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        card.Controls.Add(New Label With {.Text = number, .AutoSize = True, .ForeColor = Theme.Accent, .Font = New Font("Segoe UI", 20.0F, FontStyle.Bold)}, 0, 0)
        card.SetRowSpan(card.GetControlFromPosition(0, 0), 3)
        card.Controls.Add(New Label With {.Text = title, .AutoSize = True, .Font = New Font("Segoe UI", 11.5F, FontStyle.Bold)}, 1, 0)
        card.Controls.Add(New Label With {.Text = text, .AutoSize = True, .MaximumSize = New Size(490, 0), .Tag = Theme.MutedTag, .Margin = New Padding(3, 2, 3, 6)}, 1, 1)
        Dim row As New FlowLayoutPanel With {.AutoSize = True, .WrapContents = True, .MaximumSize = New Size(500, 0), .Margin = New Padding(0)}
        For Each a In actions
            Dim pick = a.Choice
            row.Controls.Add(Ui.MakeButton(a.Label, Sub(s, e)
                                                        Choice = pick
                                                        DialogResult = DialogResult.OK
                                                    End Sub, If(row.Controls.Count = 0, Theme.PrimaryTag, Nothing)))
        Next
        card.Controls.Add(row, 1, 2)
        body.Controls.Add(card)
    End Sub

    Protected Overrides Sub OnHandleCreated(e As EventArgs)
        MyBase.OnHandleCreated(e)
        Theme.DarkTitleBar(Me)
    End Sub

End Class
