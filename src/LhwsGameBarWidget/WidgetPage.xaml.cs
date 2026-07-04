using System.Collections.ObjectModel;
using System.ComponentModel;
using LhwsGameBarWidget.Config;
using LhwsGameBarWidget.Lhws;
using Microsoft.Gaming.XboxGameBar;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

namespace LhwsGameBarWidget;

/// <summary>
/// View model of one configured widget row ("bar" layout): static name plus
/// per-tick value slots. Recreated whenever the config changes, so only the
/// live values raise change notifications.
/// </summary>
public sealed class CompositeRow : INotifyPropertyChanged
{
    private string centerText = "";
    private string rightText = "";
    private double barValue;
    private Brush? centerBrush;

    public string Name { get; init; } = "";
    public string? BarId { get; init; }
    public string? CenterId { get; init; }
    public string? RightId { get; init; }

    public Visibility BarVisibility => BarId is null ? Visibility.Collapsed : Visibility.Visible;

    public string CenterText
    {
        get => centerText;
        set => Set(ref centerText, value, nameof(CenterText));
    }

    public string RightText
    {
        get => rightText;
        set => Set(ref rightText, value, nameof(RightText));
    }

    public double BarValue
    {
        get => barValue;
        set
        {
            if (Math.Abs(barValue - value) > 0.001)
            {
                barValue = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BarValue)));
            }
        }
    }

    public Brush? CenterBrush
    {
        get => centerBrush;
        set
        {
            if (!ReferenceEquals(centerBrush, value))
            {
                centerBrush = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CenterBrush)));
            }
        }
    }

    private void Set(ref string field, string value, string name)
    {
        if (field != value)
        {
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed partial class WidgetPage : Page
{
    private readonly LhwsSensorReader reader = new();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly ObservableCollection<CompositeRow> rows = [];

    private readonly SolidColorBrush darkDefault = new(Colors.White);
    private readonly SolidColorBrush lightDefault = new(Colors.Black);
    private readonly SolidColorBrush warnBrush = new(Color.FromArgb(255, 255, 185, 0));   // ≥ 70 °C
    private readonly SolidColorBrush hotBrush = new(Color.FromArgb(255, 255, 92, 92));    // ≥ 85 °C

    private XboxGameBarWidget? widget;
    // Sentinel that never equals a stored value (or its absence), so the first
    // tick always applies the config — including the empty-state visibility.
    private string? appliedConfigRaw = "\0unset";
    private int resizedRowCount = -1;
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
        if (widget != null)
        {
            widget.RequestedThemeChanged += Widget_RequestedThemeChanged;
            widget.SettingsClicked += Widget_SettingsClicked;
            RequestedTheme = widget.RequestedTheme;
        }
        timer.Tick += OnTick;
        timer.Start();
        OnTick(null, null!);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        if (widget != null)
        {
            widget.RequestedThemeChanged -= Widget_RequestedThemeChanged;
            widget.SettingsClicked -= Widget_SettingsClicked;
        }
        timer.Stop();
        timer.Tick -= OnTick;
        reader.Dispose();
    }

    // Game Bar raises this off the UI thread
    private async void Widget_RequestedThemeChanged(XboxGameBarWidget sender, object args)
    {
        await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal,
            () => RequestedTheme = sender.RequestedTheme);
    }

    // The title-bar settings button is NOT handled by Game Bar itself: it raises this
    // event and the widget must ask Game Bar to activate its settings widget.
    private async void Widget_SettingsClicked(XboxGameBarWidget sender, object args)
    {
        App.Log("SettingsClicked → ActivateSettingsAsync");
        try
        {
            await sender.ActivateSettingsAsync();
        }
        catch (Exception ex)
        {
            App.Log($"ActivateSettingsAsync FAILED: {ex}");
        }
    }

    private void OnTick(object? sender, object e)
    {
        // The settings window writes to LocalSettings; pick up edits on the next tick
        var raw = ConfigStore.LoadRaw();
        if (raw != appliedConfigRaw)
        {
            appliedConfigRaw = raw;
            RebuildRows(ConfigStore.Load());
        }

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

    private void RebuildRows(WidgetConfig config)
    {
        rows.Clear();
        foreach (var r in config.Rows)
        {
            rows.Add(new CompositeRow
            {
                Name = r.Name,
                BarId = r.Bar,
                CenterId = r.Center,
                RightId = r.Right,
            });
        }
        EmptyState.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        SensorList.Visibility = rows.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        ResizeToRows();
    }

    // TryResizeWindowAsync's height semantics (chrome inclusion, DPI) are undocumented,
    // so instead of a guessed constant the fit is self-calibrating: request, measure the
    // resulting view height, correct by the error. The learned chrome offset is kept for
    // the session so later fits land on the first try.
    private static double chromeOffset = 44;
    private int resizeFailures;

    private async void ResizeToRows()
    {
        if (widget == null || rows.Count == resizedRowCount || resizeFailures >= 5)
        {
            return;
        }

        // During bootstrap the CoreWindow still reports full-screen bounds; wait for
        // the host to size the view (manifest allows 240–600 wide) and retry next tick.
        double width = Window.Current.Bounds.Width;
        if (width < 100 || width > 620)
        {
            return;
        }

        try
        {
            double content = ComputeContentHeight();
            double request = content + chromeOffset;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                var op = widget.TryResizeWindowAsync(new Windows.Foundation.Size(width, request));
                bool ok = op != null && await op;
                if (!ok)
                {
                    resizeFailures++;
                    App.Log($"TryResizeWindowAsync({width:F0}x{request:F0}) -> {(op == null ? "null-op" : "false")}, failures={resizeFailures}");
                    return;
                }
                await Task.Delay(150); // let the host apply the new bounds
                // The WinRT await may have resumed off the UI thread, where
                // Window.Current is null — measure the view back on the dispatcher.
                double view = 0;
                await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal,
                    () => view = Window.Current.Bounds.Height);
                double error = view - content;
                App.Log($"fit: requested {request:F0}, content {content:F0}, view {view:F0}, error {error:F0}");
                if (Math.Abs(error) <= 2)
                {
                    break;
                }
                request -= error;
            }
            resizeFailures = 0;
            resizedRowCount = rows.Count;
            chromeOffset = request - content;
        }
        catch (Exception ex)
        {
            resizeFailures++;
            App.Log($"ResizeToRows FAILED (failures={resizeFailures}): {ex}");
        }
    }

    private double ComputeContentHeight()
    {
        UpdateLayout();
        double h = 16 + StatusText.ActualHeight + 4; // page padding + status + its margin
        if (rows.Count == 0)
        {
            h += Math.Max(EmptyState.ActualHeight, 120);
        }
        else
        {
            for (int i = 0; i < rows.Count; i++)
            {
                double item = (SensorList.ContainerFromIndex(i) as FrameworkElement)?.ActualHeight ?? 0;
                h += item > 1 ? item : 40; // unrealized containers fall back to MinHeight
            }
        }
        // Sub-pixel rounding at fractional DPI scales (e.g. 125%) can otherwise leave
        // the list 1px short of its content, flashing a scrollbar.
        return Math.Ceiling(h) + 3;
    }

    private void UpdateRows(LhwsSnapshot snapshot)
    {
        if (rows.Count == 0)
        {
            return;
        }

        var byId = new Dictionary<string, SensorReading>(snapshot.Sensors.Count, StringComparer.Ordinal);
        foreach (var s in snapshot.Sensors)
        {
            byId[s.Identifier] = s;
        }

        var defaultBrush = ActualTheme == ElementTheme.Light ? lightDefault : darkDefault;

        foreach (var row in rows)
        {
            var center = Lookup(byId, row.CenterId);
            row.CenterText = FormatSlot(center, row.CenterId);
            row.CenterBrush = center is { SensorType: "Temperature" }
                ? BrushForTemperature(center.Value, defaultBrush)
                : defaultBrush;

            row.RightText = FormatSlot(Lookup(byId, row.RightId), row.RightId);

            var bar = Lookup(byId, row.BarId);
            row.BarValue = bar == null ? 0 : Math.Clamp(bar.Value, 0, 100);
        }
    }

    private static SensorReading? Lookup(Dictionary<string, SensorReading> byId, string? id) =>
        id != null && byId.TryGetValue(id, out var s) ? s : null;

    private Brush BrushForTemperature(float celsius, Brush defaultBrush) => celsius switch
    {
        >= 85 => hotBrush,
        >= 70 => warnBrush,
        _ => defaultBrush,
    };

    /// <summary>Empty slot renders as nothing; configured but missing sensor as "—".</summary>
    private static string FormatSlot(SensorReading? s, string? id) =>
        id == null ? "" : s == null ? "—" : FormatValue(s);

    private static string FormatValue(SensorReading s) => s.SensorType switch
    {
        "Temperature" => $"{s.Value:F0}°",
        "Load" or "Control" or "Level" => $"{s.Value:F0}%",
        "Fan" => $"{s.Value:F0}",
        "Power" => $"{s.Value:F0} W",
        "Clock" => $"{s.Value:F0} MHz",
        "Voltage" => $"{s.Value:F2} V",
        "Current" => $"{s.Value:F1} A",
        "Data" => $"{s.Value:F1} GB",
        "SmallData" => $"{s.Value:F0} MB",
        "Throughput" => FormatThroughput(s.Value),
        _ => s.Value.ToString("F1"),
    };

    private static string FormatThroughput(float bytesPerSecond) => bytesPerSecond switch
    {
        >= 1024 * 1024 => $"{bytesPerSecond / (1024 * 1024):F1} MB/s",
        >= 1024 => $"{bytesPerSecond / 1024:F0} KB/s",
        _ => $"{bytesPerSecond:F0} B/s",
    };
}
