import {
  collection, doc, getDoc, getDocs, setDoc, updateDoc, deleteDoc, deleteField,
  addDoc, query, orderBy, limit, where, serverTimestamp, writeBatch, Timestamp
} from "firebase/firestore";
import { db, auth } from "./firebase";
import type { AppUser, License, Reseller, ActivityLog, UserStatus, ResellerPermissions, SiteConfig } from "./types";
import { DEFAULT_RESELLER_PERMISSIONS } from "./types";

// ── Helpers ──
const ts = (t: Timestamp | null | undefined): string => {
  if (!t) return "—";
  if (t.toDate) return t.toDate().toLocaleString();
  return String(t);
};

const tsDate = (t: Timestamp | null | undefined): string => {
  if (!t) return "—";
  if (t.toDate) return t.toDate().toLocaleDateString();
  return String(t);
};

function randomSegment(len: number): string {
  const chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
  let s = "";
  for (let i = 0; i < len; i++) s += chars[Math.floor(Math.random() * chars.length)];
  return s;
}

// ── Activity Log (only meaningful actions, no login/logout) ──
export async function logAction(action: string, target: string, details: string) {
  try {
    const user = auth.currentUser;
    await addDoc(collection(db, "admin_logs"), {
      action, target, details,
      adminUid: user ? user.uid : "",
      adminEmail: user ? user.email : "",
      timestamp: serverTimestamp(),
    });
  } catch {}
}

// ── Stats ──
export async function fetchStats() {
  const usersSnap = await getDocs(collection(db, "users"));
  let totalUsers = 0, licensedUsers = 0;
  usersSnap.forEach(d => { totalUsers++; if (d.data().licensed) licensedUsers++; });

  const licSnap = await getDocs(collection(db, "licenses"));
  let totalLicenses = 0, unusedLicenses = 0;
  licSnap.forEach(d => { totalLicenses++; if (d.data().active && !d.data().uid) unusedLicenses++; });

  const resSnap = await getDocs(collection(db, "resellers"));
  const totalResellers = resSnap.size;

  return { totalUsers, licensedUsers, totalLicenses, unusedLicenses, totalResellers };
}

// ══════════════════════════════════════════════════════
//  USERS
// ══════════════════════════════════════════════════════

export async function fetchUsers(): Promise<AppUser[]> {
  const snap = await getDocs(collection(db, "users"));

  // Build reseller name cache
  const resellerSnap = await getDocs(collection(db, "resellers"));
  const resellerMap = new Map<string, string>();
  resellerSnap.forEach(d => {
    const data = d.data();
    resellerMap.set(d.id, data.name || data.email || d.id);
  });

  const users: AppUser[] = [];

  for (const userDoc of snap.docs) {
    const u = userDoc.data();
    const uid = userDoc.id;

    let features = { bypass: false, streamerMode: false, formBypass: false, stopNetwork: false };
    try {
      const fs = await getDoc(doc(db, "users", uid, "features", "flags"));
      if (fs.exists()) features = { ...features, ...fs.data() };
    } catch {}

    let hwid = {};
    try {
      const hs = await getDoc(doc(db, "users", uid, "hwid", "data"));
      if (hs.exists()) hwid = hs.data();
    } catch {}

    // Resolve reseller name from the user's license code
    let resellerName = "";
    if (u.licenseCode) {
      try {
        const licSnap = await getDoc(doc(db, "licenses", u.licenseCode));
        if (licSnap.exists()) {
          const licData = licSnap.data();
          const gb = licData.generatedBy || "";
          if (gb && gb !== "admin" && resellerMap.has(gb)) {
            resellerName = resellerMap.get(gb)!;
          }
        }
      } catch {}
    }

    users.push({
      uid,
      email: u.email || "",
      licensed: !!u.licensed,
      licenseCode: u.licenseCode || "",
      features: features as any,
      hwid,
      status: (u.status as UserStatus) || "active",
      deactivatedAt: u.deactivatedAt || undefined,
      suspendedAt: u.suspendedAt || undefined,
      bannedAt: u.bannedAt || undefined,
      bannedHwids: u.bannedHwids || undefined,
      resellerName,
    });
  }
  return users;
}

