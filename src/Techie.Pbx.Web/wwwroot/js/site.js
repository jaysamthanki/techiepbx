// Glue shared by every page. The form modals are Bootstrap's and htmx's between them (D42), so
// what is left here is: the JSON calls the pages make, sweetalert2 toasts and confirms, keeping
// the form modal tidy, and starting bootstrap-table on tables htmx brought in.

window.pbx = (function () {
    'use strict';

    const token = document.querySelector('meta[name="request-verification-token"]').content;

    // The Entra session ended. A full navigation signs in again.
    function sessionEnded() {
        window.location.reload();
        return new Error('Your session ended.');
    }

    return {
        // Writes the config files and reloads only what changed. Every page with an apply button
        // calls this; the result is the summary the server wrote.
        applyConfig: async function (button) {
            button.disabled = true;

            try {
                const result = await pbx.send('POST', '/api/config/apply');

                // The banner asks the server again rather than being hidden from here.
                htmx.trigger(document.body, 'configApplied');
                pbx.toast('success', result.summary);
            } catch (error) {
                pbx.failed(error);
            } finally {
                button.disabled = false;
            }
        },

        failed: function (error) {
            Swal.fire({ icon: 'error', title: 'That did not work', text: error.message, heightAuto: false });
        },

        // What a click on a table row does: fetch that row's edit form into the shared modal and
        // open it. The same thing the Add button does with attributes, which a row cannot use
        // because the click has to come from anywhere on it (D48).
        openEdit: function (url) {
            htmx.ajax('GET', url, { target: '#form-modal-content', swap: 'innerHTML' });
            bootstrap.Modal.getOrCreateInstance(document.getElementById('form-modal')).show();
        },

        // A call to one of our own JSON endpoints, with the antiforgery header (D23) and the
        // session-expiry handling (D20) every one of them needs.
        send: async function (method, url) {
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
        },

        toast: function (icon, text) {
            Swal.fire({
                toast: true,
                position: 'top-end',
                icon: icon,
                title: text,
                showConfirmButton: false,
                timer: 5000,
                timerProgressBar: true
            });
        }
    };
})();

(function () {
    'use strict';

    const tableSelector = 'table[data-toggle="table"]';
    const modalPlaceholder = '<div class="modal-body"><p class="text-secondary mb-0">Loading…</p></div>';

    function formModal() {
        return document.getElementById('form-modal');
    }

    // bootstrap-table only initialises tables that were in the page at load, and it rebuilds its
    // rows on sort and search, which throws away htmx's handlers. Re-process after each render.
    function startTables(root) {
        $(root).find(tableSelector).addBack(tableSelector).each(function () {
            const table = this;
            if ($(table).data('bootstrap.table')) {
                return;
            }

            $(table).bootstrapTable({ onPostBody: function () { htmx.process(table); } });
        });
    }

    document.body.addEventListener('htmx:afterSwap', function (event) {
        startTables(event.target);
    });

    // Clicking a row opens its edit form (D48). Delegated from the body, so it keeps working
    // through htmx swaps and bootstrap-table's own re-rendering of the rows.
    document.body.addEventListener('click', function (event) {
        const row = event.target.closest('tr[data-edit-url]');

        // Anything clickable inside a row is itself, not the row.
        if (row && !event.target.closest('a, button, input, select, label')) {
            pbx.openEdit(row.dataset.editUrl);
        }
    });

    // hx-confirm, asked with sweetalert2 rather than the browser's grey box. Confirms are what
    // sweetalert2 is still for (D42).
    document.body.addEventListener('htmx:confirm', function (event) {
        if (!event.detail.question) {
            return;
        }

        event.preventDefault();
        Swal.fire({
            icon: 'warning',
            title: 'Are you sure?',
            text: event.detail.question,
            showCancelButton: true,
            confirmButtonText: 'Yes',
            confirmButtonColor: '#dc3545'
        }).then(function (result) {
            if (result.isConfirmed) {
                event.detail.issueRequest(true);
            }
        });
    });

    // Anything that changed data answers 204 with an HX-Trigger of pbxToast. A save made in the
    // form modal is finished, so the modal closes; a delete made from a row has none open.
    document.body.addEventListener('pbxToast', function (event) {
        const modal = formModal();
        if (modal) {
            bootstrap.Modal.getInstance(modal)?.hide();
        }

        pbx.toast('success', event.detail.message);
    });

    // Put the placeholder back, so opening the modal again never shows the last form for an
    // instant before the new one arrives.
    document.addEventListener('hidden.bs.modal', function (event) {
        if (event.target.id === 'form-modal') {
            document.getElementById('form-modal-content').innerHTML = modalPlaceholder;
        }
    });

    document.body.addEventListener('htmx:responseError', function (event) {
        pbx.toast('error', 'The server refused that request (' + event.detail.xhr.status + '). Reload the page and try again.');
    });
})();
