// The /phone softphone (D161), and nothing else — this file is loaded only by that page. JsSIP
// (vendored under lib/jssip) speaks SIP over the app's own WebSocket relay (D160) and WebRTC
// carries the audio; what is here is the glue between that and the dial pad. One call at a time,
// blind transfer only: the PoC's scope, on purpose.

(function () {
    'use strict';

    const page = document.getElementById('phone-client');
    if (!page) {
        return;
    }

    const audio = document.getElementById('phone-audio');
    const callButton = document.getElementById('phone-call');
    const clearButton = document.getElementById('phone-clear');
    const display = document.getElementById('phone-display');
    const extensionSelect = document.getElementById('phone-extension');
    const hangupButton = document.getElementById('phone-hangup');
    const holdButton = document.getElementById('phone-hold');
    const statusLine = document.getElementById('phone-status');
    const xferButton = document.getElementById('phone-xfer');

    // Empty iceServers: no STUN or TURN, because the PoC runs on a LAN or VPN where host
    // candidates reach the server directly (D161). Revisit before this crosses a NAT.
    const callOptions = {
        mediaConstraints: { audio: true, video: false },
        pcConfig: { iceServers: [] }
    };

    let ua = null;        // The one JsSIP.UA, for whichever extension the dropdown names.
    let session = null;   // The one call in progress, incoming or outgoing.
    let registered = false;

    // ---- Status line and buttons: everything visible follows these two. ----

    function setStatus(text) {
        statusLine.textContent = text;
    }

    // Answered and still up, whichever side started it: what hold, DTMF and transfer need.
    function established() {
        return session !== null && session.isEstablished();
    }

    // Ringing here and not yet answered: what turns the Call button into Answer.
    function ringingIn() {
        return session !== null && session.direction === 'incoming' && !session.isEstablished();
    }

    function updateButtons() {
        const digits = display.value.length > 0;

        callButton.textContent = ringingIn() ? 'Answer' : 'Call';
        callButton.disabled = !registered || (session ? !ringingIn() : !digits);
        hangupButton.disabled = session === null;
        holdButton.textContent = established() && session.isOnHold().local ? 'Resume' : 'Hold';
        holdButton.disabled = !established();
        xferButton.disabled = !established() || !digits;
    }

    // ---- Audio. Browsers refuse autoplayed audio until a user gesture has touched the page,
    //      so play() runs from every button that starts a call as well as when the track lands. ----

    function playAudio() {
        const attempt = audio.play();
        if (attempt) {
            // Not a gesture yet, or nothing attached yet: the next press tries again.
            attempt.catch(function () { });
        }
    }

    function hookAudio(connection) {
        connection.addEventListener('track', function (event) {
            // Firefox delivers the remote stream with the track; wrap the bare track otherwise.
            if (event.streams.length > 0) {
                audio.srcObject = event.streams[0];
            } else {
                const stream = new MediaStream();
                stream.addTrack(event.track);
                audio.srcObject = stream;
            }

            playAudio();
        });
    }

    // ---- The UA: one per dropdown choice, replaced whole on a change. ----

    function disconnect() {
        if (session) {
            try {
                session.terminate();
            } catch (ignored) {
                // Already ending; nothing left to say to it.
            }
            session = null;
        }

        if (ua) {
            // The old UA is finished: silence it first, so its shutdown events cannot write
            // over the status line of the UA replacing it.
            ua.removeAllListeners();
            ua.stop();
            ua = null;
        }

        registered = false;
        updateButtons();
    }

    async function connect(number) {
        disconnect();
        setStatus('Connecting…');

        let config;
        try {
            config = await pbx.send('GET', page.dataset.clientUrl + '&ext=' + encodeURIComponent(number));
        } catch (error) {
            setStatus('Not available');
            pbx.toast('error', 'The web client details could not be fetched: ' + error.message);
            return;
        }

        const socket = new JsSIP.WebSocketInterface(config.wsUrl);
        ua = new JsSIP.UA({
            sockets: [socket],
            uri: config.uri,
            password: config.secret,
            display_name: config.name,
            register: true
        });

        ua.on('connected', function () {
            setStatus('Connected, registering…');
        });

        // Covers the WebSocket failing to open at all as well as dropping later: JsSIP surfaces
        // both as 'disconnected' (there is no separate connection-error event on the UA).
        ua.on('disconnected', function () {
            registered = false;
            setStatus('Disconnected');
            updateButtons();
            pbx.toast('error', 'The connection to the phone system dropped. Reload the page to reconnect.');
        });

        ua.on('registered', function () {
            registered = true;
            setStatus('Ready as ' + config.number + ' — ' + config.name);
            updateButtons();
        });

        ua.on('unregistered', function () {
            registered = false;
            setStatus('Unregistered');
            updateButtons();
        });

        ua.on('registrationFailed', function (event) {
            registered = false;
            setStatus('Registration failed');
            updateButtons();
            pbx.toast('error', 'Registration failed: ' + (event.cause || 'unknown'));
        });

        ua.on('newRTCSession', newSession);

        ua.start();
        updateButtons();
    }

    // ---- The call, either direction. ----

    function newSession(event) {
        // One call at a time is the page's whole model: a second incoming call is answered
        // busy rather than juggled.
        if (session) {
            if (event.originator === 'remote') {
                event.session.terminate({ status_code: 486, reason_phrase: 'Busy Here' });
            }
            return;
        }

        session = event.session;

        const peer = session.remote_identity.display_name || session.remote_identity.uri.user;
        setStatus(event.originator === 'remote' ? 'Incoming from ' + peer : 'Calling ' + peer + '…');

        // The audio track arrives on the peer connection, which an incoming session only gets
        // once it is answered — hence both the hook now and the event for later.
        if (session.connection) {
            hookAudio(session.connection);
        }
        session.on('peerconnection', function (data) {
            hookAudio(data.peerconnection);
        });

        session.on('accepted', function () {
            // The dialed digits have done their job. From here the display collects what the
            // keypad sends as DTMF, which doubles as the transfer target.
            display.value = '';
            setStatus('In call with ' + peer);
            playAudio();
            updateButtons();
        });

        session.on('hold', function () {
            setStatus('On hold');
            updateButtons();
        });

        session.on('unhold', function () {
            setStatus('In call with ' + peer);
            updateButtons();
        });

        session.on('ended', function () {
            endSession('Call ended');
        });

        // Rejections, timeouts and a refused microphone all land here; the cause for the
        // last is JsSIP's 'User Denied Media Access'.
        session.on('failed', function (data) {
            endSession('Call failed');
            pbx.toast('error', 'Call failed: ' + (data.cause || 'unknown'));
        });

        updateButtons();
    }

    function endSession(text) {
        session = null;
        setStatus(registered ? text : 'Disconnected');
        updateButtons();
    }

    // ---- The buttons. ----

    function pressCall() {
        playAudio();

        if (ringingIn()) {
            session.answer(callOptions);
        } else if (session === null && ua !== null && display.value.length > 0) {
            ua.call(display.value, callOptions);
        }
    }

    function pressClear() {
        display.value = '';
        updateButtons();
    }

    function pressDigit(digit) {
        playAudio();

        // In a call the pad is a DTMF pad; the digits stay in the display either way, because
        // in-call they are also the transfer target.
        if (established()) {
            session.sendDTMF(digit);
        }

        display.value += digit;
        updateButtons();
    }

    function pressHangup() {
        if (session) {
            try {
                session.terminate();
            } catch (ignored) {
                // Already ending.
            }
        }
    }

    function pressHold() {
        if (!established()) {
            return;
        }

        if (session.isOnHold().local) {
            session.unhold();
        } else {
            session.hold();
        }
    }

    function pressXfer() {
        if (!established() || display.value.length === 0) {
            return;
        }

        // Blind transfer only (D161): a REFER, then Asterisk ends this leg, which lands in
        // the session's 'ended' handler like any other hangup.
        setStatus('Transferring to ' + display.value + '…');
        session.refer(display.value);
    }

    // ---- Wiring. ----

    page.addEventListener('click', function (event) {
        const key = event.target.closest('button[data-digit]');
        if (key) {
            pressDigit(key.dataset.digit);
        }
    });

    callButton.addEventListener('click', pressCall);
    clearButton.addEventListener('click', pressClear);
    hangupButton.addEventListener('click', pressHangup);
    holdButton.addEventListener('click', pressHold);
    xferButton.addEventListener('click', pressXfer);

    extensionSelect.addEventListener('change', function () {
        connect(extensionSelect.value);
    });

    // Unregister on the way out, so Asterisk stops ringing a tab that is gone rather than
    // waiting out the registration. pagehide rather than unload: it also fires into bfcache.
    window.addEventListener('pagehide', function () {
        if (ua) {
            ua.stop();
        }
    });

    connect(extensionSelect.value);
})();
