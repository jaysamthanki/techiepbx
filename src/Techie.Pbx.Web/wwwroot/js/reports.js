// The quick date buttons on the call reports page. The server has already worked out each range in
// the site's zone (ReportRange); this copies it into the two date inputs and fires one change, which
// is what htmx is listening for to fetch the results again.
(function () {
    'use strict';

    document.getElementById('report-filters')?.addEventListener('click', function (event) {
        const button = event.target.closest('button[data-report-from]');
        if (!button) {
            return;
        }

        document.getElementById('report-from').value = button.dataset.reportFrom;

        const to = document.getElementById('report-to');
        to.value = button.dataset.reportTo;
        to.dispatchEvent(new Event('change', { bubbles: true }));
    });
})();
