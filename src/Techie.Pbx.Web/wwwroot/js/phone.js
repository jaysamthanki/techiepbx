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
    const ringtone = document.getElementById('phone-ring');
    const callButton = document.getElementById('phone-call');
    const clearButton = document.getElementById('phone-clear');
    const display = document.getElementById('phone-display');
    const extensionSelect = document.getElementById('phone-extension');
    const hangupButton = document.getElementById('phone-hangup');
    const holdButton = document.getElementById('phone-hold');
    const statusLine = document.getElementById('phone-status');
    const unreadBadge = document.getElementById('phone-unread');
    const unreadCount = document.getElementById('phone-unread-count');
    const voicemailButton = document.getElementById('phone-voicemail');
    const xferButton = document.getElementById('phone-xfer');

    // iceServers starts empty — a LAN or VPN needs no STUN, and the handler only sends one
    // when the Sip.StunServer setting is set (D163) — and gains it per connection, because
    // the server hands it to the page with the rest of the registration details.
    const callOptions = {
        mediaConstraints: { audio: true, video: false },
        pcConfig: { iceServers: [] }
    };

    let ua = null;        // The one JsSIP.UA, for whichever extension the dropdown names.
    let session = null;   // The one call in progress, incoming or outgoing.
    let registered = false;
    let connecting = false;       // A connect() is in flight; pageshow must not race it.
    let autoReconnecting = false; // A bfcache resume is driving a reconnect: no error toast.

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
        voicemailButton.disabled = !registered || session !== null;
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

    // ---- Ringing (D178): a looped local asset plus a flashing tab title, so an incoming call
    //      is audible in the tab and visible from any other one. The same autoplay rule as the
    //      call audio applies — a page the user has never touched stays silent, which the
    //      title flash covers; every gesture press starts the ring too, so the common case
    //      (signed in, tab open, something pressed at least once) rings out loud. ----

    const baseTitle = document.title;
    let ringFlash = null;

    function startRinging(peer) {
        if (!ringFlash) {
            ringtone.currentTime = 0;
            const attempt = ringtone.play();
            if (attempt) {
                attempt.catch(function () { });
            }

            // Alternate the title between the base and the caller, about twice a second.
            ringFlash = setInterval(function () {
                document.title = document.title === baseTitle
                    ? 'Incoming call — ' + peer
                    : baseTitle;
            }, 500);
        }
    }

    function stopRinging() {
        if (ringFlash) {
            clearInterval(ringFlash);
            ringFlash = null;
            document.title = baseTitle;
        }
        ringtone.pause();
        ringtone.currentTime = 0;
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
        stopRinging();

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
        connecting = true;
        setStatus('Connecting…');

        let config;
        try {
            config = await pbx.send('GET', page.dataset.clientUrl + '&ext=' + encodeURIComponent(number));
        } catch (error) {
            connecting = false;
            setStatus('Not available');
            pbx.toast('error', 'The web client details could not be fetched: ' + error.message);
            return;
        }

        // What the handler sent: STUN when the setting is set (D163), nothing when it is not.
        callOptions.pcConfig.iceServers = config.stun ? [{ urls: config.stun }] : [];

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
            // A bfcache-driven reconnect suppresses this toast: the drop is the parked page
            // being reactivated, and pageshow reconnects it in the next turn (D196).
            if (!autoReconnecting) {
                pbx.toast('error', 'The connection to the phone system dropped. Reload the page to reconnect.');
            }
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
        connecting = false;
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

        // An inbound invite rings until it is answered or goes away (D178).
        if (event.originator === 'remote') {
            startRinging(peer);
        }

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
            stopRinging();
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
        stopRinging();
        setStatus(registered ? text : 'Disconnected');
        updateButtons();
    }

    // ---- The buttons. ----

    function pressBackspace() {
        display.value = display.value.slice(0, -1);
        updateButtons();
    }

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
        // in-call they are also the transfer target. '+' (keyboard only) has no DTMF tone.
        if (established() && digit !== '+') {
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

    // *97 from the registered extension: Asterisk opens that extension's own mailbox.
    function pressVoicemail() {
        if (session === null && ua !== null && registered) {
            playAudio();
            display.value = page.dataset.voicemailCode;
            ua.call(display.value, callOptions);
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

    // ---- The unread voicemail badge (D169): the user's own mailboxes, hidden at zero. ----

    async function refreshUnread() {
        let unread = 0;
        try {
            unread = (await pbx.send('GET', page.dataset.unreadUrl)).unread;
        } catch (ignored) {
            // No mailbox of their own (a 404) or the server is away: no badge either way.
        }

        unreadCount.textContent = unread;
        unreadBadge.classList.toggle('d-none', !(unread > 0));
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
    voicemailButton.addEventListener('click', pressVoicemail);
    xferButton.addEventListener('click', pressXfer);

    // The physical keyboard drives the pad too. Form fields keep their keys (the dropdown
    // jumps by typed digit), except the display, which is read-only and so has none of its
    // own. A focused button keeps Enter and Space, which press it; digits still reach the pad,
    // so typing goes on working after a keypad click. Shortcuts with modifiers are left alone.
    document.addEventListener('keydown', function (event) {
        if (event.ctrlKey || event.altKey || event.metaKey) {
            return;
        }

        const target = event.target;
        if (target !== display && target.closest('input, textarea, select')) {
            return;
        }

        if (/^[0-9*#+]$/.test(event.key)) {
            event.preventDefault();
            pressDigit(event.key);
        } else if (event.key === 'Backspace') {
            event.preventDefault();
            pressBackspace();
        } else if (event.key === 'Enter' && !target.closest('button')) {
            event.preventDefault();
            pressCall();
        }
    });

    extensionSelect.addEventListener('change', function () {
        connect(extensionSelect.value);
    });

    // The Teams client keeps its "loading" spinner over the tab until the page says it is
    // ready (D196): initialize + notifySuccess. The SSO shell does the same on its own page
    // now, so every state this tab can render clears the spinner. Outside Teams (a plain
    // browser) initialize rejects; there is no spinner to clear and the failure is ignored.
    let teamsNotified = false;

    function clearTeamsSpinner() {
        if (teamsNotified || typeof microsoftTeams === 'undefined') {
            return;
        }

        teamsNotified = true;
        microsoftTeams.app.initialize().then(function () {
            microsoftTeams.app.notifySuccess();
        }, function () {
            // Not in a Teams frame: nothing to clear.
        });
    }

    // Navigating away inside Teams either destroys the tab's iframe or parks it in bfcache
    // (D196). A parked frame resumes alive with its sockets intact, so stopping the UA here
    // would kill a registration the page keeps using; only a frame that is truly going away
    // (persisted=false) unregisters. A destroyed frame's socket dies with the tab and its
    // Asterisk registration simply expires.
    window.addEventListener('pagehide', function (event) {
        stopRinging();
        if (!event.persisted && ua) {
            ua.stop();
        }
    });

    // A frame resumed out of bfcache reconnects if its socket dropped while parked. The check
    // waits one turn: isConnected() can still report the pre-freeze state when pageshow fires,
    // because the queued WebSocket close event runs after reactivation (agy review, D196). A
    // connect() in flight wins — this must not race it. While the reconnect runs the
    // 'disconnected' toast is suppressed via autoReconnecting.
    window.addEventListener('pageshow', function (event) {
        if (!event.persisted) {
            return;
        }

        autoReconnecting = true;
        setTimeout(function () {
            if (!connecting && (!ua || !ua.isConnected())) {
                connect(extensionSelect.value);
            }

            autoReconnecting = false;
        }, 0);
    });

    connect(extensionSelect.value);
    clearTeamsSpinner();

    refreshUnread();
    setInterval(refreshUnread, 30000);
})();
