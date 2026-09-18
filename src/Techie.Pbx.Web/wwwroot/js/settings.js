// The settings page. htmx and Bootstrap do the table and the form modal between them (D42), and
// hx-confirm asks sweetalert2 before a reset; what is left here is the two things a secret needs:
// showing what is being typed, and reading the stored one back from the API (D68).

window.pbxSettings = (function () {
    'use strict';

    return {
        showSecret: async function (key) {
            try {
                const result = await pbx.send('GET', '/api/settings/secret?key=' + encodeURIComponent(key));

                Swal.fire({
                    icon: 'info',
                    title: 'Stored value',
                    text: 'The value stored for ' + result.key + '.',
                    input: 'text',
                    inputValue: result.secret,
                    inputAttributes: { readonly: 'readonly', spellcheck: 'false' },
                    confirmButtonText: 'Done',

                    // Opened from the edit modal (D48); heightAuto would shift it.
                    heightAuto: false
                });
            } catch (error) {
                pbx.failed(error);
            }
        },

        // The password box's show/hide. It reveals what the admin is typing now — the stored
        // value is never in the page to reveal.
        toggle: function (elementID, button) {
            const input = document.getElementById(elementID);
            const hidden = input.type === 'password';

            input.type = hidden ? 'text' : 'password';
            button.textContent = hidden ? 'Hide' : 'Show';
        }
    };
})();
