// The trunks page. htmx does the table, the form and the status badges; this file only opens
// sweetalert2 around the form partial and calls the two JSON endpoints. Same shape as
// extensions.js, because the page is the same shape.

window.pbxTrunks = (function () {
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

    return {
        // Writes the config files and reloads only what changed.
        applyConfig: async function (button) {
            button.disabled = true;

            try {
                const result = await send('POST', '/api/config/apply');

                // The banner asks the server again rather than being hidden from here.
                htmx.trigger(document.body, 'configApplied');
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
                    width: '40rem',
                    showConfirmButton: false,
                    didOpen: function () { htmx.process(Swal.getHtmlContainer()); }
                });
            } catch (error) {
                failed(error);
            }
        },

        showSecret: async function (trunkID) {
            try {
                const result = await send('GET', '/api/trunks/' + trunkID + '/secret');

                Swal.fire({
                    icon: 'info',
                    title: 'Password',
                    text: 'The password the provider issued for the ' + result.name + ' trunk.',
                    input: 'text',
                    inputValue: result.secret,
                    inputAttributes: { readonly: 'readonly', spellcheck: 'false' },
                    confirmButtonText: 'Done'
                });
            } catch (error) {
                failed(error);
            }
        }
    };
})();
