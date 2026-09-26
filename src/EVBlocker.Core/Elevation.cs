using System.Security.Principal;

namespace EVBlocker.Core;

/// <summary>
/// Whether this process can perform the privileged operations in this assembly.
/// </summary>
/// <remarks>
/// Lives in Core rather than in the UI because Core is where the privileged work happens, and
/// because checking first turns an opaque failure into a sentence the user can act on: netsh and
/// auditpol report elevation failures as a localised message and a non-zero exit code, which
/// cannot be told apart from any other failure without reading text in whatever language the
/// machine is set to.
/// </remarks>
public static class Elevation
{
    private static readonly Lazy<bool> Elevated = new(Detect);

    public static bool IsElevated => Elevated.Value;

    /// <summary>Throws with a message naming the operation, when not elevated.</summary>
    public static void Require(string operation)
    {
        if (!IsElevated)
        {
            throw new UnauthorizedAccessException(
                $"Cannot {operation}: this requires running EVBlocker as administrator.");
        }
    }

    private static bool Detect()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }
}
