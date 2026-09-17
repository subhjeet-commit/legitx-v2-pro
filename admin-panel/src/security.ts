/**
 * LegitX V2 — Admin Panel Security Module
 *
 * Provides: rate limiting, input sanitization, session management,
 * secure error handling, and admin-specific protections.
 */

// ══════════════════════════════════════════════════════
//  RATE LIMITER
// ══════════════════════════════════════════════════════

interface RateLimitEntry {
  count: number;
  firstAttempt: number;
  blockedUntil: number;
}

const rateLimitStore = new Map<string, RateLimitEntry>();

export function checkRateLimit(
  key: string,
  maxAttempts: number = 5,
  windowMs: number = 60_000,
  cooldownMs: number = 60_000,
): { allowed: boolean; retryAfterMs: number } {
  const now = Date.now();
  const entry = rateLimitStore.get(key);

  if (entry) {
    if (entry.blockedUntil > now) {
      return { allowed: false, retryAfterMs: entry.blockedUntil - now };
    }
    if (now - entry.firstAttempt > windowMs) {
      rateLimitStore.set(key, { count: 1, firstAttempt: now, blockedUntil: 0 });
      return { allowed: true, retryAfterMs: 0 };
    }
    entry.count++;
    if (entry.count > maxAttempts) {
      entry.blockedUntil = now + cooldownMs;
      return { allowed: false, retryAfterMs: cooldownMs };
    }
    return { allowed: true, retryAfterMs: 0 };
  }

  rateLimitStore.set(key, { count: 1, firstAttempt: now, blockedUntil: 0 });
  return { allowed: true, retryAfterMs: 0 };
}

export function resetRateLimit(key: string) {
  rateLimitStore.delete(key);
}

// ══════════════════════════════════════════════════════
//  INPUT SANITIZATION
// ══════════════════════════════════════════════════════

export function sanitizeInput(input: string): string {
  return input
    .replace(/<[^>]*>/g, "")
    .replace(/[<>"'`;(){}]/g, "")
    .trim();
}

export function isValidEmail(email: string): boolean {
  const re = /^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$/;
  return re.test(email) && email.length <= 254;
}

// ══════════════════════════════════════════════════════
//  SESSION SECURITY
// ══════════════════════════════════════════════════════

const SESSION_TIMEOUT_MS = 2 * 60 * 60 * 1000; // 2 hours for admin (stricter)
const SESSION_KEY = "legitx_admin_session_start";
const ACTIVITY_KEY = "legitx_admin_last_activity";
const IDLE_TIMEOUT_MS = 30 * 60 * 1000; // 30 min idle = auto-logout

export function markSessionStart() {
  sessionStorage.setItem(SESSION_KEY, Date.now().toString());
  updateActivity();
}

export function updateActivity() {
  sessionStorage.setItem(ACTIVITY_KEY, Date.now().toString());
}

export function isSessionExpired(): boolean {
  const start = sessionStorage.getItem(SESSION_KEY);
  if (!start) return false;
  // Absolute timeout
  if (Date.now() - parseInt(start, 10) > SESSION_TIMEOUT_MS) return true;
  // Idle timeout
  const lastActive = sessionStorage.getItem(ACTIVITY_KEY);
  if (lastActive && Date.now() - parseInt(lastActive, 10) > IDLE_TIMEOUT_MS) return true;
  return false;
}

export function clearSession() {
  sessionStorage.removeItem(SESSION_KEY);
  sessionStorage.removeItem(ACTIVITY_KEY);
}

// ══════════════════════════════════════════════════════
//  SECURE ERROR HANDLING
// ══════════════════════════════════════════════════════

export function safeErrorMessage(error: unknown): string {
  const msg = error instanceof Error ? error.message : String(error);

  if (msg.includes("permission-denied")) return "Access denied — insufficient permissions";
  if (msg.includes("not-found")) return "Resource not found";
  if (msg.includes("already-exists")) return "Resource already exists";
  if (msg.includes("auth/popup-closed")) return "Sign-in popup was closed";
  if (msg.includes("auth/network-request-failed")) return "Network error. Check your connection.";
  if (msg.includes("too-many-requests")) return "Too many requests. Slow down.";
  if (msg.includes("Firebase") || msg.includes("firestore") || msg.includes("auth/"))
    return "An error occurred. Please try again.";
  if (msg.length < 100 && !msg.includes("Error:")) return msg;
  return "An unexpected error occurred.";
}

// ══════════════════════════════════════════════════════
//  ANTI-TAMPER
// ══════════════════════════════════════════════════════

export function initAntiTamper() {
  if (import.meta.env.PROD) {
    try {
      Object.freeze(window.fetch);
      Object.freeze(window.XMLHttpRequest);
    } catch {}
  }
}

// ══════════════════════════════════════════════════════
//  OPERATION CONFIRMATION — double-check destructive ops
// ══════════════════════════════════════════════════════

/** Generate a unique confirmation token for destructive operations */
export function generateConfirmToken(): string {
  const array = new Uint8Array(16);
  crypto.getRandomValues(array);
  return Array.from(array, b => b.toString(16).padStart(2, "0")).join("");
}
