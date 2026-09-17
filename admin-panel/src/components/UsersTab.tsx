import { useState, useEffect, useCallback, useMemo } from "react";
import { Eye, XCircle, RefreshCw, Search, Trash2, ShieldOff, ShieldBan, Ban } from "lucide-react";
import type { AppUser } from "../types";
import {
  fetchUsers, revokeLicense, deleteUser,
  deactivateUser, reactivateUser,
  suspendUser, unsuspendUser,
  systemBanUser, unbanUser,
} from "../api";
import { UserDetailModal } from "./UserDetailModal";
import { ConfirmModal } from "./ConfirmModal";

interface Props {
  onToast: (msg: string, type: "success" | "error" | "info") => void;
  refreshKey: number;
  onRefresh: () => void;
}

type ConfirmAction = {
  type: "revoke" | "delete" | "deactivate" | "reactivate" | "suspend" | "unsuspend" | "ban" | "unban";
  uid: string;
  email: string;
};

const ACTION_CONFIG: Record<ConfirmAction["type"], { title: string; msg: (e: string) => string; btnLabel: string }> = {
  revoke:     { title: "Revoke License",   msg: e => `Remove license from <strong>${e}</strong>?`, btnLabel: "Revoke" },
  delete:     { title: "Delete User",      msg: e => `Permanently delete <strong>${e}</strong>? This cannot be undone.`, btnLabel: "Delete" },
  deactivate: { title: "Deactivate User",  msg: e => `Deactivate <strong>${e}</strong>? Their features will be disabled.`, btnLabel: "Deactivate" },
  reactivate: { title: "Reactivate User",  msg: e => `Reactivate <strong>${e}</strong>? Their features will be restored.`, btnLabel: "Reactivate" },
  suspend:    { title: "Suspend User",     msg: e => `Permanently suspend <strong>${e}</strong>? Their license will be revoked and all features disabled.`, btnLabel: "Suspend" },
  unsuspend:  { title: "Unsuspend User",   msg: e => `Unsuspend <strong>${e}</strong>? Their features will be restored.`, btnLabel: "Unsuspend" },
  ban:        { title: "System Ban (HWID)", msg: e => `System ban <strong>${e}</strong>? Their hardware IDs will be permanently blocked. They will never be able to use the software on their current PC.`, btnLabel: "Ban" },
  unban:      { title: "Remove System Ban", msg: e => `Remove the hardware ban from <strong>${e}</strong>? Their HWID blocks will be lifted.`, btnLabel: "Unban" },
};

