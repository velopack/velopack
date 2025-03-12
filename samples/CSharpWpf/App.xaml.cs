using System.Windows;
using Velopack;

namespace CSharpWpf
{
    public partial class App : Application
    {
        public static MemoryLogger Log { get; private set; } = new();

        // Since WPF has an "automatic" Program.Main, we need to create our own.
        // In order for this to work, you must also add the following to your .csproj:
        // <StartupObject>CSharpWpf.App</StartupObject>
        [STAThread]
        private static void Main(string[] args)
        {
            try {
                // It's important to Run() the VelopackApp as early as possible in app startup.
                VelopackApp.Build()
                    .OnFirstRun((v) => { /* Your first run code here */ })
                    .SetLogger(Log)
                    .Run();

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
