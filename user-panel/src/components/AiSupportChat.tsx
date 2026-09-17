import { useState, useRef, useEffect, useCallback } from "react";
import { Send, ImagePlus, Trash2, Bot, User, Brain, FileText, X, Paperclip, Plus, MessageSquare, ChevronLeft, ChevronRight } from "lucide-react";

interface ChatMessage {
  role: "user" | "assistant";
  content: string;
  thinking?: string;
  attachments?: { type: "image" | "text"; name: string; preview?: string }[];
}

interface ChatSession {
  id: string;
  title: string;
  messages: ChatMessage[];
  createdAt: number;
  updatedAt: number;
}

interface Props {
  onToast: (msg: string, type: "success" | "error" | "info") => void;
}

const STORAGE_KEY = "legitx-ai-chat-history";

function loadSessions(): ChatSession[] {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    if (!raw) return [];
    return JSON.parse(raw);
  } catch { return []; }
}

function saveSessions(sessions: ChatSession[]) {
  try { localStorage.setItem(STORAGE_KEY, JSON.stringify(sessions)); } catch {}
}

function createSession(): ChatSession {
  return { id: crypto.randomUUID(), title: "New Chat", messages: [], createdAt: Date.now(), updatedAt: Date.now() };
}

const API_URL = "YOUR_BASE_URL";
const API_KEY = "YOUR_API";
const MODEL = "moonshotai/kimi-k2.5";

