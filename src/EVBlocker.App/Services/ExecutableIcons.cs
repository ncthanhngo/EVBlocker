using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace EVBlocker.App.Services;

/// <summary>
/// The icon Explorer shows for an executable, so a row can be recognised by sight rather than by
/// reading a file name.
/// </summary>
/// <remarks>
/// Asks the shell rather than reading the icon out of the file, so an executable that carries no
/// icon of its own gets the same generic one Explorer shows instead of a blank cell - which is
/// most helper processes, and a list that is half blank reads as broken rather than as informative.
///
/// SHFILEINFO is filled into a byte buffer read by offset, the same approach IpHlpApi.cs takes,
/// because its two fixed-length string fields make it non-blittable: declaring it as a struct
/// needs either unsafe fixed buffers or hand-written marshalling, and only the first field is
/// wanted here.
/// </remarks>
public static partial class ExecutableIcons
{
    /// <summary>Frozen images, shared by every row naming the same file.</summary>
    /// <remarks>
    /// The grids virtualise with recycling, so a converter runs again for every row that scrolls
    /// into view. Without this, showing icons would mean a disk read per row per scroll.
    /// </remarks>
    private static readonly ConcurrentDictionary<string, ImageSource?> Cache =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Returns the icon for <paramref name="executablePath"/>, or null if there is none.</summary>
    public static ImageSource? Get(string? executablePath) =>
        string.IsNullOrWhiteSpace(executablePath) ? null : Cache.GetOrAdd(executablePath, Extract);

    private static ImageSource? Extract(string path)
    {
        Span<byte> info = stackalloc byte[ShFileInfoSize];
        if (SHGetFileInfoW(path, 0, ref MemoryMarshal.GetReference(info), ShFileInfoSize, ShgfiIcon | ShgfiLargeIcon) == 0)
        {
            // Cached as null like any other answer. A path that resolves to nothing would
            // otherwise be looked up again on every single scroll.
            return null;
        }

        nint icon = MemoryMarshal.Read<nint>(info);
        if (icon == 0)
        {
            return null;
        }

        try
        {
            // The 32x32 icon drawn at 16 points. Downscaling stays sharp on a high-DPI display,
            // where the 16x16 icon would be the one getting stretched.
            BitmapSource image = Imaging.CreateBitmapSourceFromHIcon(
                icon,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());

            // Shared by every row with this path, so it is frozen: no per-render copy, and safe
            // to hand out from whichever thread asks next.
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is COMException or ArgumentException)
        {
            return null;
        }
        finally
        {
            DestroyIcon(icon);
        }
    }

    /// <summary>Retrieve the icon handle.</summary>
    private const uint ShgfiIcon = 0x000000100;

    /// <summary>The 32x32 icon rather than the 16x16 one. Zero because it is the default.</summary>
    private const uint ShgfiLargeIcon = 0x000000000;

    /// <summary>
    /// sizeof(SHFILEINFOW) on 64-bit: HICON 8, int 4, UINT 4, then WCHAR[260] and WCHAR[80].
    /// </summary>
    /// <remarks>Passed as the declared size, so it has to be the exact figure, not an upper bound.</remarks>
    private const int ShFileInfoSize = 8 + 4 + 4 + (260 * 2) + (80 * 2);

    /// <summary>The icon handle is the first field, so it is read from offset zero.</summary>
    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint SHGetFileInfoW(
        string pszPath,
        uint dwFileAttributes,
        ref byte psfi,
        uint cbFileInfo,
        uint uFlags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyIcon(nint hIcon);
}

/// <summary>Binds an executable path straight to its icon.</summary>
/// <remarks>
/// A converter rather than a property on each row: the four grids project four different record
/// types, and this keeps the lookup in one place instead of repeating it in each view-model.
/// </remarks>
[ValueConversion(typeof(string), typeof(ImageSource))]
public sealed class ExecutableIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        ExecutableIcons.Get(value as string);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
