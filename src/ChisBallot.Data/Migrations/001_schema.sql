-- chisBallot schema for PostgreSQL. See docs/data-model.md for what each table
-- is for. Applied by the migrator as the database owner.

CREATE SCHEMA IF NOT EXISTS ballot;

CREATE TABLE ballot.elections (
    id                uuid         NOT NULL PRIMARY KEY,
    slug              varchar(60)  NOT NULL UNIQUE,
    name              varchar(200) NOT NULL,
    status            smallint     NOT NULL CHECK (status IN (0, 1, 2)), -- 0 Draft, 1 Open, 2 Closed
    codes_issued      integer      NOT NULL DEFAULT 0,
    show_live_results boolean      NOT NULL DEFAULT false,
    shuffle_contests  boolean      NOT NULL DEFAULT false,
    shuffle_choices   boolean      NOT NULL DEFAULT false,
    is_demo           boolean      NOT NULL DEFAULT false
);

CREATE TABLE ballot.contests (
    id             uuid         NOT NULL PRIMARY KEY,
    election_id    uuid         NOT NULL REFERENCES ballot.elections (id),
    title          varchar(200) NOT NULL,
    seats          integer      NOT NULL DEFAULT 1 CHECK (seats >= 1),
    max_selections integer      NOT NULL DEFAULT 1 CHECK (max_selections >= 1),
    allow_write_in boolean      NOT NULL DEFAULT false,
    sort_order     integer      NOT NULL
);

CREATE TABLE ballot.choices (
    id         uuid         NOT NULL PRIMARY KEY,
    contest_id uuid         NOT NULL REFERENCES ballot.contests (id),
    label      varchar(200) NOT NULL,
    sort_order integer      NOT NULL
);

-- One row per printed slip. Only a hash of the code is stored.
CREATE TABLE ballot.voting_codes (
    election_id uuid    NOT NULL REFERENCES ballot.elections (id),
    code_hash   bytea   NOT NULL CHECK (octet_length(code_hash) = 32),
    used        boolean NOT NULL DEFAULT false,
    PRIMARY KEY (election_id, code_hash)
);

-- No voting code, no voter, no timestamp, no sequence number.
CREATE TABLE ballot.ballots (
    id           uuid     NOT NULL PRIMARY KEY, -- random, never sequential
    election_id  uuid     NOT NULL REFERENCES ballot.elections (id),
    receipt_code char(11) NOT NULL,
    UNIQUE (election_id, receipt_code)
);

CREATE TABLE ballot.ballot_selections (
    ballot_id  uuid NOT NULL REFERENCES ballot.ballots (id),
    contest_id uuid NOT NULL REFERENCES ballot.contests (id),
    choice_id  uuid NOT NULL REFERENCES ballot.choices (id),
    PRIMARY KEY (ballot_id, contest_id, choice_id)
);

CREATE TABLE ballot.ballot_write_ins (
    ballot_id  uuid        NOT NULL REFERENCES ballot.ballots (id),
    contest_id uuid        NOT NULL REFERENCES ballot.contests (id),
    name       varchar(60) NOT NULL,
    name_key   varchar(60) NOT NULL,
    PRIMARY KEY (ballot_id, contest_id, name_key)
);

-- Election lifecycle: Draft -> Open -> Closed, and Closed is final. Once an
-- election is Open, the settings that affect how it is run are frozen.
-- Error code CB002 is what the application treats as "change not allowed".
CREATE FUNCTION ballot.elections_lifecycle() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
    IF OLD.status = 2
       OR (OLD.status = 0 AND NEW.status NOT IN (0, 1))
       OR (OLD.status = 1 AND NEW.status NOT IN (1, 2))
       OR NEW.is_demo <> OLD.is_demo
       OR (OLD.status <> 0 AND (   NEW.slug <> OLD.slug
                                OR NEW.codes_issued <> OLD.codes_issued
                                OR NEW.show_live_results <> OLD.show_live_results
                                OR NEW.shuffle_contests <> OLD.shuffle_contests
                                OR NEW.shuffle_choices <> OLD.shuffle_choices))
    THEN
        RAISE EXCEPTION 'This change to the election is not allowed.' USING ERRCODE = 'CB002';
    END IF;
    RETURN NEW;
END;
$$;

CREATE TRIGGER tr_elections_lifecycle
    BEFORE UPDATE ON ballot.elections
    FOR EACH ROW EXECUTE FUNCTION ballot.elections_lifecycle();

-- Casting a ballot is one function call, so one transaction: consume the
-- code, insert the ballot. Returns 0 cast, 1 code refused (unknown, used,
-- wrong election or not open), 2 selections refused. Nothing records which
-- code produced which ballot. The function runs with its owner's rights, so
-- the voting role needs no access to the ballot tables.
CREATE FUNCTION ballot.cast_ballot(
    p_election_id         uuid,
    p_code_hash           bytea,
    p_receipt_code        text,
    p_selection_contests  uuid[],
    p_selection_choices   uuid[],
    p_writein_contests    uuid[],
    p_writein_names       text[],
    p_writein_keys        text[])
RETURNS integer
LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog, ballot AS $$
DECLARE
    v_ballot uuid := gen_random_uuid();
