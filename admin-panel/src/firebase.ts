import { initializeApp } from "firebase/app";
import { getAuth, GoogleAuthProvider } from "firebase/auth";
import { getFirestore } from "firebase/firestore";

const firebaseConfig = {
  apiKey: import.meta.env.VITE_FIREBASE_API_KEY || "PASTE YOUR FIREBASE WEB API KEY HERE",
  authDomain: import.meta.env.VITE_FIREBASE_AUTH_DOMAIN || "PASTE YOUR FIREBASE AUTH DOMAIN HERE",
  projectId: import.meta.env.VITE_FIREBASE_PROJECT_ID || "PASTE YOUR FIREBASE PROJECT ID HERE",
  storageBucket: import.meta.env.VITE_FIREBASE_STORAGE_BUCKET || "PASTE YOUR FIREBASE STORAGE BUCKET HERE",
  messagingSenderId: import.meta.env.VITE_FIREBASE_MESSAGING_SENDER_ID || "PASTE YOUR FIREBASE MESSAGING SENDER ID HERE",
  appId: import.meta.env.VITE_FIREBASE_APP_ID || "PASTE YOUR FIREBASE APP ID HERE",
};

export const isFirebaseConfigured = Object.values(firebaseConfig).every(
  (value) => !value.startsWith("PASTE YOUR "),
);

const app = initializeApp(firebaseConfig);
export const auth = getAuth(app);
export const db = getFirestore(app);
export const googleProvider = new GoogleAuthProvider();

export const ALLOWED_EMAILS = (import.meta.env.VITE_ADMIN_EMAILS || "PASTE YOUR ADMIN EMAIL HERE")
  .split(",")
  .map((email: string) => email.trim().toLowerCase())
  .filter(Boolean);
