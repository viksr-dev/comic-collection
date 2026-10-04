Option Strict On

Imports System.IO
Imports System.Text.Json

Namespace Data

    ''' <summary>Settings kept next to the database in Documents\Comic Catalog.</summary>
    Public Class AppSettings
        Public Property RelayUrl As String = ""
        Public Property DatabasePath As String = ""
        ''' <summary>Your own picture shown across the top of the app (kept in the Comic Catalog folder).</summary>
        Public Property BannerPath As String = ""
        ''' <summary>Whether the main and edit windows were maximised last time, so they open that way again.</summary>
        Public Property MainMaximized As Boolean
        Public Property EditMaximized As Boolean

        Public Shared ReadOnly Property Folder As String
            Get
                Return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Comic Catalog")
            End Get
        End Property

        Private Shared ReadOnly Property FilePath As String
            Get
                Return Path.Combine(Folder, "settings.json")
            End Get
        End Property

        Public Shared Function Load() As AppSettings
            Dim s As AppSettings = Nothing
            Try
                If File.Exists(FilePath) Then s = JsonSerializer.Deserialize(Of AppSettings)(File.ReadAllText(FilePath))
            Catch ex As Exception When TypeOf ex Is JsonException OrElse TypeOf ex Is IOException
            End Try
            s = If(s, New AppSettings())
            If String.IsNullOrWhiteSpace(s.DatabasePath) Then s.DatabasePath = Path.Combine(Folder, "comics.db")
            Return s
        End Function

        Public Sub Save()
            Directory.CreateDirectory(Folder)
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Me, New JsonSerializerOptions With {.WriteIndented = True}))
        End Sub
    End Class

End Namespace
