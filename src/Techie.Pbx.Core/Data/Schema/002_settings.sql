-- Key/value settings, including the AMI secret. The database file is 0600 and already holds
-- SIP secrets, so it is the right place for it: no credentials in appsettings.json or the repo.
CREATE TABLE Settings (
    SettingID INTEGER PRIMARY KEY,
    "Key"     TEXT    NOT NULL UNIQUE,
    Value     TEXT    NOT NULL
);
