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
| F4 | [Email notifications](#f4-email-notifications) | Planned |
| F5 | [Call reports](#f5-call-reports) | Planned |
| F6 | [IVRs](#f6-ivrs) | Planned |
| F7 | [Announcements](#f7-announcements) | Planned |

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

**Still to do:** per-extension caller ID for outbound calls, which needs trunks and outbound
routes to exist first.

## F2a. Voicemail

A voicemail box per extension (optional).

**Done (2026-09-16):** the box itself — PIN, email address, attach and delete-after-email
settings on the extension (D27), `voicemail.conf` generation, busy/unanswered fallback to the
mailbox and `*97` to listen (D29), and the fields in the extension modal.

**Still to do:** sending the email (F4), voicemail as a destination for inbound routes and IVRs
(needs destinations), and MWI.

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

- Voicemail to email, with the recording attached (from F2a).
- System alerts worth considering: trunk registration lost, many failed SIP logins or blocked
  IPs, disk space low, config apply failed.
- Missed call emails: to be confirmed.

Open questions: SMTP relay vs Microsoft Graph (clients are already on Microsoft 365 via Entra),
and whether Asterisk sends voicemail emails itself (needs a local mail transfer agent) or hands
them to our app.

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

- Greeting audio, digit → destination mapping, direct dial to extensions (optional),
  timeout and invalid-key handling with retry count, then a final destination.
- Destinations: extension, ring group, voicemail, another IVR, hang up.
- Audio: upload a file (converted to a format Asterisk plays) and/or record by phone via a
  feature code. Conversion tooling adds surface, so pick one approach deliberately.

## F7. Announcements

A recorded message a caller hears: "we are closed for the holiday", "calls may be recorded".
Built first because IVR greetings (F6) and inbound destinations both reuse it.

- Upload an audio file a browser or phone actually produces (MP3, MP4/M4A from an iPhone,
  WAV, WebM, Ogg), or record one in the browser. Converted on upload by ffmpeg to the one
  stored format, 16-bit 8 kHz mono WAV (D55).
- Optional **play extension**: dial it from any phone to hear the announcement, or target it
  as a destination from an inbound route / IVR key / ring group failover (D57).
- Name, description, enabled flag. Replacing the audio keeps the same announcement.

---

## Supporting pieces

Not on the list above but needed for it to work. Confirm scope before building beyond the
minimum.

| Piece | Needed by | Notes |
|---|---|---|
| **Outbound routes** | Trunks, follow me | Dial pattern → trunk. Restrict international dialing by default (toll fraud). |
| **Inbound routes** | Trunks | DID → destination |
| **Destinations** | Inbound routes, IVR, ring groups, voicemail | One shared "send the call to X" picker and dialplan helper used by every feature. **Built 2026-09-17** (D35, D36): types so far are Extension, Voicemail and Hangup; each new feature adds its own |
| **Feature codes** | Voicemail, IVR recording | `*43` echo exists; `*97` voicemail etc. |
| **Audio file handling** | IVR, voicemail greetings | Storage, format conversion |
| **Time conditions** | Probably IVR / inbound routes (business hours) | Not requested. Ask before building. |
| **Music on hold** | Ring groups, transfers | Asterisk default may be enough. Ask. |
