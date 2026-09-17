import { useState, useEffect } from "react";
import { onAuthStateChanged, signOut, type User } from "firebase/auth";
import { auth } from "./firebase";
import { fetchMyResellerProfile } from "./api";
import { LoginScreen } from "./components/LoginScreen";
import { Dashboard } from "./components/Dashboard";
import {
  initAntiTamper, markSessionStart, isSessionExpired, clearSession, updateActivity,
} from "./security";
import "./index.css";

// Initialize anti-tamper protections on load
initAntiTamper();

function App() {
  const [user, setUser] = useState<User | null>(null);
  const [checking, setChecking] = useState(true);

  // ── Session + idle timeout checker — runs every 30s ──
  useEffect(() => {
    const interval = setInterval(() => {
      if (user && isSessionExpired()) {
        clearSession();
        signOut(auth);
      }
    }, 30_000);
    return () => clearInterval(interval);
  }, [user]);

  // ── Track user activity for idle timeout ──
  useEffect(() => {
    if (!user) return;
    const onActivity = () => updateActivity();
    window.addEventListener("mousemove", onActivity, { passive: true });
    window.addEventListener("keydown", onActivity, { passive: true });
    window.addEventListener("click", onActivity, { passive: true });
    return () => {
      window.removeEventListener("mousemove", onActivity);
      window.removeEventListener("keydown", onActivity);
      window.removeEventListener("click", onActivity);
    };
  }, [user]);

  useEffect(() => {
    const unsub = onAuthStateChanged(auth, async (u) => {
      if (u) {
        // Verify this user is actually an active reseller
        try {
          const profile = await fetchMyResellerProfile();
          if (profile && profile.active) {
            setUser(u);
            markSessionStart();
          } else {
            setUser(null);
            clearSession();
          }
        } catch {
          setUser(null);
          clearSession();
        }
      } else {
        setUser(null);
        clearSession();
      }
      setChecking(false);
    });
    return unsub;
  }, []);

  if (checking) {
    return (
      <div className="loading-screen">
        <div className="spinner" />
        <p>Checking authentication…</p>
      </div>
    );
  }

  if (!user) {
    return <LoginScreen />;
  }

  return <Dashboard email={user.email || ""} />;
}

export default App;
