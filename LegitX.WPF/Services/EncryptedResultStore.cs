using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;

namespace LegitX.WPF.Services;

/// <summary>
/// Layer 6 — Encrypted Critical Code Flow.
///
/// Stores the license-check and HWID-check boolean results in AES-256-encrypted
/// memory with an HMAC-SHA256 integrity tag.  A cracker cannot simply scan memory
/// for "Licensed = true" and flip the bit — the value is encrypted with a
/// session-derived key, and every read re-validates the HMAC.
///
/// Usage:
///   After a successful check  → EncryptedResultStore.StoreLicense(result)
///   Before granting access    → EncryptedResultStore.ReadLicense() and validate
///   Quick gate                → EncryptedResultStore.IsLicenseValid()
/// </summary>
public static class EncryptedResultStore
{
    // ── Session key material (generated once per app lifetime) ──
    private static readonly byte[] _sessionKey;
    private static readonly byte[] _hmacKey;

    // ── Encrypted blobs ──
    private static byte[]? _licenseCipher;
    private static byte[]? _licenseIv;
    private static byte[]? _licenseHmac;

    private static byte[]? _hwidCipher;
    private static byte[]? _hwidIv;
    private static byte[]? _hwidHmac;

    private static readonly object _lock = new();

    // ═══════════════════════════════════════════════════════════════
    //  KEY DERIVATION  (once, at static init)
    // ═══════════════════════════════════════════════════════════════

    static EncryptedResultStore()
    {
        // Derive keys from multiple runtime-unique sources so they differ every launch
        var entropy = new byte[64];
        RandomNumberGenerator.Fill(entropy);

        // Mix in process-specific data
        var pid = Environment.ProcessId;
        var tick = Environment.TickCount64;
        var extra = Encoding.UTF8.GetBytes($"{pid}|{tick}|LegitX_L6_Salt");

        // HKDF-like: SHA-512 over entropy + extra, then split into two 32-byte keys
        using var sha = SHA512.Create();
        sha.TransformBlock(entropy, 0, entropy.Length, null, 0);
        sha.TransformFinalBlock(extra, 0, extra.Length);
        var derived = sha.Hash!; // 64 bytes

        _sessionKey = derived[..32];  // AES-256 key
        _hmacKey = derived[32..];     // HMAC-SHA256 key
    }

    // ═══════════════════════════════════════════════════════════════
    //  STORE
    // ═══════════════════════════════════════════════════════════════

    /// <summary>Encrypts and stores the license check result.</summary>
    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    public static void StoreLicense(bool licensed, bool needsCode, string reason)
    {
        var payload = $"{(licensed ? "1" : "0")}|{(needsCode ? "1" : "0")}|{reason}";
        var plaintext = Encoding.UTF8.GetBytes(payload);

        lock (_lock)
        {
            Encrypt(plaintext, out _licenseCipher, out _licenseIv, out _licenseHmac);
        }
    }