export async function fetchUserDetail(uid: string) {
  const userSnap = await getDoc(doc(db, "users", uid));
  if (!userSnap.exists()) throw new Error("User not found");
  const u = userSnap.data();

  let features = { bypass: false, streamerMode: false, formBypass: false, stopNetwork: false };
  try { const fs = await getDoc(doc(db, "users", uid, "features", "flags")); if (fs.exists()) features = { ...features, ...fs.data() }; } catch {}

  let hwid = {};
  try { const hs = await getDoc(doc(db, "users", uid, "hwid", "data")); if (hs.exists()) hwid = hs.data(); } catch {}

  return {
    uid,
    email: u.email || "",
    licensed: !!u.licensed,
    licenseCode: u.licenseCode || "",
    features,
    hwid,
    status: (u.status as UserStatus) || "active",
    deactivatedAt: u.deactivatedAt || undefined,
    suspendedAt: u.suspendedAt || undefined,
    bannedAt: u.bannedAt || undefined,
    bannedHwids: u.bannedHwids || undefined,
  } as AppUser;
}

export async function toggleFeature(uid: string, key: string, value: boolean) {
  await setDoc(doc(db, "users", uid, "features", "flags"), { [key]: value }, { merge: true });
  await logAction("toggle_feature", uid, `${key} = ${value}`);
}

export async function setAllFeatures(uid: string, enabled: boolean) {
  await setDoc(doc(db, "users", uid, "features", "flags"), {
    bypass: enabled, streamerMode: enabled, formBypass: enabled, stopNetwork: enabled,
  });
  await logAction("set_all_features", uid, `all = ${enabled}`);
}

export async function triggerHwidReset(uid: string, type: "windows" | "full") {
  const field = type === "full" ? { fullReset: true } : { windowsReset: true };
  await setDoc(doc(db, "users", uid, "hwid", "data"), field, { merge: true });
  await logAction("hwid_reset", uid, `type = ${type}`);
}

export async function wipeHwid(uid: string) {
  await deleteDoc(doc(db, "users", uid, "hwid", "data"));
  await logAction("hwid_wipe", uid, "Complete HWID wipe");
}

// ── Clear Self-Reset Cooldown (admin removes the 30-day cooldown for a user) ──
export async function clearResetCooldown(uid: string, email: string) {
  await updateDoc(doc(db, "users", uid, "hwid", "data"), {
    lastSelfReset: deleteField(),
  });
  await logAction("clear_reset_cooldown", uid, `Cleared self-reset cooldown for: ${email}`);
}

// ── Full Account Reset (wipes HWID, features, license — complete fresh start) ──
export async function fullAccountReset(uid: string, email: string) {
  // 1. Revoke license if any
  const userSnap = await getDoc(doc(db, "users", uid));
  const userData = userSnap.data();
  if (userData?.licenseCode) {
    try {
      await updateDoc(doc(db, "licenses", userData.licenseCode), { uid: "", redeemedAt: null });
    } catch {}
  }

  // 2. Wipe HWID completely
  try { await deleteDoc(doc(db, "users", uid, "hwid", "data")); } catch {}

  // 3. Reset all features to disabled
  await setDoc(doc(db, "users", uid, "features", "flags"), {
    bypass: false, streamerMode: false, formBypass: false, stopNetwork: false,
  });

  // 4. Reset user document to clean state
  await updateDoc(doc(db, "users", uid), {
    licensed: false,
    licenseCode: "",
  });

  await logAction("full_account_reset", uid, `Full account reset for: ${email}`);
}

