using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;

namespace EVBlocker.App.Services;

/// <summary>Which product an executable belongs to and who published it.</summary>
public sealed record SoftwareDescription(string Product, string Publisher);

/// <summary>
/// Reads the version resource of an executable, so a row can say "Google Chrome" instead of
/// only "chrome.exe" - a file name alone does not tell anyone which software is asking.
/// </summary>
/// <remarks>
/// Cached per path: the live grid builds its rows again every second, and each read opens the
/// file. The answer only changes when the file is replaced, which a restart of the app covers.
/// </remarks>
public static class ExecutableDescriptions
{
    private static readonly SoftwareDescription Unknown = new("—", "—");

    private static readonly ConcurrentDictionary<string, SoftwareDescription> Cache =
        new(StringComparer.OrdinalIgnoreCase);

    public static SoftwareDescription Get(string? executablePath) =>
        string.IsNullOrWhiteSpace(executablePath) ? Unknown : Cache.GetOrAdd(executablePath, Read);

    private static SoftwareDescription Read(string path)
    {
        FileVersionInfo info;
        try
        {
            info = FileVersionInfo.GetVersionInfo(path);
        }
        catch (FileNotFoundException)
        {
            return Unknown;
        }
        catch (IOException)
        {
            return Unknown;
        }
        catch (UnauthorizedAccessException)
        {
            return Unknown;
        }

        // Many helper executables leave the product name empty but describe themselves.
        string product = Clean(info.ProductName) ?? Clean(info.FileDescription) ?? "—";
        string publisher = Clean(info.CompanyName) ?? "—";

        return new SoftwareDescription(product, publisher);
    }

    /// <summary>Drops the ® and ™ marks that make Microsoft's names hard to scan in a column.</summary>
    private static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Replace("®", string.Empty, StringComparison.Ordinal)
            .Replace("™", string.Empty, StringComparison.Ordinal)
            .Replace("(R)", string.Empty, StringComparison.Ordinal)
            .Replace("(TM)", string.Empty, StringComparison.Ordinal)
            .Trim();
    }
}
