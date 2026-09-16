// The extensions page. htmx does the table, the form and the status badges; this file only
// opens sweetalert2 around the form partial and calls the three JSON endpoints.

window.pbxExtensions = (function () {
    'use strict';

    const token = document.querySelector('meta[name="request-verification-token"]').content;

    // The Entra session ended. A full navigation signs in again.
    function sessionEnded() {
        window.location.reload();
        return new Error('Your session ended.');
    }

    async function send(method, url) {
        const response = await fetch(url, {
            method: method,
            // The header name is Program.AntiforgeryHeaderName.
            headers: { 'RequestVerificationToken': token, 'Accept': 'application/json' }
        });

        if (response.status === 401) {
            throw sessionEnded();
        }

        const body = await response.json().catch(function () { return null; });
        if (!response.ok) {
            throw new Error((body && body.message) || (response.status + ' ' + response.statusText));
        }

        return body;
    }

    function failed(error) {
        Swal.fire({ icon: 'error', title: 'That did not work', text: error.message });
    }

    function pendingApply() {
        document.getElementById('apply-pending').classList.remove('d-none');
    }

    function showPassword(title, result) {
        Swal.fire({
            icon: 'info',
            title: title,
            text: 'SIP password for extension ' + result.number + '. Put this into the phone.',
            input: 'text',
            inputValue: result.secret,
            inputAttributes: { readonly: 'readonly', spellcheck: 'false' },
            confirmButtonText: 'Done'
        });
    }

    document.body.addEventListener('configChanged', pendingApply);

    return {
        // Writes the config files and reloads only what changed.
        applyConfig: async function (button) {
            button.disabled = true;

            try {
                const result = await send('POST', '/api/config/apply');
                document.getElementById('apply-pending').classList.add('d-none');
                pbx.toast('success', result.summary);
            } catch (error) {
                failed(error);
            } finally {
                button.disabled = false;
            }
        },

        // The create and edit form, rendered by the page and shown in a modal.
        openModal: async function (url) {
            try {
                // HX-Request makes an expired session answer 401 rather than redirecting to
                // Entra, which a fetch cannot follow (D20).
                const response = await fetch(url, { headers: { 'Accept': 'text/html', 'HX-Request': 'true' } });
                if (response.status === 401) {
                    throw sessionEnded();
                }

                if (!response.ok) {
                    throw new Error('The form could not be loaded (' + response.status + ').');
                }

                Swal.fire({
                    html: await response.text(),
                    width: '34rem',
                    showConfirmButton: false,
                    didOpen: function () { htmx.process(Swal.getHtmlContainer()); }
                });
            } catch (error) {
                failed(error);
            }
        },

        regenerateSecret: async function (extensionID, number) {
            const confirmed = await Swal.fire({
                icon: 'warning',
                title: 'Regenerate the password for ' + number + '?',
                text: 'The phone keeps working until the next apply, and then has to be set up again with the new password.',
                showCancelButton: true,
                confirmButtonText: 'Regenerate',
                confirmButtonColor: '#dc3545'
            });

            if (!confirmed.isConfirmed) {
                return;
            }

            try {
                const result = await send('POST', '/api/extensions/' + extensionID + '/secret');
                pendingApply();
                showPassword('New password', result);
            } catch (error) {
                failed(error);
            }
        },

        showSecret: async function (extensionID) {
            try {
                showPassword('Password', await send('GET', '/api/extensions/' + extensionID + '/secret'));
            } catch (error) {
                failed(error);
            }
        }
    };
})();
