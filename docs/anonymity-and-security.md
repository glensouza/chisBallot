# Anonymity and security: what to check

This page lists every measure chisBallot relies on to keep a vote anonymous
and the result honest. Each one says **where to look in the code or
infrastructure** so you can read the claim here, then confirm it yourself.
Please do. Nothing on this page should be taken on trust.

**Status:** the database layer exists and is tested; the websites do not yet.
Entries below that name a table, procedure or grant can be checked today. The
rest are the specification the sites must meet, and as each piece lands its
entry gets the real file and test name. A measure with no test is a gap worth
reporting. The server entries can be checked in `ops/`.

## Checked today

The tests in `tests/ChisBallot.Tests/CastBallotTests.cs` run against a real
PostgreSQL, using the same migration and the same two database roles as the
sites.

| Measure | Code | Test |
|---|---|---|
| A code works once | `ballot.cast_ballot` in `src/ChisBallot.Data/Migrations/001_schema.sql` | `A_code_works_once` |
| A code from another election is refused; a closed or draft election refuses codes | `ballot.cast_ballot` | `A_code_from_another_election_is_refused`, `A_code_is_refused_unless_the_election_is_open` |
| Consuming the code and inserting the ballot are one transaction; bad choices do not use up the code | `ballot.cast_ballot` | `Choices_that_do_not_fit_are_refused_and_the_code_stays_usable` |
| A ballot row has no code, time, sequence or identity column, and nothing references the code table | `ballot.ballots`, `ballot_selections`, `ballot_write_ins` | `A_ballot_row_has_nothing_that_links_it_to_a_code_or_to_voting_order` |
| The voting site cannot read or write ballots, codes or tallies directly; the admin site cannot read ballots or codes | Grants in `002_users_and_grants.sql` | `The_voting_site_cannot_read_or_write_ballots_codes_or_tallies_directly`, `The_admin_site_cannot_read_ballots_or_codes` |
| Results are unreadable until Closed, unless live results were turned on; live results are counts only | `ballot.get_results` | `Results_are_unreadable_until_the_election_is_closed`, `Live_results_show_counts_while_open_only_when_the_election_turned_them_on` |
| Turnout is public and ballots never exceed codes used | `ballot.get_turnout` | `Turnout_is_public_and_ballots_never_exceed_codes_used` |
| Closed is final and settings freeze once Open | `tr_elections_lifecycle` | `A_closed_election_is_final_and_settings_freeze_once_open` |
| Closing deletes only unused codes | `ballot.close_election` | `Closing_deletes_only_the_unused_codes` |
| Write-ins are limited, normalized and counted by a fixed key | `src/ChisBallot.Data/WriteInName.cs` | `CodeTests`, `A_write_in_counts_against_the_limit_and_is_normalized` |
| Codes are 80 random bits and only a hash is stored; receipts are random and unrelated | `VotingCode.cs`, `ReceiptCode.cs` | `CodeTests` |
| Casting logs only the election and the outcome, never a code, hash, choices or address | `BallotCaster.cs` | Read the two `logger.Information` calls |

### Running an election

| Measure | Code | Test |
|---|---|---|
| A new election is checked twice, in the admin page and again in the data layer, before it reaches the database | `src/ChisBallot.Contracts/ElectionRequestValidator.cs`, `ElectionAdmin.CreateAsync` | `ElectionRequestValidatorTests`, `A_bad_request_or_a_used_address_is_refused_with_a_reason`, `A_failed_create_leaves_nothing_behind` |
| A vote cannot open without slips, opens once, and closes once; nothing reopens a closed vote | `ElectionAdmin.OpenAsync`, `CloseAsync`, `tr_elections_lifecycle` | `An_election_with_slips_opens_once_and_then_closes_once`, `A_closed_election_is_final_and_settings_freeze_once_open` |
| A tie that decides who wins is flagged and never broken; clear winners keep their seats | `src/ChisBallot.Contracts/ResultsCalculator.cs` | `ResultsCalculatorTests` (ten cases, including a level yes/no vote and a vote nobody voted in) |
| The admin site shows results only after the database allows them, and the admin database user still cannot read ballots | `ResultsReader`, `ballot.get_results` | `Results_come_back_only_after_close_and_follow_the_tie_rule`, `The_admin_site_cannot_read_ballots_or_codes` |

### Making the slips

