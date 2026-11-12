-- What the two database roles may do. The migrator creates the roles
-- (chisballot_vote and chisballot_admin) with passwords from the deployment
-- before this script runs; passwords are never stored in this file.
--
--   chisballot_vote   used by the voting site. Can read what a ballot page
--                     needs and call the functions below. It has no right to
--                     read or write ballots, codes or tallies directly, so
--                     results cannot be read early.
--   chisballot_admin  used by the admin site. Can manage elections, contests,
--                     choices and codes, and close an election. It has no
--                     right to read ballots at all.
--
-- Functions are executable by everyone by default in PostgreSQL, so that is
-- taken away first.

REVOKE ALL ON SCHEMA ballot FROM PUBLIC;
REVOKE ALL ON ALL TABLES IN SCHEMA ballot FROM PUBLIC;
REVOKE ALL ON ALL FUNCTIONS IN SCHEMA ballot FROM PUBLIC;

GRANT USAGE ON SCHEMA ballot TO chisballot_vote, chisballot_admin;

GRANT SELECT ON ballot.elections, ballot.contests, ballot.choices TO chisballot_vote;
GRANT EXECUTE ON FUNCTION ballot.cast_ballot(uuid, bytea, text, uuid[], uuid[], uuid[], text[], text[]) TO chisballot_vote;
GRANT EXECUTE ON FUNCTION ballot.get_turnout(uuid) TO chisballot_vote;
GRANT EXECUTE ON FUNCTION ballot.get_results(uuid) TO chisballot_vote;

GRANT SELECT, INSERT, UPDATE ON ballot.elections TO chisballot_admin;
GRANT SELECT, INSERT, UPDATE, DELETE ON ballot.contests TO chisballot_admin;
GRANT SELECT, INSERT, UPDATE, DELETE ON ballot.choices TO chisballot_admin;
GRANT INSERT ON ballot.voting_codes TO chisballot_admin;
GRANT EXECUTE ON FUNCTION ballot.get_turnout(uuid) TO chisballot_admin;
GRANT EXECUTE ON FUNCTION ballot.get_results(uuid) TO chisballot_admin;
GRANT EXECUTE ON FUNCTION ballot.close_election(uuid) TO chisballot_admin;
