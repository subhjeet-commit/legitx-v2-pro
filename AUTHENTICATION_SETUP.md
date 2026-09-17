# Authentication setup

The login pages and desktop login window are kept in the open-source build, but no Firebase project credentials are committed.

For each web panel, create a local `.env.local` file with:

```text
VITE_FIREBASE_API_KEY=PASTE YOUR FIREBASE WEB API KEY HERE
VITE_FIREBASE_AUTH_DOMAIN=PASTE YOUR FIREBASE AUTH DOMAIN HERE
VITE_FIREBASE_PROJECT_ID=PASTE YOUR FIREBASE PROJECT ID HERE
VITE_FIREBASE_STORAGE_BUCKET=PASTE YOUR FIREBASE STORAGE BUCKET HERE
VITE_FIREBASE_MESSAGING_SENDER_ID=PASTE YOUR FIREBASE MESSAGING SENDER ID HERE
VITE_FIREBASE_APP_ID=PASTE YOUR FIREBASE APP ID HERE
VITE_ADMIN_EMAILS=PASTE YOUR ADMIN EMAIL HERE
```

The same Firebase project ID must be supplied to `.firebaserc` before deploying rules. Replace the super-admin placeholder in `firestore.rules` with the same admin email, or create the administrator in the `admins` collection. The desktop WPF app uses the environment variables documented in `LegitX.WPF/AUTHENTICATION_SETUP.md`.

When the variables are absent, the existing login controls remain visible and show a configuration message instead of contacting a previous project. Never commit `.env.local`, service-account JSON, OAuth private keys, or Firebase Admin credentials.
