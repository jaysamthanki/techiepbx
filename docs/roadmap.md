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
| 4 | AMI client + "apply config" (DB → render → write → reload only what changed) | | Done 2026-09-15 |
| 5 | Extensions UI: table + modals, live registration status, local auth bypass (D24), Data folder (D25), config-pending marker (D26) | F2 | Done 2026-09-15 (verified on lab VM: add/edit/apply/status/secret all exercised end-to-end) |
| 6 | Auth hardening: app role requirement (break-glass deferred) | | |
| 7 | Generate the remaining base config: `modules.conf` allowlist, `logger.conf`, `rtp.conf`, `manager.conf`, `asterisk.conf` | | Done 2026-09-16. Lab-verified: restart onto the 32-module allowlist, SIP registration, AMI login via regenerated `manager.conf`, and voicemail all pass. `/etc/asterisk` is now 100% DB-generated. |
| 8 | Destinations: shared "send call to X" model + dialplan helper | supporting | Done 2026-09-17 (D35 derived model, D36 helper; golden extensions.conf unchanged; deployed + lab-verified) |
| 9 | Generic SIP trunk | F1 | Done 2026-09-17 (D37–D41). Lab-verified: Callcentric trunk created in the UI, config applied, `callcentric-reg` **Registered** to sip.callcentric.net, renewal confirmed, AMI status endpoint live. Outbound audio test deferred to piece 10 (needs a route). |
| 10 | Outbound routes (international restricted by default) | supporting | **Done 2026-09-18** (D44–D48). Lab-verified end-to-end: real call from MicroSIP over the Callcentric trunk, answered and audio-bridged. Fixed en route: allowlist was missing `res_pjsip_pubsub` (chan_pjsip would not load — calls silently stalled), and trunk dial needs the full URI form. International patterns refused by validation. |
| 11 | Inbound routes (DID → destination) | supporting | **Done 2026-09-19** (D49–D51). Lab-verified end-to-end: call to DID 19498800842 from a cell rings ext 1003 on MicroSIP with caller ID. DID dispatched from the To header per Callcentric's routing doc (`PJSIP_HEADER` + `CUT`, `func_cut` in the allowlist); catch-all flag per trunk; unmatched calls hang up unanswered (D50). |
| 12 | Callcentric wizard (verify settings on lab VM first) | F1 | **Skipped by user 2026-09-19**: no wizard; provider-specific settings (identify CIDRs, To-header DID matching) were verified live with Callcentric and noted in D51 — **"tested with Callcentric"**. Revisit if another provider (voip.ms) is tried. **2026-09-21 amendment: voip.ms tested live by the user and works.** Provider note for inbound routes: Callcentric delivers the DID as `1NXXNXXXXXX` (11 digits), voip.ms as `NXXNXXXXX` (10 digits) — the inbound route's DID match must be written in the form the provider sends. Documented here rather than coded: a wizard or per-trunk DID normalization stays out until a second real provider is actually deployed to customers. |
| 13 | Voicemail | F2a | Done 2026-09-16, except email (piece 14). Lab-verified: UI enable -> voicemail.conf + dialplan -> real unanswered call -> message recorded in INBOX. |
| 14 | Email notifications (voicemail to email first, then alerts) | F4 | **Done except alerts** (D114, D115; user-verified 2026-09-23). How mail leaves the box is done: the embedded alert template, the seven `Mail.*` settings on the new System page's Email tab, both transports (Graph via this app's own Entra registration, SMTP via `SmtpClient`), and a "send test mail" button that is the only thing sending anything today. **Not built:** voicemail to email, which is still Asterisk's own job through a local MTA and unchanged by D115, and the alerts themselves — nothing raises one yet. Graph additionally needs `Mail.Send` application permission with admin consent and `AzureAd:ClientSecret` on the server, neither of which is a code change. |
| 14a | Voicemail to email, delivered | F4 | **Done 2026-09-23, user-verified** (D126). The half of piece 14 that was left open. Asterisk still composes the email; delivery is now a fixed `mailcmd` — `scripts/voicemail-mail`, a root-owned python3 script installed at `/opt/tnpbx/bin` — which relays the message exactly as composed through the `Mail.Smtp.*` settings the test button already proves, so **there is no MTA on the box**. The script reads `/opt/tnpbx/Config/mail.json` (0640 `tnpbx:asterisk`, in a setgid 2750 directory — the only privilege move here, and no Helper), written by `MailConfigFile` on every settings save and at every start; unusable settings **remove** the file and the script fails closed. `voicemail.conf` gains `mailcmd`, an `emailsubject`/`emailbody` pair using only the variables app_voicemail substitutes, and `emaildateformat`; `format` is reordered to `wav49|g722` because the attachment is the first format in the list and a raw `.g722` plays nowhere. `install.sh` adds `python3`, `bin/` and `Config/`; `app-deploy.sh` preserves both across a re-deploy and reinstalls the script root-owned after its `chown -R`. **Not verified:** a real message arriving, the attachment playing, the rewritten `From` getting past the relay, and the first-format claim itself, which needs a look at `app_voicemail.c` on the VM. |
| 14b | Voicemail transcription | F4 | **Done 2026-09-23, user-verified** (D128). The mailcmd script from 14a now transcribes the recording before it relays it, with whisper.cpp and the `small.en` model **on the box** — no transcription service, no API key, nothing leaves the server. Per extension (`Extensions.VoicemailTranscribe`, schema `023`), because it costs roughly the length of the message in CPU time. `app_voicemail` has no transcription option and `mailcmd` takes no arguments, so the choice reaches the script through a generated `/etc/asterisk/tnpbx-voicemail-options.json` (mailbox → `{Transcribe}`) written by the apply like every other file there; it carries no PIN, address or name. The script finds the mailbox in `X-Asterisk-VM-Extension` (subject as a fallback), reads the recording out of the MIME rather than off disk, converts wav49 → 16 kHz mono with ffmpeg in a temporary directory it deletes, runs whisper-cli with a 300 s timeout, and adds a `text/plain` "Transcript:" part before the attachment plus `X-TNPBX-Transcribed: yes\|no`. **Every failure path relays the email unchanged.** `install.sh` gains a section that builds whisper.cpp and downloads the 488 MB model and **only ever warns** — a box where that failed is a working box without transcripts — and `app-deploy.sh` preserves `whisper/`. **Not verified:** the header name, the attachment's content type, ffmpeg decoding app_voicemail's wav49, the build on a fresh VM, and what `small.en` makes of real telephone audio. |
| 14c | The voicemail email is ours | F4 | **Done 2026-09-23, user-verified** (D129). Composition moves from app_voicemail to this application, without giving up 14a's guarantee. `mailcmd` still runs `voicemail-mail`, but the script now POSTs the message's details (mailbox, path on disk, caller ID, duration, arrival time) to `POST /api/voicemail/notify` on `127.0.0.1:8080` first; the app composes the D129 branded HTML template with the **transcript inline** and the recording attached as an **MP3** (ffmpeg, 48 kbit/s mono; the original `.WAV` is attached if that fails), and sends it through the same `Mail.Smtp.*` settings. **Anything but 200 and the script relays what app_voicemail composed, exactly as before** — the app being down must never mean a voicemail nobody hears. The endpoint is outside the Entra cookie and protected by `Mail.VoicemailCallbackToken` alone (a generated 40-character secret, checked first and in fixed time, blank matching nothing), with a loopback check behind it; the token reaches the script through the existing `Config/mail.json`. The posted path is refused unless it matches `VoicemailSpool`'s pattern for **that mailbox** — no traversal, no reading another mailbox — and the caller's name and number are flattened (`MailText`) before a subject line and HTML-encoded before a body. Transcription moves into the app and now reads the recording **from disk**, so a mailbox that emails without the audio can still have a transcript. No schema change (the token is a `Settings` row) and no new privilege (the web user is already in the `asterisk` group; the spool is group-readable). **Not verified:** the `X-Asterisk-VM-*` header spellings, which end of the message-number off-by-one is real, the web user reading the spool, the MP3 playing, and the round trip finishing inside a caller's patience. |
| 15 | Ring groups: ring all + hunt | F3 | **Done 2026-09-23, user-verified** (D52, D54, D136). Model + schema `007_ring_groups.sql` + repository (number collision check, full-catalog failover validation, ring-group→ring-group loop limit), ring-all (`Dial` with members joined by `&`) and hunt (sequential `Dial` per member) rendered into `extensions.conf` with golden tests, `RingGroup` destination type, and the Ring Groups page (members multi-select, failover via the shared picker). External-number members deliberately deferred (toll fraud, D52). **2026-09-23 amendment (D136):** the failover picker is universal — everything except the group being edited — and ring group rows label announcement / IVR / time condition failovers. **Verified 2026-09-23** by the user on the lab ("works"). |
| 16 | Forwarding | F2b | **Done 2026-09-22, user-verified** (D130). Follow me as one field: `Extensions.Forwarding` (schema `024`) is a space-separated list of up to four places to ring, and when it is not empty it **replaces** the extension's own phone — to keep your own handset ringing you put your own number in the list, which is what the modal's hint says in bold. Ring all, one `Dial` with the targets joined by `&`, and the ring time, the `tTkKr`/`U(sub-setmoh)` options, the hint and the voicemail fallthrough behind it all unchanged; an empty field renders the dialplan byte for byte as before. An extension target becomes `PJSIP/<number>` (matched against every row, so a switched-off target is a phone that does not ring rather than a number offered to the routes); anything else becomes `Local/<number>@internal/n`, so outbound route matching, the caller ID lines and the toll rules are inherited from a manually dialled call rather than re-implemented. Targets are digits only, 2–15, never starting with 0 (the D47 refusal, now shared through `OutboundRoute.InternationalPrefix`), no duplicates, spaces the only separator; a 2–6 digit target has to be an enabled extension, checked in `ExtensionRepository` where the other rows are visible. No schema beyond `024`, no new modules, no UI page — one input in the extension modal. **Verified 2026-09-22** on the lab: a non-matching external target got the no-service playback (the routes really are inherited), and the user confirmed a real forward ringing a mobile together with a desk phone. |
| 16a | Announcements (upload/record, convert, play ext, destination type) | F7 | **Done 2026-09-19** (D55–D57), lab-verified: MP3 uploaded through the UI -> ffmpeg converted to 8 kHz mono WAV -> apply -> `dialplan show 7100@internal` shows Answer/Playback/Hangup live, file at `/var/lib/asterisk/sounds/tnpbx/announcements/1/` 0640 group asterisk. IVR (17) reuses the audio handling. |
| 17 | IVRs (uses announcement audio) | F6 | **Done 2026-09-19** (D58–D61), lab-verified: IVR "Main menu" at 7102 created through the UI, greeting = holiday-test announcement, key 1 → ext 1003 — dialed live from MicroSIP, greeting played, digit routing + direct dial confirmed by the user. Model + schema `009_ivrs.sql` + repository, `ivr-<IvrID>` contexts in `extensions.conf`, `Ivr` destination type, `func_timeout.so` in the allowlist, IVRs page. See [piece 17 detail](#piece-17-detail-built-2026-09-19-not-yet-lab-verified). **Fix 2026-09-22 (D132), lab-verified:** a caller routed from an IVR to an extension heard silence, not ringing — `indications.conf` is now generated with a fixed `us` tone zone and reloaded on apply; the user confirmed ringing is audible on a live inbound call. |
| 17a | Time conditions | F8 | **Built 2026-09-19, pending lab verification** (D62–D66). Model + schema `010_time_conditions.sql` + repository with validation, `tc-<TimeConditionID>` contexts (holidays first, then weekly GotoIfTime, then closed), `TimeCondition` destination type, Timezone setting, and the Time Conditions page (open-hours rows with weekday pickers, holiday date rows with per-date override, three destination selects). No module additions. **2026-09-20 amendment**: timezone became a dropdown and every `GotoIfTime` names the zone as its fifth argument, so open hours are entered in the customer's local time and evaluated there (DST included) while the server clock is meant to be UTC — the installer (piece 21) will set that. D74, D75. |
| 17b | Settings UI: general key/value page + SIP Settings page (transports, NAT, STUN, codecs) | supporting | **Done 2026-09-20** (D67–D73). Lab-verified end-to-end: values saved through the UI's form endpoint, Apply reloaded pjsip and reported the rtp.conf restart honestly; after restart `pjsip show transports` shows transport-tcp 0.0.0.0:5062, `rtp show settings` shows ICE Yes + STUN stun.l.google.com:19302, ext 1001 re-registered by real SIP digest, endpoints show settings-driven `allow = ulaw,alaw`. TLS port stored only (D71) until certificate management is a piece. |
| 18 | Call reports | F5 | **Done 2026-09-22** (D131). Lab-verified end-to-end: real baresip calls landed as Cdrs over AMI (`cdr_manager` + `func_cdr`, `read = system,cdr`), Reports page shows them with Today/Yesterday/This week/Last week quick buttons, totals and CSV export. Fixed en route: cdr.conf with `unanswered = yes` (Asterisk default drops unanswered calls — missed-call reports would have been empty). One row per leg, kept forever. |
| 22 | Phone provisioning: Polycom (DHCP option 160 -> http://user:pass@host/polycom, MAC capture on first contact, generated per-device config) | supporting | **Done 2026-09-21** (D77–D83). Lab-verified with a spoofed VVX 450 User-Agent: no/bad Basic auth → 401, valid auth + non-Polycom UA → 403; first contact auto-registered the MAC (model, firmware, IP); unassigned phone got a minimal no-reg config; after assigning ext 1001 in the UI the generated config carries reg.1 with the real secret, server, MWI and the deterministic local SIP port; a model mismatch on a known MAC → 403. Schema `011_phones.sql`, `Phone` model + `PhoneRepository`, `PolycomUserAgent`, `BasicAuth`, the `/polycom` controller outside the Entra cookie, master + per-phone renderers with golden files, `Provisioning.Username`/`Provisioning.Password` settings keys, and the Phones page. **Round 2 (D84–D87)**: NTP server as a setting (default pool.ntp.org), Polycom device web passwords as secret settings (admin one doubles as the push digest credential), push-on-save `Action:UpdateConfig` + Reboot phone button — best-effort, polling is the safety net. **Round 3 — Yealink (D88–D91)**: `/yealink` endpoint on the same gate, plain key=value config generated per request, Brand column (schema 012), tabbed Phones page (Phones | Settings → Polycom | Yealink), SIP-NOTIFY signal via generated `notify.conf` + AMI PJSIPSendNotify (res_pjsip_notify added to the allowlist); Yealink uses DHCP option 66. Yealink path pending lab verification. Yealink web passwords, reboot button and dial-now rules came later, in piece 35 (D133–D135). See [piece 22 detail](#piece-22-detail-built-2026-09-21-not-yet-lab-verified). |
| 26 | Call parking + music on hold | supporting | **Built 2026-09-19, pending lab verification** (D119). One parking lot, off by default: `*3` parks the call you are on, Asterisk speaks the slot number to whoever parked it, and dialling that single digit from any phone picks the call up. Five `Parking.*` settings (all Asterisk-scoped), three new generated files — `features.conf` (`parkcall`), `res_parking.conf` (the lot; **not** parking.conf, which Asterisk 12 retired) and `musiconhold.conf` (one class called `parking`, `mode=files` on `/var/lib/asterisk/moh`) — plus one `ParkedCall()` entry per slot in `extensions.conf`, written out rather than generated from a `parkext` (D12, D46, D60). **Every generated `Dial()` now carries `tTkK`**: `k`/`K` are what make the feature code reachable at all, and `t`/`T` come with them, which also turns on Asterisk's default `#` blind transfer. Silence is implemented as having no class to start, so the generated `musiconhold.conf` deliberately never defines a class called `default`. New table `MohFiles` (017) + `MohFileRepository` + `MohStore` (the announcements convert-on-upload pattern, D55, one flat directory), three modules on the allowlist (`res_parking.so`, `res_musiconhold.so`, `bridge_holding.so`), and a `/Parking` page under Call Handling. **Superseded in part by piece 29** (D122): music on hold is several classes on a page of its own now, and the class a parked caller hears is the `Parking.MusicClass` setting. |
| 19 | Helper: Unix socket, peer credential check, first commands (firewall) | | **Done 2026-09-23, lab-verified end to end** (D142, D143). The privileged helper of D3 exists: `Techie.Pbx.Helper` listens on `/run/tnpbx/helper.sock`, checks the caller's UID with `SO_PEERCRED` against the `tnpbx` user resolved at startup, and answers three typed messages from `Techie.Pbx.Contracts` — `ping`, `firewall.status`, `firewall.apply` — as newline-delimited JSON. No message carries a command, a path or a shell string. A `firewall.apply` is re-validated in the helper, rendered by a pure static function with a golden file (`Expected/nft-ruleset.conf`) into one `inet tnpbx-input` table with `policy drop`, checked with `nft -c -f` and loaded with `nft -f` through `ArgumentList` at a fixed path — never a shell. The four safety rules (loopback, established/related, ICMP, TCP 22) are written by the helper itself before anything a message carried, so no apply can lock the box out; fail2ban's `f2b-table` is a separate table and is left alone. The applied ruleset is kept in `/var/lib/tnpbx-helper` and re-applied at boot. Web side: `HelperClient`, `FirewallRulesBuilder` (expected rules derived from the SIP settings, the `RtpConfRenderer` constants and `WebBindings`, so the firewall cannot drift from what is listening) and a **Settings → Firewall** status page showing reachable/expected/applied with an out-of-sync badge and one red Apply button. `install.sh` creates `/var/lib/tnpbx-helper`, the `/run/tnpbx` tmpfiles entry and the hardened `tnpbx-helper.service`; `app-deploy.sh` installs the helper self-contained into `/opt/tnpbx-helper` **root:root**, outside the tree the web user owns. 44 new tests. Lab-verified 2026-09-23: non-group users cannot reach the socket (0660), **root itself connected and was refused by the UID check** (journal line), the tnpbx user got a correct ping reply; the Firewall page applied the ruleset through the UI with `policy drop` armed and SSH, web, both phones and the trunk survived — while fail2ban simultaneously re-banned the returning real scanner (208.100.60.35) in its own untouched table; a reboot re-applied `last.nft` at boot and everything came back unaided. |
| 20 | fail2ban setup, then own AMI-security-event blocker via Helper | | **fail2ban half done 2026-09-23, lab-verified** (D141): `scripts/fail2ban/` (filter + jail + install.sh) deployed to the lab; `tnpbx` jail bans SIP auth-failure sources for 24h in nftables, scanner-proofed against the Sep 22 real-world probe in the logs. The own Helper/AMI blocker stays deferred (D7). Roadmap note: the base firewall is no longer open — it landed with piece 19 on the same day (D142, D143), in its own nftables table, so the two do not tread on each other. |
| 23 | Certificate management: in-process ACME (Certes, HTTP-01), Kestrel on 80/443, SIP TLS, auto-renewal | security | **Done 2026-09-22, verified live end to end** (D97–D101). `013_certificates.sql` + `CertificateRepository` (Current() = newest usable), Certes-based `AcmeCertificateService` answering its own HTTP-01 challenges on port 80, `WebBindings` (80 + 8080 always, 443 with a usable cert; `CAP_NET_BIND_SERVICE` in the unit), daily renewal at ≤30 days, Certificates page, combined `tnpbx-cert.pem` feeding the generated pjsip `transport-tls` on `Sip.TlsPort` (D71 real). **Live verification**: staging order first (proved the ACME loop through the Azure NAT, caught a Certes cross-signed-chain bug we fixed by taking leaf/issuers off the chain object), then a **production order — `pbx.example.com` serves a real Let's Encrypt cert on 443**, port 80 redirects everything except provisioning + the ACME challenge (a redirect-on-8080 bug fixed en route: 8080 stays plain HTTP as the way back in), and Asterisk serves `transport-tls` on 5061 with the same cert. 857 tests. |
| 21 | Installer script for fresh Debian (users, permissions, hardened systemd units, polkit rule, Asterisk build) | | **Done 2026-09-22** (D92–D96). Part 1 (`scripts/install.sh`) verified one-shot on a freshly wiped lab VM (Debian 13.7, zero manual steps): UTC clock + NTP synced, `tnpbx` in the `asterisk` group, `/etc/asterisk` root:asterisk 2770 and empty, `/opt/tnpbx` deploy target, Asterisk 22.11.0, unit enabled but inactive by design. Part 2 (`scripts/app-deploy.sh <publish.tgz>`, D95) deployed the app the same day: hardened `tnpbx-web.service` (User=tnpbx, ProtectSystem=strict), polkit rule scoped to exactly `asterisk.service`, `appsettings.json` + `Data/` preserved across re-deploys. Full first-run proven on the fresh box (D96): sign in → settings → extension → first apply wrote all nine conf files → Asterisk started on generated config → AMI/pjsip/provisioning live. See [piece 21 detail](#piece-21-detail-part-1-built-2026-09-22-not-yet-verified). |
| 24 | Smarter apply: settings scopes, and a confirmed Asterisk restart after apply | supporting | **Built 2026-09-23, pending lab verification** (D103, D104). Settings carry a `SettingScope` (Asterisk / Phones / App) classified by what actually reads each key, and only an Asterisk-scoped write raises the config-pending marker — a provisioning password or the phones' NTP server no longer lights the apply button, and the toast says phones pick it up at their next poll. The apply response now carries `RestartRequired` + `RestartFiles`; when an apply writes a startup-only file the page asks with a sweetalert2 confirm and, on yes, posts `/api/config/restartAsterisk`, which runs `systemctl restart asterisk.service` as `tnpbx` through the existing polkit rule (no sudo, no shell, 30s timeout). A declined restart leaves an "Asterisk restart required" badge + Restart button in the navbar poll area, backed by `AsteriskRestartMarker` beside the database. 890 tests. |
| 25 | Status home page + Logs page (Status menu) | supporting | **Done 2026-09-19, lab-verified** (D106, D107). Deployed to the lab VM and checked live: tiles read a real Asterisk (uptime, 1 of 1 trunk, 1 of 2 extensions, cert 89 days), the attention list named the two unassigned phones, the Logs page tailed `messages.log` / the app log with filter and Follow working, `tnpbx` reads `/var/log/asterisk/*.log` as-is (Asterisk creates them 0644), and a path as a source name got a 400. Not yet seen live: a bridged call in the calls table (no call was placed). `/` is the status page: six health tiles + calls in progress polled every 5 s over one AMI session (`CoreShowChannels` / `CoreStatus` added to `AmiSession`, no permission change), a "needs attention" list from the pure, tested `AttentionRules` (dangling destinations, unregistered / rejected trunks, trunks with no routes, phones without extensions, certificate expiry and failed orders, disk usage, UTC time conditions, AMI down, apply / restart owed), and counts. `/Status/Logs` tails `messages.log`, `security.log` or the log4net file — fixed allowlist, no path from the browser, 4 MB scan cap, follow mode. New setting `Asterisk.LogDirectory`. 954 tests. Seen on the lab VM: every 5-second poll is an AMI login, and each login is a `SuccessfulAuth` line in `security.log` and `messages.log` — the extensions and trunks pages already did this, but the home page makes it constant while anyone has it open. Left as is; a long-lived AMI session is the fix if it matters. **2026-09-19 amendment (D116)**: the Logs page gained a fourth source, **Web requests (W3C)**. Kestrel now keeps a W3C request log of its own (`AddW3CLogging`, first in the pipeline so it wraps auth) in `logs/requests/` inside the install — client address, port, method, path, status, time taken, User-Agent and Referer, no cookie and no `sc-bytes` (the logger has no bytes field). It is the answer to "did the phone even reach us": `/polycom` and `/yealink` have no session, and `RequestLogUserMiddleware` names those requests after the Basic username so `cs-username` is not blank for them. New setting `Web.RequestLog` (App scope, on by default, **read at startup — restart tnpbx-web after changing it**) and the first `Toggles` (`on`/`off`) setting. No `app-deploy.sh` change: `/opt/tnpbx` is already in `ReadWritePaths`. |
| 27 | Navbar regroup + printable cheat sheet | supporting | **Built 2026-09-20, pending lab verification** (D120). The navbar is now four dropdowns — **Connectivity** (Extensions, Phones, Trunks, then the cheat sheet under a divider), Call Handling (unchanged), Settings (which gains Certificates as a menu item; the page stays at `/Certificates`) and Status, now last. `/Connectivity/CheatSheet` is a read-only, server-rendered page for office staff rather than admins: every enabled extension by number, and every code the generated config really carries, from the pure `FeatureCodes.All(parking, voicemailInUse)` — `*2`, `#`, the park code and slot range when parking is on, `*97` when anyone has a mailbox, `*43`. No schema, no settings, no new permission, no secrets on the page. Printing is Bootstrap's `d-print-none` on the layout's navbar and footer plus one `@media print` block in `site.css` that forces light colours, so dark mode does not print white text on white paper. **Not verified without a browser:** the printed result itself — margins, one-page fit and the dark-mode override all need a real print preview on the lab VM. |
| 28 | Phone buttons: eight assignable keys per phone (BLF + quick dial) | supporting | **Built 2026-09-20, pending lab verification** (D121). The phone edit modal is now two tabs, **Buttons** (which opens) and Details. Eight dropdowns, each nothing, an extension, or a parking slot when parking is on. New table `PhoneButtons` (018) + `PhoneButtonRepository` — `TargetType` is plain `TEXT` with no `CHECK`, so call flow control becomes a third kind with no schema script. The dialplan grows hints in `[internal]`: `PJSIP/<number>` per enabled extension and `park:<slot>@parkedcalls` per slot, the park device name read off `res/res_parking.c`. Polycom renders the keys as an attendant resource list (`attendant.reg` + `resourceList.N.address/label/type`), numbered 1..n in key order so an unassigned key leaves no gap; Yealink renders the same keys as `linekey.N` of type 15 (BLF), keeping each key's own number because a Yealink line key is addressed directly. Two modules join the allowlist for the other half of a lamp: `res_pjsip_exten_state.so` and `res_pjsip_dialog_info_body_generator.so`. `modal-dialog-centered` is gone from every modal. **Not verified:** a real handset lighting up — the lamps, the labels and how many keys a VVX actually shows need a phone on the lab VM. |
| 29 | Music on hold: several classes, and one that ships | supporting | **Built 2026-09-21, pending lab verification** (D122). Music on hold is its own feature now, not a section of the Parking page: new table `MohClasses` (019) + `MohClassRepository`, `MohFiles` gains `MohClassID` (cascade, `File` unique per class), and `musiconhold.conf` renders one `[<name>]` section per class with `mode=files` on `/var/lib/asterisk/moh/<class directory>`. A class with no rows is still written out, because `files` mode scans the directory. No class may be called `default` in any case — Asterisk matches class names with `strcasecmp`, and that name is the fallback D119's silence depends on — so the class that ships is **Standard** with directory `default`. New setting `Parking.MusicClass` (Asterisk scope, default `Standard`) is written as `parkedmusicclass`; `Parking.Audio` still decides whether there is music at all. New `/Moh` page under Call Handling: classes table, tracks table (with the class each is in), the D119 upload/convert unchanged but into the class's directory, and delete refused for the class that ships and for the one parking points at. The three royalty-free Audiodollar tracks in `media/musiconhold` are transcoded by `install.sh` / `build-asterisk-vm.sh` to `default-{1,2,3}.wav` and seeded as rows by the schema script on a system that had no tracks of its own; the opsound download is gone from both installers. **Not verified:** a parked caller actually hearing the right class, and the installed files playing — both need the lab VM. |

| 30 | Buttons are Lines and BLFs, and a phone reboots by NOTIFY | supporting | **Built 2026-09-21, pending lab verification** (D121 amended, D123). The user configured all eight keys on a Poly Edge 450 and got **nine** lines: the Details tab's Extension dropdown took line key 1 on top of the Buttons tab. So the Extension dropdown is gone and the registration is **key 1**, a new `Line` kind; what was `Extension` is now `Blf`, offered as a **User list** group from key 2 down, alongside Parking slots. Schema `020_phone_line_buttons.sql` renames the old rows, moves each phone's `ExtensionID` in as key 1, shifts the rest down (a phone with all eight keys full loses its last one) and drops the column. A phone must have a line, lines lead, and two phones still cannot register as one extension. Polycom writes one `reg.N` per line and the rest as attendant resources; Yealink writes one `account.N` per line, `linekey.N.type = 15` for a line and `16` for a BLF. **Rebooting a phone is now a SIP NOTIFY** over AMI (`PJSIPNotify` with the headers spelled out) instead of an HTTP push to the phone's web UI, so it works through NAT — the button is disabled with the reason on it when the phone has no line or no registered contact. `pjsip_notify.conf` carries the two types for the CLI, has no `[general]` section, and joins the startup-only file set. **Not verified:** a real handset showing eight keys and one line, and a reboot actually arriving — both need a phone on the lab VM. |
| 31 | Three fixes from a live Poly Edge 450 | supporting | **Built 2026-09-21, pending lab verification** (D121 amended again, D122 amended, D124). Three things the user found with a real handset in their hand. **(1) A key may be left blank anywhere.** `PhoneButton.ValidateSet` now only insists that the *lines* are keys 1..n with no gap; every other key may be blank wherever the admin wants one. Polycom's attendant resource list is written **by position** — index = key less the number of registrations, every index up to the last assigned key present, a blank key as `resourceList.N.address=""` — because a phone reads the list until the first index it cannot find. Yealink was already right: `linekey.6` is key 6. **(2) The Polycom digit map no longer truncates.** `xxxx` full-matched at four digits and sent an attended transfer to a ten-digit mobile as four digits; the map is now built per system by `PolycomDigitMap.For(extensions)`, eager only for `[2-9]xxxxxxxxx`, `1xxxxxxxxxx` and `[2-9]11`, everything else ended with `T`. The tests compile the map the way the phone reads it and assert that no prefix of a ten- or eleven-digit number is ever sent on its own. **(3) Music on hold per inbound route.** New nullable `InboundRoutes.MohClassID` (021, `ON DELETE SET NULL`), a dropdown on the route form, and one `Set(CHANNEL(musicclass)=<name>)` in the trunk's context before the call is handed on. **Not verified:** all three need the lab VM and the phone — the blank key staying blank, a ten-digit transfer going out whole, and a held caller hearing the route's class. **Worth a decision:** a route left on "Default" names no class, and this system's shipped class is `Standard` rather than Asterisk's `default`, so that caller hears silence on hold (D122 amendment). |
| 32 | Internal calls get hold music too | supporting | **Built 2026-09-21, pending lab verification** (D122 amended again). A call that started on a phone here never passes a trunk context, so nothing had ever set a music class on that caller's channel and holding them was silent. Every extension in `[internal]` now carries `ExecIf($["${CHANNEL(musicclass)}" = "" \| "${CHANNEL(musicclass)}" = "default"]?Set(CHANNEL(musicclass)=<the class that ships>))` ahead of its `Dial`, which makes the `Dial` priority 2. The guard is the point: an inbound route's class is the caller's and survives the `Goto` into the extension it leads to, and `default` is the value that actually fires, because `chan_pjsip` puts the endpoint's `moh_suggest` on every channel it creates. No class flagged as the default means no line at all — the old silence, not a dangling name. **Two modules join the allowlist**, `app_exec.so` (`ExecIf`) and `func_channel.so` (`CHANNEL`) — and `func_channel` was already missing for the trunk-context line piece 31 added, so **that line cannot have worked**: with `autoload = no` an unlisted function is not registered. **Not verified:** a colleague on hold hearing the shipped tracks, the route's own class still winning for an inbound caller, and whether a parked caller whose lot is set to silence now hears music — the channel's class is consulted when there is no `parkedmusicclass`, so an internal caller parked in a silent lot is expected to hear `Standard` now. Ring groups are deliberately not covered, and neither is the *other* direction: the extension that answers still carries `default` from its endpoint's `moh_suggest`, so a caller holding **them** is still silence. Both are noted in the decision. |
| 33 | The phone that answers gets the hold class too | supporting | **Built 2026-09-21, pending lab verification** (D122 amended a third time). The other half of piece 32, and the half the lab log complained about: `WARNING res_musiconhold.c _get_mohbyname: Music on Hold class 'default' not found in memory.` Piece 32 names the class on the channel *executing dialplan*, which is the caller's; the phone they dialled answers on a channel `chan_pjsip` created, which runs no dialplan and carries the literal `default` from its `moh_suggest` — so when the **caller** presses hold, that is the channel Asterisk looks for music on. Defining a `[default]` class is not the fix and never will be: `default` staying undefined is what parked-caller silence is made of (D119). Instead every internal `Dial` gains `U(sub-setmoh)` — Asterisk's own hook, a `Gosub` run on the called channel as it answers — and one `[sub-setmoh]` context is written with the *same* `ExecIf` string the caller's line is built from, then `Return()`. **Ring groups are covered** this time; a member answering is a Dial-created channel like any other. **Outbound is deliberately not**: the channel a trunk `Dial` creates is the provider's, so it keeps the bare `tTkK`, and an internal caller holding an outbound call is still the silent case. No class flagged as the default means no context *and* no `U()` naming one — a `U()` pointing at a missing context fails the Gosub and ends the call, so one value decides both. **One module joins the allowlist**, `app_stack.so` (`Gosub`/`Return`). **Not verified:** the warning gone, a held colleague hearing the shipped tracks, and a ring group member behaving the same. |

| 34 | Outbound caller ID per extension and per route, and route hold music | supporting | **Built 2026-09-21, pending lab verification** (D125). Schema 022 adds `OutboundRoutes.CallerID`, `OutboundRoutes.MohClassID` and `Extensions.OutboundCallerID`. Precedence is **trunk < route < extension**, and the trunk's `callerid` in pjsip.conf is untouched: the extension claims at origination through `set_var = TNPBX_CID=...` on its endpoint, and the outbound route contexts — the only place that reads it — apply it with `ExecIf($["${TNPBX_CID}" != ""]?Set(CALLERID(all)=${TNPBX_CID}))`, with the route's own caller ID written as the opposite half of the same guard. So an internal call still shows the extension's name and number, and a call in off a trunk cannot be rewritten either (a trunk's context includes nothing, D50). A route may also name a hold class, set unguarded before the `Dial` because that channel has passed nothing that could have named one — it is what the caller hears while the far side holds *them*. Both caller ID fields go through one validator, `CallerIDFormat` (FreePBX's `"Name" <number>` or a bare number; digits only, and no brackets, commas or quotes in a name, because the value lands inside a `Set()` inside an `ExecIf`). **No new modules** — `set_var` is core res_pjsip and `func_callerid`, `app_exec`, `func_channel` were all already on the allowlist. A route that names neither renders exactly what it did before, so every existing golden file is unchanged; two new ones cover the feature. **Not verified:** whether the provider honours the caller ID we present (Callcentric may override it to an account-owned number), a held outbound caller hearing the route's class, and an extension's DID beating a route's caller ID on a real call. |
| 35 | Yealink reaches Polycom parity: web passwords, reboot button, dial-now rules | supporting | **Built 2026-09-22, pending lab verification** (D133–D135). The user has a factory-fresh Yealink on their desk sitting on the "set a new admin password" prompt. **(1) Web passwords:** the Yealink config now writes `security.user_password = admin:<pw>` and `= user:<pw>` from the existing `Provisioning.AdminPassword`/`Provisioning.UserPassword` settings (no new keys), each left out when unset, for unassigned phones too. **(2) Reboot button:** the phone modal's Reboot button now works for Yealink phones — same handler, confirm and disabled-with-a-reason as Polycom; `[yealink-reboot]` in `pjsip_notify.conf` is now `check-sync;reboot=true` (one Asterisk restart on the next apply). Save still sends the `reboot=false` re-read. **(3) Dial-now:** `dialplan.dialnow.rule.1..3` = `[2-9]11`, `[2-9]xxxxxxxxx`, `1xxxxxxxxxx` — D124's eager subset; extensions, 7-digit, feature codes and `0` stay on Send/timers. A test checks no rule matches a strict prefix of anything dialable. **Not verified:** the prompt going away after a reboot, the reboot arriving, and a ten-digit number going out without Send — all need the Yealink handset. |
| 36 | Call flow control (day/night override): toggle from a phone, two destinations (F9) | F9 | **Done 2026-09-23, user-verified** (D137). One row per switch (schema `027`): name, feature code, normal + override destinations. State lives in Asterisk astdb (`TNPBX/CFC/<id>`) so a flip takes effect on the next call with no apply; `*<code>` in `[internal]` is the toggle (plays `activated`/`de-activated`), `[cfc-entry]` is where destinations enter so a routed call can never flip a switch; the lamp is a hint (`Custom:tnpbx-cfc-<id>`) driven by `DEVICE_STATE()` — `func_db.so` + `func_devstate.so` join the allowlist, both verified to exist in the Asterisk 22 build. `CallFlowControl` is a destination type (D136 pickers), phone buttons gain the target kind, page badge reads astdb over AMI `DBGet` (read-only, no permission change). En route: all 51 `GeneratedRegex` validators re-anchored `$`→`\z` after the tests found a trailing newline passing validation. **Verified 2026-09-23** by the user on the lab ("it works"). |

| 37 | IVR announcement return + renumber cascade | supporting | **Built 2026-09-23, pending lab verification** (D138). (A) `Ivrs.ReturnAfterAnnouncement` (schema `028`, default off): an announcement destination on that IVR's keys plays inline and `Goto(s,1)` back into the menu — fresh-dial semantics, no stack; the final destination never returns; off is byte-identical. (B) `Renumbering` helper: a changed IVR/ring-group/time-condition number or CFC code rewrites every stored reference (10 destination column pairs, forwarding lists, ring group members, phone Line/BLF/CFC keys) in one transaction — sweep first, row second, so a mid-way refusal rolls back everything. Deletes never cascade; voicemail spool folders left for the Helper. 1740 tests. **Not verified on the lab:** announcement-return on a live call, a live renumber leaving nothing dangling. |

The order after piece 7 is a proposal. The reasoning: trunks and routes first so the system can
make and take real calls; voicemail before email because voicemail-to-email is the first email
use; ring groups, follow me and IVR all send calls to destinations, so destinations come early;
reports last because they only need call data to exist. Helper, fail2ban and installer can move
earlier whenever deployment needs them. Record changes here.

## Piece 4 detail (done 2026-09-15)

- `Techie.Pbx.Asterisk/Ami/`: minimal AMI client over TCP. `Login`, `Logoff`, action/response
  with ActionID matching, event lists, event reading, `Reload` and `PJSIPShowContacts`.
  `Action: Command` was dropped (D17), so the AMI user needs no `command` permission.
- `ConfigApplier`: renders every file from the database, writes with `WriteAtomic`, then reloads
  only the modules whose files changed (D16). `ConfigApplier.FromDatabase` is the normal wiring.
- Settings: `Settings` key/value table, script `002_settings.sql`, keys in `SettingsKeys`,
  read through `SettingsRepository` and turned into objects by `AsteriskSettings`. The AMI
  secret lives there too (D14, D15).
- Tests: AMI protocol and session against canned responses, settings repository and loader,
  applier from database to files. The end-to-end reload check happens on the lab VM.

Left for later, on purpose: nothing writes the settings yet (no UI, and `manager.conf` with the
AMI user is generated in piece 7), so a lab VM has to have its AMI rows inserted by hand or by
the lab script until then.

## Piece 5 detail (code done 2026-09-15)

- `/Extensions`: bootstrap-table of number, name, enabled and a live registration badge, with
  create, edit and delete in modals, "show password", "regenerate password" and
  "apply config". The page is a shell; htmx fetches every part of it from page handlers as HTML
  partials, and changes answer 204 with `HX-Trigger` events (D21). The create/edit form moved
  from a sweetalert2 popup to a Bootstrap modal on 2026-09-18 (D42).
- `RegistrationStatus` (in `Techie.Pbx.Asterisk/Ami`) turns `PJSIPShowContacts` into a state per
  extension number and never throws: no AMI means every badge says Unknown. The page polls it
  once every 5 seconds and htmx swaps the badges out of band.
- `PbxDatabase` opens the database at startup from `Database:Path` (D22); pages and controllers
  build their own repositories over it.
- API: `POST /api/config/apply`, `GET`/`POST /api/extensions/{id}/secret`. Unauthenticated calls
  now get 401 rather than a redirect (D20), and every browser call carries an antiforgery header
  (D23).

Left over from this piece: per-extension caller ID for outbound calls (listed under F2) is not
built; it waits for trunks and outbound routes to exist.

## Piece 13 detail (in progress, started 2026-09-16)

Pulled forward from its place in the order because extensions are the only thing that exists to
hang it on. Scope is F2a minus the email itself.

- Schema `003_voicemail.sql`: five `Voicemail*` columns on `Extensions` (D27).
- `VoicemailConfRenderer` writes `voicemail.conf`: a `[general]` section (wav49, 100 messages,
  5 minutes each) and one mailbox line per enabled extension that asked for one, in context
  `default`. `ConfigApplier` reloads `app_voicemail` when that file changes.
- `ExtensionsConfRenderer`: busy and unanswered calls fall back to the mailbox (D29), and `*97`
  plays your own messages. Extensions without voicemail are unchanged.
- The extension modal gains the voicemail fields, and says plainly that no email is sent yet.

Still to do:

- **Email delivery is not built** (F4, piece 14): addresses and the attach/delete toggles are
  stored and written into `voicemail.conf`, but with no `serveremail` or template Asterisk sends
  nothing. The open question there — SMTP relay versus Microsoft Graph, and whether Asterisk
  sends or the app does — is still open.
- Voicemail as a **destination** for inbound routes, IVRs and ring group failover waits for
  piece 8 (destinations).
- ~~**MWI** (the message-waiting light on the phone) is not configured: it needs `mailboxes =` on
  the PJSIP endpoint and a decision about subscriptions.~~ Done 2026-09-19 (D108), after Claude
  saw the phones' subscribe failures in the lab VM logs: `res_pjsip_mwi` +
  `res_pjsip_mwi_body_generator` on the allowlist and `mailboxes = <ext>@default` on endpoints
  with voicemail. Needs an Asterisk restart (modules.conf, D33) to take effect.
- Run it on the lab VM: leave a message, listen to it with `*97`, check the busy greeting.

## Piece 7 detail (in progress, started 2026-09-16)

`/etc/asterisk` is now generated end to end: eight files, no hand-written ones left. The lab
script still writes its own versions, and it has to — Asterisk must be running before the app can
apply anything — but the first apply replaces every one of them.

| File | Renderer | Applied by |
|---|---|---|
| `asterisk.conf` | `AsteriskConfRenderer` | restart (D33) |
| `modules.conf` | `ModulesConfRenderer` | restart (D33) |
| `rtp.conf` | `RtpConfRenderer` | restart (D33) |
| `logger.conf` | `LoggerConfRenderer` | `logger` reload |
| `manager.conf` | `ManagerConfRenderer` | `manager` reload, last and disconnect-tolerant (D34) |
| `pjsip.conf` | `PjsipConfRenderer` | `res_pjsip` reload |
| `extensions.conf` | `ExtensionsConfRenderer` | `pbx_config` reload |
| `voicemail.conf` | `VoicemailConfRenderer` | `app_voicemail` reload |
| `tnpbx-voicemail-options.json` | `VoicemailOptionsRenderer` | `app_voicemail` reload — Asterisk does not read this one, and "no module" would mean "restart" (D128) |

`ApplyResult` grew `RestartRequiredFiles`/`RestartRequired`, and the apply toast says so.

Still to do:

- **Verify the module allowlist on the lab VM** (D31). This is the one that needs real hardware
  in front of it: restart on the generated `modules.conf`, then `core show modules`, register a
  phone, place a call, leave a message, and read the log for "Error loading module". Expect to
  iterate — a missing module usually shows up as a feature that silently does nothing.
- **The AMI secret has to be in the database before the first apply**, or rendering
  `manager.conf` fails with "AMI secret is required" and nothing is written. The lab script still
  generates its own secret into its own `manager.conf`; until there is a settings UI, the two are
  reconciled by putting the script's secret into the `Settings` table by hand.
- Confirm whether `res_rtp_asterisk` reloads `rtp.conf` cleanly (D33), and move it out of the
  restart group if it does.
- ~~Restarting Asterisk is still a manual step.~~ Done in piece 24 (D104): an apply that owes a
  restart offers it, the app runs it through the polkit rule, and a declined restart leaves a
  banner in the navbar.

## Piece 8 detail (in progress, started 2026-09-17)

The shared "send the call to X" model, for the four features that will send calls somewhere.
**No schema script**: a destination is a reference to something that already has a table, so the
list is computed and a stored choice is two columns on whichever feature stores it (D35).

- `Core/Models`: `DestinationType` (Extension, Hangup, Voicemail), `Destination` (type + value,
  `Key`/`TryParse` for the string form), `DestinationChoice`, and `DestinationCatalog` — a pure
  function from the rows a caller loaded to the list a picker shows.
- `Asterisk/Config/DestinationDialplan`: the only code that writes "and then the call goes here"
  (D36). `ExtensionsConfRenderer` now uses it for the voicemail fallback, and the golden
  `extensions.conf` did not change.
- `Pages/Shared/_DestinationSelect.cshtml`: the shared picker, grouped by type, which shows a
  destination that no longer resolves instead of silently repointing it.

Still to do:

- **Nothing renders the picker yet.** It compiles, and the logic behind it is tested in Core, but
  it is not on a page until inbound routes (piece 11) or IVRs (piece 17) need it. The extensions
  page had no field it belonged in, and adding one would have been building a feature that is not
  on the list.
- Each feature that stores a destination adds `DestinationType` / `DestinationValue` columns in
  its own schema script, and validates on save that the destination still resolves.
- New destination types are additive: a member on the enum, a case in `DestinationDialplan`, a
  source in `DestinationCatalog.All`. Ring groups and IVRs will each add one.

## Piece 10 detail (in progress, started 2026-09-18)

Which numbers go out, and over which trunk. Schema `005_outbound_routes.sql`, a repository that
refuses international patterns outright (D47), and a dialplan of one context per route included in
priority order, ending in a context that stops everything nothing matched (D45, D46).

Two UI changes landed alongside it, both user decisions: the apply button moved into the navbar and
only exists when there is something to apply (D43), and table rows became clickable with their
actions in the edit modal's footer (D48). Extensions and Trunks were converted too.

Still to do:

- **Place a real outbound call from the lab VM.** Nothing here has dialled a provider yet: worth
  checking the route order, that an unmatched number fails closed, and that `ss-noservice` is
  actually installed (D45) rather than leaving the caller in silence.
- ~~**No strip or prepend digits** (D44), so "dial 9 for an outside line" is not possible.~~
  Added 2026-09-19 (D109): `PrependDigits` / `StripDigits` per route, schema `014`, the
  pattern's underscore stored on save, and the prepend door closed to the international guard.
  Golden file `extensions-routes-digits.conf`; existing goldens unchanged.
- **The international guard assumes North American dialling** (D47). A UK or European site cannot
  write a route at all under it, and will need a deliberate per-system escape hatch.

## Piece 11 detail (in progress, started 2026-09-19)

Which number arriving on which trunk goes where. Schema `006_inbound_routes.sql`, a repository that
checks the trunk and the destination still exist, and entries in each trunk's own context sent on
by the shared destination helper (D36). The `from-trunk-<name>` placeholder from piece 9 is now
only what a trunk with no routes gets (D50).

This is the first consumer of the destination picker built in piece 8 (D35): the form renders
`_DestinationSelect` and stores what it posts as `DestinationType` / `DestinationValue`, exactly
as that decision said a feature would.

Still to do:

- **Take a real inbound call on the lab VM**, and above all **read the log for what Callcentric
  actually puts in `${EXTEN}`** (D51). DIDs are matched exactly, so if the provider sends 10 digits
  where an admin typed 11, the call goes to the catch-all instead. A normalisation step may be
  needed once that is known.
- Destinations are still only Extension, Voicemail and Hangup. Ring groups (piece 15) and IVRs
  (piece 17) each add one, and inbound routes pick them up without changing (D35).
- A route whose destination has since been deleted shows as "gone" in the table and hangs the call
  up in the dialplan. ~~There is no warning anywhere else that it needs fixing.~~ The status
  page's attention list now names every dangling destination (D106).

## Piece 17 detail (built 2026-09-19, not yet lab-verified)

The auto attendant (F6), in two parts: the model, schema `009_ivrs.sql`, repository and renderer
(D58–D61), then the page.

The page is the same shape as the other list pages — a shell htmx fills, the shared Bootstrap
modal for the form (D42), rows that open their own edit form (D48) — with one thing of its own:
the **digit map editor is twelve fixed rows**, one per key a caller can press, each a shared
`_DestinationSelect`. A row left on "Not used" is dropped on save, so adding and removing keys is
choosing and clearing destinations and the editor needs no JavaScript at all. The menu being
edited is in its own keys' picker, because "press 9 to hear this again" is a feature (D59); the
final-destination loop check is the repository's, not the form's.

Still to do:

- **Verify on the lab VM**: `func_timeout.so` is actually built (D61), the core `invalid` prompt is
  installed, and a real call to a menu's play extension plays the greeting, takes a key, times out
  to the final destination and — with direct dial on — reaches an extension.
- **The inbound routes and ring groups forms still offer only the destinations they knew about when
  they were written**: inbound routes offer extensions and voicemail only, ring groups those plus
  ring groups. Both repositories accept announcements and IVRs, and both tables label them, so this
  is one line in each page's `Fill` — but until it is done, **an inbound DID cannot be pointed at an
  IVR from the UI**, which is the main way a menu is meant to be reached.
- The loop check follows IVR → IVR only, so ring group → IVR → ring group is still possible to
  build (D59).

## Piece 22 detail (built 2026-09-21, not yet lab-verified)

Polycom phones configure themselves from the database. Nothing is written to disk and nothing is
applied: both files a phone fetches are generated per request (D79), so this piece adds no
renderer to `ConfigApplier`, no module to the allowlist, and no reason for the apply button to
appear.

| Piece | Where |
|---|---|
| `Phones` table, MAC unique. (Its `ExtensionID` is gone as of piece 30: a phone's registration is its line key, schema 020) | `Data/Schema/011_phones.sql` |
| `Phone` model (MAC rules, `MatchesModel`, derived local SIP port) | `Core/Models` |
| `PolycomUserAgent` (the header regex, D78) | `Core/Models` |
| `BasicAuth` (parse + fixed-time compare, D77) | `Core/Security` |
| `PhoneRepository`, including `Register` — the write a phone causes | `Core/Data` |
| `PolycomFiles`, `PolycomXml`, `PolycomMasterRenderer`, `PolycomConfigRenderer` | `Asterisk/Provisioning` |
| `GET /polycom/{file}`, `[AllowAnonymous]`, outside the Entra cookie (D77) | `Web/Controllers/PolycomController.cs` |
| `Provisioning.Username` / `Provisioning.Password` (the second is a secret) | `SettingsKeys`, `SettingsValidation`, `SettingsCatalog` |
| Phones page: table, edit modal, no Add button and no apply (D78, D79) | `Web/Pages/Phones` |

Still to do:

- **Verify against a real handset on the lab VM.** In order: set the two provisioning settings,
  point a phone at `http://user:pass@<vm>/polycom` with DHCP option 160, watch the log for the
  auto-add, check the row appears on the Phones page, assign an extension, reboot the phone and
  confirm it registers and can call. Then confirm the 403s: a `curl` with the right credentials
  and no Polycom User-Agent, and a second phone claiming the first one's MAC.
- **`voIpProt.SIP.local.port` is the spelling the approved design specifies; Polycom's own docs
  say `voIpProt.local.port`** (D81). If the phone ignores it — two phones behind one NAT still
  both sourcing from 5060 — it is one constant.
- **The digit map is fixed at four-digit extensions** (`xxxx|*xx.T|[2-9]11|0T`). Extensions here
  may be 2 to 6 digits, so a site numbered 101–199 gets a phone that waits 3 seconds before
  dialling rather than dialling at once. Making the map follow the real extension lengths is a
  question for the user, not something to guess.
- **Daylight saving reaches a phone at its next poll, up to a day late** (D82). Open question:
  accept it, or derive Polycom's `tcpIpApp.sntp.daylightSavings.*` parameters from the zone.
- **Kestrel has to be listening on the port the DHCP option names.** D76 has the app on 80/443
  directly; until the installer (piece 21) sets that up, the lab VM's port goes in the option 160
  URL. Provisioning is exempt from the HTTPS redirect for exactly this reason (D77).
- **No upload endpoints** for the `logs`, `overrides` and `contacts` directories the master file
  names (D79), **no firmware serving** (D83), no BLF, no attendant console, no softkey layout, no
  second line, and no non-Polycom phones. Each is its own piece.

## Piece 21 detail (part 1 built 2026-09-22, not yet verified)

`src/Techie.Pbx.Core/scripts/install.sh` turns a fresh Debian 12/13 server into everything a
TNPBX box is **except the application** (D92). It is the lab spike
(`build-asterisk-vm.sh`) with the hand-written config removed, the `tnpbx` user added and the
layout the lab proved made explicit (D94).

What it produces, in order: preflight (root, Debian version, architecture, memory-aware build
jobs) → the clock set to `Etc/UTC` with NTP before anything else (D74) → one `apt-get` for build
and runtime dependencies, with `libicu72`/`libicu76` chosen by Debian version and `ffmpeg`
because announcements require it (D55) → the `asterisk` and `tnpbx` system users, `tnpbx` in the
`asterisk` group, and the directory layout including `/etc/asterisk` at **2770 root:asterisk**
(D18) and `/opt/tnpbx` at 0750 `tnpbx:asterisk`, created and left empty → Asterisk 22 built from
the tarball with a verified sha256, bundled pjproject and `BUILD_NATIVE` off → our own systemd
unit, `daemon-reload`, `enable`, and **no start** (D93).

Since D128 it also builds **whisper.cpp** and downloads the `small.en` model into
`/opt/tnpbx/whisper` for voicemail transcription — the one section that only ever warns, because a
box without it is a working box that emails voicemail without a transcript.

`--dry-run` prints every mutating step instead of running it, and skips the root check, so the
whole install can be read before it is run. `--rebuild` forces the Asterisk build the way the
spike script does; otherwise an existing `22.x` binary is left alone.

Still to do:

- **Run it on a genuinely fresh Debian 12 and Debian 13 VM.** Nothing here has been executed
  yet — only `bash -n` and a `--dry-run` pass. The two things most likely to be wrong are the
  package list (a missing runtime dependency only shows up when the app is deployed) and the
  `libicu` name on Debian 12.
- **Part 2: deploy the application.** Publishing it into `/opt/tnpbx`, `tnpbx-web.service` with
  the hardening named in [security.md](security.md) (`NoNewPrivileges`, `ProtectSystem=strict`,
  explicit `ReadWritePaths`), binding 80/443 as an unprivileged user (D76), and the polkit rule
  that lets the web user restart `asterisk.service` (D33).
- **The firewall (piece 19), the Helper (piece 19) and fail2ban (piece 20)** are not installed
  and not referenced. The script prints them as "deliberately not done" so the operator is not
  left believing the box is protected.
- **`make samples` is not run**, so `/etc/asterisk` is empty until the first apply. That is the
  point (D93), but it means an operator who starts Asterisk by hand before deploying the app
  gets an Asterisk with no config at all.
- **The announcements directory is created `asterisk:asterisk` 2770**, which is what the lab VM
  has; D56 describes it as `root:asterisk`. Both work — `tnpbx` writes there through the
  `asterisk` group either way — but the two should be reconciled.