export async function revokeLicense(uid: string, email: string) {
  const userSnap = await getDoc(doc(db, "users", uid));
  const userData = userSnap.data();
  if (userData?.licenseCode) {
    await updateDoc(doc(db, "licenses", userData.licenseCode), { uid: "", redeemedAt: null });
  }
  await updateDoc(doc(db, "users", uid), { licensed: false, licenseCode: "" });
  await logAction("revoke_license", uid, `user: ${email}`);
}

// ── Deactivate user (soft disable — can be reactivated) ──
export async function deactivateUser(uid: string, email: string) {
  await updateDoc(doc(db, "users", uid), {
    status: "deactivated",
    deactivatedAt: new Date().toISOString(),
  });
  // Disable all features
  await setDoc(doc(db, "users", uid, "features", "flags"), {
    bypass: false, streamerMode: false, formBypass: false, stopNetwork: false,
  });
  await logAction("deactivate_user", uid, `Deactivated user: ${email}`);
}

// ── Reactivate a deactivated user ──
export async function reactivateUser(uid: string, email: string) {
  await updateDoc(doc(db, "users", uid), {
    status: "active",
    deactivatedAt: "",
  });
  // Restore default features (disabled by default — admin enables individually)
  await setDoc(doc(db, "users", uid, "features", "flags"), {
    bypass: false, streamerMode: false, formBypass: false, stopNetwork: false,
  });
  await logAction("reactivate_user", uid, `Reactivated user: ${email}`);
}

// ── Suspend user permanently (cannot use software, reversible by admin) ──
export async function suspendUser(uid: string, email: string) {
  // Revoke license first
  const userSnap = await getDoc(doc(db, "users", uid));
  const userData = userSnap.data();
  if (userData?.licenseCode) {
    try {
      await updateDoc(doc(db, "licenses", userData.licenseCode), { uid: "", redeemedAt: null });
    } catch {}
  }
  await updateDoc(doc(db, "users", uid), {
    status: "suspended",
    suspendedAt: new Date().toISOString(),
    licensed: false,
    licenseCode: "",
  });
  // Disable all features
  await setDoc(doc(db, "users", uid, "features", "flags"), {
    bypass: false, streamerMode: false, formBypass: false, stopNetwork: false,
  });
  await logAction("suspend_user", uid, `Permanently suspended user: ${email}`);
}

// ── Unsuspend user ──
export async function unsuspendUser(uid: string, email: string) {
  await updateDoc(doc(db, "users", uid), {
    status: "active",
    suspendedAt: "",
  });
  await setDoc(doc(db, "users", uid, "features", "flags"), {
    bypass: false, streamerMode: false, formBypass: false, stopNetwork: false,
  });
  await logAction("unsuspend_user", uid, `Unsuspended user: ${email}`);
}

// ── System Ban (hardware ban — stores HWID hashes in banned_hwids collection) ──
export async function systemBanUser(uid: string, email: string) {
  // Grab the HWID hashes before banning
  const bannedHwids: string[] = [];
  try {
    const hwidSnap = await getDoc(doc(db, "users", uid, "hwid", "data"));
    if (hwidSnap.exists()) {
      const h = hwidSnap.data();
      if (h.permanentHash) bannedHwids.push(h.permanentHash);
      if (h.windowsHash) bannedHwids.push(h.windowsHash);
    }
  } catch {}

  // Revoke license
  const userSnap = await getDoc(doc(db, "users", uid));
  const userData = userSnap.data();
  if (userData?.licenseCode) {
    try {
      await updateDoc(doc(db, "licenses", userData.licenseCode), { uid: "", redeemedAt: null });
    } catch {}
  }

  // Update user doc
  await updateDoc(doc(db, "users", uid), {
    status: "banned",
    bannedAt: new Date().toISOString(),
    bannedHwids,
    licensed: false,
    licenseCode: "",
  });

  // Disable all features
  await setDoc(doc(db, "users", uid, "features", "flags"), {
    bypass: false, streamerMode: false, formBypass: false, stopNetwork: false,
  });

  // Store each HWID hash in the banned_hwids collection for the WPF app to check
  for (const hash of bannedHwids) {
    await setDoc(doc(db, "banned_hwids", hash), {
      uid,
      email,
      bannedAt: new Date().toISOString(),
      reason: "system_ban",
    });
  }

  await logAction("system_ban", uid, `System banned user: ${email} — ${bannedHwids.length} HWID(s) blocked`);
}

