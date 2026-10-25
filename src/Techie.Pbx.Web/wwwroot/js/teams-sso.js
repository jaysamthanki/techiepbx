// The Teams tab's sign-in (D183). This app was reached without a session — inside the Teams
// client the normal Entra redirect cannot run because Microsoft's login pages refuse to render
// in the tab's iframe — so the shell asks Teams for a token for this app's own API (granted
// silently to the already-signed-in Teams user) and posts it to the exchange endpoint. A 204
// means the server signed the user in with the same cookie the browser flow uses, and the
// reload that follows lands on the real phone.
//
// The antiforgery token goes along, read from the same meta tag site.js reads: the exchange
// endpoint sits behind the same protection as every form post, anonymous by design but still
// token-checked.
//
// In a plain browser the same shell shows the ordinary sign-in instead: TeamsJS is absent, so
// getAuthToken cannot happen and the Entra redirect the iframe can't do is exactly what a
// browser tab can.

(function () {
    'use strict';

    const shell = document.getElementById('teams-sso');
    if (!shell) {
        return;
    }

    const status = document.getElementById('teams-sso-status');
    const errorBox = document.getElementById('teams-sso-error');

    function show(text) {
        status.classList.add('d-none');
        errorBox.classList.remove('d-none');
        errorBox.textContent = text;
    }

    function browserSignIn() {
        // The Microsoft Identity sign-in the layout's own link uses; inside Teams this would be
        // framed and refused, which is why the Teams path runs first and this is only the
        // fallback for a browser that reached the shell without a session.
        const link = document.createElement('a');
        link.href = '/MicrosoftIdentity/Account/SignIn?returnUrl=%2Fphone';
        link.className = 'btn btn-primary';
        link.textContent = 'Sign in';
        shell.appendChild(link);
        show('Open this app in Teams for automatic sign-in, or sign in here.');
    }

    // Plain fetch, not pbx.send: the exchange endpoint is anonymous by design, and pbx.send
    // would treat its 401 as a session that ended and reload the page in a loop.
    async function exchange(token) {
        const antiforgery = document.querySelector('meta[name="request-verification-token"]');

        const response = await fetch(shell.dataset.authUrl, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'RequestVerificationToken': antiforgery ? antiforgery.content : '',
            },
            body: JSON.stringify({ token: token }),
        });

        if (response.status === 204) {
            window.location.reload();
            return;
        }

        const parsed = await response.json().catch(function () { return null; });
        show((parsed && parsed.message) || ('Sign-in failed (' + response.status + ').'));
    }

    // Third-party cookies (D195): inside the Teams client this page is embedded, so its session
    // cookie is a third-party one — and the new Teams client blocks those until the user allows
    // them. Without the grant the exchange below succeeds on every loop while the reload never
    // carries the session, an infinite "Signing you in…" that no server change can fix. The
    // Storage Access API needs a user gesture, so the first visit shows one button; every later
    // visit already holds the grant and runs straight through with nobody clicking anything.
    function storageAccess() {
        if (typeof document.hasStorageAccess !== 'function' || typeof document.requestStorageAccess !== 'function') {
            return Promise.resolve(); // API absent: an older client that allows the cookie anyway.
        }

        return document.hasStorageAccess().then(function (has) {
            if (has) {
                return Promise.resolve();
            }

            return new Promise(function (resolve) {
                const allow = document.createElement('button');
                allow.type = 'button';
                allow.className = 'btn btn-primary';
                allow.textContent = 'Sign in';
                shell.appendChild(allow);
                status.textContent = 'Teams needs your permission to keep you signed in on this device.';

                allow.addEventListener('click', function () {
                    allow.remove();
                    status.textContent = 'Signing you in through Microsoft Teams…';
                    // A refusal resolves too: the exchange may still work, and the failure is
                    // visible on the page rather than a silent loop.
                    document.requestStorageAccess().then(resolve, resolve);
                });
            });
        });
    }

    storageAccess().then(function () {
        return microsoftTeams.app.initialize().then(function () {
            // The shell is up and interactive (sign-in is running or waiting on the user) —
            // that is "loaded" as far as the client's tab spinner is concerned (D196).
            microsoftTeams.app.notifySuccess();
            return true;
        });
    }).then(function () {
        return microsoftTeams.authentication.getAuthToken({
            resources: [shell.dataset.resource],
        });
    }).then(exchange).catch(function (error) {
        // getAuthToken refuses with a message when the caller is not in a Teams client or the
        // token is not available; a browser falls back to the ordinary redirect.
        if (error && error.message && /not in a Teams|inside a Teams|authentication .* required/i.test(error.message)) {
            browserSignIn();
            return;
        }

        show('Teams could not sign you in: ' + ((error && error.message) || 'unknown error') +
            '. Open the phone in a browser to sign in there.');
    });
})();
