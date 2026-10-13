Option Strict On

Imports System.Data
Imports System.Globalization

Namespace Data

    Partial Public Class ComicDb

        ' ---------- the Boxes page ----------

        ''' <summary>Adds a box or shelf with nothing in it yet, so it can be picked and filled later. Returns False if there's one by that name already.</summary>
        Public Function AddBox(name As String) As Boolean
            name = If(name, "").Trim()
            If name = "" Then Return False
            Using conn = Open()
                If Scalar(conn, "SELECT 1 FROM collection WHERE location = $p0 COLLATE NOCASE UNION SELECT 1 FROM boxes WHERE name = $p0", Nothing, name) IsNot Nothing Then Return False
                Execute(conn, Nothing, "INSERT INTO boxes (name) VALUES ($p0)", name)
            End Using
            Return True
        End Function

        ''' <summary>
        ''' Every box and shelf, empty ones too, with how many comics are in it (copies counted), how many series
        ''' and what they're worth. Columns: Box, Comics, Series, Value. "Box 2" before "Box 10".
        ''' </summary>
        Public Function BoxSummaries() As DataTable
            Dim table As New DataTable()
            table.Columns.Add("Box", GetType(String))
            table.Columns.Add("Comics", GetType(Integer))
            table.Columns.Add("Series", GetType(Integer))
            table.Columns.Add("Value", GetType(Double))
            Dim found As New List(Of Object())
            Using conn = Open()
                For Each r As DataRow In Query(conn,
                    "SELECT location, SUM(quantity), COUNT(DISTINCT series || '|' || IFNULL(volume, '')), SUM(IFNULL(total_value, 0))
                     FROM v_collection WHERE location <> '' GROUP BY location COLLATE NOCASE
                     UNION ALL SELECT name, 0, 0, 0 FROM boxes WHERE name NOT IN (SELECT location FROM collection)").Rows
                    found.Add({CStr(r(0)), Convert.ToInt32(r(1), CultureInfo.InvariantCulture), Convert.ToInt32(r(2), CultureInfo.InvariantCulture),
                               Convert.ToDouble(r(3), CultureInfo.InvariantCulture)})
                Next
            End Using
            For Each row In found.OrderBy(Function(f) NaturalKey(CStr(f(0))), StringComparer.OrdinalIgnoreCase)
                table.Rows.Add(row)
            Next
            Return table
        End Function

        ''' <summary>How many comics aren't in any box (copies counted).</summary>
        Public Function NotInBoxCount() As Integer
            Using conn = Open()
                Return Convert.ToInt32(Scalar(conn, "SELECT IFNULL(SUM(quantity), 0) FROM collection WHERE location = ''", Nothing), CultureInfo.InvariantCulture)
            End Using
        End Function

        ''' <summary>
        ''' The comics in a box ("" for ones in no box), in series order, issues before trades.
        ''' Columns: comic_id (hidden), Series, Issue, Format, Condition, Copies, Value, Read.
        ''' </summary>
        Public Function BoxComics(box As String) As DataTable
            Using conn = Open()
                Return Query(conn,
                    $"SELECT comic_id, series || CASE WHEN IFNULL(volume, '') <> '' THEN ' (' || volume || ')' ELSE '' END AS Series,
                            issue || CASE WHEN variant_name <> '' THEN ' · ' || variant_name ELSE '' END AS Issue,
                            format AS Format, condition AS Condition, quantity AS Copies, current_value AS Value,
                            CASE WHEN is_read THEN 'Read' ELSE '' END AS Read
                     FROM v_collection WHERE location = $p0 COLLATE NOCASE
                     ORDER BY {TradeSql("format")}, CASE WHEN series LIKE 'The %' THEN substr(series, 5) ELSE series END COLLATE NOCASE,
                              volume, issue_sort, issue", If(box, "").Trim())
            End Using
        End Function

        ''' <summary>The ids of the comics in a box.</summary>
        Public Function BoxComicIds(box As String) As List(Of Long)
            Using conn = Open()
                Return Query(conn, "SELECT comic_id FROM collection WHERE location = $p0 COLLATE NOCASE", If(box, "").Trim()).Rows.Cast(Of DataRow)().
                    Select(Function(r) Convert.ToInt64(r(0), CultureInfo.InvariantCulture)).ToList()
            End Using
        End Function

        ''' <summary>Renames a box: everything in it moves to the new name (joining that box if there's one already).</summary>
        Public Sub RenameBox(oldName As String, newName As String)
            oldName = If(oldName, "").Trim()
            newName = If(newName, "").Trim()
            If oldName = "" OrElse newName = "" Then Return
            Using conn = Open()
                Using tx = conn.BeginTransaction()
                    Execute(conn, tx, "UPDATE collection SET location = $p1 WHERE location = $p0 COLLATE NOCASE", oldName, newName)
                    Execute(conn, tx, "DELETE FROM boxes WHERE name = $p0", oldName)
                    ' An empty box stays listed under its new name.
                    If Scalar(conn, "SELECT 1 FROM collection WHERE location = $p0 COLLATE NOCASE", tx, newName) Is Nothing Then
                        Execute(conn, tx, "INSERT OR IGNORE INTO boxes (name) VALUES ($p0)", newName)
                    End If
                    tx.Commit()
                End Using
            End Using
        End Sub

        ''' <summary>Removes a box. Anything still in it is taken out (left in no box), not deleted.</summary>
        Public Sub RemoveBox(name As String)
            name = If(name, "").Trim()
            If name = "" Then Return
            Using conn = Open()
                Using tx = conn.BeginTransaction()
                    Execute(conn, tx, "UPDATE collection SET location = '' WHERE location = $p0 COLLATE NOCASE", name)
                    Execute(conn, tx, "DELETE FROM boxes WHERE name = $p0", name)
                    tx.Commit()
                End Using
            End Using
        End Sub

        ''' <summary>Empty boxes you've added (for the phone, which only sees boxes through the comics in them).</summary>
        Public Function EmptyBoxes() As List(Of String)
            Using conn = Open()
                Return Query(conn, "SELECT name FROM boxes WHERE name NOT IN (SELECT location FROM collection)").Rows.Cast(Of DataRow)().
                    Select(Function(r) CStr(r(0))).OrderBy(Function(n) NaturalKey(n), StringComparer.OrdinalIgnoreCase).ToList()
            End Using
        End Function

    End Class

End Namespace
