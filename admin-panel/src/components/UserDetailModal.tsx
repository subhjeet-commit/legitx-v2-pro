import { useState, useEffect, useCallback } from "react";
import { Modal } from "./Modal";
import type { AppUser, ActivityLog } from "../types";
import {
  fetchUserDetail, toggleFeature, setAllFeatures, triggerHwidReset,
  fullAccountReset, revokeLicense, deleteUser, deactivateUser, reactivateUser,
  suspendUser, unsuspendUser, systemBanUser, unbanUser, fetchUserLogs, clearResetCooldown, forceLogoutUser, ts,
} from "../api";
import { ConfirmModal } from "./ConfirmModal";

interface Props {
  uid: string;
  onClose: () => void;
  onToast: (msg: string, type: "success" | "error" | "info") => void;
}

type ActionType = "wipe" | "fullAccountReset" | "revoke" | "delete" | "deactivate" | "reactivate" | "suspend" | "unsuspend" | "ban" | "unban" | "clearCooldown" | "forceLogout";

const CONFIRM_TITLES: Record<ActionType, string> = {
  wipe: "Windows Reset (HWID)",
  fullAccountReset: "Full Account Reset",
  revoke: "Revoke License",
  delete: "Delete User",
  deactivate: "Deactivate User",
  reactivate: "Reactivate User",
  suspend: "Suspend User",
  unsuspend: "Unsuspend User",
  ban: "System Ban (HWID)",
  unban: "Remove System Ban",
  clearCooldown: "Clear Reset Cooldown",
  forceLogout: "Force Logout User",
};

const CONFIRM_MSGS: Record<ActionType, (e: string) => string> = {
  wipe: () => "This will trigger a Windows HWID reset. Use this when the user has reinstalled Windows or reset their PC but is on the same hardware.",
  fullAccountReset: e => `This will perform a <strong>complete account reset</strong> for <strong>${e}</strong>:<br/><br/>• Wipe all hardware ID data<br/>• Revoke their license<br/>• Disable all features<br/><br/>The user will need a new license code and will re-register on next login. Use this when the user wants to start completely fresh or move to a different account/PC.`,
  revoke: e => `Remove license from <strong>${e}</strong>?`,
  delete: e => `Permanently delete <strong>${e}</strong> and all their data? This cannot be undone.`,
  deactivate: e => `Deactivate <strong>${e}</strong>? Their features will be disabled.`,
  reactivate: e => `Reactivate <strong>${e}</strong>? Their features will be restored.`,
  suspend: e => `Permanently suspend <strong>${e}</strong>? License will be revoked and all features disabled.`,
  unsuspend: e => `Unsuspend <strong>${e}</strong>?`,
  ban: e => `System ban <strong>${e}</strong>? Their hardware IDs will be permanently blocked — they will <strong>never</strong> be able to use the software on their current PC.`,
  unban: e => `Remove the hardware ban from <strong>${e}</strong>?`,
  clearCooldown: e => `Clear the self-service reset cooldown for <strong>${e}</strong>? They will immediately be able to request another Windows reset from their user panel.`,
  forceLogout: e => `Force logout <strong>${e}</strong> from the software? They will be disconnected immediately (within 90 seconds) and will need to sign in again.`,
};

