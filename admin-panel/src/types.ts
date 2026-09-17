import { Timestamp } from "firebase/firestore";

export type UserStatus = "active" | "deactivated" | "suspended" | "banned";

export interface AppUser {
  uid: string;
  email: string;
  licensed: boolean;
  licenseCode: string;
  features: FeatureFlags;
  hwid: HwidData;
  status: UserStatus;
  deactivatedAt?: string;
  suspendedAt?: string;
  bannedAt?: string;
  bannedHwids?: string[];
  resellerName?: string;      // which reseller's key was used (empty = admin key or none)
}

export interface FeatureFlags {
  bypass: boolean;
  streamerMode: boolean;
  formBypass: boolean;
  stopNetwork: boolean;
}

export interface HwidData {
  permanentHash?: string;
  windowsHash?: string;
  computerName?: string;
  registeredAt?: string;
  lastLogin?: string;
  windowsReset?: boolean;
  fullReset?: boolean;
  lastSelfReset?: string;
}

export interface License {
  code: string;
  active: boolean;
  uid: string;
  plan: "trial" | "permanent";
  trialDays?: number;
  kernelAccess: boolean;
  createdAt: Timestamp | null;
  redeemedAt: Timestamp | null;
  expiresAt?: Timestamp | null;
  generatedBy?: string;       // reseller ID or "admin"
  generatedByName?: string;   // resolved display name (populated client-side)
}

export interface ResellerPermissions {
  // Which features the reseller can toggle on/off for users
  features: {
    bypass: boolean;
    streamerMode: boolean;
    formBypass: boolean;
    stopNetwork: boolean;
  };
  // Which user actions the reseller can perform
  actions: {
    deactivate: boolean;
    reactivate: boolean;
    suspend: boolean;
    unsuspend: boolean;
    revokeLicense: boolean;
  };
  // Which license types the reseller can generate
  licenseTypes: {
    trial: boolean;
    permanent: boolean;
    kernelAccessControl: boolean;
  };
  // Maximum trial days this reseller can assign (0 = unlimited)
  maxTrialDays: number;
  // What data the reseller can view
  visibility: {
    hwid: boolean;
    licenseCode: boolean;
    email: boolean;
    features: boolean;
  };
  // Which reset actions the reseller can perform
  resets: {
    windowsReset: boolean;
    fullAccountReset: boolean;
  };
}

export const DEFAULT_RESELLER_PERMISSIONS: ResellerPermissions = {
  features: { bypass: false, streamerMode: false, formBypass: false, stopNetwork: false },
  actions: { deactivate: false, reactivate: false, suspend: false, unsuspend: false, revokeLicense: false },
  licenseTypes: { trial: false, permanent: false, kernelAccessControl: false },
  maxTrialDays: 7,
  visibility: { hwid: false, licenseCode: false, email: true, features: false },
  resets: { windowsReset: false, fullAccountReset: false },
};

export interface Reseller {
  id: string;
  name: string;
  email: string;
  username: string;
  platform: string;
  keyLimit: number;
  keysUsed: number;
  commission: number;
  allowedPlan: string;
  pricePerKey: number;
  notes: string;
  active: boolean;
  totalRevenue: number;
  generatedCodes: string[];
  lastActivity: Timestamp | null;
  createdAt: Timestamp | null;
  addedBy: string;
  permissions: ResellerPermissions;
}

export interface ActivityLog {
  id: string;
  action: string;
  target: string;
  details: string;
  adminUid: string;
  adminEmail: string;
  timestamp: Timestamp | null;
}

export type Tab = "users" | "licenses" | "resellers" | "logs" | "site" | "announcements";

export interface SiteConfigRequirement {
  id: string;
  text: string;
  downloadUrl?: string; // optional download link for this requirement
}

export interface SiteConfigStep {
  id: string;
  text: string;
}

export interface SiteConfig {
  downloadUrl: string;
  downloadLabel: string;
  requirements: SiteConfigRequirement[];
  gettingStarted: SiteConfigStep[];
}
