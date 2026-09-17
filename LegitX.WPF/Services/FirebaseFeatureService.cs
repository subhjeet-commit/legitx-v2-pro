using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace LegitX.WPF.Services;

/// <summary>
/// Connects to Firestore Cloud Database to read per-user feature flags.
///
/// Firestore document structure:
///   Collection: users → Document: {uid} → Sub-collection: features → Document: flags
///     fields:
///       bypass         : boolean  (true/false)
///       streamerMode   : boolean  (true/false)
///       formBypass     : boolean  (true/false)
///       stopNetwork    : boolean  (true/false)
///
/// Alternatively stored as fields in: users/{uid}/features/flags
///
/// When a feature flag is missing or the document doesn't exist, the default is FALSE (disabled).
/// The admin must explicitly enable features per user via the admin panel.
/// </summary>
public static class FirebaseFeatureService
{
    // Firestore URL now pulled from SecureStrings — no plaintext project ID in this file
    private static string FirestoreBaseUrl => SecureStrings.FirestoreBaseUrl;
    private static readonly HttpClient _http = SecureHttpClient.Create(TimeSpan.FromSeconds(10));

    /// <summary>
    /// Feature flag keys — must match the keys stored in the Firestore document.
    /// </summary>
    public const string KeyBypass = "bypass";
    public const string KeyStreamerMode = "streamerMode";
    public const string KeyFormBypass = "formBypass";
    public const string KeyStopNetwork = "stopNetwork";

    /// <summary>All feature keys for convenience.</summary>
    public static readonly string[] AllKeys = { KeyBypass, KeyStreamerMode, KeyFormBypass, KeyStopNetwork };

    // ═══════════════════════════════════════════════════════════════
    //  FIRESTORE HELPERS
    // ═══════════════════════════════════════════════════════════════

    /// <summary>Creates an HttpRequestMessage with Bearer auth header.</summary>
    private static HttpRequestMessage CreateRequest(HttpMethod method, string url, string? idToken)
    {
        var request = new HttpRequestMessage(method, url);
        if (!string.IsNullOrEmpty(idToken))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", idToken);
        return request;
    }

    /// <summary>Extracts a boolean value from a Firestore field element. Defaults to false if missing/invalid.</summary>
    private static bool GetFirestoreBool(JsonElement fields, string fieldName, bool defaultValue = false)
    {
        if (fields.TryGetProperty(fieldName, out var field) &&
            field.TryGetProperty("booleanValue", out var val))
            return val.GetBoolean();
        return defaultValue;
    }

    /// <summary>
    /// Builds the Firestore JSON document body with typed field values.
    /// </summary>
    private static string BuildFirestoreDocument(Dictionary<string, object> fieldValues)
    {
        var fields = new Dictionary<string, object>();
        foreach (var kvp in fieldValues)
        {
            fields[kvp.Key] = kvp.Value switch
            {
                bool b => new { booleanValue = b },
                string s => new { stringValue = s },
                _ => new { stringValue = kvp.Value?.ToString() ?? "" },
            };
        }
        return JsonSerializer.Serialize(new { fields });
    }

    // ═══════════════════════════════════════════════════════════════
    //  FEATURE FLAG OPERATIONS
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Fetches the feature flags for a specific user from Firestore Cloud Database.
    /// Document path: users/{uid}/features/flags
    /// Returns a dictionary of feature key → enabled (true = visible, false = hidden).
    /// If the user has no entry or the DB is unreachable, all features default to FALSE (disabled).
    /// </summary>
    public static async Task<Dictionary<string, bool>> FetchFeatureFlagsAsync(string uid, string? idToken = null)
    {
        // Default: all features DISABLED — admin must explicitly enable per user
        var flags = new Dictionary<string, bool>
        {
            [KeyBypass] = false,
            [KeyStreamerMode] = false,
            [KeyFormBypass] = false,
            [KeyStopNetwork] = false,
        };

        if (string.IsNullOrEmpty(uid)) return flags;

        try
        {
            var url = $"{FirestoreBaseUrl}/users/{uid}/features/flags";
            var request = CreateRequest(HttpMethod.Get, url, idToken);

            var response = await _http.SendAsync(request).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return flags;

            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(json)) return flags;

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("fields", out var fields)) return flags;

            foreach (var key in AllKeys)
            {
                flags[key] = GetFirestoreBool(fields, key, false);
            }
        }
        catch
        {
            // Network failure / parse error → all features stay disabled
        }

        return flags;
    }

    /// <summary>
    /// Initializes default feature flags for a new user in Firestore.
    /// Document path: users/{uid}/features/flags
    /// Called once after first login to create the document so the admin website can toggle them.
    /// Does NOT overwrite existing values — only creates if the document doesn't exist.
    /// </summary>
    public static async Task InitializeUserFeaturesAsync(string uid, string? idToken = null)
    {
        if (string.IsNullOrEmpty(uid)) return;

        try
        {
            // First check if the document already exists
            var checkUrl = $"{FirestoreBaseUrl}/users/{uid}/features/flags";
            var checkRequest = CreateRequest(HttpMethod.Get, checkUrl, idToken);
            var checkResponse = await _http.SendAsync(checkRequest).ConfigureAwait(false);

            if (checkResponse.IsSuccessStatusCode)
            {
                var existing = await checkResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(existing))
                {
                    using var existDoc = JsonDocument.Parse(existing);
                    if (existDoc.RootElement.TryGetProperty("fields", out _))
                        return; // Already has feature flags — don't overwrite
                }
            }

            // Create default flags document (all DISABLED — admin enables per user)
            var fieldValues = new Dictionary<string, object>
            {
                [KeyBypass] = false,
                [KeyStreamerMode] = false,
                [KeyFormBypass] = false,
                [KeyStopNetwork] = false,
            };

            var patchUrl = $"{FirestoreBaseUrl}/users/{uid}/features/flags";
            var body = BuildFirestoreDocument(fieldValues);
            var content = new StringContent(body, Encoding.UTF8, "application/json");

            var request = CreateRequest(HttpMethod.Patch, patchUrl, idToken);
            request.Content = content;
            await _http.SendAsync(request).ConfigureAwait(false);
        }
        catch
        {
            // Silently ignore — feature flags will default to all-enabled
        }
    }

    /// <summary>
    /// Writes the user's email to their Firestore user document for admin reference.
    /// Document path: users/{uid}
    /// Only updates the 'email' field without touching other fields.
    /// </summary>
    public static async Task StoreUserEmailAsync(string uid, string email, string? idToken = null)
    {
        if (string.IsNullOrEmpty(uid) || string.IsNullOrEmpty(email)) return;

        try
        {
            // PATCH with updateMask to only update the email field
            var url = $"{FirestoreBaseUrl}/users/{uid}?updateMask.fieldPaths=email";

            var fieldValues = new Dictionary<string, object>
            {
                ["email"] = email,
            };

            var body = BuildFirestoreDocument(fieldValues);
            var content = new StringContent(body, Encoding.UTF8, "application/json");

            var request = CreateRequest(HttpMethod.Patch, url, idToken);
            request.Content = content;
            await _http.SendAsync(request).ConfigureAwait(false);
        }
        catch { /* silent */ }
    }
}
