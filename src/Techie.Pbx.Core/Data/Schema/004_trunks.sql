-- A connection to a SIP provider. One row becomes a PJSIP endpoint, auth and aor, plus a
-- registration when we register and an identify when the provider's addresses are known.
-- Name starts with a letter so its section names cannot collide with an extension's (D37).
CREATE TABLE Trunks (
    TrunkID        INTEGER PRIMARY KEY,
    Name           TEXT    NOT NULL UNIQUE,
    ServerHost     TEXT    NOT NULL,
    ServerPort     INTEGER NOT NULL DEFAULT 5060,
    Username       TEXT    NOT NULL DEFAULT '',
    AuthUsername   TEXT    NOT NULL DEFAULT '',
    Password       TEXT    NOT NULL DEFAULT '',
    Register       INTEGER NOT NULL DEFAULT 1,
    CallerIDName   TEXT    NOT NULL DEFAULT '',
    CallerIDNumber TEXT    NOT NULL DEFAULT '',
    Codecs         TEXT    NOT NULL DEFAULT 'ulaw,alaw',
    MatchAddresses TEXT    NOT NULL DEFAULT '',
    Enabled        INTEGER NOT NULL DEFAULT 1
);
