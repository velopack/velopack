namespace CSharpMaui;

public static class MauiProgram
{
    public static MemoryLogger Log { get; private set; } = new();

    public static MauiApp CreateMauiApp()
    {
        return MauiApp.CreateBuilder()
            .UseMauiApp<App>()
            .Build();
    }
}
