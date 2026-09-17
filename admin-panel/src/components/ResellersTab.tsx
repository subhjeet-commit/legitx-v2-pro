import { useState, useEffect, useCallback, useMemo } from "react";
import { Search, RefreshCw, Plus, ShieldCheck, ShieldOff, Trash2, Eye } from "lucide-react";
import type { Reseller, ResellerPermissions } from "../types";
import { DEFAULT_RESELLER_PERMISSIONS } from "../types";
import { fetchResellers, addReseller, toggleResellerStatus, deleteReseller } from "../api";
import { Modal } from "./Modal";
import { ConfirmModal } from "./ConfirmModal";
import { ResellerDetailModal } from "./ResellerDetailModal";

interface Props {
  onToast: (msg: string, type: "success" | "error" | "info") => void;
  refreshKey: number;
  onRefresh: () => void;
}

export function ResellersTab({ onToast, refreshKey, onRefresh }: Props) {
  const [resellers, setResellers] = useState<Reseller[]>([]);
  const [search, setSearch] = useState("");
  const [loading, setLoading] = useState(true);
  const [addOpen, setAddOpen] = useState(false);
  const [addLoading, setAddLoading] = useState(false);
  const [addForm, setAddForm] = useState({ name: "", email: "", username: "", platform: "Telegram", keyLimit: 10, commission: 0, notes: "" });
  const [addPerms, setAddPerms] = useState<ResellerPermissions>(structuredClone(DEFAULT_RESELLER_PERMISSIONS));
  const [selectedReseller, setSelectedReseller] = useState<Reseller | null>(null);
  const [confirm, setConfirm] = useState<{ action: string; id: string; msg: string } | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const list = await fetchResellers();
      setResellers(list);
    } catch (e: any) { onToast("Failed to load resellers: " + e.message, "error"); }
    setLoading(false);
  }, [onToast]);

  useEffect(() => {
    let ignore = false;
    fetchResellers().then(list => { if (!ignore) { setResellers(list); setLoading(false); } }).catch(() => setLoading(false));
    return () => { ignore = true; };
  }, [refreshKey]);

  const filtered = useMemo(() => {
    const q = search.toLowerCase();
    return resellers.filter(r =>
      r.name.toLowerCase().includes(q) || r.email.toLowerCase().includes(q) || (r.username || "").toLowerCase().includes(q)
    );
  }, [search, resellers]);

  const handleAdd = async () => {
    if (!addForm.name || !addForm.email) { onToast("Name and email required", "error"); return; }
    setAddLoading(true);
    try {
      await addReseller({
        name: addForm.name,
        email: addForm.email,
        username: addForm.username || "",
        platform: addForm.platform || "Telegram",
        keyLimit: addForm.keyLimit || 10,
        commission: addForm.commission || 0,
        allowedPlan: "permanent",
        notes: addForm.notes || "",
        permissions: addPerms,
      });
      onToast("Reseller added successfully", "success");
      setAddOpen(false);
      setAddForm({ name: "", email: "", username: "", platform: "Telegram", keyLimit: 10, commission: 0, notes: "" });
      setAddPerms(structuredClone(DEFAULT_RESELLER_PERMISSIONS));
      load();
      onRefresh();
    } catch (e: any) { onToast("Failed: " + e.message, "error"); }
    setAddLoading(false);
  };

  const handleConfirm = async () => {
    if (!confirm) return;
    try {
      const r = resellers.find(x => x.id === confirm.id);
      if (confirm.action === "delete") {
        await deleteReseller(confirm.id, r?.name || "");
        onToast("Reseller deleted", "success");
      } else if (confirm.action === "suspend") {
        await toggleResellerStatus(confirm.id, true);
        onToast("Reseller suspended", "success");
      } else if (confirm.action === "activate") {
        await toggleResellerStatus(confirm.id, false);
        onToast("Reseller activated", "success");
      }
      setConfirm(null);
      load();
      onRefresh();
    } catch (e: any) { onToast("Failed: " + e.message, "error"); }
  };

  const pct = (used: number, limit: number) => limit > 0 ? Math.min((used / limit) * 100, 100) : 0;

  return (
    <>
      <div className="section-header">
        <h2>Resellers</h2>
        <div className="header-actions">
          <div className="search-box">
            <Search size={16} color="#71717a" />
            <input type="text" placeholder="Search resellers..." value={search} onChange={e => setSearch(e.target.value)} />
          </div>
          <button className="btn-primary btn-sm" onClick={() => setAddOpen(true)}>
            <Plus size={14} /> Add Reseller
          </button>
          <button className="btn-secondary" onClick={load}>
            <RefreshCw size={14} /> Refresh
          </button>
        </div>
      </div>

      <div className="table-wrapper">
        <table>
          <thead>
            <tr>
              <th>Name</th>
              <th>Email</th>
              <th>Username</th>
              <th>Status</th>
              <th>Permissions</th>
              <th>Key Usage</th>
              <th>Commission</th>
              <th>Joined</th>
              <th>Actions</th>
            </tr>
          </thead>
          <tbody>
            {loading ? (
              <tr><td colSpan={9} className="loading-cell"><span className="spinner" /> Loading resellers...</td></tr>
            ) : filtered.length === 0 ? (
              <tr className="empty-row"><td colSpan={9}>No resellers found</td></tr>
            ) : filtered.map(r => {
              const p = r.permissions || DEFAULT_RESELLER_PERMISSIONS;
              const permCount =
                Object.values(p.features).filter(Boolean).length +
                Object.values(p.actions).filter(Boolean).length +
                Object.values(p.licenseTypes).filter(Boolean).length +
                Object.values(p.visibility).filter(Boolean).length +
                Object.values(p.resets).filter(Boolean).length;
              const permTotal = 4 + 5 + 2 + 4 + 2; // 17
              return (
              <tr key={r.id}>
                <td className="primary-cell">{r.name}</td>
                <td>{r.email}</td>
                <td>{r.username ? `@${r.username} (${r.platform || "Telegram"})` : "—"}</td>
                <td>
                  {r.active
                    ? <span className="badge badge-green"><span className="badge-dot" />Active</span>
                    : <span className="badge badge-red"><span className="badge-dot" />Suspended</span>}
                </td>
                <td>
                  <span className={`badge ${permCount === 0 ? "badge-red" : permCount < permTotal ? "badge-yellow" : "badge-green"}`}>
                    {permCount}/{permTotal}
                  </span>
                </td>
                <td>
                  <div className="key-usage">
                    <div className="key-usage-bar">
                      <div
                        className={`key-usage-fill ${pct(r.keysUsed || 0, r.keyLimit || 0) > 80 ? "warn" : ""}`}
                        style={{ width: `${pct(r.keysUsed || 0, r.keyLimit || 0)}%` }}
                      />
                    </div>
                    <span className="key-usage-text">{r.keysUsed || 0}/{r.keyLimit || 0}</span>
                  </div>
                </td>
                <td>{r.commission || 0}%</td>
                <td className="mono" style={{ fontSize: "11px" }}>
                  {r.createdAt?.toDate ? r.createdAt.toDate().toLocaleDateString() : "—"}
                </td>
                <td>
                  <div className="row-actions">
                    <button className="btn-icon" title="View Details" onClick={() => setSelectedReseller(r)}><Eye size={14} /></button>
                    {r.active
                      ? <button className="btn-icon danger" title="Suspend" onClick={() => setConfirm({ action: "suspend", id: r.id, msg: `Suspend reseller <strong>${r.name}</strong>?` })}><ShieldOff size={14} /></button>
                      : <button className="btn-icon" title="Activate" onClick={() => setConfirm({ action: "activate", id: r.id, msg: `Activate reseller <strong>${r.name}</strong>?` })}><ShieldCheck size={14} /></button>}
                    <button className="btn-icon danger" title="Delete" onClick={() => setConfirm({ action: "delete", id: r.id, msg: `Permanently delete <strong>${r.name}</strong>?<br><br><strong style="color:#f87171;">All data will be lost!</strong>` })}><Trash2 size={14} /></button>
                  </div>
                </td>
              </tr>
              );
            })}
          </tbody>
        </table>
      </div>

      {/* Add Reseller Modal */}
      <Modal open={addOpen} onClose={() => setAddOpen(false)} title="Add New Reseller" size="sm">
        <div className="input-group">
          <label>Full Name *</label>
          <input type="text" value={addForm.name} onChange={e => setAddForm({ ...addForm, name: e.target.value })} placeholder="John Doe" />
        </div>
        <div className="input-group">
          <label>Email *</label>
          <input type="email" value={addForm.email} onChange={e => setAddForm({ ...addForm, email: e.target.value })} placeholder="john@example.com" />
        </div>
        <div className="input-group-row">
          <div className="input-group">
            <label>Username</label>
            <input type="text" value={addForm.username} onChange={e => setAddForm({ ...addForm, username: e.target.value })} placeholder="johndoe" />
          </div>
          <div className="input-group">
            <label>Platform</label>
            <select value={addForm.platform} onChange={e => setAddForm({ ...addForm, platform: e.target.value })}>
              <option>Telegram</option>
              <option>Discord</option>
              <option>WhatsApp</option>
              <option>Other</option>
            </select>
          </div>
        </div>
        <div className="input-group-row">
          <div className="input-group">
            <label>Key Limit</label>
            <input type="number" value={addForm.keyLimit} min={1} onChange={e => setAddForm({ ...addForm, keyLimit: parseInt(e.target.value) || 10 })} />
          </div>
          <div className="input-group">
            <label>Commission (%)</label>
            <input type="number" value={addForm.commission} min={0} max={100} onChange={e => setAddForm({ ...addForm, commission: parseInt(e.target.value) || 0 })} />
          </div>
        </div>
        <div className="input-group">
          <label>Notes</label>
          <textarea rows={3} value={addForm.notes} onChange={e => setAddForm({ ...addForm, notes: e.target.value })} placeholder="Optional notes..." />
        </div>

        {/* ── Permissions ── */}
        <div className="permissions-section">
          <h4 className="permissions-title">Role & Permissions</h4>

          <div className="perm-category">
            <span className="perm-cat-label">Feature Toggles <span className="perm-hint">Which features can this reseller enable/disable for users</span></span>
            <div className="perm-grid">
              {(["bypass", "streamerMode", "formBypass", "stopNetwork"] as const).map(k => (
                <label key={k} className="perm-toggle">
                  <input type="checkbox" checked={addPerms.features[k]} onChange={e => setAddPerms({ ...addPerms, features: { ...addPerms.features, [k]: e.target.checked } })} />
                  <span className="perm-label">{k === "bypass" ? "Bypass" : k === "streamerMode" ? "Streamer Mode" : k === "formBypass" ? "Form Bypass" : "Stop Network"}</span>
                </label>
              ))}
            </div>
          </div>

          <div className="perm-category">
            <span className="perm-cat-label">User Actions <span className="perm-hint">What actions can this reseller perform on users</span></span>
            <div className="perm-grid">
              {(["deactivate", "reactivate", "suspend", "unsuspend", "revokeLicense"] as const).map(k => (
                <label key={k} className="perm-toggle">
                  <input type="checkbox" checked={addPerms.actions[k]} onChange={e => setAddPerms({ ...addPerms, actions: { ...addPerms.actions, [k]: e.target.checked } })} />
                  <span className="perm-label">{k === "revokeLicense" ? "Revoke License" : k.charAt(0).toUpperCase() + k.slice(1)}</span>
                </label>
              ))}
            </div>
          </div>

          <div className="perm-category">
            <span className="perm-cat-label">License Types <span className="perm-hint">What types of keys can this reseller generate</span></span>
            <div className="perm-grid">
              {(["trial", "permanent"] as const).map(k => (
                <label key={k} className="perm-toggle">
                  <input type="checkbox" checked={addPerms.licenseTypes[k]} onChange={e => setAddPerms({ ...addPerms, licenseTypes: { ...addPerms.licenseTypes, [k]: e.target.checked } })} />
                  <span className="perm-label">{k.charAt(0).toUpperCase() + k.slice(1)}</span>
                </label>
              ))}
              <label className="perm-toggle">
                <input type="checkbox" checked={addPerms.licenseTypes.kernelAccessControl} onChange={e => setAddPerms({ ...addPerms, licenseTypes: { ...addPerms.licenseTypes, kernelAccessControl: e.target.checked } })} />
                <span className="perm-label">Kernel Access Control</span>
              </label>
            </div>
          </div>

          {addPerms.licenseTypes.trial && (
            <div className="perm-category">
              <span className="perm-cat-label">Max Trial Days <span className="perm-hint">Maximum number of trial days this reseller can assign per key</span></span>
              <div style={{ padding: "4px 0" }}>
                <input type="number" min={1} max={365} value={addPerms.maxTrialDays} onChange={e => setAddPerms({ ...addPerms, maxTrialDays: Math.max(1, parseInt(e.target.value) || 7) })} style={{ width: 100 }} />
                <span style={{ marginLeft: 8, fontSize: 12, color: "#a1a1aa" }}>days</span>
              </div>
            </div>
          )}

          <div className="perm-category">
            <span className="perm-cat-label">Data Visibility <span className="perm-hint">What user data can this reseller see</span></span>
            <div className="perm-grid">
              {(["hwid", "licenseCode", "email", "features"] as const).map(k => (
                <label key={k} className="perm-toggle">
                  <input type="checkbox" checked={addPerms.visibility[k]} onChange={e => setAddPerms({ ...addPerms, visibility: { ...addPerms.visibility, [k]: e.target.checked } })} />
                  <span className="perm-label">{k === "hwid" ? "Hardware ID" : k === "licenseCode" ? "License Code" : k === "email" ? "Email" : "Features"}</span>
                </label>
              ))}
            </div>
          </div>

          <div className="perm-category">
            <span className="perm-cat-label">Reset Capabilities <span className="perm-hint">Which resets can this reseller perform</span></span>
            <div className="perm-grid">
              {(["windowsReset", "fullAccountReset"] as const).map(k => (
                <label key={k} className="perm-toggle">
                  <input type="checkbox" checked={addPerms.resets[k]} onChange={e => setAddPerms({ ...addPerms, resets: { ...addPerms.resets, [k]: e.target.checked } })} />
                  <span className="perm-label">{k === "windowsReset" ? "Windows Reset" : "Full Account Reset"}</span>
                </label>
              ))}
            </div>
          </div>
        </div>

        <button className="btn-primary" onClick={handleAdd} disabled={addLoading}>
          {addLoading ? <span className="spinner" /> : "Add Reseller"}
        </button>
      </Modal>

      {/* Detail Modal */}
      {selectedReseller && (
        <ResellerDetailModal
          reseller={selectedReseller}
          onClose={() => setSelectedReseller(null)}
          onToast={onToast}
          onRefresh={() => { load(); onRefresh(); }}
        />
      )}

      <ConfirmModal
        open={!!confirm}
        title={confirm?.action === "delete" ? "Delete Reseller" : confirm?.action === "suspend" ? "Suspend Reseller" : "Activate Reseller"}
        message={confirm?.msg || ""}
        onConfirm={handleConfirm}
        onCancel={() => setConfirm(null)}
      />
    </>
  );
}
