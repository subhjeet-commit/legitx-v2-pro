import { useState, useEffect, useCallback, useMemo } from "react";
import { signOut } from "firebase/auth";
import { auth } from "../firebase";
import {
  Users, Key, LogOut, RefreshCw, Search, LayoutDashboard, Eye, Plus, Copy, Megaphone, Pin, Info, AlertTriangle, Zap, AlertOctagon,
} from "lucide-react";
import type { Tab, AppUser, License, ResellerProfile, Announcement } from "../types";
import { fetchMyResellerProfile, fetchUsers, fetchMyLicenses, generateLicenses, ts, fetchAnnouncements } from "../api";
import { useToast } from "../hooks/useToast";
import { ToastContainer } from "./ToastContainer";
import { Modal } from "./Modal";
import { UserDetailModal } from "./UserDetailModal";

interface Props { email: string; }

export function Dashboard({ email }: Props) {
  const [tab, setTab] = useState<Tab>("overview");
  const [profile, setProfile] = useState<ResellerProfile | null>(null);
  const [users, setUsers] = useState<AppUser[]>([]);
  const [licenses, setLicenses] = useState<License[]>([]);
  const [loadingProfile, setLoadingProfile] = useState(true);
  const [loadingUsers, setLoadingUsers] = useState(false);
  const [loadingLicenses, setLoadingLicenses] = useState(false);
  const [refreshKey, setRefreshKey] = useState(0);
  const [search, setSearch] = useState("");
  const [selectedUid, setSelectedUid] = useState<string | null>(null);

  // Announcements
  const [announcements, setAnnouncements] = useState<Announcement[]>([]);
  const [loadingAnn, setLoadingAnn] = useState(false);

  // Generate license modal
  const [genOpen, setGenOpen] = useState(false);
  const [genCount, setGenCount] = useState(1);
  const [genPlan, setGenPlan] = useState<"trial" | "permanent">("trial");
  const [genPrefix, setGenPrefix] = useState("LX");
  const [genTrialDays, setGenTrialDays] = useState(7);
  const [genKernelAccess, setGenKernelAccess] = useState(true);
  const [genLoading, setGenLoading] = useState(false);
  const [genResult, setGenResult] = useState<string[]>([]);

  const { toasts, show: onToast } = useToast();

  // Load reseller profile
  useEffect(() => {
    setLoadingProfile(true);
    fetchMyResellerProfile()
      .then(p => { setProfile(p); setLoadingProfile(false); })
      .catch(() => { setLoadingProfile(false); });
  }, [refreshKey]);

  // Load users when tab = users and profile loaded
  useEffect(() => {
    if (tab !== "users" || !profile) return;
    setLoadingUsers(true);
    fetchUsers(profile.permissions, profile.generatedCodes)
      .then(u => { setUsers(u); setLoadingUsers(false); })
      .catch(() => setLoadingUsers(false));
  }, [tab, profile, refreshKey]);

  // Load licenses when tab = licenses and profile loaded
  useEffect(() => {
    if (tab !== "licenses" || !profile) return;
    setLoadingLicenses(true);
    fetchMyLicenses(profile.generatedCodes)
      .then(l => { setLicenses(l); setLoadingLicenses(false); })
      .catch(() => setLoadingLicenses(false));
  }, [tab, profile, refreshKey]);

  // Load announcements when tab = announcements
  useEffect(() => {
    if (tab !== "announcements") return;
    setLoadingAnn(true);
    fetchAnnouncements()
      .then(a => { setAnnouncements(a); setLoadingAnn(false); })
      .catch(() => setLoadingAnn(false));
  }, [tab, refreshKey]);

  const doRefresh = useCallback(() => setRefreshKey(k => k + 1), []);

  // Filtered users
  const filteredUsers = useMemo(() => {
    if (!search) return users;
    const q = search.toLowerCase();
    return users.filter(u =>
      u.email.toLowerCase().includes(q) ||
      u.uid.toLowerCase().includes(q) ||
      u.licenseCode.toLowerCase().includes(q)
    );
  }, [users, search]);

  // Can generate
  const canGenTrial = profile?.permissions.licenseTypes.trial ?? false;
  const canGenPermanent = profile?.permissions.licenseTypes.permanent ?? false;
  const canKernelControl = profile?.permissions.licenseTypes.kernelAccessControl ?? false;
  const canGenerate = canGenTrial || canGenPermanent;
  const keysRemaining = profile ? profile.keyLimit - profile.keysUsed : 0;
  const maxTrialDays = profile?.permissions.maxTrialDays || 7;

  // Generate handler
  const handleGenerate = async () => {
    if (!profile) return;
    if (genCount < 1 || genCount > keysRemaining) {
      onToast(`You can generate max ${keysRemaining} more keys`, "error");
      return;
    }
    // Enforce max trial days from admin settings
    const clampedTrialDays = genPlan === "trial" ? Math.min(genTrialDays, maxTrialDays) : undefined;
    setGenLoading(true);
    try {
      const codes = await generateLicenses(
        profile.id, genCount, genPlan, genPrefix,
        profile.keysUsed, profile.generatedCodes,
        clampedTrialDays,
        genKernelAccess
      );
      setGenResult(codes);
      onToast(`Generated ${codes.length} license(s)`, "success");
      doRefresh();
    } catch (e: unknown) {
      const err = e as { message?: string };
      onToast("Failed: " + (err.message || ""), "error");
    }
    setGenLoading(false);
  };

  const copyText = (text: string) => {
    navigator.clipboard.writeText(text);
    onToast("Copied to clipboard", "info");
  };

  if (loadingProfile) {
    return (
      <div className="loading-screen">
        <div className="spinner" />
        <p>Loading reseller profile…</p>
      </div>
    );
  }

  if (!profile) {
    return (
      <div className="loading-screen">
        <p style={{ color: "#f87171", fontSize: 18 }}>Reseller profile not found.</p>
        <button className="btn-secondary" onClick={() => signOut(auth)}>Sign Out</button>
      </div>
    );
  }

  if (!profile.active) {
    return (
      <div className="loading-screen">
        <p style={{ color: "#f87171", fontSize: 18 }}>Your reseller account is suspended.</p>
        <button className="btn-secondary" onClick={() => signOut(auth)}>Sign Out</button>
      </div>
    );
  }

  const usagePercent = profile.keyLimit > 0 ? Math.min(100, Math.round((profile.keysUsed / profile.keyLimit) * 100)) : 0;

  const tabs: { id: Tab; label: string; icon: typeof LayoutDashboard }[] = [
    { id: "overview", label: "Overview", icon: LayoutDashboard },
    { id: "users", label: "My Clients", icon: Users },
    { id: "licenses", label: "Licenses", icon: Key },
    { id: "announcements", label: "Announcements", icon: Megaphone },
  ];

  return (
    <div className="dashboard">
      {/* Topbar */}
      <header className="topbar">
        <div className="topbar-left">
          <h1 className="topbar-title">LegitX <span className="v2">V2</span></h1>
          <span className="admin-badge">RESELLER</span>
        </div>
        <div className="topbar-right">
          <span className="admin-email">{email}</span>
          <button className="btn-logout" onClick={() => signOut(auth)}>
            <LogOut size={14} /> Sign Out
          </button>
        </div>
      </header>

      {/* Stats Bar */}
      <div className="stats-bar" style={{ gridTemplateColumns: "repeat(4, 1fr)" }}>
        <div className="stat-card">
          <div className="stat-info">
            <span className="stat-value">{profile.keysUsed}</span>
            <span className="stat-label">Keys Used</span>
          </div>
        </div>
        <div className="stat-card">
          <div className="stat-info">
            <span className="stat-value">{profile.keyLimit}</span>
            <span className="stat-label">Key Limit</span>
          </div>
        </div>
        <div className="stat-card">
          <div className="stat-info">
            <span className="stat-value">{keysRemaining}</span>
            <span className="stat-label">Remaining</span>
          </div>
        </div>
        <div className="stat-card">
          <div className="stat-info">
            <span className="stat-value">{profile.generatedCodes.length}</span>
            <span className="stat-label">Total Generated</span>
          </div>
        </div>
      </div>

      {/* Tabs */}
      <nav className="tab-bar">
        {tabs.map(t => (
          <button
            key={t.id}
            className={`tab-btn ${tab === t.id ? "active" : ""}`}
            onClick={() => { setTab(t.id); setSearch(""); }}
          >
            <t.icon size={15} />{t.label}
          </button>
        ))}
      </nav>

      {/* Tab Content */}
      <main className="tab-content">
        {/* ═══ OVERVIEW ═══ */}
        {tab === "overview" && (
          <div>
            <div className="section-header">
              <h2>Welcome, {profile.name}</h2>
              <button className="btn-secondary" onClick={doRefresh}><RefreshCw size={14} />Refresh</button>
            </div>

            {/* Profile info */}
            <div className="detail-section">
              <div className="detail-section-title">Your Profile</div>
              <div className="detail-grid">
                <div className="detail-row">
                  <span className="detail-label">Name</span>
                  <span className="detail-value">{profile.name}</span>
                </div>
                <div className="detail-row">
                  <span className="detail-label">Email</span>
                  <span className="detail-value">{profile.email}</span>
                </div>
                <div className="detail-row">
                  <span className="detail-label">Username</span>
                  <span className="detail-value">{profile.username}</span>
                </div>
                <div className="detail-row">
                  <span className="detail-label">Platform</span>
                  <span className="detail-value">{profile.platform}</span>
                </div>
                <div className="detail-row">
                  <span className="detail-label">Commission</span>
                  <span className="detail-value">{profile.commission}%</span>
                </div>
                <div className="detail-row">
                  <span className="detail-label">Last Activity</span>
                  <span className="detail-value">{ts(profile.lastActivity)}</span>
                </div>
              </div>
            </div>

            {/* Key Usage */}
            <div className="detail-section" style={{ marginTop: 16 }}>
              <div className="detail-section-title">Key Usage</div>
              <div className="reseller-progress-section">
                <label>Usage: {profile.keysUsed} / {profile.keyLimit}</label>
                <div className="key-usage-bar-lg">
                  <div
                    className={`key-usage-fill ${usagePercent > 90 ? "warn" : ""}`}
                    style={{ width: `${usagePercent}%` }}
                  />
                </div>
                <span className="key-usage-text-lg">{usagePercent}% used</span>
              </div>
            </div>
          </div>
        )}

        {/* ═══ USERS ═══ */}
        {tab === "users" && (
          <div>
            <div className="section-header">
              <h2>My Clients</h2>
              <div className="header-actions">
                <div className="search-box">
                  <Search size={14} />
                  <input placeholder="Search users…" value={search} onChange={e => setSearch(e.target.value)} />
                </div>
                <button className="btn-secondary" onClick={doRefresh}><RefreshCw size={14} />Refresh</button>
              </div>
            </div>

            <div className="table-wrapper">
              <table>
                <thead>
                  <tr>
                    {profile.permissions.visibility.email && <th>Email</th>}
                    <th>Status</th>
                    <th>Licensed</th>
                    {profile.permissions.visibility.licenseCode && <th>License Code</th>}
                    {profile.permissions.visibility.features && <th>Features</th>}
                    <th>Actions</th>
                  </tr>
                </thead>
                <tbody>
                  {loadingUsers ? (
                    <tr><td colSpan={10} className="loading-cell"><div className="spinner" /> Loading…</td></tr>
                  ) : filteredUsers.length === 0 ? (
                    <tr className="empty-row"><td colSpan={10}>No clients found — users will appear here after they redeem your license keys</td></tr>
                  ) : filteredUsers.map(u => {
                    const rowClass = u.status === "banned" ? "row-banned" : u.status === "suspended" ? "row-suspended" : u.status === "deactivated" ? "row-deactivated" : "";
                    return (
                      <tr key={u.uid} className={rowClass}>
                        {profile.permissions.visibility.email && <td className="primary-cell">{u.email}</td>}
                        <td>
                          <span className={`badge ${u.status === "active" ? "badge-green" : u.status === "deactivated" ? "badge-yellow" : u.status === "suspended" ? "badge-orange" : "badge-red"}`}>
                            <span className="badge-dot" />{u.status}
                          </span>
                        </td>
                        <td>
                          <span className={`badge ${u.licensed ? "badge-green" : "badge-gray"}`}>
                            <span className="badge-dot" />{u.licensed ? "Yes" : "No"}
                          </span>
                        </td>
                        {profile.permissions.visibility.licenseCode && <td className="mono">{u.licenseCode || "—"}</td>}
                        {profile.permissions.visibility.features && (
                          <td>
                            <div className="features-cell">
                              {(["bypass", "streamerMode", "formBypass", "stopNetwork"] as const).map(k => (
                                <span key={k} className={`feat-tag ${u.features[k] ? "feat-on" : "feat-off"}`}>
                                  {k.slice(0, 3).toUpperCase()}
                                </span>
                              ))}
                            </div>
                          </td>
                        )}
                        <td>
                          <button className="btn-icon" onClick={() => setSelectedUid(u.uid)} title="View Details">
                            <Eye size={14} />
                          </button>
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>
          </div>
        )}

        {/* ═══ LICENSES ═══ */}
        {tab === "licenses" && (
          <div>
            <div className="section-header">
              <h2>My Licenses</h2>
              <div className="header-actions">
                {canGenerate && (
                  <button className="btn-primary" onClick={() => { setGenOpen(true); setGenResult([]); }}>
                    <Plus size={14} />Generate Licenses
                  </button>
                )}
                <button className="btn-secondary" onClick={doRefresh}><RefreshCw size={14} />Refresh</button>
              </div>
            </div>

            <div className="table-wrapper">
              <table>
                <thead>
                  <tr>
                    <th>Code</th>
                    <th>Plan</th>
                    <th>Status</th>
                    <th>Claimed By</th>
                    <th>Created</th>
                  </tr>
                </thead>
                <tbody>
                  {loadingLicenses ? (
                    <tr><td colSpan={5} className="loading-cell"><div className="spinner" /> Loading…</td></tr>
                  ) : licenses.length === 0 ? (
                    <tr className="empty-row"><td colSpan={5}>No licenses generated yet</td></tr>
                  ) : licenses.map(l => (
                    <tr key={l.code}>
                      <td>
                        <span className="mono" style={{ cursor: "pointer" }} onClick={() => copyText(l.code)} title="Click to copy">
                          {l.code}
                        </span>
                      </td>
                      <td>
                        <span className={`badge ${l.plan === "permanent" ? "badge-blue" : "badge-yellow"}`}>
                          {l.plan}{l.plan === "trial" && l.trialDays ? ` (${l.trialDays}d)` : ""}
                        </span>
                      </td>
                      <td>
                        <span className={`badge ${l.active ? "badge-green" : "badge-red"}`}>
                          <span className="badge-dot" />{l.active ? "Active" : "Disabled"}
                        </span>
                      </td>
                      <td className="mono">{l.uid || "—"}</td>
                      <td>{ts(l.createdAt)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>
        )}

        {/* ═══ ANNOUNCEMENTS ═══ */}
        {tab === "announcements" && (
          <div>
            <div className="section-header">
              <h2>Announcements</h2>
              <button className="btn-secondary" onClick={doRefresh}><RefreshCw size={14} />Refresh</button>
            </div>

            {loadingAnn ? (
              <div className="ann-loading"><div className="spinner" /> Loading announcements…</div>
            ) : announcements.length === 0 ? (
              <div className="ann-empty"><Megaphone size={32} style={{ opacity: 0.3 }} /><p>No announcements yet</p></div>
            ) : (
              <div className="ann-grid">
                {[...announcements].sort((a, b) => {
                  if (a.pinned && !b.pinned) return -1;
                  if (!a.pinned && b.pinned) return 1;
                  const aT = a.createdAt?.toDate?.()?.getTime() ?? 0;
                  const bT = b.createdAt?.toDate?.()?.getTime() ?? 0;
                  return bT - aT;
                }).map(a => {
                  const TypeIcon = a.type === "urgent" ? AlertOctagon : a.type === "warning" ? AlertTriangle : a.type === "update" ? Zap : Info;
                  const borderColor = a.type === "urgent" ? "#ef4444" : a.type === "warning" ? "#eab308" : a.type === "update" ? "#3b82f6" : "#22c55e";
                  const bgColor = a.type === "urgent" ? "rgba(239,68,68,0.06)" : a.type === "warning" ? "rgba(234,179,8,0.06)" : a.type === "update" ? "rgba(59,130,246,0.06)" : "rgba(34,197,94,0.06)";
                  return (
                    <div key={a.id} className="ann-card" style={{ borderLeftColor: borderColor, background: bgColor }}>
                      <div className="ann-card-top">
                        <div className="ann-card-meta">
                          <span className="ann-type-badge" style={{ color: borderColor, background: `${borderColor}18`, borderColor: `${borderColor}33` }}>
                            <TypeIcon size={12} />{a.type.toUpperCase()}
                          </span>
                          {a.pinned && <span className="ann-pinned-badge"><Pin size={11} />Pinned</span>}
                        </div>
                      </div>
                      <h3 className="ann-card-title">{a.title}</h3>
                      <p className="ann-card-body">{a.body}</p>
                      <div className="ann-card-footer">
                        <span className="ann-card-date">{ts(a.createdAt)}</span>
                        {a.createdBy && <span className="ann-card-author">by {a.createdBy}</span>}
                      </div>
                    </div>
                  );
                })}
              </div>
            )}
          </div>
        )}
      </main>

      {/* Generate License Modal */}
      <Modal open={genOpen} onClose={() => { setGenOpen(false); setGenResult([]); }} title="Generate Licenses" size="sm">
        <p style={{ fontSize: 13, color: "#a1a1aa", marginBottom: 4 }}>
          Remaining quota: <strong style={{ color: "#fafafa" }}>{keysRemaining}</strong> keys
        </p>

        <div className="input-group">
          <label>Plan</label>
          <select value={genPlan} onChange={e => setGenPlan(e.target.value as "trial" | "permanent")}>
            {canGenTrial && <option value="trial">Trial</option>}
            {canGenPermanent && <option value="permanent">Permanent</option>}
          </select>
        </div>

        {genPlan === "trial" && (
          <div className="input-group">
            <label>Trial Days <span style={{ fontSize: 11, color: "#a1a1aa" }}>(max {maxTrialDays})</span></label>
            <input type="number" min={1} max={maxTrialDays} value={genTrialDays} onChange={e => setGenTrialDays(Math.min(+e.target.value, maxTrialDays))} />
            {genTrialDays > maxTrialDays && (
              <span style={{ fontSize: 11, color: "#f87171", marginTop: 2 }}>Cannot exceed {maxTrialDays} days</span>
            )}
          </div>
        )}

        <div className="input-group-row">
          <div className="input-group">
            <label>Prefix</label>
            <input value={genPrefix} onChange={e => setGenPrefix(e.target.value.toUpperCase())} maxLength={6} />
          </div>
          <div className="input-group">
            <label>Count</label>
            <input type="number" min={1} max={keysRemaining} value={genCount} onChange={e => setGenCount(+e.target.value)} />
          </div>
        </div>

        {canKernelControl && (
          <div className="input-group" style={{ display: "flex", alignItems: "center", gap: 10 }}>
            <label style={{ flex: 1, margin: 0 }}>Allow Kernel Access</label>
            <button
              type="button"
              onClick={() => setGenKernelAccess(v => !v)}
              style={{
                padding: "4px 14px", borderRadius: 6, border: "none", cursor: "pointer", fontWeight: 600, fontSize: 12,
                background: genKernelAccess ? "#22c55e" : "#ef4444", color: "#fff"
              }}
            >{genKernelAccess ? "ON" : "OFF"}</button>
          </div>
        )}

        <button className="btn-primary" onClick={handleGenerate} disabled={genLoading || genCount < 1} style={{ width: "100%", justifyContent: "center" }}>
          {genLoading ? <><div className="spinner" /> Generating…</> : <><Key size={14} /> Generate {genCount} Key(s)</>}
        </button>

        {genResult.length > 0 && (
          <div className="gen-result">
            <label>Generated Codes (click to copy):</label>
            <pre onClick={() => copyText(genResult.join("\n"))}>{genResult.join("\n")}</pre>
            <p className="hint"><Copy size={12} style={{ verticalAlign: -2 }} /> Click codes to copy all</p>
          </div>
        )}
      </Modal>

      {/* User Detail Modal */}
      {selectedUid && profile && (
        <UserDetailModal
          uid={selectedUid}
          perms={profile.permissions}
          onClose={() => setSelectedUid(null)}
          onToast={onToast}
          onRefresh={doRefresh}
        />
      )}

      <ToastContainer toasts={toasts} />
    </div>
  );
}