BEGIN
    IF EXISTS (
            SELECT 1
            FROM unnest(p_selection_contests, p_selection_choices) AS s(contest_id, choice_id)
            LEFT JOIN ballot.choices ch ON ch.id = s.choice_id AND ch.contest_id = s.contest_id
            LEFT JOIN ballot.contests c ON c.id = s.contest_id AND c.election_id = p_election_id
            WHERE ch.id IS NULL OR c.id IS NULL)
        OR EXISTS (
            SELECT 1
            FROM unnest(p_writein_contests) AS w(contest_id)
            LEFT JOIN ballot.contests c ON c.id = w.contest_id AND c.election_id = p_election_id AND c.allow_write_in
            WHERE c.id IS NULL)
        OR EXISTS (
            SELECT 1
            FROM ballot.contests c
            WHERE c.election_id = p_election_id
              AND (SELECT count(*) FROM unnest(p_selection_contests) AS s(contest_id) WHERE s.contest_id = c.id)
                + (SELECT count(*) FROM unnest(p_writein_contests) AS w(contest_id) WHERE w.contest_id = c.id) > c.max_selections)
    THEN
        RETURN 2;
    END IF;

    UPDATE ballot.voting_codes vc
    SET used = true
    FROM ballot.elections e
    WHERE e.id = vc.election_id
      AND vc.election_id = p_election_id
      AND vc.code_hash = p_code_hash
      AND NOT vc.used
      AND e.status = 1;

    IF NOT FOUND THEN
        RETURN 1;
    END IF;

    INSERT INTO ballot.ballots (id, election_id, receipt_code)
    VALUES (v_ballot, p_election_id, p_receipt_code);

    INSERT INTO ballot.ballot_selections (ballot_id, contest_id, choice_id)
    SELECT v_ballot, s.contest_id, s.choice_id
    FROM unnest(p_selection_contests, p_selection_choices) AS s(contest_id, choice_id);

    INSERT INTO ballot.ballot_write_ins (ballot_id, contest_id, name, name_key)
    SELECT v_ballot, w.contest_id, w.name, w.name_key
    FROM unnest(p_writein_contests, p_writein_names, p_writein_keys) AS w(contest_id, name, name_key);

    RETURN 0;
END;
$$;

-- Turnout is always public: how many slips were used, never what was voted.
CREATE FUNCTION ballot.get_turnout(p_election_id uuid)
RETURNS TABLE (codes_issued integer, codes_used integer, ballots_counted integer)
LANGUAGE sql SECURITY DEFINER SET search_path = pg_catalog, ballot AS $$
    SELECT e.codes_issued,
           (SELECT count(*)::integer FROM ballot.voting_codes vc WHERE vc.election_id = e.id AND vc.used),
           (SELECT count(*)::integer FROM ballot.ballots b WHERE b.election_id = e.id)
    FROM ballot.elections e
    WHERE e.id = p_election_id;
$$;

-- Results are readable only once the election is Closed, or while Open when
-- the election turned on live results. Live results are per-choice counts;
-- write-ins appear as one combined count until Closed. Error code CB001
-- means "not available".
CREATE FUNCTION ballot.get_results(p_election_id uuid)
RETURNS TABLE (contest_id uuid, choice_id uuid, write_in text, votes integer)
LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog, ballot AS $$
#variable_conflict use_column
DECLARE
    v_status smallint;
    v_live   boolean;
BEGIN
    SELECT e.status, e.show_live_results INTO v_status, v_live
    FROM ballot.elections e WHERE e.id = p_election_id;

    IF v_status IS NULL OR NOT (v_status = 2 OR (v_status = 1 AND v_live)) THEN
        RAISE EXCEPTION 'Results are not available.' USING ERRCODE = 'CB001';
    END IF;

    RETURN QUERY
    SELECT r.contest_id, r.choice_id, r.write_in, r.votes
    FROM (
        SELECT ch.contest_id AS contest_id,
               ch.id AS choice_id,
               NULL::text AS write_in,
               count(bs.ballot_id)::integer AS votes
        FROM ballot.choices ch
        JOIN ballot.contests c ON c.id = ch.contest_id
        LEFT JOIN ballot.ballot_selections bs ON bs.choice_id = ch.id
        WHERE c.election_id = p_election_id
        GROUP BY ch.contest_id, ch.id

        UNION ALL

        SELECT w.contest_id,
               NULL::uuid,
               CASE WHEN v_status = 2 THEN w.name_key::text END,
               count(*)::integer
        FROM ballot.ballot_write_ins w
        JOIN ballot.contests c ON c.id = w.contest_id
        WHERE c.election_id = p_election_id
        GROUP BY w.contest_id, CASE WHEN v_status = 2 THEN w.name_key::text END
    ) r
    ORDER BY r.contest_id, r.votes DESC, r.choice_id;
END;
$$;

-- Closing records the final state and deletes the codes nobody used. Error
-- code CB003 means "only an open election can be closed".
CREATE FUNCTION ballot.close_election(p_election_id uuid)
RETURNS void
LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog, ballot AS $$
BEGIN
    UPDATE ballot.elections SET status = 2 WHERE id = p_election_id AND status = 1;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'Only an open election can be closed.' USING ERRCODE = 'CB003';
    END IF;

    DELETE FROM ballot.voting_codes WHERE election_id = p_election_id AND NOT used;
END;
$$;
