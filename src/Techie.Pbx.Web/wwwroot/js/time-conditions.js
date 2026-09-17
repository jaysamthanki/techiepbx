// Time conditions: adding and removing the open-hours and holiday rows of the form modal.
// Everything else on the page is htmx; this is here because a row has to be cloned in the
// browser, with its field names re-keyed, so the server can bind the rows the admin left.
(function () {
    'use strict';

    // Keys only have to be unique inside one form. Numbers are what the stored rows use, so
    // anything with a letter in it cannot collide with them.
    var nextKey = (function () {
        var n = 0;
        return function () { return 'n' + n++; };
    }());

    function rekey(html) {
        // The template row carries the placeholder key the page model puts in every name and id.
        return html.replaceAll('__key__', nextKey());
    }

    document.addEventListener('click', function (event) {
        var target = event.target;

        if (target.classList.contains('tc-remove')) {
            var row = target.closest('.tc-row');
            if (row != null)
                row.remove();
            return;
        }

        var add = target.closest('.tc-add-hours, .tc-add-holidays');
        if (add == null)
            return;

        var hours = add.classList.contains('tc-add-hours');
        var template = document.getElementById(hours ? 'tc-hours-template' : 'tc-holidays-template');
        var rows = template.parentElement.querySelector('.tc-rows');

        // importNode rather than innerHTML so the <option selected> of the destination picker in
        // the holiday template survives the copy.
        var fragment = document.createElement('div');
        fragment.innerHTML = rekey(template.innerHTML);
        rows.appendChild(fragment.firstElementChild);
    });
}());