export function UserDetailModal({ uid, onClose, onToast }: Props) {
  const [user, setUser] = useState<AppUser | null>(null);
  const [loading, setLoading] = useState(true);
  const [confirmAction, setConfirmAction] = useState<ActionType | null>(null);
  const [userLogs, setUserLogs] = useState<ActivityLog[]>([]);
  const [logsLoading, setLogsLoading] = useState(false);
  const [logsExpanded, setLogsExpanded] = useState(false);

  const loadLogs = useCallback(async () => {
    setLogsLoading(true);
    try {
      const logs = await fetchUserLogs(uid);
      setUserLogs(logs);
    } catch { /* ignore */ }
    setLogsLoading(false);
  }, [uid]);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const u = await fetchUserDetail(uid);
      setUser(u);
    } catch (e: any) {
      onToast("Failed: " + e.message, "error");
    }
    setLoading(false);
    // Refresh logs too
    loadLogs();
  }, [uid, onToast, loadLogs]);

  useEffect(() => {
    let ignore = false;
    fetchUserDetail(uid).then(u => {
      if (!ignore) { setUser(u); setLoading(false); }
    }).catch(e => {
      if (!ignore) { onToast("Failed: " + e.message, "error"); setLoading(false); }
    });
    // Also fetch user-specific logs
    fetchUserLogs(uid).then(logs => {
      if (!ignore) setUserLogs(logs);
    }).catch(() => {});
    return () => { ignore = true; };
  }, [uid, onToast]);

  const handleToggle = async (key: string, value: boolean) => {
    try {
      await toggleFeature(uid, key, value);
      onToast(`${key} → ${value ? "ON" : "OFF"}`, "success");
      load();
    } catch (e: any) { onToast("Failed: " + e.message, "error"); }
  };

  const handleSetAll = async (enabled: boolean) => {
    try {
      await setAllFeatures(uid, enabled);
      onToast(`All features ${enabled ? "enabled" : "disabled"}`, "success");
      load();
    } catch (e: any) { onToast("Failed: " + e.message, "error"); }
  };

  const handleConfirmAction = async () => {
    if (!confirmAction || !user) return;
    try {
      switch (confirmAction) {
        case "wipe":        await triggerHwidReset(uid, "windows"); break;
        case "fullAccountReset": await fullAccountReset(uid, user.email); break;
        case "revoke":      await revokeLicense(uid, user.email); break;
        case "delete":      await deleteUser(uid, user.email); onClose(); return;
        case "deactivate":  await deactivateUser(uid, user.email); break;
        case "reactivate":  await reactivateUser(uid, user.email); break;
        case "suspend":     await suspendUser(uid, user.email); break;
        case "unsuspend":   await unsuspendUser(uid, user.email); break;
        case "ban":         await systemBanUser(uid, user.email); break;
        case "unban":       await unbanUser(uid, user.email); break;
        case "clearCooldown": await clearResetCooldown(uid, user.email); break;
        case "forceLogout": await forceLogoutUser(uid, user.email); break;
      }
      onToast(`${CONFIRM_TITLES[confirmAction]} — success`, "success");
      setConfirmAction(null);
      load();
    } catch (e: any) { onToast("Failed: " + e.message, "error"); }
  };

  const features: { key: string; label: string; desc: string }[] = [
    { key: "bypass", label: "Bypass", desc: "Allow string bypass feature" },
    { key: "streamerMode", label: "Streamer Mode", desc: "Enable streamer overlay mode" },
    { key: "formBypass", label: "Form Bypass", desc: "Allow form bypass feature" },
    { key: "stopNetwork", label: "Stop Network", desc: "Enable network stop feature" },
  ];

  const statusLabel = (s: string) => {
    switch (s) {
      case "deactivated": return <span className="badge badge-yellow"><span className="badge-dot" />Deactivated</span>;
      case "suspended":   return <span className="badge badge-orange"><span className="badge-dot" />Suspended</span>;
      case "banned":      return <span className="badge badge-red"><span className="badge-dot" />System Banned</span>;
      default:            return <span className="badge badge-green"><span className="badge-dot" />Active</span>;
    }
  };

  const formatLogAction = (action: string): string => {
    return action.replace(/_/g, " ").replace(/\b\w/g, c => c.toUpperCase());
  };

  const getLogBadgeClass = (action: string): string => {
    if (action.includes("ban") || action.includes("delete") || action.includes("suspend")) return "log-badge-red";
    if (action.includes("reset") || action.includes("wipe") || action.includes("revoke")) return "log-badge-yellow";
    if (action.includes("reactivate") || action.includes("unsuspend") || action.includes("unban")) return "log-badge-green";
    if (action.includes("toggle") || action.includes("set_all")) return "log-badge-blue";
    return "log-badge-gray";
  };

  return (
    <>
      <Modal open={true} onClose={onClose} title={user?.email || uid} size="md">
        {loading || !user ? (
          <div style={{ textAlign: "center", padding: 40 }}><span className="spinner" /> Loading...</div>
        ) : (
          <>
            {/* Account */}
            <div className="detail-section">
              <div className="detail-section-title">Account</div>
              <div className="detail-grid">
                <div className="detail-row"><span className="detail-label">Email</span><span className="detail-value">{user.email}</span></div>
                <div className="detail-row">
                  <span className="detail-label">UID</span>
                  <span className="detail-value mono clickable" onClick={() => { navigator.clipboard.writeText(uid); onToast("Copied!", "success"); }}>{uid}</span>
                </div>
                <div className="detail-row">
                  <span className="detail-label">Status</span>
                  <span className="detail-value">{statusLabel(user.status)}</span>
                </div>
                <div className="detail-row">
                  <span className="detail-label">Licensed</span>
                  <span className="detail-value">
                    {user.licensed
                      ? <span className="badge badge-green"><span className="badge-dot" />Yes</span>
                      : <span className="badge badge-gray">No</span>}
                  </span>
                </div>
                <div className="detail-row"><span className="detail-label">License Code</span><span className="detail-value mono">{user.licenseCode || "—"}</span></div>
                {user.status === "banned" && user.bannedHwids && user.bannedHwids.length > 0 && (
                  <div className="detail-row">
                    <span className="detail-label">Banned HWIDs</span>
                    <span className="detail-value mono" style={{ color: "var(--red-500)" }}>
                      {user.bannedHwids.map(h => h.substring(0, 16) + "…").join(", ")}
                    </span>
                  </div>
                )}
              </div>
            </div>

            {/* HWID */}
            <div className="detail-section">
              <div className="detail-section-title">Hardware ID</div>
              <div className="detail-grid">
                <div className="detail-row"><span className="detail-label">Permanent Hash</span><span className="detail-value mono">{user.hwid?.permanentHash || "Not registered"}</span></div>
                <div className="detail-row"><span className="detail-label">Windows Hash</span><span className="detail-value mono">{user.hwid?.windowsHash || "Not registered"}</span></div>
                <div className="detail-row"><span className="detail-label">Computer Name</span><span className="detail-value">{user.hwid?.computerName || "—"}</span></div>
                <div className="detail-row"><span className="detail-label">Registered At</span><span className="detail-value mono">{user.hwid?.registeredAt || "—"}</span></div>
                <div className="detail-row"><span className="detail-label">Last Login</span><span className="detail-value mono">{user.hwid?.lastLogin || "—"}</span></div>
                <div className="detail-row">
                  <span className="detail-label">Windows Reset</span>
                  <span className="detail-value">{user.hwid?.windowsReset ? <span className="badge badge-yellow">Pending</span> : <span className="badge badge-gray">Normal</span>}</span>
                </div>
                <div className="detail-row">
                  <span className="detail-label">Full Reset</span>
                  <span className="detail-value">{user.hwid?.fullReset ? <span className="badge badge-yellow">Pending</span> : <span className="badge badge-gray">Normal</span>}</span>
                </div>
                {user.hwid?.lastSelfReset && (
                  <div className="detail-row">
                    <span className="detail-label">Self-Reset Cooldown</span>
                    <span className="detail-value">
                      {(() => {
                        const lastReset = new Date(user.hwid.lastSelfReset);
                        const nextAvailable = new Date(lastReset.getTime() + 30 * 24 * 60 * 60 * 1000);
                        const now = new Date();
                        if (now >= nextAvailable) return <span className="badge badge-green">Available</span>;
                        const daysLeft = Math.ceil((nextAvailable.getTime() - now.getTime()) / (1000 * 60 * 60 * 24));
                        return <span className="badge badge-orange">{daysLeft}d left — until {nextAvailable.toLocaleDateString()}</span>;
                      })()}
                    </span>
                  </div>
                )}
              </div>
              <div className="hwid-actions">
                <button className="btn-secondary" onClick={() => setConfirmAction("wipe")}>Windows Reset</button>
                <button className="btn-danger" onClick={() => setConfirmAction("fullAccountReset")}>Full Account Reset</button>
                {user.hwid?.lastSelfReset && (() => {
                  const lastReset = new Date(user.hwid.lastSelfReset!);
                  const nextAvailable = new Date(lastReset.getTime() + 30 * 24 * 60 * 60 * 1000);
                  return new Date() < nextAvailable;
                })() && (
                  <button className="btn-warning" onClick={() => setConfirmAction("clearCooldown")}>Clear Reset Cooldown</button>
                )}
              </div>
            </div>

            {/* Features */}
            <div className="detail-section">
              <div className="detail-section-title">Feature Flags</div>
              <div className="feature-list">
                {features.map(f => (
                  <div className="feature-row" key={f.key}>
                    <span className="feature-name">{f.label}</span>
                    <label className="toggle-switch">
                      <input
                        type="checkbox"
                        checked={(user.features as any)[f.key] !== false}
                        onChange={e => handleToggle(f.key, e.target.checked)}
                      />
                      <span className="toggle-slider" />
                    </label>
                  </div>
                ))}
              </div>
              <div className="feature-bulk-actions">
                <button className="btn-secondary" onClick={() => handleSetAll(true)}>Enable All</button>
                <button className="btn-secondary" onClick={() => handleSetAll(false)}>Disable All</button>
              </div>
            </div>

            {/* User Activity Logs */}
            <div className="detail-section">
              <div className="detail-section-title" style={{ display: "flex", alignItems: "center", justifyContent: "space-between" }}>
                <span>User Activity</span>
                <button
                  className="btn-xs"
                  onClick={() => { setLogsExpanded(!logsExpanded); if (!logsExpanded && userLogs.length === 0) loadLogs(); }}
                >
                  {logsExpanded ? "Hide" : `Show (${userLogs.length})`}
                </button>
              </div>
              {logsExpanded && (
                <div className="user-logs-section">
                  {logsLoading ? (
                    <div style={{ textAlign: "center", padding: 16 }}><span className="spinner" /> Loading logs…</div>
                  ) : userLogs.length === 0 ? (
                    <div className="user-logs-empty">No activity recorded for this user yet.</div>
                  ) : (
                    <div className="user-logs-list">
                      {userLogs.map(log => (
                        <div className="user-log-item" key={log.id}>
                          <div className="user-log-action">
                            <span className={`user-log-badge ${getLogBadgeClass(log.action)}`}>{formatLogAction(log.action)}</span>
                            <span className="user-log-time">{ts(log.timestamp)}</span>
                          </div>
                          {log.details && <div className="user-log-details">{log.details}</div>}
                          <div className="user-log-admin">by {log.adminEmail || "system"}</div>
                        </div>
                      ))}
                    </div>
                  )}
                </div>
              )}
            </div>

            {/* Admin Actions */}
            <div className="detail-section">
              <div className="detail-section-title">Admin Actions</div>
              <div className="admin-actions-grid">
                {/* Force Logout — always available regardless of status */}
                <button className="btn-warning" onClick={() => setConfirmAction("forceLogout")}>Force Logout</button>

                {user.status === "active" && (
                  <>
                    <button className="btn-warning" onClick={() => setConfirmAction("deactivate")}>Deactivate User</button>
                    <button className="btn-danger" onClick={() => setConfirmAction("suspend")}>Suspend Permanently</button>
                    <button className="btn-ban" onClick={() => setConfirmAction("ban")}>System Ban (HWID)</button>
                  </>
                )}
                {user.status === "deactivated" && (
                  <button className="btn-secondary" onClick={() => setConfirmAction("reactivate")}>Reactivate User</button>
                )}
                {user.status === "suspended" && (
                  <button className="btn-secondary" onClick={() => setConfirmAction("unsuspend")}>Unsuspend User</button>
                )}
                {user.status === "banned" && (
                  <button className="btn-secondary" onClick={() => setConfirmAction("unban")}>Remove System Ban</button>
                )}
                {user.licensed && user.status !== "banned" && user.status !== "suspended" && (
                  <button className="btn-danger" onClick={() => setConfirmAction("revoke")}>Revoke License</button>
                )}
                <button className="btn-danger" onClick={() => setConfirmAction("delete")}>Delete User</button>
              </div>
            </div>
          </>
        )}
      </Modal>

      {confirmAction && user && (
        <ConfirmModal
          open={true}
          title={CONFIRM_TITLES[confirmAction]}
          message={CONFIRM_MSGS[confirmAction](user.email)}
          onConfirm={handleConfirmAction}
          onCancel={() => setConfirmAction(null)}
        />
      )}
    </>
  );
}
