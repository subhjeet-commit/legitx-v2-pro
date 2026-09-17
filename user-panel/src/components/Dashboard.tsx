import { useState, useEffect, useMemo } from "react";
import { signOut } from "firebase/auth";
import { auth } from "../firebase";
import {
  Shield, LogOut, User, Key, Cpu,
  CheckCircle, XCircle, AlertTriangle, Ban,
  Clock, Monitor, Download, RefreshCw,
  Sparkles, Activity, ExternalLink, RotateCw,
  Megaphone, Bot, Pin,
} from "lucide-react";
import {
  fetchMyProfile, fetchMyHwid, fetchMyLicense, tsDisplay,
  requestSelfHwidReset, getSelfResetCooldown, fetchSiteConfig,
  fetchAnnouncements,
} from "../api";
import type { UserProfile, HwidData, LicenseInfo, SiteConfig, Announcement } from "../types";
import { RedeemModal } from "./RedeemModal";
import { AiSupportChat } from "./AiSupportChat";

interface Props {
  email: string;
  onToast: (msg: string, type: "success" | "error" | "info") => void;
}

type Tab = "overview" | "license" | "hwid" | "download" | "announcements" | "ai-support";

export function Dashboard({ email, onToast }: Props) {
  const [tab, setTab] = useState<Tab>("overview");
  const [profile, setProfile] = useState<UserProfile | null>(null);
  const [hwid, setHwid] = useState<HwidData | null>(null);
  const [license, setLicense] = useState<LicenseInfo | null>(null);
  const [loading, setLoading] = useState(true);
  const [redeemOpen, setRedeemOpen] = useState(false);
  const [refreshing, setRefreshing] = useState(false);
  const [resetBusy, setResetBusy] = useState(false);
  const [showResetConfirm, setShowResetConfirm] = useState(false);
  const [siteConfig, setSiteConfig] = useState<SiteConfig | null>(null);
  const [announcements, setAnnouncements] = useState<Announcement[]>([]);

  useEffect(() => {
    let ignore = false;

    Promise.all([
      fetchMyProfile(),
      fetchMyHwid(),
      fetchSiteConfig(),
      fetchAnnouncements(),
    ]).then(async ([prof, hw, sc, ann]) => {
      if (ignore) return;
      setProfile(prof);
      setHwid(hw);
      setSiteConfig(sc);
      setAnnouncements(ann);
      if (prof.licenseCode) {
        const lic = await fetchMyLicense(prof.licenseCode);
        if (!ignore) setLicense(lic);
      }
      setLoading(false);
    }).catch(() => {
      if (!ignore) setLoading(false);
    });

    return () => { ignore = true; };
  }, []);

  const reload = async () => {
    setRefreshing(true);
    try {
      const [prof, hw, sc, ann] = await Promise.all([
        fetchMyProfile(),
        fetchMyHwid(),
        fetchSiteConfig(),
        fetchAnnouncements(),
      ]);
      setProfile(prof);
      setHwid(hw);
      setSiteConfig(sc);
      setAnnouncements(ann);
      if (prof.licenseCode) {
        const lic = await fetchMyLicense(prof.licenseCode);
        setLicense(lic);
      } else {
        setLicense(null);
      }
    } catch { /* ignore */ }
    setRefreshing(false);
  };

  const handleLogout = async () => {
    try { await signOut(auth); } catch { /* ignore */ }
  };

  const resetCooldown = useMemo(() => getSelfResetCooldown(hwid), [hwid]);

  const handleSelfReset = async () => {
    setResetBusy(true);
    try {
      const result = await requestSelfHwidReset();
      if (result.success) {
        onToast("Windows reset requested successfully! You can now log in on your reset/reinstalled PC.", "success");
        setShowResetConfirm(false);
        reload();
      } else if (result.nextAvailable) {
        onToast(`Reset not available yet. Try again on ${result.nextAvailable.toLocaleDateString()}.`, "error");
      }
    } catch (e: any) {
      onToast("Failed: " + e.message, "error");
    }
    setResetBusy(false);
  };

  const statusInfo = useMemo(() => {
    if (!profile) return { icon: <User size={16} />, label: "Loading", cls: "badge-gray" };
    switch (profile.status) {
      case "banned": return { icon: <Ban size={16} />, label: "Banned", cls: "badge-red" };
      case "suspended": return { icon: <AlertTriangle size={16} />, label: "Suspended", cls: "badge-orange" };
      case "deactivated": return { icon: <XCircle size={16} />, label: "Deactivated", cls: "badge-yellow" };
      default: return { icon: <CheckCircle size={16} />, label: "Active", cls: "badge-green" };
    }
  }, [profile]);

  const trialExpiry = useMemo(() => {
    if (!license || license.plan !== "trial" || !license.expiresAt) return null;
    const exp = license.expiresAt.toDate ? license.expiresAt.toDate() : new Date(String(license.expiresAt));
    const now = new Date();
    const diff = Math.ceil((exp.getTime() - now.getTime()) / (1000 * 60 * 60 * 24));
    return { date: exp.toLocaleDateString(), daysLeft: diff, expired: diff <= 0 };
  }, [license]);

  const tabs: { id: Tab; label: string; icon: React.ReactNode; badge?: number }[] = [
    { id: "announcements", label: "Announcements", icon: <Megaphone size={16} />, badge: announcements.length || undefined },
    { id: "overview", label: "Overview", icon: <Activity size={16} /> },
    { id: "license", label: "License", icon: <Key size={16} /> },
    { id: "hwid", label: "Hardware", icon: <Cpu size={16} /> },
    ...(profile?.licensed ? [{ id: "download" as Tab, label: "Download", icon: <Download size={16} /> }] : []),
    { id: "ai-support", label: "AI Support", icon: <Bot size={16} /> },
  ];

  return (
    <div className="dashboard">
      {/* ─── Top Bar ─── */}
      <header className="topbar">
        <div className="topbar-left">
          <Shield size={22} className="topbar-logo" />
          <span className="topbar-brand">LegitX <span className="v2">V2</span></span>
        </div>
        <div className="topbar-right">
          <button className={`btn-icon-refresh${refreshing ? " spinning" : ""}`} onClick={reload} title="Refresh">
            <RefreshCw size={16} />
          </button>
          <span className="user-email">{email}</span>
          <span className={`badge ${statusInfo.cls}`}><span className="badge-dot" />{statusInfo.label}</span>
          <button className="btn-logout" onClick={handleLogout} title="Sign Out">
            <LogOut size={16} />
            <span>Sign Out</span>
          </button>
        </div>
      </header>

      {/* Status Banner */}
      {profile && profile.status !== "active" && (
        <div className={`status-banner status-${profile.status}`}>
          {statusInfo.icon}
          <span>
            {profile.status === "banned"
              ? "Your account has been permanently banned. Contact support if you believe this is an error."
              : profile.status === "suspended"
              ? "Your account has been suspended. All features are disabled."
              : "Your account has been deactivated. Contact an administrator."}
          </span>
        </div>
      )}

      {/* ─── Tab Bar ─── */}
      <nav className="tab-bar">
        {tabs.map(t => (
          <button
            key={t.id}
            className={`tab-btn${tab === t.id ? " active" : ""}`}
            onClick={() => setTab(t.id)}
          >
            {t.icon}
            <span>{t.label}</span>
            {t.badge ? <span className="tab-badge">{t.badge}</span> : null}
          </button>
        ))}
      </nav>

      {/* ─── Content ─── */}
      <main className="tab-content">
        {loading ? (
          <div className="loading-state">
            <div className="loading-pulse">
              <span className="spinner-lg" />
              <span>Loading your data...</span>
            </div>
          </div>
        ) : !profile ? (
          <div className="loading-state">Unable to load profile. Please try again.</div>
        ) : (
          <>
            {/* ═══ OVERVIEW ═══ */}
            {tab === "overview" && (
              <div className="overview-layout">
                {/* Quick Stats Row */}
                <div className="quick-stats">
                  <div className="stat-card">
                    <div className="stat-icon stat-icon-green">
                      <User size={20} />
                    </div>
                    <div className="stat-info">
                      <span className="stat-value">{statusInfo.label}</span>
                      <span className="stat-label">Account Status</span>
                    </div>
                  </div>
                  <div className="stat-card">
                    <div className={`stat-icon ${profile.licensed ? "stat-icon-blue" : "stat-icon-gray"}`}>
                      <Key size={20} />
                    </div>
                    <div className="stat-info">
                      <span className="stat-value">{profile.licensed ? (license?.plan === "trial" ? "Trial" : "Permanent") : "None"}</span>
                      <span className="stat-label">License Plan</span>
                    </div>
                  </div>
                  <div className="stat-card">
                    <div className={`stat-icon ${hwid?.permanentHash ? "stat-icon-purple" : "stat-icon-gray"}`}>
                      <Cpu size={20} />
                    </div>
                    <div className="stat-info">
                      <span className="stat-value">{hwid?.permanentHash ? "Registered" : "Unregistered"}</span>
                      <span className="stat-label">Hardware</span>
                    </div>
                  </div>
                </div>

                {/* Cards Grid */}
                <div className="panel-grid">
                  {/* Account Card */}
                  <div className="glass-card">
                    <div className="glass-card-header">
                      <div className="glass-card-icon"><User size={18} /></div>
                      <h3>Account Details</h3>
                    </div>
                    <div className="glass-card-body">
                      <div className="info-row">
                        <span className="info-label">Email</span>
                        <span className="info-value">{profile.email}</span>
                      </div>
                      <div className="info-row">
                        <span className="info-label">UID</span>
                        <span className="info-value mono" style={{ fontSize: 11 }}>{profile.uid}</span>
                      </div>
                      <div className="info-row">
                        <span className="info-label">Status</span>
                        <span className="info-value">
                          <span className={`badge ${statusInfo.cls}`}><span className="badge-dot" />{statusInfo.label}</span>
                        </span>
                      </div>
                    </div>
                  </div>

                  {/* License Card */}
                  <div className="glass-card">
                    <div className="glass-card-header">
                      <div className="glass-card-icon"><Key size={18} /></div>
                      <h3>License</h3>
                    </div>
                    <div className="glass-card-body">
                      {profile.licensed && license ? (
                        <>
                          <div className="info-row">
                            <span className="info-label">Status</span>
                            <span className="info-value">
                              <span className="badge badge-green"><span className="badge-dot" />Active</span>
                            </span>
                          </div>
                          <div className="info-row">
                            <span className="info-label">Plan</span>
                            <span className="info-value">
                              {license.plan === "trial"
                                ? <span className="badge badge-orange">Trial {license.trialDays ? `(${license.trialDays}d)` : ""}</span>
                                : <span className="badge badge-blue">Permanent</span>}
                            </span>
                          </div>
                          <div className="info-row">
                            <span className="info-label">Code</span>
                            <span className="info-value mono">{profile.licenseCode}</span>
                          </div>
                          {trialExpiry && (
                            <div className="info-row">
                              <span className="info-label">Expires</span>
                              <span className="info-value">
                                {trialExpiry.expired
                                  ? <span className="badge badge-red">Expired</span>
                                  : <span className="badge badge-yellow">{trialExpiry.daysLeft}d left</span>}
                              </span>
                            </div>
                          )}
                        </>
                      ) : (
                        <div className="empty-state-inline">
                          <Sparkles size={20} className="empty-icon" />
                          <p>No active license</p>
                          <button className="btn-primary btn-sm" onClick={() => { setTab("license"); setRedeemOpen(true); }}>
                            Redeem a Code
                          </button>
                        </div>
                      )}
                    </div>
                  </div>

                  {/* Hardware Card */}
                  <div className="glass-card">
                    <div className="glass-card-header">
                      <div className="glass-card-icon"><Cpu size={18} /></div>
                      <h3>Hardware</h3>
                    </div>
                    <div className="glass-card-body">
                      {hwid?.permanentHash ? (
                        <>
                          <div className="info-row">
                            <span className="info-label">Computer</span>
                            <span className="info-value">{hwid.computerName || "—"}</span>
                          </div>
                          <div className="info-row">
                            <span className="info-label">Last Login</span>
                            <span className="info-value mono">{hwid.lastLogin || "—"}</span>
                          </div>
                          <div className="info-row">
                            <span className="info-label">HWID Reset</span>
                            <span className="info-value">
                              {hwid.windowsReset || hwid.fullReset
                                ? <span className="badge badge-yellow"><Clock size={12} /> Awaiting App Login</span>
                                : <span className="badge badge-gray">Normal</span>}
                            </span>
                          </div>
                        </>
                      ) : (
                        <div className="empty-state-inline">
                          <Monitor size={20} className="empty-icon" />
                          <p>Not registered yet</p>
                          <span className="hint">Hardware registered on first app launch.</span>
                        </div>
                      )}
                    </div>
                  </div>
                </div>
              </div>
            )}

            {/* ═══ LICENSE TAB ═══ */}
            {tab === "license" && (
              <div className="section-center">
                <div className="glass-card wide">
                  <div className="glass-card-header">
                    <div className="glass-card-icon"><Key size={18} /></div>
                    <h3>License Details</h3>
                  </div>
                  <div className="glass-card-body">
                    {profile.licensed && license ? (
                      <div className="detail-grid">
                        <div className="info-row"><span className="info-label">License Code</span><span className="info-value mono">{license.code}</span></div>
                        <div className="info-row"><span className="info-label">Status</span><span className="info-value"><span className="badge badge-green"><span className="badge-dot" />Active</span></span></div>
                        <div className="info-row">
                          <span className="info-label">Plan</span>
                          <span className="info-value">
                            {license.plan === "trial"
                              ? <span className="badge badge-orange">Trial {license.trialDays ? `— ${license.trialDays} days` : ""}</span>
                              : <span className="badge badge-blue">Permanent</span>}
                          </span>
                        </div>
                        <div className="info-row"><span className="info-label">Redeemed At</span><span className="info-value mono">{tsDisplay(license.redeemedAt)}</span></div>
                        {trialExpiry && (
                          <>
                            <div className="info-row">
                              <span className="info-label">Expires</span>
                              <span className="info-value mono">{trialExpiry.date}</span>
                            </div>
                            <div className="info-row">
                              <span className="info-label">Days Left</span>
                              <span className="info-value">
                                {trialExpiry.expired
                                  ? <span className="badge badge-red">Expired</span>
                                  : <span className={`badge ${trialExpiry.daysLeft <= 3 ? "badge-red" : trialExpiry.daysLeft <= 7 ? "badge-yellow" : "badge-green"}`}>{trialExpiry.daysLeft} days</span>}
                              </span>
                            </div>
                          </>
                        )}
                        <div className="info-row"><span className="info-label">Created At</span><span className="info-value mono">{tsDisplay(license.createdAt)}</span></div>
                      </div>
                    ) : (
                      <div className="empty-hero">
                        <div className="empty-hero-icon">
                          <Key size={40} />
                        </div>
                        <h4>No Active License</h4>
                        <p>Enter a license code to activate your account and unlock all features.</p>
                        <button className="btn-primary btn-glow" onClick={() => setRedeemOpen(true)}>
                          <Key size={16} /> Redeem License Code
                        </button>
                      </div>
                    )}
                  </div>
                </div>
              </div>
            )}

            {/* ═══ HWID TAB ═══ */}
            {tab === "hwid" && (
              <div className="section-center">
                <div className="glass-card wide">
                  <div className="glass-card-header">
                    <div className="glass-card-icon"><Cpu size={18} /></div>
                    <h3>Hardware Information</h3>
                  </div>
                  <div className="glass-card-body">
                    {hwid?.permanentHash ? (
                      <div className="detail-grid">
                        <div className="info-row"><span className="info-label">Permanent Hash</span><span className="info-value mono">{hwid.permanentHash}</span></div>
                        <div className="info-row"><span className="info-label">Windows Hash</span><span className="info-value mono">{hwid.windowsHash || "—"}</span></div>
                        <div className="info-row"><span className="info-label">Computer Name</span><span className="info-value">{hwid.computerName || "—"}</span></div>
                        <div className="info-row"><span className="info-label">Registered</span><span className="info-value mono">{hwid.registeredAt || "—"}</span></div>
                        <div className="info-row"><span className="info-label">Last Login</span><span className="info-value mono">{hwid.lastLogin || "—"}</span></div>
                        <div className="info-row">
                          <span className="info-label">Reset Status</span>
                          <span className="info-value">
                            {hwid.windowsReset || hwid.fullReset
                              ? <span className="badge badge-yellow"><Clock size={12} /> Awaiting Next App Login</span>
                              : <span className="badge badge-gray">Normal</span>}
                          </span>
                        </div>
                        {(hwid.windowsReset || hwid.fullReset) && (
                          <div className="info-row">
                            <span className="info-label" />
                            <span className="info-value" style={{ fontSize: 12, color: "var(--gray-400)" }}>
                              Reset has been requested. Open LegitX V2 on your PC to complete the process.
                            </span>
                          </div>
                        )}
                        {hwid.lastSelfReset && (
                          <div className="info-row">
                            <span className="info-label">Last Self-Reset</span>
                            <span className="info-value mono">{new Date(hwid.lastSelfReset).toLocaleDateString()}</span>
                          </div>
                        )}
                      </div>
                    ) : (
                      <div className="empty-hero">
                        <div className="empty-hero-icon">
                          <Monitor size={40} />
                        </div>
                        <h4>No Hardware Registered</h4>
                        <p>Your hardware ID will be automatically registered when you first launch the LegitX V2 application.</p>
                      </div>
                    )}
                  </div>
                </div>

                {/* Self-Service Windows Reset Card */}
                {hwid?.permanentHash && (
                  <div className="glass-card wide">
                    <div className="glass-card-header">
                      <div className="glass-card-icon"><RotateCw size={18} /></div>
                      <h3>Windows Reset</h3>
                    </div>
                    <div className="glass-card-body">
                      <p className="hwid-reset-desc">
                        If you've <strong>reinstalled Windows</strong> or <strong>reset your PC</strong>, use this to re-link your account to your new installation.
                        This keeps your hardware registered but allows the software to accept your new Windows identity.
                      </p>

                      <div className="info-box info-box-blue" style={{ marginTop: 0 }}>
                        <Activity size={16} />
                        <p>This feature can be used <strong>once every 30 days</strong>. It only resets your Windows identity — your hardware registration stays intact.</p>
                      </div>

                      {resetCooldown.canReset ? (
                        <div style={{ marginTop: 16 }}>
                          {!showResetConfirm ? (
                            <button
                              className="btn-primary"
                              onClick={() => setShowResetConfirm(true)}
                              style={{ width: "100%" }}
                            >
                              <RotateCw size={16} /> Request Windows Reset
                            </button>
                          ) : (
                            <div className="reset-confirm-box">
                              <div className="info-box info-box-yellow" style={{ marginTop: 0, marginBottom: 12 }}>
                                <AlertTriangle size={16} />
                                <p>Are you sure? This will trigger a Windows HWID reset on your account. You'll need to log in again on the software after this.</p>
                              </div>
                              <div style={{ display: "flex", gap: 8 }}>
                                <button
                                  className="btn-danger"
                                  onClick={handleSelfReset}
                                  disabled={resetBusy}
                                  style={{ flex: 1 }}
                                >
                                  {resetBusy ? <><span className="spinner" /> Processing...</> : "Yes, Reset Now"}
                                </button>
                                <button
                                  className="btn-secondary"
                                  onClick={() => setShowResetConfirm(false)}
                                  disabled={resetBusy}
                                  style={{ flex: 1 }}
                                >
                                  Cancel
                                </button>
                              </div>
                            </div>
                          )}
                        </div>
                      ) : (
                        <div className="cooldown-box">
                          <div className="cooldown-header">
                            <Clock size={18} />
                            <span>Reset on Cooldown</span>
                          </div>
                          <p className="cooldown-text">
                            You've already used your monthly reset. Next available on{" "}
                            <strong>{resetCooldown.nextAvailable?.toLocaleDateString()}</strong>
                            {" "}({resetCooldown.daysLeft} day{resetCooldown.daysLeft !== 1 ? "s" : ""} left).
                          </p>
                          <div className="cooldown-bar">
                            <div className="cooldown-fill" style={{ width: `${Math.max(3, ((30 - resetCooldown.daysLeft) / 30) * 100)}%` }} />
                          </div>
                        </div>
                      )}
                    </div>
                  </div>
                )}
              </div>
            )}

            {/* ═══ DOWNLOAD TAB ═══ */}
            {tab === "download" && (
              <div className="section-center">
                <div className="glass-card wide">
                  <div className="glass-card-body download-content">
                    <div className="download-hero">
                      <div className="download-hero-glow">
                        <Shield size={56} />
                      </div>
                      <h2>LegitX <span className="v2">V2</span></h2>
                      <div className="version-pill">
                        <Sparkles size={12} /> Latest Release
                      </div>
                    </div>

                    <div className="download-grid">
                      <div className="download-box">
                        <h4><ExternalLink size={15} /> System Requirements</h4>
                        <ul>
                          {(siteConfig?.requirements || []).map((req) => (
                            <li key={req.id}>
                              {req.downloadUrl ? (
                                <a href={req.downloadUrl} target="_blank" rel="noopener noreferrer" className="req-link">
                                  {req.text} <ExternalLink size={11} />
                                </a>
                              ) : (
                                req.text
                              )}
                            </li>
                          ))}
                          {(!siteConfig || siteConfig.requirements.length === 0) && (
                            <>
                              <li>Windows 10 / 11 (64-bit)</li>
                              <li>.NET 8.0 Runtime</li>
                              <li>Active license required</li>
                            </>
                          )}
                        </ul>
                      </div>
                      <div className="download-box">
                        <h4><Activity size={15} /> Getting Started</h4>
                        <ol>
                          {(siteConfig?.gettingStarted || []).map((step) => (
                            <li key={step.id}>{step.text}</li>
                          ))}
                          {(!siteConfig || siteConfig.gettingStarted.length === 0) && (
                            <>
                              <li>Download the latest release</li>
                              <li>Extract the ZIP file</li>
                              <li>Run <code>LegitX V2.exe</code></li>
                              <li>Sign in with your account</li>
                            </>
                          )}
                        </ol>
                      </div>
                    </div>

                    {!profile.licensed && (
                      <div className="info-box info-box-yellow" style={{ maxWidth: 520, alignSelf: "center" }}>
                        <AlertTriangle size={16} />
                        <span>You need an active license to use the application. <button className="link-btn" onClick={() => { setTab("license"); setRedeemOpen(true); }}>Redeem a code</button></span>
                      </div>
                    )}

                    {siteConfig?.downloadUrl ? (
                      <a
                        className="btn-primary btn-glow btn-download"
                        href={siteConfig.downloadUrl}
                        target="_blank"
                        rel="noopener noreferrer"
                      >
                        <Download size={18} /> {siteConfig.downloadLabel || "Download LegitX V2"}
                      </a>
                    ) : (
                      <button
                        className="btn-primary btn-glow btn-download"
                        onClick={() => onToast("Download link coming soon!", "info")}
                      >
                        <Download size={18} /> {siteConfig?.downloadLabel || "Download LegitX V2"}
                      </button>
                    )}
                    {!siteConfig?.downloadUrl && (
                      <p className="hint" style={{ textAlign: "center" }}>Contact your reseller or admin for the download link.</p>
                    )}
                  </div>
                </div>
              </div>
            )}

            {/* ═══ ANNOUNCEMENTS TAB ═══ */}
            {tab === "announcements" && (
              <div className="section-center">
                <div className="glass-card wide">
                  <div className="glass-card-header">
                    <div className="glass-card-icon"><Megaphone size={18} /></div>
                    <h3>Announcements</h3>
                  </div>
                  <div className="glass-card-body">
                    {announcements.length === 0 ? (
                      <div className="empty-hero">
                        <div className="empty-hero-icon"><Megaphone size={40} /></div>
                        <h4>No Announcements</h4>
                        <p>There are no announcements at this time. Check back later!</p>
                      </div>
                    ) : (
                      <div className="announcements-list">
                        {announcements.map(ann => (
                          <div key={ann.id} className={`announcement-card announcement-${ann.type}`}>
                            <div className="announcement-header">
                              <div className="announcement-type-badge">
                                {ann.pinned && <Pin size={12} className="announcement-pin" />}
                                <span className={`announcement-type-label type-${ann.type}`}>
                                  {ann.type === "urgent" ? "🔴 Urgent" : ann.type === "warning" ? "⚠️ Warning" : ann.type === "update" ? "🚀 Update" : "ℹ️ Info"}
                                </span>
                              </div>
                              <span className="announcement-date">
                                {new Date(ann.createdAt).toLocaleDateString(undefined, { year: "numeric", month: "short", day: "numeric" })}
                              </span>
                            </div>
                            <h4 className="announcement-title">{ann.title}</h4>
                            <p className="announcement-body">{ann.body}</p>
                          </div>
                        ))}
                      </div>
                    )}
                  </div>
                </div>
              </div>
            )}

            {/* ═══ AI SUPPORT TAB ═══ */}
            {tab === "ai-support" && (
              <AiSupportChat onToast={onToast} />
            )}
          </>
        )}
      </main>

      {/* Redeem Modal */}
      <RedeemModal
        open={redeemOpen}
        onClose={() => setRedeemOpen(false)}
        onToast={onToast}
        onRedeemed={reload}
        alreadyLicensed={!!profile?.licensed}
      />
    </div>
  );
}