// ── Unban user (removes hardware ban) ──
export async function unbanUser(uid: string, email: string) {
  // Remove HWID hashes from banned_hwids collection
  const userSnap = await getDoc(doc(db, "users", uid));
  const userData = userSnap.data();
  const hashes = userData?.bannedHwids || [];
  for (const hash of hashes) {
    try { await deleteDoc(doc(db, "banned_hwids", hash)); } catch {}
  }

  await updateDoc(doc(db, "users", uid), {
    status: "active",
    bannedAt: "",
    bannedHwids: [],
  });

  await setDoc(doc(db, "users", uid, "features", "flags"), {
    bypass: true, streamerMode: true, formBypass: true, stopNetwork: true,
  });

  await logAction("unban_user", uid, `Unbanned user: ${email} — ${hashes.length} HWID(s) unblocked`);
}

// ── Delete user completely ──
export async function deleteUser(uid: string, email: string) {
  // Revoke license first
  const userSnap = await getDoc(doc(db, "users", uid));
  const userData = userSnap.data();
  if (userData?.licenseCode) {
    try {
      await updateDoc(doc(db, "licenses", userData.licenseCode), { uid: "", redeemedAt: null });
    } catch {}
  }

  // Remove banned HWID entries if any
  const hashes = userData?.bannedHwids || [];
  for (const hash of hashes) {
    try { await deleteDoc(doc(db, "banned_hwids", hash)); } catch {}
  }

  // Delete subcollections
  try { await deleteDoc(doc(db, "users", uid, "features", "flags")); } catch {}
  try { await deleteDoc(doc(db, "users", uid, "hwid", "data")); } catch {}

  // Delete user document
  await deleteDoc(doc(db, "users", uid));
  await logAction("delete_user", uid, `Deleted user: ${email}`);
}

// ══════════════════════════════════════════════════════
//  LICENSES (Trial + Permanent only)
// ══════════════════════════════════════════════════════

export async function fetchLicenses(): Promise<License[]> {
  const snap = await getDocs(collection(db, "licenses"));

  // Build a cache of reseller names for generatedBy lookups
  const resellerSnap = await getDocs(collection(db, "resellers"));
  const resellerMap = new Map<string, string>();
  resellerSnap.forEach(d => {
    const data = d.data();
    resellerMap.set(d.id, data.name || data.email || d.id);
  });

  const list: License[] = [];
  snap.forEach(d => {
    const l = d.data();
    const generatedBy = (l.generatedBy as string) || "";
    let generatedByName = "";
    if (generatedBy === "admin") {
      generatedByName = "Admin";
    } else if (generatedBy && resellerMap.has(generatedBy)) {
      generatedByName = resellerMap.get(generatedBy)!;
    } else if (generatedBy) {
      generatedByName = generatedBy.substring(0, 8) + "…";
    } else {
      generatedByName = "Admin";
    }
    list.push({ ...l, code: d.id, generatedBy, generatedByName, kernelAccess: l.kernelAccess !== false } as License);
  });
  return list;
}

export async function generateLicenses(count: number, plan: "trial" | "permanent", prefix: string, trialDays?: number, kernelAccess: boolean = true): Promise<string[]> {
  const codes: string[] = [];
  const batch = writeBatch(db);

  for (let i = 0; i < count; i++) {
    const code = `${prefix}-${randomSegment(4)}-${randomSegment(4)}-${randomSegment(4)}`;
    codes.push(code);
    const data: any = { active: true, uid: "", plan, createdAt: serverTimestamp(), redeemedAt: null, generatedBy: "admin", kernelAccess };
    if (plan === "trial" && trialDays) data.trialDays = trialDays;
    batch.set(doc(db, "licenses", code), data);
  }

  await batch.commit();
  await logAction("generate_licenses", "", `${count} ${plan}${plan === "trial" ? ` (${trialDays}d)` : ""} codes | kernel=${kernelAccess}`);
  return codes;
}

