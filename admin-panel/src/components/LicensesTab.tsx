import { useState, useEffect, useCallback, useMemo } from "react";
import { Search, RefreshCw, Plus, XCircle, Check, RotateCcw, Trash2, ShieldCheck, ShieldOff } from "lucide-react";
import type { License } from "../types";
import { fetchLicenses, generateLicenses, deactivateLicense, reactivateLicense, revokeCodeClaim, deleteLicense, toggleKernelAccess } from "../api";
import { Modal } from "./Modal";
import { ConfirmModal } from "./ConfirmModal";

interface Props {
  onToast: (msg: string, type: "success" | "error" | "info") => void;
  refreshKey: number;
  onRefresh: () => void;
}

const tsDisplay = (t: any): string => {
  if (!t) return "—";
  if (t.toDate) return t.toDate().toLocaleString();
  return String(t);
};

export function LicensesTab({ onToast, refreshKey, onRefresh }: Props) {
  const [licenses, setLicenses] = useState<License[]>([]);
  const [search, setSearch] = useState("");
  const [loading, setLoading] = useState(true);
  const [genOpen, setGenOpen] = useState(false);
  const [genCount, setGenCount] = useState(1);
  const [genPlan, setGenPlan] = useState<"trial" | "permanent">("permanent");
  const [genPrefix, setGenPrefix] = useState("LEGITX");
  const [genTrialDays, setGenTrialDays] = useState(7);
  const [genKernelAccess, setGenKernelAccess] = useState(true);
  const [genCodes, setGenCodes] = useState<string[]>([]);
  const [genLoading, setGenLoading] = useState(false);
  const [confirm, setConfirm] = useState<{ action: string; code: string; msg: string } | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const list = await fetchLicenses();
      setLicenses(list);
    } catch (e: any) {
      onToast("Failed to load licenses: " + e.message, "error");
    }
    setLoading(false);
  }, [onToast]);

  useEffect(() => {
    let ignore = false;
    fetchLicenses().then(list => { if (!ignore) { setLicenses(list); setLoading(false); } }).catch(() => setLoading(false));
    return () => { ignore = true; };
  }, [refreshKey]);

  const filtered = useMemo(() => {
    const q = search.toLowerCase();
    return licenses.filter(l =>
      l.code.toLowerCase().includes(q) || (l.uid || "").toLowerCase().includes(q) || (l.plan || "").toLowerCase().includes(q) || (l.generatedByName || "").toLowerCase().includes(q)
    );
  }, [search, licenses]);

  const handleGenerate = async () => {
    setGenLoading(true);
    try {
      const codes = await generateLicenses(
        Math.min(genCount, 50),
        genPlan,
        genPrefix.toUpperCase() || "LEGITX",
        genPlan === "trial" ? genTrialDays : undefined,
        genKernelAccess
      );
      setGenCodes(codes);
      onToast(`${codes.length} code(s) generated`, "success");
      load();
      onRefresh();
    } catch (e: any) { onToast("Failed: " + e.message, "error"); }
    setGenLoading(false);
  };

  const handleConfirm = async () => {
    if (!confirm) return;
    try {
      switch (confirm.action) {
        case "deactivate": await deactivateLicense(confirm.code); onToast("Code deactivated", "success"); break;
        case "revoke": await revokeCodeClaim(confirm.code); onToast("Code claim revoked", "success"); break;
        case "delete": await deleteLicense(confirm.code); onToast("Code deleted", "success"); break;
      }
      setConfirm(null);
      load();
      onRefresh();
    } catch (e: any) { onToast("Failed: " + e.message, "error"); }
  };

  const handleReactivate = async (code: string) => {
    try {
      await reactivateLicense(code);
      onToast("Code reactivated", "success");
      load();
      onRefresh();
    } catch (e: any) { onToast("Failed: " + e.message, "error"); }
  };

  const copyText = (text: string) => { navigator.clipboard.writeText(text); onToast("Copied!", "success"); };

  return (
    <>
      <div className="section-header">
        <h2>License Codes</h2>
        <div className="header-actions">
          <div className="search-box">
            <Search size={16} color="#71717a" />
            <input type="text" placeholder="Search codes..." value={search} onChange={e => setSearch(e.target.value)} />
          </div>
          <button className="btn-primary btn-sm" onClick={() => { setGenCodes([]); setGenOpen(true); }}>
            <Plus size={14} /> Generate Codes
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
              <th>Code</th>
              <th>Status</th>
              <th>Plan</th>
              <th>Kernel</th>
              <th>Created By</th>
              <th>Claimed By</th>
              <th>Created</th>
              <th>Redeemed</th>
              <th>Actions</th>
            </tr>
          </thead>
          <tbody>
            {loading ? (
              <tr><td colSpan={9} className="loading-cell"><span className="spinner" /> Loading licenses...</td></tr>
            ) : filtered.length === 0 ? (
              <tr className="empty-row"><td colSpan={9}>No license codes found</td></tr>
            ) : filtered.map(l => {
              const status = !l.active
                ? <span className="badge badge-red"><span className="badge-dot" />Disabled</span>
                : l.uid
                  ? <span className="badge badge-yellow"><span className="badge-dot" />Redeemed</span>
                  : <span className="badge badge-green"><span className="badge-dot" />Available</span>;

              const planBadge = l.plan === "trial"
                ? <span className="badge badge-orange">{l.trialDays ? `Trial ${l.trialDays}d` : "Trial"}</span>
                : <span className="badge badge-blue">Permanent</span>;

              return (
                <tr key={l.code}>
                  <td className="mono" style={{ cursor: "pointer" }} onClick={() => copyText(l.code)} title="Click to copy">{l.code}</td>
                  <td>{status}</td>
                  <td>{planBadge}</td>
                  <td>
                    {l.kernelAccess
                      ? <span className="badge badge-green"><ShieldCheck size={12} style={{ marginRight: 4 }} />Allowed</span>
                      : <span className="badge badge-red"><ShieldOff size={12} style={{ marginRight: 4 }} />Denied</span>}
                  </td>
                  <td>
                    <span className={`badge ${l.generatedByName === "Admin" ? "badge-blue" : "badge-purple"}`}>
                      {l.generatedByName || "Admin"}
                    </span>
                  </td>
                  <td>{l.uid ? <span className="mono uid-cell" onClick={() => copyText(l.uid)} title={l.uid}>{l.uid.substring(0, 12)}…</span> : "—"}</td>
                  <td className="mono" style={{ fontSize: "11px" }}>{tsDisplay(l.createdAt)}</td>
                  <td className="mono" style={{ fontSize: "11px" }}>{tsDisplay(l.redeemedAt)}</td>
                  <td>
                    <div className="row-actions">
                      {l.kernelAccess
                        ? <button className="btn-icon danger" title="Revoke Kernel Access" onClick={async () => { await toggleKernelAccess(l.code, false); onToast("Kernel access revoked", "success"); load(); }}><ShieldOff size={14} /></button>
                        : <button className="btn-icon" title="Grant Kernel Access" onClick={async () => { await toggleKernelAccess(l.code, true); onToast("Kernel access granted", "success"); load(); }}><ShieldCheck size={14} /></button>}
                      {l.active
                        ? <button className="btn-icon danger" title="Deactivate" onClick={() => setConfirm({ action: "deactivate", code: l.code, msg: `Deactivate <strong>${l.code}</strong>?` })}><XCircle size={14} /></button>
                        : <button className="btn-icon" title="Reactivate" onClick={() => handleReactivate(l.code)}><Check size={14} /></button>}
                      {l.uid && <button className="btn-icon" title="Revoke Claim" onClick={() => setConfirm({ action: "revoke", code: l.code, msg: `Un-claim <strong>${l.code}</strong>?` })}><RotateCcw size={14} /></button>}
                      <button className="btn-icon danger" title="Delete" onClick={() => setConfirm({ action: "delete", code: l.code, msg: `Permanently delete <strong>${l.code}</strong>?<br><br><strong style="color:#f87171;">This cannot be undone!</strong>` })}><Trash2 size={14} /></button>
                    </div>
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>

      {/* Generate Modal */}
      <Modal open={genOpen} onClose={() => setGenOpen(false)} title="Generate License Codes" size="sm">
        <div className="input-group">
          <label>Number of codes</label>
          <input type="number" value={genCount} min={1} max={50} onChange={e => setGenCount(parseInt(e.target.value) || 1)} />
        </div>
        <div className="input-group">
          <label>Plan</label>
          <select value={genPlan} onChange={e => setGenPlan(e.target.value as "trial" | "permanent")}>
            <option value="permanent">Permanent</option>
            <option value="trial">Trial</option>
          </select>
        </div>
        {genPlan === "trial" && (
          <div className="input-group">
            <label>Trial Duration (days)</label>
            <input type="number" value={genTrialDays} min={1} max={365} onChange={e => setGenTrialDays(parseInt(e.target.value) || 7)} />
          </div>
        )}
        <div className="input-group">
          <label>Prefix</label>
          <input type="text" value={genPrefix} maxLength={10} style={{ textTransform: "uppercase" }} onChange={e => setGenPrefix(e.target.value)} />
        </div>
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
        <button className="btn-primary" onClick={handleGenerate} disabled={genLoading}>
          {genLoading ? <span className="spinner" /> : "Generate"}
        </button>
        {genCodes.length > 0 && (
          <div className="gen-result">
            <label>Generated Codes (click to copy all):</label>
            <pre onClick={() => { navigator.clipboard.writeText(genCodes.join("\n")); onToast("All codes copied!", "success"); }}>
              {genCodes.join("\n")}
            </pre>
            <p className="hint">Click the codes to copy to clipboard</p>
          </div>
        )}
      </Modal>

      <ConfirmModal
        open={!!confirm}
        title={confirm?.action === "delete" ? "Delete License Code" : confirm?.action === "deactivate" ? "Deactivate Code" : "Revoke Code Claim"}
        message={confirm?.msg || ""}
        onConfirm={handleConfirm}
        onCancel={() => setConfirm(null)}
      />
    </>
  );
}
