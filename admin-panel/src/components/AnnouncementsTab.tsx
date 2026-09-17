import { useState, useEffect } from "react";
import { Megaphone, Plus, Trash2, Pin, PinOff, AlertTriangle, Info, Rocket, AlertCircle, RefreshCw, Edit3, Save, X } from "lucide-react";
import { fetchAnnouncements, createAnnouncement, deleteAnnouncement, updateAnnouncement, type AnnouncementData } from "../api";
import { ConfirmModal } from "./ConfirmModal";

interface Props {
  onToast: (msg: string, type: "success" | "error" | "info") => void;
}

const TYPE_COLORS: Record<string, string> = { urgent: "#ef4444", warning: "#eab308", update: "#3b82f6", info: "#71717a" };
const TYPE_BG: Record<string, string> = { urgent: "rgba(239,68,68,0.08)", warning: "rgba(234,179,8,0.08)", update: "rgba(59,130,246,0.08)", info: "rgba(113,113,122,0.08)" };
const TYPE_LABELS: Record<string, string> = { urgent: "🔴 Urgent", warning: "⚠️ Warning", update: "🚀 Update", info: "ℹ️ Info" };

const TypeIcon = ({ type }: { type: string }) => {
  switch (type) {
    case "urgent": return <AlertCircle size={14} />;
    case "warning": return <AlertTriangle size={14} />;
    case "update": return <Rocket size={14} />;
    default: return <Info size={14} />;
  }
};