    /// <summary>Encrypts and stores the HWID check result.</summary>
    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    public static void StoreHwid(bool allowed, string reason)
    {
        var payload = $"{(allowed ? "1" : "0")}|{reason}";
        var plaintext = Encoding.UTF8.GetBytes(payload);

        lock (_lock)
        {
            Encrypt(plaintext, out _hwidCipher, out _hwidIv, out _hwidHmac);
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  READ  (decrypts + validates HMAC)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Decrypts and returns the stored license result.
    /// Returns (licensed, needsCode, reason).
    /// Returns (false, false, "tampered") if the HMAC check fails or data is missing.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    public static (bool Licensed, bool NeedsCode, string Reason) ReadLicense()
    {
        lock (_lock)
        {
            if (_licenseCipher == null || _licenseIv == null || _licenseHmac == null)
                return (false, false, "No license result stored.");

            var plaintext = Decrypt(_licenseCipher, _licenseIv, _licenseHmac);
            if (plaintext == null)
                return (false, false, "License result integrity check failed.");

            var payload = Encoding.UTF8.GetString(plaintext);
            var parts = payload.Split('|', 3);
            if (parts.Length < 3)
                return (false, false, "License result corrupted.");

            return (parts[0] == "1", parts[1] == "1", parts[2]);
        }
    }

    /// <summary>
    /// Decrypts and returns the stored HWID result.
    /// Returns (allowed, reason).
    /// Returns (false, "tampered") if the HMAC check fails or data is missing.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    public static (bool Allowed, string Reason) ReadHwid()
    {
        lock (_lock)
        {
            if (_hwidCipher == null || _hwidIv == null || _hwidHmac == null)
                return (false, "No HWID result stored.");

            var plaintext = Decrypt(_hwidCipher, _hwidIv, _hwidHmac);
            if (plaintext == null)
                return (false, "HWID result integrity check failed.");

            var payload = Encoding.UTF8.GetString(plaintext);
            var parts = payload.Split('|', 2);
            if (parts.Length < 2)
                return (false, "HWID result corrupted.");

            return (parts[0] == "1", parts[1]);
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  QUICK GATES  (for use in critical code paths)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Returns true only if the encrypted license blob decrypts to Licensed=true
    /// AND the HMAC is valid. Safe to call from hot paths.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    public static bool IsLicenseValid()
    {
        var (licensed, _, _) = ReadLicense();
        return licensed;
    }

    /// <summary>
    /// Returns true only if the encrypted HWID blob decrypts to Allowed=true
    /// AND the HMAC is valid.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    public static bool IsHwidValid()
    {
        var (allowed, _) = ReadHwid();
        return allowed;
    }

    /// <summary>Clears all stored blobs (call on logout / exit).</summary>
    public static void Clear()
    {
        lock (_lock)
        {
            if (_licenseCipher != null) CryptographicOperations.ZeroMemory(_licenseCipher);
            if (_licenseIv != null) CryptographicOperations.ZeroMemory(_licenseIv);
            if (_licenseHmac != null) CryptographicOperations.ZeroMemory(_licenseHmac);
            if (_hwidCipher != null) CryptographicOperations.ZeroMemory(_hwidCipher);
            if (_hwidIv != null) CryptographicOperations.ZeroMemory(_hwidIv);
            if (_hwidHmac != null) CryptographicOperations.ZeroMemory(_hwidHmac);

            _licenseCipher = _licenseIv = _licenseHmac = null;
            _hwidCipher = _hwidIv = _hwidHmac = null;
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  AES-256-CBC + HMAC-SHA256  (encrypt-then-MAC)
    // ═══════════════════════════════════════════════════════════════

    private static void Encrypt(byte[] plaintext, out byte[] cipher, out byte[] iv, out byte[] hmac)
    {
        using var aes = Aes.Create();
        aes.KeySize = 256;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = _sessionKey;
        aes.GenerateIV();

        iv = (byte[])aes.IV.Clone();

        using var enc = aes.CreateEncryptor();
        cipher = enc.TransformFinalBlock(plaintext, 0, plaintext.Length);

        // HMAC over IV || ciphertext
        using var mac = new HMACSHA256(_hmacKey);
        mac.TransformBlock(iv, 0, iv.Length, null, 0);
        mac.TransformFinalBlock(cipher, 0, cipher.Length);
        hmac = mac.Hash!;
    }

    /// <summary>Returns null if HMAC validation fails (tampered).</summary>
    private static byte[]? Decrypt(byte[] cipher, byte[] iv, byte[] storedHmac)
    {
        // Verify HMAC first (encrypt-then-MAC pattern)
        using var mac = new HMACSHA256(_hmacKey);
        mac.TransformBlock(iv, 0, iv.Length, null, 0);
        mac.TransformFinalBlock(cipher, 0, cipher.Length);
        var computed = mac.Hash!;

        if (!CryptographicOperations.FixedTimeEquals(computed, storedHmac))
            return null; // Tampered!

        using var aes = Aes.Create();
        aes.KeySize = 256;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = _sessionKey;
        aes.IV = iv;

        using var dec = aes.CreateDecryptor();
        return dec.TransformFinalBlock(cipher, 0, cipher.Length);
    }
}
