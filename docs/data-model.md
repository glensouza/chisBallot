# Data model

chisBallot holds any number of elections. Each election has its own
contests, its own printed voting codes and its own ballots. Nothing connects
a voting code to a ballot, and nothing connects one election's ballots to
another's.

## Tables

```sql
CREATE TABLE Elections (
    Id          uniqueidentifier NOT NULL PRIMARY KEY,
    Slug        nvarchar(60)     NOT NULL UNIQUE,   -- vote.chisballot.org/e/{slug}
    Name        nvarchar(200)    NOT NULL,
    Status      tinyint          NOT NULL,          -- 0 Draft, 1 Open, 2 Closed
    CodesIssued int              NOT NULL DEFAULT 0,-- fixed when the slips are printed
    ShowLiveResults bit          NOT NULL DEFAULT 0,-- set in Draft, frozen once Open
    ShuffleContests bit          NOT NULL DEFAULT 0,-- each voter sees contests in their own random order
    ShuffleChoices  bit          NOT NULL DEFAULT 0,-- each voter sees choices in their own random order
    IsDemo          bit          NOT NULL DEFAULT 0 -- practice election; set at creation, never changed
);

-- One question on the ballot: an officer race or a yes/no motion.
CREATE TABLE Contests (
    Id            uniqueidentifier NOT NULL PRIMARY KEY,
    ElectionId    uniqueidentifier NOT NULL REFERENCES Elections (Id),
    Title         nvarchar(200)    NOT NULL,
    Seats         int              NOT NULL DEFAULT 1, -- winners to elect; "top 3" is Seats = 3
    MaxSelections int              NOT NULL DEFAULT 1, -- most picks one voter may make
    AllowWriteIn  bit              NOT NULL DEFAULT 0, -- voters may type a name not on the list
    SortOrder     int              NOT NULL
);

CREATE TABLE Choices (
    Id        uniqueidentifier NOT NULL PRIMARY KEY,
    ContestId uniqueidentifier NOT NULL REFERENCES Contests (Id),
    Label     nvarchar(200)    NOT NULL,
    SortOrder int              NOT NULL
);

-- One row per printed slip. Only a hash of the code is stored.
CREATE TABLE VotingCodes (
    ElectionId uniqueidentifier NOT NULL REFERENCES Elections (Id),
    CodeHash   binary(32)       NOT NULL,
    Used       bit              NOT NULL DEFAULT 0,
    PRIMARY KEY (ElectionId, CodeHash)
);

-- No voting code, no voter, no timestamp, no sequence number.
CREATE TABLE Ballots (
    Id          uniqueidentifier NOT NULL PRIMARY KEY, -- random, never sequential
    ElectionId  uniqueidentifier NOT NULL REFERENCES Elections (Id),
    ReceiptCode char(11)         NOT NULL,
    UNIQUE (ElectionId, ReceiptCode)
);

CREATE TABLE BallotSelections (
    BallotId  uniqueidentifier NOT NULL REFERENCES Ballots (Id),
    ContestId uniqueidentifier NOT NULL REFERENCES Contests (Id),
    ChoiceId  uniqueidentifier NOT NULL REFERENCES Choices (Id),
    PRIMARY KEY (BallotId, ContestId, ChoiceId)
);

-- Typed-in names, only for contests with AllowWriteIn. NameKey is the
-- normalized form used for counting (see "Results and ties").
CREATE TABLE BallotWriteIns (
    BallotId  uniqueidentifier NOT NULL REFERENCES Ballots (Id),
    ContestId uniqueidentifier NOT NULL REFERENCES Contests (Id),
    Name      nvarchar(60)     NOT NULL,
    NameKey   nvarchar(60)     NOT NULL,
    PRIMARY KEY (BallotId, ContestId, NameKey)
);
```

## Rules

**Casting a ballot** is one transaction:

1. `UPDATE VotingCodes SET Used = 1 WHERE ElectionId = @e AND CodeHash = @h AND Used = 0`,
   joined to an election whose `Status` is Open. If no row changes, stop:
   the code is unknown, already used, or belongs to another election.
2. Insert the ballot, its selections and its write-ins. For each contest,
   choices plus write-ins must not exceed `MaxSelections`, and write-ins are
   refused unless the contest has `AllowWriteIn`.

**Random order (optional, off by default)**

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

**Results and ties**

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

**Demo elections** (`IsDemo`, see `docs/demo.md`)

- Set when an admin creates the election and immutable afterwards.
- The only election type where the public site can ask for a code. That goes
  through one database procedure that refuses real elections, so a real
  election has no code-issuing endpoint.
- Admin can reset a demo (delete its ballots and codes); reset refuses real
  elections.

**Election lifecycle**

| Status | Allowed |
|---|---|
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
  caches its answer briefly. Static Web Apps has no push channel.
- Closing an election deletes its unused codes after recording how many
  there were.
- A closed election cannot be reopened. Run a new one instead.

**Anonymity**

- Codes are generated per election. A slip from one vote does nothing in
  another, and a member's ballots in different elections cannot be linked.
- `Ballots` and `VotingCodes` share no column except the election. Ballot
  ids are random so storage order does not reveal voting order.
- Request logs must never record the code or the ballot contents.

**Audit numbers published with every result**

- Codes issued, codes used, ballots counted (used must equal counted)
- The receipt list, and every anonymous ballot, so anyone can recount

## Implementation

The schema, the two database users and their grants are the SQL scripts in
`src/ChisBallot.Data/Migrations/`. The sites reach ballots only through
stored procedures: `CastBallot`, `GetTurnout`, `GetResults` (voting and admin
site) and `CloseElection` (admin site). Neither database user has any
table-level right to read ballots, codes or tallies, so the database itself
refuses an early peek, whatever the application code does. A trigger on
`Elections` enforces the lifecycle: Draft to Open to Closed, Closed final,
settings frozen once Open.

## Known limit

With a small board, the result itself can reveal votes: a unanimous outcome
tells everyone how each member voted. No ballot system, paper included,
avoids this. Live results make it worse: a tally that moves right after a
known member signs off points at that member's vote. The chair should turn
live results on only for votes where that does not matter.

## Addresses

| Address | What it is |
|---|---|
| www.chisballot.org | Landing page on GitHub Pages. `chisballot.org` redirects here |
| vote.chisballot.org | Voting site. The root says a slip's QR code is needed to vote |
| vote.chisballot.org/v/#{code} | A voter's ballot. The code is in the URL fragment, which the browser never sends to a server, so it cannot reach request logs, the CDN or a referrer header |
| vote.chisballot.org/e/{slug} | Election page: live turnout, live results if enabled, full results once Closed |
| vote.chisballot.org/audit/{slug} | Audit page: audit numbers, receipt list, every anonymous ballot, in-browser recount and the deployed commit. See `docs/audit-page.md` |
| admin.chisballot.org | Admin site |

## Two deployments

| | Voting site | Admin site |
|---|---|---|
| Address | vote.chisballot.org | admin.chisballot.org |
| Sign-in | None. A slip's code is the only credential | Required on every page and API call; invited admins only |
| Database user | `chisballot_vote` | `chisballot_admin` |
| Can do | Cast a ballot, read published results | Create elections, contests and choices; generate codes; open and close |
| Cannot do | Anything else | Read or change ballots while an election is open; change a closed election |

The voting site ships no admin pages and exposes no admin endpoints, so
there is nothing on the public site for an attacker to probe. Database
permissions are enforced by the database itself, not only by application
code.
