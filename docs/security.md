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
  gets `read = system` / `write = system,config` — no `command`, no `originate` (D32).
- AMI binds to `127.0.0.1` only.

### Secrets
- SIP secrets are 16–64 letters/digits; generated ones are 24 characters from an alphabet
  without look-alike characters.
- Never log secrets. Never put real credentials in the repo, docs, tests or commit messages.
  Tests use obviously fake values.

## Known gaps

Deliberately deferred. Don't treat them as done.

| Gap | Risk | Notes |
|---|---|---|
| Any user in the Entra tenant can sign in as an admin | Over-broad access | Add an app role (e.g. `Pbx.Admin`) and require it |
| No break-glass login if Entra is unreachable | Admin lockout | Deferred by user (D6) |
| No firewall or fail2ban yet | SIP brute force | Lab VM is protected by the Azure NSG only |
| SIP secrets stored in plain text in DB and `pjsip.conf` | Secret exposure if files are read | Option: `auth_type = md5` with `md5_cred`, show password once at creation |
| UDP SIP only, no TLS/SRTP | Eavesdropping | Add a TLS transport later |
