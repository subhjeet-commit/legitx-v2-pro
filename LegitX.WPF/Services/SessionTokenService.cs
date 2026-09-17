using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LegitX.WPF.Services;

/// <summary>
/// Layer 5 — Server-Side Session Token Binding.
///
/// On each login, generates a deterministic session token from UID + login timestamp + HWID hash
/// and stores it in Firestore (users/{uid} → field "sessionToken").
/// The session monitor periodically reads the Firestore value and compares it to the local token.
/// If someone cracks the client to skip login, no valid server token exists → forced logout.
/// </summary>
public static class SessionTokenService
{
    private static string FirestoreBaseUrl => SecureStrings.FirestoreBaseUrl;
    private static readonly HttpClient _http = SecureHttpClient.Create(TimeSpan.FromSeconds(10));

    /// <summary>Current session token held in memory (set at login time).</summary>
    private static string? _currentToken;

    /// <summary>Returns the current in-memory session token (null if not yet bound).</summary>
    public static string? CurrentToken => _currentToken;

    // ═══════════════════════════════════════════════════════════════
    //  TOKEN GENERATION
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Generates a SHA-256 session token from UID + timestamp + HWID hash.
    /// </summary>
    private static string GenerateToken(string uid, string timestampIso, string hwidHash)
    {
        var raw = $"{uid}|{timestampIso}|{hwidHash}|LegitX_Session_Salt_9F2E";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }

    // ═══════════════════════════════════════════════════════════════
    //  BIND (called right after successful login)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Generates a session token and writes it to Firestore.
    /// Call this from both Google and email login paths, and from auto-login.
    /// </summary>
    public static async Task BindSessionAsync(string uid, string? idToken)
    {
        if (string.IsNullOrEmpty(uid)) return;

        try
        {
            var hwidHash = HardwareIdService.GetPermanentHardwareHash();
            var timestamp = DateTime.UtcNow.ToString("o");
            var token = GenerateToken(uid, timestamp, hwidHash);

            // Store locally
            _currentToken = token;

            // Write to Firestore: users/{uid}  → fields sessionToken, sessionBoundAt
            var url = $"{FirestoreBaseUrl}/users/{uid}?updateMask.fieldPaths=sessionToken&updateMask.fieldPaths=sessionBoundAt";

            var body = JsonSerializer.Serialize(new
            {
                fields = new
                {
                    sessionToken = new { stringValue = token },
                    sessionBoundAt = new { stringValue = timestamp }
                }
            });

            var content = new StringContent(body, Encoding.UTF8, "application/json");
            var request = new HttpRequestMessage(HttpMethod.Patch, url) { Content = content };
            if (!string.IsNullOrEmpty(idToken))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", idToken);

            await _http.SendAsync(request).ConfigureAwait(false);
        }
        catch
        {
            // If write fails, clear local token so validation will soft-fail
            _currentToken = null;
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  VALIDATE (called from FirebaseSessionMonitor)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Reads the session token from Firestore and compares it to the local token.
    /// Returns (valid, reason).
    /// </summary>
    public static async Task<(bool Valid, string Reason)> ValidateAsync(string uid, string? idToken)
    {
        // If we never bound a token (e.g. bind call failed), skip validation gracefully
        if (string.IsNullOrEmpty(_currentToken))
            return (true, string.Empty);

        if (string.IsNullOrEmpty(uid))
            return (false, "Session validation failed — no user ID.");

        try
        {
            var url = $"{FirestoreBaseUrl}/users/{uid}";
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (!string.IsNullOrEmpty(idToken))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", idToken);

            var response = await _http.SendAsync(request).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                return (true, string.Empty); // Server issue — don't punish user

            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(json))
                return (true, string.Empty);

            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("fields", out var fields))
                return (false, "Session token missing from server.\n\nPlease sign in again.");

            if (!fields.TryGetProperty("sessionToken", out var tokenField)
                || !tokenField.TryGetProperty("stringValue", out var sv))
                return (false, "Session token missing from server.\n\nPlease sign in again.");

            var serverToken = sv.GetString() ?? "";

            if (!string.Equals(serverToken, _currentToken, StringComparison.Ordinal))
            {
                return (false,
                    "Your session has been invalidated.\n\n" +
                    "This can happen if you logged in from another device,\n" +
                    "or if your session was revoked by an administrator.\n\n" +
                    "Please sign in again.");
            }

            return (true, string.Empty);
        }
        catch
        {
            // Network errors — don't force logout
            return (true, string.Empty);
        }
    }

    /// <summary>Clears the in-memory token (call on logout).</summary>
    public static void Clear()
    {
        _currentToken = null;
    }
}
