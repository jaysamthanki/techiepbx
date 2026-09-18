-- Provisioned desk phones (piece 22). A row is a piece of hardware, identified by its MAC
-- address, that has asked us for its configuration. Most rows are created by the phone itself:
-- a phone with valid provisioning credentials and a Polycom User-Agent that asks for a MAC we
-- have never seen is added here with what its User-Agent says about it (D78).
--
-- Nothing here is rendered into /etc/asterisk. A phone's config is generated per request and
-- never written to disk (D79), so a write to this table is not a config change and does not
-- raise the apply marker.
--
-- ExtensionID is ON DELETE SET NULL: deleting an extension leaves the phone known but
-- unassigned, because the hardware is still on the desk and will want assigning again (D80).
CREATE TABLE Phones (
    PhoneID     INTEGER PRIMARY KEY,
    Mac         TEXT    NOT NULL UNIQUE,
    Name        TEXT    NOT NULL DEFAULT '',
    Model       TEXT    NOT NULL DEFAULT '',
    Firmware    TEXT    NOT NULL DEFAULT '',
    LastIP      TEXT    NOT NULL DEFAULT '',
    LastConfig  TEXT    NOT NULL DEFAULT '',
    ExtensionID INTEGER NULL REFERENCES Extensions(ExtensionID) ON DELETE SET NULL,
    Enabled     INTEGER NOT NULL DEFAULT 1
);
