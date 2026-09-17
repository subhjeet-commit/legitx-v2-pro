import type { ReactNode } from "react";
import { X } from "lucide-react";

interface ModalProps {
  open: boolean;
  onClose: () => void;
  title: string;
  size?: "sm" | "md" | "lg" | "xs";
  children: ReactNode;
}

export function Modal({ open, onClose, title, size = "md", children }: ModalProps) {
  if (!open) return null;

  return (
    <div className="modal-backdrop" onClick={e => { if (e.target === e.currentTarget) onClose(); }}>
      <div className={`modal-box modal-${size}`}>
        <div className="modal-header">
          <h3>{title}</h3>
          <button className="modal-close" onClick={onClose}><X size={16} /></button>
        </div>
        <div className="modal-body">
          {children}
        </div>
      </div>
    </div>
  );
}
