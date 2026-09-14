CREATE TABLE Extensions (
    ExtensionID INTEGER PRIMARY KEY,
    Number      TEXT    NOT NULL UNIQUE,
    Name        TEXT    NOT NULL,
    Secret      TEXT    NOT NULL,
    Enabled     INTEGER NOT NULL DEFAULT 1
);
