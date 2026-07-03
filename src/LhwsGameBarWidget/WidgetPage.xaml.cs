using System.Collections.ObjectModel;
using System.ComponentModel;
using LhwsGameBarWidget.Lhws;
using Microsoft.Gaming.XboxGameBar;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace LhwsGameBarWidget;

public sealed class SensorRow : INotifyPropertyChanged
{
    private string valueText = "";

    public string Identifier { get; init; } = "";
    public string Label { get; init; } = "";

    public string ValueText
    {
        get => valueText;
        set
        {
            if (valueText != value)
            {
                valueText = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ValueText)));
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed partial class WidgetPage : Page
{
    private static readonly HashSet<string> ShownSensorTypes =
        ["Temperature", "Load", "Power", "Fan"];

    private readonly LhwsSensorReader reader = new();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly ObservableCollection<SensorRow> rows = [];

    private XboxGameBarWidget? widget;
    private long lastSeenUpdate;
    private int staleTicks;

    public WidgetPage()
    {
        InitializeComponent();
        SensorList.ItemsSource = rows;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        widget = e.Parameter as XboxGameBarWidget;
        timer.Tick += OnTick;
        timer.Start();
        OnTick(null, null!);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        timer.Stop();
        timer.Tick -= OnTick;
        reader.Dispose();
    }

    private void OnTick(object? sender, object e)
    {
        if (!reader.IsConnected && !reader.TryConnect())
        {
            StatusText.Text = "LibreHardwareService not found — is the service running?";
            return;
        }

        var snapshot = reader.Read();
        if (snapshot == null)
        {
            StatusText.Text = "Waiting for sensor data…";
            return;
        }

        // A restarted service recreates the named objects; our old handles then point
        // to a frozen mapping. Reconnect when the timestamp stops advancing.
        if (snapshot.LastUpdateUnixSeconds == lastSeenUpdate)
        {
            if (++staleTicks >= 10)
            {
                staleTicks = 0;
                reader.TryConnect();
            }
        }
        else
        {
            staleTicks = 0;
            lastSeenUpdate = snapshot.LastUpdateUnixSeconds;
        }

        UpdateRows(snapshot);
        StatusText.Text = "Updated " +
            DateTimeOffset.FromUnixTimeSeconds(snapshot.LastUpdateUnixSeconds).ToLocalTime().ToString("HH:mm:ss");
    }

    private void UpdateRows(LhwsSnapshot snapshot)
    {
        var shown = snapshot.Sensors
            .Where(s => ShownSensorTypes.Contains(s.SensorType))
            .OrderBy(s => s.HardwareName, StringComparer.Ordinal)
            .ThenBy(s => s.SensorType, StringComparer.Ordinal)
            .ThenBy(s => s.Name, StringComparer.Ordinal)
            .ToList();

        // Rebuild only when the sensor set changes; otherwise update values in place
        bool sameSet = shown.Count == rows.Count &&
            !shown.Where((s, i) => s.Identifier != rows[i].Identifier).Any();

        if (!sameSet)
        {
            rows.Clear();
            foreach (var s in shown)
            {
                rows.Add(new SensorRow
                {
                    Identifier = s.Identifier,
                    Label = $"{s.HardwareName} · {s.Name}",
                    ValueText = FormatValue(s),
                });
            }
        }
        else
        {
            for (int i = 0; i < shown.Count; i++)
            {
                rows[i].ValueText = FormatValue(shown[i]);
            }
        }
    }

    private static string FormatValue(SensorReading s) => s.SensorType switch
    {
        "Temperature" => $"{s.Value:F0} °C",
        "Load" => $"{s.Value:F0} %",
        "Power" => $"{s.Value:F1} W",
        "Fan" => $"{s.Value:F0} RPM",
        "Clock" => $"{s.Value:F0} MHz",
        _ => s.Value.ToString("F1"),
    };
}
