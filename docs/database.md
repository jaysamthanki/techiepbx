# Database

SQLite, accessed with Dapper over `Microsoft.Data.Sqlite`. One file, owned by the web
service user, mode 0600.

## Conventions

- **Primary key:** `<Entity>ID INTEGER PRIMARY KEY`, e.g. `ExtensionID`. Never `Id`.
- **Foreign keys:** same name as the key they reference (`Voicemail.ExtensionID` → `Extensions.ExtensionID`).
- **Names:** PascalCase. Tables are plural (`Extensions`), columns singular (`Number`).
- **Booleans:** `INTEGER NOT NULL` with 0/1.
- **C# models** use the same names as the columns, so Dapper maps them with no configuration.
- Foreign key enforcement is on (`ForeignKeys = true` in the connection string).

## Schema scripts

- Location: `src/Techie.Pbx.Core/Data/Schema/NNN_description.sql`, embedded into the assembly.
- `Database.Migrate()` reads `PRAGMA user_version`, runs every script with a higher number in
  order, each in its own transaction, then sets `user_version` to that number.
- **Add a new script for every change. Never edit one that has been deployed.** Until the
  first real install, `001_initial.sql` may still be edited; this note gets removed when that
  changes.
- Scripts can contain several statements.

## Data access

- One repository class per aggregate (`ExtensionRepository`, `SettingsRepository`), constructed
  with `new Repository(database)`.
- Repositories validate models before writing and turn constraint violations into
  `ValidationFailedException` with a message fit to show a user.
- Write explicit column lists, no `SELECT *`.

## Tables

### Extensions (001)

| Column | Type | Notes |
|---|---|---|
| `ExtensionID` | INTEGER PK | |
| `Number` | TEXT, unique | 2–6 digits |
| `Name` | TEXT | Up to 64 chars: letters, digits, space, `. , ' - _ ( ) &`. Used as caller ID name. |
| `Secret` | TEXT | SIP password, 16–64 letters/digits |
| `Enabled` | INTEGER | 0/1, default 1. Disabled extensions are left out of the generated config. |

### Extensions: voicemail columns (003)

One optional mailbox per extension, so columns rather than a table (D27). A disabled extension
gets no mailbox whatever these say.

| Column | Type | Notes |
|---|---|---|
| `VoicemailEnabled` | INTEGER | 0/1, default 0 |
| `VoicemailPin` | TEXT | 4–8 digits, required once enabled. Written into `voicemail.conf` as typed (D28) |
| `VoicemailEmail` | TEXT | Optional. Stored and rendered, but nothing sends email yet (F4) |
| `VoicemailAttachRecording` | INTEGER | 0/1, default 1 |
| `VoicemailDeleteAfterEmail` | INTEGER | 0/1, default 0. Never rendered as `delete=yes` without an address to email |

### Announcements (008)

| Column | Type | Notes |
|---|---|---|
| `AnnouncementID` | INTEGER PK | Also names the audio directory (D56) |
| `Name` | TEXT, unique | Up to 64 chars; drives the stored file name (slug) |
| `Description` | TEXT | Optional, default '' |
| `PlayExtension` | TEXT, optional | Digits; collision-checked against extensions, ring groups, feature codes (D57). Empty = no dialplan entry, not a destination |
| `AudioFile` | TEXT | Stored file name (`<slug>.wav`); empty = no audio yet |
| `Enabled` | INTEGER | 0/1, default 1 |

### Ivrs (009)

The auto attendant (F6). The greeting is a **reference** to an announcement rather than audio of
its own (D58), so `AnnouncementID` is a real foreign key: an announcement an IVR still greets with
cannot be deleted.

| Column | Type | Notes |
|---|---|---|
| `IvrID` | INTEGER PK | Also names the menu's dialplan context, `ivr-<IvrID>` (D59) |
| `Name` | TEXT, unique | Up to 64 chars |
| `Description` | TEXT | Optional, default ''. Becomes a dialplan comment |
| `AnnouncementID` | INTEGER FK → `Announcements` | Required. The greeting, played with `Background()` (D58) |
| `PlayExtension` | TEXT, optional | Digits; collision-checked against extensions, ring groups, announcements and other IVRs, both ways (D57). Empty = no dialplan entry, not a destination |
| `TimeoutSeconds` | INTEGER | Default 10. The wait for a key, and the gap allowed between digits of a directly dialled extension |
| `Retries` | INTEGER | Default 3. Second chances after a timeout or an unused key; 0 gives up at the first |
| `EnableDirectDial` | INTEGER | 0/1, default 0. One `Goto` entry per enabled extension in the menu's context (D60) |
| `DestinationType` / `DestinationValue` | TEXT | Where a caller who chose nothing goes (D35). Empty type-only `Hangup` = hang up |
| `Enabled` | INTEGER | 0/1, default 1 |

