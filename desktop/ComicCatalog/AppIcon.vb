Option Strict On

Imports System.Drawing

''' <summary>The app's own icon (app.ico, built in), for the window and taskbar.</summary>
Friend Module AppIcon

    Public Function Load() As Icon
        Using s = GetType(AppIcon).Assembly.GetManifestResourceStream("app.ico")
            Return If(s Is Nothing, Nothing, New Icon(s))
        End Using
    End Function

End Module
