using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Windows.Threading;

namespace LegitX.WPF.Services;

/// <summary>
/// Background monitor that continuously validates the Firebase session.
///
/// Checks performed every 90 seconds:
///   1. Firebase Auth — is the account still valid? (not deleted/disabled)
///   2. Firestore — can we reach the database? (server health check)
///
/// If the account is deleted/disabled → immediate forced logout.
/// If Firestore is completely unreachable for 3 consecutive checks → forced logout.
/// Network blips (no internet, timeout) are tolerated — no logout for those.
/// </summary>
public sealed class FirebaseSessionMonitor : IDisposable
{
    // Firestore URL now pulled from SecureStrings — no plaintext project ID in this file
    private static string FirestoreBaseUrl => SecureStrings.FirestoreBaseUrl;
    private static readonly HttpClient _http = SecureHttpClient.Create(TimeSpan.FromSeconds(10));

    private readonly DispatcherTimer _timer;
    private int _consecutiveFirestoreFailures;
    private const int MaxFirestoreFailures = 3;  // 3 consecutive failures = ~4.5 min of downtime
    private readonly DateTime _loginTimestamp = DateTime.UtcNow;  // When this session started

    /// <summary>
    /// Raised when the session is invalid and the user must be force-logged out.
    /// The string parameter contains the reason message.
    /// </summary>
    public event Action<string>? SessionInvalidated;

    /// <summary>
    /// Raised on each check cycle with the current status.
    /// True = healthy, False = warning (partial failure).
    /// </summary>
    public event Action<bool>? HealthStatusChanged;

    /// <summary>
    /// Raised when maintenance mode is detected. The string is the admin-supplied message.
    /// </summary>
    public event Action<string>? MaintenanceDetected;

