using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace LegitX.WPF.Services;

/// <summary>
/// License / Redemption Code verification via Firestore Cloud Database.
///
/// ═══════════════════════════════════════════════════════════════════
///  FIRESTORE STRUCTURE
/// ═══════════════════════════════════════════════════════════════════
///
///  Collection: licenses → Document: {CODE}
///    fields:
///      active       : boolean  (true = code is valid)
///      uid          : string   (empty = not redeemed, or Firebase UID of redeemer)
///      redeemedAt   : string   (ISO timestamp when redeemed)
///      createdAt    : string   (ISO timestamp when admin created the code)
///      plan         : string   ("pro", "standard", etc.)
///
///  Collection: users → Document: {uid}
///    fields:
///      licenseCode  : string   (the code the user redeemed)
///      licensed     : boolean  (true = licensed user)
///      email        : string   (already stored by FirebaseFeatureService)
///
/// ═══════════════════════════════════════════════════════════════════
///  FLOW
/// ═══════════════════════════════════════════════════════════════════
///
///   1. User logs in (email or Google)
///   2. HWID check passes
///   3. License check:
///      a) Read users/{uid} → if "licensed" == true → ✅ PASS
///      b) If not licensed → prompt for redemption code
///      c) User enters code → check licenses/{CODE}:
///         - Code doesn't exist → ❌ INVALID CODE
///         - Code exists but active=false → ❌ CODE DEACTIVATED
///         - Code exists, active=true, uid is not empty and ≠ current uid → ❌ ALREADY USED
///         - Code exists, active=true, uid is empty OR uid == current uid → ✅ REDEEM
///      d) On redeem: write uid + redeemedAt to licenses/{CODE},
///         write licenseCode + licensed=true to users/{uid}
///
///   4. Session monitor also checks "licensed" field every 90 seconds.
///      Admin can revoke by setting licensed=false → instant kick.
/// </summary>
public static class LicenseService
{
    // Firestore URL now pulled from SecureStrings — no plaintext project ID in this file
    private static string FirestoreBaseUrl => SecureStrings.FirestoreBaseUrl;
    private static readonly HttpClient _http = SecureHttpClient.Create(TimeSpan.FromSeconds(12));

    // ═══════════════════════════════════════════════════════════════
    //  HELPERS
    // ═══════════════════════════════════════════════════════════════

    private static HttpRequestMessage CreateRequest(HttpMethod method, string url, string? idToken)
    {
        var request = new HttpRequestMessage(method, url);
        if (!string.IsNullOrEmpty(idToken))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", idToken);
        return request;
    }

    private static string? GetFirestoreString(JsonElement fields, string fieldName)
    {
        if (fields.TryGetProperty(fieldName, out var field) &&
            field.TryGetProperty("stringValue", out var val))
            return val.GetString();
        return null;
    }

    private static bool GetFirestoreBool(JsonElement fields, string fieldName, bool defaultValue = false)
    {
        if (fields.TryGetProperty(fieldName, out var field) &&
            field.TryGetProperty("booleanValue", out var val))
            return val.GetBoolean();
        return defaultValue;
    }

    private static int GetFirestoreInt(JsonElement fields, string fieldName, int defaultValue = 0)
    {
        if (fields.TryGetProperty(fieldName, out var field))
        {
            if (field.TryGetProperty("integerValue", out var val))
            {
                if (val.ValueKind == JsonValueKind.String && int.TryParse(val.GetString(), out var i))
                    return i;
                if (val.ValueKind == JsonValueKind.Number)
                    return val.GetInt32();
            }
        }
        return defaultValue;
    }