### IvrEntries (009)

The digit map: one row per key that does something. A digit with no row is not a setting, it is
absent — the caller gets the IVR's invalid handling (D59). A table rather than a list in one column
(unlike ring group members, D53) because each key carries a destination and order means nothing.

| Column | Type | Notes |
|---|---|---|
| `IvrEntryID` | INTEGER PK | |
| `IvrID` | INTEGER FK → `Ivrs` | `ON DELETE CASCADE`: a key has no life without its menu |
| `Digit` | TEXT | One character: `0`-`9`, `*` or `#` |
| `DestinationType` / `DestinationValue` | TEXT | Where that key sends the call (D35) |
| | | `UNIQUE (IvrID, Digit)`: one menu cannot use a key twice |

### TimeConditions (010)

The business-hours switch (F8). One row is the whole condition: three destinations, no
time-group entity to reference (D62). Rules live in `TimeConditionRules` and are replaced
wholesale in one save, the way an IVR's digit map is.

| Column | Type | Notes |
|---|---|---|
| `TimeConditionID` | INTEGER PK | Also names the context, `tc-<TimeConditionID>` (D62) |
| `Name` | TEXT, unique | Up to 64 chars |
| `Description` | TEXT | Optional. Dialplan comment |
| `PlayExtension` | TEXT, optional | Digits; collision-checked against extensions, ring groups, announcements, IVRs and other conditions (D57, D62). Empty = no dialplan entry, not a destination |
| `OpenDestinationType` / `OpenDestinationValue` | TEXT | Where a call inside the open hours goes (D35) |
| `ClosedDestinationType` / `ClosedDestinationValue` | TEXT | Where a call outside them goes |
| `HolidayDestinationType` / `HolidayDestinationValue` | TEXT | Where a call on a holiday date goes, unless that date overrides it (D63) |
| `Enabled` | INTEGER | 0/1, default 1 |

### TimeConditionRules (010)

One row per line of the condition: either a weekly open window or a holiday date. `Kind` says
which fields mean anything, because the two are edited in one form and always read together (D62,
D63). `ON DELETE CASCADE`: a rule has no life without its condition.

| Column | Type | Notes |
|---|---|---|
| `TimeConditionRuleID` | INTEGER PK | |
| `TimeConditionID` | INTEGER FK → `TimeConditions` | |
| `Kind` | INTEGER | 0 = weekly, 1 = holiday |
| `DaysMask` | INTEGER | Bit per weekday, Monday = 1. Weekly only |
| `StartTime` / `EndTime` | TEXT | `HH:MM` window. Weekly only |
| `HolidayDate` | TEXT | `YYYY-MM-DD`. Holiday only; the year is stored but never matched (D64) |
| `DestinationType` / `DestinationValue` | TEXT | Holiday-only override destination; empty = the condition's holiday destination (D63) |
| `SortOrder` | INTEGER | The order the rules are written in |

### Phones (011, 012, 020)

Desk phones that provision themselves from us (D77, D78). Most rows are created by the phone
rather than by an admin: a valid credential plus a Polycom User-Agent plus an unknown MAC inserts
one. Nothing here is rendered into `/etc/asterisk` — a phone's config is generated per request
(D79) — so a write to this table raises no config-pending marker.

**There is no `ExtensionID`.** Which extension a phone registers as is its line key in
`PhoneButtons` (020, D121 amended); `PhoneButton.LineNumber` is what asks, and
`PhoneButtonRepository.GetLines` answers it for every phone at once.

