import { useState, useEffect } from "react";
import {
  Save, Plus, Trash2, GripVertical, Link, Download,
  ExternalLink, Activity, ListOrdered, Monitor, ArrowUp, ArrowDown,
} from "lucide-react";
import { fetchSiteConfig, saveSiteConfig } from "../api";
import type { SiteConfig, SiteConfigRequirement } from "../types";

interface Props {
  onToast: (msg: string, type: "success" | "error" | "info") => void;
}

let idCounter = Date.now();
const nextId = () => `item_${idCounter++}`;

export function SiteConfigTab({ onToast }: Props) {
  const [config, setConfig] = useState<SiteConfig | null>(null);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [dirty, setDirty] = useState(false);

  useEffect(() => {
    fetchSiteConfig()
      .then((c) => { setConfig(c); setLoading(false); })
      .catch(() => { setLoading(false); onToast("Failed to load site config", "error"); });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const update = (patch: Partial<SiteConfig>) => {
    setConfig((prev) => prev ? { ...prev, ...patch } : prev);
    setDirty(true);
  };

  const handleSave = async () => {
    if (!config) return;
    setSaving(true);
    try {
      await saveSiteConfig(config);
      setDirty(false);
      onToast("Site config saved successfully!", "success");
    } catch (e: any) {
      onToast("Failed to save: " + e.message, "error");
    }
    setSaving(false);
  };

  // ── Requirement helpers ──
  const addRequirement = () => {
    update({
      requirements: [
        ...(config?.requirements || []),
        { id: nextId(), text: "", downloadUrl: "" },
      ],
    });
  };

  const updateRequirement = (id: string, patch: Partial<SiteConfigRequirement>) => {
    update({
      requirements: (config?.requirements || []).map((r) =>
        r.id === id ? { ...r, ...patch } : r
      ),
    });
  };

  const removeRequirement = (id: string) => {
    update({
      requirements: (config?.requirements || []).filter((r) => r.id !== id),
    });
  };

  const moveRequirement = (idx: number, dir: -1 | 1) => {
    const arr = [...(config?.requirements || [])];
    const newIdx = idx + dir;
    if (newIdx < 0 || newIdx >= arr.length) return;
    [arr[idx], arr[newIdx]] = [arr[newIdx], arr[idx]];
    update({ requirements: arr });
  };

  // ── Step helpers ──
  const addStep = () => {
    update({
      gettingStarted: [
        ...(config?.gettingStarted || []),
        { id: nextId(), text: "" },
      ],
    });
  };

  const updateStep = (id: string, text: string) => {
    update({
      gettingStarted: (config?.gettingStarted || []).map((s) =>
        s.id === id ? { ...s, text } : s
      ),
    });
  };

  const removeStep = (id: string) => {
    update({
      gettingStarted: (config?.gettingStarted || []).filter((s) => s.id !== id),
    });
  };

  const moveStep = (idx: number, dir: -1 | 1) => {
    const arr = [...(config?.gettingStarted || [])];
    const newIdx = idx + dir;
    if (newIdx < 0 || newIdx >= arr.length) return;
    [arr[idx], arr[newIdx]] = [arr[newIdx], arr[idx]];
    update({ gettingStarted: arr });
  };

  if (loading) {
    return (
      <div className="loading-state">
        <span className="spinner" /> Loading site config…
      </div>
    );
  }

  if (!config) {
    return (
      <div className="loading-state">Failed to load site config.</div>
    );
  }

  return (
    <div className="site-config-tab">
      {/* Save Bar */}
      <div className="site-config-save-bar">
        <div className="site-config-save-left">
          <Monitor size={20} />
          <div>
            <h2>Download Page Configuration</h2>
            <p>Manage the download link, system requirements, and getting started steps shown to users.</p>
          </div>
        </div>
        <button
          className={`btn-primary${dirty ? " btn-glow" : ""}`}
          onClick={handleSave}
          disabled={saving || !dirty}
        >
          {saving ? <><span className="spinner" /> Saving…</> : <><Save size={16} /> Save Changes</>}
        </button>
      </div>

      {/* ── Download Link Section ── */}
      <div className="site-config-section">
        <div className="site-config-section-header">
          <Download size={18} />
          <h3>Download Link</h3>
        </div>
        <div className="site-config-section-body">
          <div className="form-group">
            <label className="form-label">
              <Link size={14} /> Download URL
            </label>
            <input
              type="url"
              className="form-input"
              placeholder="https://example.com/LegitX-V2-Latest.zip"
              value={config.downloadUrl}
              onChange={(e) => update({ downloadUrl: e.target.value })}
            />
            <span className="form-hint">Paste the direct download URL for the LegitX V2 application. Leave empty to show "coming soon".</span>
          </div>
          <div className="form-group">
            <label className="form-label">
              <Download size={14} /> Button Label
            </label>
            <input
              type="text"
              className="form-input"
              placeholder="Download LegitX V2"
              value={config.downloadLabel}
              onChange={(e) => update({ downloadLabel: e.target.value })}
            />
          </div>
          {config.downloadUrl && (
            <div className="site-config-preview-link">
              <ExternalLink size={14} />
              <a href={config.downloadUrl} target="_blank" rel="noopener noreferrer">{config.downloadUrl}</a>
            </div>
          )}
        </div>
      </div>

      {/* ── System Requirements Section ── */}
      <div className="site-config-section">
        <div className="site-config-section-header">
          <ExternalLink size={18} />
          <h3>System Requirements</h3>
          <span className="badge badge-blue">{config.requirements.length}</span>
        </div>
        <div className="site-config-section-body">
          <div className="site-config-items">
            {config.requirements.map((req, idx) => (
              <div key={req.id} className="site-config-item">
                <div className="site-config-item-handle">
                  <GripVertical size={14} />
                </div>
                <div className="site-config-item-num">{idx + 1}</div>
                <div className="site-config-item-fields">
                  <input
                    type="text"
                    className="form-input"
                    placeholder="e.g. Windows 10 / 11 (64-bit)"
                    value={req.text}
                    onChange={(e) => updateRequirement(req.id, { text: e.target.value })}
                  />
                  <div className="site-config-item-link-row">
                    <Link size={12} />
                    <input
                      type="url"
                      className="form-input form-input-sm"
                      placeholder="Optional download link (e.g. https://dotnet.microsoft.com)"
                      value={req.downloadUrl || ""}
                      onChange={(e) => updateRequirement(req.id, { downloadUrl: e.target.value })}
                    />
                  </div>
                </div>
                <div className="site-config-item-actions">
                  <button
                    className="btn-icon btn-icon-xs"
                    onClick={() => moveRequirement(idx, -1)}
                    disabled={idx === 0}
                    title="Move up"
                  >
                    <ArrowUp size={12} />
                  </button>
                  <button
                    className="btn-icon btn-icon-xs"
                    onClick={() => moveRequirement(idx, 1)}
                    disabled={idx === config.requirements.length - 1}
                    title="Move down"
                  >
                    <ArrowDown size={12} />
                  </button>
                  <button
                    className="btn-icon btn-icon-xs danger"
                    onClick={() => removeRequirement(req.id)}
                    title="Remove"
                  >
                    <Trash2 size={12} />
                  </button>
                </div>
              </div>
            ))}
          </div>
          <button className="btn-secondary btn-sm" onClick={addRequirement}>
            <Plus size={14} /> Add Requirement
          </button>
        </div>
      </div>

      {/* ── Getting Started Section ── */}
      <div className="site-config-section">
        <div className="site-config-section-header">
          <ListOrdered size={18} />
          <h3>Getting Started Steps</h3>
          <span className="badge badge-blue">{config.gettingStarted.length}</span>
        </div>
        <div className="site-config-section-body">
          <div className="site-config-items">
            {config.gettingStarted.map((step, idx) => (
              <div key={step.id} className="site-config-item">
                <div className="site-config-item-handle">
                  <GripVertical size={14} />
                </div>
                <div className="site-config-item-num">{idx + 1}</div>
                <div className="site-config-item-fields" style={{ flex: 1 }}>
                  <input
                    type="text"
                    className="form-input"
                    placeholder={`Step ${idx + 1} — e.g. "Extract the ZIP file"`}
                    value={step.text}
                    onChange={(e) => updateStep(step.id, e.target.value)}
                  />
                </div>
                <div className="site-config-item-actions">
                  <button
                    className="btn-icon btn-icon-xs"
                    onClick={() => moveStep(idx, -1)}
                    disabled={idx === 0}
                    title="Move up"
                  >
                    <ArrowUp size={12} />
                  </button>
                  <button
                    className="btn-icon btn-icon-xs"
                    onClick={() => moveStep(idx, 1)}
                    disabled={idx === config.gettingStarted.length - 1}
                    title="Move down"
                  >
                    <ArrowDown size={12} />
                  </button>
                  <button
                    className="btn-icon btn-icon-xs danger"
                    onClick={() => removeStep(step.id)}
                    title="Remove"
                  >
                    <Trash2 size={12} />
                  </button>
                </div>
              </div>
            ))}
          </div>
          <button className="btn-secondary btn-sm" onClick={addStep}>
            <Plus size={14} /> Add Step
          </button>
        </div>
      </div>

      {/* Preview Hint */}
      <div className="site-config-hint">
        <Activity size={14} />
        <span>Changes are saved to Firestore and reflected immediately on the user panel's Download page.</span>
      </div>
    </div>
  );
}
