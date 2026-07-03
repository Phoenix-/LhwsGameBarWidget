using System.Text.Json.Serialization;

namespace LhwsGameBarWidget.Lhws;

// Wire format of LibreHardwareService (service/src/models/DataSensor.cs),
// serialized as camelCase JSON.

public sealed class SensorReading
{
    [JsonPropertyName("identifier")] public string Identifier { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("sensorType")] public string SensorType { get; set; } = "";
    [JsonPropertyName("hardwareId")] public string HardwareId { get; set; } = "";
    [JsonPropertyName("hardwareName")] public string HardwareName { get; set; } = "";
    [JsonPropertyName("hardwareType")] public string HardwareType { get; set; } = "";
    [JsonPropertyName("value")] public float Value { get; set; }
    [JsonPropertyName("min")] public float Min { get; set; }
    [JsonPropertyName("max")] public float Max { get; set; }
}

public sealed class LhwsSnapshot
{
    public long LastUpdateUnixSeconds { get; init; }
    public int UpdateIntervalMs { get; init; }
    public IReadOnlyList<SensorReading> Sensors { get; init; } = [];
}

// UWP on .NET 9 publishes with Native AOT, so reflection-based System.Text.Json is unavailable
[JsonSerializable(typeof(SensorReading))]
internal sealed partial class LhwsJsonContext : JsonSerializerContext
{
}
