using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;

namespace LegitX.WPF.Services;

/// <summary>
/// When the app runs as <c>dotnet LegitX V2.dll</c>, <see cref="Environment.ProcessPath"/> points at the host
/// (<c>dotnet.exe</c>), not the managed image. <see cref="SecurityService"/> fingerprints the host EXE instead.
/// This guard captures a golden SHA-256 of the real managed assembly file and re-verifies it periodically.
/// </summary>
internal sealed class ManagedImageGuard : IDisposable
{
    private string? _dllPath;
    private byte[]? _goldenSha256;
    private long _goldenSize;

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    public bool Initialize()
    {
        try
        {
            var loc = typeof(ManagedImageGuard).Assembly.Location;
            if (string.IsNullOrEmpty(loc))
                return true; // single-file — no separate DLL on disk

            var proc = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(proc) &&
                string.Equals(Path.GetFullPath(loc), Path.GetFullPath(proc), StringComparison.OrdinalIgnoreCase))
                return true; // native apphost is our image — SecurityService already covers it

            if (!File.Exists(loc))
                return !SecurityPolicy.StrictFailClosed;

            _dllPath = loc;
            var bytes = ReadAllBytesShared(loc);
            if (bytes == null || bytes.Length == 0)
                return !SecurityPolicy.StrictFailClosed;

            _goldenSize = bytes.Length;
            _goldenSha256 = SHA256.HashData(bytes);
            Array.Clear(bytes);
            return true;
        }
        catch
        {
            return !SecurityPolicy.StrictFailClosed;
        }
    }

    /// <summary>Verifies the managed assembly on disk still matches startup.</summary>
    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    public bool TryVerify(out string? failureReason)
    {
        failureReason = null;
        try
        {
            if (_dllPath == null || _goldenSha256 == null)
                return true;

            if (!File.Exists(_dllPath))
            {
                failureReason = "Managed assembly file missing.";
                return false;
            }

            var fi = new FileInfo(_dllPath);
            if (!fi.Exists || fi.Length != _goldenSize)
            {
                failureReason = "Managed assembly size changed on disk.";
                return false;
            }

            var bytes = ReadAllBytesShared(_dllPath);
            if (bytes == null)
                return true;

            try
            {
                if (bytes.Length != _goldenSize)
                {
                    failureReason = "Managed assembly length mismatch.";
                    return false;
                }

                var hash = SHA256.HashData(bytes);
                if (!CryptographicOperations.FixedTimeEquals(hash, _goldenSha256))
                {
                    failureReason = "Managed assembly was modified or replaced (LegitX V2.dll).";
                    return false;
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(bytes);
            }

            return true;
        }
        catch
        {
            failureReason = "Managed assembly verification error.";
            return !SecurityPolicy.StrictFailClosed;
        }
    }

    private static byte[]? ReadAllBytesShared(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            var len = fs.Length;
            if (len <= 0 || len > 200 * 1024 * 1024)
                return null;
            var buf = new byte[len];
            fs.ReadExactly(buf, 0, buf.Length);
            return buf;
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (_goldenSha256 != null)
            CryptographicOperations.ZeroMemory(_goldenSha256);
        _goldenSha256 = null;
        _dllPath = null;
    }
}
