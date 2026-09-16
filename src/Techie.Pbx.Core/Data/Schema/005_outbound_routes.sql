-- "Numbers that look like this go out over that trunk", tried in priority order (D45).
-- The trunk reference is a real foreign key: deleting a trunk that a route still points at fails,
-- and the repository turns that into a message rather than a crash.
CREATE TABLE OutboundRoutes (
    OutboundRouteID INTEGER PRIMARY KEY,
    Name            TEXT    NOT NULL UNIQUE,
    DialPattern     TEXT    NOT NULL,
    TrunkID         INTEGER NOT NULL REFERENCES Trunks(TrunkID),
    Priority        INTEGER NOT NULL DEFAULT 100,
    Enabled         INTEGER NOT NULL DEFAULT 1
);
