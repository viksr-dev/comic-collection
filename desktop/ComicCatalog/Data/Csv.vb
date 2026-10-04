Option Strict On

Imports System.Text

Namespace Data

    ''' <summary>Small CSV reader that handles quoted fields, doubled quotes and line breaks inside quotes.</summary>
    Public Module Csv

        Public Function Parse(text As String) As List(Of List(Of String))
            Dim rows As New List(Of List(Of String))
            Dim row As New List(Of String)
            Dim field As New StringBuilder()
            Dim quoted = False
            text = If(text, "").TrimStart(ChrW(&HFEFF))
            Dim i = 0
            While i < text.Length
                Dim ch = text(i)
                If quoted Then
                    If ch = """"c Then
                        If i + 1 < text.Length AndAlso text(i + 1) = """"c Then
                            field.Append(""""c)
                            i += 1
                        Else
                            quoted = False
                        End If
                    Else
                        field.Append(ch)
                    End If
                ElseIf ch = """"c Then
                    quoted = True
                ElseIf ch = ","c Then
                    row.Add(field.ToString())
                    field.Clear()
                ElseIf ch = vbCr(0) OrElse ch = vbLf(0) Then
                    If ch = vbCr(0) AndAlso i + 1 < text.Length AndAlso text(i + 1) = vbLf(0) Then i += 1
                    row.Add(field.ToString())
                    field.Clear()
                    rows.Add(row)
                    row = New List(Of String)
                Else
                    field.Append(ch)
                End If
                i += 1
            End While
            If field.Length > 0 OrElse row.Count > 0 Then
                row.Add(field.ToString())
                rows.Add(row)
            End If
            rows.RemoveAll(Function(r) r.All(Function(v) v.Trim() = ""))
            ' The phone app guards text that starts with = + - @ with a leading apostrophe.
            For Each r In rows
                For j = 0 To r.Count - 1
                    If r(j).Length > 1 AndAlso r(j)(0) = "'"c AndAlso "=+-@".Contains(r(j)(1)) Then r(j) = r(j).Substring(1)
                Next
            Next
            Return rows
        End Function

    End Module

End Namespace