    private static DateTime? GetFirestoreTimestamp(JsonElement fields, string fieldName)
    {
        // Firestore timestamps can be stored as:
        //   { "timestampValue": "2025-01-15T10:30:00Z" }
        //   { "stringValue": "2025-01-15T10:30:00.0000000+00:00" }
        if (fields.TryGetProperty(fieldName, out var field))
        {
            if (field.TryGetProperty("timestampValue", out var tsVal))
            {
                var s = tsVal.GetString();
                if (!string.IsNullOrEmpty(s) && DateTime.TryParse(s, null,
                    System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
                    return dt.ToUniversalTime();
            }
            if (field.TryGetProperty("stringValue", out var strVal))
            {
                var s = strVal.GetString();
                if (!string.IsNullOrEmpty(s) && DateTime.TryParse(s, null,
                    System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
                    return dt.ToUniversalTime();
            }
        }
        return null;
    }

    // ═══════════════════════════════════════════════════════════════
    //  STATUS TYPES
    // ═══════════════════════════════════════════════════════════════

    public enum AccountIssue
    {
        None,
        Banned,
        Suspended,
        Deactivated,
        TrialExpired,
        LicenseRevoked,
        NoLicense,
        HwidBanned,
        SessionInvalid,
        ServerDown
    }

    // ═══════════════════════════════════════════════════════════════
    //  LICENSE VERIFICATION
    // ═══════════════════════════════════════════════════════════════

    public record LicenseCheckResult(
        bool Licensed,
        string Reason,
        bool NeedsCode,
        AccountIssue Issue = AccountIssue.None,
        string? LicensePlan = null,
        int TrialDaysTotal = 0,
        int TrialDaysRemaining = 0,
        DateTime? TrialExpiresAt = null,
        bool KernelAccessAllowed = true);

    /// <summary>
    /// Checks if the user has a valid license in Firestore.
    /// Also checks account status (deactivated, suspended, banned).
    /// Returns Licensed=true if users/{uid}.licensed == true AND status is "active" (or missing).
    /// Returns NeedsCode=true if the user needs to enter a redemption code.
    /// </summary>
    public static async Task<LicenseCheckResult> VerifyLicenseAsync(string uid, string? idToken = null)
    {
        var result = await VerifyLicenseInternalAsync(uid, idToken).ConfigureAwait(false);

        // ── Layer 6: Store encrypted result so memory-patching 'Licensed=true' is useless ──
        EncryptedResultStore.StoreLicense(result.Licensed, result.NeedsCode, result.Reason);

        return result;
    }

    private static async Task<LicenseCheckResult> VerifyLicenseInternalAsync(string uid, string? idToken = null)
    {
        if (string.IsNullOrEmpty(uid))
            return new LicenseCheckResult(false, "Invalid user ID.", false, AccountIssue.SessionInvalid);

        // ── Integrity gate — if binary is tampered, license is never valid ──
        if (System.Windows.Application.Current is App app && app.Security != null && !app.Security.QuickCheck())
            return new LicenseCheckResult(false, "Application integrity check failed.", false, AccountIssue.SessionInvalid);

        try
        {
            // Read the user document to check "licensed" and "status" fields
            var url = $"{FirestoreBaseUrl}/users/{uid}";
            var request = CreateRequest(HttpMethod.Get, url, idToken);
            var response = await _http.SendAsync(request).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(json))
                {
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("fields", out var fields))
                    {
                        // ── Check account status first ──
                        var status = GetFirestoreString(fields, "status") ?? "active";

                        if (status == "banned")
                            return new LicenseCheckResult(false,
                                "Your account and hardware have been blocked by an administrator.\n" +
                                "This ban is tied to your PC hardware — creating a new account " +
                                "will not bypass this restriction.",
                                false, AccountIssue.Banned);

                        if (status == "suspended")
                            return new LicenseCheckResult(false,
                                "An administrator has suspended your account.\n" +
                                "Your license has been revoked and all features are disabled.",
                                false, AccountIssue.Suspended);

                        if (status == "deactivated")
                            return new LicenseCheckResult(false,
                                "An administrator has deactivated your account.\n" +
                                "All features have been disabled.",
                                false, AccountIssue.Deactivated);

                        // ── Check license ──
                        var licensed = GetFirestoreBool(fields, "licensed");
                        if (!licensed)
                        {
                            // User exists but not licensed
                            return new LicenseCheckResult(false,
                                "No active license found for this account.", true, AccountIssue.NoLicense);
                        }

                        // ── Licensed = true → Now check if it's a trial and if it's expired ──
                        var licenseCode = GetFirestoreString(fields, "licenseCode");
                        bool kernelAccess = true; // default: allowed

                        if (!string.IsNullOrEmpty(licenseCode))
                        {
                            var trialResult = await CheckTrialExpiryAsync(licenseCode, uid, idToken).ConfigureAwait(false);
                            if (trialResult != null)
                                return trialResult;

                            // Read kernelAccess from the license document
                            kernelAccess = await ReadKernelAccessAsync(licenseCode, idToken).ConfigureAwait(false);
                        }

                        return new LicenseCheckResult(true, "License valid.", false, AccountIssue.None,
                            KernelAccessAllowed: kernelAccess);
                    }
                }
            }

            // 404 = user doc doesn't exist → needs code
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return new LicenseCheckResult(false,
                    "Please enter your redemption code to activate LegitX V2.", true, AccountIssue.NoLicense);
            }

            // 401/403 or other server error → token may be expired or transient issue
            // Treat as "skipped" so the app doesn't falsely kick the user
            return new LicenseCheckResult(true, "License check skipped (server error).", false);
        }
        catch (HttpRequestException)
        {
            // No internet — allow through (session monitor will catch it later)
            return new LicenseCheckResult(true, "License check skipped (no internet).", false);
        }
        catch (TaskCanceledException)
        {
            return new LicenseCheckResult(true, "License check skipped (timeout).", false);
        }
        catch
        {
            return new LicenseCheckResult(true, "License check skipped (error).", false);
        }
    }

