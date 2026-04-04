using Uno.UI.Hosting;
using Velopack;

namespace UnoSample;
public class Program
{
	[STAThread]
	public static void Main(string[] args)
	{
		App.InitializeLogging();
		
		// It's important to Run() the VelopackApp as early as possible in app startup.
		VelopackApp.Build()
			.OnFirstRun((v) => { /* Your first run code here */ })
			.Run();

		var host = UnoPlatformHostBuilder.Create()
			.App(() => new App())
			.UseX11()
			.UseLinuxFrameBuffer()
			.UseMacOS()
			.UseWin32()
			.Build();

		host.Run();
	}
}
