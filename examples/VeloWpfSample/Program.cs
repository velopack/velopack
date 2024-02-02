using System.Reflection;
using System.Windows;
using Velopack;

namespace VeloWpfSample
{
    public class Program
    {
        public static bool WasFirstRun { get; private set; }

        public static bool WasJustUpdated { get; private set; }

        public static string UpdateUrl { get; private set; }

        public static MemoryLogger Log { get; private set; }

        // Since WPF has an "automatic" Program.Main, we need to create our own.
        // In order for this to work, you must also add the following to your .csproj:
        // <StartupObject>VeloWpfSample.Program</StartupObject>
        [STAThread]
        public static void Main(string[] args)
        {
            try {
                // Logging is essential for debugging! Ideally you should write it to a file.
                Log = new MemoryLogger();

                // It's important to Run() the VelopackApp as early as possible in app startup.
                VelopackApp.Build()
                    .WithRestarted((v) => WasJustUpdated = true)
                    .WithFirstRun((v) => WasFirstRun = true)
                    .Run(Log);

                // This is purely for demonstration purposes, we get the update URL from a
                // property defined by MSBuild, so we can locate the local releases directory.
                // In your production app, this should point to your update server.
                UpdateUrl = Assembly.GetEntryAssembly()
                    .GetCustomAttributes<AssemblyMetadataAttribute>()
                    .Where(x => x.Key == "WpfSampleReleaseDir")
                    .Single().Value;

                // We can now launch the WPF application as normal.
                var app = new App();
                app.InitializeComponent();
                app.Run();
            } catch (Exception ex) {
                MessageBox.Show("Unhandled exception: " + ex.ToString());
            }
        }
    }
}
