import { useState } from "react";
import {
  signInWithPopup,
  signInWithEmailAndPassword,
  createUserWithEmailAndPassword,
  sendEmailVerification,
  sendPasswordResetEmail,
} from "firebase/auth";
import { doc, setDoc, getDoc } from "firebase/firestore";
import { auth, googleProvider, db, actionCodeSettings, isFirebaseConfigured } from "../firebase";
import {
  Shield, LogIn, UserPlus, Key, ChevronRight, Eye, EyeOff, Sparkles,
  Mail, ArrowLeft, CheckCircle, AlertTriangle, RefreshCw,
} from "lucide-react";
import {
  checkRateLimit, resetRateLimit, sanitizeInput,
  isValidEmail, isValidLicenseCode, validatePassword, safeErrorMessage,
} from "../security";

interface Props {
  onToast: (msg: string, type: "success" | "error" | "info") => void;
}

type Mode = "login" | "register" | "redeem" | "forgotPassword";
type FlowState =
  | "idle"
  | "verificationSent"     // Registration: email verification sent
  | "resetSent";           // Password reset email sent

export function LoginScreen({ onToast }: Props) {
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [showPassword, setShowPassword] = useState(false);
  const [mode, setMode] = useState<Mode>("login");
  const [loading, setLoading] = useState(false);
  const [flowState, setFlowState] = useState<FlowState>("idle");
  const [resetEmail, setResetEmail] = useState("");

  // Redeem-first state
  const [redeemCode, setRedeemCode] = useState("");
  const [redeemValid, setRedeemValid] = useState<null | boolean>(null);
  const [redeemPlan, setRedeemPlan] = useState("");
  const [redeemChecking, setRedeemChecking] = useState(false);

  const ensureUserDoc = async (uid: string, userEmail: string) => {
    const ref = doc(db, "users", uid);
    const snap = await getDoc(ref);
    if (!snap.exists()) {
      await setDoc(ref, {
        email: userEmail,
        licensed: false,
        licenseCode: "",
        status: "active",
      });
    }
  };

  const handleGoogle = async () => {
    if (!isFirebaseConfigured) {
      onToast("Firebase is not configured. Set the VITE_FIREBASE_* variables before signing in.", "error");
      return;
    }

    setLoading(true);
    try {
      const result = await signInWithPopup(auth, googleProvider);
      await ensureUserDoc(result.user.uid, result.user.email || "");
      resetRateLimit("google_login");
      onToast("Welcome back!", "success");
    } catch (e: unknown) {
      const msg = safeErrorMessage(e);
      if (!msg.includes("popup")) onToast(msg, "error");
    }
    setLoading(false);
  };

  // ── Register: Create account + send verification email ──
  const handleRegister = async () => {
    if (!isFirebaseConfigured) {
      onToast("Firebase is not configured. Set the VITE_FIREBASE_* variables before registering.", "error");
      return;
    }

    const cleanEmail = sanitizeInput(email).toLowerCase();
    if (!cleanEmail || !password) {
      onToast("Please enter email and password", "error");
      return;
    }
    if (!isValidEmail(cleanEmail)) {
      onToast("Please enter a valid email address", "error");
      return;
    }

    const pwCheck = validatePassword(password);
    if (!pwCheck.valid) {
      onToast(pwCheck.reason!, "error");
      return;
    }

    // Rate limit registration attempts
    const rl = checkRateLimit("register", 3, 300_000, 300_000); // 3 per 5 min, 5 min cooldown
    if (!rl.allowed) {
      onToast(`Too many attempts. Try again in ${Math.ceil(rl.retryAfterMs / 60000)} min.`, "error");
      return;
    }

    setLoading(true);
    try {
      const result = await createUserWithEmailAndPassword(auth, cleanEmail, password);

      // Send verification email
      await sendEmailVerification(result.user, actionCodeSettings);

      // Create user doc in Firestore
      await ensureUserDoc(result.user.uid, result.user.email || cleanEmail);

      // Sign out immediately — user must verify email first
      await auth.signOut();

      setFlowState("verificationSent");
      onToast("Verification email sent! Check your inbox.", "success");
    } catch (e: unknown) {
      onToast(safeErrorMessage(e), "error");
    }
    setLoading(false);
  };

  // ── Login: Verify credentials → sign in directly ──
  const handleLogin = async () => {
    if (!isFirebaseConfigured) {
      onToast("Firebase is not configured. Set the VITE_FIREBASE_* variables before signing in.", "error");
      return;
    }

    const cleanEmail = sanitizeInput(email).toLowerCase();
    if (!cleanEmail || !password) {
      onToast("Please enter email and password", "error");
      return;
    }

    // Rate limit login attempts
    const rl = checkRateLimit("login", 5, 60_000, 120_000); // 5 per min, 2 min cooldown
    if (!rl.allowed) {
      onToast(`Too many attempts. Try again in ${Math.ceil(rl.retryAfterMs / 1000)}s.`, "error");
      return;
    }

    setLoading(true);
    try {
      const result = await signInWithEmailAndPassword(auth, cleanEmail, password);

      // Check if email is verified
      if (!result.user.emailVerified) {
        // Resend verification email
        await sendEmailVerification(result.user, actionCodeSettings);
        await auth.signOut();
        setFlowState("verificationSent");
        onToast("Please verify your email first. Verification email resent.", "info");
        setLoading(false);
        return;
      }

      // Credentials valid & email verified → user is now signed in
      await ensureUserDoc(result.user.uid, result.user.email || cleanEmail);
      resetRateLimit("login");
      onToast("Welcome back!", "success");
    } catch (e: unknown) {
      onToast(safeErrorMessage(e), "error");
    }
    setLoading(false);
  };

  // ── Forgot Password ──
  const handleForgotPassword = async () => {
    if (!isFirebaseConfigured) {
      onToast("Firebase is not configured. Set the VITE_FIREBASE_* variables before resetting passwords.", "error");
      return;
    }

    const target = sanitizeInput(resetEmail).toLowerCase();
    if (!target) {
      onToast("Please enter your email address", "error");
      return;
    }
    if (!isValidEmail(target)) {
      onToast("Please enter a valid email address", "error");
      return;
    }

    // Rate limit password reset
    const rl = checkRateLimit("reset_pw", 3, 300_000, 300_000); // 3 per 5 min
    if (!rl.allowed) {
      onToast(`Too many attempts. Try again in ${Math.ceil(rl.retryAfterMs / 60000)} min.`, "error");
      return;
    }

    setLoading(true);
    try {
      await sendPasswordResetEmail(auth, target, actionCodeSettings);
      setFlowState("resetSent");
      onToast("Password reset email sent!", "success");
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : "";
      if (msg.includes("user-not-found")) {
        // Don't reveal whether the email exists — security best practice
        setFlowState("resetSent");
        onToast("If an account exists with this email, a reset link has been sent.", "info");
      } else {
        onToast(safeErrorMessage(e), "error");
      }
    }
    setLoading(false);
  };

  // ── Resend verification email ──
  const handleResendVerification = async () => {
    if (!isFirebaseConfigured) {
      onToast("Firebase is not configured. Set the VITE_FIREBASE_* variables before verifying accounts.", "error");
      return;
    }

    if (!email || !password) {
      onToast("Enter your email and password to resend verification", "error");
      setFlowState("idle");
      return;
    }
    setLoading(true);
    try {
      const result = await signInWithEmailAndPassword(auth, email, password);
      if (result.user.emailVerified) {
        onToast("Your email is already verified! You can sign in.", "info");
        setFlowState("idle");
        setMode("login");
        await auth.signOut();
      } else {
        await sendEmailVerification(result.user, actionCodeSettings);
        await auth.signOut();
        onToast("Verification email resent! Check your inbox and spam folder.", "success");
      }
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : "Failed to resend";
      onToast(msg, "error");
    }
    setLoading(false);
  };

  const handleCheckCode = async () => {
    const trimmed = sanitizeInput(redeemCode).toUpperCase().replace(/[^A-Z0-9-]/g, "");
    if (!trimmed) {
      onToast("Please enter a license code", "error");
      return;
    }
    if (!isValidLicenseCode(trimmed)) {
      onToast("Invalid code format", "error");
      return;
    }

    // Rate limit code checks to prevent enumeration
    const rl = checkRateLimit("check_code", 10, 60_000, 120_000); // 10 per min, 2 min cooldown
    if (!rl.allowed) {
      onToast(`Too many attempts. Try again in ${Math.ceil(rl.retryAfterMs / 1000)}s.`, "error");
      return;
    }

    setRedeemChecking(true);
    try {
      const snap = await getDoc(doc(db, "licenses", trimmed));
      if (!snap.exists()) {
        setRedeemValid(false);
        onToast("Invalid license code", "error");
      } else {
        const d = snap.data();
        if (!d.active) {
          setRedeemValid(false);
          onToast("This code has been deactivated", "error");
        } else if (d.uid && d.uid !== "") {
          // Already redeemed — check if trial expired
          if (d.plan === "trial" && d.expiresAt) {
            const expiresMs = d.expiresAt.toDate ? d.expiresAt.toDate().getTime() : new Date(d.expiresAt).getTime();
            if (Date.now() > expiresMs) {
              setRedeemValid(false);
              onToast("This trial code has expired", "error");
            } else {
              setRedeemValid(false);
              onToast("This code has already been redeemed", "error");
            }
          } else {
            setRedeemValid(false);
            onToast("This code has already been redeemed", "error");
          }
        } else {
          setRedeemValid(true);
          setRedeemPlan(d.plan === "trial" ? `Trial (${d.trialDays || "?"}d)` : "Permanent");
          onToast("Valid code! Create an account or sign in to activate.", "success");
        }
      }
    } catch {
      setRedeemValid(false);
      onToast("Error checking code", "error");
    }
    setRedeemChecking(false);
  };

  const resetFlow = () => {
    setFlowState("idle");
    setPassword("");
  };

  // ═══ EMAIL SENT SCREENS ═══

  // Verification sent screen (after registration)
  if (flowState === "verificationSent") {
    return (
      <div className="login-screen">
        <div className="login-bg-effects">
          <div className="orb orb-1" /><div className="orb orb-2" /><div className="orb orb-3" />
        </div>
        <div className="email-sent-container">
          <div className="email-sent-card">
            <div className="email-sent-icon email-sent-icon-green">
              <Mail size={48} />
            </div>
            <h2>Verify Your Email</h2>
            <p className="email-sent-address">{email}</p>
            <p className="email-sent-desc">
              We've sent a verification link to your email address.
              Click the link in the email to activate your account.
            </p>
            <div className="email-sent-warning">
              <AlertTriangle size={16} />
              <span>Don't forget to check your <strong>Spam / Junk</strong> folder if you don't see the email in your inbox!</span>
            </div>
            <div className="email-sent-steps">
              <div className="email-step">
                <span className="email-step-num">1</span>
                <span>Open the verification email</span>
              </div>
              <div className="email-step">
                <span className="email-step-num">2</span>
                <span>Click the verification link</span>
              </div>
              <div className="email-step">
                <span className="email-step-num">3</span>
                <span>Come back here and sign in</span>
              </div>
            </div>
            <div className="email-sent-actions">
              <button className="btn-secondary btn-full" onClick={handleResendVerification} disabled={loading}>
                {loading ? <span className="spinner" /> : <><RefreshCw size={16} /> Resend Verification Email</>}
              </button>
              <button className="btn-ghost btn-full" onClick={() => { resetFlow(); setMode("login"); }}>
                <ArrowLeft size={16} /> Back to Sign In
              </button>
            </div>
          </div>
        </div>
      </div>
    );
  }

  // Password reset sent screen
  if (flowState === "resetSent") {
    return (
      <div className="login-screen">
        <div className="login-bg-effects">
          <div className="orb orb-1" /><div className="orb orb-2" /><div className="orb orb-3" />
        </div>
        <div className="email-sent-container">
          <div className="email-sent-card">
            <div className="email-sent-icon email-sent-icon-yellow">
              <CheckCircle size={48} />
            </div>
            <h2>Password Reset Sent</h2>
            <p className="email-sent-address">{resetEmail}</p>
            <p className="email-sent-desc">
              If an account exists with this email, you'll receive a password reset link.
              Click the link to set a new password.
            </p>
            <div className="email-sent-warning">
              <AlertTriangle size={16} />
              <span>Check your <strong>Spam / Junk</strong> folder if you don't see the email!</span>
            </div>
            <div className="email-sent-steps">
              <div className="email-step">
                <span className="email-step-num">1</span>
                <span>Open the password reset email</span>
              </div>
              <div className="email-step">
                <span className="email-step-num">2</span>
                <span>Click the reset link</span>
              </div>
              <div className="email-step">
                <span className="email-step-num">3</span>
                <span>Create your new password</span>
              </div>
            </div>
            <div className="email-sent-actions">
              <button className="btn-ghost btn-full" onClick={() => { resetFlow(); setMode("login"); }}>
                <ArrowLeft size={16} /> Back to Sign In
              </button>
            </div>
          </div>
        </div>
      </div>
    );
  }

  // ═══ MAIN LOGIN / REGISTER / REDEEM FORM ═══

  return (
    <div className="login-screen">
      {/* Animated background orbs */}
      <div className="login-bg-effects">
        <div className="orb orb-1" />
        <div className="orb orb-2" />
        <div className="orb orb-3" />
      </div>

      <div className="login-container">
        {/* Left Hero Panel */}
        <div className="login-hero">
          <div className="hero-content">
            <div className="hero-badge">
              <Sparkles size={14} />
              <span>Premium Software</span>
            </div>
            <div className="hero-logo-wrapper">
              <div className="hero-logo">
                <Shield size={56} />
              </div>
            </div>
            <h1>LegitX <span className="v2">V2</span></h1>
            <p className="hero-tagline">Your all-in-one gaming companion — performance tools, sensitivity fine-tuning, and smart overlays built for competitive play.</p>
            <div className="hero-features">
              <div className="hero-feature">
                <ChevronRight size={14} />
                <span>Precision Sensitivity Controls</span>
              </div>
              <div className="hero-feature">
                <ChevronRight size={14} />
                <span>Real-Time Performance Optimizer</span>
              </div>
              <div className="hero-feature">
                <ChevronRight size={14} />
                <span>Smart Overlay & HUD Tools</span>
              </div>
              <div className="hero-feature">
                <ChevronRight size={14} />
                <span>Secure & Licensed Per Account</span>
              </div>
            </div>
          </div>
          <div className="hero-footer">
            <span>&copy; 2026 LegitX V2. All rights reserved.</span>
          </div>
        </div>

        {/* Right Form Panel */}
        <div className="login-card">
          {/* Forgot Password Mode */}
          {mode === "forgotPassword" ? (
            <div className="login-card-body">
              <div className="login-form-header">
                <h2>Reset Password</h2>
                <p>Enter your email address and we'll send you a link to reset your password.</p>
              </div>
              <div className="login-form">
                <div className="input-group">
                  <label>Email Address</label>
                  <input
                    type="email"
                    placeholder="you@example.com"
                    value={resetEmail}
                    onChange={e => setResetEmail(e.target.value)}
                    onKeyDown={e => e.key === "Enter" && handleForgotPassword()}
                    autoFocus
                  />
                </div>

                <button
                  className="btn-primary btn-full btn-glow"
                  onClick={handleForgotPassword}
                  disabled={loading || !resetEmail.trim()}
                >
                  {loading ? <span className="spinner" /> : <><Mail size={16} /> Send Reset Link</>}
                </button>

                <button className="btn-ghost btn-full" onClick={() => { setMode("login"); setResetEmail(""); }}>
                  <ArrowLeft size={16} /> Back to Sign In
                </button>
              </div>
            </div>
          ) : (
            <>
              {/* Mode Tabs */}
              <div className="login-mode-tabs">
                <button
                  className={`mode-tab${mode === "login" ? " active" : ""}`}
                  onClick={() => setMode("login")}
                >
                  <LogIn size={15} /> Sign In
                </button>
                <button
                  className={`mode-tab${mode === "register" ? " active" : ""}`}
                  onClick={() => setMode("register")}
                >
                  <UserPlus size={15} /> Register
                </button>
                <button
                  className={`mode-tab${mode === "redeem" ? " active" : ""}`}
                  onClick={() => setMode("redeem")}
                >
                  <Key size={15} /> Redeem
                </button>
              </div>

              <div className="login-card-body">
                {/* REDEEM MODE */}
                {mode === "redeem" ? (
                  <div className="redeem-first-flow">
                    <div className="redeem-first-header">
                      <Key size={28} className="redeem-first-icon" />
                      <h2>Got a License Code?</h2>
                      <p>Enter your code below to verify it, then create an account or sign in to activate it.</p>
                    </div>

                    <div className="input-group">
                      <label>License Code</label>
                      <div className="input-with-action">
                        <input
                          type="text"
                          placeholder="LEGITX-XXXX-XXXX-XXXX"
                          value={redeemCode}
                          onChange={e => setRedeemCode(e.target.value.toUpperCase())}
                          onKeyDown={e => e.key === "Enter" && handleCheckCode()}
                          className="code-input"
                          autoFocus
                        />
                      </div>
                    </div>

                    {redeemValid === true && (
                      <div className="redeem-valid-box">
                        <div className="valid-badge">✓ Valid Code</div>
                        <span>Plan: <strong>{redeemPlan}</strong></span>
                        <p>Now sign in or create an account to activate this license.</p>
                      </div>
                    )}

                    {redeemValid === false && (
                      <div className="redeem-invalid-box">
                        <span>✕ Invalid or already redeemed code</span>
                      </div>
                    )}

                    <button
                      className="btn-primary btn-full btn-glow"
                      onClick={handleCheckCode}
                      disabled={redeemChecking || !redeemCode.trim()}
                    >
                      {redeemChecking ? <span className="spinner" /> : <><Key size={16} /> Verify Code</>}
                    </button>

                    <div className="login-divider"><span>then</span></div>

                    <div className="redeem-action-row">
                      <button className="btn-secondary btn-full" onClick={() => setMode("login")}>
                        <LogIn size={15} /> Sign In
                      </button>
                      <button className="btn-secondary btn-full" onClick={() => setMode("register")}>
                        <UserPlus size={15} /> Create Account
                      </button>
                    </div>
                  </div>
                ) : (
                  /* LOGIN / REGISTER MODE */
                  <>
                    <div className="login-form-header">
                      <h2>{mode === "register" ? "Create Your Account" : "Welcome Back"}</h2>
                      <p>{mode === "register"
                        ? "Sign up to get started — we'll send a verification email"
                        : "Sign in securely — we'll send a confirmation link to your email"}</p>
                    </div>

                    <div className="login-form">
                      <div className="input-group">
                        <label>Email Address</label>
                        <input
                          type="email"
                          placeholder="you@example.com"
                          value={email}
                          onChange={e => setEmail(e.target.value)}
                          onKeyDown={e => e.key === "Enter" && (mode === "register" ? handleRegister() : handleLogin())}
                        />
                      </div>
                      <div className="input-group">
                        <label>Password</label>
                        <div className="input-password-wrapper">
                          <input
                            type={showPassword ? "text" : "password"}
                            placeholder={mode === "register" ? "Min 8 chars, uppercase, number, symbol" : "Enter your password"}
                            value={password}
                            onChange={e => setPassword(e.target.value)}
                            onKeyDown={e => e.key === "Enter" && (mode === "register" ? handleRegister() : handleLogin())}
                          />
                          <button
                            className="password-toggle"
                            onClick={() => setShowPassword(!showPassword)}
                            type="button"
                            tabIndex={-1}
                          >
                            {showPassword ? <EyeOff size={16} /> : <Eye size={16} />}
                          </button>
                        </div>
                        {mode === "register" && (
                          <div className="password-requirements">
                            <span className={password.length >= 8 ? "req-met" : ""}>8+ characters</span>
                            <span className={/[A-Z]/.test(password) ? "req-met" : ""}>Uppercase</span>
                            <span className={/[0-9]/.test(password) ? "req-met" : ""}>Number</span>
                            <span className={/[^A-Za-z0-9]/.test(password) ? "req-met" : ""}>Symbol</span>
                          </div>
                        )}
                      </div>

                      <button
                        className="btn-primary btn-full btn-glow"
                        onClick={mode === "register" ? handleRegister : handleLogin}
                        disabled={loading}
                      >
                        {loading ? (
                          <span className="spinner" />
                        ) : mode === "register" ? (
                          <><UserPlus size={16} /> Create Account</>
                        ) : (
                          <><LogIn size={16} /> Sign In</>
                        )}
                      </button>

                      {/* Forgot Password link (only on login) */}
                      {mode === "login" && (
                        <div className="forgot-password-row">
                          <button className="link-btn" onClick={() => { setMode("forgotPassword"); setResetEmail(email); }}>
                            Forgot your password?
                          </button>
                        </div>
                      )}

                      <div className="login-divider"><span>or continue with</span></div>

                      <button
                        className="btn-google"
                        onClick={handleGoogle}
                        disabled={loading}
                      >
                        <svg width="18" height="18" viewBox="0 0 24 24">
                          <path fill="#4285F4" d="M22.56 12.25c0-.78-.07-1.53-.2-2.25H12v4.26h5.92a5.06 5.06 0 01-2.2 3.32v2.77h3.57c2.08-1.92 3.28-4.74 3.28-8.1z" />
                          <path fill="#34A853" d="M12 23c2.97 0 5.46-.98 7.28-2.66l-3.57-2.77c-.98.66-2.23 1.06-3.71 1.06-2.86 0-5.29-1.93-6.16-4.53H2.18v2.84C3.99 20.53 7.7 23 12 23z" />
                          <path fill="#FBBC05" d="M5.84 14.09c-.22-.66-.35-1.36-.35-2.09s.13-1.43.35-2.09V7.07H2.18C1.43 8.55 1 10.22 1 12s.43 3.45 1.18 4.93l2.85-2.22.81-.62z" />
                          <path fill="#EA4335" d="M12 5.38c1.62 0 3.06.56 4.21 1.64l3.15-3.15C17.45 2.09 14.97 1 12 1 7.7 1 3.99 3.47 2.18 7.07l3.66 2.84c.87-2.6 3.3-4.53 6.16-4.53z" />
                        </svg>
                        Continue with Google
                      </button>

                      <p className="login-toggle">
                        {mode === "register" ? "Already have an account?" : "Don't have an account?"}{" "}
                        <button className="link-btn" onClick={() => setMode(mode === "register" ? "login" : "register")}>
                          {mode === "register" ? "Sign In" : "Create Account"}
                        </button>
                      </p>

                    </div>
                  </>
                )}
              </div>
            </>
          )}
        </div>
      </div>
    </div>
  );
}
