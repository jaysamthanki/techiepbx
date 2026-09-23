-- Call flow control (F9): the "we are closing early, flip us to night mode" switch. One row is one
-- switch with two destinations — normal when it is off, override when it is on — and the feature
-- code a phone dials to flip it. Each destination is the two fields D35 settled on.
--
-- FeatureCode is what a phone dials to toggle the switch, a star and two or three digits, and it
-- is also what a destination points at, the way a time condition's play extension is (D63).
--
-- There is deliberately no State column. Whether a switch is on lives in Asterisk's own database
-- (astdb, TNPBX/CFC/<CallFlowControlID>), written by the dialplan when a phone dials the code, so a
-- flip takes effect on the next call with no apply and no involvement from this app. The rendered
-- dialplan reads it at call time and is the same file whichever way the switch is set.
CREATE TABLE IF NOT EXISTS CallFlowControls (
    CallFlowControlID        INTEGER PRIMARY KEY,
    Name                     TEXT    NOT NULL UNIQUE,
    FeatureCode              TEXT    NOT NULL UNIQUE,
    NormalDestinationType    TEXT    NOT NULL DEFAULT 'Hangup',
    NormalDestinationValue   TEXT    NOT NULL DEFAULT '',
    OverrideDestinationType  TEXT    NOT NULL DEFAULT 'Hangup',
    OverrideDestinationValue TEXT    NOT NULL DEFAULT ''
);