export function UsersTab({ onToast, refreshKey, onRefresh }: Props) {
  const [users, setUsers] = useState<AppUser[]>([]);
  const [search, setSearch] = useState("");
  const [loading, setLoading] = useState(true);
  const [selectedUid, setSelectedUid] = useState<string | null>(null);
  const [confirm, setConfirm] = useState<ConfirmAction | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const list = await fetchUsers();
      setUsers(list);
    } catch (e: any) {
      onToast("Failed to load users: " + e.message, "error");
    }
    setLoading(false);
  }, [onToast]);

  useEffect(() => {
    let ignore = false;
    fetchUsers().then(list => { if (!ignore) { setUsers(list); setLoading(false); } }).catch(() => setLoading(false));
    return () => { ignore = true; };
  }, [refreshKey]);

  const filtered = useMemo(() => {
    const q = search.toLowerCase();
    return users.filter(u =>
      u.email.toLowerCase().includes(q) || u.uid.toLowerCase().includes(q) || (u.resellerName || "").toLowerCase().includes(q)
    );
  }, [search, users]);

  const abbrev: Record<string, string> = { bypass: "BYP", streamerMode: "STR", formBypass: "FRM", stopNetwork: "NET" };

  const handleConfirm = async () => {
    if (!confirm) return;
    const { type, uid, email } = confirm;
    try {
      switch (type) {
        case "revoke":     await revokeLicense(uid, email); break;
        case "delete":     await deleteUser(uid, email); break;
        case "deactivate": await deactivateUser(uid, email); break;
        case "reactivate": await reactivateUser(uid, email); break;
        case "suspend":    await suspendUser(uid, email); break;
        case "unsuspend":  await unsuspendUser(uid, email); break;
        case "ban":        await systemBanUser(uid, email); break;
        case "unban":      await unbanUser(uid, email); break;
      }
      onToast(`${ACTION_CONFIG[type].title} — success`, "success");
      setConfirm(null);
      load();
      onRefresh();
    } catch (e: any) { onToast("Failed: " + e.message, "error"); }
  };

  const statusBadge = (s: string) => {
    switch (s) {
      case "deactivated": return <span className="badge badge-yellow"><span className="badge-dot" />Deactivated</span>;
      case "suspended":   return <span className="badge badge-orange"><span className="badge-dot" />Suspended</span>;
      case "banned":      return <span className="badge badge-red"><span className="badge-dot" />Banned</span>;
      default:            return <span className="badge badge-green"><span className="badge-dot" />Active</span>;
    }
  };

  return (
    <>
      <div className="section-header">
        <h2>User Management</h2>
        <div className="search-box">
          <Search size={16} color="#71717a" />
          <input type="text" placeholder="Search by email or UID..." value={search} onChange={e => setSearch(e.target.value)} />
        </div>
        <button className="btn-secondary" onClick={load}>
          <RefreshCw size={14} /> Refresh
        </button>
      </div>

      <div className="table-wrapper">
        <table>
          <thead>
            <tr>
              <th>Email</th>
              <th>UID</th>
              <th>Status</th>
              <th>License</th>
              <th>HWID</th>
              <th>Reseller</th>
              <th>Last Login</th>
              <th>Features</th>
              <th>Actions</th>
            </tr>
          </thead>
          <tbody>
            {loading ? (
              <tr><td colSpan={9} className="loading-cell"><span className="spinner" /> Loading users...</td></tr>
            ) : filtered.length === 0 ? (
              <tr className="empty-row"><td colSpan={9}>No users found</td></tr>
            ) : filtered.map(u => (
              <tr key={u.uid} className={u.status !== "active" ? `row-${u.status}` : ""}>
                <td>{u.email}</td>
                <td className="mono uid-cell" title={u.uid} onClick={() => { navigator.clipboard.writeText(u.uid); onToast("Copied!", "success"); }}>
                  {u.uid.substring(0, 12)}…
                </td>
                <td>{statusBadge(u.status)}</td>
                <td>
                  {u.licensed
                    ? <span className="badge badge-green"><span className="badge-dot" />Active</span>
                    : <span className="badge badge-gray"><span className="badge-dot" />None</span>}
                </td>
                <td>
                  {u.hwid?.permanentHash
                    ? <span className="badge badge-blue">Registered</span>
                    : <span className="badge badge-gray">None</span>}
                </td>
                <td>
                  {u.resellerName
                    ? <span className="badge badge-purple">{u.resellerName}</span>
                    : <span className="badge badge-gray">—</span>}
                </td>
                <td className="mono" style={{ fontSize: "11px" }}>{u.hwid?.lastLogin || "—"}</td>
                <td>
                  <div className="features-cell">
                    {(["bypass", "streamerMode", "formBypass", "stopNetwork"] as const).map(f => (
                      <span key={f} className={`feat-tag ${u.features[f] !== false ? "feat-on" : "feat-off"}`}>
                        {abbrev[f]}
                      </span>
                    ))}
                  </div>
                </td>
                <td>
                  <div className="row-actions">
                    <button className="btn-icon" title="View Details" onClick={() => setSelectedUid(u.uid)}>
                      <Eye size={14} />
                    </button>
                    {u.status === "active" && (
                      <>
                        <button className="btn-icon warning" title="Deactivate" onClick={() => setConfirm({ type: "deactivate", uid: u.uid, email: u.email })}>
                          <ShieldOff size={14} />
                        </button>
                        <button className="btn-icon danger" title="Suspend" onClick={() => setConfirm({ type: "suspend", uid: u.uid, email: u.email })}>
                          <Ban size={14} />
                        </button>
                        <button className="btn-icon danger" title="System Ban (HWID)" onClick={() => setConfirm({ type: "ban", uid: u.uid, email: u.email })}>
                          <ShieldBan size={14} />
                        </button>
                      </>
                    )}
                    {u.status === "deactivated" && (
                      <button className="btn-icon success" title="Reactivate" onClick={() => setConfirm({ type: "reactivate", uid: u.uid, email: u.email })}>
                        <ShieldOff size={14} />
                      </button>
                    )}
                    {u.status === "suspended" && (
                      <button className="btn-icon success" title="Unsuspend" onClick={() => setConfirm({ type: "unsuspend", uid: u.uid, email: u.email })}>
                        <Ban size={14} />
                      </button>
                    )}
                    {u.status === "banned" && (
                      <button className="btn-icon success" title="Remove Ban" onClick={() => setConfirm({ type: "unban", uid: u.uid, email: u.email })}>
                        <ShieldBan size={14} />
                      </button>
                    )}
                    {u.licensed && u.status !== "banned" && u.status !== "suspended" && (
                      <button className="btn-icon danger" title="Revoke License" onClick={() => setConfirm({ type: "revoke", uid: u.uid, email: u.email })}>
                        <XCircle size={14} />
                      </button>
                    )}
                    <button className="btn-icon danger" title="Delete User" onClick={() => setConfirm({ type: "delete", uid: u.uid, email: u.email })}>
                      <Trash2 size={14} />
                    </button>
                  </div>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {selectedUid && (
        <UserDetailModal
          uid={selectedUid}
          onClose={() => { setSelectedUid(null); load(); }}
          onToast={onToast}
        />
      )}

      {confirm && (
        <ConfirmModal
          open={true}
          title={ACTION_CONFIG[confirm.type].title}
          message={ACTION_CONFIG[confirm.type].msg(confirm.email)}
          onConfirm={handleConfirm}
          onCancel={() => setConfirm(null)}
        />
      )}
    </>
  );
}
