namespace EVBlocker.Core.Audit;

/// <summary>
/// Which outcomes Windows records for an audit subcategory. Values match the numeric
/// "Setting Value" column that auditpol reports, so they can be read straight from it.
/// </summary>
[Flags]
public enum AuditSetting
{
    None = 0,
    Success = 1,
    Failure = 2,
}

/// <summary>
/// Controls the "Filtering Platform Connection" audit subcategory, which is what makes events
/// 5157 (blocked) and 5156 (allowed) appear in the Security log. Without it enabled, the history
/// feature has no data to read.
/// </summary>
public interface IAuditPolicy
{
    /// <summary>Current setting, so the original value can be restored rather than guessed at.</summary>
    AuditSetting GetConnectionAudit();

    /// <summary>
    /// Applies <paramref name="setting"/> to the subcategory. Requires administrator rights.
    /// </summary>
    void SetConnectionAudit(AuditSetting setting);
}
