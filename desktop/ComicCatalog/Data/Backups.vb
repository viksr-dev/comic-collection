Option Strict On

Imports System.Globalization
Imports System.IO

Namespace Data

    ''' <summary>Daily copies of the database, kept in OneDrive when this computer has it.</summary>
    Public Module Backups

        ''' <summary>How many daily copies are kept. Older ones are deleted.</summary>
        Public Const KeepCount As Integer = 30

        ''' <summary>The OneDrive folder's "Comic Catalog Backups", or Documents\Comic Catalog\Backups without OneDrive.</summary>
        Public Function DefaultFolder() As String
            For Each name In {"OneDriveConsumer", "OneDrive"}
                Dim oneDrive = Environment.GetEnvironmentVariable(name)
                If Not String.IsNullOrEmpty(oneDrive) AndAlso Directory.Exists(oneDrive) Then Return Path.Combine(oneDrive, "Comic Catalog Backups")
            Next
            Return Path.Combine(AppSettings.Folder, "Backups")
        End Function

        ''' <summary>Makes today's copy (replacing one made earlier today) and deletes the oldest past the limit. Returns its path.</summary>
        Public Function MakeBackup(db As ComicDb, folder As String) As String
            Dim target = Path.Combine(folder, $"comics-{DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.db")
            db.BackupTo(target)
            For Each old In Directory.GetFiles(folder, "comics-????-??-??.db").OrderByDescending(Function(f) f, StringComparer.Ordinal).Skip(KeepCount)
                File.Delete(old)
            Next
            Return target
        End Function

    End Module

End Namespace