export function AiSupportChat({ onToast }: Props) {
  const [sessions, setSessions] = useState<ChatSession[]>(() => loadSessions());
  const [activeId, setActiveId] = useState<string>(() => {
    const s = loadSessions();
    return s.length > 0 ? s[0].id : "";
  });
  const [sidebarOpen, setSidebarOpen] = useState(true);
  const [messages, setMessages] = useState<ChatMessage[]>([]);
  const [input, setInput] = useState("");
  const [streaming, setStreaming] = useState(false);
  const [pendingFiles, setPendingFiles] = useState<{ type: "image" | "text"; name: string; data: string }[]>([]);
  const chatEndRef = useRef<HTMLDivElement>(null);
  const textareaRef = useRef<HTMLTextAreaElement>(null);
  const fileInputRef = useRef<HTMLInputElement>(null);
  const imgInputRef = useRef<HTMLInputElement>(null);
  const abortRef = useRef<AbortController | null>(null);
  const titleGenerated = useRef(false);

  // Load active session's messages
  useEffect(() => {
    const session = sessions.find(s => s.id === activeId);
    if (session) {
      setMessages(session.messages);
      titleGenerated.current = session.title !== "New Chat";
    } else if (sessions.length === 0) {
      const first = createSession();
      setSessions([first]);
      setActiveId(first.id);
      setMessages([]);
      titleGenerated.current = false;
    }
  }, [activeId]);

  // Save messages to session whenever they change
  useEffect(() => {
    if (!activeId) return;
    setSessions(prev => {
      const updated = prev.map(s =>
        s.id === activeId ? { ...s, messages, updatedAt: Date.now() } : s
      );
      saveSessions(updated);
      return updated;
    });
  }, [messages, activeId]);

  // Auto-scroll
  useEffect(() => {
    chatEndRef.current?.scrollIntoView({ behavior: "smooth" });
  }, [messages]);

  // Auto-resize textarea
  useEffect(() => {
    const ta = textareaRef.current;
    if (ta) {
      ta.style.height = "auto";
      ta.style.height = Math.min(ta.scrollHeight, 160) + "px";
    }
  }, [input]);

  const startNewChat = () => {
    if (streaming) return;
    const session = createSession();
    const updated = [session, ...sessions];
    setSessions(updated);
    saveSessions(updated);
    setActiveId(session.id);
    setMessages([]);
    setPendingFiles([]);
    titleGenerated.current = false;
  };

  const switchSession = (id: string) => {
    if (streaming || id === activeId) return;
    setActiveId(id);
    setPendingFiles([]);
  };

  const deleteSession = (id: string) => {
    if (streaming) return;
    const updated = sessions.filter(s => s.id !== id);
    setSessions(updated);
    saveSessions(updated);
    if (id === activeId) {
      if (updated.length > 0) {
        setActiveId(updated[0].id);
      } else {
        const fresh = createSession();
        setSessions([fresh]);
        saveSessions([fresh]);
        setActiveId(fresh.id);
        setMessages([]);
        titleGenerated.current = false;
      }
    }
  };

  const generateTitle = async (userMsg: string, aiReply: string) => {
    if (titleGenerated.current) return;
    titleGenerated.current = true;
    try {
      const res = await fetch(API_URL, {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          Authorization: `Bearer ${API_KEY}`,
        },
        body: JSON.stringify({
          model: MODEL,
          messages: [
            { role: "system", content: "Generate a very short chat topic title (max 5 words, no quotes, no punctuation at end). Based on the user question and AI answer below." },
            { role: "user", content: `User: ${userMsg.slice(0, 200)}\nAI: ${aiReply.slice(0, 200)}` }
          ],
          max_tokens: 20,
          temperature: 0.5,
          stream: false,
        }),
      });
      if (!res.ok) return;
      const data = await res.json();
      const title = data.choices?.[0]?.message?.content?.trim().slice(0, 40) || "Chat";
      setSessions(prev => {
        const updated = prev.map(s =>
          s.id === activeId ? { ...s, title } : s
        );
        saveSessions(updated);
        return updated;
      });
    } catch {}
  };

  const readFileAsText = (file: File): Promise<string> =>
    new Promise((resolve, reject) => {
      const reader = new FileReader();
      reader.onload = () => resolve(reader.result as string);
      reader.onerror = reject;
      reader.readAsText(file);
    });

  const readFileAsBase64 = (file: File): Promise<string> =>
    new Promise((resolve, reject) => {
      const reader = new FileReader();
      reader.onload = () => resolve(reader.result as string);
      reader.onerror = reject;
      reader.readAsDataURL(file);
    });

  const handleTextUpload = async (e: React.ChangeEvent<HTMLInputElement>) => {
    const files = e.target.files;
    if (!files) return;
    for (const file of Array.from(files)) {
      if (file.size > 2 * 1024 * 1024) {
        onToast(`File "${file.name}" is too large (max 2MB)`, "error");
        continue;
      }
      try {
        const text = await readFileAsText(file);
        setPendingFiles(prev => [...prev, { type: "text", name: file.name, data: text }]);
      } catch {
        onToast(`Failed to read "${file.name}"`, "error");
      }
    }
    e.target.value = "";
  };

  const handleImageUpload = async (e: React.ChangeEvent<HTMLInputElement>) => {
    const files = e.target.files;
    if (!files) return;
    for (const file of Array.from(files)) {
      if (file.size > 5 * 1024 * 1024) {
        onToast(`Image "${file.name}" is too large (max 5MB)`, "error");
        continue;
      }
      if (!file.type.startsWith("image/")) {
        onToast(`"${file.name}" is not an image`, "error");
        continue;
      }
      try {
        const b64 = await readFileAsBase64(file);
        setPendingFiles(prev => [...prev, { type: "image", name: file.name, data: b64 }]);
      } catch {
        onToast(`Failed to read "${file.name}"`, "error");
      }
    }
    e.target.value = "";
  };

  // Handle paste (images from clipboard)
  const handlePaste = useCallback(async (e: React.ClipboardEvent) => {
    const items = e.clipboardData.items;
    for (const item of Array.from(items)) {
      if (item.type.startsWith("image/")) {
        e.preventDefault();
        const file = item.getAsFile();
        if (!file) continue;
        try {
          const b64 = await readFileAsBase64(file);
          setPendingFiles(prev => [...prev, { type: "image", name: `pasted-image-${Date.now()}.png`, data: b64 }]);
          onToast("Image pasted from clipboard", "info");
        } catch {
          onToast("Failed to paste image", "error");
        }
      }
    }
  }, [onToast]);

  const removePendingFile = (idx: number) => {
    setPendingFiles(prev => prev.filter((_, i) => i !== idx));
  };

  const buildApiMessages = (history: ChatMessage[], userText: string, files: typeof pendingFiles) => {
    const apiMsgs: any[] = [
      {
        role: "system",
        content: "You are the LegitX V2 AI Support Assistant. Help users with questions about LegitX V2 software, troubleshooting, features, setup, and general support. Be concise and helpful. If you are unsure, say so honestly."
      }
    ];

    // Add history
    for (const msg of history) {
      if (msg.role === "user") {
        apiMsgs.push({ role: "user", content: msg.content });
      } else {
        apiMsgs.push({ role: "assistant", content: msg.content });
      }
    }

    // Build current user message
    const contentParts: any[] = [];

    // Add text files as context
    for (const f of files) {
      if (f.type === "text") {
        contentParts.push({
          type: "text",
          text: `[Attached file: ${f.name}]\n\`\`\`\n${f.data}\n\`\`\``
        });
      }
    }

    // Add images
    for (const f of files) {
      if (f.type === "image") {
        contentParts.push({
          type: "image_url",
          image_url: { url: f.data }
        });
      }
    }

    // Add user text
    if (userText.trim()) {
      contentParts.push({ type: "text", text: userText.trim() });
    }

    const hasImages = files.some(f => f.type === "image");

    if (contentParts.length === 1 && contentParts[0].type === "text") {
      apiMsgs.push({ role: "user", content: contentParts[0].text });
    } else if (contentParts.length > 0) {
      apiMsgs.push({ role: "user", content: contentParts });
    }

    return { apiMsgs, hasImages };
  };

  const sendMessage = async () => {
    const trimmed = input.trim();
    if (!trimmed && pendingFiles.length === 0) return;
    if (streaming) return;

    const userAttachments = pendingFiles.map(f => ({
      type: f.type,
      name: f.name,
      preview: f.type === "image" ? f.data : undefined,
    }));

    const userMsg: ChatMessage = {
      role: "user",
      content: trimmed || (pendingFiles.length > 0 ? `[${pendingFiles.length} file(s) attached]` : ""),
      attachments: userAttachments.length > 0 ? userAttachments : undefined,
    };

    const newHistory = [...messages, userMsg];
    setMessages(newHistory);
    setInput("");
    setStreaming(true);

    const { apiMsgs } = buildApiMessages(messages, trimmed, pendingFiles);
    setPendingFiles([]);

    const assistantMsg: ChatMessage = { role: "assistant", content: "", thinking: "" };
    setMessages([...newHistory, assistantMsg]);

    try {
      abortRef.current = new AbortController();

      const res = await fetch(API_URL, {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          Authorization: `Bearer ${API_KEY}`,
          Accept: "text/event-stream",
        },
        body: JSON.stringify({
          model: MODEL,
          messages: apiMsgs,
          max_tokens: 16384,
          temperature: 1.0,
          top_p: 1.0,
          stream: true,
          chat_template_kwargs: { thinking: true },
        }),
        signal: abortRef.current.signal,
      });

      if (!res.ok) {
        const errText = await res.text();
        throw new Error(`API error ${res.status}: ${errText}`);
      }

      const reader = res.body!.getReader();
      const decoder = new TextDecoder();
      let buffer = "";
      let accContent = "";
      let accThinking = "";

      while (true) {
        const { done, value } = await reader.read();
        if (done) break;

        buffer += decoder.decode(value, { stream: true });
        const lines = buffer.split("\n");
        buffer = lines.pop() || "";

        for (const line of lines) {
          const trimLine = line.trim();
          if (!trimLine || !trimLine.startsWith("data:")) continue;
          const jsonStr = trimLine.slice(5).trim();
          if (jsonStr === "[DONE]") continue;

          try {
            const parsed = JSON.parse(jsonStr);
            const delta = parsed.choices?.[0]?.delta;
            if (!delta) continue;

            // Check for thinking/reasoning content
            if (delta.reasoning_content) {
              accThinking += delta.reasoning_content;
            } else if (delta.content) {
              accContent += delta.content;
            }

            setMessages(prev => {
              const updated = [...prev];
              updated[updated.length - 1] = {
                role: "assistant",
                content: accContent,
                thinking: accThinking || undefined,
              };
              return updated;
            });
          } catch {
            // skip malformed
          }
        }
      }
    } catch (err: any) {
      if (err.name === "AbortError") return;
      onToast("AI Error: " + err.message, "error");
      setMessages(prev => {
        const updated = [...prev];
        updated[updated.length - 1] = {
          role: "assistant",
          content: "Sorry, I encountered an error. Please try again.",
        };
        return updated;
      });
    } finally {
      setStreaming(false);
      abortRef.current = null;
      // Generate title after first AI response
      setMessages(prev => {
        const lastMsg = prev[prev.length - 1];
        if (lastMsg?.role === "assistant" && lastMsg.content && !titleGenerated.current) {
          generateTitle(trimmed, lastMsg.content);
        }
        return prev;
      });
    }
  };

  const handleKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === "Enter" && !e.shiftKey) {
      e.preventDefault();
      sendMessage();
    }
  };

  const clearChat = () => {
    if (streaming) {
      abortRef.current?.abort();
      setStreaming(false);
    }
    setMessages([]);
    setPendingFiles([]);
  };

  return (
    <div className="ai-chat-wrapper">
      {/* Sidebar */}
      <div className={`ai-sidebar ${sidebarOpen ? "open" : "closed"}`}>
        <div className="ai-sidebar-header">
          <button className="ai-sidebar-new" onClick={startNewChat} disabled={streaming}>
            <Plus size={14} /> New Chat
          </button>
          <button className="ai-sidebar-toggle" onClick={() => setSidebarOpen(false)}>
            <ChevronLeft size={16} />
          </button>
        </div>
        <div className="ai-sidebar-list">
          {sessions.map(s => (
            <div
              key={s.id}
              className={`ai-sidebar-item ${s.id === activeId ? "active" : ""}`}
              onClick={() => switchSession(s.id)}
            >
              <MessageSquare size={13} />
              <span className="ai-sidebar-title">{s.title}</span>
              <button
                className="ai-sidebar-delete"
                onClick={(e) => { e.stopPropagation(); deleteSession(s.id); }}
                title="Delete chat"
              >
                <Trash2 size={12} />
              </button>
            </div>
          ))}
          {sessions.length === 0 && (
            <div className="ai-sidebar-empty">No chat history</div>
          )}
        </div>
      </div>

      {/* Toggle sidebar button (when closed) */}
      {!sidebarOpen && (
        <button className="ai-sidebar-open-btn" onClick={() => setSidebarOpen(true)}>
          <ChevronRight size={16} />
        </button>
      )}

      <div className="ai-chat-container">
      {/* Chat Messages */}
      <div className="ai-chat-messages">
        {messages.length === 0 && (
          <div className="ai-chat-welcome">
            <div className="ai-welcome-icon"><Bot size={48} /></div>
            <h3>LegitX V2 AI Support</h3>
            <p>Ask me anything about LegitX V2 — setup, features, troubleshooting, and more.</p>
            <div className="ai-welcome-tips">
              <span onClick={() => setInput("How do I set up LegitX V2?")}>💡 How to set up?</span>
              <span onClick={() => setInput("My hardware ID is not registering")}>🔧 HWID issue</span>
              <span onClick={() => setInput("How do I redeem a license code?")}>🔑 Redeem license</span>
            </div>
          </div>
        )}

        {messages.map((msg, i) => (
          <div key={i} className={`ai-msg ai-msg-${msg.role}`}>
            <div className="ai-msg-avatar">
              {msg.role === "user" ? <User size={16} /> : <Bot size={16} />}
            </div>
            <div className="ai-msg-body">
              {/* Attachments */}
              {msg.attachments && msg.attachments.length > 0 && (
                <div className="ai-msg-attachments">
                  {msg.attachments.map((att, j) => (
                    <div key={j} className="ai-attachment-chip">
                      {att.type === "image" ? (
                        att.preview ? (
                          <img src={att.preview} alt={att.name} className="ai-attachment-thumb" />
                        ) : (
                          <><ImagePlus size={12} /> {att.name}</>
                        )
                      ) : (
                        <><FileText size={12} /> {att.name}</>
                      )}
                    </div>
                  ))}
                </div>
              )}

              {/* Thinking / Reasoning */}
              {msg.thinking && (
                <details className="ai-thinking-block" open={i === messages.length - 1 && streaming}>
                  <summary><Brain size={13} /> Reasoning</summary>
                  <div className="ai-thinking-content">{msg.thinking}</div>
                </details>
              )}

              {/* Content */}
              <div className="ai-msg-content">
                {msg.content || (msg.role === "assistant" && streaming && i === messages.length - 1 ? (
                  <span className="ai-typing-indicator">
                    <span /><span /><span />
                  </span>
                ) : null)}
              </div>
            </div>
          </div>
        ))}
        <div ref={chatEndRef} />
      </div>

      {/* Pending Attachments */}
      {pendingFiles.length > 0 && (
        <div className="ai-pending-files">
          {pendingFiles.map((f, i) => (
            <div key={i} className="ai-pending-chip">
              {f.type === "image" ? (
                <img src={f.data} alt={f.name} className="ai-pending-thumb" />
              ) : (
                <FileText size={14} />
              )}
              <span className="ai-pending-name">{f.name}</span>
              <button className="ai-pending-remove" onClick={() => removePendingFile(i)}>
                <X size={12} />
              </button>
            </div>
          ))}
        </div>
      )}

      {/* Input Area */}
      <div className="ai-chat-input-area">
        <div className="ai-input-actions">
          <button
            className="ai-action-btn"
            onClick={() => fileInputRef.current?.click()}
            title="Upload TXT file"
            disabled={streaming}
          >
            <Paperclip size={16} />
          </button>
          <button
            className="ai-action-btn"
            onClick={() => imgInputRef.current?.click()}
            title="Upload image"
            disabled={streaming}
          >
            <ImagePlus size={16} />
          </button>
          <button
            className="ai-action-btn ai-action-btn-danger"
            onClick={clearChat}
            title="Clear chat"
          >
            <Trash2 size={16} />
          </button>
        </div>

        <div className="ai-input-wrap">
          <textarea
            ref={textareaRef}
            className="ai-textarea"
            placeholder="Type your message… (paste images with Ctrl+V)"
            value={input}
            onChange={e => setInput(e.target.value)}
            onKeyDown={handleKeyDown}
            onPaste={handlePaste}
            rows={1}
            disabled={streaming}
          />
          <button
            className="ai-send-btn"
            onClick={sendMessage}
            disabled={streaming || (!input.trim() && pendingFiles.length === 0)}
            title="Send"
          >
            <Send size={16} />
          </button>
        </div>

        {/* Hidden file inputs */}
        <input
          ref={fileInputRef}
          type="file"
          accept=".txt,.log,.csv,.json,.xml,.md,.cfg,.ini,.bat,.ps1,.sh,.py,.js,.ts,.cs,.java,.cpp,.c,.h"
          multiple
          style={{ display: "none" }}
          onChange={handleTextUpload}
        />
        <input
          ref={imgInputRef}
          type="file"
          accept="image/*"
          multiple
          style={{ display: "none" }}
          onChange={handleImageUpload}
        />
      </div>
      </div>
    </div>
  );
}
