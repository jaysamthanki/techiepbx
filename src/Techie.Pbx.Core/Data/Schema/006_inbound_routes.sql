-- "A call arriving on this trunk for this number goes there" (D49). The destination is the two
-- fields D35 settled on: a type by name and what it points at.
--
-- UNIQUE(TrunkID, DID) does two jobs: it stops two routes claiming the same number on one trunk,
-- which would be two dialplan entries for one extension, and because a catch-all stores an empty
-- DID, it also stops a trunk having two catch-alls.
CREATE TABLE InboundRoutes (
    InboundRouteID   INTEGER PRIMARY KEY,
    TrunkID          INTEGER NOT NULL REFERENCES Trunks(TrunkID),
    DID              TEXT    NOT NULL DEFAULT '',
    CatchAll         INTEGER NOT NULL DEFAULT 0,
    DestinationType  TEXT    NOT NULL,
    DestinationValue TEXT    NOT NULL DEFAULT '',
    Description      TEXT    NOT NULL DEFAULT '',
    Enabled          INTEGER NOT NULL DEFAULT 1,
    UNIQUE (TrunkID, DID)
);
