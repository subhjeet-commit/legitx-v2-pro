using System.Diagnostics;

namespace LegitX.WPF.Services;

/// <summary>
/// Central security posture switches.
/// Default behavior:
/// - DEBUG: permissive (developer-friendly)
/// - RELEASE: strict fail-closed
///
/// Override with env var LEGITX_STRICT_SECURITY:
/// - "1"/"true"/"yes" => strict
/// - "0"/"false"/"no" => permissive
/// </summary>
internal static class SecurityPolicy
{
    private const string StrictEnvVar = "LEGITX_STRICT_SECURITY";

    internal static bool StrictFailClosed
    {
        get
        {
            var env = Environment.GetEnvironmentVariable(StrictEnvVar);
            if (!string.IsNullOrWhiteSpace(env))
            {
                if (env.Equals("1", StringComparison.OrdinalIgnoreCase) ||
                    env.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                    env.Equals("yes", StringComparison.OrdinalIgnoreCase))
                    return true;

                if (env.Equals("0", StringComparison.OrdinalIgnoreCase) ||
                    env.Equals("false", StringComparison.OrdinalIgnoreCase) ||
                    env.Equals("no", StringComparison.OrdinalIgnoreCase))
                    return false;
            }

#if DEBUG
            return false;
#else
            return true;
#endif
        }
    }
}