| Column | Type | Notes |
|---|---|---|
| `PhoneID` | INTEGER PK | Also decides the phone's local SIP port, `1024 + (PhoneID mod 64512)` (D81) |
| `Mac` | TEXT, unique | Exactly 12 lower-case hex digits, no separators. The key a provisioning request looks up |
| `Name` | TEXT | What an admin calls it. Empty until somebody names it |
| `Model` | TEXT | From the User-Agent, e.g. `VVX_410`. A request whose model stops matching gets 403 (D78) |
| `Firmware` | TEXT | From the User-Agent, e.g. `5.9.5.0614` |
| `LastIP` | TEXT | Where it last asked from |
| `LastConfig` | TEXT | When it last fetched its config, `2026-09-17 09:31:02Z`. Empty = never |
| `Brand` | TEXT | `Polycom` or `Yealink`, set at insert and never changed afterwards (012, D88) |
| `Enabled` | INTEGER | 0/1, default 1. A disabled phone is refused its config at the next poll |

### MohClasses (019)

The music on hold classes (D122). A class is a name Asterisk knows and a directory it plays, so
this is what `musiconhold.conf` writes a section each for. The class that ships is seeded by the
schema script; its audio is put there by the installer.

| Column | Type | Notes |
|---|---|---|
| `MohClassID` | INTEGER PK | |
| `Name` | TEXT, unique `COLLATE NOCASE` | What Asterisk calls the class and what a setting names. Case-insensitive because Asterisk matches class names with `strcasecmp`; never `default`, which is Asterisk's own fallback (D119) |
| `Directory` | TEXT, unique | One subdirectory of `/var/lib/asterisk/moh`: lower-case letters, digits and dashes, up to 24 chars. Separate from `Name` so renaming a class need not move files |
| `IsDefault` | INTEGER | 0/1. The class that ships with the product, seeded as `Standard` / `default`. Cannot be deleted: it is where the installer writes |

### MohFiles (017, 019)

The music on hold tracks (D119, D122). One row per file in a class's directory; a track has no
options of its own and nothing points at it.

| Column | Type | Notes |
|---|---|---|
| `MohFileID` | INTEGER PK | Also the first part of an uploaded file's name, which is what keeps two tracks apart |
| `MohClassID` | INTEGER FK → `MohClasses` | `ON DELETE CASCADE`: a track outside a class is a file nothing would play (019) |
| `Name` | TEXT | Up to 64 chars. **Not** unique: nothing refers to a track by name |
| `File` | TEXT, unique per class | Stored file name under `/var/lib/asterisk/moh/<class directory>`. `<MohFileID>-<slug>.wav` for an upload, `default-N.wav` for the tracks the installer writes. Unique per class because a class plays its own directory whole |
| `CreatedUnix` | INTEGER | When it was uploaded. Shown, nothing else |

### InboundRoutes: music on hold (021)

Which class a caller on this route hears whenever somebody holds them (D122 amended). The table
itself is from 006; this is the one column added since.

| Column | Type | Notes |
|---|---|---|
| `MohClassID` | INTEGER FK → `MohClasses`, nullable | Null means no class is named, which is what every route did before this column and leaves the channel to Asterisk's own fallback. `ON DELETE SET NULL`: deleting a class must not delete the route, and a route naming a class that has gone is a name the renderer refuses to write |

### OutboundRoutes: caller ID and music on hold (022)

What a call that matched this route calls out as, and what its caller hears when the far side holds
them (D125). The table itself is from 005 (014 added the digit columns); these are the two added
since. `Extensions.OutboundCallerID` lands in the same script, because the three columns are one
decision.

| Column | Type | Notes |
|---|---|---|
| `CallerID` | TEXT, default `''` | FreePBX's "option CID": a bare number of up to 15 digits, or `"Acme Sales" <17141234567>` — `CallerIDFormat` reads both, and it is the same validator the extension column uses. Empty means none, and then the trunk's own `callerid` says who we are. Written **guarded** into the dialplan, so an extension that claimed one keeps it |
| `MohClassID` | INTEGER FK → `MohClasses`, nullable | The outbound twin of `InboundRoutes.MohClassID`, and the same rules: null means no class named, `ON DELETE SET NULL` so deleting a class does not delete the route, and the renderer refuses a class it was not given or one called `default` |

### Extensions: outbound caller ID (022)

