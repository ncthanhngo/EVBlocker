using System.Runtime.InteropServices;

namespace EVBlocker.Core.Usb;

/// <summary>A drive worth scanning, with what it is called.</summary>
public sealed record RemovableDrive
{
    /// <summary>Root path, such as <c>E:\</c>.</summary>
    public required string Root { get; init; }

    public required string Label { get; init; }

    /// <summary>How it is attached, in the words the interface shows.</summary>
    public required string Kind { get; init; }
}

public interface IRemovableDriveProbe
{
    IReadOnlyList<RemovableDrive> List();
}

/// <summary>
/// Finds the drives that arrived from outside this machine.
/// </summary>
/// <remarks>
/// Asks the storage driver for the bus the volume sits on rather than trusting
/// <c>DriveType</c>. Measured on the machine this was written for: an external disk attached
/// over USB reports DriveType 3, the same value an internal disk reports, so a scanner keyed on
/// DriveType 2 would skip it. USB hard disks are exactly what this family of worm travels on.
/// </remarks>
public sealed partial class RemovableDriveProbe : IRemovableDriveProbe
{
    /// <summary>IOCTL_STORAGE_QUERY_PROPERTY.</summary>
    private const uint StorageQueryProperty = 0x002D1400;

    /// <summary>STORAGE_DEVICE_DESCRIPTOR.BusType, past the fixed fields before it.</summary>
    private const int BusTypeOffset = 28;

    private const uint BusTypeUsb = 0x07;

    /// <summary>Size of STORAGE_PROPERTY_QUERY: two enums and one padded byte.</summary>
    private const int QuerySize = 12;

    private const uint GenericRead = 0;
    private const uint FileShareReadWrite = 0x00000003;
    private const uint OpenExisting = 3;

    public IReadOnlyList<RemovableDrive> List()
    {
        var found = new List<RemovableDrive>();

        foreach (DriveInfo drive in DriveInfo.GetDrives())
        {
            if (!drive.IsReady || drive.DriveType is DriveType.Network or DriveType.CDRom)
            {
                continue;
            }

            string? kind = Describe(drive);

            if (kind is null)
            {
                continue;
            }

            found.Add(new RemovableDrive
            {
                Root = drive.RootDirectory.FullName,
                Label = SafeLabel(drive),
                Kind = kind,
            });
        }

        return found;
    }

    /// <summary>What kind of outside drive this is, or null when it belongs to the machine.</summary>
    private static string? Describe(DriveInfo drive)
    {
        // Removable is taken at its word: a flash drive reports it and needs no further asking.
        if (drive.DriveType == DriveType.Removable)
        {
            return "USB";
        }

        return IsOnUsbBus(drive.Name) ? "ổ cứng USB" : null;
    }

    private static bool IsOnUsbBus(string root)
    {
        // \\.\E: - the volume itself, opened for nothing but a query, so no rights are needed.
        string device = @"\\.\" + root.TrimEnd('\\');

        using SafeFileHandleWrapper handle = SafeFileHandleWrapper.Open(device);

        if (handle.IsInvalid)
        {
            return false;
        }

        Span<byte> query = stackalloc byte[QuerySize];
        Span<byte> descriptor = stackalloc byte[512];

        // PropertyId = StorageDeviceProperty (0) and QueryType = PropertyStandardQuery (0) are
        // both zero, so the zeroed buffer is already the query that has to be sent.
        bool ok = DeviceIoControl(
            handle.Handle,
            StorageQueryProperty,
            ref MemoryMarshal.GetReference(query),
            QuerySize,
            ref MemoryMarshal.GetReference(descriptor),
            descriptor.Length,
            out uint returned,
            0);

        return ok
            && returned >= BusTypeOffset + 4
            && MemoryMarshal.Read<uint>(descriptor[BusTypeOffset..]) == BusTypeUsb;
    }

    private static string SafeLabel(DriveInfo drive)
    {
        try
        {
            return string.IsNullOrWhiteSpace(drive.VolumeLabel)
                ? drive.Name.TrimEnd('\\')
                : drive.VolumeLabel;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A drive can be pulled between being listed and being asked about itself.
            return drive.Name.TrimEnd('\\');
        }
    }

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint CreateFileW(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        nint lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        nint hTemplateFile);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint hObject);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeviceIoControl(
        nint hDevice,
        uint dwIoControlCode,
        ref byte lpInBuffer,
        int nInBufferSize,
        ref byte lpOutBuffer,
        int nOutBufferSize,
        out uint lpBytesReturned,
        nint lpOverlapped);

    /// <summary>Closes the volume handle however the query ends.</summary>
    private readonly struct SafeFileHandleWrapper : IDisposable
    {
        private SafeFileHandleWrapper(nint handle) => Handle = handle;

        public nint Handle { get; }

        public bool IsInvalid => Handle == -1 || Handle == 0;

        public static SafeFileHandleWrapper Open(string device) =>
            new(CreateFileW(device, GenericRead, FileShareReadWrite, 0, OpenExisting, 0, 0));

        public void Dispose()
        {
            if (!IsInvalid)
            {
                CloseHandle(Handle);
            }
        }
    }
}
