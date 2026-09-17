using System.Management;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LegitX.WPF.Services;

/// <summary>
/// Two-layer hardware ID lockdown system for per-user PC locking.
///
/// ═══════════════════════════════════════════════════════════════════
/// LAYER 1 — PERMANENT IDs  (NEVER change — even after full OS reinstall/reset)
/// ═══════════════════════════════════════════════════════════════════
///   • CPU ProcessorId           (Win32_Processor — burned into silicon)
///   • Motherboard SerialNumber  (Win32_BaseBoard — physically stamped)
///   • BIOS SerialNumber         (Win32_BIOS — firmware chip)
///   • SMBIOS UUID               (Win32_ComputerSystemProduct — motherboard firmware UUID)
///   • First disk SerialNumber   (Win32_DiskDrive — factory-stamped on the drive PCB)
///   • BaseBoard Product / Manufacturer (Win32_BaseBoard — model identification)
///   • TPM ManufacturerId        (Win32_Tpm — if present, hardware security chip)
///
///   → Combined into: permanentHash (SHA-256)
///   → This NEVER gets reset. If it doesn't match → different PC → BLOCKED forever.
///   → Even if user reinstalls Windows, formats all drives, resets BIOS — these stay.
///   → Only "FULL_RESET" from admin website (account transfer) clears this.
///
/// ═══════════════════════════════════════════════════════════════════
/// LAYER 2 — WINDOWS IDs  (change after OS reinstall / reset)
/// ═══════════════════════════════════════════════════════════════════
///   • Machine GUID              (Registry — regenerated on OS install)
///   • Windows Product ID        (Registry — tied to this Windows installation)
///   • Computer Name             (User-changeable)
///   • Windows Build / Edition   (Changes on reinstall)
///   • Install Date              (Changes on reinstall)
///
///   → Combined into: windowsHash (SHA-256)
///   → Admin website has "Allow New Windows" button → sets windowsReset=true
///   → On next login, if permanentHash matches but windowsHash doesn't:
///       - If windowsReset=true → re-register windowsHash → login OK
///       - If windowsReset=false → still allow (they just reinstalled Windows on same PC)
///
/// ═══════════════════════════════════════════════════════════════════
/// Firestore Cloud Database structure:
///   Collection: users → Document: {uid} → Sub-collection: hwid → Document: data
///     fields:
///       permanentHash     : string  "sha256..."       ← the real lock
///       windowsHash       : string  "sha256..."       ← secondary check
///       windowsReset      : boolean (true/false)      ← admin sets true to allow new Windows
///       fullReset         : boolean (true/false)      ← admin sets true for full account transfer
///       registeredAt      : string  "ISO timestamp"
///       lastLogin         : string  "ISO timestamp"
///       computerName      : string  "DESKTOP-..."
///       permanent         : map     { cpuId, moboSerial, biosSerial, ... }
///       windows           : map     { machineGuid, productId, computerName, ... }
/// </summary>
public static class HardwareIdService
{
    // Firestore URL now pulled from SecureStrings — no plaintext project ID in this file
    private static string FirestoreBaseUrl => SecureStrings.FirestoreBaseUrl;
    private static readonly HttpClient _http = SecureHttpClient.Create(TimeSpan.FromSeconds(15));

    // ═══════════════════════════════════════════════════════════════
    //  PERMANENT HARDWARE COLLECTION (survive any OS reinstall)
    // ═══════════════════════════════════════════════════════════════

    #region Permanent Hardware IDs