| Column | Type | Notes |
|---|---|---|
| `OutboundCallerID` | TEXT, default `''` | The user with a direct DID of their own. Same two forms as the route column, and it **beats** the route: the endpoint carries it as `set_var = TNPBX_CID=...`, which the outbound route contexts apply and nothing else reads. Empty means no claim, which is every extension until somebody sets one. Internal calls are unaffected — they show the endpoint's own `callerid` (D125) |

### PhoneButtons (018, 020)

The assignable keys on a phone (D121): one row per key that has something on it, so a key nobody
assigned is absent rather than a row saying "nothing". Like `Phones` it raises no config-pending
marker — the hints the lamps watch are in the dialplan whether a key points at them or not.

Since 020 this is also **where a phone's registration lives**: key 1 is a `Line`, and a phone
without one registers as nothing. `PhoneButton.ValidateSet` is what holds that together — a line
is required, the lines are keys 1..n with no gap in them, and no two phones may claim one
extension as a line. Every other key may be left blank wherever the admin wants the gap, and the
renderers keep each key where it was put (D121 amended again).

| Column | Type | Notes |
|---|---|---|
| `PhoneButtonID` | INTEGER PK | |
| `PhoneID` | INTEGER FK → `Phones` | `ON DELETE CASCADE`: a key has no life of its own |
| `Position` | INTEGER | Which key, 1 to 8. Unique per phone, and **sparse**: a key left blank has no row and the keys after it keep their own numbers (D121 amended) |
| `TargetType` | TEXT | `Line`, `Blf` or `ParkingSlot`. No `CHECK`, so the reserved `CallFlowControl` needs no schema script (D35, D121) |
| `TargetValue` | TEXT | The extension number, or the slot number. A reference, never a copy |

Script 020 renames every `Extension` row to `Blf`, inserts each phone's old `Phones.ExtensionID`
as a `Line` at key 1 and shifts that phone's other keys down one — **dropping the eighth**, which
has nowhere to go — then drops the column. The shift is two passes (out to `Position + 1000`, back
to `Position - 999`) because `UNIQUE (PhoneID, Position)` is checked row by row.

### Settings (002)

Key/value rather than a column per setting, so adding one needs no schema script (D15).

| Column | Type | Notes |
|---|---|---|
| `SettingID` | INTEGER PK | |
| `Key` | TEXT, unique | Must be one of the constants in `SettingsKeys`; the repository rejects anything else. Quoted in SQL, since `KEY` is a SQLite keyword. |
| `Value` | TEXT | Up to 1024 characters. Empty or blank counts as "not set", so the default applies. |

Current keys: `Asterisk.ConfDirectory`, `Ami.Host`, `Ami.Port`, `Ami.Username`, `Ami.Secret`,
`Ami.TimeoutSeconds`, `Sip.BindAddress`, `Sip.Port`, `Sip.LocalNets` (comma separated CIDRs),
`Sip.ExternalAddress`.

Also `Sip.TcpPort`, `Sip.TlsPort`, `Sip.StunServer`, `Sip.Codecs`, `System.Timezone`,
`Provisioning.Username` and `Provisioning.Password` — the last two being the user:pass a phone
sends to fetch its configuration, i.e. the credentials embedded in the DHCP option 160 URL (D77).

And call parking (D119, D122), all six of which a generated conf file carries: `Parking.Enabled`
(`on`/`off`, default off), `Parking.DtmfCode` (a star and one or two digits, default `*3`),
`Parking.Slots` (1–9, default 9), `Parking.Timeout` (30–600 seconds, default 60),
`Parking.Audio` (`silence` or `moh`, default silence) and `Parking.MusicClass` (the name of a
music on hold class, default `Standard`, written as `parkedmusicclass`).

And the mail settings (D115), which this application reads and no generated conf file carries:
`Mail.Transport` (`graph`, `smtp`, or blank for "decide for me"), `Mail.FromAddress`,
`Mail.FromName`, `Mail.Smtp.Host`, `Mail.Smtp.Port`, `Mail.Smtp.Username` and
`Mail.Smtp.Password`.

`Ami.Secret`, `Provisioning.Password` and `Mail.Smtp.Password` are credentials (D14, D77, D115):
`SettingsKeys.IsSecret` marks them, they are never logged, and the table masks them — the edit
form shows the stored value (D112). Defaults are not seeded as rows — they live on `AmiSettings` and `PjsipTransport`,
and `AsteriskSettings` applies a row on top only when there is one.
