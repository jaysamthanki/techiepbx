-- Which vendor a phone is (piece 22c). Every phone the Polycom controller auto-adds is
-- "Polycom"; the Yealink controller writes "Yealink". A model-mismatch refusal (D78) only
-- ever compares within the same brand, so a MAC already known as one brand cannot be silently
-- taken over by a request that looks like the other (D88).
ALTER TABLE Phones ADD COLUMN Brand TEXT NOT NULL DEFAULT 'Polycom';
