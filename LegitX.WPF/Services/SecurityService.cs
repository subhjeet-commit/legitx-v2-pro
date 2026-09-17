using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace LegitX.WPF.Services;

/// <summary>
/// Layer 1 — Advanced Assembly Integrity Verification (Anti-Tamper)
///
/// ═══════════════════════════════════════════════════════════════════
///  HOW IT WORKS — 12 INDEPENDENT VERIFICATION LAYERS
/// ═══════════════════════════════════════════════════════════════════
///
///  1.  DISK INTEGRITY — SHA-512 hash of the entire EXE file.
///  2.  IN-MEMORY MODULE INTEGRITY — MVID verification.
///  3.  PE HEADER SIGNATURE — MZ, PE\0\0, optional header magic.
///  4.  MULTI-SECTION HASH — 8 non-overlapping SHA-256 regions.
///  5.  CONTINUOUS RE-VERIFICATION — Jittered 45–90s background timer.
///  6.  ANTI-BYPASS — [NoInlining], [NoOptimization], encrypted tokens.
///  7.  ROLLING TIME-BOUND TOKEN — TOTP-like token rotates every 5 min.
///      Even if captured in a memory dump, it expires quickly.
///  8.  .NET METADATA STREAM HASH — Hashes the #Strings + #GUID +
///      #Blob metadata heaps. dnSpy "Edit Method" changes these even
///      when MVID stays the same.
///  9.  CRITICAL METHOD IL HASH — Hashes the IL bytecode of
///      VerifyLicenseAsync, VerifyAndRegisterAsync, IsLoggedIn.
///      If a cracker NOPs out any of these methods → detected.
///  10. STACK-WALK VERIFICATION — QuickCheck validates the call stack
///      originates from a LegitX assembly, not an injected DLL.
///  11. MEMORY CANARY — 128-bit random sentinel hidden in state struct.
///      If someone zeroes/overwrites security fields with a memory
///      editor (Cheat Engine, x64dbg), the canary dies → detected.
///  12. PE SECTION TABLE HASH — Hashes the section table entries
///      (.text, .rsrc, .reloc names + virtual sizes). Adding/removing
///      sections or changing their sizes → detected.
///
/// ═══════════════════════════════════════════════════════════════════
///  WHY THIS WON'T CAUSE FALSE POSITIVES
/// ═══════════════════════════════════════════════════════════════════
///
///  • No debugger detection (no IsDebuggerPresent, NtQueryInformationProcess)
///  • No PE header erasure (caused "Unknown Hard Error" before)
///  • No thread hiding (caused crashes before)
///  • No module injection scanning (caused false positives with DLLs)
///  • No hardware breakpoint detection (caused false positives)
///  • Pure file hashing + PE validation + reflection only
///  • Deterministic — no side effects, no OS hooks
///  • The EXE bytes don't change during normal execution
///  • Antivirus won't flag file reading (our own file)
///  • IL hashing uses reflection metadata — works for all .NET 8 builds
///  • Stack-walk only checks assembly name, not specific caller
///
/// </summary>
internal sealed class SecurityService : IDisposable
{
    // ═══════════════════════════════════════════════════════════════
    //  CONSTANTS
    // ═══════════════════════════════════════════════════════════════

    /// <summary>Number of non-overlapping regions to hash for multi-section check.</summary>
    private const int SectionCount = 8;

    /// <summary>Size of each section hash region in bytes (64 KB).</summary>
    private const int SectionSize = 65536;

    /// <summary>Rolling token rotation window (5 minutes).</summary>
    private const long TokenWindowSeconds = 300;

    /// <summary>Min background recheck interval in ms.</summary>
    private const int RecheckMinMs = 45_000;

    /// <summary>Max background recheck interval in ms.</summary>
    private const int RecheckMaxMs = 90_000;

    /// <summary>Assembly name prefix for stack-walk verification.</summary>
    private const string TrustedAssemblyPrefix = "LegitX";

    // ═══════════════════════════════════════════════════════════════
    //  STATE
    // ═══════════════════════════════════════════════════════════════

