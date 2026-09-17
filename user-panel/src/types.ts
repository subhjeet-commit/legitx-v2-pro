import type { Timestamp } from "firebase/firestore";

export interface UserProfile {
  uid: string;
  email: string;
  licensed: boolean;
  licenseCode: string;
  status: "active" | "deactivated" | "suspended" | "banned";
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
  lastSelfReset?: string;
}

export interface LicenseInfo {
  code: string;
  active: boolean;
  plan: "trial" | "permanent";
  trialDays?: number;
  createdAt: Timestamp | null;
  redeemedAt: Timestamp | null;
  expiresAt?: Timestamp | null;
}

export interface SiteConfigRequirement {
  id: string;
  text: string;
  downloadUrl?: string;
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

export interface Announcement {
  id: string;
  title: string;
  body: string;
  type: "info" | "warning" | "update" | "urgent";
  createdAt: string;
  pinned?: boolean;
}
