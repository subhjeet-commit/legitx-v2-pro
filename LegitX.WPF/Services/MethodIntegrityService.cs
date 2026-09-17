using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace LegitX.WPF.Services;

/// <summary>
/// Layer 2 — Method Integrity Verification (Anti-Patch)
/// Detects runtime patching via Harmony, MonoMod, or manual IL replacement.
/// </summary>
internal sealed class MethodIntegrityService : IDisposable
{
    // ═══════════════════════════════════════════════════════════════
    //  Types
    // ═══════════════════════════════════════════════════════════════

    private readonly record struct MethodFingerprint(
        int MetadataToken,
        int IlSize,
        byte[] IlHash,
        int LocalVarCount,
        int MaxStackSize,
        int ExceptionHandlerCount,
        nint NativeCodeAddress);

    // ═══════════════════════════════════════════════════════════════
    //  State
    // ═══════════════════════════════════════════════════════════════

    private Dictionary<int, MethodFingerprint>? _goldenFingerprints;
    private System.Threading.Timer? _scanTimer;
    private static readonly Random _jitter = new();

    public event Action<string>? MethodTampered;

    // ═══════════════════════════════════════════════════════════════
    //  Critical methods to monitor
    // ═══════════════════════════════════════════════════════════════

    private static readonly (Type Type, string Name, BindingFlags Flags)[] CriticalMethods =
    [
        (typeof(LicenseService),     "VerifyLicenseAsync",      BindingFlags.Public | BindingFlags.Static),
        (typeof(LicenseService),     "CheckHwidBanAsync",       BindingFlags.Public | BindingFlags.Static),
        (typeof(LicenseService),     "RedeemCodeAsync",         BindingFlags.Public | BindingFlags.Static),
        (typeof(HardwareIdService),  "VerifyAndRegisterAsync",  BindingFlags.Public | BindingFlags.Static),
        (typeof(HardwareIdService),  "GetPermanentHardwareHash",BindingFlags.Public | BindingFlags.Static),
        (typeof(HardwareIdService),  "GetCpuId",                BindingFlags.Public | BindingFlags.Static),
        (typeof(HardwareIdService),  "GetMotherboardSerial",    BindingFlags.Public | BindingFlags.Static),
        (typeof(HardwareIdService),  "GetBiosSerial",           BindingFlags.Public | BindingFlags.Static),
        (typeof(HardwareIdService),  "GetSmbiosUuid",           BindingFlags.Public | BindingFlags.Static),
        (typeof(AuthService),        "IsLoggedIn",              BindingFlags.Public | BindingFlags.Static),
        (typeof(AuthService),        "ValidateSessionAsync",    BindingFlags.Public | BindingFlags.Static),
        (typeof(AuthService),        "LoginAsync",              BindingFlags.Public | BindingFlags.Static),
        (typeof(AuthService),        "GoogleSignInAsync",       BindingFlags.Public | BindingFlags.Static),
        (typeof(AuthService),        "Logout",                  BindingFlags.Public | BindingFlags.Static),
        (typeof(SessionTokenService),"BindSessionAsync",        BindingFlags.Public | BindingFlags.Static),
        (typeof(SessionTokenService),"ValidateAsync",           BindingFlags.Public | BindingFlags.Static),
        (typeof(SecurityService),    "QuickCheck",              BindingFlags.Public | BindingFlags.Instance),
        (typeof(SecurityService),    "Initialize",              BindingFlags.Public | BindingFlags.Instance),
        (typeof(MethodIntegrityService), "Scan",                BindingFlags.Public | BindingFlags.Instance),
    ];

    // Common Harmony/MonoMod detour prologues (x64)
    // jmp qword [rip+0]  = FF 25 00 00 00 00
    // mov r11, imm64      = 49 BB xx xx xx xx xx xx xx xx ; jmp r11 = 41 FF E3
    // mov rax, imm64      = 48 B8 xx xx xx xx xx xx xx xx ; jmp rax = FF E0
    private static readonly byte[][] DetourSignatures =
    [
        [0xFF, 0x25, 0x00, 0x00, 0x00, 0x00],          // jmp [rip+0]
        [0x49, 0xBB],                                    // mov r11, imm64 (first 2 bytes)
        [0x48, 0xB8],                                    // mov rax, imm64 (first 2 bytes)
        [0xE9],                                          // jmp rel32 (near jump)
    ];