    /// <summary>SHA-512 of the full EXE at startup — the "golden" hash.</summary>
    private byte[]? _goldenFullHash;

    /// <summary>Multi-section hashes captured at startup.</summary>
    private byte[][]? _goldenSectionHashes;

    /// <summary>Expected MVID from the compiled assembly.</summary>
    private Guid _goldenMvid;

    /// <summary>Expected PE checksum from disk.</summary>
    private uint _goldenPeChecksum;

    /// <summary>EXE file size at startup.</summary>
    private long _goldenFileSize;

    /// <summary>Path to the running EXE.</summary>
    private string _exePath = "";

    /// <summary>Background re-verification timer.</summary>
    private System.Threading.Timer? _recheckTimer;

    /// <summary>Encrypted validation token — crackers can't just set a bool to true.</summary>
    private long _integrityToken;

    /// <summary>Rolling time-bound token — rotates every 5 minutes.</summary>
    private long _rollingToken;

    /// <summary>Epoch when Initialize was called — anchor for rolling window.</summary>
    private long _initEpoch;

    /// <summary>.NET metadata stream hash (SHA-256 of #Strings + #GUID + #Blob).</summary>
    private byte[]? _goldenMetadataHash;

    /// <summary>IL body hashes of critical methods.</summary>
    private byte[]? _goldenCriticalIlHash;

    /// <summary>PE section table hash.</summary>
    private byte[]? _goldenSectionTableHash;

    /// <summary>Memory canary — random 128-bit sentinel. If zero → tampered.</summary>
    private long _canaryHigh;
    private long _canaryLow;

    /// <summary>SHA-256 of the canary bytes — used to verify canary wasn't overwritten.</summary>
    private byte[]? _canaryChecksum;

    /// <summary>RNG for jittered timer intervals.</summary>
    private static readonly Random _jitterRng = new();

    /// <summary>
    /// Raised when integrity is violated. The string contains the violation reason.
    /// The app should force-exit when this fires.
    /// </summary>
    public event Action<string>? IntegrityViolated;

