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

        ''' <summary>
        ''' Copies your comic photos into the backup folder's Photos folder (only ones not copied before).
        ''' Returns how many were copied.
        ''' </summary>
        Public Function CopyPhotos(db As ComicDb, folder As String) As Integer
            Dim target = Path.Combine(folder, "Photos")
            Dim copied = 0
            For Each name In db.AllPhotoFiles()
                Dim source = Path.Combine(db.PhotosFolder, Path.GetFileName(name))
                Dim dest = Path.Combine(target, Path.GetFileName(name))
                If Not File.Exists(source) OrElse File.Exists(dest) Then Continue For
                Directory.CreateDirectory(target)
                File.Copy(source, dest)
                copied += 1
            Next
            Return copied
        End Function

        ''' <summary>After restoring a backup, brings back any photos missing from the Photos folder.</summary>
        Public Function RestorePhotos(db As ComicDb, folder As String) As Integer
            Dim source = Path.Combine(folder, "Photos")
            Dim restored = 0
            If Not Directory.Exists(source) Then Return 0
            For Each name In db.AllPhotoFiles()
                Dim from = Path.Combine(source, Path.GetFileName(name))
                Dim dest = Path.Combine(db.PhotosFolder, Path.GetFileName(name))
                If Not File.Exists(from) OrElse File.Exists(dest) Then Continue For
                Directory.CreateDirectory(db.PhotosFolder)
                File.Copy(from, dest)
                restored += 1
            Next
            Return restored
        End Function

    End Module

End Namespace
