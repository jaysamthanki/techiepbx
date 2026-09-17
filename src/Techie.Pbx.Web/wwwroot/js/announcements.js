// The announcements page. The table, the modal and the upload are htmx's and Bootstrap's between
// them (D42, D48); what is left here is the one thing they cannot do, which is record from the
// microphone.
//
// MediaRecorder gives back a Blob. Rather than a second upload endpoint for it, the blob is put
// into the form's own file input, so a recording and a chosen file take exactly the same path to
// the server and get exactly the same conversion and checks (D55).

(function () {
    'use strict';

    // Chrome and Firefox record WebM/Opus, Safari and iPhones record MP4/AAC. Both are containers
    // the server accepts, and it works out which from the bytes rather than from this name.
    const fileNames = { 'audio/webm': 'recording.webm', 'audio/mp4': 'recording.mp4', 'audio/ogg': 'recording.ogg' };

    function supported() {
        return !!(navigator.mediaDevices && window.MediaRecorder && window.DataTransfer);
    }

    function fileNameFor(type) {
        const container = (type || '').split(';')[0];
        return fileNames[container] || 'recording.webm';
    }

    // The only way to put a Blob into an <input type="file"> the form will post.
    function attach(input, blob) {
        const transfer = new DataTransfer();
        transfer.items.add(new File([blob], fileNameFor(blob.type), { type: blob.type }));
        input.files = transfer.files;
    }

    function start(fieldset) {
        const button = fieldset.querySelector('[data-audio-record]');
        const status = fieldset.querySelector('[data-audio-status]');
        const preview = fieldset.querySelector('[data-audio-preview]');
        const input = fieldset.querySelector('[data-audio-input]');

        navigator.mediaDevices.getUserMedia({ audio: true }).then(function (stream) {
            const recorder = new MediaRecorder(stream);
            const chunks = [];

            recorder.ondataavailable = function (event) { chunks.push(event.data); };

            recorder.onstop = function () {
                stream.getTracks().forEach(function (track) { track.stop(); });

                const blob = new Blob(chunks, { type: recorder.mimeType });
                attach(input, blob);

                preview.src = URL.createObjectURL(blob);
                preview.classList.remove('d-none');
                button.textContent = 'Record again';
                button.classList.remove('btn-danger');
                button.classList.add('btn-outline-secondary');
                status.textContent = 'Recorded. Save to convert and store it.';
                fieldset.dataset.recording = '';
            };

            recorder.start();
            fieldset.recorder = recorder;
            fieldset.dataset.recording = 'yes';
            button.textContent = 'Stop';
            button.classList.remove('btn-outline-secondary');
            button.classList.add('btn-danger');
            status.textContent = 'Recording…';
        }).catch(function () {
            status.textContent = 'The microphone is not available. Check the browser\'s permission, or upload a file instead.';
        });
    }

    function onClick(event) {
        const button = event.target.closest('[data-audio-record]');
        if (!button) {
            return;
        }

        const fieldset = button.closest('[data-announcement-audio]');
        if (fieldset.dataset.recording === 'yes') {
            fieldset.recorder.stop();
        } else {
            start(fieldset);
        }
    }

    // The recorder controls are hidden in the markup and shown here, so a browser that cannot
    // record (or a page served over plain HTTP, where getUserMedia is not allowed) simply offers
    // the file picker instead of a button that would fail.
    function reveal(root) {
        if (!supported()) {
            return;
        }

        root.querySelectorAll('[data-audio-recorder]').forEach(function (recorder) {
            recorder.classList.remove('d-none');
            recorder.classList.add('d-flex');
        });
    }

    document.body.addEventListener('click', onClick);

    // The form arrives in the modal by htmx swap, so the controls are revealed after each one.
    document.body.addEventListener('htmx:afterSwap', function (event) { reveal(event.target); });
    document.addEventListener('DOMContentLoaded', function () { reveal(document); });
})();
