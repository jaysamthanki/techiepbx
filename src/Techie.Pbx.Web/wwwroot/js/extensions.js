// The extensions page. htmx and Bootstrap do the table, the form modal and the status badges
// between them (D42); what is left here is the two password actions, which answer with data
// rather than with a piece of the page.

window.pbxExtensions = (function () {
    'use strict';

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

    return {
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
                const result = await pbx.send('POST', '/api/extensions/' + extensionID + '/secret');

                // A password change is a config change: let the page catch up with the server.
                htmx.trigger(document.body, 'extensionsChanged');
                showPassword('New password', result);
            } catch (error) {
                pbx.failed(error);
            }
        },

        showSecret: async function (extensionID) {
            try {
                showPassword('Password', await pbx.send('GET', '/api/extensions/' + extensionID + '/secret'));
            } catch (error) {
                pbx.failed(error);
            }
        }
    };
})();
