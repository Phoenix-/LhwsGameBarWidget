using System.IO.MemoryMappedFiles;
using System.Text.Json;

namespace LhwsGameBarWidget.Lhws;

/// <summary>
/// Reads sensor data published by LibreHardwareService into named shared memory.
///
/// Layout (see service/src/core/MemoryMappedSensors.cs of the service):
///   0:  int32  metadataSize (12)
///   4:  int32  updateInterval (ms)
///   8:  int64  lastUpdate (unix seconds)
///   16: int32  indexLength
///   20: int32  indexOffset
///   24: int32  indexFormat (1 = json, 2 = msgpack)
///   28: int32  dataLength
///   32: int32  dataOffset
///   36: int32[4] reserved
///   index: serialized DataIndex[]; data: concatenated sensor JSON objects,
///   each followed by a NUL byte; index offsets are relative to data start.
/// </summary>
public sealed class LhwsSensorReader : IDisposable
{
    private const string SensorsMapName = @"Global\LibreHardwareService/json/sensors/data";
    private const string SensorsMutexName = @"Global\LibreHardwareService/json/sensors/data/MUTEX";
    private const int MutexTimeoutMs = 250;

    private MemoryMappedFile? mmf;
    private MemoryMappedViewAccessor? accessor;
    private Mutex? mutex;

    public bool IsConnected => accessor != null;

    /// <summary>Opens the shared memory and mutex. Returns false while the service is not running.</summary>
    public bool TryConnect()
    {
        Disconnect();
        try
        {
            mmf = MemoryMappedFile.OpenExisting(SensorsMapName, MemoryMappedFileRights.Read);
            accessor = mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
            mutex = Mutex.OpenExisting(SensorsMutexName);
            return true;
        }
        catch (Exception ex) when (ex is FileNotFoundException or WaitHandleCannotBeOpenedException or UnauthorizedAccessException or IOException)
        {
            Disconnect();
            return false;
        }
    }

    /// <summary>
    /// Reads one snapshot. Returns null if not connected or the data is not readable yet.
    /// If the service was restarted, the old mapping stays alive but stale — the caller
    /// should watch LastUpdateUnixSeconds and TryConnect() again when it stops advancing.
    /// </summary>
    public LhwsSnapshot? Read()
    {
        if (accessor == null || mutex == null)
        {
            return null;
        }

        byte[] dataBytes;
        int updateInterval;
        long lastUpdate;

        bool acquired = false;
        try
        {
            try
            {
                acquired = mutex.WaitOne(MutexTimeoutMs);
            }
            catch (AbandonedMutexException)
            {
                acquired = true; // previous owner died; the data is rewritten every cycle, safe to read
            }

            int metadataSize = accessor.ReadInt32(0);
            updateInterval = accessor.ReadInt32(4);
            lastUpdate = accessor.ReadInt64(8);

            int header = 4 + metadataSize;
            int dataLength = accessor.ReadInt32(header + 12);
            int dataOffset = accessor.ReadInt32(header + 16);

            long capacity = accessor.Capacity;
            if (dataLength <= 0 || dataOffset < 0 || dataOffset + (long)dataLength > capacity)
            {
                return null; // service has not written a full snapshot yet
            }

            dataBytes = new byte[dataLength];
            accessor.ReadArray(dataOffset, dataBytes, 0, dataLength);
        }
        finally
        {
            if (acquired)
            {
                try { mutex.ReleaseMutex(); } catch (ApplicationException) { }
            }
        }

        // Parse outside the mutex to keep the service's write path unblocked.
        // The data block is a sequence of JSON sensor objects, each followed by a NUL
        // byte, so the index (whose format depends on the service's indexFormat
        // setting) is not needed at all.
        var sensors = new List<SensorReading>();
        int start = 0;
        for (int i = 0; i < dataBytes.Length; i++)
        {
            if (dataBytes[i] != 0)
            {
                continue;
            }
            if (i > start)
            {
                var sensor = JsonSerializer.Deserialize(
                    dataBytes.AsSpan(start, i - start), LhwsJsonContext.Default.SensorReading);
                if (sensor != null)
                {
                    sensors.Add(sensor);
                }
            }
            start = i + 1;
        }

        return new LhwsSnapshot
        {
            LastUpdateUnixSeconds = lastUpdate,
            UpdateIntervalMs = updateInterval,
            Sensors = sensors,
        };
    }

    public void Disconnect()
    {
        accessor?.Dispose();
        accessor = null;
        mmf?.Dispose();
        mmf = null;
        mutex?.Dispose();
        mutex = null;
    }

    public void Dispose() => Disconnect();
}
