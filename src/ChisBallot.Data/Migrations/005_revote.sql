-- A revote is a new election that records which closed election it follows. Nullable,
-- so every release before this one still works with the column there. At most one
-- revote per election: a revote that ties again is followed by a revote of its own.
ALTER TABLE ballot.elections ADD COLUMN revote_of uuid NULL REFERENCES ballot.elections (id);
CREATE UNIQUE INDEX elections_one_revote ON ballot.elections (revote_of) WHERE revote_of IS NOT NULL;
