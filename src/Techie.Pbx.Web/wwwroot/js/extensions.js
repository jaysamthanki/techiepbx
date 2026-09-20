// The extensions page. htmx and Bootstrap do the table, the form modal and the status badges
// between them (D42); what is left here is regenerating the password, which answers with data
// rather than with a piece of the page. The password itself lives in the form (D112).

window.pbxExtensions = (function () {
    'use strict';

    return {
        regenerateSecret: async function (extensionID, number) {
            const confirmed = await Swal.fire({
                icon: 'warning',
                title: 'Regenerate the password for ' + number + '?',
                text: 'The phone keeps working until the next apply, and then has to be set up again with the new password.',
                showCancelButton: true,
                confirmButtonText: 'Regenerate',
                confirmButtonColor: '#dc3545',
                heightAuto: false
            });

            if (!confirmed.isConfirmed) {
                return;
            }

            try {
                const result = await pbx.send('POST', '/api/extensions/' + extensionID + '/secret');

                // A password change is a config change: let the page catch up with the server,
                // and put the new password into the open form, which is the only place it shows (D112).
                htmx.trigger(document.body, 'extensionsChanged');
                document.getElementById('extension-secret').value = result.secret;
            } catch (error) {
                pbx.failed(error);
            }
        }
    };
})();