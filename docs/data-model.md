# Data model

chisBallot holds any number of elections. Each election has its own contests, its own printed voting codes and its own ballots. Nothing connects a voting code to a ballot, and nothing connects one election's ballots to another's.

## Tables

```sql
CREATE TABLE ballot.elections (
    id                uuid         NOT NULL PRIMARY KEY,
    slug              varchar(60)  NOT NULL UNIQUE,    -- vote.chisballot.org/e/{slug}
    name              varchar(200) NOT NULL,
    status            smallint     NOT NULL,           -- 0 Draft, 1 Open, 2 Closed
    codes_issued      integer      NOT NULL DEFAULT 0, -- fixed when the slips are printed
    show_live_results boolean      NOT NULL DEFAULT false, -- set in Draft, frozen once Open
    shuffle_contests  boolean      NOT NULL DEFAULT false, -- each voter sees contests in their own random order
    shuffle_choices   boolean      NOT NULL DEFAULT false, -- each voter sees choices in their own random order
    is_demo           boolean      NOT NULL DEFAULT false  -- practice election; set at creation, never changed
);

-- One question on the ballot: an officer race or a yes/no motion.
CREATE TABLE ballot.contests (
    id             uuid         NOT NULL PRIMARY KEY,
    election_id    uuid         NOT NULL REFERENCES ballot.elections (id),
    title          varchar(200) NOT NULL,
    seats          integer      NOT NULL DEFAULT 1, -- winners to elect; "top 3" is seats = 3
    max_selections integer      NOT NULL DEFAULT 1, -- most picks one voter may make
    allow_write_in boolean      NOT NULL DEFAULT false, -- voters may type a name not on the list
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
    code_hash   bytea   NOT NULL,
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

-- Typed-in names, only for contests with allow_write_in. name_key is the
-- normalized form used for counting (see "Results and ties").
CREATE TABLE ballot.ballot_write_ins (
    ballot_id  uuid        NOT NULL REFERENCES ballot.ballots (id),
    contest_id uuid        NOT NULL REFERENCES ballot.contests (id),
    name       varchar(60) NOT NULL,
    name_key   varchar(60) NOT NULL,
    PRIMARY KEY (ballot_id, contest_id, name_key)
);
```

## Rules

### Casting a ballot is one transaction

1. `UPDATE ballot.voting_codes SET used = true WHERE election_id = @e AND code_hash = @h AND NOT used`, joined to an election whose `status` is Open. If no row changes, stop: the code is unknown, already used, or belongs to another election.
2. Insert the ballot, its selections and its write-ins. For each contest, choices plus write-ins must not exceed `MaxSelections`, and write-ins are refused unless the contest has `AllowWriteIn`.

### Random order (optional, off by default)

- With `ShuffleContests` and `ShuffleChoices`, each voter sees the questions
  and the options in an order of their own. People sitting near each other
  cannot copy a neighbor's screen, and a ballot does not tell you who
  voted early or in what rhythm.
- The shuffle happens in the voter's browser, using the browser's
  cryptographic random generator. The server never learns the order shown.
  The order is not stored, not logged and not derived from the voting code,
  so it can never link a ballot to a slip.
- `SortOrder` is the fixed order used on every published page. The ballot
  stores selections without any order, so it is the same however the voter
  saw the screen.
- A write-in box, when allowed, always stays last in its contest.

### Results and ties

- Each contest counts one vote per choice per ballot. Write-in names are
  counted by `NameKey`: the name trimmed, inner whitespace collapsed and
  case folded. No one can merge or edit write-ins by hand, so counts follow
  fixed rules and the chair cannot reshape them.
- Choices are ranked by votes. The top `Seats` are the winners.
- **A tie that decides who wins is a problem, never a coin flip.** If the
  last winning place and the first non-winning place have equal votes, the
  contest is marked **Tie: revote needed**. Choices clearly above the tie
  keep their seats; the tied choices and the number of seats still open are
  listed. Equal totals in a yes/no motion, or when nobody got a vote, are
  ties too.
- Equal totals among choices that are all inside the top `Seats` do not change
  who wins, so they are shown as equal ranks and are not a problem.
- The tie check is one pure function of the counts, covered by tests.
- A closed election cannot be reopened, so a revote is a **new election**
  containing only the unresolved contest, its tied choices and the open
  seats, with fresh slips. The admin site creates it in one step and the
  results page links the two.
- Write-ins carry a risk the other choices do not: a voter can type something
  distinctive to prove how they voted, and the full ballot list is public.
  So the name is limited to 60 characters of letters, spaces, hyphens,
  apostrophes and periods, and write-in names appear only once the election
  is Closed. Live results, when on, show write-ins as a single combined count.

### Demo elections (`IsDemo`, see `docs/demo.md`)

- Set when an admin creates the election and immutable afterwards.
- The only election type where the public site can ask for a code. That goes
  through one database procedure that refuses real elections, so a real
  election has no code-issuing endpoint.
