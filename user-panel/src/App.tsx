import { useState, useEffect } from "react";
import { onAuthStateChanged, signOut, type User } from "firebase/auth";
import { auth } from "./firebase";
import { useToast } from "./hooks/useToast";
import { ToastContainer } from "./components/ToastContainer";
import { LoginScreen } from "./components/LoginScreen";
import { Dashboard } from "./components/Dashboard";
import {
  initAntiTamper, markSessionStart, isSessionExpired, clearSession,
} from "./security";

// Initialize anti-tamper protections on load
initAntiTamper();

function App() {
  const [user, setUser] = useState<User | null>(null);
  const [loading, setLoading] = useState(true);
  const { toasts, show: showToast } = useToast();

  // ── Session timeout checker — runs every 60s ──
  useEffect(() => {
    const interval = setInterval(() => {
      if (user && isSessionExpired()) {
        clearSession();
        signOut(auth);
        showToast("Session expired — please sign in again", "info");
      }
    }, 60_000);
    return () => clearInterval(interval);
  }, [user, showToast]);

  useEffect(() => {
    const unsub = onAuthStateChanged(auth, (u) => {
      // For email/password users, only allow verified users through
      // Google users are always verified
      if (u && !u.emailVerified && u.providerData[0]?.providerId === "password") {
        // Unverified email user — treat as not signed in
        setUser(null);
      } else {
        setUser(u);
        if (u) {
          markSessionStart();
        } else {
          clearSession();
        }
      }
      setLoading(false);
    });
    return unsub;
  }, []);

  if (loading) {
    return (
      <div className="loading-screen">
        <span className="spinner" />
        <span>Loading...</span>
      </div>
    );
  }

  return (
    <>
      {user ? (
        <Dashboard email={user.email || ""} onToast={showToast} />
      ) : (
        <LoginScreen onToast={showToast} />
      )}
      <ToastContainer toasts={toasts} />
    </>
  );
}

export default App;
