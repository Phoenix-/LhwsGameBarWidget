using LhwsGameBarWidget.Lhws;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace LhwsGameBarWidget;

public sealed partial class MainPage : Page
{
    public MainPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Connection self-test, mostly useful when launched outside Game Bar
        using var reader = new LhwsSensorReader();
        if (!reader.TryConnect())
        {
            ServiceStatusText.Text =
                "LibreHardwareService is not reachable. Check that the service is running " +
                "and grants ALL APPLICATION PACKAGES access to its shared memory.";
            return;
        }
        var snapshot = reader.Read();
        ServiceStatusText.Text = snapshot == null
            ? "Connected to LibreHardwareService, waiting for data…"
            : $"Connected to LibreHardwareService — {snapshot.Sensors.Count} sensors available.";
    }
}
