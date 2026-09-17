import type { Toast } from "../hooks/useToast";

export function ToastContainer({ toasts }: { toasts: Toast[] }) {
  return (
    <div className="toast-container">
      {toasts.map(t => (
        <div key={t.id} className={`toast show ${t.type}`}>
          {t.message}
        </div>
      ))}
    </div>
  );
}
