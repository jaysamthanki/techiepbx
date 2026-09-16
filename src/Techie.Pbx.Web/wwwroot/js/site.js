// Glue shared by every page. Three jobs: sweetalert2 toasts, sweetalert2 instead of the
// browser's confirm() for hx-confirm, and starting bootstrap-table on tables that htmx brought
// in. Anything page specific lives in its own file.

window.pbx = {
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

(function () {
    'use strict';

    const tableSelector = 'table[data-toggle="table"]';

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

    // hx-confirm, asked with sweetalert2 rather than the browser's grey box.
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

    // Anything that changed data answers with an HX-Trigger of pbxToast: close whatever modal
    // asked for it and say what happened.
    document.body.addEventListener('pbxToast', function (event) {
        const message = event.detail.message;

        if (Swal.isVisible()) {
            Swal.close();
        }

        window.setTimeout(function () { pbx.toast('success', message); }, 250);
    });

    document.body.addEventListener('htmx:responseError', function (event) {
        pbx.toast('error', 'The server refused that request (' + event.detail.xhr.status + '). Reload the page and try again.');
    });
})();
