using Velopack;

namespace CSharpWinForms;

internal static class Program
{
    // Logging is essential for debugging! Ideally you should write it to a file.
    public static MemoryLogger Log { get; } = new MemoryLogger();

    /// <summary>
    ///  The main entry point for the application.
    /// </summary>
    [STAThread]
    static void Main()
    {
        try {
            // It's important to Run() the VelopackApp as early as possible in app startup.
            VelopackApp.Build()
                .WithFirstRun((v) => { /* Your first run code here */ })
                .Run(Log);

            // To customize application configuration such as set high DPI settings or default font,
            // see https://aka.ms/applicationconfiguration.
            ApplicationConfiguration.Initialize();
            Application.Run(new Form1());

        } catch (Exception ex) {
            MessageBox.Show("Unhandled exception: " + ex.ToString());
        }
    }
}