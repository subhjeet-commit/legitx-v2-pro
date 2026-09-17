import { useState } from "react";
import { Key, CheckCircle, Sparkles, X } from "lucide-react";
import { redeemLicense } from "../api";

interface Props {
  open: boolean;
  onClose: () => void;
  onToast: (msg: string, type: "success" | "error" | "info") => void;
  onRedeemed: () => void;
  alreadyLicensed: boolean;
}

export function RedeemModal({ open, onClose, onToast, onRedeemed, alreadyLicensed }: Props) {
  const [code, setCode] = useState("");
  const [loading, setLoading] = useState(false);
  const [success, setSuccess] = useState(false);
  const [result, setResult] = useState<{ plan: string; trialDays?: number } | null>(null);

  if (!open) return null;

  const handleRedeem = async () => {
    const trimmed = code.trim().toUpperCase();
    if (!trimmed) {
      onToast("Please enter a license code", "error");
      return;
    }
    setLoading(true);
    try {
      const res = await redeemLicense(trimmed);
      setResult(res);
      setSuccess(true);
      onToast("License activated successfully!", "success");
      onRedeemed();
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : "Failed to redeem code";
      onToast(msg, "error");
    }
    setLoading(false);
  };

  const handleClose = () => {
    setCode("");
    setSuccess(false);
    setResult(null);
    onClose();
  };

  return (
    <div className="modal-backdrop" onClick={handleClose}>
      <div className="modal-box modal-sm" onClick={e => e.stopPropagation()}>
        <div className="modal-header">
          <h3><Key size={18} /> Redeem License Code</h3>
          <button className="modal-close" onClick={handleClose}><X size={18} /></button>
        </div>

        <div className="modal-body">
          {alreadyLicensed && !success ? (
            <div className="redeem-already">
              <div className="redeem-already-icon">
                <CheckCircle size={32} />
              </div>
              <h4>Already Licensed!</h4>
              <p>You already have an active license.</p>
              <p className="hint">If you need to change your license, contact an administrator.</p>
            </div>
          ) : success && result ? (
            <div className="redeem-success">
              <div className="success-burst">
                <Sparkles size={20} className="burst-spark" />
                <CheckCircle size={52} />
                <Sparkles size={20} className="burst-spark" />
              </div>
              <h4>License Activated!</h4>
              <p>
                Your <strong>{result.plan === "trial" ? `Trial (${result.trialDays}d)` : "Permanent"}</strong> license has been activated successfully.
              </p>
              <button className="btn-primary btn-full btn-glow" onClick={handleClose}>
                Continue to Dashboard
              </button>
            </div>
          ) : (
            <>
              <div className="redeem-intro">
                <div className="redeem-intro-icon">
                  <Key size={24} />
                </div>
                <p>Enter your license code to activate your account and unlock all features.</p>
              </div>
              <div className="input-group">
                <label>License Code</label>
                <input
                  type="text"
                  placeholder="LEGITX-XXXX-XXXX-XXXX"
                  value={code}
                  onChange={e => setCode(e.target.value.toUpperCase())}
                  onKeyDown={e => e.key === "Enter" && handleRedeem()}
                  className="code-input"
                  autoFocus
                />
              </div>
              <button
                className="btn-primary btn-full btn-glow"
                onClick={handleRedeem}
                disabled={loading || !code.trim()}
              >
                {loading ? <span className="spinner" /> : <><Key size={16} /> Activate License</>}
              </button>
            </>
          )}
        </div>
      </div>
    </div>
  );
}
