using System.Text;

namespace EVBlocker.Core.Usb;

/// <summary>The parts of a Windows shortcut that say what it runs.</summary>
public sealed record ShellLinkTarget
{
    /// <summary>Path stored relative to the shortcut, when the shortcut carries one.</summary>
    public string? RelativePath { get; init; }

    /// <summary>The command line handed to the target. Where a USB worm hides its payload.</summary>
    public string? Arguments { get; init; }

    public string? WorkingDirectory { get; init; }

    public string? IconLocation { get; init; }
}

/// <summary>
/// Reads the string fields out of a <c>.lnk</c> file.
/// </summary>
/// <remarks>
/// Parsed rather than read through IShellLink so the rules that use it can be tested against
/// bytes instead of against files a test would have to create through COM. It also means no
/// apartment requirements on whatever thread a scan runs on.
///
/// Only the fields a detection rule reads are decoded. The target id list and link info blocks
/// are skipped by their own recorded sizes rather than interpreted: what matters here is the
/// argument string, which is where a shortcut worm writes the command that runs its payload.
///
/// Format: [MS-SHLLINK]. A malformed file yields null, never an exception - these files come
/// from removable media written by anything at all, and half of them are hostile by assumption.
/// </remarks>
public static class ShellLink
{
    private const int HeaderSize = 0x4C;

    // LinkFlags bits, in the order the optional blocks appear after the header.
    private const uint HasLinkTargetIdList = 1 << 0;
    private const uint HasLinkInfo = 1 << 1;
    private const uint HasName = 1 << 2;
    private const uint HasRelativePath = 1 << 3;
    private const uint HasWorkingDir = 1 << 4;
    private const uint HasArguments = 1 << 5;
    private const uint HasIconLocation = 1 << 6;
    private const uint IsUnicode = 1 << 7;

    /// <summary>Signature every shell link starts with, followed by its class identifier.</summary>
    private static readonly byte[] LinkClsid =
    {
        0x01, 0x14, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00,
        0xC0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x46,
    };

    /// <summary>Parses <paramref name="bytes"/>, or returns null when it is not a shell link.</summary>
    public static ShellLinkTarget? TryParse(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < HeaderSize
            || BitConverter.ToUInt32(bytes[..4]) != HeaderSize
            || !bytes.Slice(4, LinkClsid.Length).SequenceEqual(LinkClsid))
        {
            return null;
        }

        uint flags = BitConverter.ToUInt32(bytes.Slice(0x14, 4));
        int offset = HeaderSize;

        if ((flags & HasLinkTargetIdList) != 0)
        {
            // A two-byte size that does not count itself.
            if (offset + 2 > bytes.Length)
            {
                return null;
            }

            offset += 2 + BitConverter.ToUInt16(bytes.Slice(offset, 2));
        }

        if ((flags & HasLinkInfo) != 0)
        {
            // A four-byte size that does count itself.
            if (offset + 4 > bytes.Length)
            {
                return null;
            }

            offset += (int)BitConverter.ToUInt32(bytes.Slice(offset, 4));
        }

        bool unicode = (flags & IsUnicode) != 0;

        // Order is fixed by the format, so each present block has to be read to reach the next.
        string? name = ReadString(bytes, ref offset, flags, HasName, unicode);
        string? relative = ReadString(bytes, ref offset, flags, HasRelativePath, unicode);
        string? working = ReadString(bytes, ref offset, flags, HasWorkingDir, unicode);
        string? arguments = ReadString(bytes, ref offset, flags, HasArguments, unicode);
        string? icon = ReadString(bytes, ref offset, flags, HasIconLocation, unicode);

        _ = name;

        return new ShellLinkTarget
        {
            RelativePath = relative,
            Arguments = arguments,
            WorkingDirectory = working,
            IconLocation = icon,
        };
    }

    /// <summary>Reads one file, or returns null when it cannot be read or parsed.</summary>
    public static ShellLinkTarget? TryRead(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        try
        {
            // Bounded: a shortcut is a few kilobytes, and this runs over files on removable media
            // whose sizes nothing here controls.
            using FileStream stream = File.OpenRead(path);

            if (stream.Length is 0 or > 1024 * 1024)
            {
                return null;
            }

            byte[] bytes = new byte[stream.Length];
            stream.ReadExactly(bytes);
            return TryParse(bytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? ReadString(
        ReadOnlySpan<byte> bytes,
        ref int offset,
        uint flags,
        uint flag,
        bool unicode)
    {
        if ((flags & flag) == 0 || offset + 2 > bytes.Length)
        {
            return null;
        }

        // A character count, not a byte count - the distinction is the whole reason a
        // hand-rolled reader gets this wrong.
        int characters = BitConverter.ToUInt16(bytes.Slice(offset, 2));
        offset += 2;

        int byteCount = unicode ? characters * 2 : characters;

        if (byteCount < 0 || offset + byteCount > bytes.Length)
        {
            return null;
        }

        string value = unicode
            ? Encoding.Unicode.GetString(bytes.Slice(offset, byteCount))
            : Encoding.Default.GetString(bytes.Slice(offset, byteCount));

        offset += byteCount;
        return value;
    }
}
