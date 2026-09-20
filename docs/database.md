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

### Phones (011)

Desk phones that provision themselves from us (D77, D78). Most rows are created by the phone
rather than by an admin: a valid credential plus a Polycom User-Agent plus an unknown MAC inserts
one. Nothing here is rendered into `/etc/asterisk` — a phone's config is generated per request
(D79) — so a write to this table raises no config-pending marker.

| Column | Type | Notes |
|---|---|---|
| `PhoneID` | INTEGER PK | Also decides the phone's local SIP port, `1024 + (PhoneID mod 64512)` (D81) |
| `Mac` | TEXT, unique | Exactly 12 lower-case hex digits, no separators. The key a provisioning request looks up |
| `Name` | TEXT | What an admin calls it. Empty until somebody names it |
| `Model` | TEXT | From the User-Agent, e.g. `VVX_410`. A request whose model stops matching gets 403 (D78) |
| `Firmware` | TEXT | From the User-Agent, e.g. `5.9.5.0614` |
| `LastIP` | TEXT | Where it last asked from |
| `LastConfig` | TEXT | When it last fetched its config, `2026-09-17 09:31:02Z`. Empty = never |
| `ExtensionID` | INTEGER FK → `Extensions`, nullable | `ON DELETE SET NULL`: deleting an extension unassigns the phone rather than being refused (D80) |
| `Enabled` | INTEGER | 0/1, default 1. A disabled phone is refused its config at the next poll |

### MohFiles (017)

The music on hold tracks (D119). One class, one directory, one row per file in it — there is no
per-class UI, so a track has no options of its own and nothing points at it.

| Column | Type | Notes |
|---|---|---|
| `MohFileID` | INTEGER PK | Also the first part of the stored file name, which is what makes it unique |
| `Name` | TEXT | Up to 64 chars. **Not** unique: nothing refers to a track by name |
| `File` | TEXT, unique | Stored file name, `<MohFileID>-<slug>.wav`, under `/var/lib/asterisk/moh`. Unique because one flat directory is what `res_musiconhold` plays |
| `CreatedUnix` | INTEGER | When it was uploaded. Shown, nothing else |

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

And call parking (D119), all five of which a generated conf file carries: `Parking.Enabled`
(`on`/`off`, default off), `Parking.DtmfCode` (a star and one or two digits, default `*3`),
`Parking.Slots` (1–9, default 9), `Parking.Timeout` (30–600 seconds, default 60) and
`Parking.Audio` (`silence` or `moh`, default silence).

And the mail settings (D115), which this application reads and no generated conf file carries:
`Mail.Transport` (`graph`, `smtp`, or blank for "decide for me"), `Mail.FromAddress`,
`Mail.FromName`, `Mail.Smtp.Host`, `Mail.Smtp.Port`, `Mail.Smtp.Username` and
`Mail.Smtp.Password`.

`Ami.Secret`, `Provisioning.Password` and `Mail.Smtp.Password` are credentials (D14, D77, D115):
`SettingsKeys.IsSecret` marks them, they are never logged, and the table masks them — the edit
form shows the stored value (D112). Defaults are not seeded as rows — they live on `AmiSettings` and `PjsipTransport`,
and `AsteriskSettings` applies a row on top only when there is one.
