namespace UnoSample.Presentation;

public sealed partial class MainPage : Page
{
    public MainViewModel ViewModel => (MainViewModel)DataContext;

    public bool NotInstalled => ViewModel?.IsInstalled != true;

    public MainPage()
    {
        InitializeComponent();
    }
}
