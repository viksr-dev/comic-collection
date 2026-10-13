Option Strict On

Imports System.Drawing
Imports System.Globalization
Imports System.Windows.Forms

''' <summary>Small helpers for building the screens in code.</summary>
Friend Module Ui

    Public ReadOnly Conditions As String() = {"", "Mint", "Near Mint", "Very Fine", "Fine", "Very Good", "Good", "Fair", "Poor"}

    Public Function MakeButton(text As String, onClick As EventHandler, Optional style As String = Nothing) As Button
        Dim b As New Button With {.Text = text, .AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .Tag = style,
                                  .MinimumSize = New Size(0, 34), .Padding = New Padding(10, 2, 10, 2), .Margin = New Padding(3, 3, 3, 3)}
        AddHandler b.Click, onClick
        Return b
    End Function

    Public Function MakeLabel(text As String) As Label
        Return New Label With {.Text = text, .AutoSize = True, .Anchor = AnchorStyles.Left, .Margin = New Padding(3, 8, 3, 3)}
    End Function

    Public Function MakeGrid() As DataGridView
        Dim g As New DataGridView With {
            .Dock = DockStyle.Fill, .ReadOnly = True, .AllowUserToAddRows = False, .AllowUserToDeleteRows = False,
            .AllowUserToResizeRows = False, .SelectionMode = DataGridViewSelectionMode.FullRowSelect, .MultiSelect = True,
            .RowHeadersVisible = False, .AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            .BackgroundColor = SystemColors.Window, .BorderStyle = BorderStyle.None}
        g.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(246, 247, 250)
        Return g
    End Function

    ''' <summary>Reads a price typed as "12", "12.50" or "$12.50". Blank means not set.</summary>
    Public Function ParseMoney(text As String, ByRef value As Double?) As Boolean
        Dim t = If(text, "").Trim().TrimStart("$"c).Replace(",", "")
        If t = "" Then
            value = Nothing
            Return True
        End If
        Dim d As Double
        If Double.TryParse(t, NumberStyles.Number, CultureInfo.InvariantCulture, d) AndAlso d >= 0 Then
            value = d
            Return True
        End If
        Return False
    End Function

    Public Function MoneyText(value As Double?) As String
        Return If(value.HasValue, value.Value.ToString("0.00", CultureInfo.InvariantCulture), "")
    End Function

    ''' <summary>Asks for a line of text. Returns Nothing if cancelled.</summary>
    Public Function Ask(owner As IWin32Window, title As String, prompt As String, Optional initial As String = "",
                        Optional suggestions As IEnumerable(Of String) = Nothing) As String
        Using f As New Form With {.Text = title, .StartPosition = FormStartPosition.CenterParent, .FormBorderStyle = FormBorderStyle.FixedDialog,
                                  .MinimizeBox = False, .MaximizeBox = False, .ShowInTaskbar = False, .AutoScaleMode = AutoScaleMode.Font,
                                  .Font = New Font("Segoe UI", 9.5F), .ClientSize = New Size(420, 140)}
            Dim box As New ComboBox With {.Dock = DockStyle.Top, .DropDownStyle = ComboBoxStyle.DropDown, .Text = initial}
            If suggestions IsNot Nothing Then
                box.Items.AddRange(suggestions.Cast(Of Object)().ToArray())
                ' Typing finds a match in a long list.
                box.AutoCompleteMode = AutoCompleteMode.SuggestAppend
                box.AutoCompleteSource = AutoCompleteSource.ListItems
            End If
            Dim label As New Label With {.Text = prompt, .Dock = DockStyle.Top, .AutoSize = False, .Height = 30, .TextAlign = ContentAlignment.MiddleLeft}
            Dim body As New Panel With {.Dock = DockStyle.Fill, .Padding = New Padding(12, 8, 12, 0)}
            body.Controls.Add(box)
            body.Controls.Add(label)
            Dim ok = MakeButton("OK", Sub(s, e) f.DialogResult = DialogResult.OK, Theme.PrimaryTag)
            Dim cancel = MakeButton("Cancel", Sub(s, e) f.DialogResult = DialogResult.Cancel)
            Dim buttons As New FlowLayoutPanel With {.Dock = DockStyle.Bottom, .FlowDirection = FlowDirection.RightToLeft, .AutoSize = True, .Padding = New Padding(6)}
            buttons.Controls.AddRange({cancel, ok})
            f.AcceptButton = ok
            f.CancelButton = cancel
            f.Controls.Add(body)
            f.Controls.Add(buttons)
            Theme.Apply(f)
            AddHandler f.HandleCreated, Sub(s, e) Theme.DarkTitleBar(f)
            Return If(f.ShowDialog(owner) = DialogResult.OK, box.Text.Trim(), Nothing)
        End Using
    End Function

    ''' <summary>
    ''' What went wrong in plain words, with what to do about it, for the usual troubles (no internet,
    ''' a file open in another program, a full disk…). Anything else keeps its own message.
    ''' It follows "Couldn't …: "; <paramref name="sentence"/> starts it with a capital to stand on its own.
    ''' </summary>
    Public Function Friendly(ex As Exception, Optional sentence As Boolean = False) As String
        Dim text = FriendlyText(ex)
        Return If(sentence AndAlso text <> "", Char.ToUpperInvariant(text(0)) & text.Substring(1), text)
    End Function

    Private Function FriendlyText(ex As Exception) As String
        Dim inner = ex
        While inner.InnerException IsNot Nothing AndAlso Not TypeOf inner Is Net.Http.HttpRequestException
            inner = inner.InnerException
        End While
        Select Case True
            Case TypeOf ex Is Net.Http.HttpRequestException, TypeOf inner Is Net.Http.HttpRequestException,
                 TypeOf ex Is Net.Sockets.SocketException, TypeOf inner Is Net.Sockets.SocketException
                Return "no answer from the internet or your relay. Check you're online and try again. " &
                       "If it keeps happening, open Settings and click Save and test."
            Case TypeOf ex Is TaskCanceledException, TypeOf ex Is TimeoutException
                Return "it took too long to answer. Check you're online and try again in a minute."
            Case TypeOf ex Is UnauthorizedAccessException
                Return "Windows didn't let the app use that file or folder. Pick one in your Documents folder, " &
                       "or check the file isn't read-only."
            Case TypeOf ex Is IO.IOException
                Dim code = ex.HResult And &HFFFF
                If code = &H20 OrElse code = &H21 Then Return "the file is open in another program (like Excel or a PDF viewer). Close it and try again."
                If code = &H70 OrElse code = &H27 Then Return "the disk is full. Free up some space and try again."
                If TypeOf ex Is IO.FileNotFoundException OrElse TypeOf ex Is IO.DirectoryNotFoundException Then
                    Return "that file or folder isn't there any more. It may have been moved, renamed or deleted."
                End If
                Return ex.Message
            Case TypeOf ex Is Microsoft.Data.Sqlite.SqliteException
                Return "your collection's database is busy or can't be read. Close any other copy of Comic Catalog and try again. " &
                       "If it keeps happening, Settings has Restore a backup."
            Case Else
                Return ex.Message
        End Select
    End Function

    Public Sub ShowError(owner As IWin32Window, message As String)
        MessageBox.Show(owner, message, "Comic Catalog", MessageBoxButtons.OK, MessageBoxIcon.Warning)
    End Sub

End Module
