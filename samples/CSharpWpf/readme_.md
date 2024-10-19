# VeloWpfSample
_Prerequisites: vpk command line tool installed_

This app demonstrates how to use WPF to provide a desktop UI, installer, and updates for Windows only.

You can run this sample by executing the build script with a version number: `build.bat 1.0.0`. Once built, you can install the app - build more updates, and then test updates and so forth. The sample app will check the local release dir for new update packages. 

In your production apps, you should deploy your updates to some kind of update server instead.

## WPF Implementation Notes
WPF generates a `Program.Main(argv[])` method automatically for you, so it requires a couple of extra steps to get Velopack working with WPF. 

1. You need to create your own `Program.cs` class, and add a static `Main()` method.
2. In order for dotnet to execute this new Main() method instead of the default WPF one, you need to add the following to your .csproj:
   ```xml
   <PropertyGroup>
     <StartupObject>YourNamespace.Program</StartupObject>
   </PropertyGroup>
   ```
3. You should run the `VelopackApp` builder before starting WPF as usual.
   ```cs
   [STAThread]
   public static void Main(string[] args)
   {
       VelopackApp.Build().Run();
       var application = new App();
       application.InitializeComponent();
       application.Run();
   }
   ```