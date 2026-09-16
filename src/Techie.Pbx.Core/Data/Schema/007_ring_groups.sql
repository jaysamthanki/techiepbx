-- A number that rings several extensions (F3). Members are an ordered, comma separated list of
-- extension numbers in one column rather than a table of their own (D53): the order is the whole
-- point for Hunt, and the same shape is already used for a trunk's codecs and match addresses.
--
-- The no-answer destination is the two fields D35 settled on.
CREATE TABLE RingGroups (
    RingGroupID      INTEGER PRIMARY KEY,
    Number           TEXT    NOT NULL UNIQUE,
    Name             TEXT    NOT NULL,
    Strategy         TEXT    NOT NULL DEFAULT 'All',
    Members          TEXT    NOT NULL DEFAULT '',
    RingSeconds      INTEGER NOT NULL DEFAULT 20,
    CallerIDPrefix   TEXT    NOT NULL DEFAULT '',
    DestinationType  TEXT    NOT NULL DEFAULT 'Hangup',
    DestinationValue TEXT    NOT NULL DEFAULT '',
    Enabled          INTEGER NOT NULL DEFAULT 1
);