    // ═══════════════════════════════════════════════════════════════
    //  Public API
    // ═══════════════════════════════════════════════════════════════

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    public bool Initialize()
    {
        try
        {
            _goldenFingerprints = CaptureAllFingerprints();
            if (_goldenFingerprints == null || _goldenFingerprints.Count == 0)
                return true;

            // Background scan every 30–60s (offset from Layer 1's 45–90s)
            var delay = NextInterval();
            _scanTimer = new System.Threading.Timer(OnScanTimer, null, delay, Timeout.Infinite);
            return true;
        }
        catch { return !SecurityPolicy.StrictFailClosed; }
    }

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    public bool Scan()
    {
        try
        {
            if (_goldenFingerprints == null || _goldenFingerprints.Count == 0)
                return true;

            var current = CaptureAllFingerprints();
            if (current == null)
                return true;

            foreach (var (token, golden) in _goldenFingerprints)
            {
                if (!current.TryGetValue(token, out var now))
                {
                    OnTamper($"Method 0x{token:X8} removed from assembly.");
                    return false;
                }

                // IL body replaced or NOPped
                if (now.IlSize != golden.IlSize)
                {
                    OnTamper($"Method 0x{token:X8} IL size changed ({golden.IlSize}→{now.IlSize}).");
                    return false;
                }

                if (!CryptographicOperations.FixedTimeEquals(golden.IlHash, now.IlHash))
                {
                    OnTamper($"Method 0x{token:X8} IL bytecode modified.");
                    return false;
                }

                // Local variable or exception handler count changed
                if (now.LocalVarCount != golden.LocalVarCount ||
                    now.ExceptionHandlerCount != golden.ExceptionHandlerCount)
                {
                    OnTamper($"Method 0x{token:X8} structure modified (locals/EH).");
                    return false;
                }

                // MaxStack changed — tools that rewrite IL often miscalculate this
                if (now.MaxStackSize != golden.MaxStackSize)
                {
                    OnTamper($"Method 0x{token:X8} MaxStack changed ({golden.MaxStackSize}→{now.MaxStackSize}).");
                    return false;
                }

                // Native code address changed AND old address was valid
                // (initial JIT compilation is allowed, but re-JIT to a new address = detour)
                if (golden.NativeCodeAddress != 0 && now.NativeCodeAddress != 0 &&
                    golden.NativeCodeAddress != now.NativeCodeAddress)
                {
                    OnTamper($"Method 0x{token:X8} native code pointer relocated (possible detour).");
                    return false;
                }

                // Check native prologue for Harmony/MonoMod detour patterns
                if (now.NativeCodeAddress != 0 && HasDetourPrologue(now.NativeCodeAddress))
                {
                    OnTamper($"Method 0x{token:X8} has a JMP-detour prologue (Harmony/MonoMod).");
                    return false;
                }
            }

            // Update native code addresses for methods that got JIT-compiled since last scan
            foreach (var (token, now) in current)
            {
                if (_goldenFingerprints.TryGetValue(token, out var golden) &&
                    golden.NativeCodeAddress == 0 && now.NativeCodeAddress != 0)
                {
                    _goldenFingerprints[token] = golden with { NativeCodeAddress = now.NativeCodeAddress };
                }
            }

            return true;
        }
        catch { return !SecurityPolicy.StrictFailClosed; }
    }

    // ═══════════════════════════════════════════════════════════════
    //  Fingerprinting
    // ═══════════════════════════════════════════════════════════════

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Dictionary<int, MethodFingerprint>? CaptureAllFingerprints()
    {
        try
        {
            var map = new Dictionary<int, MethodFingerprint>(CriticalMethods.Length);

            foreach (var (type, name, flags) in CriticalMethods)
            {
                try
                {
                    var mi = type.GetMethod(name, flags);
                    if (mi == null) continue;

                    var fp = CaptureFingerprint(mi);
                    if (fp.HasValue)
                        map[mi.MetadataToken] = fp.Value;
                }
                catch { }
            }

            return map.Count > 0 ? map : null;
        }
        catch { return null; }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static MethodFingerprint? CaptureFingerprint(MethodInfo mi)
    {
        try
        {
            var body = mi.GetMethodBody();
            if (body == null) return null;

            var il = body.GetILAsByteArray();
            if (il == null || il.Length == 0) return null;

            var ilHash = SHA256.HashData(il);
            var localCount = body.LocalVariables?.Count ?? 0;
            var maxStack = body.MaxStackSize;
            var ehCount = body.ExceptionHandlingClauses?.Count ?? 0;

            // Get JIT-compiled native address (0 if not yet JITted)
            nint nativeAddr = 0;
            try
            {
                RuntimeHelpers.PrepareMethod(mi.MethodHandle);
                nativeAddr = mi.MethodHandle.GetFunctionPointer();
            }
            catch { }

            return new MethodFingerprint(
                mi.MetadataToken, il.Length, ilHash,
                localCount, maxStack, ehCount, nativeAddr);
        }
        catch { return null; }
    }

    // ═══════════════════════════════════════════════════════════════
    //  Detour Detection — checks native function prologue
    // ═══════════════════════════════════════════════════════════════

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool HasDetourPrologue(nint address)
    {
        try
        {
            if (address == 0) return false;

            // Read first 16 bytes of the native method
            Span<byte> prologue = stackalloc byte[16];
            Marshal.Copy(address, prologue.ToArray(), 0, 16); // safe copy
            var prologueArr = prologue.ToArray();

            foreach (var sig in DetourSignatures)
            {
                if (prologueArr.Length >= sig.Length &&
                    prologueArr.AsSpan(0, sig.Length).SequenceEqual(sig))
                    return true;
            }

            return false;
        }
        catch { return false; }
    }

    // ═══════════════════════════════════════════════════════════════
    //  Background Timer
    // ═══════════════════════════════════════════════════════════════

    private void OnScanTimer(object? state)
    {
        try
        {
            if (!Scan())
                return; // OnTamper already fired
        }
        catch { }
        finally
        {
            try { _scanTimer?.Change(NextInterval(), Timeout.Infinite); }
            catch { }
        }
    }

    private static int NextInterval()
    {
        lock (_jitter) { return _jitter.Next(30_000, 60_001); }
    }

    private void OnTamper(string detail)
    {
        _scanTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        MethodTampered?.Invoke(detail);
    }

    public void Dispose()
    {
        _scanTimer?.Dispose();
        _scanTimer = null;
        if (_goldenFingerprints != null)
        {
            foreach (var fp in _goldenFingerprints.Values)
                Array.Clear(fp.IlHash);
            _goldenFingerprints.Clear();
        }
    }
}