| Measure | Code | Test |
|---|---|---|
| Codes are random (80 bits) and only their SHA-256 hash is stored; the plain codes are returned once and never logged | `ElectionAdmin.IssueSlipsAsync`, `VotingCode` | `Slips_are_made_once_and_only_their_hashes_are_stored` |
| Slips can be made once, only for a draft, 1 to 500 at a time | `ElectionAdmin.IssueSlipsAsync` | `A_slip_count_out_of_range_is_refused`, `Slips_cannot_be_made_for_an_open_election` |
| The codes are shown on one page, once, and held only in that page's memory; the QR images are drawn by the admin site itself, so no other service sees a code | `ElectionDetail.razor`, `SlipSheet.razor` | checked by hand in a browser |

### The sites

| Measure | Code | How to check |
|---|---|---|
| The voting site has no admin routes and no way to read results or ballots; it exposes a ballot, turnout and casting a ballot | `src/ChisBallot.Vote.Server/VoteEndpoints.cs` | Read the file; the only routes are `GET /api/elections/{slug}`, its `/turnout`, and `POST /api/cast` |
| The ballot page reads the code from the address fragment, which the browser never sends, then removes it from the address bar; the code is sent only in the body of one POST | `src/ChisBallot.Vote/Pages/Ballot.razor` | Read `OnInitializedAsync` and `SubmitAsync` |
| The ballot page sends no referrer, API answers are never cached, request logging is off, and the site loads no analytics or third-party scripts | `src/ChisBallot.Vote/wwwroot/index.html`, `src/ChisBallot.Vote.Server/SecurityHeaders.cs`, `Program.cs` | Read the files; response headers checked with curl |
| Random question and option order is chosen in the browser with the cryptographic random generator and never sent | `src/ChisBallot.Vote/Shuffler.cs`, `Ballot.razor` | `CastRequest` in `ChisBallot.Contracts` has no order field |
| Every admin page requires a signed-in account that is in the admin role and has a second factor (a passkey or an authenticator app). The role and the second factor are read from the account store on every check, not from the sign-in cookie, so removing an admin takes effect at once | `src/ChisBallot.Admin/AdminAuth.cs`, `AdminRequirement.cs`, the `[Authorize]` line on each page | `AdminAccessTests` (seven cases); changing either check made the matching tests fail |
| There is no public registration and no email. An admin is made by an existing admin (a one-time link, valid three days) or by the `create-admin` command on the server; passwords need 12 characters; five wrong passwords lock the account for 15 minutes; the sign-in cookie is HttpOnly, Strict and Secure, with the `__Host-` prefix in production | `AdminInvites.cs`, `AdminAuth.cs`, `Program.cs` | `An_invitation_makes_an_admin_account...`, `Five_wrong_passwords_lock_the_account`; no `/Account/Register` route exists |
| Admin accounts are stored in a small SQLite file on the admin server, apart from the ballot database, so a ballot-database role cannot see them | `AdminAuth.cs` (`IdentityDatabasePath`) | Read the file |
| The local-development sign-in exists only in Debug builds, and the build pipeline fails if it is found in a Release build | `src/ChisBallot.Admin/DevAdminHandler.cs`, `.github/workflows/build-and-deploy.yml` | The "Dev-only admin sign-in" step |
| The local-development admin sign-in is compiled out of release builds, and CI fails if it is not | `#if DEBUG` in `DevAdminHandler.cs` and `AdminAuth.cs`; the "Dev-only admin sign-in" step in `.github/workflows/build-and-deploy.yml` | Read the step; it searches the release DLL |
| The admin site connects as `chisballot_admin`, the voting site as `chisballot_vote` | `src/ChisBallot.AppHost/AppHost.cs` locally; the server setup when deployed (not written yet) | Read them |

Not covered yet: nothing on the list; see the "What this does not protect against" section for the limits.

Report problems as a GitHub issue on this repository.

## 1. A ballot cannot be linked to a voter

| Measure | Where to check |
|---|---|
| The `ballot.ballots` table has no voter, no voting code, no code hash, no timestamp and no sequence number. Its only columns are a random id, the election id and a random receipt code. | `docs/data-model.md`, then the schema migration in the data project |
| Ballot ids and receipt codes are random, never sequential, so storage order does not reveal voting order. The receipt code is not derived from the voting code. | Ballot creation code; no sequence, `IDENTITY` or time default on any ballot table |
| `ballot.voting_codes` and `ballot.ballots` share no column except the election id. | Schema migration |
| Casting a ballot is one transaction: mark the code used, insert the ballot. Nothing records which code produced which ballot. | The cast-ballot code and its tests |
| If an election randomizes question and option order, the shuffle runs in the voter's browser. The order shown is never sent to the server, stored or logged, and is not derived from the voting code. Ballots store selections with no order. | The ballot page script (shuffle uses `crypto.getRandomValues`); the cast request body carries no order; schema has no order column |
| The voter never signs in and the voting site has no accounts, so there is no identity to attach. | Voting site has no authentication code; `ops/lxc/setup.sh` gives it a database role that can only read an election's setup and call the cast, turnout and results functions |
| Codes are distributed by draw: the chair signs a member off on the roster, the member picks any folded slip from a pile. The roster says who voted; the slips say nothing about who holds them. | Procedure, not code. The chair must keep the roster off the system |

