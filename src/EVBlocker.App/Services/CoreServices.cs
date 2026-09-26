using EVBlocker.Core.Baseline;
using EVBlocker.Core.Firewall;
using EVBlocker.Core.Policy;
using EVBlocker.Core.Safety;
using EVBlocker.Core.Startup;

namespace EVBlocker.App.Services;

/// <summary>
/// Builds the Core objects the application uses.
/// </summary>
/// <remarks>
/// One place, because the window and the headless reconcile mode both need the same graph and
/// must agree about it. Two constructions that drift apart would mean the policy the UI shows is
/// not the policy the boot-time run applies.
/// </remarks>
internal static class CoreServices
{
    public static AllowListStore CreateAllowListStore() => new(AllowListStore.DefaultPath);

    public static EnforcementController CreateEnforcementController() => new(
        new WindowsFirewallPolicy(),
        new ConfigBackup(ConfigBackup.DefaultDirectory),
        new ScheduledTaskDeadManSwitch(),
        new OsBaseline());

    public static PolicyReconciler CreateReconciler() => new(
        CreateEnforcementController(),
        new WindowsFirewallPolicy(),
        CreateAllowListStore());
}
