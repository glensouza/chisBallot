-- The demo votes, made and reset by the database itself, so the migrator (on every deploy)
-- and the admin site (a button) do the same thing and the admin role needs no right to
-- delete anything directly. Both functions touch only demos: the two named below and any
-- revote made from them. A real election is never read or changed here.
--
--   ballot.ensure_demos()   makes `demo` (open, empty) and `demo-tie` (closed, a tie) if missing
--   ballot.reset_demos()    deletes both, and any revote of them, and makes them again

CREATE FUNCTION ballot.ensure_demos()
RETURNS void
LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog, ballot AS $$
DECLARE
    v_election uuid;
    v_contest  uuid;
    v_choice   uuid;
    v_ballot   uuid;
    v_alphabet text := '0123456789ABCDEFGHJKMNPQRSTVWXYZ';
    v_receipt  text;
    v_names    text[] := ARRAY['Ada', 'Ben', 'Cy', 'Dee'];
    v_votes    int[]  := ARRAY[6, 3, 3, 1];
    v_trips    text[] := ARRAY['Science museum', 'Zoo', 'Aquarium', 'State capitol', 'Botanical garden', 'Planetarium'];
    v_label    text;
    v_index    int;
    v_vote     int;
BEGIN
    IF NOT EXISTS (SELECT 1 FROM ballot.elections WHERE slug = 'demo') THEN
        v_election := gen_random_uuid();
        INSERT INTO ballot.elections (id, slug, name, status, codes_issued, show_live_results, shuffle_contests, shuffle_choices, is_demo)
        VALUES (v_election, 'demo', 'Demo vote', 0, 0, true, true, true, true);

        v_contest := gen_random_uuid();
        INSERT INTO ballot.contests (id, election_id, title, seats, max_selections, allow_write_in, sort_order)
        VALUES (v_contest, v_election, 'Pick three field trip spots', 3, 3, true, 1);
        v_index := 0;
        FOREACH v_label IN ARRAY v_trips LOOP
            INSERT INTO ballot.choices (id, contest_id, label, sort_order) VALUES (gen_random_uuid(), v_contest, v_label, v_index);
            v_index := v_index + 1;
        END LOOP;

        v_contest := gen_random_uuid();
        INSERT INTO ballot.contests (id, election_id, title, seats, max_selections, allow_write_in, sort_order)
        VALUES (v_contest, v_election, 'Should lunch be longer?', 1, 1, false, 2);
        INSERT INTO ballot.choices (id, contest_id, label, sort_order) VALUES (gen_random_uuid(), v_contest, 'Yes', 0);
        INSERT INTO ballot.choices (id, contest_id, label, sort_order) VALUES (gen_random_uuid(), v_contest, 'No', 1);

        -- Opening needs no slips: the demo page hands them out one at a time.
        UPDATE ballot.elections SET status = 1 WHERE id = v_election;
    END IF;

    IF NOT EXISTS (SELECT 1 FROM ballot.elections WHERE slug = 'demo-tie') THEN
        v_election := gen_random_uuid();
        INSERT INTO ballot.elections (id, slug, name, status, codes_issued, show_live_results, shuffle_contests, shuffle_choices, is_demo)
        VALUES (v_election, 'demo-tie', 'Demo vote: a tie', 0, 13, false, false, false, true);

        -- Two seats. Ada wins one clearly; Ben and Cy tie for the other.
        v_contest := gen_random_uuid();
        INSERT INTO ballot.contests (id, election_id, title, seats, max_selections, allow_write_in, sort_order)
        VALUES (v_contest, v_election, 'Two student representatives', 2, 1, false, 1);

        FOR v_index IN 1..array_length(v_names, 1) LOOP
            v_choice := gen_random_uuid();
            INSERT INTO ballot.choices (id, contest_id, label, sort_order) VALUES (v_choice, v_contest, v_names[v_index], v_index - 1);

            FOR v_vote IN 1..v_votes[v_index] LOOP
                v_ballot := gen_random_uuid();
                v_receipt := '';
                FOR i IN 1..9 LOOP
                    v_receipt := v_receipt || substr(v_alphabet, 1 + floor(random() * 32)::int, 1);
                    IF i IN (3, 6) THEN
                        v_receipt := v_receipt || '-';
                    END IF;
                END LOOP;
                INSERT INTO ballot.ballots (id, election_id, receipt_code) VALUES (v_ballot, v_election, v_receipt);
                INSERT INTO ballot.ballot_selections (ballot_id, contest_id, choice_id) VALUES (v_ballot, v_contest, v_choice);
                INSERT INTO ballot.voting_codes (election_id, code_hash, used)
                VALUES (v_election, decode(md5(random()::text || clock_timestamp()::text) || md5(random()::text), 'hex'), true);
            END LOOP;
        END LOOP;

        UPDATE ballot.elections SET status = 1 WHERE id = v_election;
        PERFORM ballot.close_election(v_election);
    END IF;
END;
$$;

CREATE FUNCTION ballot.reset_demos()
RETURNS void
LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog, ballot AS $$
DECLARE
    v_ids uuid[];
BEGIN
    -- The two demos and any revote made from them (a revote of a demo is a demo too).
    SELECT array_agg(d.id) INTO v_ids
    FROM (
        WITH RECURSIVE d AS (
            SELECT e.id FROM ballot.elections e WHERE e.is_demo AND e.slug IN ('demo', 'demo-tie')
            UNION
            SELECT e.id FROM ballot.elections e JOIN d ON e.revote_of = d.id WHERE e.is_demo
        )
        SELECT id FROM d
    ) d;

    IF v_ids IS NOT NULL THEN
        DELETE FROM ballot.ballot_selections WHERE ballot_id IN (SELECT b.id FROM ballot.ballots b WHERE b.election_id = ANY (v_ids));
        DELETE FROM ballot.ballot_write_ins WHERE ballot_id IN (SELECT b.id FROM ballot.ballots b WHERE b.election_id = ANY (v_ids));
        DELETE FROM ballot.ballots WHERE election_id = ANY (v_ids);
        DELETE FROM ballot.voting_codes WHERE election_id = ANY (v_ids);
        DELETE FROM ballot.choices WHERE contest_id IN (SELECT c.id FROM ballot.contests c WHERE c.election_id = ANY (v_ids));
        DELETE FROM ballot.contests WHERE election_id = ANY (v_ids);
        DELETE FROM ballot.elections WHERE id = ANY (v_ids);
    END IF;

    PERFORM ballot.ensure_demos();
END;
$$;

REVOKE ALL ON FUNCTION ballot.ensure_demos() FROM PUBLIC;
REVOKE ALL ON FUNCTION ballot.reset_demos() FROM PUBLIC;
GRANT EXECUTE ON FUNCTION ballot.ensure_demos() TO chisballot_admin;
GRANT EXECUTE ON FUNCTION ballot.reset_demos() TO chisballot_admin;
