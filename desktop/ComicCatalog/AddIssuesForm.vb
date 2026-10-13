Option Strict On

Imports System.Drawing
Imports System.Windows.Forms
Imports ComicCatalog.Data

''' <summary>Adds issues of a series you collect by typing their numbers ("1-12, 15"), instead of scanning each one.</summary>
Public Class AddIssuesForm
    Inherits Form

    Private ReadOnly _issuesBox As New TextBox With {.Dock = DockStyle.Fill, .PlaceholderText = "e.g. 1-12, 15, 20-24"}
    Private ReadOnly _condition As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList, .Dock = DockStyle.Fill}
    Private ReadOnly _box As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDown, .Dock = DockStyle.Fill}
    Private ReadOnly _count As New Label With {.AutoSize = True, .Tag = Theme.MutedTag, .Margin = New Padding(3, 4, 3, 3)}

    Public ReadOnly Property Issues As New List(Of String)
    Public ReadOnly Property Condition As String
        Get
            Return If(_condition.SelectedIndex <= 0, "", CStr(_condition.SelectedItem))
        End Get
    End Property
    Public ReadOnly Property Box As String
        Get
            Return _box.Text.Trim()
        End Get
    End Property

    Public Sub New(seriesName As String, issuesText As String, haveText As String, boxes As IEnumerable(Of String), lastBox As String)
        Text = $"Add issues of {seriesName}"
        StartPosition = FormStartPosition.CenterParent
        FormBorderStyle = FormBorderStyle.FixedDialog
        MinimizeBox = False
        MaximizeBox = False
        ShowInTaskbar = False
        AutoScaleMode = AutoScaleMode.Font
        Font = New Font("Segoe UI", 9.5F)
        ClientSize = New Size(540, 300)

        _issuesBox.Text = issuesText
        _condition.Items.Add("Not set")
        _condition.Items.AddRange(Ui.Conditions.Where(Function(c) c <> "").Cast(Of Object)().ToArray())
        _condition.SelectedIndex = 0
        _box.Items.AddRange(boxes.Cast(Of Object)().ToArray())
        _box.Text = lastBox
        AddHandler _issuesBox.TextChanged, Sub(s, e) ShowCount()

        Dim fields As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 2, .Padding = New Padding(12, 6, 12, 0)}
        fields.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        fields.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        For Each row In {Tuple.Create("Issues you have", CType(_issuesBox, Control)), Tuple.Create("", CType(_count, Control)),
                         Tuple.Create("Condition", CType(_condition, Control)), Tuple.Create("Box or shelf", CType(_box, Control))}
            fields.RowStyles.Add(New RowStyle(SizeType.AutoSize))
            fields.Controls.Add(Ui.MakeLabel(row.Item1))
            fields.Controls.Add(row.Item2)
        Next
        Dim note As New Label With {.Dock = DockStyle.Top, .AutoSize = False, .Height = 62, .Padding = New Padding(12, 10, 12, 0), .Tag = Theme.MutedTag,
                                    .Text = $"{haveText} Type the issue numbers you have, with a dash for a run. Ones already in your collection are skipped. " &
                                            "Covers and details are filled in afterwards by Find covers."}
        Dim ok = Ui.MakeButton("Add them", AddressOf OnOk, Theme.PrimaryTag)
        Dim cancel = Ui.MakeButton("Cancel", Sub(s, e) DialogResult = DialogResult.Cancel)
        Dim buttons As New FlowLayoutPanel With {.Dock = DockStyle.Bottom, .FlowDirection = FlowDirection.RightToLeft, .AutoSize = True, .Padding = New Padding(6)}
        buttons.Controls.AddRange({cancel, ok})
        AcceptButton = ok
        CancelButton = cancel
        Controls.Add(fields)
        Controls.Add(note)
        Controls.Add(buttons)
        Theme.Apply(Me)
        ShowCount()
    End Sub

    Protected Overrides Sub OnHandleCreated(e As EventArgs)
        MyBase.OnHandleCreated(e)
        Theme.DarkTitleBar(Me)
    End Sub

    Private Sub ShowCount()
        Dim list = ComicDb.ParseIssueList(_issuesBox.Text)
        _count.Text = If(list Is Nothing, "Type numbers like 1-12, 15, 20-24", If(list.Count = 1, "1 issue", $"{list.Count:N0} issues"))
    End Sub

    Private Sub OnOk(sender As Object, e As EventArgs)
        Dim list = ComicDb.ParseIssueList(_issuesBox.Text)
        If list Is Nothing OrElse list.Count = 0 Then
            Ui.ShowError(Me, "Type the issue numbers you have, like 1-12, 15, 20-24.")
            _issuesBox.Focus()
            Return
        End If
        Issues.AddRange(list)
        DialogResult = DialogResult.OK
    End Sub

End Class
