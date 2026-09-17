import { useState, useEffect, type ReactNode } from "react";
import { Users, Key, ShoppingBag, Activity, LogOut, Shield, Settings, Power, Wrench, Megaphone } from "lucide-react";
import { signOut } from "firebase/auth";
import { auth } from "../firebase";
import { fetchStats, forceLogoutAllUsers, getMaintenanceMode, setMaintenanceMode } from "../api";
import type { Tab } from "../types";
import { useToast } from "../hooks/useToast";
import { ToastContainer } from "./ToastContainer";
import { UsersTab } from "./UsersTab";
import { LicensesTab } from "./LicensesTab";
import { ResellersTab } from "./ResellersTab";
import { LogsTab } from "./LogsTab";
import { SiteConfigTab } from "./SiteConfigTab";
import { AnnouncementsTab } from "./AnnouncementsTab";

interface Props {
  email: string;
}

export function Dashboard({ email }: Props) {
  const [tab, setTab] = useState<Tab>("users");
  const [stats, setStats] = useState({ totalUsers: 0, licensedUsers: 0, totalLicenses: 0, unusedLicenses: 0, totalResellers: 0 });
  const [refreshKey, setRefreshKey] = useState(0);
  const [logoutAllBusy, setLogoutAllBusy] = useState(false);
  const [maintenanceOn, setMaintenanceOn] = useState(false);
  const [maintenanceMsg, setMaintenanceMsg] = useState("LegitX V2 is currently under maintenance. Please try again later.");
  const [maintenanceBusy, setMaintenanceBusy] = useState(false);
  const { toasts, show: showToast } = useToast();

  useEffect(() => {
    let ignore = false;
    fetchStats().then(s => { if (!ignore) setStats(s); }).catch(() => {});
    getMaintenanceMode().then(m => { if (!ignore) { setMaintenanceOn(m.enabled); if (m.message) setMaintenanceMsg(m.message); } }).catch(() => {});
    return () => { ignore = true; };
  }, [refreshKey]);

  const triggerRefresh = () => {
    setRefreshKey(k => k + 1);
  };

  const handleLogout = async () => {
    try { await signOut(auth); } catch { /* ignore */ }
  };

  const handleForceLogoutAll = async () => {
    if (logoutAllBusy) return;
    if (!window.confirm(
      "⚠ Force Logout All Users\n\n" +
      "This will disconnect ALL users currently using LegitX V2 software.\n" +
      "They will need to sign in again.\n\n" +
      "Are you sure you want to proceed?"
    )) return;

    setLogoutAllBusy(true);
    try {
      await forceLogoutAllUsers();
      showToast("All users have been force-logged out", "success");
    } catch (e: any) {
      showToast("Failed: " + e.message, "error");
    } finally {
      setLogoutAllBusy(false);
    }
  };

  const handleToggleMaintenance = async () => {
    if (maintenanceBusy) return;
    const newState = !maintenanceOn;

    if (newState) {
      const msg = window.prompt(
        "Enter maintenance message shown to users:",
        maintenanceMsg
      );
      if (msg === null) return; // cancelled
      setMaintenanceMsg(msg);
      setMaintenanceBusy(true);
      try {
        await setMaintenanceMode(true, msg);
        setMaintenanceOn(true);
        showToast("Maintenance mode ENABLED — software will not open for users", "success");
      } catch (e: any) {
        showToast("Failed: " + e.message, "error");
      } finally {
        setMaintenanceBusy(false);
      }
    } else {
      if (!window.confirm("Disable maintenance mode? Users will be able to open the software again.")) return;
      setMaintenanceBusy(true);
      try {
        await setMaintenanceMode(false, maintenanceMsg);
        setMaintenanceOn(false);
        showToast("Maintenance mode DISABLED — software is back online", "success");
      } catch (e: any) {
        showToast("Failed: " + e.message, "error");
      } finally {
        setMaintenanceBusy(false);
      }
    }
  };

  const tabs: { id: Tab; label: string; icon: ReactNode }[] = [
    { id: "users", label: "Users", icon: <Users size={16} /> },
    { id: "licenses", label: "Licenses", icon: <Key size={16} /> },
    { id: "resellers", label: "Resellers", icon: <ShoppingBag size={16} /> },
    { id: "logs", label: "Activity Log", icon: <Activity size={16} /> },
    { id: "site", label: "Site Config", icon: <Settings size={16} /> },
    { id: "announcements", label: "Announcements", icon: <Megaphone size={16} /> },
  ];

  return (
    <div className="dashboard">
      {/* Top Bar */}
      <header className="topbar">
        <div className="topbar-left">
          <Shield size={22} color="#ef4444" />
          <h1 className="topbar-title">LegitX <span className="v2">V2</span></h1>
          <span className="admin-badge">ADMIN</span>
        </div>
        <div className="topbar-right">
          <span className="admin-email">{email}</span>
          <button
            className={`btn-maintenance${maintenanceOn ? " btn-maintenance-active" : ""}`}
            onClick={handleToggleMaintenance}
            disabled={maintenanceBusy}
            title={maintenanceOn ? "Disable maintenance mode" : "Enable maintenance mode"}
          >
            <Wrench size={16} /> {maintenanceBusy ? "Updating…" : maintenanceOn ? "Maintenance ON" : "Maintenance"}
          </button>
          <button className="btn-force-logout" onClick={handleForceLogoutAll} disabled={logoutAllBusy} title="Force logout all software users">
            <Power size={16} /> {logoutAllBusy ? "Logging out…" : "Logout All Users"}
          </button>
          <button className="btn-logout" onClick={handleLogout}>
            <LogOut size={16} /> Logout
          </button>
        </div>
      </header>

      {/* Stats Bar */}
      <div className="stats-bar">
        <div className="stat-card">
          <div className="stat-icon"><Users size={20} /></div>
          <div className="stat-info">
            <span className="stat-value">{stats.totalUsers}</span>
            <span className="stat-label">Total Users</span>
          </div>
        </div>
        <div className="stat-card">
          <div className="stat-icon green"><Key size={20} /></div>
          <div className="stat-info">
            <span className="stat-value">{stats.licensedUsers}</span>
            <span className="stat-label">Licensed Users</span>
          </div>
        </div>
        <div className="stat-card">
          <div className="stat-icon purple"><Key size={20} /></div>
          <div className="stat-info">
            <span className="stat-value">{stats.totalLicenses}</span>
            <span className="stat-label">Total Codes</span>
          </div>
        </div>
        <div className="stat-card">
          <div className="stat-icon cyan"><Key size={20} /></div>
          <div className="stat-info">
            <span className="stat-value">{stats.unusedLicenses}</span>
            <span className="stat-label">Available Codes</span>
          </div>
        </div>
        <div className="stat-card">
          <div className="stat-icon orange"><ShoppingBag size={20} /></div>
          <div className="stat-info">
            <span className="stat-value">{stats.totalResellers}</span>
            <span className="stat-label">Resellers</span>
          </div>
        </div>
      </div>

      {/* Tab Navigation */}
      <nav className="tab-bar">
        {tabs.map(t => (
          <button
            key={t.id}
            className={`tab-btn${tab === t.id ? " active" : ""}`}
            onClick={() => setTab(t.id)}
          >
            {t.icon} {t.label}
          </button>
        ))}
      </nav>

      {/* Tab Content */}
      <main className="tab-content">
        {tab === "users" && <UsersTab onToast={showToast} refreshKey={refreshKey} onRefresh={triggerRefresh} />}
        {tab === "licenses" && <LicensesTab onToast={showToast} refreshKey={refreshKey} onRefresh={triggerRefresh} />}
        {tab === "resellers" && <ResellersTab onToast={showToast} refreshKey={refreshKey} onRefresh={triggerRefresh} />}
        {tab === "logs" && <LogsTab onToast={showToast} refreshKey={refreshKey} />}
        {tab === "site" && <SiteConfigTab onToast={showToast} />}
        {tab === "announcements" && <AnnouncementsTab onToast={showToast} />}
      </main>

      <ToastContainer toasts={toasts} />
    </div>
  );
}
