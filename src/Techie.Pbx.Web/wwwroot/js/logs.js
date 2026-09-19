// The Logs page's one job of its own: keep the tail pinned to the bottom after every poll, so
// "Follow" reads like a follow. Everything else - the poll, the toolbar, the filter - is htmx's
// hx-include and hx-trigger (see Logs.cshtml).

(function () {
    'use strict';

    document.body.addEventListener('htmx:afterSwap', function (event) {
        if (event.target.id !== 'log-view') {
            return;
        }

        const pre = event.target.querySelector('pre');
        if (pre) {
            pre.scrollTop = pre.scrollHeight;
        }
    });
})();