    /// <summary>
    /// Reads the license document and checks if a trial license has expired.
    /// Returns null if the license is permanent or still valid.
    /// Returns a LicenseCheckResult if the trial has expired (auto-revokes the user).
    /// </summary>
    private static async Task<LicenseCheckResult?> CheckTrialExpiryAsync(string code, string uid, string? idToken)
    {
        try
        {
            var licenseUrl = $"{FirestoreBaseUrl}/licenses/{code}";
            var request = CreateRequest(HttpMethod.Get, licenseUrl, idToken);
            var response = await _http.SendAsync(request).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                return null; // Can't read license doc — don't block

            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(json)) return null;

            using var licDoc = JsonDocument.Parse(json);
            if (!licDoc.RootElement.TryGetProperty("fields", out var fields))
                return null;

            var plan = GetFirestoreString(fields, "plan") ?? "permanent";

            // Permanent licenses never expire
            if (plan != "trial")
                return null;

            var trialDays = GetFirestoreInt(fields, "trialDays", 0);
            if (trialDays <= 0)
                return null; // No trial days set — treat as permanent

            // Get redemption date
            var redeemedAt = GetFirestoreTimestamp(fields, "redeemedAt");
            if (redeemedAt == null)
                return null; // Not yet redeemed — shouldn't happen but don't block

            // Calculate expiry
            var expiresAt = redeemedAt.Value.AddDays(trialDays);
            var now = DateTime.UtcNow;
            var remaining = (expiresAt - now).TotalDays;

            if (remaining <= 0)
            {
                // ── TRIAL EXPIRED — auto-revoke the license ──
                await RevokeExpiredTrialAsync(uid, code, idToken).ConfigureAwait(false);

                return new LicenseCheckResult(
                    Licensed: false,
                    Reason: "Your trial period has expired.\n\n" +
                            "To continue using LegitX V2, please purchase a permanent license " +
                            "or contact your reseller for a new key.",
                    NeedsCode: false,
                    Issue: AccountIssue.TrialExpired,
                    LicensePlan: "trial",
                    TrialDaysTotal: trialDays,
                    TrialDaysRemaining: 0,
                    TrialExpiresAt: expiresAt);
            }

            // Trial is still valid — return null so normal flow continues
            // (The caller treats null as "no problem found")
            return null;
        }
        catch
        {
            return null; // Network errors — don't block
        }
    }

    /// <summary>
    /// Reads the kernelAccess field from a license document.
    /// Returns true (allowed) by default if the field is missing or unreadable.
    /// </summary>
    private static async Task<bool> ReadKernelAccessAsync(string code, string? idToken)
    {
        try
        {
            var url = $"{FirestoreBaseUrl}/licenses/{code}";
            var request = CreateRequest(HttpMethod.Get, url, idToken);
            var response = await _http.SendAsync(request).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode) return true;

            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(json)) return true;

            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("fields", out var fields)) return true;

            return GetFirestoreBool(fields, "kernelAccess", true);
        }
        catch
        {
            return true; // Network errors — default to allowed
        }
    }

    /// <summary>
    /// When a trial expires, revoke the user's license in Firestore so they
    /// can't keep using it. Also deactivates the license code.
    /// </summary>
    private static async Task RevokeExpiredTrialAsync(string uid, string code, string? idToken)
    {
        try
        {
            // Mark user as unlicensed
            var userFields = new Dictionary<string, object>
            {
                ["licensed"] = new { booleanValue = false },
            };
            var userUrl = $"{FirestoreBaseUrl}/users/{uid}?updateMask.fieldPaths=licensed";
            var userBody = JsonSerializer.Serialize(new { fields = userFields });
            var userContent = new StringContent(userBody, Encoding.UTF8, "application/json");
            var userPatch = CreateRequest(HttpMethod.Patch, userUrl, idToken);
            userPatch.Content = userContent;
            await _http.SendAsync(userPatch).ConfigureAwait(false);

            // Deactivate the license code so it can't be re-used
            var licFields = new Dictionary<string, object>
            {
                ["active"] = new { booleanValue = false },
            };
            var licUrl = $"{FirestoreBaseUrl}/licenses/{code}?updateMask.fieldPaths=active";
            var licBody = JsonSerializer.Serialize(new { fields = licFields });
            var licContent = new StringContent(licBody, Encoding.UTF8, "application/json");
            var licPatch = CreateRequest(HttpMethod.Patch, licUrl, idToken);
            licPatch.Content = licContent;
            await _http.SendAsync(licPatch).ConfigureAwait(false);
        }
        catch { /* Best effort — session monitor will catch next cycle */ }
    }

    // ═══════════════════════════════════════════════════════════════
    //  HWID BAN CHECK — checks banned_hwids collection
    // ═══════════════════════════════════════════════════════════════

    public record HwidBanCheckResult(bool Banned, string Reason, AccountIssue Issue = AccountIssue.None);

    /// <summary>
    /// Checks if the current PC's hardware hashes exist in the banned_hwids collection.
    /// This catches hardware-banned users even if they create a new account.
    /// </summary>
    public static async Task<HwidBanCheckResult> CheckHwidBanAsync(string? idToken = null)
    {
        try
        {
            // Get current PC hardware hashes
            var permanentHash = HardwareIdService.GetPermanentHardwareHash();
            var windowsHash = HardwareIdService.GetWindowsHash();

            // Check permanentHash first (most reliable)
            if (!string.IsNullOrEmpty(permanentHash))
            {
                var banned = await IsHashBannedAsync(permanentHash, idToken).ConfigureAwait(false);
                if (banned)
                    return new HwidBanCheckResult(true,
                        "The hardware of this computer has been blocked by an administrator.\n" +
                        "LegitX V2 cannot be used on this machine.\n\n" +
                        "This ban is permanent and hardware-based — reinstalling Windows, " +
                        "creating new accounts, or any other method will NOT bypass it.",
                        AccountIssue.HwidBanned);
            }

            // Also check windowsHash
            if (!string.IsNullOrEmpty(windowsHash))
            {
                var banned = await IsHashBannedAsync(windowsHash, idToken).ConfigureAwait(false);
                if (banned)
                    return new HwidBanCheckResult(true,
                        "The hardware of this computer has been blocked by an administrator.\n" +
                        "LegitX V2 cannot be used on this machine.",
                        AccountIssue.HwidBanned);
            }

            return new HwidBanCheckResult(false, "");
        }
        catch
        {
            // Network errors — don't block, session monitor will catch later
            return new HwidBanCheckResult(false, "");
        }
    }

    /// <summary>
    /// Checks if a specific hash exists in the banned_hwids Firestore collection.
    /// </summary>
    private static async Task<bool> IsHashBannedAsync(string hash, string? idToken)
    {
        try
        {
            var url = $"{FirestoreBaseUrl}/banned_hwids/{hash}";
            var request = CreateRequest(HttpMethod.Get, url, idToken);
            var response = await _http.SendAsync(request).ConfigureAwait(false);

            // If the document exists (200), the hash is banned
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                // Make sure it's a real document with fields (not an empty response)
                return !string.IsNullOrWhiteSpace(json) && json.Contains("fields");
            }

            return false; // 404 = not banned, other errors = assume not banned
        }
        catch
        {
            return false; // Network error — assume not banned
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  CODE REDEMPTION
    // ═══════════════════════════════════════════════════════════════

    public record RedeemResult(bool Success, string Message);

    /// <summary>
    /// Attempts to redeem a license code for the given user.
    ///
    /// Steps:
    ///   1. Read licenses/{code} document
    ///   2. Validate: exists, active=true, uid is empty or matches
    ///   3. Write uid + redeemedAt to licenses/{code}
    ///   4. Write licenseCode + licensed=true to users/{uid}
    /// </summary>
    public static async Task<RedeemResult> RedeemCodeAsync(string code, string uid, string? idToken = null)
    {
        if (string.IsNullOrWhiteSpace(code))
            return new RedeemResult(false, "Please enter a redemption code.");

        if (string.IsNullOrEmpty(uid))
            return new RedeemResult(false, "Invalid user session.");

        code = code.Trim().ToUpperInvariant(); // Normalize code

        try
        {
            // ── Step 1: Read the license document ──
            var licenseUrl = $"{FirestoreBaseUrl}/licenses/{code}";
            var readRequest = CreateRequest(HttpMethod.Get, licenseUrl, idToken);
            var readResponse = await _http.SendAsync(readRequest).ConfigureAwait(false);

            if (!readResponse.IsSuccessStatusCode)
            {
                var statusCode = (int)readResponse.StatusCode;
                if (statusCode == 404)
                    return new RedeemResult(false, "Invalid redemption code. Please check and try again.");
                return new RedeemResult(false, $"Could not verify code (error {statusCode}).");
            }

            var readJson = await readResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
            using var readDoc = JsonDocument.Parse(readJson);

            if (!readDoc.RootElement.TryGetProperty("fields", out var fields))
                return new RedeemResult(false, "Invalid redemption code.");

            // ── Step 2: Validate the code ──
            var active = GetFirestoreBool(fields, "active");
            if (!active)
                return new RedeemResult(false, "This code has been deactivated.");

            var existingUid = GetFirestoreString(fields, "uid") ?? "";
            if (!string.IsNullOrEmpty(existingUid) && existingUid != uid)
                return new RedeemResult(false, "This code has already been used by another account.");

            // Already redeemed by this user
            if (existingUid == uid)
            {
                // Just make sure the user doc is marked licensed
                await MarkUserLicensedAsync(uid, code, idToken).ConfigureAwait(false);
                return new RedeemResult(true, "Code already activated on this account.");
            }

            // ── Step 3: Write to licenses/{code} — bind to this user ──
            var now = DateTime.UtcNow;
            var nowStr = now.ToString("o");
            var licenseFieldsToWrite = new Dictionary<string, object>
            {
                ["uid"] = new { stringValue = uid },
                ["redeemedAt"] = new { stringValue = nowStr },
            };

            // For trial licenses, calculate and store the expiry date
            var plan = GetFirestoreString(fields, "plan") ?? "permanent";
            var trialDays = GetFirestoreInt(fields, "trialDays", 0);
            var fieldPaths = "updateMask.fieldPaths=uid&updateMask.fieldPaths=redeemedAt";

            if (plan == "trial" && trialDays > 0)
            {
                var expiresAt = now.AddDays(trialDays).ToString("o");
                licenseFieldsToWrite["expiresAt"] = new { stringValue = expiresAt };
                fieldPaths += "&updateMask.fieldPaths=expiresAt";
            }

            var licensePatchUrl = $"{licenseUrl}?{fieldPaths}";
            var licenseBody = JsonSerializer.Serialize(new { fields = licenseFieldsToWrite });
            var licenseContent = new StringContent(licenseBody, Encoding.UTF8, "application/json");

            var licensePatch = CreateRequest(HttpMethod.Patch, licensePatchUrl, idToken);
            licensePatch.Content = licenseContent;
            var licenseResp = await _http.SendAsync(licensePatch).ConfigureAwait(false);

            if (!licenseResp.IsSuccessStatusCode)
                return new RedeemResult(false, "Failed to activate code. Please try again.");

            // ── Step 4: Mark user as licensed ──
            await MarkUserLicensedAsync(uid, code, idToken).ConfigureAwait(false);

            // Build success message with trial info
            var successMsg = "✓ License activated successfully!";
            if (plan == "trial" && trialDays > 0)
                successMsg = $"✓ Trial license activated! You have {trialDays} days.";

            return new RedeemResult(true, successMsg);
        }
        catch (HttpRequestException)
        {
            return new RedeemResult(false, "No internet connection. Please check your network.");
        }
        catch (TaskCanceledException)
        {
            return new RedeemResult(false, "Connection timed out. Please try again.");
        }
        catch (Exception ex)
        {
            return new RedeemResult(false, $"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Marks the user document as licensed with the code.
    /// </summary>
    private static async Task MarkUserLicensedAsync(string uid, string code, string? idToken)
    {
        var userFields = new Dictionary<string, object>
        {
            ["licenseCode"] = new { stringValue = code },
            ["licensed"] = new { booleanValue = true },
        };

        var userUrl = $"{FirestoreBaseUrl}/users/{uid}?updateMask.fieldPaths=licenseCode&updateMask.fieldPaths=licensed";
        var userBody = JsonSerializer.Serialize(new { fields = userFields });
        var userContent = new StringContent(userBody, Encoding.UTF8, "application/json");

        var userPatch = CreateRequest(HttpMethod.Patch, userUrl, idToken);
        userPatch.Content = userContent;
        await _http.SendAsync(userPatch).ConfigureAwait(false);
    }
}