export async function toggleKernelAccess(code: string, value: boolean) {
  await updateDoc(doc(db, "licenses", code), { kernelAccess: value });
  await logAction("toggle_kernel_access", code, `kernelAccess=${value}`);
}

export async function deactivateLicense(code: string) {
  const licSnap = await getDoc(doc(db, "licenses", code));
  const ld = licSnap.data();
  if (ld?.uid) {
    await updateDoc(doc(db, "users", ld.uid), { licensed: false, licenseCode: "" });
  }
  await updateDoc(doc(db, "licenses", code), { active: false });
  await logAction("deactivate_license", code, "");
}

export async function reactivateLicense(code: string) {
  await updateDoc(doc(db, "licenses", code), { active: true });
  await logAction("activate_license", code, "");
}

export async function revokeCodeClaim(code: string) {
  const licSnap = await getDoc(doc(db, "licenses", code));
  const ld = licSnap.data();
  if (ld?.uid) {
    await updateDoc(doc(db, "users", ld.uid), { licensed: false, licenseCode: "" });
  }
  await updateDoc(doc(db, "licenses", code), { uid: "", redeemedAt: null });
  await logAction("revoke_code_claim", code, "");
}

export async function deleteLicense(code: string) {
  const licSnap = await getDoc(doc(db, "licenses", code));
  const ld = licSnap.data();
  if (ld?.uid) {
    await updateDoc(doc(db, "users", ld.uid), { licensed: false, licenseCode: "" });
  }
  await deleteDoc(doc(db, "licenses", code));
  await logAction("delete_license", code, "");
}

// ══════════════════════════════════════════════════════
//  RESELLERS
// ══════════════════════════════════════════════════════

export async function fetchResellers(): Promise<Reseller[]> {
  const snap = await getDocs(collection(db, "resellers"));
  const list: Reseller[] = [];
  snap.forEach(d => {
    const data = d.data();
    list.push({
      ...data,
      id: d.id,
      permissions: data.permissions
        ? { ...DEFAULT_RESELLER_PERMISSIONS, ...data.permissions }
        : DEFAULT_RESELLER_PERMISSIONS,
    } as Reseller);
  });
  return list;
}

export async function addReseller(data: {
  name: string; email: string; username: string; platform: string;
  keyLimit: number; commission: number; allowedPlan: string; notes: string;
  permissions?: ResellerPermissions;
}) {
  await addDoc(collection(db, "resellers"), {
    ...data,
    email: data.email.toLowerCase(),
    keysUsed: 0,
    pricePerKey: 0,
    active: true,
    totalRevenue: 0,
    generatedCodes: [],
    lastActivity: null,
    createdAt: serverTimestamp(),
    addedBy: auth.currentUser?.email || "",
    permissions: data.permissions || DEFAULT_RESELLER_PERMISSIONS,
  });
  await logAction("add_reseller", data.email, `${data.name} (${data.username} on ${data.platform})`);
}

export async function toggleResellerStatus(id: string, currentlyActive: boolean) {
  await updateDoc(doc(db, "resellers", id), { active: !currentlyActive });
  await logAction(currentlyActive ? "suspend_reseller" : "activate_reseller", id, "");
}

export async function updateResellerKeyLimit(id: string, newLimit: number) {
  await updateDoc(doc(db, "resellers", id), { keyLimit: newLimit });
  await logAction("update_reseller_limit", id, `New limit: ${newLimit}`);
}

export async function addKeysToReseller(id: string, extra: number) {
  const snap = await getDoc(doc(db, "resellers", id));
  const current = snap.data()?.keyLimit || 0;
  await updateDoc(doc(db, "resellers", id), { keyLimit: current + extra });
  await logAction("add_reseller_keys", id, `+${extra} keys`);
}

