// The FreePBX import page (D170). Everything is server-rendered; the one piece of glue here is
// the import button, which stays hollow and disabled until the operator ticks the box saying they
// have read the preview, and says it is working once pressed — an import with a full voicemail
// spool takes a while, and a second press would only find the upload already gone.

(function () {
    'use strict';

    const form = document.querySelector('[data-migration-confirm]');
    if (!form) {
        return;
    }

    const reviewed = form.querySelector('[data-migration-reviewed]');
    const button = form.querySelector('[data-migration-import]');

    reviewed.addEventListener('change', function () {
        button.disabled = !reviewed.checked;
        button.classList.toggle('btn-danger', reviewed.checked);
        button.classList.toggle('btn-outline-danger', !reviewed.checked);
    });

    form.addEventListener('submit', function (event) {
        if (event.submitter === button) {
            button.disabled = true;
            button.textContent = 'Importing…';
        }
    });
})();
