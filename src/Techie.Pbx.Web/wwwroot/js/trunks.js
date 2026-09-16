// The trunks page. htmx and Bootstrap do the table, the form modal and the status badges between
// them (D42); what is left here is reading back the provider's password, which answers with data
// rather than with a piece of the page.

window.pbxTrunks = (function () {
    'use strict';

    return {
        showSecret: async function (trunkID) {
            try {
                const result = await pbx.send('GET', '/api/trunks/' + trunkID + '/secret');

                Swal.fire({
                    icon: 'info',
                    title: 'Password',
                    text: 'The password the provider issued for the ' + result.name + ' trunk.',
                    input: 'text',
                    inputValue: result.secret,
                    inputAttributes: { readonly: 'readonly', spellcheck: 'false' },
                    confirmButtonText: 'Done',

                    // Opened from the edit modal now (D48); heightAuto would shift it.
                    heightAuto: false
                });
            } catch (error) {
                pbx.failed(error);
            }
        }
    };
})();