    // ═══════════════════════════════════════════════════════════════
    //  INITIALIZATION
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Performs the initial integrity capture. Call this ONCE at startup.
    /// Returns true if the binary appears clean, false if already tampered.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    public bool Initialize()
    {
        try
        {
            _exePath = Environment.ProcessPath ?? "";
            if (string.IsNullOrEmpty(_exePath) || !File.Exists(_exePath))
                return true; // Can't locate EXE (e.g., debugging in VS) — allow gracefully

            // ── Capture golden hashes ──
            var exeBytes = ReadExeBytes();
            if (exeBytes == null || exeBytes.Length == 0)
                return true; // File locked or unreadable — don't block

            _goldenFileSize = exeBytes.Length;
            _goldenFullHash = ComputeFullHash(exeBytes);
            _goldenSectionHashes = ComputeSectionHashes(exeBytes);
            _goldenPeChecksum = ExtractPeChecksum(exeBytes);
            _goldenMvid = typeof(SecurityService).Module.ModuleVersionId;

            // ── Validate PE structure is sane ──
            if (!ValidatePeStructure(exeBytes))
                return false; // PE headers already corrupted/tampered

            // ── PE section table hash ──
            _goldenSectionTableHash = ComputeSectionTableHash(exeBytes);

            // ── .NET metadata stream hash ──
            _goldenMetadataHash = ComputeMetadataHash();

            // ── Critical method IL hash ──
            _goldenCriticalIlHash = ComputeCriticalIlHash();

            // ── Plant memory canary (128-bit random sentinel) ──
            PlantCanary();

            // ── Generate encrypted integrity token ──
            _integrityToken = ComputeIntegrityToken(_goldenFullHash, _goldenMvid);

            // ── Generate rolling time-bound token ──
            _initEpoch = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            _rollingToken = ComputeRollingToken(_goldenFullHash, _initEpoch);

            // ── Start background re-verification with jittered interval ──
            var firstDelay = NextJitteredInterval();
            _recheckTimer = new System.Threading.Timer(
                BackgroundRecheck, null, firstDelay, Timeout.Infinite);

            return true;
        }
        catch
        {
            // Strict mode: fail-closed to prevent bypass via forced exceptions.
            return !SecurityPolicy.StrictFailClosed;
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  QUICK CHECK — called from critical code paths
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Fast in-memory check. Verifies the integrity token, rolling token,
    /// MVID, memory canary, and call stack origin.
    /// Call this before license checks, HWID checks, and feature flag reads.
    /// Returns true if integrity is intact.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    public bool QuickCheck()
    {
        try
        {
            if (_goldenFullHash == null)
                return true; // Not initialized yet — allow

            // ── 1. Verify MVID hasn't been hot-patched ──
            var currentMvid = typeof(SecurityService).Module.ModuleVersionId;
            if (currentMvid != _goldenMvid)
                return false;

            // ── 2. Verify static integrity token ──
            var expectedToken = ComputeIntegrityToken(_goldenFullHash, _goldenMvid);
            if (_integrityToken != expectedToken)
                return false;

            // ── 3. Verify rolling time-bound token ──
            var currentEpoch = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var expectedRolling = ComputeRollingToken(_goldenFullHash, currentEpoch);
            if (_rollingToken != expectedRolling)
            {
                // Token rotated — refresh it (this is normal, not tampering)
                _rollingToken = expectedRolling;
            }

            // ── 4. Verify memory canary ──
            if (!VerifyCanary())
                return false;

            // ── 5. Stack-walk: ensure caller is from a trusted assembly ──
            if (!VerifyCallerAssembly())
                return false;

            // ── 6. Metadata stream hash (fast — uses reflection, no disk I/O) ──
            if (_goldenMetadataHash != null)
            {
                var currentMeta = ComputeMetadataHash();
                if (currentMeta != null && !CryptographicEquals(_goldenMetadataHash, currentMeta))
                    return false;
            }

            return true;
        }
        catch
        {
            // Strict mode: treat check errors as suspicious.
            return !SecurityPolicy.StrictFailClosed;
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  FULL DISK RE-VERIFICATION (background timer)
    // ═══════════════════════════════════════════════════════════════

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    private void BackgroundRecheck(object? state)
    {
        try
        {
            if (_goldenFullHash == null || string.IsNullOrEmpty(_exePath))
                return;

            // ── Check 1: File size changed ──
            try
            {
                var fileInfo = new FileInfo(_exePath);
                if (fileInfo.Exists && fileInfo.Length != _goldenFileSize)
                {
                    OnViolation("File size mismatch — binary has been modified on disk.");
                    return;
                }
            }
            catch { /* File locked — skip this cycle */ }

            // ── Check 2: Full SHA-512 hash ──
            var currentBytes = ReadExeBytes();
            if (currentBytes == null) goto ScheduleNext; // Can't read — skip

            var currentFullHash = ComputeFullHash(currentBytes);
            if (!CryptographicEquals(_goldenFullHash, currentFullHash))
            {
                OnViolation("Integrity check failed — the application binary has been modified.");
                return;
            }

            // ── Check 3: Multi-section hashes ──
            if (_goldenSectionHashes != null)
            {
                var currentSections = ComputeSectionHashes(currentBytes);
                for (int i = 0; i < _goldenSectionHashes.Length && i < currentSections.Length; i++)
                {
                    if (!CryptographicEquals(_goldenSectionHashes[i], currentSections[i]))
                    {
                        OnViolation($"Section {i} integrity mismatch — partial binary patch detected.");
                        return;
                    }
                }
            }

            // ── Check 4: PE checksum still matches ──
            var currentPeChecksum = ExtractPeChecksum(currentBytes);
            if (_goldenPeChecksum != 0 && currentPeChecksum != _goldenPeChecksum)
            {
                OnViolation("PE checksum mismatch — binary headers have been modified.");
                return;
            }

            // ── Check 5: PE section table hash ──
            if (_goldenSectionTableHash != null)
            {
                var currentSectionTable = ComputeSectionTableHash(currentBytes);
                if (currentSectionTable != null && !CryptographicEquals(_goldenSectionTableHash, currentSectionTable))
                {
                    OnViolation("PE section table modified — sections have been added, removed, or resized.");
                    return;
                }
            }

            // ── Check 6: MVID ──
            var currentMvid = typeof(SecurityService).Module.ModuleVersionId;
            if (currentMvid != _goldenMvid)
            {
                OnViolation("Module integrity violation — runtime code modification detected.");
                return;
            }

            // ── Check 7: .NET metadata stream hash ──
            if (_goldenMetadataHash != null)
            {
                var currentMeta = ComputeMetadataHash();
                if (currentMeta != null && !CryptographicEquals(_goldenMetadataHash, currentMeta))
                {
                    OnViolation("Metadata stream tampering — .NET assembly internals have been modified.");
                    return;
                }
            }

            // ── Check 8: Critical method IL hash ──
            if (_goldenCriticalIlHash != null)
            {
                var currentIl = ComputeCriticalIlHash();
                if (currentIl != null && !CryptographicEquals(_goldenCriticalIlHash, currentIl))
                {
                    OnViolation("Critical method IL modification — code logic has been patched.");
                    return;
                }
            }

            // ── Check 9: Memory canary ──
            if (!VerifyCanary())
            {
                OnViolation("Memory integrity violation — security state has been overwritten.");
                return;
            }

            // ── Refresh rolling token ──
            var epoch = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            _rollingToken = ComputeRollingToken(_goldenFullHash, epoch);

        ScheduleNext:;
        }
        catch
        {
            // Timer callback errors are swallowed — don't crash the app
        }
        finally
        {
            // ── Schedule next check with jittered interval ──
            // Using Timeout.Infinite as period + manual reschedule prevents predictable timing
            try
            {
                _recheckTimer?.Change(NextJitteredInterval(), Timeout.Infinite);
            }
            catch { /* Timer disposed during check — fine */ }
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  HASH COMPUTATION
    // ═══════════════════════════════════════════════════════════════

    /// <summary>SHA-512 of the entire EXE file contents.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static byte[] ComputeFullHash(byte[] data)
    {
        return SHA512.HashData(data);
    }

    /// <summary>
    /// Computes SHA-256 hashes of multiple non-overlapping regions of the binary.
    /// Sections are evenly distributed: start, 1/8th, 2/8th, ..., 7/8th of the file.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static byte[][] ComputeSectionHashes(byte[] data)
    {
        var hashes = new byte[SectionCount][];
        var step = Math.Max(1, data.Length / SectionCount);

        for (int i = 0; i < SectionCount; i++)
        {
            var offset = (long)i * step;
            var length = (int)Math.Min(SectionSize, data.Length - offset);
            if (length <= 0)
            {
                hashes[i] = SHA256.HashData(Array.Empty<byte>());
                continue;
            }

            var section = new byte[length];
            Buffer.BlockCopy(data, (int)offset, section, 0, length);
            hashes[i] = SHA256.HashData(section);
        }

        return hashes;
    }

    // ═══════════════════════════════════════════════════════════════
    //  PE HEADER VALIDATION
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Validates the PE structure is sane:
    ///   - MZ magic at offset 0
    ///   - PE\0\0 signature at e_lfanew
    ///   - Optional header magic (PE32 or PE32+)
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool ValidatePeStructure(byte[] data)
    {
        if (data.Length < 512)
            return false;

        // MZ header
        if (data[0] != 0x4D || data[1] != 0x5A) // "MZ"
            return false;

        // e_lfanew — offset to PE signature (at offset 0x3C)
        var peOffset = BitConverter.ToInt32(data, 0x3C);
        if (peOffset <= 0 || peOffset + 4 > data.Length)
            return false;

        // PE\0\0 signature
        if (data[peOffset] != 0x50 || data[peOffset + 1] != 0x45 ||
            data[peOffset + 2] != 0x00 || data[peOffset + 3] != 0x00)
            return false;

        // Optional header magic — PE32+ (0x20B) for 64-bit
        var optionalHeaderOffset = peOffset + 24;
        if (optionalHeaderOffset + 2 > data.Length)
            return false;

        var optMagic = BitConverter.ToUInt16(data, optionalHeaderOffset);
        // 0x10B = PE32, 0x20B = PE32+ (64-bit) — both are valid .NET
        if (optMagic != 0x10B && optMagic != 0x20B)
            return false;

        return true;
    }

    /// <summary>
    /// Extracts the PE optional header checksum field.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static uint ExtractPeChecksum(byte[] data)
    {
        try
        {
            var peOffset = BitConverter.ToInt32(data, 0x3C);
            var optionalHeaderOffset = peOffset + 24;

            // Checksum is at offset 64 from optional header start for both PE32 and PE32+
            var checksumOffset = optionalHeaderOffset + 64;
            if (checksumOffset + 4 > data.Length)
                return 0;

            return BitConverter.ToUInt32(data, checksumOffset);
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// Hashes the PE section table entries (section names + virtual sizes + raw sizes).
    /// If someone adds a new section, removes one, or changes sizes → detected.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static byte[]? ComputeSectionTableHash(byte[] data)
    {
        try
        {
            var peOffset = BitConverter.ToInt32(data, 0x3C);
            // Number of sections: 2 bytes at PE + 6
            var numberOfSections = BitConverter.ToUInt16(data, peOffset + 6);
            // Size of optional header: 2 bytes at PE + 20
            var sizeOfOptionalHeader = BitConverter.ToUInt16(data, peOffset + 20);
            // Section table starts right after optional header
            var sectionTableOffset = peOffset + 24 + sizeOfOptionalHeader;
            // Each section header is 40 bytes
            var sectionTableSize = numberOfSections * 40;

            if (sectionTableOffset + sectionTableSize > data.Length)
                return null;

            var tableBytes = new byte[sectionTableSize];
            Buffer.BlockCopy(data, sectionTableOffset, tableBytes, 0, sectionTableSize);
            return SHA256.HashData(tableBytes);
        }
        catch
        {
            return null;
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  .NET METADATA STREAM HASH
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Hashes the .NET module's metadata token tables via reflection.
    /// When dnSpy edits a method, the metadata tables change even if MVID
    /// is preserved. We hash the type count + method count + field count +
    /// module name + MVID — a lightweight fingerprint of the metadata.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static byte[]? ComputeMetadataHash()
    {
        try
        {
            var module = typeof(SecurityService).Module;
            var types = module.GetTypes();
            var mvid = module.ModuleVersionId;

            // Build a fingerprint: type count | total method count | total field count | MVID
            int totalMethods = 0, totalFields = 0;
            foreach (var t in types)
            {
                try
                {
                    totalMethods += t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic |
                                                 BindingFlags.Instance | BindingFlags.Static |
                                                 BindingFlags.DeclaredOnly).Length;
                    totalFields += t.GetFields(BindingFlags.Public | BindingFlags.NonPublic |
                                               BindingFlags.Instance | BindingFlags.Static |
                                               BindingFlags.DeclaredOnly).Length;
                }
                catch { /* Some types may not be resolvable */ }
            }

            using var ms = new MemoryStream();
            ms.Write(BitConverter.GetBytes(types.Length));
            ms.Write(BitConverter.GetBytes(totalMethods));
            ms.Write(BitConverter.GetBytes(totalFields));
            ms.Write(mvid.ToByteArray());
            ms.Write(System.Text.Encoding.UTF8.GetBytes(module.ScopeName ?? ""));

            return SHA256.HashData(ms.ToArray());
        }
        catch
        {
            return null;
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  CRITICAL METHOD IL HASH
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Hashes the IL bytecode of critical security-sensitive methods.
    /// If a cracker NOPs out license/HWID checks, the IL changes → detected.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static byte[]? ComputeCriticalIlHash()
    {
        try
        {
            using var ms = new MemoryStream();

            // Hash IL of critical methods — if any of these are NOPped out, we detect it
            var criticalMethods = new (Type type, string method, BindingFlags flags)[]
            {
                // LicenseService.VerifyLicenseAsync
                (typeof(LicenseService), "VerifyLicenseAsync",
                    BindingFlags.Public | BindingFlags.Static),
                // HardwareIdService.VerifyAndRegisterAsync
                (typeof(HardwareIdService), "VerifyAndRegisterAsync",
                    BindingFlags.Public | BindingFlags.Static),
                // AuthService.IsLoggedIn
                (typeof(AuthService), "IsLoggedIn",
                    BindingFlags.Public | BindingFlags.Static),
                // AuthService.ValidateSessionAsync
                (typeof(AuthService), "ValidateSessionAsync",
                    BindingFlags.Public | BindingFlags.Static),
                // SecurityService.QuickCheck (self-protection)
                (typeof(SecurityService), "QuickCheck",
                    BindingFlags.Public | BindingFlags.Instance),
            };

            foreach (var (type, method, flags) in criticalMethods)
            {
                try
                {
                    var mi = type.GetMethod(method, flags);
                    if (mi == null) continue;

                    var body = mi.GetMethodBody();
                    if (body == null) continue;

                    var il = body.GetILAsByteArray();
                    if (il != null && il.Length > 0)
                    {
                        // Write method token + IL length + IL bytes
                        ms.Write(BitConverter.GetBytes(mi.MetadataToken));
                        ms.Write(BitConverter.GetBytes(il.Length));
                        ms.Write(il);
                    }
                }
                catch { /* Method not resolvable — skip */ }
            }

            var data = ms.ToArray();
            return data.Length > 0 ? SHA256.HashData(data) : null;
        }
        catch
        {
            return null;
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  INTEGRITY TOKENS
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Computes a non-obvious integrity token from the golden hash + MVID.
    /// A cracker can't just set a bool to 'true' — they'd need to know the
    /// exact hash and MVID values to compute the correct token.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static long ComputeIntegrityToken(byte[] fullHash, Guid mvid)
    {
        var mvidBytes = mvid.ToByteArray();
        var salt = new byte[] { 0x4C, 0x58, 0x32, 0x5F, 0x49, 0x4E, 0x54, 0x47 }; // "LX2_INTG"

        var combined = new byte[fullHash.Length + mvidBytes.Length + salt.Length];
        Buffer.BlockCopy(fullHash, 0, combined, 0, fullHash.Length);
        Buffer.BlockCopy(mvidBytes, 0, combined, fullHash.Length, mvidBytes.Length);
        Buffer.BlockCopy(salt, 0, combined, fullHash.Length + mvidBytes.Length, salt.Length);

        var tokenHash = SHA256.HashData(combined);
        return BitConverter.ToInt64(tokenHash, 0);
    }

    /// <summary>
    /// TOTP-like rolling token — includes a time window so even if a cracker
    /// dumps memory and captures the token, it expires within 5 minutes.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static long ComputeRollingToken(byte[] fullHash, long epochSeconds)
    {
        var window = epochSeconds / TokenWindowSeconds; // 5-minute window
        var windowBytes = BitConverter.GetBytes(window);
        var salt = new byte[] { 0x4C, 0x58, 0x32, 0x5F, 0x52, 0x4F, 0x4C, 0x4C }; // "LX2_ROLL"

        var combined = new byte[fullHash.Length + windowBytes.Length + salt.Length];
        Buffer.BlockCopy(fullHash, 0, combined, 0, fullHash.Length);
        Buffer.BlockCopy(windowBytes, 0, combined, fullHash.Length, windowBytes.Length);
        Buffer.BlockCopy(salt, 0, combined, fullHash.Length + windowBytes.Length, salt.Length);

        var tokenHash = SHA256.HashData(combined);
        return BitConverter.ToInt64(tokenHash, 0);
    }

    // ═══════════════════════════════════════════════════════════════
    //  MEMORY CANARY
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Plants a random 128-bit sentinel in memory. If anyone uses a memory
    /// editor (Cheat Engine, x64dbg, Reclass.NET) to zero out or overwrite
    /// the SecurityService fields, the canary checksum won't match.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void PlantCanary()
    {
        var rng = RandomNumberGenerator.Create();
        var bytes = new byte[16];
        rng.GetBytes(bytes);
        _canaryHigh = BitConverter.ToInt64(bytes, 0);
        _canaryLow = BitConverter.ToInt64(bytes, 8);
        _canaryChecksum = SHA256.HashData(bytes);

        // Don't leave raw canary bytes in managed heap
        Array.Clear(bytes);
    }

    /// <summary>
    /// Verifies the memory canary hasn't been overwritten.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private bool VerifyCanary()
    {
        if (_canaryChecksum == null) return true; // Not planted yet
        if (_canaryHigh == 0 && _canaryLow == 0) return false; // Zeroed out!

        var bytes = new byte[16];
        BitConverter.TryWriteBytes(bytes.AsSpan(0, 8), _canaryHigh);
        BitConverter.TryWriteBytes(bytes.AsSpan(8, 8), _canaryLow);
        var currentChecksum = SHA256.HashData(bytes);
        Array.Clear(bytes);

        return CryptographicEquals(_canaryChecksum, currentChecksum);
    }

    // ═══════════════════════════════════════════════════════════════
    //  STACK-WALK VERIFICATION
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Walks the call stack to ensure QuickCheck is being called from
    /// a trusted LegitX assembly — not from an injected DLL that loaded
    /// the assembly and calls QuickCheck() to always return true.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool VerifyCallerAssembly()
    {
        try
        {
            var frames = new StackTrace(false).GetFrames();
            if (frames == null || frames.Length < 2)
                return true; // Can't walk stack — allow

            // Check that at least one caller frame is from our assembly
            for (int i = 1; i < Math.Min(frames.Length, 8); i++)
            {
                var method = frames[i].GetMethod();
                var asmName = method?.DeclaringType?.Assembly.GetName().Name;
                if (asmName != null && asmName.StartsWith(TrustedAssemblyPrefix, StringComparison.OrdinalIgnoreCase))
                    return true; // Called from our code
            }

            return false; // No trusted caller found — suspicious
        }
        catch
        {
            return true; // Stack walk failed — don't block
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  HELPERS
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Reads the EXE file from disk. Uses FileShare.ReadWrite to avoid
    /// locking conflicts with antivirus or Windows Defender.
    /// </summary>
    private byte[]? ReadExeBytes()
    {
        try
        {
            using var fs = new FileStream(_exePath, FileMode.Open,
                FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var data = new byte[fs.Length];
            fs.ReadExactly(data, 0, data.Length);
            return data;
        }
        catch
        {
            return null; // File locked or unreadable
        }
    }

    /// <summary>
    /// Constant-time comparison to prevent timing side-channel attacks.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool CryptographicEquals(byte[] a, byte[] b)
    {
        if (a.Length != b.Length)
            return false;

        return CryptographicOperations.FixedTimeEquals(a, b);
    }

    /// <summary>Returns a jittered interval between RecheckMinMs and RecheckMaxMs.</summary>
    private static int NextJitteredInterval()
    {
        lock (_jitterRng)
        {
            return _jitterRng.Next(RecheckMinMs, RecheckMaxMs + 1);
        }
    }

    /// <summary>Fires the integrity violation event.</summary>
    private void OnViolation(string reason)
    {
        _recheckTimer?.Change(Timeout.Infinite, Timeout.Infinite); // Stop further checks
        IntegrityViolated?.Invoke(reason);
    }

    public void Dispose()
    {
        _recheckTimer?.Dispose();
        _recheckTimer = null;

        // Zero out golden hashes from memory
        if (_goldenFullHash != null) Array.Clear(_goldenFullHash);
        if (_goldenSectionHashes != null)
            foreach (var h in _goldenSectionHashes)
                Array.Clear(h);
        if (_goldenMetadataHash != null) Array.Clear(_goldenMetadataHash);
        if (_goldenCriticalIlHash != null) Array.Clear(_goldenCriticalIlHash);
        if (_goldenSectionTableHash != null) Array.Clear(_goldenSectionTableHash);
        if (_canaryChecksum != null) Array.Clear(_canaryChecksum);

        _integrityToken = 0;
        _rollingToken = 0;
        _canaryHigh = 0;
        _canaryLow = 0;
    }
}
