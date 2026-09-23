# Security

Small surface area is the reason this project exists, so security rules here are requirements,
not suggestions.

## Threat model

Most real PBX compromises are **toll fraud**: attackers guess or steal SIP credentials and
place expensive international calls. After that come compromised admin UIs and outdated
software.

| Threat | Main defences |
|---|---|
| SIP credential guessing | Long random secrets (`SecretGenerator`), fail2ban → own blocker, firewall limits on 5060 |
| Scanners hitting SIP | Only needed modules loaded, no anonymous calls, explicit dialplan entries |
| Admin UI compromise | Entra ID sign-in, HTTPS only, unprivileged web process |
| Web process compromise | Not root, no sudo, systemd hardening, Helper allows only fixed commands |
| Config injection (user input breaking out of a conf value) | Strict model validation + `ConfText.Safe` in renderers |
| Half-written config during reload | `ConfFileWriter.WriteAtomic` |
| Secrets leaking | DB file 0600, conf files 0640, secrets never logged |

## Rules for code

### Web process
- Never runs as root, never calls sudo, never shells out with string-built commands.
- The API shares the session cookie, so every browser call carries an antiforgery token in the
  `RequestVerificationToken` header and controllers validate it by default (D23). A call without
  a session gets 401, not a redirect (D20).
- SIP secrets are never rendered into a list or a table. They go to the browser one at a time,
  when an admin asks for that one extension, and the request is logged without the value.
- `POST /api/voicemail/notify` is outside the Entra cookie, because the caller is the voicemail
  `mailcmd` script and a script has no session (D129). **The bearer token is the only thing
  protecting it**, so the token check is the first thing the action does, it is compared in fixed
  time, and a blank configured token matches nothing at all. A loopback check sits behind the
  token, never in front of it. Everything the request names is then checked before anything is
  opened: the mailbox must be an extension with voicemail and an address, and the message path
  must match `VoicemailSpool`'s pattern **for that mailbox** — which is what stops traversal and
  stops one mailbox reading another's messages.
- Intended systemd hardening: `NoNewPrivileges=true`, `ProtectSystem=strict` with explicit
  `ReadWritePaths`, `PrivateTmp=true`, `ProtectHome=true`, minimal `CapabilityBoundingSet`.
  Don't add code that would need these relaxed without recording a decision.

### Helper
- Listens only on a Unix socket, never TCP.
- Checks the caller's UID with `SO_PEERCRED`.
- Accepts only typed messages defined in `Techie.Pbx.Contracts`. Every argument is validated
  (enums, IP/CIDR parsing, fixed ranges).
- **Never** a "run command" message, never a file path from the caller, never a shell.
- Keep it small enough to read end to end.

### Asterisk config
- Values reach conf files only through the renderers.
- Models validate strictly (character allowlists, lengths). Renderers re-validate every row
  and `ConfText.Safe` rejects `\r \n \0 ; [ ] "` and control characters anyway, so a bad row
  that bypassed the repository still can't inject a section.
- Generated files are owner/group readable only (0640); they contain SIP secrets.
- `modules.conf` is an allowlist: `autoload = no` and an explicit `load =` per module we use
  (D31). A feature that needs a new module adds it in the same change, and the list is reviewed
  the same way as any other code.
- AMI binds to `127.0.0.1` and permits only `127.0.0.1`, with `webenabled = no`, and the account
  gets `read = system,cdr` / `write = system,config` — no `command`, no `originate` (D32;
  `cdr` added in piece 18 for the call reports, as D32 anticipated).
- AMI binds to `127.0.0.1` only.

### Secrets
- SIP secrets are 16–64 letters/digits; generated ones are 24 characters from an alphabet
  without look-alike characters.
- Never log secrets. Never put real credentials in the repo, docs, tests or commit messages.
  Tests use obviously fake values.
- The W3C web request log (D116) records the client address, the path, the status and the
  User-Agent of every request. It deliberately does **not** record `cs(Cookie)`, because our cookie
  is the Entra session itself, and it never sees the `Authorization` header: the provisioning
  username reaches `cs-username` through `RequestLogUserMiddleware`, which reads the username half
  of a Basic header and discards the password without looking at it.
- `/opt/tnpbx/Config/mail.json` holds the SMTP relay password, because the voicemail `mailcmd`
  script runs as `asterisk` and cannot read the database (D126). It is 0640 `tnpbx:asterisk` in a
  setgid 2750 directory, never logged, and **removed** rather than left stale when the relay
  settings are cleared. It also carries `Mail.VoicemailCallbackToken` (D129), which is a secret in
  the same sense and lives under the same mode: it is the credential the script proves itself with
  on `/api/voicemail/notify`. Generated, never typed, and masked in the settings table like the
  AMI secret.

### The voicemail mailcmd script

`/opt/tnpbx/bin/voicemail-mail` is the only program of ours that Asterisk executes (D126).

- **root:root 0755.** Neither the web user nor the asterisk user may rewrite it. `app-deploy.sh`
  re-asserts this *after* its `chown -R tnpbx`, so a deploy cannot hand it to the web user.
- **No arguments, no environment, no shell.** app_voicemail runs it as
  `( mailcmd < tmpfile ; rm -f tmpfile ) &`, so there is no command line to inject into. It reads
  fixed paths only — its config, the generated transcription options, and stdin — and no path
  ever comes from the message or from the environment, `PATH` included. The one path it *builds*
  is the message's own (D129): from a mailbox and a context it has matched against a pattern and a
  message number it formats itself, and the application checks the result again before opening
  anything.
- **The callback goes to this machine and nowhere else** (D129). It posts the message's details to
  the URL in `mail.json`, and refuses to post them anywhere that is not `127.0.0.1` or
  `localhost` — a voicemail's caller ID and mailbox are not for a URL somebody edited into a file.
- **Fails closed on the relay.** A missing, unreadable, malformed or incomplete config is exit 1
  with a sentence on stderr. There is no fallback path that sends mail some other way. The
  callback is the other way round: every way it can fail ends with the message relayed here
  instead, because an application that is down must not mean a voicemail nobody hears.
- **Never submits the relay password in the clear**: implicit TLS on 465, STARTTLS elsewhere, and
  it hangs up rather than authenticating to a relay that offers neither.
- **Transcription (D128) is the only thing it executes**, and only two programs: `ffmpeg` at one of
  two fixed paths and `/opt/tnpbx/bin/whisper-cli`, each with an argument list and never a shell
  string. Both are handed a temporary file the script wrote itself from the MIME attachment —
  never a path from the message — in a 0700 directory it deletes however it returns, so a
  customer's recording does not linger in `/tmp`. `whisper-cli` and its model are root-owned for
  the same reason this script is. Where the relay fails closed, transcription **fails open**: every
  way it can go wrong relays the voicemail exactly as app_voicemail composed it.

## Known gaps

Deliberately deferred. Don't treat them as done.

| Gap | Risk | Notes |
|---|---|---|
| ~~Any user in the Entra tenant can sign in as an admin~~ | Closed 2026-09-23 (D139) | Enterprise app requires assignment; Entra refuses unassigned users at sign-in. Admin changes are an Entra-side workflow. |
| No break-glass login if Entra is unreachable | Admin lockout | Deferred by user (D6) |
| No firewall or fail2ban yet | SIP brute force | Lab VM is protected by the Azure NSG only |
| SIP secrets stored in plain text in DB and `pjsip.conf` | Secret exposure if files are read | Option: `auth_type = md5` with `md5_cred`, show password once at creation |
| UDP SIP only, no TLS/SRTP | Eavesdropping | Add a TLS transport later |