- Admin can reset a demo (delete its ballots and codes); reset refuses real
  elections.

### Election lifecycle

| Status | Allowed |
| --- | --- |
| Draft | Edit contests and choices, generate codes, print slips |
| Open | Cast ballots only. Contests, choices and codes are frozen |
| Closed | Read only. Results, receipt list and audit numbers are published |

- **Turnout is always public**: codes used out of codes issued, updated near
  real time while the election is Open.

- **Results are hidden until the election is closed**, unless the chair
  turned on `ShowLiveResults` for that election while it was still Draft.
  With it on, running tallies (counts per choice, nothing else) are shown
  near real time while Open. With it off, a running tally can never hint at
  how the latest voter voted. The flag defaults to off and cannot change once
  the election is Open.
- The receipt list and the full anonymous ballot list are published only at
  Closed, whatever the flag says.
- The audit page (`/audit/{slug}`) shows the three audit counts while Open and
  everything else only at Closed. The published ballot list is ordered by
  receipt code, never by storage order.
- Near real time means the page polls the API every few seconds and the API
  caches its answer briefly. The voting site has no push channel, so the page asks again every few seconds.
- Closing an election deletes its unused codes after recording how many
  there were.
- A closed election cannot be reopened. Run a new one instead.

### Anonymity

- Codes are generated per election. A slip from one vote does nothing in
  another, and a member's ballots in different elections cannot be linked.
- `ballot.ballots` and `ballot.voting_codes` share no column except the election. Ballot
  ids are random so storage order does not reveal voting order.
- Request logs must never record the code or the ballot contents.

### Audit numbers published with every result

- Codes issued, codes used, ballots counted (used must equal counted)
- The receipt list, and every anonymous ballot, so anyone can recount

## Implementation

The database is PostgreSQL. The schema and the grants of the two database
roles (`chisballot_vote`, `chisballot_admin`) are the SQL scripts in
`src/ChisBallot.Data/Migrations/`. The sites reach ballots only through
functions: `ballot.cast_ballot`, `ballot.get_turnout`, `ballot.get_results`
(voting and admin site) and `ballot.close_election` (admin site). They run
with their owner's rights (`SECURITY DEFINER`), and execute rights on every
function are taken from `PUBLIC` and given to named roles only. Neither
role has any table-level right to read ballots, codes or tallies, so the
database itself refuses an early peek, whatever the application code does.
A trigger on `ballot.elections` enforces the lifecycle: Draft to Open to
Closed, Closed final, settings frozen once Open.

The migrator creates the two roles itself from passwords it is given, and
needs only a role that can create roles and own the database, not a
superuser.

The tie rule is `ResultsCalculator` in `src/ChisBallot.Contracts`. It is a
pure function of the counts, so the audit page can run the same rules in a
voter's browser. The admin site shows the result with the tie flagged; the
revote shortcut (a new election with only the unresolved choices and open
seats) is not built yet.

## Known limit

With a small board, the result itself can reveal votes: a unanimous outcome
tells everyone how each member voted. No ballot system, paper included,
avoids this. Live results make it worse: a tally that moves right after a
known member signs off points at that member's vote. The chair should turn
live results on only for votes where that does not matter.

## Addresses

| Address | What it is |
| --- | --- |
| [www.chisballot.org](https://www.chisballot.org) | Landing page on GitHub Pages. `chisballot.org` redirects here |
| [vote.chisballot.org](https://vote.chisballot.org) | Voting site. The root says a slip's QR code is needed to vote |
| [vote.chisballot.org/v/{slug}#{code}](https://vote.chisballot.org/v/{slug}#{code}) | A voter's ballot. The slug says which election the slip is for and is not secret. The code is in the URL fragment, which the browser never sends to a server, so it cannot reach request logs, the CDN or a referrer header |
| [vote.chisballot.org/e/{slug}](https://vote.chisballot.org/e/{slug}) | Election page: live turnout, live results if enabled, full results once Closed |
| [vote.chisballot.org/audit/{slug}](https://vote.chisballot.org/audit/{slug}) | Audit page: audit numbers, receipt list, every anonymous ballot, in-browser recount and the deployed commit. See `docs/audit-page.md` |
| [admin.chisballot.org](https://admin.chisballot.org) | Admin site |

## Two deployments

| | Voting site | Admin site |
| --- | --- | --- |
| Address | [vote.chisballot.org](https://vote.chisballot.org) | [admin.chisballot.org](https://admin.chisballot.org) |
| Sign-in | None. A slip's code is the only credential | Required on every page and API call; invited admins only |
| Database user | `chisballot_vote` | `chisballot_admin` |
| Can do | Cast a ballot, read published results | Create elections, contests and choices; generate codes; open and close |
| Cannot do | Anything else | Read or change ballots while an election is open; change a closed election |

The voting site ships no admin pages and exposes no admin endpoints, so
there is nothing on the public site for an attacker to probe. Database
permissions are enforced by the database itself, not only by application
code.
