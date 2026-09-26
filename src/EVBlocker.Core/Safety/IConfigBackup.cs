namespace EVBlocker.Core.Safety;

/// <summary>
/// Captures and restores the whole Windows Firewall configuration.
/// </summary>
/// <remarks>
/// An interface because the real implementation shells out to netsh and needs elevation, which
/// would make every test of the code above it require an administrator.
/// </remarks>
public interface IConfigBackup
{
    /// <summary>Exports the current configuration and prunes older exports.</summary>
    BackupInfo Create();

    /// <summary>Known backups, newest first.</summary>
    IReadOnlyList<BackupInfo> List();

    /// <summary>
    /// Replaces the entire configuration with a previously exported one. Wholesale: rules added
    /// since the export are gone afterwards, including rules other software created.
    /// </summary>
    void Restore(string path);
}
