import { doc, getDoc, updateDoc, setDoc, Timestamp, collection, getDocs, query, orderBy } from "firebase/firestore";
import { db, auth } from "./firebase";
import type { UserProfile, FeatureFlags, HwidData, LicenseInfo, SiteConfig, Announcement } from "./types";
import { checkRateLimit } from "./security";

/** Ensure user is authenticated — throws if not */
function requireAuth(): string {
  const uid = auth.currentUser?.uid;
  if (!uid) throw new Error("Not authenticated");
  return uid;
}

// ── Fetch current user's profile ──
export async function fetchMyProfile(): Promise<UserProfile> {
  const uid = requireAuth();

  const snap = await getDoc(doc(db, "users", uid));
  if (!snap.exists()) throw new Error("User profile not found");

  const d = snap.data();
  return {
    uid,
    email: d.email || auth.currentUser?.email || "",
    licensed: !!d.licensed,
    licenseCode: d.licenseCode || "",
    status: d.status || "active",
    deactivatedAt: d.deactivatedAt || undefined,
    suspendedAt: d.suspendedAt || undefined,
    bannedAt: d.bannedAt || undefined,
  };
}

// ── Fetch current user's feature flags ──
export async function fetchMyFeatures(): Promise<FeatureFlags> {
  const uid = requireAuth();

  const defaults: FeatureFlags = { bypass: true, streamerMode: true, formBypass: true, stopNetwork: true };

  try {
    const snap = await getDoc(doc(db, "users", uid, "features", "flags"));
    if (snap.exists()) return { ...defaults, ...snap.data() } as FeatureFlags;
  } catch {
    /* use defaults */
  }
  return defaults;
}

// ── Fetch current user's HWID data ──
export async function fetchMyHwid(): Promise<HwidData> {
  const uid = requireAuth();

  try {
    const snap = await getDoc(doc(db, "users", uid, "hwid", "data"));
    if (snap.exists()) return snap.data() as HwidData;
  } catch {
    /* none */
  }
  return {};
}

// ── Fetch license info for current user ──
export async function fetchMyLicense(code: string): Promise<LicenseInfo | null> {
  if (!code) return null;

  try {
    const snap = await getDoc(doc(db, "licenses", code));
    if (snap.exists()) {
      const d = snap.data();
      return {
        code,
        active: !!d.active,
        plan: d.plan || "permanent",
        trialDays: d.trialDays,
        createdAt: d.createdAt,
        redeemedAt: d.redeemedAt,
        expiresAt: d.expiresAt,
      };
    }
  } catch {
    /* not found */
  }
  return null;
}

// ── Redeem a license code ──
export async function redeemLicense(code: string): Promise<{ plan: string; trialDays?: number }> {
  const uid = requireAuth();

  // Rate limit redemption attempts
  const rl = checkRateLimit("redeem_license", 5, 60_000, 120_000);
  if (!rl.allowed) throw new Error("Too many attempts. Please try again later.");

  // Sanitize code
  const cleanCode = code.replace(/[^A-Z0-9-]/g, "").substring(0, 30);
  if (!cleanCode) throw new Error("Invalid license code format");

  // Check code exists & is available
  const licSnap = await getDoc(doc(db, "licenses", cleanCode));
  if (!licSnap.exists()) throw new Error("Invalid license code");

  const lic = licSnap.data();
  if (!lic.active) throw new Error("This license code has been deactivated");
  if (lic.uid && lic.uid !== "") throw new Error("This license code has already been redeemed");

  // Check user isn't already licensed
  const userSnap = await getDoc(doc(db, "users", uid));
  if (userSnap.exists() && userSnap.data().licensed) {
    throw new Error("You already have an active license");
  }

  // Redeem: update license doc
  const updateData: Record<string, unknown> = {
    uid,
    redeemedAt: Timestamp.now(),
  };

  // If trial, compute expiration
  if (lic.plan === "trial" && lic.trialDays) {
    const expiry = new Date();
    expiry.setDate(expiry.getDate() + lic.trialDays);
    updateData.expiresAt = Timestamp.fromDate(expiry);
  }

  await updateDoc(doc(db, "licenses", cleanCode), updateData);

  // Update user doc
  await updateDoc(doc(db, "users", uid), {
    licensed: true,
    licenseCode: cleanCode,
  });

  return { plan: lic.plan, trialDays: lic.trialDays };
}

// ── Timestamp display helper ──
export function tsDisplay(t: Timestamp | null | undefined): string {
  if (!t) return "—";
  if (t.toDate) return t.toDate().toLocaleString();
  return String(t);
}

export function tsDateDisplay(t: Timestamp | null | undefined): string {
  if (!t) return "—";
  if (t.toDate) return t.toDate().toLocaleDateString();
  return String(t);
}

// ── Self-service HWID Windows Reset (once per month) ──
export async function requestSelfHwidReset(): Promise<{ success: boolean; nextAvailable?: Date }> {
  const uid = requireAuth();

  // Rate limit to prevent abuse
  const rl = checkRateLimit("hwid_reset", 3, 300_000, 600_000); // 3 per 5 min, 10 min cooldown
  if (!rl.allowed) throw new Error("Too many attempts. Please try again later.");

  // Read current HWID data
  const hwidSnap = await getDoc(doc(db, "users", uid, "hwid", "data"));
  if (!hwidSnap.exists()) throw new Error("No hardware registered yet");

  const hwid = hwidSnap.data();

  // Check cooldown (30 days)
  if (hwid.lastSelfReset) {
    const lastReset = new Date(hwid.lastSelfReset);
    const nextAvailable = new Date(lastReset.getTime() + 30 * 24 * 60 * 60 * 1000);
    if (new Date() < nextAvailable) {
      return { success: false, nextAvailable };
    }
  }

  // Trigger Windows reset + record timestamp
  await setDoc(doc(db, "users", uid, "hwid", "data"), {
    windowsReset: true,
    lastSelfReset: new Date().toISOString(),
  }, { merge: true });

  return { success: true };
}

// ── Check self-reset cooldown status ──
export function getSelfResetCooldown(hwid: { lastSelfReset?: string } | null): { canReset: boolean; nextAvailable: Date | null; daysLeft: number } {
  if (!hwid?.lastSelfReset) return { canReset: true, nextAvailable: null, daysLeft: 0 };

  const lastReset = new Date(hwid.lastSelfReset);
  const nextAvailable = new Date(lastReset.getTime() + 30 * 24 * 60 * 60 * 1000);
  const now = new Date();

  if (now >= nextAvailable) return { canReset: true, nextAvailable: null, daysLeft: 0 };

  const daysLeft = Math.ceil((nextAvailable.getTime() - now.getTime()) / (1000 * 60 * 60 * 24));
  return { canReset: false, nextAvailable, daysLeft };
}

// ── Fetch site config (download page content managed by admin) ──
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

// ── Fetch announcements ──
export async function fetchAnnouncements(): Promise<Announcement[]> {
  requireAuth();
  try {
    const q = query(collection(db, "announcements"), orderBy("createdAt", "desc"));
    const snap = await getDocs(q);
    return snap.docs.map(d => ({ id: d.id, ...d.data() } as Announcement));
  } catch {
    return [];
  }
}
