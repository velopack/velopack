namespace CSharpMaui;

public class App : Application
{
    protected override Window CreateWindow(IActivationState activationState)
    {
        return new Window(new MainPage()) { Title = "CSharpMaui" };
    }
}
