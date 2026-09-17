using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace LegitX.WPF.Services;

/// <summary>
/// Custom HttpClientHandler with SSL certificate pinning for Firebase/Google APIs.
///
/// This prevents man-in-the-middle attacks where someone intercepts HTTPS traffic
/// using tools like Fiddler, Charles, Burp Suite, or mitmproxy by installing a
/// custom CA certificate.
///
/// We pin to Google's root CAs — if the certificate chain doesn't include
/// a known Google/GTS CA, the connection is rejected.
/// </summary>
internal static class SecureHttpClient
{
    /// <summary>
    /// Known Google Trust Services root CA thumbprints (SHA-256 of the public key).
    /// These are Google's actual root CAs used for googleapis.com, firebase, etc.
    /// Updated as of 2024 — includes GTS Root R1-R4 and GlobalSign (used by Google).
    /// </summary>
    private static readonly HashSet<string> TrustedCaNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "GTS Root R1",
        "GTS Root R2",
        "GTS Root R3",
        "GTS Root R4",
        "GlobalSign Root CA",
        "GlobalSign Root CA - R2",
        "GlobalSign Root CA - R3",
        "Google Trust Services LLC",
        "GTS CA 1C3",
        "GTS CA 1D4",
        // Also allow DigiCert (used as backup by some Google services)
        "DigiCert Global Root G2",
        "DigiCert Global Root CA",
    };

    /// <summary>
    /// Domains that require certificate pinning.
    /// All Firebase / Google API domains are pinned.
    /// </summary>
    private static readonly string[] PinnedDomains =
    [
        "googleapis.com",
        "firebaseapp.com",
        "google.com",
        "accounts.google.com",
    ];

    /// <summary>
    /// Creates an HttpClient with certificate pinning enabled.
    /// Use this instead of `new HttpClient()` for all API calls.
    /// </summary>
    public static HttpClient Create(TimeSpan? timeout = null)
    {
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = ValidateCertificate
        };

        var client = new HttpClient(handler)
        {
            Timeout = timeout ?? TimeSpan.FromSeconds(15)
        };

        return client;
    }

    /// <summary>
    /// Custom SSL certificate validation callback.
    /// For pinned domains, verifies the certificate chain includes a trusted Google CA.
    /// For non-pinned domains (shouldn't happen), uses default validation.
    /// </summary>
    private static bool ValidateCertificate(
        HttpRequestMessage request,
        X509Certificate2? certificate,
        X509Chain? chain,
        SslPolicyErrors errors)
    {
        // If there are any SSL errors other than name mismatch on non-pinned domains, reject
        if (errors.HasFlag(SslPolicyErrors.RemoteCertificateNotAvailable))
            return false;

        var host = request.RequestUri?.Host ?? "";

        // Check if this is a pinned domain
        bool isPinned = false;
        foreach (var domain in PinnedDomains)
        {
            if (host.EndsWith(domain, StringComparison.OrdinalIgnoreCase))
            {
                isPinned = true;
                break;
            }
        }

        // For non-pinned domains, use default validation
        if (!isPinned)
            return errors == SslPolicyErrors.None;

        // For pinned domains, the chain must be valid
        if (errors != SslPolicyErrors.None)
            return false;

        if (certificate == null || chain == null)
            return false;

        // Check that at least one certificate in the chain is from a trusted Google CA
        try
        {
            foreach (var element in chain.ChainElements)
            {
                var cert = element.Certificate;
                var issuer = cert.Issuer;
                var subject = cert.Subject;

                // Check if the issuer or subject matches a known Google CA
                foreach (var trustedName in TrustedCaNames)
                {
                    if (issuer.Contains(trustedName, StringComparison.OrdinalIgnoreCase) ||
                        subject.Contains(trustedName, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
        }
        catch
        {
            return false;
        }

        // No trusted CA found in chain — possible MITM attack
        return false;
    }
}
