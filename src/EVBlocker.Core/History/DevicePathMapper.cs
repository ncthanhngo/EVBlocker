using System.Runtime.InteropServices;

namespace EVBlocker.Core.History;

/// <summary>
/// Builds the device-to-drive-letter map by asking Windows via QueryDosDevice.
/// </summary>
/// <remarks>
/// The map is cached because it is stable in practice, but a miss triggers one refresh: plugging
/// in a USB disk or mounting a VHD adds a volume mid-session, and a stale map would silently
/// leave those paths untranslated.
/// </remarks>
public sealed partial class DevicePathMapper : IDevicePathMapper
{
    private const string DevicePrefix = @"\device\";

    private readonly object _gate = new();
    private Dictionary<string, string> _deviceToDrive;

    public DevicePathMapper()
    {
        _deviceToDrive = BuildMap();
    }

    public string Normalize(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return path;
        }

        // Only kernel device paths need translating; anything else is already usable.
        if (!path.StartsWith(DevicePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        if (TryTranslate(path, out string? translated))
        {
            return translated;
        }

        // A device appeared since the map was built (USB disk, mounted VHD). Refresh once,
        // then accept the answer either way rather than refreshing on every future miss.
        lock (_gate)
        {
            _deviceToDrive = BuildMap();
        }

        return TryTranslate(path, out translated) ? translated : path;
    }

    private bool TryTranslate(string path, out string translated)
    {
        Dictionary<string, string> map = _deviceToDrive;

        foreach ((string device, string drive) in map)
        {
            // Match on a full path segment: "\device\harddiskvolume1" must not match
            // "\device\harddiskvolume11".
            if (path.Length > device.Length
                && path.StartsWith(device, StringComparison.OrdinalIgnoreCase)
                && path[device.Length] == '\\')
            {
                translated = drive + path[device.Length..];
                return true;
            }
        }

        translated = string.Empty;
        return false;
    }

    private static Dictionary<string, string> BuildMap()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (string logicalDrive in Directory.GetLogicalDrives())
        {
            // GetLogicalDrives yields "C:\"; QueryDosDevice wants "C:".
            string drive = logicalDrive.TrimEnd('\\');
            string? device = QueryDosDevicePath(drive);

            if (!string.IsNullOrEmpty(device))
            {
                // Several drive letters can point at one device (subst, mounted folders).
                // First writer wins, which keeps the real volume letter rather than an alias.
                map.TryAdd(device, drive);
            }
        }

        return map;
    }

    private static unsafe string? QueryDosDevicePath(string driveLetter)
    {
        const int capacity = 512;
        char* buffer = stackalloc char[capacity];

        uint written = QueryDosDevice(driveLetter, buffer, capacity);
        return written == 0 ? null : new string(buffer);
    }

    [LibraryImport("kernel32.dll", EntryPoint = "QueryDosDeviceW",
        StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static unsafe partial uint QueryDosDevice(string lpDeviceName, char* lpTargetPath, uint ucchMax);
}
