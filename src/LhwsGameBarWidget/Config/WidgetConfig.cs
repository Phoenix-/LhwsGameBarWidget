using System.Text.Json;
using System.Text.Json.Serialization;
using Windows.Storage;

namespace LhwsGameBarWidget.Config;

/// <summary>
/// One row of the widget. A row is an instance of a layout template; the only
/// layout so far is "bar": name on the left, two value slots, progress bar below.
/// Slots hold LHWS sensor identifiers (e.g. "/amdcpu/0/temperature/2") and are
/// all optional — an empty slot renders as nothing.
/// </summary>
public sealed class RowConfig
{
    public string Layout { get; set; } = "bar";
    public string Name { get; set; } = "";
    /// <summary>Sensor driving the progress bar; percent-typed sensors only (Load/Control/Level).</summary>
    public string? Bar { get; set; }
    /// <summary>Sensor shown in the middle column (typically a temperature).</summary>
    public string? Center { get; set; }
    /// <summary>Sensor shown right-aligned (load %, RPM, …).</summary>
    public string? Right { get; set; }
}

public sealed class WidgetConfig
{
    public int Version { get; set; } = 1;
    public List<RowConfig> Rows { get; set; } = [];
}

// Native AOT publish → reflection-free serializer required (same as LhwsJsonContext)
[JsonSerializable(typeof(WidgetConfig))]
internal sealed partial class ConfigJsonContext : JsonSerializerContext
{
}

/// <summary>
/// Persists the row list as a JSON string in LocalSettings. LocalSettings is shared
/// between the widget window and the settings window (same package, different view
/// threads) and each read returns a consistent value, so the widget just polls the
/// raw string once a second and reloads when it changes — no cross-window eventing.
/// </summary>
public static class ConfigStore
{
    private const string Key = "widgetConfig";

    public static string? LoadRaw() =>
        ApplicationData.Current.LocalSettings.Values[Key] as string;

    public static WidgetConfig Load()
    {
        try
        {
            var raw = LoadRaw();
            if (!string.IsNullOrEmpty(raw))
            {
                return JsonSerializer.Deserialize(raw, ConfigJsonContext.Default.WidgetConfig)
                    ?? new WidgetConfig();
            }
        }
        catch (JsonException)
        {
            // corrupt config → start over rather than crash the widget
        }
        return new WidgetConfig();
    }

    public static void Save(WidgetConfig config) =>
        ApplicationData.Current.LocalSettings.Values[Key] =
            JsonSerializer.Serialize(config, ConfigJsonContext.Default.WidgetConfig);
}
