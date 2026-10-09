# FreePBX import — manifest schema v1

The migration path from a customer's running FreePBX to TNPBX is two halves:

- **Exporter** — `scripts/freepbx-export/export.php`, a standalone PHP script run on the
  FreePBX box itself (as root, or any user that can read the DB). It reads the `asterisk`
  MySQL database, `/etc/asterisk/voicemail.conf`, the voicemail spool and the custom sounds
  directory, and writes a single `tnpbx-migrate.tar.gz` containing `manifest.json` (the
  contract below) plus `files/` (sounds and voicemail messages referenced by the manifest).
- **Importer** — a piece in the web app (see roadmap) that takes the tarball, previews what
  will land, and imports into the TNPBX schema. **The importer never parses FreePBX's database
  or conf syntax**; it only reads `manifest.json`. All FreePBX-version drift (14/15/16/17
  table differences) is absorbed once, inside the exporter, on the box where the real data
  can be inspected.

Both halves are tested against a real FreePBX 16/Asterisk 16 box (chan_sip + pjsip mixed) and
a real FreePBX 17/Asterisk 22 box.

## Scope (what the manifest carries)

Required, from the user:

1. Extensions with secrets (chan_sip and pjsip boxes both; chan_sip converts to pjsip).
2. Voicemail messages and PINs.
3. Per-extension email address.
4. Custom sounds / system recordings / announcement files.
5. Polycom MACs and their line/key assignments.
6. Trunks (with secrets — FreePBX stores them plaintext).
7. Outbound routes.
8. Inbound routes.

Everything else FreePBX has (IVRs, ring groups, time conditions, parking, queues...) is
**not** in v1. Destinations that name them are reported by the importer as
`skipped, needs manual attention` rather than silently dropped.

## The import clears first (D174)

The six tables the import owns — extensions, trunks, phones (with their keys), outbound routes,
inbound routes and announcements — are **emptied before anything lands**. There is no
keep-or-overwrite decision to make: what the import lands is everything there will be.
The preview shows what will be deleted before the operator can confirm; the report says what
was. Settings, users, parking and everything else the import does not own are untouched, and
voicemail already in the spool is never deleted by the clear (messages are copied in, never
removed). Re-running an import is clean: it clears and lands the same rows again, so a retry
after a failed import does not leave half of the old one behind. **Take a backup first** —
`tnpbx restore` (D171) or a database copy — if anything already there matters.

## Manifest v1

Top level:

```json
{
  "manifestVersion": 1,
  "exported": "2026-10-08T14:03:11Z",
  "source": { "distribution": "FreePBX", "version": "16.0.x", "asterisk": "16.30.0" },
  "extensions": [],
  "trunks": [],
  "outboundRoutes": [],
  "inboundRoutes": [],
  "sounds": [],
  "phones": [],
  "warnings": []
}
```

Every array element is JSON with the fields below. Unknown FreePBX rows are collected into
`warnings` (free text) rather than failing the export — a migration that half-lands and says
why beats one that refuses.

### extensions[]

| field | from | notes |
|---|---|---|
| `number` | `sip.id` / `pjsip.id` / `devices.id` | |
| `name` | `users.name` / voicemail.conf name field | display name |
| `secret` | `sip.keyword='secret'` / `pjsip` secret row | plaintext; TNPBX stores plaintext too (D112) |
| `outboundCallerId` | `users.outboundcid` | |
| `voicemailEnabled` | voicemail.conf box exists for this ext | |
| `voicemailPin` | voicemail.conf `ext=PIN,...` first field | blank PIN exports as blank |
| `voicemailEmail` | voicemail.conf 3rd field | |
| `voicemailAttach` | `attach=yes` in the box options | |
| `voicemailMessages` | `files/voicemail/<ext>/...` count | audio in the tarball |
| `tech` | `devices.tech` | `sip` or `pjsip`; informational — importer converts chan_sip boxes to pjsip, same ext + secret |

### trunks[]

