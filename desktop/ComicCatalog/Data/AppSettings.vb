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
        ''' <summary>The code shown on the phone under "Send scans to your computer".</summary>
        Public Property SyncCode As String = ""
        ''' <summary>Whether the main and edit windows were maximised last time, so they open that way again.</summary>
        Public Property MainMaximized As Boolean
        Public Property EditMaximized As Boolean
        ''' <summary>Where daily backups go. Blank means OneDrive (or Documents without it).</summary>
        Public Property BackupFolder As String = ""
        Public Property LastBackup As DateTime?
        Public Property LastReleaseCheck As DateTime?
        ''' <summary>Find covers, story arcs and new releases by themselves while the app is open.</summary>
        Public Property AutoJobs As Boolean = True
        ''' <summary>An update you said "not now" to, so it isn't offered again every start.</summary>
        Public Property SkippedBuild As Integer
        ''' <summary>The build that ran last time, so the app can say what's new after an update.</summary>
        Public Property LastRunBuild As Integer
        ''' <summary>The heading used last time for the share page, e.g. "James's comics".</summary>
        Public Property ShareTitle As String = ""
        ''' <summary>How much you'd like to spend on comics a month (0 = no budget).</summary>
        Public Property MonthlyBudget As Double
        ''' <summary>How many comics you'd like to read a month (0 = no goal).</summary>
        Public Property MonthlyReadGoal As Integer
        ''' <summary>Your comic shop's email, for sending the pull list.</summary>
        Public Property ShopEmail As String = ""
        ''' <summary>The name printed on the insurance report.</summary>
        Public Property InsuranceOwner As String = ""
        ''' <summary>How many of your dollars one US dollar costs (1 if you think in US dollars, about 1.7 for NZ dollars). Wishlist max prices are in your dollars.</summary>
        Public Property LocalPerUsd As Double = 1
        ''' <summary>What grading one comic costs all up, in US dollars (fee plus postage both ways).</summary>
        Public Property GradingCost As Double = 60

        ''' <summary>The backup folder in use.</summary>
        Public Function BackupFolderOrDefault() As String
            Return If(String.IsNullOrWhiteSpace(BackupFolder), Backups.DefaultFolder(), BackupFolder)
        End Function

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
