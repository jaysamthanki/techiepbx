# Feature list

What TNPBX needs to do, as agreed with the user. This is the scope: anything not listed here
needs a conversation before it gets built. Build order lives in [roadmap.md](roadmap.md).

Status: **Done**, **Partial**, **Planned**.

| # | Feature | Status |
|---|---|---|
| F1 | [Trunks](#f1-trunks): Callcentric wizard + generic SIP trunk | Planned |
| F2 | [Extensions](#f2-extensions) | Partial |
| F2a | [Voicemail](#f2a-voicemail) | Partial |
| F2b | [Follow me](#f2b-follow-me) | Planned |
| F3 | [Ring groups](#f3-ring-groups) (including hunt groups) | Planned |
| F4 | [Email notifications](#f4-email-notifications) | Partial (voicemail done) |
| F5 | [Call reports](#f5-call-reports) | Planned |
| F6 | [IVRs](#f6-ivrs) | Partial |
| F7 | [Announcements](#f7-announcements) | Partial |
| F8 | [Time conditions](#f8-time-conditions) | Planned |

Plus the [supporting pieces](#supporting-pieces) these features can't work without.

---

## F1. Trunks

Connections to a SIP provider for calls to and from the phone network.

**Callcentric wizard.** The user puts all clients on Callcentric, so onboarding it should take
a few fields instead of knowing PJSIP. Roughly: account number, extension, SIP password,
DIDs. The wizard creates a normal trunk record with Callcentric's settings filled in, so after
creation it's an ordinary trunk.

**Generic SIP trunk.** For anything else: server host/port, username, auth username, password,
whether to register, caller ID settings, codecs, and the provider's IP addresses for matching
inbound calls.

Notes:
- Rendered into `pjsip.conf` as `registration` + `endpoint` + `auth` + `aor` + `identify` sections.
- Callcentric's exact PJSIP settings (server name, username format, inbound IP ranges, how DIDs
  arrive) need to be taken from Callcentric's current Asterisk documentation and verified on
  the lab VM before being hard-coded into the wizard.
- Show registration status live via AMI.
- Needs [outbound routes](#supporting-pieces) and [inbound routes](#supporting-pieces) to be useful.

## F2. Extensions

**Done:** model, schema, repository, `pjsip.conf` and dialplan generation, tests. UI (table +
modals), apply config via AMI, live registration status, show or regenerate the SIP password —
all written, none of it run against the lab VM yet (see [roadmap.md](roadmap.md#piece-5-detail-code-done-2026-09-15)).

**Done 2026-09-21:** per-extension caller ID for outbound calls (D125). An extension names its own
outbound caller ID — a user with a direct DID calls out as that DID — and it beats the route's and
the trunk's. Stored as `Extensions.OutboundCallerID` (schema 022), carried on the endpoint as
`set_var = TNPBX_CID=...` and applied by the outbound route contexts, so internal calls still show
the extension's own name and number.

## F2a. Voicemail

A voicemail box per extension (optional).

**Done (2026-09-16):** the box itself — PIN, email address, attach and delete-after-email
settings on the extension (D27), `voicemail.conf` generation, busy/unanswered fallback to the
mailbox and `*97` to listen (D29), and the fields in the extension modal.

**Done 2026-09-21:** sending the email (D126). `voicemail.conf` names a `mailcmd` — a fixed,
root-owned script in this repo — which relays what app_voicemail composed through the `Mail.Smtp.*`
settings, so there is no mail server on the box and one set of credentials on it. `format` is
`wav49|g722` now, because the attachment is the first format in that list.

**Still to do:** voicemail as a destination for inbound routes and IVRs (needs destinations), and
MWI. Voicemail email is SMTP only: a site whose `Mail.Transport` is Graph has to fill in the SMTP
relay as well (D126).

- PIN, email address, whether to attach the recording to the email, whether to delete after emailing.
- Unanswered or busy calls to an extension go to its voicemail.
- A feature code to check your own voicemail, e.g. `*97` (FreePBX convention).
- Voicemail can also be a destination for inbound routes, IVR options and ring group failover.
- Asterisk's `app_voicemail` stores messages on disk; email delivery links to [F4](#f4-email-notifications).

## F2b. Follow me

When an extension doesn't answer, try other numbers (for example a mobile) before voicemail.

- Per extension: enabled, list of numbers (internal or external), ring time per step,
  ring the extension first or together with the list.
- Optional "press 1 to accept" confirmation so a mobile's own voicemail doesn't grab the call.
- Calls to external numbers go out through outbound routes, so they cost money: see the
  toll-fraud notes in [security.md](security.md).
- Implementation choice to make: generated dialplan vs Asterisk's `app_followme`.

## F3. Ring groups

A number that rings several extensions.

Types (strategies):
- **Ring all:** every member at once.
- **Hunt:** one member at a time, in order, until someone answers.
- More strategies only if needed. Ask before adding.

Settings: number, name, members (extensions, possibly external numbers), ring time,
caller ID name prefix, destination if nobody answers (voicemail, another group, IVR, hang up).

Implementation choice to make: generated `Dial()` dialplan (simple, fits both strategies) vs
Asterisk queues (`app_queue`, more features, much more surface).

## F4. Email notifications

**Done (2026-09-19):** how mail leaves the box. The alert template (D114), the `Mail.*` settings
on the System page's Email tab, both transports, and a "send test mail" button that proves them
(D115). The old open question is answered: **both** SMTP and Graph, chosen per site by
`Mail.Transport`, and sent by our app.

**Done 2026-09-21:** voicemail to email (D126). The open question is answered, and the answer is
neither of the two it listed: Asterisk still **composes** the message, and a fixed `mailcmd` script
of ours **delivers** it through the `Mail.Smtp.*` settings. No MTA, no `externnotify`, no watching
directories.

**Done 2026-09-21:** the voicemail email itself (D129). The application composes it — its own
template, the transcript in the body, the recording attached as an MP3 — and the script calls the
application to ask for that, keeping its own relay as the fallback for when the application cannot
answer. So composition is ours and delivery has two paths, of which the plainer one always works.

**Still to do:** the alerts. Nothing raises one yet.

- Voicemail to email, with the recording attached (from F2a). **Done**: `voicemail.conf` names
  `mailcmd = /opt/tnpbx/bin/voicemail-mail`, a root-owned python3 script that relays the composed
  message — headers, body and attachment — through the same SMTP relay the test button proves. It
  reads `/opt/tnpbx/Config/mail.json`, which the web app writes (0640, group `asterisk`) whenever
  a mail setting is saved, and fails closed when that file is missing or incomplete. It replaces
  the `From` header with `Mail.FromAddress`, and refuses to authenticate to a relay that will not
  offer TLS. **SMTP only** — Graph is not available to it (D126). Since D129 that is the *second*
  path: the script first asks the application to compose and send a proper email (branded
  template, transcript inline, MP3 attachment) over `POST /api/voicemail/notify` on the loopback
  address, and relays the plainer message only when it cannot.
- Voicemail **transcription**, per extension. **Done**: the same script runs whisper.cpp
  (`small.en`) on the recording before relaying it and puts the text in the email. Entirely on the
  box — no transcription service, no API key, nothing leaves the server — and entirely optional:
  the engine is built by `install.sh` in a section that only ever warns, and a box without it
  emails voicemail exactly as it did before. The extension's choice reaches the script through a
  generated `/etc/asterisk/tnpbx-voicemail-options.json`, because `app_voicemail` has no option
  for it and no way to pass one (D128).
- System alerts worth considering: trunk registration lost, many failed SIP logins or blocked
  IPs, disk space low, config apply failed. The template and the sender are ready for these; what
  is missing is deciding which are worth an email and what raises them.
- Missed call emails: to be confirmed.

## F5. Call reports

- Call history (CDR): date/time, from, to, trunk or extension, duration, answered/missed/busy.
- Filters: date range, extension, direction (inbound/outbound/internal), status.
- Totals per extension and per trunk, missed calls.
- CSV export.

Implementation choice to make: Asterisk writes CDRs to its own SQLite/CSV module vs our app
collecting `Cdr` events over AMI into our database (fewer modules, one place for data).
Retention period needs deciding.

## F6. IVRs

Auto attendant: "Press 1 for sales, 2 for support."

- **Greeting is an announcement** (F7), referenced rather than owned, so there is one audio
  store, one upload path and one set of checks (D58). Recording a greeting is recording an
  announcement; one with no play extension is simply audio with a name.
- Digit → destination mapping (0-9, `*`, `#`), each key any destination the shared picker
  offers: extension, voicemail, ring group, announcement, another IVR, hang up (D35, D59).
- Timeout and invalid-key handling with a retry count, then a final destination. A menu lives
  in a context of its own, `ivr-<IvrID>`, and includes nothing (D59).
- Optional **play extension**: dial it from any phone to hear the menu, or target it as a
  destination from an inbound route / another IVR key / ring group failover (D57, D59).
- Optional **direct dial** to extensions, off by default and written as one dialplan entry per
  enabled extension so only numbers that exist can be reached (D60).

Not built: recording a greeting by phone via a feature code (the browser recorder on F7 covers
it), and the loop check follows IVR → IVR only, so a cross-feature ring is still possible to
build (D59).

## F7. Announcements

A recorded message a caller hears: "we are closed for the holiday", "calls may be recorded".
Built first because IVR greetings (F6) and inbound destinations both reuse it.

- Upload an audio file a browser or phone actually produces (MP3, MP4/M4A from an iPhone,
  WAV, WebM, Ogg), or record one in the browser. Converted on upload by ffmpeg to the one
  stored format, 16-bit 8 kHz mono WAV (D55).
- Optional **play extension**: dial it from any phone to hear the announcement, or target it
  as a destination from an inbound route / IVR key / ring group failover (D57).
- Name, description, enabled flag. Replacing the audio keeps the same announcement.

## F8. Time conditions

"Open hours go here, closed hours go there, holidays somewhere else." One form per condition —
there is deliberately no time-group entity to reference first (D62).

- Optional **play extension**: dial it to see which way the condition decides right now, and
  target it as a destination from an inbound route, IVR key or ring group failover (D62).
- **Open hours**: rows of weekday-picker + time range; no rows means always closed.
- **Holidays**: a date list, each optionally carrying its own destination override; a holiday
  beats open hours, and repeats every year — `GotoIfTime` has no year field (D63, D64).
- Three destinations: when open, when closed, on holidays — anything the shared picker offers.
- The clock is meant to be UTC (the installer sets it); a single Timezone setting, chosen from
  a dropdown, names the customer's zone. Every generated check tells Asterisk to evaluate in
  that zone, so open hours are entered in local time (D74, D75).

---

## Supporting pieces

Not on the list above but needed for it to work. Confirm scope before building beyond the
minimum.

| Piece | Needed by | Notes |
|---|---|---|
| **Outbound routes** | Trunks, follow me | Dial pattern → trunk, with prepend/strip digits (D109). Restrict international dialing by default (toll fraud). Each route may also name **the caller ID calls out as** and **the music on hold class** its caller hears while the far side holds them (D125); an extension's own outbound caller ID beats both. |
| **Inbound routes** | Trunks | DID → destination |
| **Destinations** | Inbound routes, IVR, ring groups, voicemail | One shared "send the call to X" picker and dialplan helper used by every feature. **Built 2026-09-17** (D35, D36): types so far are Extension, Voicemail and Hangup; each new feature adds its own |
| **Feature codes** | Voicemail, IVR recording | `*43` echo exists; `*97` voicemail etc. |
| **Audio file handling** | IVR, voicemail greetings | Storage, format conversion |
| **Time conditions** | Inbound routes, IVRs, anything needing business-hours routing | **Requested 2026-09-19.** One form per condition: open hours → destination, closed → destination, holidays → destination, with per-holiday overrides. No time-group entity (D62). F8. |
| **Music on hold** | Call parking | **Built 2026-09-19, several classes 2026-09-21** (D119, D122). Its own page at `/Moh`: as many classes as you like, each `mode=files` on its own directory under `/var/lib/asterisk/moh`, with tracks uploaded and converted like announcements (D55). One class ships with the product — **Standard**, three royalty-free tracks the installer transcodes — and no class may be called `default`, which is the name Asterisk keeps for its own fallback. What asks for a class today: a parked call (`Parking.MusicClass`), an inbound route, an internal call's backfill (D122 amended) and an outbound route (D125). Ring groups and transfers do not. |
| **Call parking** | — | **Built 2026-09-19** (D119). One lot, off by default. `*3` (configurable) parks the call you are on, the system speaks the slot back, and dialling the slot number from any phone picks it up. 1–9 slots, 30–600s timeout, then it rings back whoever parked it. Parked callers hear silence or one of the music on hold classes above, named by `Parking.MusicClass` (D122). Own page at `/Parking`. A slot can be put on a phone key, and its lamp is lit while a call is sitting there (D121). Not built: per-user or per-department lots, a park-and-page button. |
| **Phone provisioning** | Extensions | **Built 2026-09-21** (D77–D83). Polycom only. DHCP option 160 points phones at `http://user:pass@host/polycom`; a phone fetches `<mac>.cfg` then `exten<mac>.cfg`, both generated from the database per request and never stored on disk (D79). A phone with valid credentials and a Polycom User-Agent **adds itself** the first time it asks (D78); an admin then names it and gives it an extension. Basic auth against two settings keys, outside the Entra cookie (D77). Eight assignable keys per phone: **key 1 is the line it registers as** and the rest are lamps — another extension (BLF + quick dial) or a parking slot (D121, schema 020). Rebooting a phone is a SIP NOTIFY to its registered contact, so it works through NAT (D123). **Yealink** (D88–D90) uses DHCP option 66 and `/yealink` on the same gate, and gets the same web UI passwords from the same two settings (`security.user_password`, D133), the same Reboot button (`check-sync;reboot=true`, D134) and dial-now rules for the eager-safe numbers only — N11, ten and eleven digits (D135). Not built: firmware serving (D83), log/overrides/contacts upload endpoints (D79), softkeys; for Yealink, a per-phone local SIP port and missed-call tracking (parameters not verified). |
| **Settings UI** | Everything | **Built 2026-09-20.** Settings dropdown: a general page listing every key (modal edit, masked secrets, reset to default, D67–D69) and a SIP Settings page — bind address (0.0.0.0 default), UDP/TCP/TLS ports, NAT external address, local networks, STUN (default stun.l.google.com:19302), and codecs (ulaw/alaw/gsm only, the allowlist's modules, D73). TCP transport conditional (D70), TLS stored-only until cert management (D71), STUN + icesupport into rtp.conf (D72). |
| **Status page + logs** | Everything | **Built 2026-09-19** (D106, D107). The home page: health tiles, calls in progress, a "needs attention" list and counts, under a Status menu with a Logs page for Asterisk's messages / security logs and the app's own. Not built: call history (F5), security events (piece 20), graphs of anything. |
