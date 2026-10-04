Option Strict On

Imports System.Windows.Forms

Module Program

    <STAThread>
    Sub Main()
        Application.SetHighDpiMode(HighDpiMode.SystemAware)
        Application.EnableVisualStyles()
        Application.SetCompatibleTextRenderingDefault(False)
        Dim form As New MainForm()
        form.EditFirstOnStart = Environment.GetCommandLineArgs().Contains("--screenshot-edit")
        Application.Run(form)
    End Sub

End Module