    private static string GetWmiValue(string wmiClass, string property)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher($"SELECT {property} FROM {wmiClass}");
            foreach (var obj in searcher.Get())
            {
                var val = obj[property]?.ToString()?.Trim();
                if (!string.IsNullOrEmpty(val) &&
                    val != "To Be Filled By O.E.M." &&
                    val != "Default string" &&
                    val != "None" &&
                    val != "Not Specified" &&
                    val != "System Serial Number" &&
                    val != "System Product Name" &&
                    val != "All Series")
                    return val;
            }
        }
        catch { }
        return "";
    }

    /// <summary>CPU ProcessorId — burned into silicon at factory, never changes.</summary>
    public static string GetCpuId() => GetWmiValue("Win32_Processor", "ProcessorId");

    /// <summary>Motherboard serial — physically printed on the PCB.</summary>
    public static string GetMotherboardSerial() => GetWmiValue("Win32_BaseBoard", "SerialNumber");

    /// <summary>BIOS serial — stored in firmware chip, survives OS reinstall.</summary>
    public static string GetBiosSerial() => GetWmiValue("Win32_BIOS", "SerialNumber");

    /// <summary>SMBIOS UUID — motherboard firmware UUID, extremely hard to change.</summary>
    public static string GetSmbiosUuid() => GetWmiValue("Win32_ComputerSystemProduct", "UUID");

    /// <summary>Motherboard product name — identifies the exact board model.</summary>
    public static string GetMotherboardProduct() => GetWmiValue("Win32_BaseBoard", "Product");

    /// <summary>Motherboard manufacturer.</summary>
    public static string GetMotherboardManufacturer() => GetWmiValue("Win32_BaseBoard", "Manufacturer");

    /// <summary>Primary physical disk serial — factory-stamped on the drive's PCB.</summary>
    public static string GetDiskSerial()
    {
        try
        {
            // Try fixed disks first
            using var searcher = new ManagementObjectSearcher(
                "SELECT SerialNumber FROM Win32_DiskDrive WHERE MediaType LIKE '%fixed%' OR MediaType LIKE '%Fixed%'");
            foreach (var obj in searcher.Get())
            {
                var val = obj["SerialNumber"]?.ToString()?.Trim();
                if (!string.IsNullOrEmpty(val)) return val;
            }
            // Fallback: any disk
            using var fallback = new ManagementObjectSearcher("SELECT SerialNumber FROM Win32_DiskDrive");
            foreach (var obj in fallback.Get())
            {
                var val = obj["SerialNumber"]?.ToString()?.Trim();
                if (!string.IsNullOrEmpty(val)) return val;
            }
        }
        catch { }
        return "";
    }

    /// <summary>TPM ManufacturerId — hardware security module ID (if present).</summary>
    public static string GetTpmId()
    {
        try
        {
            // TPM is in root\CIMV2\Security\MicrosoftTpm namespace
            using var searcher = new ManagementObjectSearcher(
                @"root\CIMV2\Security\MicrosoftTpm",
                "SELECT ManufacturerId FROM Win32_Tpm");
            foreach (var obj in searcher.Get())
            {
                var val = obj["ManufacturerId"]?.ToString()?.Trim();
                if (!string.IsNullOrEmpty(val)) return val;
            }
        }
        catch { }
        return "";
    }

    #endregion

    // ═══════════════════════════════════════════════════════════════
    //  WINDOWS IDS (change after OS reinstall / reset)
    // ═══════════════════════════════════════════════════════════════

    #region Windows IDs

    /// <summary>Machine GUID — regenerated on every Windows install.</summary>
    public static string GetMachineGuid()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Cryptography", false);
            return key?.GetValue("MachineGuid")?.ToString() ?? "";
        }
        catch { return ""; }
    }

    /// <summary>Windows Product ID — tied to this specific installation.</summary>
    public static string GetWindowsProductId()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion", false);
            return key?.GetValue("ProductId")?.ToString() ?? "";
        }
        catch { return ""; }
    }

    /// <summary>Windows build string (e.g., "22631.2428").</summary>
    public static string GetWindowsBuild()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion", false);
            var major = key?.GetValue("CurrentBuildNumber")?.ToString() ?? "";
            var ubr = key?.GetValue("UBR")?.ToString() ?? "";
            return string.IsNullOrEmpty(ubr) ? major : $"{major}.{ubr}";
        }
        catch { return ""; }
    }

    /// <summary>Windows original install date (registry timestamp).</summary>
    public static string GetWindowsInstallDate()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion", false);
            var raw = key?.GetValue("InstallDate");
            if (raw is int epoch)
            {
                var dt = DateTimeOffset.FromUnixTimeSeconds(epoch);
                return dt.ToString("o");
            }
            return raw?.ToString() ?? "";
        }
        catch { return ""; }
    }

    #endregion

    // ═══════════════════════════════════════════════════════════════
    //  HASH GENERATION
    // ═══════════════════════════════════════════════════════════════

    #region Hash Generation

    /// <summary>
    /// SHA-256 hash of ALL permanent hardware IDs combined.
    /// This hash will NEVER change even after Windows reinstall/reset.
    /// It identifies the PHYSICAL COMPUTER itself.
    /// </summary>
    public static string GetPermanentHardwareHash()
    {
        var cpu = GetCpuId();
        var mobo = GetMotherboardSerial();
        var bios = GetBiosSerial();
        var uuid = GetSmbiosUuid();
        var disk = GetDiskSerial();
        var moboProduct = GetMotherboardProduct();
        var moboMfg = GetMotherboardManufacturer();
        var tpm = GetTpmId();

        // Combine with delimiters — order matters
        var combined = $"LEGITX_PERMANENT_V2|{cpu}|{mobo}|{bios}|{uuid}|{disk}|{moboProduct}|{moboMfg}|{tpm}|LOCK_SALT_2024";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(combined));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// SHA-256 hash of Windows-level IDs that change after reinstall.
    /// </summary>
    public static string GetWindowsHash()
    {
        var machineGuid = GetMachineGuid();
        var productId = GetWindowsProductId();
        var computerName = Environment.MachineName;
        var winBuild = GetWindowsBuild();
        var installDate = GetWindowsInstallDate();

        var combined = $"LEGITX_WINDOWS_V2|{machineGuid}|{productId}|{computerName}|{winBuild}|{installDate}|LOCK_SALT_2024";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(combined));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>Collects all permanent hardware details for Firebase storage / admin debug.</summary>
    public static Dictionary<string, string> GetPermanentDetails()
    {
        return new Dictionary<string, string>
        {
            ["cpuId"] = GetCpuId(),
            ["moboSerial"] = GetMotherboardSerial(),
            ["biosSerial"] = GetBiosSerial(),
            ["smbiosUuid"] = GetSmbiosUuid(),
            ["diskSerial"] = GetDiskSerial(),
            ["moboProduct"] = GetMotherboardProduct(),
            ["moboMfg"] = GetMotherboardManufacturer(),
            ["tpmId"] = GetTpmId(),
        };
    }

    /// <summary>Collects all Windows-level details for Firebase storage.</summary>
    public static Dictionary<string, string> GetWindowsDetails()
    {
        return new Dictionary<string, string>
        {
            ["machineGuid"] = GetMachineGuid(),
            ["productId"] = GetWindowsProductId(),
            ["computerName"] = Environment.MachineName,
            ["winBuild"] = GetWindowsBuild(),
            ["installDate"] = GetWindowsInstallDate(),
        };
    }

    #endregion

    // ═══════════════════════════════════════════════════════════════
    //  FIRESTORE HELPERS
    // ═══════════════════════════════════════════════════════════════

    #region Firestore Helpers

    /// <summary>Creates an HttpRequestMessage with Bearer auth header.</summary>
    private static HttpRequestMessage CreateRequest(HttpMethod method, string url, string? idToken)
    {
        var request = new HttpRequestMessage(method, url);
        if (!string.IsNullOrEmpty(idToken))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", idToken);
        return request;
    }

    /// <summary>Extracts a string value from a Firestore field element.</summary>
    private static string? GetFirestoreString(JsonElement fields, string fieldName)
    {
        if (fields.TryGetProperty(fieldName, out var field) &&
            field.TryGetProperty("stringValue", out var val))
            return val.GetString();
        return null;
    }

    /// <summary>Extracts a boolean value from a Firestore field element.</summary>
    private static bool GetFirestoreBool(JsonElement fields, string fieldName, bool defaultValue = false)
    {
        if (fields.TryGetProperty(fieldName, out var field) &&
            field.TryGetProperty("booleanValue", out var val))
            return val.GetBoolean();
        return defaultValue;
    }

    /// <summary>Wraps a string as a Firestore stringValue object.</summary>
    private static object StringField(string value) => new { stringValue = value };

    /// <summary>Wraps a bool as a Firestore booleanValue object.</summary>
    private static object BoolField(bool value) => new { booleanValue = value };

    /// <summary>Wraps a Dictionary&lt;string,string&gt; as a Firestore mapValue object.</summary>
    private static object MapField(Dictionary<string, string> dict)
    {
        var mapFields = new Dictionary<string, object>();
        foreach (var kvp in dict)
            mapFields[kvp.Key] = new { stringValue = kvp.Value };
        return new { mapValue = new { fields = mapFields } };
    }

    #endregion

    // ═══════════════════════════════════════════════════════════════
    //  FIRESTORE HWID VERIFICATION — THE CORE LOCK LOGIC
    // ═══════════════════════════════════════════════════════════════

    #region Firestore HWID Verification

    public record HwidCheckResult(bool Allowed, string Reason, bool IsFirstLogin);

    /// <summary>
    /// Deep hardware verification against Firebase — compares INDIVIDUAL hardware components.
    ///
    /// LOGIC:
    ///   1. No HWID record exists            → first login → register ALL IDs → ALLOW
    ///   2. fullReset = true (admin)          → re-register ALL IDs → ALLOW  (account transfer)
    ///   3. Compare EACH permanent component individually:
    ///      - cpuId, biosSerial, diskSerial, smbiosUuid, moboSerial, moboProduct, moboMfg, tpmId
    ///      - If ALL permanent components match → SAME PHYSICAL PC → ALLOW
    ///        └─ If windowsHash mismatch → they reinstalled Windows
    ///             - If windowsReset=true → re-register Windows IDs → ALLOW
    ///             - If windowsReset=false → tell them to request HWID reset from website
    ///      - If MOST permanent components match (1-2 differ) → possible hardware upgrade
    ///        → tell them to request HWID reset from website  
    ///      - If MANY permanent components differ → COMPLETELY DIFFERENT PC → BLOCKED
    ///        → Only "Full Reset" from admin website transfers the account
    /// </summary>
    public static async Task<HwidCheckResult> VerifyAndRegisterAsync(string uid, string? idToken = null)
    {
        var result = await VerifyAndRegisterInternalAsync(uid, idToken).ConfigureAwait(false);

        // ── Layer 6: Store encrypted result so memory-patching 'Allowed=true' is useless ──
        EncryptedResultStore.StoreHwid(result.Allowed, result.Reason);

        return result;
    }

    private static async Task<HwidCheckResult> VerifyAndRegisterInternalAsync(string uid, string? idToken = null)
    {
        if (string.IsNullOrEmpty(uid))
            return new HwidCheckResult(false, "Invalid user ID.", false);

        // ── Integrity gate — if binary is tampered, HWID checks always fail ──
        if (System.Windows.Application.Current is App app && app.Security != null && !app.Security.QuickCheck())
            return new HwidCheckResult(false, "Application integrity check failed.", false);

        try
        {
            var currentPermanentHash = GetPermanentHardwareHash();
            var currentWindowsHash = GetWindowsHash();
            var permanentDetails = GetPermanentDetails();
            var windowsDetails = GetWindowsDetails();

            // ── Fetch existing HWID document from Firestore ──
            var url = $"{FirestoreBaseUrl}/users/{uid}/hwid/data";
            var request = CreateRequest(HttpMethod.Get, url, idToken);

            var response = await _http.SendAsync(request).ConfigureAwait(false);
            string? storedPermanentHash = null;
            string? storedWindowsHash = null;
            bool fullReset = false;
            bool windowsReset = false;
            Dictionary<string, string>? storedPermanent = null;

            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(json))
                {
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("fields", out var fields))
                    {
                        storedPermanentHash = GetFirestoreString(fields, "permanentHash");
                        storedWindowsHash = GetFirestoreString(fields, "windowsHash");
                        fullReset = GetFirestoreBool(fields, "fullReset");
                        windowsReset = GetFirestoreBool(fields, "windowsReset");

                        // Extract the stored permanent map for individual comparison
                        storedPermanent = ExtractMapFields(fields, "permanent");
                    }
                }
            }

            // ── Case 1: No HWID registered yet — first login ever ──
            if (string.IsNullOrEmpty(storedPermanentHash))
            {
                await RegisterAllHardwareAsync(uid, currentPermanentHash, currentWindowsHash,
                    permanentDetails, windowsDetails, idToken);
                return new HwidCheckResult(true, "Hardware registered successfully.", true);
            }

            // ── Case 2: Admin did "Full Reset" (account transfer to new PC) ──
            if (fullReset)
            {
                await RegisterAllHardwareAsync(uid, currentPermanentHash, currentWindowsHash,
                    permanentDetails, windowsDetails, idToken);
                return new HwidCheckResult(true, "Hardware re-registered (admin full reset).", true);
            }

            // ── Case 3: Quick check — permanent hash matches exactly → same PC ──
            if (storedPermanentHash == currentPermanentHash)
            {
                // Same PC — check if Windows hash changed
                if (storedWindowsHash != currentWindowsHash)
                {
                    if (windowsReset)
                    {
                        // Admin approved Windows reset — re-register Windows IDs
                        await UpdateLoginInfoAsync(uid, currentWindowsHash, windowsDetails, true, idToken);
                        return new HwidCheckResult(true, "Windows IDs updated (admin approved reset).", false);
                    }
                    else
                    {
                        // Windows changed but same PC hardware — auto-allow and update
                        await UpdateLoginInfoAsync(uid, currentWindowsHash, windowsDetails, false, idToken);
                        return new HwidCheckResult(true, "Hardware verified.", false);
                    }
                }

                // Everything matches perfectly — clear windowsReset if it was left pending
                await UpdateLoginInfoAsync(uid, currentWindowsHash, windowsDetails, windowsReset, idToken);
                return new HwidCheckResult(true, "Hardware verified.", false);
            }

            // ── Case 4: Permanent hash mismatch — do DEEP component-by-component comparison ──
            if (storedPermanent != null && storedPermanent.Count > 0)
            {
                var comparison = CompareHardwareComponents(storedPermanent, permanentDetails);

                // All critical permanent components match → hash diff is from minor change (TPM update, etc.)
                if (comparison.CriticalMismatches == 0)
                {
                    // Same core hardware — minor hash difference, allow and re-register
                    await RegisterAllHardwareAsync(uid, currentPermanentHash, currentWindowsHash,
                        permanentDetails, windowsDetails, idToken);
                    return new HwidCheckResult(true, "Hardware verified (minor update detected).", false);
                }

                // 1-2 critical components differ → possible hardware upgrade (new disk, new CPU, etc.)
                if (comparison.CriticalMismatches <= 2)
                {
                    var changedParts = string.Join(", ", comparison.ChangedComponents);
                    return new HwidCheckResult(false,
                        $"⚠ Hardware Change Detected\n\n" +
                        $"The following hardware components have changed:\n" +
                        $"  • {changedParts}\n\n" +
                        "If you upgraded your hardware, you need to request\n" +
                        "an HWID reset from the LegitX website.\n\n" +
                        "Go to legitx.com → Account → Request HWID Reset\n" +
                        "or contact support for assistance.",
                        false);
                }

                // 3+ critical components differ → completely different PC
                return new HwidCheckResult(false,
                    "⚠ This account is locked to a different PC.\n\n" +
                    $"{comparison.CriticalMismatches} out of {comparison.TotalChecked} hardware components " +
                    "do not match the registered computer.\n\n" +
                    "This account can only be used on the PC where it\n" +
                    "was first activated.\n\n" +
                    "• Reinstalling Windows does NOT help.\n" +
                    "• Only the account owner can request a full\n" +
                    "  transfer from the LegitX website.\n\n" +
                    "Go to legitx.com → Account → Request Full Reset\n" +
                    "or contact support at legitx.com/support",
                    false);
            }

            // ── Fallback: No stored permanent map (old data format) — use hash comparison ──
            return new HwidCheckResult(false,
                "⚠ This account is locked to another PC.\n\n" +
                "LegitX V2 is bound to the hardware where it was first activated.\n\n" +
                "If you are the owner, visit legitx.com/support to request\n" +
                "an HWID reset.",
                false);
        }
        catch (HttpRequestException)
        {
            return new HwidCheckResult(true, "Hardware check skipped (no internet).", false);
        }
        catch (TaskCanceledException)
        {
            return new HwidCheckResult(true, "Hardware check skipped (timeout).", false);
        }
        catch (Exception ex)
        {
            return new HwidCheckResult(true, $"Hardware check skipped: {ex.Message}", false);
        }
    }

    #endregion

    // ═══════════════════════════════════════════════════════════════
    //  DEEP HARDWARE COMPARISON
    // ═══════════════════════════════════════════════════════════════

    #region Deep Hardware Comparison

    public record HardwareComparison(
        int TotalChecked,
        int CriticalMismatches,
        int MinorMismatches,
        List<string> ChangedComponents,
        List<string> MatchedComponents);

    /// <summary>
    /// Compares stored permanent hardware fields with current hardware field by field.
    /// Critical = CPU, BIOS, Disk, SMBIOS UUID (very hard/impossible to change)
    /// Minor = moboSerial (sometimes blank), moboMfg, moboProduct, tpmId
    /// </summary>
    private static HardwareComparison CompareHardwareComponents(
        Dictionary<string, string> stored, Dictionary<string, string> current)
    {
        // Critical components — these physically identify the PC
        var criticalKeys = new (string Key, string Label)[]
        {
            ("cpuId",       "CPU Processor ID"),
            ("biosSerial",  "BIOS Serial"),
            ("diskSerial",  "Disk Serial"),
            ("smbiosUuid",  "SMBIOS UUID"),
        };

        // Minor components — can sometimes be blank or change with BIOS update
        var minorKeys = new (string Key, string Label)[]
        {
            ("moboSerial",  "Motherboard Serial"),
            ("moboProduct", "Motherboard Model"),
            ("moboMfg",     "Motherboard Manufacturer"),
            ("tpmId",       "TPM ID"),
        };

        int criticalMismatches = 0, minorMismatches = 0, totalChecked = 0;
        var changed = new List<string>();
        var matched = new List<string>();

        foreach (var (key, label) in criticalKeys)
        {
            var storedVal = stored.GetValueOrDefault(key, "");
            var currentVal = current.GetValueOrDefault(key, "");

            // Skip comparison if both are empty (component doesn't exist on this PC)
            if (string.IsNullOrEmpty(storedVal) && string.IsNullOrEmpty(currentVal))
                continue;

            totalChecked++;

            if (!string.Equals(storedVal, currentVal, StringComparison.OrdinalIgnoreCase))
            {
                criticalMismatches++;
                changed.Add(label);
            }
            else
            {
                matched.Add(label);
            }
        }

        foreach (var (key, label) in minorKeys)
        {
            var storedVal = stored.GetValueOrDefault(key, "");
            var currentVal = current.GetValueOrDefault(key, "");

            if (string.IsNullOrEmpty(storedVal) && string.IsNullOrEmpty(currentVal))
                continue;

            totalChecked++;

            if (!string.Equals(storedVal, currentVal, StringComparison.OrdinalIgnoreCase))
            {
                minorMismatches++;
                // Don't count minor changes as critical
            }
            else
            {
                matched.Add(label);
            }
        }

        return new HardwareComparison(totalChecked, criticalMismatches, minorMismatches, changed, matched);
    }

    /// <summary>
    /// Extracts a Firestore map field into a string dictionary.
    /// Firestore format: { "permanent": { "mapValue": { "fields": { "cpuId": { "stringValue": "..." }, ... } } } }
    /// </summary>
    private static Dictionary<string, string>? ExtractMapFields(JsonElement fields, string mapName)
    {
        try
        {
            if (!fields.TryGetProperty(mapName, out var mapField))
                return null;
            if (!mapField.TryGetProperty("mapValue", out var mapValue))
                return null;
            if (!mapValue.TryGetProperty("fields", out var mapFields))
                return null;

            var result = new Dictionary<string, string>();
            foreach (var prop in mapFields.EnumerateObject())
            {
                if (prop.Value.TryGetProperty("stringValue", out var strVal))
                    result[prop.Name] = strVal.GetString() ?? "";
            }
            return result;
        }
        catch
        {
            return null;
        }
    }

    #endregion

    // ═══════════════════════════════════════════════════════════════
    //  FIRESTORE WRITE OPERATIONS
    // ═══════════════════════════════════════════════════════════════

    #region Firestore Write Operations

    /// <summary>
    /// Full registration of ALL hardware IDs (first login or full account transfer).
    /// Writes everything + clears both reset flags.
    /// Document path: users/{uid}/hwid/data
    /// </summary>
    private static async Task RegisterAllHardwareAsync(
        string uid, string permanentHash, string windowsHash,
        Dictionary<string, string> permanentDetails,
        Dictionary<string, string> windowsDetails,
        string? idToken)
    {
        var now = DateTime.UtcNow.ToString("o");

        // Build Firestore document with typed fields
        var fields = new Dictionary<string, object>
        {
            ["permanentHash"] = StringField(permanentHash),
            ["windowsHash"] = StringField(windowsHash),
            ["fullReset"] = BoolField(false),       // clear the flag
            ["windowsReset"] = BoolField(false),    // clear the flag
            ["registeredAt"] = StringField(now),
            ["lastLogin"] = StringField(now),
            ["computerName"] = StringField(Environment.MachineName),
            ["permanent"] = MapField(permanentDetails),
            ["windows"] = MapField(windowsDetails),
        };

        var url = $"{FirestoreBaseUrl}/users/{uid}/hwid/data";
        var body = JsonSerializer.Serialize(new { fields });
        var content = new StringContent(body, Encoding.UTF8, "application/json");

        var request = CreateRequest(HttpMethod.Patch, url, idToken);
        request.Content = content;
        var response = await _http.SendAsync(request).ConfigureAwait(false);

        // Log failure for debugging — Firestore rules may block the write
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            System.Diagnostics.Debug.WriteLine(
                $"[HardwareIdService] RegisterAllHardwareAsync FAILED ({response.StatusCode}): {errorBody}");
            throw new HttpRequestException(
                $"Failed to register hardware IDs (HTTP {(int)response.StatusCode}). " +
                "Please check your internet connection and try again.");
        }
    }

    /// <summary>
    /// Updates login info when the permanent hash already matches (same PC).
    /// If windowsReset was true, re-registers Windows IDs and clears the flag.
    /// Document path: users/{uid}/hwid/data
    /// Only updates specified fields using updateMask.
    /// </summary>
    private static async Task UpdateLoginInfoAsync(
        string uid, string windowsHash,
        Dictionary<string, string> windowsDetails,
        bool clearWindowsReset, string? idToken)
    {
        var now = DateTime.UtcNow.ToString("o");

        var fields = new Dictionary<string, object>
        {
            ["windowsHash"] = StringField(windowsHash),
            ["lastLogin"] = StringField(now),
            ["computerName"] = StringField(Environment.MachineName),
            ["windows"] = MapField(windowsDetails),
        };

        // Build updateMask query params to only update these fields
        var maskFields = new List<string> { "windowsHash", "lastLogin", "computerName", "windows" };

        if (clearWindowsReset)
        {
            fields["windowsReset"] = BoolField(false);
            maskFields.Add("windowsReset");
        }

        var maskParams = string.Join("&", maskFields.Select(f => $"updateMask.fieldPaths={f}"));
        var url = $"{FirestoreBaseUrl}/users/{uid}/hwid/data?{maskParams}";

        var body = JsonSerializer.Serialize(new { fields });
        var content = new StringContent(body, Encoding.UTF8, "application/json");

        var request = CreateRequest(HttpMethod.Patch, url, idToken);
        request.Content = content;
        var response = await _http.SendAsync(request).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            System.Diagnostics.Debug.WriteLine(
                $"[HardwareIdService] UpdateLoginInfoAsync FAILED ({response.StatusCode}): {errorBody}");
        }
    }

    #endregion
}
