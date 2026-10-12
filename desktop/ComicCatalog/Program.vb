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
        form.StartPage = If(Environment.GetCommandLineArgs().FirstOrDefault(Function(a) a.StartsWith("--screenshot-page=")), "").Replace("--screenshot-page=", "")
        form.ReaderSample = If(Environment.GetCommandLineArgs().FirstOrDefault(Function(a) a.StartsWith("--screenshot-reader=")), "").Replace("--screenshot-reader=", "")
        Application.Run(form)
    End Sub

End Module