    public FirebaseSessionMonitor()
    {
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(90) // Check every 90 seconds
        };
        _timer.Tick += async (_, _) => await RunCheckCycleAsync();
    }

    /// <summary>Start the background monitoring.</summary>
    public void Start()
    {
        _consecutiveFirestoreFailures = 0;
        _timer.Start();
    }

    /// <summary>Stop the background monitoring.</summary>
    public void Stop()
    {
        _timer.Stop();
    }

    /// <summary>
    /// Runs a single validation cycle:
    ///   Step 1: Validate Firebase Auth session (account exists + not disabled)
    ///   Step 2: Verify license is still active in Firestore
    ///   Step 3: Check Firestore connectivity
    /// </summary>
    private async Task RunCheckCycleAsync()
    {
        // ── Step 0a: Assembly integrity check ──
        if (System.Windows.Application.Current is App app && app.Security != null && !app.Security.QuickCheck())
        {
            _timer.Stop();
            SessionInvalidated?.Invoke(
                "Application integrity check failed.\n\n" +
                "The application binary appears to have been modified.\n" +
                "Please re-download LegitX V2 from the official website.");
            return;
        }

        // ── Step 0b: Check for admin "Force Logout All" command ──
        var forceLogout = await CheckForceLogoutAllAsync();
        if (forceLogout)
        {
            _timer.Stop();
            SessionInvalidated?.Invoke(
                "An administrator has logged out all users.\n\n" +
                "Please sign in again to continue using LegitX V2.");
            return;
        }

        // ── Step 0c: Check for maintenance mode ──
        var (maint, maintMsg) = await CheckMaintenanceModeAsync();
        if (maint)
        {
            _timer.Stop();
            MaintenanceDetected?.Invoke(maintMsg);
            return;
        }

        // ── Step 0d: Check for per-user force logout ──
        {
            var uid0 = AuthService.GetUserId();
            var idToken0 = AuthService.GetIdToken();
            if (!string.IsNullOrEmpty(uid0))
            {
                var perUserLogout = await CheckPerUserForceLogoutAsync(uid0, idToken0);
                if (perUserLogout)
                {
                    _timer.Stop();
                    SessionInvalidated?.Invoke(
                        "An administrator has logged you out.\n\n" +
                        "Please sign in again to continue using LegitX V2.");
                    return;
                }
            }
        }

        // ── Step 1: Firebase Auth validation ──
        var (valid, reason) = await AuthService.ValidateSessionAsync();

        if (!valid)
        {
            // Account deleted, disabled, or session permanently invalid → FORCE LOGOUT
            _timer.Stop();
            SessionInvalidated?.Invoke(reason);
            return;
        }

        // ── Step 2: License + Account Status verification ──
        // Re-read idToken AFTER ValidateSessionAsync (it may have refreshed an expired token)
        var uid = AuthService.GetUserId();
        var idToken = AuthService.GetIdToken();

        if (!string.IsNullOrEmpty(uid))
        {
            var licenseCheck = await LicenseService.VerifyLicenseAsync(uid, idToken);

            // Check for banned / suspended / deactivated (these return Licensed=false with NeedsCode=false)
            if (!licenseCheck.Licensed && !licenseCheck.NeedsCode
                && !licenseCheck.Reason.Contains("skipped", StringComparison.OrdinalIgnoreCase))
            {
                // Account banned, suspended, or deactivated → FORCE LOGOUT
                _timer.Stop();
                SessionInvalidated?.Invoke(licenseCheck.Reason);
                return;
            }

            if (!licenseCheck.Licensed && licenseCheck.NeedsCode
                && !licenseCheck.Reason.Contains("skipped", StringComparison.OrdinalIgnoreCase))
            {
                // License revoked by admin → FORCE LOGOUT
                _timer.Stop();
                SessionInvalidated?.Invoke(
                    "Your license has been revoked.\n\n" +
                    "An administrator has deactivated your access to LegitX V2.\n" +
                    "Please contact support at legitx.com if you believe this is an error.");
                return;
            }

            // ── Step 2b: HWID ban check (catches new accounts on banned hardware) ──
            var hwidBan = await LicenseService.CheckHwidBanAsync(idToken);
            if (hwidBan.Banned)
            {
                _timer.Stop();
                SessionInvalidated?.Invoke(hwidBan.Reason);
                return;
            }

            // ── Step 2c: Server-side session token validation (Layer 5) ──
            var (tokenValid, tokenReason) = await SessionTokenService.ValidateAsync(uid, idToken);
            if (!tokenValid)
            {
                _timer.Stop();
                SessionInvalidated?.Invoke(tokenReason);
                return;
            }

            // ── Step 2d: Encrypted result store validation (Layer 6) ──
            if (!EncryptedResultStore.IsLicenseValid() || !EncryptedResultStore.IsHwidValid())
            {
                _timer.Stop();
                SessionInvalidated?.Invoke(
                    "Session integrity check failed.\n\n" +
                    "The application's internal state has been modified.\n" +
                    "Please restart LegitX V2.");
                return;
            }
        }

        // ── Step 3: Firestore connectivity check ──
        var firestoreOk = await CheckFirestoreHealthAsync();

        if (firestoreOk)
        {
            // Reset failure counter on success
            if (_consecutiveFirestoreFailures > 0)
            {
                _consecutiveFirestoreFailures = 0;
                HealthStatusChanged?.Invoke(true); // Recovered
            }
        }
        else
        {
            _consecutiveFirestoreFailures++;
            HealthStatusChanged?.Invoke(false); // Warning

            if (_consecutiveFirestoreFailures >= MaxFirestoreFailures)
            {
                // Firestore has been unreachable for ~4.5 minutes → force logout
                _timer.Stop();
                SessionInvalidated?.Invoke(
                    "Unable to connect to LegitX servers.\n\n" +
                    "The connection to the server has been lost for an extended period.\n" +
                    "Please check your internet and try again.");
            }
        }
    }

    /// <summary>
    /// Pings Firestore to verify the database is reachable.
    /// We do a lightweight GET on the user's own document — if the server responds
    /// (even with 404 Not Found), the server is alive. Only network-level failures count as down.
    /// </summary>
    private static async Task<bool> CheckFirestoreHealthAsync()
    {
        try
        {
            var uid = AuthService.GetUserId();
            var idToken = AuthService.GetIdToken();
            if (string.IsNullOrEmpty(uid)) return false;

            var url = $"{FirestoreBaseUrl}/users/{uid}";
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (!string.IsNullOrEmpty(idToken))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", idToken);

            var response = await _http.SendAsync(request).ConfigureAwait(false);

            // Any HTTP response means the server is alive
            // 200 = doc exists, 404 = doc doesn't exist yet (still means server is reachable)
            // 403 = auth issue (server is still alive)
            // Only network-level exceptions mean the server is truly down
            return true;
        }
        catch (HttpRequestException)
        {
            return false; // Network failure — server unreachable
        }
        catch (TaskCanceledException)
        {
            return false; // Timeout — server unresponsive
        }
        catch
        {
            return true; // Unknown error — don't count as server down
        }
    }

    public void Dispose()
    {
        _timer.Stop();
    }

    /// <summary>
    /// Checks if an admin has triggered "Force Logout All" since this session started.
    /// Reads system_config/force_logout from Firestore and compares the timestamp
    /// to this session's login time. If the force_logout timestamp is newer, returns true.
    /// </summary>
    private async Task<bool> CheckForceLogoutAllAsync()
    {
        try
        {
            var idToken = AuthService.GetIdToken();
            var url = $"{FirestoreBaseUrl}/system_config/force_logout";
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (!string.IsNullOrEmpty(idToken))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", idToken);

            var response = await _http.SendAsync(request).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode) return false;

            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(json)) return false;

            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("fields", out var fields)) return false;

            // Read the "triggeredAt" field (stored as ISO string)
            if (!fields.TryGetProperty("triggeredAt", out var triggeredAtField)) return false;
            string? triggeredAtStr = null;
            if (triggeredAtField.TryGetProperty("stringValue", out var sv))
                triggeredAtStr = sv.GetString();

            if (string.IsNullOrEmpty(triggeredAtStr)) return false;

            if (DateTime.TryParse(triggeredAtStr, null,
                System.Globalization.DateTimeStyles.RoundtripKind, out var triggeredAt))
            {
                // If the force logout was triggered AFTER this session started → force logout
                return triggeredAt.ToUniversalTime() > _loginTimestamp;
            }

            return false;
        }
        catch
        {
            return false; // Network errors — don't force logout
        }
    }

    /// <summary>
    /// Checks system_config/maintenance in Firestore.
    /// Returns (true, message) if maintenance mode is enabled.
    /// </summary>
    private static async Task<(bool Enabled, string Message)> CheckMaintenanceModeAsync()
    {
        try
        {
            var idToken = AuthService.GetIdToken();
            var url = $"{FirestoreBaseUrl}/system_config/maintenance";
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (!string.IsNullOrEmpty(idToken))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", idToken);

            var response = await _http.SendAsync(request).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return (false, "");

            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(json)) return (false, "");

            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("fields", out var fields)) return (false, "");

            // Check "enabled" field
            if (fields.TryGetProperty("enabled", out var enabledField)
                && enabledField.TryGetProperty("booleanValue", out var bv)
                && bv.GetBoolean())
            {
                var msg = "LegitX V2 is currently under maintenance. Please try again later.";
                if (fields.TryGetProperty("message", out var msgField)
                    && msgField.TryGetProperty("stringValue", out var msv))
                {
                    var custom = msv.GetString();
                    if (!string.IsNullOrWhiteSpace(custom)) msg = custom;
                }
                return (true, msg);
            }

            return (false, "");
        }
        catch
        {
            return (false, "");
        }
    }

    /// <summary>
    /// Checks if an admin has force-logged out this specific user.
    /// Reads users/{uid}.forceLogoutAt and compares to session login time.
    /// </summary>
    private async Task<bool> CheckPerUserForceLogoutAsync(string uid, string? idToken)
    {
        try
        {
            var url = $"{FirestoreBaseUrl}/users/{uid}";
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (!string.IsNullOrEmpty(idToken))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", idToken);

            var response = await _http.SendAsync(request).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return false;

            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(json)) return false;

            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("fields", out var fields)) return false;

            if (!fields.TryGetProperty("forceLogoutAt", out var fla)) return false;

            string? logoutAtStr = null;
            if (fla.TryGetProperty("stringValue", out var sv))
                logoutAtStr = sv.GetString();

            if (string.IsNullOrEmpty(logoutAtStr)) return false;

            if (DateTime.TryParse(logoutAtStr, null,
                System.Globalization.DateTimeStyles.RoundtripKind, out var logoutAt))
            {
                return logoutAt.ToUniversalTime() > _loginTimestamp;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Static one-shot maintenance check (called before UI opens).
    /// Returns (true, message) if maintenance is on.
    /// </summary>
    public static async Task<(bool Enabled, string Message)> CheckMaintenanceOnceAsync()
    {
        return await CheckMaintenanceModeAsync().ConfigureAwait(false);
    }
}
