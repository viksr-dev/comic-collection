Option Strict On

Imports Microsoft.Data.Sqlite

Namespace Data

    ''' <summary>
    ''' How some comics were before a change (read, box, trade, key issue, rating, lending, sets, editing several),
    ''' so Undo can put them back. Taken with <see cref="ComicDb.Snapshot"/>.
    ''' </summary>
    Public NotInheritable Class ComicSnapshot
        Friend ReadOnly Comics As New Dictionary(Of Long, Dictionary(Of String, List(Of Dictionary(Of String, Object))))
    End Class

    Partial Public Class ComicDb

        ''' <summary>Notes how these comics are now, for <see cref="Restore"/>.</summary>
        Public Function Snapshot(comicIds As IEnumerable(Of Long)) As ComicSnapshot
            Dim snap As New ComicSnapshot()
            Using conn = Open()
                For Each id In comicIds.Distinct()
                    Dim data As New Dictionary(Of String, List(Of Dictionary(Of String, Object)))
                    data("collection") = Rows(conn, Nothing, "SELECT * FROM collection WHERE comic_id = $p0", id)
                    data("comics") = Rows(conn, Nothing, "SELECT id, key_note, format FROM comics WHERE id = $p0", id)
                    data("series") = Rows(conn, Nothing, "SELECT s.id, s.publisher_id FROM series s JOIN comics c ON c.series_id = s.id WHERE c.id = $p0", id)
                    data("loans") = Rows(conn, Nothing, "SELECT * FROM loans WHERE comic_id = $p0", id)
                    data("set") = Rows(conn, Nothing, "SELECT st.* FROM set_comics sc JOIN story_sets st ON st.id = sc.set_id WHERE sc.comic_id = $p0", id)
                    snap.Comics(id) = data
                Next
            End Using
            Return snap
        End Function

        ''' <summary>Puts comics back the way they were in the snapshot. Ones deleted since are left out.</summary>
        Public Sub Restore(snap As ComicSnapshot)
            Using conn = Open()
                Using tx = conn.BeginTransaction()
                    For Each kv In snap.Comics
                        Dim id = kv.Key
                        Dim data = kv.Value
                        If Scalar(conn, "SELECT 1 FROM comics WHERE id = $p0", tx, id) Is Nothing Then Continue For
                        For Each row In data("collection")
                            Dim cols = row.Keys.Where(Function(k) Not k.Equals("id", StringComparison.OrdinalIgnoreCase) AndAlso
                                                                  Not k.Equals("comic_id", StringComparison.OrdinalIgnoreCase)).ToList()
                            Dim sets = String.Join(", ", cols.Select(Function(c, i) $"[{c}] = $p{i + 1}"))
                            Execute(conn, tx, $"UPDATE collection SET {sets} WHERE comic_id = $p0",
                                    {CObj(id)}.Concat(cols.Select(Function(c) row(c))).ToArray())
                        Next
                        For Each row In data("comics")
                            Execute(conn, tx, "UPDATE comics SET key_note = $p1, format = $p2 WHERE id = $p0", id, row("key_note"), row("format"))
                        Next
                        For Each row In data("series")
                            Execute(conn, tx, "UPDATE series SET publisher_id = $p1 WHERE id = $p0", row("id"), row("publisher_id"))
                        Next
                        Execute(conn, tx, "DELETE FROM loans WHERE comic_id = $p0", id)
                        For Each row In data("loans")
                            InsertRow(conn, tx, "loans", row)
                        Next
                        Execute(conn, tx, "DELETE FROM set_comics WHERE comic_id = $p0", id)
                        For Each st In data("set")
                            Dim setId = Scalar(conn, "SELECT id FROM story_sets WHERE id = $p0", tx, st("id"))
                            If setId Is Nothing Then
                                InsertRow(conn, tx, "story_sets", st)
                                setId = st("id")
                            End If
                            Execute(conn, tx, "INSERT INTO set_comics (comic_id, set_id) VALUES ($p0, $p1)", id, setId)
                        Next
                    Next
                    Execute(conn, tx, "DELETE FROM story_sets WHERE id NOT IN (SELECT set_id FROM set_comics)")
                    tx.Commit()
                End Using
            End Using
        End Sub

    End Class

End Namespace
