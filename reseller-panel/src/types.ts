import type { Timestamp } from "firebase/firestore";

export type UserStatus = "active" | "deactivated" | "suspended" | "banned";

export interface ResellerPermissions {
  features: {
    bypass: boolean;
    streamerMode: boolean;
    formBypass: boolean;
    stopNetwork: boolean;
  };
  actions: {
    deactivate: boolean;
    reactivate: boolean;
    suspend: boolean;
    unsuspend: boolean;
    revokeLicense: boolean;
  };
  licenseTypes: {
    trial: boolean;
    permanent: boolean;
    kernelAccessControl: boolean;
  };
  maxTrialDays: number;
  visibility: {
    hwid: boolean;
    licenseCode: boolean;
    email: boolean;
    features: boolean;
  };
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

export interface ResellerProfile {
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
}

export interface License {
  code: string;
  active: boolean;
  uid: string;
  plan: "trial" | "permanent";
  trialDays?: number;
  createdAt: Timestamp | null;
  redeemedAt: Timestamp | null;
  expiresAt?: Timestamp | null;
}

export interface Announcement {
  id: string;
  title: string;
  body: string;
  type: "info" | "warning" | "update" | "urgent";
  pinned?: boolean;
  createdAt: Timestamp | null;
  createdBy?: string;
}

export type Tab = "overview" | "users" | "licenses" | "announcements";