Tests that should prove this (planned): a ballot row contains nothing that
references a code; a code works once; a code from another election is
refused.

## 2. Codes are hard to steal or guess

| Measure | Where to check |
|---|---|
| Only a hash of each code is stored, so the database alone cannot reproduce a slip. | Code generation and the `ballot.voting_codes.code_hash` column |
| Codes come from a cryptographic random number generator and are long enough that guessing is infeasible. The length is stated here once the code exists. | Code generation |
| A code works once and only in its own election. | The single `UPDATE ... WHERE Used = 0` joined to an Open election in `docs/data-model.md`, and its tests |
| Wrong guesses are rate-limited: after 30 refused codes from one address in 10 minutes the voting site answers 429 for that address until the window passes. The count is kept in memory only and is never logged. A code has 80 random bits, so guessing one is not feasible; the limit is there to slow a script down. Every ballot and every demo slip also has to come with a solved puzzle (a proof of work, made by the site and solved by the browser in under a second or two). That makes a script that sends many requests spend real work on each one; it does **not** tell a person from a program, and a determined attacker with a fast machine pays little. It is the self-hosted alternative to a bot check: nothing is loaded from another company, the puzzle holds nothing about the voter, and it is not logged. | `AttemptCounter`, `VoteLimits`, `ProofOfWorkVerifier` and the `/api/cast`, `/api/challenge` endpoints in `VoteEndpoints.cs`; `AttemptCounterTests`, `ProofOfWorkTests` |
| Unused codes are deleted when an election closes. | `ElectionAdmin.CloseAsync`, `ballot.close_election` |

## 3. The code never leaks through the network or the logs

| Measure | Where to check |
|---|---|
| The voting link is `vote.chisballot.org/v/<slug>#<code>`. The slug only names the election. The part after `#` is never sent by the browser to any server, so it cannot appear in a web server log, a CDN log or a Referer header. | `src/ChisBallot.Vote/Pages/Ballot.razor` reads the code from the address fragment only, then removes it from the address bar |
| The page sends the code only in the body of a POST to the site's own API. Never in a URL path or query string. | Client and API code; search for any use of the code in a URL |
| No code, no code hash alongside anything else, and no ballot contents are logged. The cast-ballot request does not log IP addresses. | Serilog configuration and every log call on the cast path |
| No analytics, tracking scripts, third-party scripts or third-party fonts on the ballot page. | The page's HTML and script tags |
| This repository configures no analytics, application-monitoring or request-logging service, and the sites' own request logging is switched off. | `ops/lxc/*.service`, `src/ChisBallot.Vote.Server/Program.cs` (search for `MinimumLevel`) |
| Traffic is encrypted: Cloudflare terminates HTTPS in front of every site and a tunnel carries it to the server, and the database accepts connections only from the same machine. Cloudflare can see traffic in transit. | `ops/lxc/cloudflared.service`, the listen settings in `ops/lxc/setup.sh` |

## 4. Results are not shown early

| Measure | Where to check |
|---|---|
| Turnout (codes used of codes issued) is always public. It reveals how many have voted, not what. | Turnout endpoint |
| Results are not readable through any API until the election is Closed, unless that election has live results turned on. The setting is chosen while the election is a draft, defaults to off and cannot change once the election is open. | Results endpoint, `ShowLiveResults` in the schema, and the code that freezes it |
| Even with live results on, only per-choice counts are shown. Receipt codes and the list of ballots wait until Closed. | Results endpoint |
| The database enforces this too: the voting site's database user can only cast a ballot and read published results. | The migration's `GRANT` and `DENY` statements |

## 5. Anyone can recount

