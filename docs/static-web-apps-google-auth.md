# Google Authentication for Azure Static Web Apps

This document explains how to add Gmail sign-in to an Azure Static Web App that uses:

- a static frontend under `site/`
- managed Azure Functions under `api/`
- Azure Static Web Apps built-in authentication

This is the setup now used in this repo for the submit flow in [site/submit/index.html](C:\Development\labs\burghindian\site\submit\index.html).

## Overview

The authentication flow is split into three parts:

1. Google provides the OAuth client registration.
2. Azure Static Web Apps handles the actual sign-in flow and cookie/session management.
3. Our frontend calls `/api/auth/session` to check whether the user is signed in and to read the Gmail email address.

This is important because the app itself is not implementing OAuth manually. Azure Static Web Apps is doing that for us.

## Why Azure asks for Client ID and Client Secret

In Azure Static Web Apps Authentication, when you add `Google` as a provider, Azure asks for:

- `Client ID`
- `Client Secret`

These values come from Google Cloud Console after you create an OAuth client for your site.

Azure needs them because:

- the `Client ID` identifies your site to Google
- the `Client Secret` proves to Google that Azure Static Web Apps is allowed to complete the OAuth flow for that app

In Azure portal, this appears in the `Authentication` screen when you add or edit the Google provider.

## Why Google Console needs these two URLs

For this site, the Google OAuth app was configured with:

- `https://gentle-desert-0872cca0f.7.azurestaticapps.net`
- `https://gentle-desert-0872cca0f.7.azurestaticapps.net/.auth/login/google/callback`

These are entered in Google Cloud Console as:

- `Authorized JavaScript origins`
- `Authorized redirect URIs`

### Authorized JavaScript origin

Use:

`https://gentle-desert-0872cca0f.7.azurestaticapps.net`

Why this is needed:

- it tells Google which website is allowed to start the OAuth flow
- it must match the actual domain of the Azure Static Web App
- Google uses this to prevent another site from pretending to be your app

Generic pattern:

`https://<your-static-web-app>.azurestaticapps.net`

If you also use a custom domain, add that too.

### Authorized redirect URI

Use:

`https://gentle-desert-0872cca0f.7.azurestaticapps.net/.auth/login/google/callback`

Why this is needed:

- after the user signs in at Google, Google must know where it is allowed to send the user back
- Azure Static Web Apps owns the `/.auth/login/google/callback` endpoint
- this callback is where Azure receives the Google authentication result and establishes the signed-in session

Without this exact callback URI, Google sign-in will fail or Azure will never receive the login result.

Generic pattern:

`https://<your-static-web-app>.azurestaticapps.net/.auth/login/google/callback`

If you use a custom domain, also add:

`https://<your-custom-domain>/.auth/login/google/callback`

## Azure Static Web Apps Authentication settings

In Azure portal:

1. Open the Static Web App.
2. Go to `Authentication`.
3. Switch to `Custom` mode.
4. Add the `Google` provider.
5. Paste the Google `Client ID`.
6. Paste the Google `Client Secret`.
7. Set `Role assignments API path`.
8. Click `Apply`.

For this repo, the important value is:

`/api/GetRoles`

## Why Role assignments API path is required

When Azure Static Web Apps is in `Custom` authentication mode, it expects an API endpoint that returns the custom roles for a signed-in user.

In our case, we are not assigning any custom roles yet. We only need authentication. But Azure still requires the endpoint to exist.

That endpoint is implemented in [api/Functions/RoleFunctions.cs](C:\Development\labs\burghindian\api\Functions\RoleFunctions.cs).

It returns:

```json
{
  "roles": []
}
```

That means:

- user is authenticated
- no extra custom roles are being assigned

The function route is:

`POST /api/GetRoles`

The Azure Authentication blade should therefore use:

`/api/GetRoles`

## Login and logout URLs

Once Google is configured in Azure Static Web Apps, Azure exposes these built-in routes:

- Login: `/.auth/login/google`
- Logout: `/.auth/logout`
- User info: `/.auth/me`

These are not pages we created manually. Azure Static Web Apps provides them.

## Frontend login flow

The frontend logic is in [site/submit/index.html](C:\Development\labs\burghindian\site\submit\index.html).

When the user clicks the Gmail button, this code runs:

```js
loginButton.addEventListener('click', () => {
    const redirectTarget = encodeURIComponent(buildSubmitRedirectUrl());
    window.location.href = `/.auth/login/google?post_login_redirect_uri=${redirectTarget}`;
});
```

What this does:

1. Sends the browser to Azure Static Web Apps login route: `/.auth/login/google`
2. Tells Azure where to send the user after login succeeds
3. Returns the user to the submit page instead of a generic auth page

For logout, the page uses:

```js
window.location.href = `/.auth/logout?post_logout_redirect_uri=${redirectTarget}`;
```

This signs the user out and returns them to the submit page.

## How `/api/auth/session` works

After login, the frontend needs a simple way to know:

- is the user authenticated?
- what Gmail email did Azure attach to this session?

That is why we created [api/Functions/AuthFunctions.cs](C:\Development\labs\burghindian\api\Functions\AuthFunctions.cs).

It exposes:

`GET /api/auth/session`

The function returns a small payload like:

```json
{
  "authenticated": true,
  "email": "user@gmail.com"
}
```

