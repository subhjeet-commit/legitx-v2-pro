import { Modal } from "./Modal";

interface ConfirmProps {
  open: boolean;
  title: string;
  message: string;
  onConfirm: () => void;
  onCancel: () => void;
  danger?: boolean;
}

export function ConfirmModal({ open, title, message, onConfirm, onCancel, danger = true }: ConfirmProps) {
  return (
    <Modal open={open} onClose={onCancel} title={title} size="xs">
      <div className="confirm-body">
        <p dangerouslySetInnerHTML={{ __html: message }} />
        <div className="confirm-actions">
          <button className="btn-secondary" onClick={onCancel}>Cancel</button>
          <button className={danger ? "btn-danger" : "btn-primary btn-sm"} onClick={onConfirm}>
            Confirm
          </button>
        </div>
      </div>
    </Modal>
  );
}
