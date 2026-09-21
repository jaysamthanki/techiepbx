-- The assignable keys on a desk phone (D121). Eight of them today, one row per key that has
-- something on it; a key nobody has assigned is simply absent rather than a row saying so, the
-- same way an IVR's unused digit is (D59).
--
-- TargetType is a plain TEXT column with no CHECK on it, exactly as InboundRoutes.DestinationType
-- is (D35): the two kinds today are 'Extension' and 'ParkingSlot', and call flow control is meant
-- to become a third. A kind is added by teaching the model and the renderers about it, never by a
-- schema change.
--
-- TargetValue is what the kind points at: an extension number for 'Extension', a slot number for
-- 'ParkingSlot'. A reference rather than a copy, as everywhere else here (D35) — the key follows
-- the extension when it is renamed, and goes dark when it is deleted.
--
-- ON DELETE CASCADE because a key has no life of its own once the phone it is on is gone, and
-- UNIQUE (PhoneID, Position) because one key is one place on one phone.
CREATE TABLE PhoneButtons (
    PhoneButtonID INTEGER PRIMARY KEY,
    PhoneID       INTEGER NOT NULL REFERENCES Phones(PhoneID) ON DELETE CASCADE,
    Position      INTEGER NOT NULL,
    TargetType    TEXT    NOT NULL DEFAULT '',
    TargetValue   TEXT    NOT NULL DEFAULT '',
    UNIQUE (PhoneID, Position)
);
