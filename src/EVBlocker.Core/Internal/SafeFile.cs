namespace EVBlocker.Core.Internal;

internal static class SafeFile
{
    /// <summary>
    /// Deletes a file, ignoring failure.
    /// </summary>
    /// <remarks>
    /// Every caller is cleaning up a temporary file in a finally block. A leftover temp file is
    /// harmless, and throwing from there would replace the real failure with an irrelevant one.
    /// </remarks>
    /// <returns>True when the file is gone afterwards.</returns>
    public static bool TryDelete(string path)
    {
        try
        {
            File.Delete(path);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
