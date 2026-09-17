import { useState } from "react";
import { Key, RotateCcw, Plus, Edit3, ShieldCheck, ShieldOff, Trash2, Copy, Save, ToggleLeft, ToggleRight } from "lucide-react";
import type { Reseller, ResellerPermissions } from "../types";
import { DEFAULT_RESELLER_PERMISSIONS } from "../types";
import { addKeysToReseller, resetResellerKeysUsed, updateResellerNotes, toggleResellerStatus, updateResellerKeyLimit, deleteReseller, updateResellerPermissions } from "../api";
import { Modal } from "./Modal";
import { ConfirmModal } from "./ConfirmModal";

interface Props {
  reseller: Reseller;
  onClose: () => void;
  onToast: (msg: string, type: "success" | "error" | "info") => void;
  onRefresh: () => void;
}

export function ResellerDetailModal({ reseller, onClose, onToast, onRefresh }: Props) {
  const [r, setR] = useState(reseller);
  const [addKeysOpen, setAddKeysOpen] = useState(false);
  const [addKeysVal, setAddKeysVal] = useState(5);
  const [editLimitOpen, setEditLimitOpen] = useState(false);
  const [editLimitVal, setEditLimitVal] = useState(r.keyLimit || 0);
  const [notesOpen, setNotesOpen] = useState(false);
  const [notesVal, setNotesVal] = useState(r.notes || "");
  const [confirm, setConfirm] = useState<{ action: string; msg: string } | null>(null);
  const [perms, setPerms] = useState<ResellerPermissions>(
    r.permissions ? structuredClone(r.permissions) : structuredClone(DEFAULT_RESELLER_PERMISSIONS)
  );
  const [permsDirty, setPermsDirty] = useState(false);
  const [permsSaving, setPermsSaving] = useState(false);

  const pct = r.keyLimit > 0 ? Math.min(((r.keysUsed || 0) / r.keyLimit) * 100, 100) : 0;

  const handleAddKeys = async () => {
    try {
      await addKeysToReseller(r.id, addKeysVal);
      setR({ ...r, keyLimit: (r.keyLimit || 0) + addKeysVal });
      onToast(`+${addKeysVal} keys added`, "success");
      setAddKeysOpen(false);
      onRefresh();
    } catch (e: any) { onToast("Failed: " + e.message, "error"); }
  };

  const handleEditLimit = async () => {
    try {
      await updateResellerKeyLimit(r.id, editLimitVal);
      setR({ ...r, keyLimit: editLimitVal });
      onToast("Key limit updated", "success");
      setEditLimitOpen(false);
      onRefresh();
    } catch (e: any) { onToast("Failed: " + e.message, "error"); }
  };

  const handleResetCounter = async () => {
    try {
      await resetResellerKeysUsed(r.id);
      setR({ ...r, keysUsed: 0 });
      onToast("Counter reset to 0", "success");
      onRefresh();
    } catch (e: any) { onToast("Failed: " + e.message, "error"); }
  };

  const handleSaveNotes = async () => {
    try {
      await updateResellerNotes(r.id, notesVal);
      setR({ ...r, notes: notesVal });
      onToast("Notes updated", "success");
      setNotesOpen(false);
    } catch (e: any) { onToast("Failed: " + e.message, "error"); }
  };

  const handleConfirm = async () => {
    if (!confirm) return;
    try {
      if (confirm.action === "toggle") {
        await toggleResellerStatus(r.id, r.active);
        setR({ ...r, active: !r.active });
        onToast(r.active ? "Reseller suspended" : "Reseller activated", "success");
      } else if (confirm.action === "delete") {
        await deleteReseller(r.id, r.name);
        onToast("Reseller deleted", "success");
        onClose();
      }
      setConfirm(null);
      onRefresh();
    } catch (e: any) { onToast("Failed: " + e.message, "error"); }
  };

  const copyText = (text: string) => { navigator.clipboard.writeText(text); onToast("Copied!", "success"); };

  type ObjectPermKeys = "features" | "actions" | "licenseTypes" | "visibility" | "resets";

  const updatePerm = <C extends ObjectPermKeys>(category: C, key: keyof ResellerPermissions[C], val: boolean) => {
    setPerms(prev => ({ ...prev, [category]: { ...prev[category], [key]: val } }));
    setPermsDirty(true);
  };

  const toggleAllInCategory = <C extends ObjectPermKeys>(category: C, val: boolean) => {
    setPerms(prev => {
      const updated = { ...prev[category] };
      for (const k of Object.keys(updated) as (keyof typeof updated)[]) {
        (updated as any)[k] = val;
      }
      return { ...prev, [category]: updated };
    });
    setPermsDirty(true);
  };

  const handleSavePermissions = async () => {
    setPermsSaving(true);
    try {
      await updateResellerPermissions(r.id, perms, r.name);
      setR({ ...r, permissions: perms });
      setPermsDirty(false);
      onToast("Permissions saved!", "success");
      onRefresh();
    } catch (e: any) { onToast("Failed to save: " + e.message, "error"); }
    setPermsSaving(false);
  };

  const grantAll = () => {
    setPerms({
      features: { bypass: true, streamerMode: true, formBypass: true, stopNetwork: true },
      actions: { deactivate: true, reactivate: true, suspend: true, unsuspend: true, revokeLicense: true },
      licenseTypes: { trial: true, permanent: true, kernelAccessControl: true },
      maxTrialDays: perms.maxTrialDays || 30,
      visibility: { hwid: true, licenseCode: true, email: true, features: true },
      resets: { windowsReset: true, fullAccountReset: true },
    });
    setPermsDirty(true);
  };

  const revokeAll = () => {
    setPerms(structuredClone(DEFAULT_RESELLER_PERMISSIONS));
    setPermsDirty(true);
  };

  return (
    <>
      <Modal open={true} onClose={onClose} title={r.name} size="lg">
        {/* Stats cards */}
        <div className="detail-stats">
          <div className="detail-stat-card">
            <span className="stat-label">Keys Generated</span>
            <span className="stat-value">{r.generatedCodes?.length || 0}</span>
          </div>
          <div className="detail-stat-card">
            <span className="stat-label">Keys Remaining</span>
            <span className="stat-value">{Math.max((r.keyLimit || 0) - (r.keysUsed || 0), 0)}</span>
          </div>
          <div className="detail-stat-card">
            <span className="stat-label">Commission</span>
            <span className="stat-value">{r.commission || 0}%</span>
          </div>
          <div className="detail-stat-card">
            <span className="stat-label">Status</span>
            <span className="stat-value" style={{ color: r.active ? "#4ade80" : "#f87171" }}>
              {r.active ? "Active" : "Suspended"}
            </span>
          </div>
        </div>

        {/* Key usage progress */}
        <div className="reseller-progress-section">
          <label>Key Usage</label>
          <div className="key-usage-bar-lg">
            <div className={`key-usage-fill ${pct > 80 ? "warn" : ""}`} style={{ width: `${pct}%` }} />
          </div>
          <span className="key-usage-text-lg">{r.keysUsed || 0} / {r.keyLimit || 0} keys used ({pct.toFixed(0)}%)</span>
        </div>

        {/* Info grid */}
        <div className="detail-grid">
          <div className="detail-row">
            <span className="detail-label">Email</span>
            <span className="detail-value mono clickable" onClick={() => copyText(r.email)}>{r.email} <Copy size={12} /></span>
          </div>
          <div className="detail-row">
            <span className="detail-label">Platform</span>
            <span className="detail-value">{r.platform || "Telegram"}</span>
          </div>
          <div className="detail-row">
            <span className="detail-label">Username</span>
            <span className="detail-value">{r.username ? `@${r.username}` : "—"}</span>
          </div>
          <div className="detail-row">
            <span className="detail-label">Joined</span>
            <span className="detail-value mono">{r.createdAt?.toDate ? r.createdAt.toDate().toLocaleString() : "—"}</span>
          </div>
          <div className="detail-row">
            <span className="detail-label">Notes</span>
            <span className="detail-value">{r.notes || "—"}</span>
          </div>
        </div>

        {/* Generated codes */}
        {r.generatedCodes && r.generatedCodes.length > 0 && (
          <div className="reseller-codes-section">
            <label>Recent Generated Codes</label>
            <div className="reseller-codes-list">
              {r.generatedCodes.slice(-10).reverse().map(c => (
                <span key={c} className="reseller-code mono clickable" onClick={() => copyText(c)}>{c}</span>
              ))}
            </div>
          </div>
        )}

        {/* Permissions Management */}
        <div className="permissions-section permissions-section-detail">
          <div className="permissions-header">
            <h4 className="permissions-title">Role & Permissions</h4>
            <div className="permissions-header-actions">
              <button className="btn-xs btn-outline-green" onClick={grantAll}><ToggleRight size={12} /> Grant All</button>
              <button className="btn-xs btn-outline-red" onClick={revokeAll}><ToggleLeft size={12} /> Revoke All</button>
            </div>
          </div>

          {/* Feature Toggles */}
          <div className="perm-category">
            <div className="perm-cat-header">
              <span className="perm-cat-label">Feature Toggles <span className="perm-hint">Which features can this reseller enable/disable</span></span>
              <div className="perm-cat-actions">
                <button className="btn-xs" onClick={() => toggleAllInCategory("features", true)}>All On</button>
                <button className="btn-xs" onClick={() => toggleAllInCategory("features", false)}>All Off</button>
              </div>
            </div>
            <div className="perm-grid">
              {([
                ["bypass", "Bypass"],
                ["streamerMode", "Streamer Mode"],
                ["formBypass", "Form Bypass"],
                ["stopNetwork", "Stop Network"],
              ] as const).map(([k, label]) => (
                <label key={k} className="perm-toggle">
                  <input type="checkbox" checked={perms.features[k]} onChange={e => updatePerm("features", k, e.target.checked)} />
                  <span className="perm-label">{label}</span>
                </label>
              ))}
            </div>
          </div>

          {/* User Actions */}
          <div className="perm-category">
            <div className="perm-cat-header">
              <span className="perm-cat-label">User Actions <span className="perm-hint">What actions can this reseller perform</span></span>
              <div className="perm-cat-actions">
                <button className="btn-xs" onClick={() => toggleAllInCategory("actions", true)}>All On</button>
                <button className="btn-xs" onClick={() => toggleAllInCategory("actions", false)}>All Off</button>
              </div>
            </div>
            <div className="perm-grid">
              {([
                ["deactivate", "Deactivate"],
                ["reactivate", "Reactivate"],
                ["suspend", "Suspend"],
                ["unsuspend", "Unsuspend"],
                ["revokeLicense", "Revoke License"],
              ] as const).map(([k, label]) => (
                <label key={k} className="perm-toggle">
                  <input type="checkbox" checked={perms.actions[k]} onChange={e => updatePerm("actions", k, e.target.checked)} />
                  <span className="perm-label">{label}</span>
                </label>
              ))}
            </div>
          </div>

          {/* License Types */}
          <div className="perm-category">
            <div className="perm-cat-header">
              <span className="perm-cat-label">License Types <span className="perm-hint">What types of keys can this reseller generate</span></span>
              <div className="perm-cat-actions">
                <button className="btn-xs" onClick={() => toggleAllInCategory("licenseTypes", true)}>All On</button>
                <button className="btn-xs" onClick={() => toggleAllInCategory("licenseTypes", false)}>All Off</button>
              </div>
            </div>
            <div className="perm-grid">
              {([
                ["trial", "Trial"],
                ["permanent", "Permanent"],
                ["kernelAccessControl", "Kernel Access Control"],
              ] as const).map(([k, label]) => (
                <label key={k} className="perm-toggle">
                  <input type="checkbox" checked={perms.licenseTypes[k]} onChange={e => updatePerm("licenseTypes", k, e.target.checked)} />
                  <span className="perm-label">{label}</span>
                </label>
              ))}
            </div>
          </div>

          {/* Max Trial Days */}
          {perms.licenseTypes.trial && (
            <div className="perm-category">
              <div className="perm-cat-header">
                <span className="perm-cat-label">Max Trial Days <span className="perm-hint">Maximum number of trial days this reseller can assign per key</span></span>
              </div>
              <div style={{ padding: "4px 0", display: "flex", alignItems: "center", gap: 8 }}>
                <input type="number" min={1} max={365} value={perms.maxTrialDays || 7}
                  onChange={e => { setPerms(p => ({ ...p, maxTrialDays: Math.max(1, parseInt(e.target.value) || 7) })); setPermsDirty(true); }}
                  style={{ width: 100 }} />
                <span style={{ fontSize: 12, color: "#a1a1aa" }}>days</span>
              </div>
            </div>
          )}

          {/* Data Visibility */}
          <div className="perm-category">
            <div className="perm-cat-header">
              <span className="perm-cat-label">Data Visibility <span className="perm-hint">What user data can this reseller see</span></span>
              <div className="perm-cat-actions">
                <button className="btn-xs" onClick={() => toggleAllInCategory("visibility", true)}>All On</button>
                <button className="btn-xs" onClick={() => toggleAllInCategory("visibility", false)}>All Off</button>
              </div>
            </div>
            <div className="perm-grid">
              {([
                ["hwid", "Hardware ID"],
                ["licenseCode", "License Code"],
                ["email", "Email"],
                ["features", "Features"],
              ] as const).map(([k, label]) => (
                <label key={k} className="perm-toggle">
                  <input type="checkbox" checked={perms.visibility[k]} onChange={e => updatePerm("visibility", k, e.target.checked)} />
                  <span className="perm-label">{label}</span>
                </label>
              ))}
            </div>
          </div>

          {/* Reset Capabilities */}
          <div className="perm-category">
            <div className="perm-cat-header">
              <span className="perm-cat-label">Reset Capabilities <span className="perm-hint">Which resets can this reseller perform</span></span>
              <div className="perm-cat-actions">
                <button className="btn-xs" onClick={() => toggleAllInCategory("resets", true)}>All On</button>
                <button className="btn-xs" onClick={() => toggleAllInCategory("resets", false)}>All Off</button>
              </div>
            </div>
            <div className="perm-grid">
              {([
                ["windowsReset", "Windows Reset"],
                ["fullAccountReset", "Full Account Reset"],
              ] as const).map(([k, label]) => (
                <label key={k} className="perm-toggle">
                  <input type="checkbox" checked={perms.resets[k]} onChange={e => updatePerm("resets", k, e.target.checked)} />
                  <span className="perm-label">{label}</span>
                </label>
              ))}
            </div>
          </div>

          {/* Save button */}
          {permsDirty && (
            <button className="btn-primary permissions-save-btn" onClick={handleSavePermissions} disabled={permsSaving}>
              {permsSaving ? <span className="spinner" /> : <><Save size={14} /> Save Permissions</>}
            </button>
          )}
        </div>

        {/* Quick Actions */}
        <div className="quick-actions">
          <h4>Quick Actions</h4>
          <div className="quick-actions-grid">
            <button className="btn-secondary" onClick={() => setAddKeysOpen(true)}><Plus size={14} /> Add Keys</button>
            <button className="btn-secondary" onClick={() => { setEditLimitVal(r.keyLimit || 0); setEditLimitOpen(true); }}><Key size={14} /> Edit Limit</button>
            <button className="btn-secondary" onClick={handleResetCounter}><RotateCcw size={14} /> Reset Counter</button>
            <button className="btn-secondary" onClick={() => { setNotesVal(r.notes || ""); setNotesOpen(true); }}><Edit3 size={14} /> Edit Notes</button>
            <button
              className={r.active ? "btn-danger" : "btn-primary"}
              onClick={() => setConfirm({ action: "toggle", msg: r.active ? `Suspend <strong>${r.name}</strong>?` : `Activate <strong>${r.name}</strong>?` })}
            >
              {r.active ? <><ShieldOff size={14} /> Suspend</> : <><ShieldCheck size={14} /> Activate</>}
            </button>
            <button
              className="btn-danger"
              onClick={() => setConfirm({ action: "delete", msg: `Permanently delete <strong>${r.name}</strong>?<br><strong style="color:#f87171;">Cannot be undone!</strong>` })}
            >
              <Trash2 size={14} /> Delete
            </button>
          </div>
        </div>
      </Modal>

      {/* Add Keys Mini Modal */}
      <Modal open={addKeysOpen} onClose={() => setAddKeysOpen(false)} title="Add Extra Keys" size="xs">
        <div className="input-group">
          <label>Number of keys to add</label>
          <input type="number" value={addKeysVal} min={1} onChange={e => setAddKeysVal(parseInt(e.target.value) || 1)} />
        </div>
        <button className="btn-primary" onClick={handleAddKeys}>Add Keys</button>
      </Modal>

      {/* Edit Limit Mini Modal */}
      <Modal open={editLimitOpen} onClose={() => setEditLimitOpen(false)} title="Edit Key Limit" size="xs">
        <div className="input-group">
          <label>New key limit</label>
          <input type="number" value={editLimitVal} min={1} onChange={e => setEditLimitVal(parseInt(e.target.value) || 1)} />
        </div>
        <button className="btn-primary" onClick={handleEditLimit}>Update Limit</button>
      </Modal>

      {/* Edit Notes Mini Modal */}
      <Modal open={notesOpen} onClose={() => setNotesOpen(false)} title="Edit Notes" size="sm">
        <div className="input-group">
          <label>Notes</label>
          <textarea rows={4} value={notesVal} onChange={e => setNotesVal(e.target.value)} />
        </div>
        <button className="btn-primary" onClick={handleSaveNotes}>Save Notes</button>
      </Modal>

      <ConfirmModal
        open={!!confirm}
        title={confirm?.action === "delete" ? "Delete Reseller" : r.active ? "Suspend Reseller" : "Activate Reseller"}
        message={confirm?.msg || ""}
        onConfirm={handleConfirm}
        onCancel={() => setConfirm(null)}
      />
    </>
  );
}
