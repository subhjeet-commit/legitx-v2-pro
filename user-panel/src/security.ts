/**
 * LegitX V2 — User Panel Security Module
 *
 * Provides: rate limiting, input sanitization, session management,
 * anti-tamper detection, and secure error handling.
 */

// ══════════════════════════════════════════════════════
//  RATE LIMITER — prevents brute-force on sensitive ops
// ══════════════════════════════════════════════════════

interface RateLimitEntry {
  count: number;
  firstAttempt: number;
  blockedUntil: number;
}

const rateLimitStore = new Map<string, RateLimitEntry>();

/**
 * Check if an action is rate-limited.
 * @param key     Unique key for the action (e.g. "login", "redeem", "hwid_reset")
 * @param maxAttempts  Max attempts allowed within the window
 * @param windowMs     Time window in milliseconds
 * @param cooldownMs   Cooldown period after being blocked
 * @returns { allowed, retryAfterMs }
 */
export function checkRateLimit(
  key: string,
  maxAttempts: number = 5,
  windowMs: number = 60_000,
  cooldownMs: number = 60_000,
): { allowed: boolean; retryAfterMs: number } {
  const now = Date.now();
  const entry = rateLimitStore.get(key);

  if (entry) {
    // Currently blocked?
    if (entry.blockedUntil > now) {
      return { allowed: false, retryAfterMs: entry.blockedUntil - now };
    }
    // Window expired? Reset.
    if (now - entry.firstAttempt > windowMs) {
      rateLimitStore.set(key, { count: 1, firstAttempt: now, blockedUntil: 0 });
      return { allowed: true, retryAfterMs: 0 };
    }
    // Within window
    entry.count++;
    if (entry.count > maxAttempts) {
      entry.blockedUntil = now + cooldownMs;
      return { allowed: false, retryAfterMs: cooldownMs };
    }
    return { allowed: true, retryAfterMs: 0 };
  }

  // First attempt
  rateLimitStore.set(key, { count: 1, firstAttempt: now, blockedUntil: 0 });
  return { allowed: true, retryAfterMs: 0 };
}

/** Reset rate limit for a key (e.g. after successful login) */
export function resetRateLimit(key: string) {
  rateLimitStore.delete(key);
}

// ══════════════════════════════════════════════════════
//  INPUT SANITIZATION
// ══════════════════════════════════════════════════════

/** Strip HTML tags and dangerous characters from input */
export function sanitizeInput(input: string): string {
  return input
    .replace(/<[^>]*>/g, "")           // strip HTML tags
    .replace(/[<>"'`;(){}]/g, "")      // remove dangerous chars
    .trim();
}

/** Validate email format strictly */
export function isValidEmail(email: string): boolean {
  const re = /^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$/;
  return re.test(email) && email.length <= 254;
}

/** Validate license code format (XXXXX-XXXX-XXXX-XXXX) */
export function isValidLicenseCode(code: string): boolean {
  // Allow letters, numbers, and hyphens only — 10-30 chars
  return /^[A-Z0-9-]{10,30}$/.test(code);
}

/** Validate password strength */
export function validatePassword(pw: string): { valid: boolean; reason?: string } {
  if (pw.length < 8) return { valid: false, reason: "Password must be at least 8 characters" };
  if (pw.length > 128) return { valid: false, reason: "Password too long" };
  if (!/[A-Z]/.test(pw)) return { valid: false, reason: "Needs at least 1 uppercase letter" };
  if (!/[0-9]/.test(pw)) return { valid: false, reason: "Needs at least 1 number" };
  if (!/[^A-Za-z0-9]/.test(pw)) return { valid: false, reason: "Needs at least 1 special character" };
  return { valid: true };
}

// ══════════════════════════════════════════════════════
//  SESSION SECURITY
// ══════════════════════════════════════════════════════

const SESSION_TIMEOUT_MS = 4 * 60 * 60 * 1000; // 4 hours
const SESSION_KEY = "legitx_session_start";

/** Mark the session start time */
export function markSessionStart() {
  sessionStorage.setItem(SESSION_KEY, Date.now().toString());
}

/** Check if the session has expired */
export function isSessionExpired(): boolean {
  const start = sessionStorage.getItem(SESSION_KEY);
  if (!start) return false; // No session yet
  return Date.now() - parseInt(start, 10) > SESSION_TIMEOUT_MS;
}

/** Clear the session */
export function clearSession() {
  sessionStorage.removeItem(SESSION_KEY);
  // Clear any cached sensitive data
  localStorage.removeItem("legitx_signin_email");
}

// ══════════════════════════════════════════════════════
//  SECURE ERROR HANDLING — never leak internal details
// ══════════════════════════════════════════════════════

/** Map Firebase/internal errors to user-safe messages */
export function safeErrorMessage(error: unknown): string {
  const msg = error instanceof Error ? error.message : String(error);

  // Firebase auth errors
  if (msg.includes("auth/wrong-password") || msg.includes("auth/invalid-credential"))
    return "Incorrect email or password";
  if (msg.includes("auth/user-not-found"))
    return "No account found with this email";
  if (msg.includes("auth/email-already-in-use"))
    return "An account with this email already exists";
  if (msg.includes("auth/too-many-requests"))
    return "Too many attempts. Please try again later.";
  if (msg.includes("auth/popup-closed"))
    return "Sign-in popup was closed";
  if (msg.includes("auth/network-request-failed"))
    return "Network error. Check your connection.";
  if (msg.includes("auth/invalid-email"))
    return "Please enter a valid email address";
  if (msg.includes("auth/weak-password"))
    return "Password is too weak";
  if (msg.includes("permission-denied"))
    return "Access denied";
  if (msg.includes("not-found"))
    return "The requested resource was not found";

  // Generic — never expose raw Firebase internals
  if (msg.includes("Firebase") || msg.includes("firestore") || msg.includes("auth/"))
    return "An error occurred. Please try again.";

  // If it's one of our own errors, pass it through
  if (msg.length < 100 && !msg.includes("Error:")) return msg;

  return "An unexpected error occurred. Please try again.";
}

// ══════════════════════════════════════════════════════
//  ANTI-TAMPER — detect console/DOM manipulation
// ══════════════════════════════════════════════════════

/** Freeze critical globals to prevent tampering */
export function initAntiTamper() {
  // Prevent overriding fetch/XMLHttpRequest in production
  if (import.meta.env.PROD) {
    try {
      Object.freeze(window.fetch);
      Object.freeze(window.XMLHttpRequest);
    } catch { /* some browsers block this */ }
  }
}

// ══════════════════════════════════════════════════════
//  CLIPBOARD PROTECTION — disable copy on sensitive fields
// ══════════════════════════════════════════════════════

/** Prevent copying the content of an element */
export function preventCopy(e: React.ClipboardEvent) {
  e.preventDefault();
}

// ══════════════════════════════════════════════════════
//  NONCE GENERATOR — for CSP and request integrity
// ══════════════════════════════════════════════════════

/** Generate a cryptographically secure random hex string */
export function generateNonce(length: number = 32): string {
  const array = new Uint8Array(length);
  crypto.getRandomValues(array);
  return Array.from(array, b => b.toString(16).padStart(2, "0")).join("");
}
