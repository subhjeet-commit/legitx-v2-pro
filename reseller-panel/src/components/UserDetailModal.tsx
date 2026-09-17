import { useState, useEffect, useCallback } from "react";
import { RotateCcw, ShieldOff, ShieldCheck, Key, Power, PowerOff, Cpu, HardDrive } from "lucide-react";
import type { AppUser, ResellerProfile } from "../types";
import { fetchUserDetail, toggleFeature, deactivateUser, reactivateUser, suspendUser, unsuspendUser, revokeLicense, triggerWindowsReset, triggerFullAccountReset } from "../api";
import { Modal } from "./Modal";
import { ConfirmModal } from "./ConfirmModal";

interface Props {
  uid: string;
  perms: ResellerProfile["permissions"];
  onClose: () => void;
  onToast: (msg: string, type: "success" | "error" | "info") => void;
  onRefresh: () => void;
}

export function UserDetailModal({ uid, perms, onClose, onToast, onRefresh }: Props) {
  const [user, setUser] = useState<AppUser | null>(null);
  const [loading, setLoading] = useState(true);
  const [confirm, setConfirm] = useState<{ action: string; msg: string } | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const u = await fetchUserDetail(uid, perms);
      setUser(u);
    } catch (e: unknown) {
      const err = e as { message?: string };
      onToast("Failed to load user: " + (err.message || ""), "error");
    }
    setLoading(false);
  }, [uid, perms, onToast]);

  useEffect(() => { load(); }, [load]);

  const hasAnyFeaturePerm = perms.features.bypass || perms.features.streamerMode || perms.features.formBypass || perms.features.stopNetwork;
  const hasAnyActionPerm = perms.actions.deactivate || perms.actions.reactivate || perms.actions.suspend || perms.actions.unsuspend || perms.actions.revokeLicense;
  const hasAnyResetPerm = perms.resets.windowsReset || perms.resets.fullAccountReset;

  const handleToggleFeature = async (key: string, value: boolean) => {
    try {
      await toggleFeature(uid, key, value);
      onToast(`${key} → ${value ? "ON" : "OFF"}`, "success");
      await load();
    } catch { onToast("Failed to toggle feature", "error"); }
  };

  const handleConfirm = async () => {
    if (!confirm || !user) return;
    try {
      switch (confirm.action) {
        case "deactivate": await deactivateUser(uid); break;
        case "reactivate": await reactivateUser(uid); break;
        case "suspend": await suspendUser(uid); break;
        case "unsuspend": await unsuspendUser(uid); break;
        case "revoke": await revokeLicense(uid); break;
        case "windowsReset": await triggerWindowsReset(uid); break;
        case "fullReset": await triggerFullAccountReset(uid); break;
      }
      onToast(`Action "${confirm.action}" completed`, "success");
      setConfirm(null);
      await load();
      onRefresh();
    } catch { onToast("Action failed", "error"); setConfirm(null); }
  };

  if (loading || !user) {
    return (
      <Modal open onClose={onClose} title="User Details" size="lg">
        <div style={{ textAlign: "center", padding: 40, color: "#71717a" }}>
          <div className="spinner" /> Loading…
        </div>
      </Modal>
    );
  }

  const statusBadge = (s: string) => {
    const map: Record<string, string> = { active: "badge-green", deactivated: "badge-yellow", suspended: "badge-orange", banned: "badge-red" };
    return map[s] || "badge-gray";
  };

  return (
    <>
      <Modal open onClose={onClose} title={`User: ${user.email}`} size="lg">
        {/* ── Account Info ── */}
        <div className="detail-section">
          <div className="detail-section-title">Account Info</div>
          <div className="detail-grid">
            {perms.visibility.email && (
              <div className="detail-row">
                <span className="detail-label">Email</span>
                <span className="detail-value">{user.email}</span>
              </div>
            )}
            <div className="detail-row">
              <span className="detail-label">UID</span>
              <span className="detail-value mono" style={{ fontSize: 11 }}>{user.uid}</span>
            </div>
            <div className="detail-row">
              <span className="detail-label">Status</span>
              <span className={`badge ${statusBadge(user.status)}`}>
                <span className="badge-dot" />{user.status}
              </span>
            </div>
            <div className="detail-row">
              <span className="detail-label">Licensed</span>
              <span className={`badge ${user.licensed ? "badge-green" : "badge-red"}`}>
                <span className="badge-dot" />{user.licensed ? "Yes" : "No"}
              </span>
            </div>
            {perms.visibility.licenseCode && user.licenseCode && (
              <div className="detail-row">
                <span className="detail-label">License Code</span>
                <span className="detail-value mono">{user.licenseCode}</span>
              </div>
            )}
          </div>
        </div>

        {/* ── Features (if visible + any toggle perms) ── */}
        {perms.visibility.features && (
          <div className="detail-section feature-section">
            <h4>Feature Flags</h4>
            <div className="feature-list">
              {(["bypass", "streamerMode", "formBypass", "stopNetwork"] as const).map(key => (
                <div className="feature-row" key={key}>
                  <span className="feature-name">{key}</span>
                  {hasAnyFeaturePerm && perms.features[key] ? (
                    <label className="toggle-switch">
                      <input
                        type="checkbox"
                        checked={user.features[key]}
                        onChange={() => handleToggleFeature(key, !user.features[key])}
                      />
                      <span className="toggle-slider" />
                    </label>
                  ) : (
                    <span className={`badge ${user.features[key] ? "badge-green" : "badge-gray"}`}>
                      <span className="badge-dot" />{user.features[key] ? "ON" : "OFF"}
                    </span>
                  )}
                </div>
              ))}
            </div>
          </div>
        )}

        {/* ── HWID (if visible) ── */}
        {perms.visibility.hwid && (
          <div className="detail-section hwid-section">
            <h4><Cpu size={14} style={{ marginRight: 6, verticalAlign: -2 }} />Hardware ID</h4>
            <div className="hwid-grid">
              {user.hwid.permanentHash && (
                <div className="hwid-item">
                  <div className="hwid-label">Permanent Hash</div>
                  <div className="hwid-value">{user.hwid.permanentHash}</div>
                </div>
              )}
              {user.hwid.windowsHash && (
                <div className="hwid-item">
                  <div className="hwid-label">Windows Hash</div>
                  <div className="hwid-value">{user.hwid.windowsHash}</div>
                </div>
              )}
              {user.hwid.computerName && (
                <div className="hwid-item">
                  <div className="hwid-label">Computer Name</div>
                  <div className="hwid-value">{user.hwid.computerName}</div>
                </div>
              )}
              {user.hwid.lastLogin && (
                <div className="hwid-item">
                  <div className="hwid-label">Last Login</div>
                  <div className="hwid-value">{user.hwid.lastLogin}</div>
                </div>
              )}
              {!user.hwid.permanentHash && !user.hwid.windowsHash && (
                <div className="hwid-item" style={{ gridColumn: "1/-1", textAlign: "center", color: "#71717a" }}>
                  No HWID registered
                </div>
              )}
            </div>

            {hasAnyResetPerm && (
              <div className="hwid-actions">
                {perms.resets.windowsReset && (
                  <button className="btn-secondary" onClick={() => setConfirm({ action: "windowsReset", msg: "Trigger a <strong>Windows Reset</strong> flag for this user?<br/>They will re-register their Windows HWID on next launch." })}>
                    <RotateCcw size={14} />Windows Reset
                  </button>
                )}
                {perms.resets.fullAccountReset && (
                  <button className="btn-danger" onClick={() => setConfirm({ action: "fullReset", msg: "Perform a <strong>Full Account Reset</strong>?<br/>This wipes HWID, features, and license." })}>
                    <HardDrive size={14} />Full Account Reset
                  </button>
                )}
              </div>
            )}
          </div>
        )}

        {/* ── Actions ── */}
        {hasAnyActionPerm && (
          <div className="detail-section">
            <div className="detail-section-title">Actions</div>
            <div className="admin-actions-grid">
              {perms.actions.deactivate && user.status === "active" && (
                <button className="btn-warning" onClick={() => setConfirm({ action: "deactivate", msg: `Deactivate <strong>${user.email}</strong>?` })}>
                  <PowerOff size={14} />Deactivate
                </button>
              )}
              {perms.actions.reactivate && user.status === "deactivated" && (
                <button className="btn-secondary" onClick={() => setConfirm({ action: "reactivate", msg: `Reactivate <strong>${user.email}</strong>?` })}>
                  <Power size={14} />Reactivate
                </button>
              )}
              {perms.actions.suspend && (user.status === "active" || user.status === "deactivated") && (
                <button className="btn-danger" onClick={() => setConfirm({ action: "suspend", msg: `Suspend <strong>${user.email}</strong>?<br/>This revokes their license.` })}>
                  <ShieldOff size={14} />Suspend
                </button>
              )}
              {perms.actions.unsuspend && user.status === "suspended" && (
                <button className="btn-secondary" onClick={() => setConfirm({ action: "unsuspend", msg: `Unsuspend <strong>${user.email}</strong>?` })}>
                  <ShieldCheck size={14} />Unsuspend
                </button>
              )}
              {perms.actions.revokeLicense && user.licensed && (
                <button className="btn-danger" onClick={() => setConfirm({ action: "revoke", msg: `Revoke license from <strong>${user.email}</strong>?` })}>
                  <Key size={14} />Revoke License
                </button>
              )}
            </div>
          </div>
        )}
      </Modal>

      <ConfirmModal
        open={!!confirm}
        title="Confirm Action"
        message={confirm?.msg || ""}
        onConfirm={handleConfirm}
        onCancel={() => setConfirm(null)}
      />
    </>
  );
}