| Measure | Where to check |
|---|---|
| At close, the receipt list and every anonymous ballot are published, so anyone can recount, and each voter can find their own receipt code in the list. | `ballot.get_ballots` in `004_audit.sql` (refuses anything but a Closed election), `AuditReader`, and `AuditTests` |
| Audit numbers are published with every result: codes issued, codes used, ballots counted. Used must equal counted. | The audit numbers on `AuditPage.razor` (the page shows a pass or fail) and `ResultsCalculator` with its tests |
| Every election has a public audit page, `/audit/{slug}`, with the audit numbers, the receipt list, every anonymous ballot as a download, and a recount that runs in the browser. Receipt lookup happens in the browser and the receipt is never sent to the server. | `AuditPage.razor`: the search is a filter over the downloaded list and makes no request (`docs/audit-page.md`) |
| The published ballot list is ordered by receipt code, which is random, never by storage order, and carries no time or sequence number. | The `ORDER BY` in `ballot.get_ballots` and the download code in `AuditPage.razor` |
| The page shows the commit the site was built from, linked to the repository and the deploy run. | Build step that stamps the commit; the workflows in `.github/workflows/` |
| A closed election cannot be reopened or changed. | Status transition code and its test |
| A tie that decides who wins is flagged "revote needed" and never broken by the system or the chair. The revote is a new election. | Tie-check function and its tests |
| Write-ins are counted by a fixed normalization and cannot be merged or edited by hand. | Write-in counting code and its tests |

## 6. The voting site cannot be used to attack the admin side

| Measure | Where to check |
|---|---|
| The voting site contains no admin pages and exposes no admin endpoints. | `ChisBallot.Vote` and `ChisBallot.Vote.Server` contain no admin code |
| Admin is a separate site, `admin.chisballot.org`, where every route and API call requires a signed-in, invited admin. | The Admin policy in `ChisBallot.Admin/AdminAuth.cs` (invited admins with a second factor) |
| The two sites use two database users with different rights. The admin user cannot read or change the ballots of an open election. | `chisballot_vote` and `chisballot_admin` grants in the migration; the settings files `ops/lxc/setup.sh` writes |
| Everything runs from this public repository. The server's setup is in `ops/` and deployment is by `.github/workflows/build-and-deploy.yml` after a reviewer approves it. The parts not in the repository are the tunnel's credentials, the passwords `setup.sh` makes on the server, and the Proxmox host's own settings (documented in `ops/README.md`). | `ops/`, `.github/workflows/` and the Actions run history |

## 7. The demo cannot touch a real vote

| Measure | Where to check |
|---|---|
| A demo is marked when it is created and can never change into a real election, or the reverse. | `IsDemo` in the schema; admin API and its tests |
| The public site can issue a code only through one database procedure that refuses any election that is not a demo. Real elections have no code-issuing endpoint, and the voting database user has no insert right on `VotingCodes`. | The procedure and the `chisballot_vote` grants in the migration; vote API routes |
| Resetting a demo works only on demo elections. | Admin API and its tests |
| A demo follows every anonymity rule above. | It runs the same code path as a real vote |

Details: `docs/demo.md`.

## 8. What this does not protect against

An honest audit needs the weak points too.

- **A small board can reveal itself.** If the outcome is unanimous, everyone
  knows how each member voted. This is true of paper too.
- **Live results make timing visible.** If a tally changes right after a
  known member is signed off, that member's choice is guessable. The chair
  should turn live results on only for votes where this does not matter.
- **Whoever has root on the server can see everything stored.** The server
  is one container in a home lab run by one person. Root on it (and root on
  the Proxmox host under it) can read the database files and the passwords in
  `/etc/chisballot`. Neither database role records who voted, but whoever
  holds root can read all data. This is the same trust placed in any host,
  moved from a cloud provider to a person; the code and the setup are public so
  the claim can be checked, and a recount from the published ballots does not
  depend on trusting the server.
- **Backups.** The nightly backup of the database and the admin accounts is
  encrypted to a public key before it is written, and the private key is not on
  the server. The weekly Proxmox backup of the whole container, kept on the NAS,
  is not encrypted: it contains the database files and the passwords. Anyone
  who can read that NAS folder can read it. The database also keeps its own
  write-ahead log; in principle it holds when each ballot row was written, and
  the code-used update and the ballot insert share one transaction. Nothing
  stored in the tables ties a code to a ballot, but that internal log is
  outside this repository's control.
- **Write-ins can mark a ballot.** In a contest that allows write-ins, a
  voter could type something distinctive to prove how they voted, and the
  full ballot list is public. Names are length- and character-limited and
  shown only after close, which reduces but does not remove this. Leave
  write-ins off where a bought or coerced vote is a concern.
- **The voter's own device.** A shoulder-surfer, a browser extension or a
  photo of the receipt screen is outside what a website can prevent.
- **The deployed code matching this repository.** The audit page shows the
  commit the site was built from and links to the deploy run, so you can
  compare it to the code you read. There is still no cryptographic proof
  that the running site is that build.
- **Paper is still the fallback.** During any pilot the paper result remains
  official.
