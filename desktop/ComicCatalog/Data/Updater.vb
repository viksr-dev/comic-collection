Option Strict On

Imports System.Globalization
Imports System.IO
Imports System.Net.Http
Imports System.Reflection
Imports System.Text.Json

Namespace Data

    ''' <summary>A newer version of the app on GitHub.</summary>
    Public Class AppUpdate
        Public Property Build As Integer
        Public Property Notes As String = ""
        Public Property DownloadUrl As String = ""
    End Class

    ''' <summary>
    ''' Checks GitHub for a newer build of the app and swaps it in. Each build on the main branch is
    ''' published as a release called "desktop-&lt;build number&gt;" with ComicCatalog.exe attached.
    ''' </summary>
    Public Module Updater

        Public Const ReleasesUrl As String = "https://api.github.com/repos/viksr-dev/comic-collection/releases/latest"
        Private ReadOnly Http As New HttpClient With {.Timeout = TimeSpan.FromMinutes(5)}

        ''' <summary>This copy's build number (set by the GitHub build; 0 when built some other way).</summary>
        Public Function CurrentBuild() As Integer
            Dim v = If(Assembly.GetEntryAssembly(), GetType(Updater).Assembly).GetName().Version
            Return If(v Is Nothing, 0, Math.Max(0, v.Build))
        End Function

        ''' <summary>The newer version, or Nothing when this one is up to date.</summary>
        Public Async Function CheckAsync() As Task(Of AppUpdate)
            Using req As New HttpRequestMessage(HttpMethod.Get, ReleasesUrl)
                req.Headers.UserAgent.ParseAdd("ComicCatalog")
                req.Headers.Accept.ParseAdd("application/vnd.github+json")
                Using res = Await Http.SendAsync(req)
                    If res.StatusCode = Net.HttpStatusCode.NotFound Then Return Nothing
                    res.EnsureSuccessStatusCode()
                    Dim found = ParseRelease(Await res.Content.ReadAsStringAsync())
                    If found Is Nothing OrElse CurrentBuild() = 0 OrElse found.Build <= CurrentBuild() Then Return Nothing
                    Return found
                End Using
            End Using
        End Function

        ''' <summary>Reads GitHub's answer about the latest release. Nothing if it isn't an app release.</summary>
        Public Function ParseRelease(json As String) As AppUpdate
            Using doc = JsonDocument.Parse(json)
                Dim root = doc.RootElement
                Dim tag As JsonElement, body As JsonElement, assets As JsonElement
                If Not root.TryGetProperty("tag_name", tag) OrElse tag.ValueKind <> JsonValueKind.String Then Return Nothing
                Dim name = tag.GetString()
                Dim build As Integer
                If Not name.StartsWith("desktop-", StringComparison.Ordinal) OrElse
                   Not Integer.TryParse(name.Substring(8), NumberStyles.None, CultureInfo.InvariantCulture, build) Then Return Nothing
                Dim result As New AppUpdate With {.Build = build}
                If root.TryGetProperty("body", body) AndAlso body.ValueKind = JsonValueKind.String Then result.Notes = body.GetString().Trim()
                If root.TryGetProperty("assets", assets) AndAlso assets.ValueKind = JsonValueKind.Array Then
                    For Each a In assets.EnumerateArray()
                        Dim n As JsonElement, u As JsonElement
                        If a.TryGetProperty("name", n) AndAlso n.GetString() = "ComicCatalog.exe" AndAlso a.TryGetProperty("browser_download_url", u) Then
                            result.DownloadUrl = u.GetString()
                        End If
                    Next
                End If
                Return If(result.DownloadUrl = "", Nothing, result)
            End Using
        End Function

        ''' <summary>
        ''' Downloads the new version next to this one and swaps them over. Windows lets a running
        ''' program be renamed, so the old one moves aside (deleted next start) and the new one takes its
        ''' name. The caller then starts it and closes this copy.
        ''' </summary>
        Public Async Function InstallAsync(update As AppUpdate, exePath As String, progress As Action(Of Integer)) As Task
            Dim folder = Path.GetDirectoryName(exePath)
            Dim incoming = Path.Combine(folder, "ComicCatalog.new.exe")
            Using req As New HttpRequestMessage(HttpMethod.Get, update.DownloadUrl)
                req.Headers.UserAgent.ParseAdd("ComicCatalog")
                Using res = Await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead)
                    res.EnsureSuccessStatusCode()
                    Dim total = res.Content.Headers.ContentLength.GetValueOrDefault()
                    Using src = Await res.Content.ReadAsStreamAsync(), dest = File.Create(incoming)
                        Dim buffer(81919) As Byte
                        Dim done As Long = 0
                        Do
                            Dim n = Await src.ReadAsync(buffer, 0, buffer.Length)
                            If n = 0 Then Exit Do
                            Await dest.WriteAsync(buffer, 0, n)
                            done += n
                            If total > 0 Then progress(CInt(done * 100 \ total))
                        Loop
                    End Using
                    If total > 0 AndAlso New FileInfo(incoming).Length <> total Then
                        File.Delete(incoming)
                        Throw New IOException("The download didn't finish. Try again.")
                    End If
                End Using
            End Using
            Dim old = exePath & ".old"
            If File.Exists(old) Then File.Delete(old)
            File.Move(exePath, old)
            File.Move(incoming, exePath)
        End Function

        ''' <summary>Removes the copy left behind by the last update.</summary>
        Public Sub CleanUp(exePath As String)
            Try
                For Each leftover In {exePath & ".old", Path.Combine(Path.GetDirectoryName(exePath), "ComicCatalog.new.exe")}
                    If File.Exists(leftover) Then File.Delete(leftover)
                Next
            Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
            End Try
        End Sub

    End Module

End Namespace
