namespace EVBlocker.Core.History;

/// <summary>
/// Translates a kernel device path into a drive-letter path.
///
/// WFP audit events report the executable as <c>\device\harddiskvolume3\windows\...</c>, while
/// firewall rules and the allow-list use <c>C:\windows\...</c>. Without this translation the two
/// never match, so history could never be joined to the allow-list.
/// </summary>
public interface IDevicePathMapper
{
    /// <summary>
    /// Returns the drive-letter form of <paramref name="path"/>, or the input unchanged when no
    /// mapping applies (an already-normal path, or a device with no drive letter such as a
    /// network redirector).
    /// </summary>
    string Normalize(string path);
}
