Option Strict On

Imports System.Data
Imports System.Globalization

Namespace Data

    ''' <summary>What to change on several comics at once. Anything left as Nothing stays as it is on each comic.</summary>
    Public Class BulkChanges
        Public Property Condition As String
        ''' <summary>Value of each copy.</summary>
        Public Property ValueEach As Double?
        ''' <summary>"YYYY-MM-DD".</summary>
        Public Property DateBought As String
        ''' <summary>Box or shelf; "" takes them out of their box.</summary>
        Public Property Box As String
        Public Property Format As String
        ''' <summary>Publisher of each comic's series (so the rest of that series changes too).</summary>
        Public Property Publisher As String
        Public Property IsRead As Boolean?

        Public ReadOnly Property IsEmpty As Boolean
            Get
                Return Condition Is Nothing AndAlso Not ValueEach.HasValue AndAlso DateBought Is Nothing AndAlso Box Is Nothing AndAlso
                       Format Is Nothing AndAlso Publisher Is Nothing AndAlso Not IsRead.HasValue
            End Get
        End Property
    End Class

    Partial Public Class ComicDb

        ''' <summary>Changes the same things on several comics at once (the edit window for several).</summary>
        Public Sub EditMany(comicIds As IEnumerable(Of Long), changes As BulkChanges)
            Using conn = Open()
                Using tx = conn.BeginTransaction()
                    Dim publisherId As Object = Nothing
                    If changes.Publisher IsNot Nothing AndAlso changes.Publisher.Trim() <> "" Then
                        Execute(conn, tx, "INSERT OR IGNORE INTO publishers (name) VALUES ($p0)", changes.Publisher.Trim())
                        publisherId = Scalar(conn, "SELECT id FROM publishers WHERE name = $p0", tx, changes.Publisher.Trim())
                    End If
                    Dim today = Date.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                    For Each id In comicIds
                        If changes.Condition IsNot Nothing Then Execute(conn, tx, "UPDATE collection SET condition = $p1 WHERE comic_id = $p0", id, changes.Condition)
                        If changes.ValueEach.HasValue Then Execute(conn, tx, "UPDATE collection SET current_value = $p1 WHERE comic_id = $p0", id, changes.ValueEach.Value)
                        If changes.DateBought IsNot Nothing Then Execute(conn, tx, "UPDATE collection SET purchase_date = $p1 WHERE comic_id = $p0", id, changes.DateBought.Trim())
                        If changes.Box IsNot Nothing Then Execute(conn, tx, "UPDATE collection SET location = $p1 WHERE comic_id = $p0", id, changes.Box.Trim())
                        If changes.Format IsNot Nothing Then Execute(conn, tx, "UPDATE comics SET format = $p1 WHERE id = $p0", id, changes.Format.Trim())
                        If changes.Publisher IsNot Nothing Then
                            Execute(conn, tx, "UPDATE series SET publisher_id = $p1 WHERE id = (SELECT series_id FROM comics WHERE id = $p0)", id, publisherId)
                        End If
                        If changes.IsRead.HasValue Then
                            Execute(conn, tx, "UPDATE collection SET read_on = CASE WHEN NOT $p1 THEN '' WHEN is_read AND read_on <> '' THEN read_on ELSE $p2 END,
                                               is_read = $p1 WHERE comic_id = $p0", id, changes.IsRead.Value, today)
                        End If
                    Next
                    tx.Commit()
                End Using
            End Using
        End Sub

    End Class

End Namespace
