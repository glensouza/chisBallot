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
    ShowLiveResults bit          NOT NULL DEFAULT 0 -- set in Draft, frozen once Open
);

-- One question on the ballot: an officer race or a yes/no motion.
CREATE TABLE Contests (
    Id            uniqueidentifier NOT NULL PRIMARY KEY,
    ElectionId    uniqueidentifier NOT NULL REFERENCES Elections (Id),
    Title         nvarchar(200)    NOT NULL,
    MaxSelections int              NOT NULL DEFAULT 1,
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
```

## Rules

**Casting a ballot** is one transaction:

1. `UPDATE VotingCodes SET Used = 1 WHERE ElectionId = @e AND CodeHash = @h AND Used = 0`,
   joined to an election whose `Status` is Open. If no row changes, stop:
   the code is unknown, already used, or belongs to another election.
2. Insert the ballot and its selections.

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
