-- "Open hours go here, closed hours go there, holidays go somewhere else" (F8). One row is the
-- whole answer for one number: three destinations, in one form, with no time-group entity to
-- reference (D63). Humans think in cases, not in referenced tables of time ranges.
--
-- PlayExtension is optional and works exactly as an announcement's and an IVR's do (D57, D59):
-- when it is set the condition gets a number that can be dialled to see which way it decides, and
-- that number is what a destination points at. Without it there is no dialplan entry and it cannot
-- be a destination.
--
-- Each of the three destinations is the two fields D35 settled on.
CREATE TABLE TimeConditions (
    TimeConditionID         INTEGER PRIMARY KEY,
    Name                    TEXT    NOT NULL UNIQUE,
    Description             TEXT    NOT NULL DEFAULT '',
    PlayExtension           TEXT    NOT NULL DEFAULT '',
    OpenDestinationType     TEXT    NOT NULL DEFAULT 'Hangup',
    OpenDestinationValue    TEXT    NOT NULL DEFAULT '',
    ClosedDestinationType   TEXT    NOT NULL DEFAULT 'Hangup',
    ClosedDestinationValue  TEXT    NOT NULL DEFAULT '',
    HolidayDestinationType  TEXT    NOT NULL DEFAULT 'Hangup',
    HolidayDestinationValue TEXT    NOT NULL DEFAULT '',
    Enabled                 INTEGER NOT NULL DEFAULT 1
);

-- When the site is open, and which dates are holidays. Two kinds of row in one table because they
-- are edited in one form and read together; Kind says which fields mean anything (D63).
--
-- Kind 0 (weekly) uses DaysMask (a bit per weekday, Monday = 1) with StartTime and EndTime as
-- 'HH:MM'. Kind 1 (holiday) uses HolidayDate as 'YYYY-MM-DD' and may carry a destination of its
-- own; empty means the condition's HolidayDestination is used. The year in HolidayDate is stored
-- but never matched: GotoIfTime has no year field, so a holiday comes round annually (D64).
--
-- ON DELETE CASCADE because a rule has no life of its own: deleting the condition deletes it.
CREATE TABLE TimeConditionRules (
    TimeConditionRuleID INTEGER PRIMARY KEY,
    TimeConditionID     INTEGER NOT NULL REFERENCES TimeConditions(TimeConditionID) ON DELETE CASCADE,
    Kind                INTEGER NOT NULL DEFAULT 0,
    DaysMask            INTEGER NOT NULL DEFAULT 0,
    StartTime           TEXT    NOT NULL DEFAULT '',
    EndTime             TEXT    NOT NULL DEFAULT '',
    HolidayDate         TEXT    NOT NULL DEFAULT '',
    DestinationType     TEXT    NOT NULL DEFAULT '',
    DestinationValue    TEXT    NOT NULL DEFAULT '',
    SortOrder           INTEGER NOT NULL DEFAULT 0
);
