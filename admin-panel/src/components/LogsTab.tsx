import { useState, useEffect, useCallback, useMemo } from "react";
import { RefreshCw, Search } from "lucide-react";
import type { ActivityLog } from "../types";
import { fetchLogs } from "../api";

interface Props {
  onToast: (msg: string, type: "success" | "error" | "info") => void;
  refreshKey: number;
}

const tsDisplay = (t: any): string => {
  if (!t) return "—";
  if (t.toDate) return t.toDate().toLocaleString();
  return String(t);
};

const actionColor = (action: string): string => {
  if (action.includes("delete") || action.includes("revoke") || action.includes("wipe")) return "badge-red";
  if (action.includes("suspend") || action.includes("deactivate")) return "badge-orange";
  if (action.includes("generate") || action.includes("add") || action.includes("activate")) return "badge-green";
  if (action.includes("reset") || action.includes("update") || action.includes("toggle")) return "badge-blue";
  return "badge-gray";
};

export function LogsTab({ onToast, refreshKey }: Props) {
  const [logs, setLogs] = useState<ActivityLog[]>([]);
  const [search, setSearch] = useState("");
  const [loading, setLoading] = useState(true);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const list = await fetchLogs();
      setLogs(list);
    } catch (e: any) { onToast("Failed to load logs: " + e.message, "error"); }
    setLoading(false);
  }, [onToast]);

  useEffect(() => {
    let ignore = false;
    fetchLogs().then(list => { if (!ignore) { setLogs(list); setLoading(false); } }).catch(() => setLoading(false));
    return () => { ignore = true; };
  }, [refreshKey]);

  const filtered = useMemo(() => {
    const q = search.toLowerCase();
    return logs.filter(l =>
      l.action.toLowerCase().includes(q) ||
      (l.target || "").toLowerCase().includes(q) ||
      (l.details || "").toLowerCase().includes(q) ||
      (l.adminEmail || "").toLowerCase().includes(q)
    );
  }, [search, logs]);

  return (
    <>
      <div className="section-header">
        <h2>Activity Log</h2>
        <div className="header-actions">
          <div className="search-box">
            <Search size={16} color="#71717a" />
            <input type="text" placeholder="Search logs..." value={search} onChange={e => setSearch(e.target.value)} />
          </div>
          <button className="btn-secondary" onClick={load}>
            <RefreshCw size={14} /> Refresh
          </button>
        </div>
      </div>

      <div className="table-wrapper">
        <table>
          <thead>
            <tr>
              <th>Timestamp</th>
              <th>Action</th>
              <th>Target</th>
              <th>Details</th>
              <th>Admin</th>
            </tr>
          </thead>
          <tbody>
            {loading ? (
              <tr><td colSpan={5} className="loading-cell"><span className="spinner" /> Loading logs...</td></tr>
            ) : filtered.length === 0 ? (
              <tr className="empty-row"><td colSpan={5}>No activity logs found</td></tr>
            ) : filtered.map(l => (
              <tr key={l.id}>
                <td className="mono" style={{ fontSize: "11px", whiteSpace: "nowrap" }}>{tsDisplay(l.timestamp)}</td>
                <td><span className={`badge ${actionColor(l.action)}`}>{l.action.replace(/_/g, " ")}</span></td>
                <td className="mono" style={{ fontSize: "12px", maxWidth: 200, overflow: "hidden", textOverflow: "ellipsis" }}>{l.target || "—"}</td>
                <td style={{ maxWidth: 300, overflow: "hidden", textOverflow: "ellipsis" }}>{l.details || "—"}</td>
                <td>{l.adminEmail || "—"}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </>
  );
}
