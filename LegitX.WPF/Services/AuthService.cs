using Microsoft.Win32;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LegitX.WPF.Services;

/// <summary>
/// Handles user authentication via Firebase Auth REST API.
/// Sessions persist in Windows Registry until explicit logout.
/// No Firebase SDK needed — pure HTTP calls.
/// </summary>
public static class AuthService
{
    private const string AUTH_KEY = @"SOFTWARE\LegitX V2\Auth";

    // ═══════════════════════════════════════════════════════════════
    //  All sensitive strings are now in SecureStrings.cs — no more
    //  plaintext API keys, OAuth secrets, or endpoint URLs in this file.
    //  This prevents extraction via string scanning tools (strings.exe,
    //  ILSpy string search, dnSpy string references, etc.)
    // ═══════════════════════════════════════════════════════════════

    private static readonly HttpClient Http = SecureHttpClient.Create(TimeSpan.FromSeconds(15));

    #region Session Persistence

    /// <summary>
    /// Checks if a user is already logged in (persistent session).
    /// </summary>
    public static bool IsLoggedIn()
    {
        if (!SecureStrings.IsConfigured) return false;

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(AUTH_KEY, false);
            if (key == null) return false;

            var email = key.GetValue("Email")?.ToString();
            var token = key.GetValue("SessionToken")?.ToString();
            var uid = key.GetValue("UserId")?.ToString();

            return !string.IsNullOrEmpty(email) &&
                   !string.IsNullOrEmpty(token) &&
                   !string.IsNullOrEmpty(uid);
        }
        catch { return false; }
    }

    /// <summary>
    /// Returns the currently logged-in user's display name or email.
    /// </summary>
    public static string GetLoggedInUser()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(AUTH_KEY, false);
            var display = key?.GetValue("DisplayName")?.ToString();
            var email = key?.GetValue("Email")?.ToString();
            // Show display name if set, otherwise email prefix
            if (!string.IsNullOrEmpty(display)) return display;
            if (!string.IsNullOrEmpty(email))
            {
                var atIndex = email.IndexOf('@');
                return atIndex > 0 ? email[..atIndex] : email;
            }
            return "";
        }
        catch { return ""; }
    }

    /// <summary>
    /// Returns the Firebase UID of the currently logged-in user, or empty string.
    /// </summary>
    public static string GetUserId()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(AUTH_KEY, false);
            return key?.GetValue("UserId")?.ToString() ?? "";
        }
        catch { return ""; }
    }

    /// <summary>
    /// Returns the Firebase ID token for authenticated API calls.
    /// </summary>
    public static string GetIdToken()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(AUTH_KEY, false);
            return key?.GetValue("IdToken")?.ToString() ?? "";
        }
        catch { return ""; }
    }

    /// <summary>
    /// Returns the Firebase refresh token for re-validating the session.
    /// </summary>
    public static string GetRefreshToken()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(AUTH_KEY, false);
            return key?.GetValue("RefreshToken")?.ToString() ?? "";
        }
        catch { return ""; }
    }

    /// <summary>
    /// Validates the current session by calling Firebase Auth lookup.
    /// Returns (valid, reason) — if the account was deleted/disabled, valid=false.
    /// Also refreshes the idToken using the refresh token if the current one is expired.
    /// </summary>
    public static async Task<(bool valid, string reason)> ValidateSessionAsync()
    {
        if (!SecureStrings.IsConfigured)
            return (false, SecureStrings.ConfigurationMessage);

        try
        {
            var idToken = GetIdToken();
            if (string.IsNullOrEmpty(idToken))
                return (false, "No session token found.");

            // Try looking up the user with current idToken
            var payload = JsonSerializer.Serialize(new { idToken });
            var response = await Http.PostAsync(
                SecureStrings.GetUserUrl + SecureStrings.FirebaseApiKey,
                new StringContent(payload, Encoding.UTF8, "application/json"));

            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.TryGetProperty("users", out var users) && users.GetArrayLength() > 0)
                {
                    var user = users[0];
                    // Check if account is disabled
                    if (user.TryGetProperty("disabled", out var disabled) && disabled.GetBoolean())
                        return (false, "Your account has been disabled by an administrator.");

                    return (true, "Session valid.");
                }

                return (false, "Account no longer exists.");
            }

            // Token might be expired — try refreshing it
            var statusCode = (int)response.StatusCode;
            if (statusCode == 400 || statusCode == 401)
            {
                var refreshed = await RefreshIdTokenAsync();
                if (refreshed)
                    return (true, "Session refreshed.");
                else
                    return (false, "Session expired and could not be renewed.");
            }

            return (false, $"Server returned {response.StatusCode}.");
        }
        catch (HttpRequestException)
        {
            // No internet — don't force logout, let them continue offline
            return (true, "Validation skipped (no internet).");
        }
        catch (TaskCanceledException)
        {
            return (true, "Validation skipped (timeout).");
        }
        catch
        {
            return (true, "Validation skipped (error).");
        }
    }

    /// <summary>
    /// Refreshes the Firebase ID token using the stored refresh token.
    /// Updates the registry with the new idToken if successful.
    /// </summary>
    private static async Task<bool> RefreshIdTokenAsync()
    {
        try
        {
            var refreshToken = GetRefreshToken();
            if (string.IsNullOrEmpty(refreshToken)) return false;

            var payload = $"grant_type=refresh_token&refresh_token={Uri.EscapeDataString(refreshToken)}";
            var content = new StringContent(payload, Encoding.UTF8, "application/x-www-form-urlencoded");

            var response = await Http.PostAsync(
                $"{SecureStrings.RefreshTokenUrl}{SecureStrings.FirebaseApiKey}", content);

            if (!response.IsSuccessStatusCode) return false;

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var newIdToken = root.TryGetProperty("id_token", out var t) ? t.GetString() : null;
            var newRefreshToken = root.TryGetProperty("refresh_token", out var r) ? r.GetString() : null;

            if (string.IsNullOrEmpty(newIdToken)) return false;

            // Update registry with new tokens
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(AUTH_KEY, true);
                if (key != null)
                {
                    key.SetValue("IdToken", newIdToken);
                    if (!string.IsNullOrEmpty(newRefreshToken))
                        key.SetValue("RefreshToken", newRefreshToken);
                }
            }
            catch { }

            return true;
        }
        catch { return false; }
    }

    /// <summary>
    /// Persists the Firebase session locally.
    /// </summary>
    private static void SaveSession(string email, string uid, string idToken, string refreshToken, string displayName)
    {
        try
        {
            var timestamp = DateTime.UtcNow.ToString("o");
            var sessionHash = GenerateSessionToken(uid, timestamp);

            using var key = Registry.CurrentUser.CreateSubKey(AUTH_KEY);
            if (key == null) return;

            key.SetValue("Email", email);
            key.SetValue("UserId", uid);
            key.SetValue("IdToken", idToken);
            key.SetValue("RefreshToken", refreshToken);
            key.SetValue("DisplayName", displayName);
            key.SetValue("SessionToken", sessionHash);
            key.SetValue("LoginTime", timestamp);
        }
        catch { }
    }

    /// <summary>
    /// Logs out the current user by clearing the session from the registry.
    /// </summary>
    public static void Logout()
    {
        try
        {
            Registry.CurrentUser.DeleteSubKey(AUTH_KEY, false);
        }
        catch { }
    }

    #endregion

    #region Firebase Auth — Sign In

    /// <summary>
    /// Signs in with email and password via Firebase REST API.
    /// </summary>
    public static async Task<(bool success, string message)> LoginAsync(string email, string password)
    {
        if (!SecureStrings.IsConfigured)
            return (false, SecureStrings.ConfigurationMessage);

        if (string.IsNullOrWhiteSpace(email))
            return (false, "Please enter your email.");
        if (string.IsNullOrWhiteSpace(password))
            return (false, "Please enter your password.");

        try
        {
            var payload = JsonSerializer.Serialize(new
            {
                email = email.Trim(),
                password,
                returnSecureToken = true
            });

            var response = await Http.PostAsync(
                SecureStrings.SignInUrl + SecureStrings.FirebaseApiKey,
                new StringContent(payload, Encoding.UTF8, "application/json"));

            var json = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var uid = root.GetProperty("localId").GetString() ?? "";
                var idToken = root.GetProperty("idToken").GetString() ?? "";
                var refreshToken = root.GetProperty("refreshToken").GetString() ?? "";
                var userEmail = root.GetProperty("email").GetString() ?? email.Trim();
                var displayName = root.TryGetProperty("displayName", out var dn) ? dn.GetString() ?? "" : "";

                SaveSession(userEmail, uid, idToken, refreshToken, displayName);
                return (true, "Login successful!");
            }
            else
            {
                return (false, ParseFirebaseError(json));
            }
        }
        catch (HttpRequestException)
        {
            return (false, "No internet connection. Please check your network.");
        }
        catch (TaskCanceledException)
        {
            return (false, "Connection timed out. Please try again.");
        }
        catch (Exception ex)
        {
            return (false, $"Error: {ex.Message}");
        }
    }

    #endregion

    #region Firebase Auth — Password Reset

    /// <summary>
    /// Sends a password reset email via Firebase.
    /// </summary>
    public static async Task<(bool success, string message)> SendPasswordResetAsync(string email)
    {
        if (!SecureStrings.IsConfigured)
            return (false, SecureStrings.ConfigurationMessage);

        if (string.IsNullOrWhiteSpace(email))
            return (false, "Please enter your email.");

        try
        {
            var payload = JsonSerializer.Serialize(new
            {
                requestType = "PASSWORD_RESET",
                email = email.Trim()
            });

            var response = await Http.PostAsync(
                SecureStrings.ResetPasswordUrl + SecureStrings.FirebaseApiKey,
                new StringContent(payload, Encoding.UTF8, "application/json"));

            if (response.IsSuccessStatusCode)
                return (true, "Password reset email sent! Check your inbox.");
            else
            {
                var json = await response.Content.ReadAsStringAsync();
                return (false, ParseFirebaseError(json));
            }
        }
        catch (HttpRequestException)
        {
            return (false, "No internet connection.");
        }
        catch
        {
            return (false, "Could not send reset email. Try again.");
        }
    }

    #endregion

    #region Firebase Auth — Google Sign In (OAuth 2.0 Desktop Flow)

    /// <summary>
    /// Opens the default browser for Google OAuth, listens on localhost for the callback,
    /// exchanges the authorization code for tokens, then signs in to Firebase with the Google credential.
    /// Uses Desktop Client ID for OAuth flow, then access_token + requestUri for Firebase signInWithIdp.
    /// </summary>
    public static async Task<(bool success, string message)> GoogleSignInAsync()
    {
        if (!SecureStrings.IsConfigured)
            return (false, SecureStrings.ConfigurationMessage);

        try
        {
            // 1. Generate PKCE code verifier + challenge for extra security
            var codeVerifier = GenerateCodeVerifier();
            var codeChallenge = GenerateCodeChallenge(codeVerifier);
            var state = Guid.NewGuid().ToString("N");

            // 2. Build the Google OAuth authorization URL (use Desktop Client ID for the browser flow)
            var authUrl = $"{SecureStrings.GoogleAuthEndpoint}" +
                $"?client_id={Uri.EscapeDataString(SecureStrings.GoogleDesktopClientId)}" +
                $"&redirect_uri={Uri.EscapeDataString(SecureStrings.RedirectUri)}" +
                $"&response_type=code" +
                $"&scope={Uri.EscapeDataString("openid email profile")}" +
                $"&state={state}" +
                $"&code_challenge={codeChallenge}" +
                $"&code_challenge_method=S256" +
                $"&access_type=offline" +
                $"&prompt=select_account";

            // 3. Start a local HTTP listener for the callback
            using var listener = new HttpListener();
            listener.Prefixes.Add("http://localhost:43821/callback/");
            listener.Start();

            // 4. Open browser
            Process.Start(new ProcessStartInfo(authUrl) { UseShellExecute = true });

            // 5. Wait for the callback (with 120s timeout)
            var contextTask = listener.GetContextAsync();
            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(120));
            var completed = await Task.WhenAny(contextTask, timeoutTask);

            if (completed == timeoutTask)
            {
                listener.Stop();
                return (false, "Google sign-in timed out. Please try again.");
            }

            var context = await contextTask;
            var queryParams = context.Request.QueryString;
            var code = queryParams["code"];
            var returnedState = queryParams["state"];

            // Send a nice response to the browser
            var responseHtml = """
                <html><body style='font-family:Inter,system-ui,sans-serif;background:#0a0a12;color:#e4e4e7;display:flex;align-items:center;justify-content:center;height:100vh;margin:0'>
                <div style='text-align:center'><h2 style='color:#EF4444'>✓ Signed In</h2><p>You can close this tab and return to LegitX V2.</p></div>
                </body></html>
                """;
            var buffer = Encoding.UTF8.GetBytes(responseHtml);
            context.Response.ContentType = "text/html";
            context.Response.ContentLength64 = buffer.Length;
            await context.Response.OutputStream.WriteAsync(buffer);
            context.Response.Close();
            listener.Stop();

            // Validate state
            if (returnedState != state)
                return (false, "Security validation failed. Please try again.");

            if (string.IsNullOrEmpty(code))
            {
                var error = queryParams["error"];
                return (false, error == "access_denied"
                    ? "Google sign-in was cancelled."
                    : $"Google sign-in failed: {error ?? "no code returned"}.");
            }

            // 6. Exchange authorization code for tokens
            //    Using Firebase's own Web Client ID — the id_token audience will match Firebase.
            var tokenPayload = $"code={Uri.EscapeDataString(code)}" +
                $"&client_id={Uri.EscapeDataString(SecureStrings.GoogleDesktopClientId)}" +
                $"&client_secret={Uri.EscapeDataString(SecureStrings.GoogleClientSecret)}" +
                $"&redirect_uri={Uri.EscapeDataString(SecureStrings.RedirectUri)}" +
                $"&grant_type=authorization_code" +
                $"&code_verifier={Uri.EscapeDataString(codeVerifier)}";

            var tokenResponse = await Http.PostAsync(SecureStrings.GoogleTokenEndpoint,
                new StringContent(tokenPayload, Encoding.UTF8, "application/x-www-form-urlencoded"));

            var tokenJson = await tokenResponse.Content.ReadAsStringAsync();

            if (!tokenResponse.IsSuccessStatusCode)
                return (false, $"Failed to exchange Google token. Please try again.");

            using var tokenDoc = JsonDocument.Parse(tokenJson);

            // Prefer id_token (audience matches Firebase), fall back to access_token
            var hasIdToken = tokenDoc.RootElement.TryGetProperty("id_token", out var idTokProp);
            var googleIdToken = hasIdToken ? (idTokProp.GetString() ?? "") : "";
            var googleAccessToken = tokenDoc.RootElement.GetProperty("access_token").GetString() ?? "";

            // 7. Sign in to Firebase using the Google credential
            //    If we have an id_token (audience = Firebase Web Client ID), use it directly.
            //    Otherwise fall back to access_token.
            string postBody;
            if (!string.IsNullOrEmpty(googleIdToken))
                postBody = $"id_token={googleIdToken}&providerId=google.com";
            else
                postBody = $"access_token={googleAccessToken}&providerId=google.com";

            var firebasePayload = JsonSerializer.Serialize(new
            {
                postBody,
                requestUri = SecureStrings.FirebaseAppUrl,
                returnIdpCredential = true,
                returnSecureToken = true
            });

            var fbResponse = await Http.PostAsync(
                SecureStrings.SignInWithIdpUrl + SecureStrings.FirebaseApiKey,
                new StringContent(firebasePayload, Encoding.UTF8, "application/json"));

            var fbJson = await fbResponse.Content.ReadAsStringAsync();

            if (fbResponse.IsSuccessStatusCode)
            {
                using var fbDoc = JsonDocument.Parse(fbJson);
                var root = fbDoc.RootElement;

                var uid = root.GetProperty("localId").GetString() ?? "";
                var idToken = root.GetProperty("idToken").GetString() ?? "";
                var refreshToken = root.GetProperty("refreshToken").GetString() ?? "";
                var email = root.TryGetProperty("email", out var em) ? em.GetString() ?? "" : "";
                var displayName = root.TryGetProperty("displayName", out var dn) ? dn.GetString() ?? "" : "";

                SaveSession(email, uid, idToken, refreshToken, displayName);
                return (true, "Google sign-in successful!");
            }
            else
            {
                return (false, ParseFirebaseError(fbJson));
            }
        }
        catch (HttpListenerException)
        {
            return (false, "Could not start local server for Google sign-in.\nTry running as Administrator.");
        }
        catch (HttpRequestException)
        {
            return (false, "No internet connection. Please check your network.");
        }
        catch (TaskCanceledException)
        {
            return (false, "Connection timed out. Please try again.");
        }
        catch (Exception ex)
        {
            return (false, $"Google sign-in error: {ex.Message}");
        }
    }

    /// <summary>Generates a random code verifier for PKCE.</summary>
    private static string GenerateCodeVerifier()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>Generates a S256 code challenge from a code verifier.</summary>
    private static string GenerateCodeChallenge(string codeVerifier)
    {
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier));
        return Convert.ToBase64String(hash)
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Parses Firebase error responses into user-friendly messages.
    /// </summary>
    private static string ParseFirebaseError(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var error = doc.RootElement.GetProperty("error");
            var message = error.GetProperty("message").GetString() ?? "Unknown error";

            return message switch
            {
                "EMAIL_NOT_FOUND" => "No account found with this email.",
                "INVALID_PASSWORD" => "Incorrect password.",
                "USER_DISABLED" => "This account has been disabled.",
                "EMAIL_EXISTS" => "An account with this email already exists.",
                "WEAK_PASSWORD : Password should be at least 6 characters" => "Password must be at least 6 characters.",
                "TOO_MANY_ATTEMPTS_TRY_LATER" => "Too many failed attempts. Please try again later.",
                "INVALID_EMAIL" => "Please enter a valid email address.",
                "INVALID_LOGIN_CREDENTIALS" => "Invalid email or password.",
                "MISSING_PASSWORD" => "Please enter your password.",
                _ => message.Replace("_", " ").ToLower() is var m
                    ? char.ToUpper(m[0]) + m[1..] + "."
                    : message
            };
        }
        catch
        {
            return "Authentication failed. Please try again.";
        }
    }

    private static string GenerateSessionToken(string uid, string timestamp)
    {
        var data = $"LegitX_V2_{uid}_{timestamp}_FirebaseSalt2024";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(data));
        return Convert.ToBase64String(hash);
    }

    #endregion
}