| field | from | notes |
|---|---|---|
| `name` | `trunks.name` | TNPBX section-name rules applied by importer |
| `tech` | `trunks.tech` | `pjsip` or `sip` (chan_sip converts to pjsip) |
| `serverHost` / `serverPort` | pjsip `sip_uri` / `sips_uri`, chan_sip `host` | |
| `username` / `authUsername` / `password` | pjsip auth + registration rows, chan_sip secret | plaintext secrets |
| `register` | registration present | |
| `matchAddresses` | pjsip identify / chan_sip permit | provider signalling addresses |
| `callerIdNumber` | `trunks.outcid` | |

**Import policy (D170): every trunk lands `Enabled = false`.** A migration runs while
production is still live; an imported trunk that registers to the provider from the TNPBX
box would collide with production registrations and could take inbound calls. The importer
reports the disabled trunks explicitly and the operator enables them at cutover.

### outboundRoutes[]

| field | from | notes |
|---|---|---|
| `name` | `outbound_routes.name` | |
| `priority` | `outbound_routes.seq` | |
| `trunkName` | `outbound_route_trunks` → resolved by name | importer resolves to TrunkID; unknown trunk = warning |
| `dialPattern` | `outbound_route_patterns.match_pattern_pass` | FreePBX pattern grammar (`N`, `X`, `Z`, `[...]`, `.`) preserved as-is |
| `stripDigits` | `match_pattern_prefix` (its length) | digits the caller dials that get STRIPPED before the trunk (`9\|NXXXXXX`) — not the same thing as prepend |
| `prependDigits` | `prepend_digits` | digits ADDED to the front of what goes to the trunk |

Pattern grammar translation happens **in the importer**, not the exporter, so the manifest
stays a faithful copy of what FreePBX had and grammar bugs are fixed in one place with
tests.

### inboundRoutes[]

| field | from | notes |
|---|---|---|
| `did` | `incoming.extension` | |
| `callerIdMatch` | `incoming.cidnum` | blank = any |
| `trunkName` | `incoming` has no trunk binding in FreePBX (any trunk) | importer leaves trunk unbound |
| `description` | `incoming.description` | |
| `destination` | `incoming.destination` (e.g. `ext-local,101,1`) | translated below |

Destination translation (exporter writes the raw FreePBX destination string plus a
pre-resolved shape; importer maps what it can and warns on the rest):

- `ext-local,<ext>,1` → `{ "type": "Extension", "value": "<ext>" }`
- anything else → `{ "type": "Unsupported", "value": "<raw>" }` + warning, reported in the
  import preview.

### sounds[]

| field | from | notes |
|---|---|---|
| `filename` | file under `/var/lib/asterisk/sounds/custom*` | as stored in the tarball under `files/sounds/` |
| `announcementName` | `announcement` table, where referenced | name of the announcement the operator had |

Audio format conversion (our announcements are 8 kHz WAV, D55) happens at import, on the
TNPBX box, where ffmpeg exists. The tarball carries FreePBX's files byte-identical.

### phones[]

From the Polycom module's own tables (`polycom_devices`, `polycom_device_lines`,
`polycom_device_line_settings`, `polycom_device_attendants`):

| field | from | notes |
|---|---|---|
| `mac` | `polycom_devices.mac` | colonless |
| `model` | `polycom_devices.model` | e.g. `VVX_410` |
| `line` | `polycom_device_lines.deviceid` → line ext | maps to the phone's Line button (schema 020) |
| `keys[]` | `polycom_device_attendants` | maps to BLF buttons, position preserved |

Phones whose MAC has no line assignment still import (the phone row exists, no buttons) —
same as a TNPBX phone with no Line button, which is valid post-020.

## Payload layout

```
tnpbx-migrate.tar.gz
├── manifest.json
└── files/
    ├── sounds/<name>            # custom sounds, byte-identical
    └── voicemail/<ext>/msgNNNN   # each message: WAV + msg*.txt envelope
```

The importer is the only consumer, and it never executes anything out of the tarball —
manifest fields are data, files land under our own store paths after format conversion.
