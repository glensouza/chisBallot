-- The published ballot list. Available only once the election is Closed; an
-- earlier call raises CB001 ("not available"), as get_results does. Rows are in
-- receipt-code order, which is random, and carry no time and no sequence number.
-- A ballot with no selections comes back as one row with null contest and
-- choice. Write-ins are listed with the name as typed (normalized for display).
CREATE FUNCTION ballot.get_ballots(p_election_id uuid)
RETURNS TABLE (receipt_code text, contest_id uuid, choice_id uuid, write_in text)
LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog, ballot AS $$
#variable_conflict use_column
DECLARE
    v_status smallint;
BEGIN
    SELECT e.status INTO v_status FROM ballot.elections e WHERE e.id = p_election_id;

    IF v_status IS NULL OR v_status <> 2 THEN
        RAISE EXCEPTION 'The ballot list is not available.' USING ERRCODE = 'CB001';
    END IF;

    RETURN QUERY
    SELECT r.receipt, r.contest, r.choice, r.write_in_name
    FROM (
        SELECT b.receipt_code::text AS receipt, s.contest_id AS contest, s.choice_id AS choice, NULL::text AS write_in_name
        FROM ballot.ballots b
        LEFT JOIN ballot.ballot_selections s ON s.ballot_id = b.id
        WHERE b.election_id = p_election_id

        UNION ALL

        SELECT b.receipt_code::text, w.contest_id, NULL::uuid, w.name::text
        FROM ballot.ballots b
        JOIN ballot.ballot_write_ins w ON w.ballot_id = b.id
        WHERE b.election_id = p_election_id
    ) r
    ORDER BY r.receipt, r.contest, r.choice, r.write_in_name;
END;
$$;

REVOKE ALL ON FUNCTION ballot.get_ballots(uuid) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION ballot.get_ballots(uuid) TO chisballot_vote;
