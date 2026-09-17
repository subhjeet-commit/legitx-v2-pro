namespace LegitX.WPF.Services;

/// <summary>
/// Runtime configuration for Firebase Auth, Google OAuth, and Firestore.
/// Open-source builds deliberately do not ship a project key, OAuth client,
/// or project URL. Configure the documented environment variables locally.
/// </summary>
internal static class SecureStrings
{
    private const string FirebaseApiKeyPlaceholder = "PASTE YOUR FIREBASE WEB API KEY HERE";
    private const string GoogleClientIdPlaceholder = "PASTE YOUR GOOGLE OAUTH CLIENT ID HERE";
    private const string GoogleClientSecretPlaceholder = "PASTE YOUR GOOGLE OAUTH CLIENT SECRET HERE";
    private const string FirestoreBaseUrlPlaceholder = "PASTE YOUR FIRESTORE REST BASE URL HERE";
    private const string FirebaseAppUrlPlaceholder = "PASTE YOUR FIREBASE APP URL HERE";

    private static string? _firebaseApiKey;
    private static string? _googleClientId;
    private static string? _googleClientSecret;
    private static string? _firestoreBaseUrl;
    private static string? _firebaseAppUrl;

    internal static string FirebaseApiKey => _firebaseApiKey ??= Read("LEGITX_FIREBASE_API_KEY", FirebaseApiKeyPlaceholder);
    internal static string GoogleDesktopClientId => _googleClientId ??= Read("LEGITX_GOOGLE_CLIENT_ID", GoogleClientIdPlaceholder);
    internal static string GoogleClientSecret => _googleClientSecret ??= Read("LEGITX_GOOGLE_CLIENT_SECRET", GoogleClientSecretPlaceholder);
    internal static string FirestoreBaseUrl => _firestoreBaseUrl ??= Read("LEGITX_FIRESTORE_BASE_URL", FirestoreBaseUrlPlaceholder);
    internal static string FirebaseAppUrl => _firebaseAppUrl ??= Read("LEGITX_FIREBASE_APP_URL", FirebaseAppUrlPlaceholder);

    internal static string SignInUrl => "https://identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key=";
    internal static string SignInWithIdpUrl => "https://identitytoolkit.googleapis.com/v1/accounts:signInWithIdp?key=";
    internal static string GetUserUrl => "https://identitytoolkit.googleapis.com/v1/accounts:lookup?key=";
    internal static string ResetPasswordUrl => "https://identitytoolkit.googleapis.com/v1/accounts:sendOobCode?key=";
    internal static string RefreshTokenUrl => "https://securetoken.googleapis.com/v1/token?key=";
    internal static string GoogleAuthEndpoint => "https://accounts.google.com/o/oauth2/v2/auth";
    internal static string GoogleTokenEndpoint => "https://oauth2.googleapis.com/token";
    internal static string GoogleUserInfoEndpoint => "https://www.googleapis.com/oauth2/v3/userinfo";
    internal static string RedirectUri => "http://localhost:43821/callback/";

    internal static bool IsConfigured =>
        IsSet(FirebaseApiKey, FirebaseApiKeyPlaceholder)
        && IsSet(GoogleDesktopClientId, GoogleClientIdPlaceholder)
        && IsSet(GoogleClientSecret, GoogleClientSecretPlaceholder)
        && IsValidHttpsUrl(FirestoreBaseUrl, FirestoreBaseUrlPlaceholder)
        && IsValidHttpsUrl(FirebaseAppUrl, FirebaseAppUrlPlaceholder);

    internal static string ConfigurationMessage =>
        "Authentication is not configured for this open-source build.\n\n" +
        "Set these environment variables before launching LegitX V2:\n" +
        "LEGITX_FIREBASE_API_KEY\n" +
        "LEGITX_GOOGLE_CLIENT_ID\n" +
        "LEGITX_GOOGLE_CLIENT_SECRET\n" +
        "LEGITX_FIRESTORE_BASE_URL\n" +
        "LEGITX_FIREBASE_APP_URL";

    private static string Read(string name, string placeholder) =>
        Environment.GetEnvironmentVariable(name)?.Trim() is { Length: > 0 } value
            ? value
            : placeholder;

    private static bool IsSet(string value, string placeholder) =>
        !string.IsNullOrWhiteSpace(value) && !string.Equals(value, placeholder, StringComparison.Ordinal);

    private static bool IsValidHttpsUrl(string value, string placeholder) =>
        IsSet(value, placeholder)
        && Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps;

    internal static unsafe void WipeString(ref string? value)
    {
        if (value == null) return;
        fixed (char* chars = value)
        {
            for (var i = 0; i < value.Length; i++)
                chars[i] = '\0';
        }
        value = null;
    }

    internal static void PurgeAll()
    {
        WipeString(ref _firebaseApiKey);
        WipeString(ref _googleClientId);
        WipeString(ref _googleClientSecret);
        WipeString(ref _firestoreBaseUrl);
        WipeString(ref _firebaseAppUrl);
    }
}