export async function resetResellerKeysUsed(id: string) {
  await updateDoc(doc(db, "resellers", id), { keysUsed: 0 });
  await logAction("reset_reseller_keys", id, "Counter reset to 0");
}

export async function updateResellerNotes(id: string, notes: string) {
  await updateDoc(doc(db, "resellers", id), { notes });
}

export async function updateResellerPermissions(id: string, permissions: ResellerPermissions, name: string) {
  await updateDoc(doc(db, "resellers", id), { permissions });
  await logAction("update_reseller_permissions", id, `Updated permissions for: ${name}`);
}

export async function deleteReseller(id: string, name: string) {
  await deleteDoc(doc(db, "resellers", id));
  await logAction("delete_reseller", id, `Deleted: ${name}`);
}

// ══════════════════════════════════════════════════════
//  ACTIVITY LOGS (no login/logout — only meaningful actions)
// ══════════════════════════════════════════════════════

const HIDDEN_ACTIONS = ["login", "logout"];

export async function fetchLogs(): Promise<ActivityLog[]> {
  const snap = await getDocs(query(collection(db, "admin_logs"), orderBy("timestamp", "desc"), limit(200)));
  const list: ActivityLog[] = [];
  snap.forEach(d => {
    const data = d.data();
    if (!HIDDEN_ACTIONS.includes(data.action)) {
      list.push({ ...data, id: d.id } as ActivityLog);
    }
  });
  return list;
}

// ── Fetch activity logs for a specific user (filtered by target UID) ──
export async function fetchUserLogs(uid: string): Promise<ActivityLog[]> {
  const snap = await getDocs(
    query(collection(db, "admin_logs"), where("target", "==", uid), orderBy("timestamp", "desc"), limit(50))
  );
  const list: ActivityLog[] = [];
  snap.forEach(d => {
    const data = d.data();
    if (!HIDDEN_ACTIONS.includes(data.action)) {
      list.push({ ...data, id: d.id } as ActivityLog);
    }
  });
  return list;
}

export { ts, tsDate };

// ══════════════════════════════════════════════════════
//  FORCE LOGOUT ALL USERS
// ══════════════════════════════════════════════════════

/**
 * Writes a timestamp to system_config/force_logout in Firestore.
 * The WPF app's session monitor checks this document every 90 seconds.
 * If the timestamp is newer than the user's login time → force logout.
 */
export async function forceLogoutAllUsers() {
  await setDoc(doc(db, "system_config", "force_logout"), {
    triggeredAt: new Date().toISOString(),
    triggeredBy: auth.currentUser?.email || "",
    timestamp: serverTimestamp(),
  });
  await logAction("force_logout_all", "all_users", "Force logged out all software users");
}

// ══════════════════════════════════════════════════════
//  FORCE LOGOUT SINGLE USER
// ══════════════════════════════════════════════════════

/**
 * Writes a forceLogoutAt timestamp to the user document.
 * The WPF session monitor checks this field every 90 seconds.
 * If forceLogoutAt is newer than the user's login time → force logout.
 */
export async function forceLogoutUser(uid: string, email: string) {
  await updateDoc(doc(db, "users", uid), {
    forceLogoutAt: new Date().toISOString(),
  });
  await logAction("force_logout_user", uid, `Force logged out user ${email}`);
}

// ══════════════════════════════════════════════════════
//  MAINTENANCE MODE
// ══════════════════════════════════════════════════════

export async function getMaintenanceMode(): Promise<{ enabled: boolean; message: string }> {
  try {
    const snap = await getDoc(doc(db, "system_config", "maintenance"));
    if (snap.exists()) {
      const data = snap.data();
      return {
        enabled: data.enabled === true,
        message: data.message || "LegitX V2 is currently under maintenance. Please try again later.",
      };
    }
  } catch {}
  return { enabled: false, message: "" };
}

