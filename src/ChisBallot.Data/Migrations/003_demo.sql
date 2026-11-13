-- Demo mode. A demo is an ordinary election with is_demo set when it is made.
--
-- On a real election no slip can be made from the public site; the voting role
-- can only call this one function, and it refuses anything that is not an open
-- demo, and stops at a fixed number of slips.
CREATE FUNCTION ballot.issue_demo_code(p_election_id uuid, p_code_hash bytea)
RETURNS boolean
LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog, ballot AS $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM ballot.elections WHERE id = p_election_id AND is_demo AND status = 1) THEN
        RETURN false;
    END IF;

    IF (SELECT count(*) FROM ballot.voting_codes WHERE election_id = p_election_id) >= 300 THEN
        RETURN false;
    END IF;

    INSERT INTO ballot.voting_codes (election_id, code_hash) VALUES (p_election_id, p_code_hash);
    RETURN true;
END;
$$;

-- Demo slips are made after the election opens, so the number issued is the
-- larger of the number recorded at opening and the slips that exist.
CREATE OR REPLACE FUNCTION ballot.get_turnout(p_election_id uuid)
RETURNS TABLE (codes_issued integer, codes_used integer, ballots_counted integer)
LANGUAGE sql SECURITY DEFINER SET search_path = pg_catalog, ballot AS $$
    SELECT GREATEST(e.codes_issued, (SELECT count(*)::integer FROM ballot.voting_codes vc WHERE vc.election_id = e.id)),
           (SELECT count(*)::integer FROM ballot.voting_codes vc WHERE vc.election_id = e.id AND vc.used),
           (SELECT count(*)::integer FROM ballot.ballots b WHERE b.election_id = e.id)
    FROM ballot.elections e
    WHERE e.id = p_election_id;
$$;

REVOKE ALL ON FUNCTION ballot.issue_demo_code(uuid, bytea) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION ballot.issue_demo_code(uuid, bytea) TO chisballot_vote;