If the user is not signed in:

```json
{
  "authenticated": false,
  "email": ""
}
```

This endpoint is intentionally simple so the frontend can check auth state without parsing the full Static Web Apps principal payload.

For this deployed site, you can verify authentication directly by opening:

`https://gentle-desert-0872cca0f.7.azurestaticapps.net/api/auth/session`

If the user is signed in with Google, the response shows:

```json
{
  "authenticated": true,
  "email": "your@gmail.com"
}
```

If the user is not signed in, the response shows:

```json
{
  "authenticated": false,
  "email": ""
}
```

This is the quickest way to confirm that:

- Google sign-in completed successfully
- Azure Static Web Apps created the authenticated session
- the managed Function can read the logged-in user's Gmail email

## How the API gets the Gmail email

Azure Static Web Apps sends the authenticated user information to Functions in the `x-ms-client-principal` header.

Our helper method in [api/Services/AuthHelpers.cs](C:\Development\labs\burghindian\api\Services\AuthHelpers.cs) does this:

1. reads `x-ms-client-principal`
2. decodes the Base64 JSON payload
3. scans claims for an email field
4. falls back to `userDetails` if needed
5. returns the email in lowercase

The relevant claim types we currently support are:

- `emails`
- `email`
- `http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress`

This is why the API code does not need to call Google directly. Azure already completed the sign-in and passed the identity details to our function.

## How the frontend checks auth state

The submit page uses this function:

```js
const refreshAuthSession = async () => {
    try {
        const response = await fetch('/api/auth/session', {
            headers: {
                'Accept': 'application/json'
            },
            cache: 'no-store'
        });

        if (!response.ok) {
            signedInEmail = '';
            updateAuthUi();
            return;
        }

        const session = await response.json();
        signedInEmail = session?.authenticated ? String(session.email || '').trim().toLowerCase() : '';
        updateAuthUi();
    } catch (error) {
        signedInEmail = '';
        updateAuthUi();
    }
};
```

What it does:

1. calls `GET /api/auth/session`
2. checks whether the API says the user is authenticated
3. stores the Gmail email in `signedInEmail`
4. updates the page UI

The UI then:

- shows the signed-in Gmail email
- unlocks the form fields
- keeps the email field read-only
- uses that email as the ownership identity for future updates

## Recommended setup steps for a new app

Use this order when repeating the setup elsewhere.

1. Deploy the Azure Static Web App and note the site URL.
2. Create a Google OAuth client in Google Cloud Console.
3. Add the site root as an `Authorized JavaScript origin`.
4. Add `/.auth/login/google/callback` as an `Authorized redirect URI`.
5. In Azure Static Web Apps, upgrade to `Standard` if custom auth is needed.
6. In Azure `Authentication`, choose `Custom`.
7. Add the `Google` provider.
8. Paste the Google `Client ID` and `Client Secret`.
9. Add `Role assignments API path = /api/GetRoles`.
10. Deploy a small role endpoint that returns `{ "roles": [] }`.
11. Add a simple `GET /api/auth/session` endpoint.
12. Update the frontend to use:
    - `/.auth/login/google`
    - `/.auth/logout`
    - `/api/auth/session`

## Quick verification checklist

After setup, verify these URLs in order:

1. `https://<your-site>/.auth/login/google`
2. `https://<your-site>/.auth/me`
3. `https://<your-site>/api/auth/session`

Expected results:

- `/.auth/me` should show a non-null `clientPrincipal`
- `/api/auth/session` should show:

```json
{
  "authenticated": true,
  "email": "your@gmail.com"
}
```

If `/.auth/me` still shows:

```json
{"clientPrincipal": null}
```

then Azure did not complete the authentication session yet. That usually means:

- Google redirect URI is wrong
- Azure Google provider was not saved correctly
- `Role assignments API path` is missing in Custom mode
- the app is still using the wrong provider

## Reusable URL templates

For another Azure Static Web App, replace the host only.

Google Console:

- Authorized JavaScript origin:
  - `https://<your-site>.azurestaticapps.net`
- Authorized redirect URI:
  - `https://<your-site>.azurestaticapps.net/.auth/login/google/callback`

Azure Static Web Apps:

- Login path:
  - `/.auth/login/google`
- Logout path:
  - `/.auth/logout`
- Role assignments API path:
  - `/api/GetRoles`
- Session endpoint:
  - `/api/auth/session`

## Files in this repo related to auth

- Frontend login flow: [site/submit/index.html](C:\Development\labs\burghindian\site\submit\index.html)
- Session endpoint: [api/Functions/AuthFunctions.cs](C:\Development\labs\burghindian\api\Functions\AuthFunctions.cs)
- Role endpoint: [api/Functions/RoleFunctions.cs](C:\Development\labs\burghindian\api\Functions\RoleFunctions.cs)
- Email extraction helper: [api/Services/AuthHelpers.cs](C:\Development\labs\burghindian\api\Services\AuthHelpers.cs)
- Route protection: [site/staticwebapp.config.json](C:\Development\labs\burghindian\site\staticwebapp.config.json)

## Notes

- This setup uses Gmail login as the identity source.
- The app uses the Gmail email as the ownership key for submissions.
- Azure Static Web Apps manages the login session.
- Our Functions only read the authenticated identity and return a simplified session payload to the frontend.