export async function setMaintenanceMode(enabled: boolean, message: string) {
  await setDoc(doc(db, "system_config", "maintenance"), {
    enabled,
    message,
    updatedAt: serverTimestamp(),
    updatedBy: auth.currentUser?.email || "",
  });
  await logAction(
    enabled ? "maintenance_on" : "maintenance_off",
    "system",
    enabled ? `Maintenance mode enabled: ${message}` : "Maintenance mode disabled"
  );
}

// ══════════════════════════════════════════════════════
//  SITE CONFIG (Download page content)
// ══════════════════════════════════════════════════════

const DEFAULT_SITE_CONFIG: SiteConfig = {
  downloadUrl: "",
  downloadLabel: "Download LegitX V2",
  requirements: [
    { id: "r1", text: "Windows 10 / 11 (64-bit)" },
    { id: "r2", text: ".NET 8.0 Runtime", downloadUrl: "https://dotnet.microsoft.com/en-us/download/dotnet/8.0" },
    { id: "r3", text: "Active license required" },
  ],
  gettingStarted: [
    { id: "s1", text: "Download the latest release" },
    { id: "s2", text: "Extract the ZIP file" },
    { id: "s3", text: "Run LegitX V2.exe" },
    { id: "s4", text: "Sign in with your account" },
  ],
};

export async function fetchSiteConfig(): Promise<SiteConfig> {
  try {
    const snap = await getDoc(doc(db, "site_config", "download"));
    if (snap.exists()) {
      const data = snap.data();
      return {
        downloadUrl: data.downloadUrl || "",
        downloadLabel: data.downloadLabel || "Download LegitX V2",
        requirements: data.requirements || DEFAULT_SITE_CONFIG.requirements,
        gettingStarted: data.gettingStarted || DEFAULT_SITE_CONFIG.gettingStarted,
      };
    }
  } catch {}
  return { ...DEFAULT_SITE_CONFIG };
}

export async function saveSiteConfig(config: SiteConfig): Promise<void> {
  await setDoc(doc(db, "site_config", "download"), {
    downloadUrl: config.downloadUrl,
    downloadLabel: config.downloadLabel,
    requirements: config.requirements,
    gettingStarted: config.gettingStarted,
    updatedAt: serverTimestamp(),
    updatedBy: auth.currentUser?.email || "",
  });
  await logAction("update_site_config", "download", "Updated download page config");
}

// ══════════════════════════════════════════════════════
//  ANNOUNCEMENTS
// ══════════════════════════════════════════════════════

export interface AnnouncementData {
  id?: string;
  title: string;
  body: string;
  type: "info" | "warning" | "update" | "urgent";
  pinned: boolean;
  createdAt: string;
  createdBy: string;
}

export async function fetchAnnouncements(): Promise<AnnouncementData[]> {
  const q = query(collection(db, "announcements"), orderBy("createdAt", "desc"));
  const snap = await getDocs(q);
  return snap.docs.map(d => ({ id: d.id, ...d.data() } as AnnouncementData));
}

export async function createAnnouncement(data: Omit<AnnouncementData, "id" | "createdAt" | "createdBy">): Promise<void> {
  await addDoc(collection(db, "announcements"), {
    title: data.title,
    body: data.body,
    type: data.type,
    pinned: data.pinned,
    createdAt: new Date().toISOString(),
    createdBy: auth.currentUser?.email || "",
  });
  await logAction("create_announcement", data.title, `Type: ${data.type}`);
}

export async function deleteAnnouncement(id: string): Promise<void> {
  await deleteDoc(doc(db, "announcements", id));
  await logAction("delete_announcement", id, "Deleted announcement");
}

export async function updateAnnouncement(id: string, data: Partial<AnnouncementData>): Promise<void> {
  const { id: _id, ...rest } = data;
  await updateDoc(doc(db, "announcements", id), rest);
  await logAction("update_announcement", id, `Updated: ${data.title || ""}`);
}

