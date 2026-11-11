# Setting up Google Cloud (one time only)

The app needs a Google **OAuth Client** to sign in and read calendars.
Everything here is free. It takes about 10 minutes.

> The console UI changes from time to time. If a menu name is different,
> look for the equivalent: today the OAuth settings live under **Google Auth Platform**.

## 1. Create the project

1. Go to <https://console.cloud.google.com/> with your Google account.
2. In the project picker (top bar), **New project** → name `juntos-agenda` → Create.
3. Make sure the new project is selected at the top.

## 2. Enable the APIs

In **APIs & Services → Library**, search for and **Enable**:

- **Google Calendar API**
- **Google Tasks API**

## 3. Consent screen (Google Auth Platform)

**Google Auth Platform** menu → **Get started**:

| Field | Value |
|---|---|
| App name | `Juntos` (shown on the permissions screen) |
| Support e-mail | yours |
| Audience | **External** |
| Contact | your e-mail |

Then, in **Data Access** → **Add or remove scopes**, add:

- `https://www.googleapis.com/auth/calendar.readonly`
- `https://www.googleapis.com/auth/tasks.readonly`

(`openid`, `email` and `profile` are included by default.)

## 4. Test users and publishing

**Now (dev):** in **Audience** → **Test users** → **Add users**, add every e-mail that will use the app
(you and your partner). That's enough for the app to work while it is in *Testing*.

**Publishing:** the **Publish app** button only becomes active once the **Branding** page is complete
(app name, support e-mail, optional logo and, under **App domain**, home page and privacy policy links
+ **Authorized domains**). An authorized domain can't be `localhost`, so in practice publishing waits
for **M4**, when we have our own domain and the `/privacy` and `/terms` pages.

> If the button stays disabled with only the required fields (*) filled in, stay in *Testing*.
> The cost is described below.

**Why publishing matters:** while the app is in *Testing*, Google makes the **refresh token expire after 7 days**.
In dev that's acceptable: when it expires, the app shows "Your Google connection expired" and clicking
**Connect Google Calendar** fixes it. In real use it would be bad: if one of you doesn't reconnect,
their calendar disappears for the other.

In production without verification:

- the **"Google hasn't verified this app"** screen shows up. Click **Advanced → Go to Juntos**.
  That's expected: it's your own app;
- the limit is **100 users**, more than enough for now;
- the official verification (free, requires a custom domain + privacy policy + a video)
  is only needed when opening the app to the public.

## 5. Create the OAuth Client

**Google Auth Platform → Clients → Create client**:

| Field | Value |
|---|---|
| Type | **Web application** |
| Name | `Juntos (dev)` |
| Authorized redirect URIs | `http://localhost:5173/signin-google` |

> **Copy the Client Secret right away** (or download the JSON). The new console only shows the secret
> at creation time. If you lose it, you have to generate another one.

Why `5173`? In dev the browser talks to Vite, which proxies `/signin-google` to the API.
ASP.NET builds the `redirect_uri` from the address the browser used.

When we deploy (M4), we'll add the production URL here, e.g. `https://your-domain/signin-google`.

## 6. Store the credentials (outside the code)

From the repository root:

```bash
dotnet user-secrets set "Google:ClientId" "YOUR_CLIENT_ID.apps.googleusercontent.com" --project src/Agenda.Api
```

```bash
dotnet user-secrets set "Google:ClientSecret" "YOUR_CLIENT_SECRET" --project src/Agenda.Api
```

**user-secrets** keeps the values in a file in your Windows profile
(`%APPDATA%\Microsoft\UserSecrets\`), outside the project folder, so they never end up in Git.
In production, **Azure Key Vault** plays the same role.

## 7. Test it

Follow "Running locally" in the [README](../README.md) and sign in with both accounts.

## Common problems

| Error | Likely cause |
|---|---|
| `redirect_uri_mismatch` | The registered URI isn't exactly `http://localhost:5173/signin-google` (watch out for `https`, the port and a trailing slash), or Vite's proxy is rewriting the Host (`changeOrigin` must be `false`). |
| `access_denied` / app blocked | The app is in *Testing* and the account isn't under "Test users". Add it (step 4). |
| The API doesn't start: "Google:ClientId and Google:ClientSecret are not configured" | Step 6 was skipped. |
| "Your calendar isn't connected yet" after signing in | The calendar permission was unchecked on Google's screen. Click "Connect Google Calendar" and keep it checked. |