export function AnnouncementsTab({ onToast }: Props) {
  const [announcements, setAnnouncements] = useState<AnnouncementData[]>([]);
  const [loading, setLoading] = useState(true);
  const [createOpen, setCreateOpen] = useState(false);
  const [title, setTitle] = useState("");
  const [body, setBody] = useState("");
  const [type, setType] = useState<AnnouncementData["type"]>("info");
  const [pinned, setPinned] = useState(false);
  const [busy, setBusy] = useState(false);
  const [editId, setEditId] = useState<string | null>(null);
  const [editTitle, setEditTitle] = useState("");
  const [editBody, setEditBody] = useState("");
  const [editType, setEditType] = useState<AnnouncementData["type"]>("info");
  const [confirm, setConfirm] = useState<{ id: string; title: string } | null>(null);

  const load = async () => {
    setLoading(true);
    try { setAnnouncements(await fetchAnnouncements()); } catch { onToast("Failed to load announcements", "error"); }
    setLoading(false);
  };

  useEffect(() => {
    let ignore = false;
    fetchAnnouncements().then(data => { if (!ignore) { setAnnouncements(data); setLoading(false); } }).catch(() => { if (!ignore) setLoading(false); });
    return () => { ignore = true; };
  }, []);

  const handleCreate = async () => {
    if (!title.trim() || !body.trim()) { onToast("Title and body are required", "error"); return; }
    setBusy(true);
    try {
      await createAnnouncement({ title: title.trim(), body: body.trim(), type, pinned });
      onToast("Announcement published!", "success");
      setTitle(""); setBody(""); setType("info"); setPinned(false); setCreateOpen(false);
      load();
    } catch (e: any) { onToast("Failed: " + e.message, "error"); }
    setBusy(false);
  };

  const handleDelete = async () => {
    if (!confirm) return;
    try { await deleteAnnouncement(confirm.id); onToast("Deleted", "success"); setConfirm(null); load(); } catch (e: any) { onToast("Failed: " + e.message, "error"); }
  };

  const handleTogglePin = async (ann: AnnouncementData) => {
    try { await updateAnnouncement(ann.id!, { pinned: !ann.pinned }); onToast(ann.pinned ? "Unpinned" : "Pinned!", "success"); load(); } catch (e: any) { onToast("Failed: " + e.message, "error"); }
  };

  const startEdit = (ann: AnnouncementData) => { setEditId(ann.id!); setEditTitle(ann.title); setEditBody(ann.body); setEditType(ann.type); };

  const handleSaveEdit = async () => {
    if (!editId || !editTitle.trim() || !editBody.trim()) return;
    setBusy(true);
    try { await updateAnnouncement(editId, { title: editTitle.trim(), body: editBody.trim(), type: editType }); onToast("Updated", "success"); setEditId(null); load(); } catch (e: any) { onToast("Failed: " + e.message, "error"); }
    setBusy(false);
  };

  const sorted = [...announcements].sort((a, b) => {
    if (a.pinned && !b.pinned) return -1;
    if (!a.pinned && b.pinned) return 1;
    return new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime();
  });

  return (
    <>
      <div className="section-header">
        <h2>Announcements</h2>
        <div className="header-actions">
          <button className="btn-primary btn-sm" onClick={() => setCreateOpen(true)}><Plus size={14} /> New Announcement</button>
          <button className="btn-secondary" onClick={load}><RefreshCw size={14} /> Refresh</button>
        </div>
      </div>

      {/* ── Create Panel ── */}
      {createOpen && (
        <div className="ann-create-panel">
          <div className="ann-create-header">
            <h3><Megaphone size={16} /> Publish Announcement</h3>
            <button className="btn-icon" onClick={() => setCreateOpen(false)}><X size={16} /></button>
          </div>
          <div className="input-group"><label>Title</label><input type="text" placeholder="Announcement title..." value={title} onChange={e => setTitle(e.target.value)} maxLength={120} /></div>
          <div className="input-group"><label>Body</label><textarea placeholder="Write your announcement..." value={body} onChange={e => setBody(e.target.value)} rows={4} style={{ resize: "vertical" }} /></div>
          <div className="ann-create-options">
            <div className="input-group" style={{ flex: 1, minWidth: 120 }}>
              <label>Type</label>
              <select value={type} onChange={e => setType(e.target.value as AnnouncementData["type"])}>
                <option value="info">ℹ️ Info</option><option value="update">🚀 Update</option><option value="warning">⚠️ Warning</option><option value="urgent">🔴 Urgent</option>
              </select>
            </div>
            <label className="ann-pin-toggle"><input type="checkbox" checked={pinned} onChange={e => setPinned(e.target.checked)} /><Pin size={14} /> Pinned</label>
            <div style={{ flex: 1 }} />
            <button className="btn-secondary" onClick={() => setCreateOpen(false)} disabled={busy}>Cancel</button>
            <button className="btn-primary" onClick={handleCreate} disabled={busy}>{busy ? <span className="spinner" /> : <><Megaphone size={14} /> Publish</>}</button>
          </div>
        </div>
      )}

      {/* ── List ── */}
      {loading ? (
        <div className="ann-loading"><span className="spinner" /> Loading announcements...</div>
      ) : sorted.length === 0 ? (
        <div className="ann-empty">
          <Megaphone size={48} style={{ opacity: 0.15 }} />
          <p>No announcements yet</p>
          <span>Click "New Announcement" to publish one. It will appear on all user dashboards.</span>
        </div>
      ) : (
        <div className="ann-grid">
          {sorted.map(ann => (
            <div key={ann.id} className="ann-card" style={{ borderLeftColor: TYPE_COLORS[ann.type] || "#71717a" }}>
              {editId === ann.id ? (
                <div className="ann-edit-form">
                  <input type="text" className="ann-edit-input" value={editTitle} onChange={e => setEditTitle(e.target.value)} />
                  <textarea className="ann-edit-textarea" value={editBody} onChange={e => setEditBody(e.target.value)} rows={3} />
                  <div className="ann-edit-actions">
                    <select value={editType} onChange={e => setEditType(e.target.value as AnnouncementData["type"])} style={{ padding: "6px 10px", borderRadius: 6, border: "1px solid #27272a", background: "#18181b", color: "#fafafa", fontSize: 12 }}>
                      <option value="info">ℹ️ Info</option><option value="update">🚀 Update</option><option value="warning">⚠️ Warning</option><option value="urgent">🔴 Urgent</option>
                    </select>
                    <div style={{ flex: 1 }} />
                    <button className="btn-secondary btn-sm" onClick={() => setEditId(null)}>Cancel</button>
                    <button className="btn-primary btn-sm" onClick={handleSaveEdit} disabled={busy}>{busy ? <span className="spinner" /> : <><Save size={12} /> Save</>}</button>
                  </div>
                </div>
              ) : (
                <>
                  <div className="ann-card-top">
                    <div className="ann-card-meta">
                      <span className="ann-type-badge" style={{ background: TYPE_BG[ann.type], color: TYPE_COLORS[ann.type] }}><TypeIcon type={ann.type} />{TYPE_LABELS[ann.type]}</span>
                      {ann.pinned && <span className="ann-pinned-badge"><Pin size={11} /> Pinned</span>}
                    </div>
                    <div className="ann-card-actions">
                      <button className="btn-icon" title={ann.pinned ? "Unpin" : "Pin"} onClick={() => handleTogglePin(ann)}>{ann.pinned ? <PinOff size={14} /> : <Pin size={14} />}</button>
                      <button className="btn-icon" title="Edit" onClick={() => startEdit(ann)}><Edit3 size={14} /></button>
                      <button className="btn-icon danger" title="Delete" onClick={() => setConfirm({ id: ann.id!, title: ann.title })}><Trash2 size={14} /></button>
                    </div>
                  </div>
                  <h4 className="ann-card-title">{ann.title}</h4>
                  <p className="ann-card-body">{ann.body}</p>
                  <div className="ann-card-footer">
                    <span className="ann-card-date">{new Date(ann.createdAt).toLocaleDateString(undefined, { year: "numeric", month: "short", day: "numeric", hour: "2-digit", minute: "2-digit" })}</span>
                    <span className="ann-card-author">by {ann.createdBy || "Admin"}</span>
                  </div>
                </>
              )}
            </div>
          ))}
        </div>
      )}

      <ConfirmModal open={!!confirm} title="Delete Announcement" message={`Delete <strong>${confirm?.title || ""}</strong>?<br><br>This will be removed from all user dashboards.`} onConfirm={handleDelete} onCancel={() => setConfirm(null)} />
    </>
  );
}
