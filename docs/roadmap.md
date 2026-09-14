# Roadmap

Built piece by piece. Each piece should build, pass tests and be verified on the lab VM
before the next one starts. **What** we build is defined in [features.md](features.md);
this file is the **order**.

| # | Piece | Feature | Status |
|---|---|---|---|
| 0 | Architecture decisions | | Done (see decisions.md) |
| 1 | Spike: Asterisk LTS built from source on a Debian VM, hand-written config, echo test | | Done 2026-09-13 |
| 2 | Solution layout, log4net, cookie auth | | Done 2026-09-13 |
| 3 | Extensions: model, schema, repository, pjsip/extensions renderers, atomic writer, tests | F2 | Done 2026-09-13 |
| 4 | AMI client + "apply config" (DB → render → write → reload only what changed) | | **Next** |
| 5 | Extensions UI: table + modals, live registration status | F2 | |
| 6 | Auth hardening: app role requirement (break-glass deferred) | | |
| 7 | Generate the remaining base config: `modules.conf` allowlist, `logger.conf`, `rtp.conf`, `manager.conf`, `asterisk.conf` | | |
| 8 | Destinations: shared "send call to X" model + dialplan helper | supporting | |
| 9 | Generic SIP trunk | F1 | |
| 10 | Outbound routes (international restricted by default) | supporting | |
| 11 | Inbound routes (DID → destination) | supporting | |
| 12 | Callcentric wizard (verify settings on lab VM first) | F1 | |
| 13 | Voicemail | F2a | |
| 14 | Email notifications (voicemail to email first, then alerts) | F4 | |
| 15 | Ring groups: ring all + hunt | F3 | |
| 16 | Follow me | F2b | |
| 17 | IVRs (with audio file handling) | F6 | |
| 18 | Call reports | F5 | |
| 19 | Helper: Unix socket, peer credential check, first commands (firewall) | | |
| 20 | fail2ban setup, then own AMI-security-event blocker via Helper | | |
| 21 | Installer script for fresh Debian (users, permissions, hardened systemd units, polkit rule, Asterisk build) | | |

The order after piece 7 is a proposal. The reasoning: trunks and routes first so the system can
make and take real calls; voicemail before email because voicemail-to-email is the first email
use; ring groups, follow me and IVR all send calls to destinations, so destinations come early;
reports last because they only need call data to exist. Helper, fail2ban and installer can move
earlier whenever deployment needs them. Record changes here.

## Piece 4 detail (next)

- `Techie.Pbx.Asterisk/Ami/`: minimal AMI client over TCP (login, action/response, events).
  Only what's needed: `Login`, `Command` / `PJSIPShowContacts`, reload actions, event reading.
- `ConfigApplier` (or similar): loads rows from repositories, renders every file, writes with
  `WriteAtomic`, then reloads only the modules whose files changed (`pjsip reload`,
  `dialplan reload`).
- Tests: renderer output is already covered. The AMI client needs protocol parsing tests using
  canned responses; the end-to-end check happens on the lab VM.
- Settings needed: conf directory, AMI host/port/user/secret, transport NAT settings. Store
  non-secret settings in the DB; decide where the AMI secret lives.
