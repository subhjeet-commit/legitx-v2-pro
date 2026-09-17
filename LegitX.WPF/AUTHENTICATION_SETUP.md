# Authentication setup

The login window is intentionally retained, but this open-source checkout does not ship Firebase or Google project credentials.

Set these environment variables locally before starting the app:

```powershell
$env:LEGITX_FIREBASE_API_KEY = "PASTE YOUR FIREBASE WEB API KEY HERE"
$env:LEGITX_GOOGLE_CLIENT_ID = "PASTE YOUR GOOGLE OAUTH CLIENT ID HERE"
$env:LEGITX_GOOGLE_CLIENT_SECRET = "PASTE YOUR GOOGLE OAUTH CLIENT SECRET HERE"
$env:LEGITX_FIRESTORE_BASE_URL = "https://firestore.googleapis.com/v1/projects/YOUR_PROJECT_ID/databases/(default)/documents"
$env:LEGITX_FIREBASE_APP_URL = "https://YOUR_PROJECT_ID.firebaseapp.com"
dotnet run --project .\LegitX.WPF.csproj
```

The Google OAuth client must allow `http://localhost:43821/callback/` as its redirect URI. Firebase Auth must have Email/Password and Google providers enabled. Never commit these values or a service-account key; the desktop app only needs the client-side Firebase Web API key and OAuth client configuration.

When the variables are absent, clicking either login button keeps the UI intact and shows the configuration message instead of making a request to someone else's Firebase project.
