namespace UnoSample;

public sealed partial class MainPage : Page
{
	public MainViewModel ViewModel { get; } = new MainViewModel();

	public bool NotInstalled => ViewModel?.IsInstalled != true;

	public MainPage()
	{
		this.InitializeComponent();

		this.DataContext = ViewModel;
	}
}
