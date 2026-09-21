-- A phone's registration becomes one of its keys (D121 amended, D123). Until now a phone had an
-- ExtensionID of its own *and* eight keys, so an eight-key handset with every key assigned showed
-- nine lines: the registration always took line key 1 on top of whatever the keys said. The
-- registration is now a key like any other — TargetType 'Line' — so what the Buttons tab shows is
-- what the handset has.
--
-- The kinds change with it: what was 'Extension' (a lamp on somebody else's extension) is now
-- 'Blf', and 'Line' is the extension this phone signs in as. Still plain TEXT with no CHECK, so
-- the reserved 'CallFlowControl' kind still needs no schema script (D35, D121).

-- 1. Every key that watched an extension was a lamp, and still is.
UPDATE PhoneButtons SET TargetType = 'Blf' WHERE TargetType = 'Extension';

-- 2. Make room at key 1 for the phones that have an extension to move down there. Two passes
--    because UNIQUE (PhoneID, Position) is checked row by row: moving everything out to
--    1001..1008 first cannot collide with 1..8, and moving it back to 2..8 cannot collide with
--    what is still out at 100x.
--
--    A phone with all eight keys assigned loses its last key, because there is nowhere else for
--    it to go: eight keys with a line on one of them is seven lamps, and that is the fact about
--    the handset this whole change is about.
UPDATE PhoneButtons SET Position = Position + 1000
 WHERE PhoneID IN (SELECT PhoneID FROM Phones WHERE ExtensionID IS NOT NULL);

DELETE FROM PhoneButtons WHERE Position > 1007;

UPDATE PhoneButtons SET Position = Position - 999 WHERE Position > 1000;

-- 3. The line itself, at key 1. It stores the extension's *number*, not its ID, because a key is
--    a reference of the same shape as every other one (D121): renaming the extension relabels the
--    key, and deleting it leaves a key the renderers drop rather than a dangling row.
--
--    Nothing stopped two phones sharing an ExtensionID before, and this does not decide which of
--    them should win: both get the line, and the first save of either is refused with a message
--    naming the other. That is an admin's decision, not a migration's.
INSERT INTO PhoneButtons (PhoneID, Position, TargetType, TargetValue)
SELECT p.PhoneID, 1, 'Line', e.Number
  FROM Phones p
  JOIN Extensions e ON e.ExtensionID = p.ExtensionID
 WHERE p.ExtensionID IS NOT NULL;

-- 4. And the column goes: a phone's registration is now derived from its keys, so keeping it
--    would be a second answer to the same question. Dropped in place, as 016 dropped
--    Extensions.MaxContacts — rebuilding Phones would cascade-delete the keys just inserted.
ALTER TABLE Phones DROP COLUMN ExtensionID;
