-- Auto attendant (F6): "press 1 for sales, 2 for support". The greeting is a reference to an
-- announcement rather than audio of its own (D58), so there is one audio store, one upload path
-- and one set of checks; AnnouncementID is a real foreign key, which is what stops an
-- announcement an IVR still greets with being deleted.
--
-- PlayExtension is optional and works exactly as an announcement's does (D57): when it is set the
-- IVR gets a number that can be dialled to hear the menu, and that number is what a destination
-- points at. Without it the IVR has no dialplan entry and cannot be a destination.
--
-- The final destination — where a caller who pressed nothing usable ends up — is the two fields
-- D35 settled on.
CREATE TABLE Ivrs (
    IvrID            INTEGER PRIMARY KEY,
    Name             TEXT    NOT NULL UNIQUE,
    Description      TEXT    NOT NULL DEFAULT '',
    AnnouncementID   INTEGER NOT NULL REFERENCES Announcements(AnnouncementID),
    PlayExtension    TEXT    NOT NULL DEFAULT '',
    TimeoutSeconds   INTEGER NOT NULL DEFAULT 10,
    Retries          INTEGER NOT NULL DEFAULT 3,
    EnableDirectDial INTEGER NOT NULL DEFAULT 0,
    DestinationType  TEXT    NOT NULL DEFAULT 'Hangup',
    DestinationValue TEXT    NOT NULL DEFAULT '',
    Enabled          INTEGER NOT NULL DEFAULT 1
);

-- The digit map: one row per key that does something. A digit with no row falls through to the
-- IVR's invalid handling, so "free" digits need no rows and no flags (D59).
--
-- A table rather than a column of pairs, unlike a ring group's members (D53), because each digit
-- carries a destination of its own and order means nothing here. ON DELETE CASCADE because an
-- entry has no life of its own: deleting the IVR deletes its keys.
CREATE TABLE IvrEntries (
    IvrEntryID       INTEGER PRIMARY KEY,
    IvrID            INTEGER NOT NULL REFERENCES Ivrs(IvrID) ON DELETE CASCADE,
    Digit            TEXT    NOT NULL,
    DestinationType  TEXT    NOT NULL,
    DestinationValue TEXT    NOT NULL DEFAULT '',
    UNIQUE (IvrID, Digit)
);
