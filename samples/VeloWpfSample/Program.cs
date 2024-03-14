using System.Windows;
using Velopack;

namespace VeloWpfSample
{
    // Since WPF has an "automatic" Program.Main, we need to create our own.
    // In order for this to work, you must also add the following to your .csproj:
    // <StartupObject>VeloWpfSample.Program</StartupObject>
    public class Program
    {
        public static MemoryLogger Log { get; private set; }

        [STAThread]
        public static void Main(string[] args)
        {
            try {
                // Logging is essential for debugging! Ideally you should write it to a file.
                Log = new MemoryLogger();

                // It's important to Run() the VelopackApp as early as possible in app startup.
                VelopackApp.Build()
                    .WithFirstRun((v) => { /* Your first run code here */ })
                    .Run(Log);

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
