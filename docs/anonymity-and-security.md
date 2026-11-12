# Anonymity and security: what to check

This page lists every measure chisBallot relies on to keep a vote anonymous
and the result honest. Each one says **where to look in the code or
infrastructure** so you can read the claim here, then confirm it yourself.
Please do. Nothing on this page should be taken on trust.

**Status:** the database layer exists and is tested; the websites do not yet.
Entries below that name a table, procedure or grant can be checked today. The
rest are the specification the sites must meet, and as each piece lands its
entry gets the real file and test name. A measure with no test is a gap worth
reporting. The infrastructure entries can be checked in `infra/`.

## Checked today

The tests in `tests/ChisBallot.Tests/CastBallotTests.cs` run against a real
SQL Server, using the same migration and the same two database users as the
sites.

| Measure | Code | Test |
|---|---|---|
| A code works once | `dbo.CastBallot` in `src/ChisBallot.Data/Migrations/001_schema.sql` | `A_code_works_once` |
| A code from another election is refused; a closed or draft election refuses codes | `dbo.CastBallot` | `A_code_from_another_election_is_refused`, `A_code_is_refused_unless_the_election_is_open` |
| Consuming the code and inserting the ballot are one transaction; bad choices do not use up the code | `dbo.CastBallot` | `Choices_that_do_not_fit_are_refused_and_the_code_stays_usable` |
| A ballot row has no code, time, sequence or identity column, and nothing references the code table | `dbo.Ballots`, `BallotSelections`, `BallotWriteIns` | `A_ballot_row_has_nothing_that_links_it_to_a_code_or_to_voting_order` |
| The voting site cannot read or write ballots, codes or tallies directly; the admin site cannot read ballots or codes | Grants in `002_users_and_grants.sql` | `The_voting_site_cannot_read_or_write_ballots_codes_or_tallies_directly`, `The_admin_site_cannot_read_ballots_or_codes` |
| Results are unreadable until Closed, unless live results were turned on; live results are counts only | `dbo.GetResults` | `Results_are_unreadable_until_the_election_is_closed`, `Live_results_show_counts_while_open_only_when_the_election_turned_them_on` |
| Turnout is public and ballots never exceed codes used | `dbo.GetTurnout` | `Turnout_is_public_and_ballots_never_exceed_codes_used` |
| Closed is final and settings freeze once Open | `TR_Elections_Lifecycle` | `A_closed_election_is_final_and_settings_freeze_once_open` |
| Closing deletes only unused codes | `dbo.CloseElection` | `Closing_deletes_only_the_unused_codes` |
| Write-ins are limited, normalized and counted by a fixed key | `src/ChisBallot.Data/WriteInName.cs` | `CodeTests`, `A_write_in_counts_against_the_limit_and_is_normalized` |
| Codes are 80 random bits and only a hash is stored; receipts are random and unrelated | `VotingCode.cs`, `ReceiptCode.cs` | `CodeTests` |
| Casting logs only the election and the outcome, never a code, hash, choices or address | `BallotCaster.cs` | Read the two `logger.Information` calls |

### The sites

| Measure | Code | How to check |
|---|---|---|
| The voting API has no admin routes and no way to read results or ballots; it exposes a ballot, turnout and casting a ballot | `src/ChisBallot.Vote.Api/VoteFunctions.cs` | Read the file; `/api/me` and `/api/elections` return 404 on the voting API |
| The ballot page reads the code from the address fragment, which the browser never sends, then removes it from the address bar; the code is sent only in the body of one POST | `src/ChisBallot.Vote/Pages/Ballot.razor` | Read `OnInitializedAsync` and `SubmitAsync` |
| The ballot page sends no referrer, and the site loads no analytics or third-party scripts | `src/ChisBallot.Vote/wwwroot/index.html`, `staticwebapp.config.json` | Read the two files |
| Random question and option order is chosen in the browser with the cryptographic random generator and never sent | `src/ChisBallot.Vote/Shuffler.cs`, `Ballot.razor` | `CastRequest` in `ChisBallot.Contracts` has no order field |
| Every admin route and API call requires the admin role, twice: by the site's route rules and again inside each admin function | `src/ChisBallot.Admin/wwwroot/staticwebapp.config.json`, `src/ChisBallot.Admin.Api/AdminPrincipal.cs` | Read both |
| The local-development admin sign-in is compiled out of release builds, and CI fails if it is not | `#if DEBUG` in `AdminPrincipal.cs`; the "Dev-only admin sign-in" step in `.github/workflows/app.yml` | Read the step; it searches the release DLL |
| The admin API connects as `chisballot_admin`, the voting API as `chisballot_vote` | `src/ChisBallot.AppHost/AppHost.cs` locally; `infra/main.tf` when deployed | Read both |

Not covered yet: the audit page, demo mode, rate limiting, the bot check, and
the tie check.

Report problems as a GitHub issue on this repository.

## 1. A ballot cannot be linked to a voter

| Measure | Where to check |
|---|---|
| The `Ballots` table has no voter, no voting code, no code hash, no timestamp and no sequence number. Its only columns are a random id, the election id and a random receipt code. | `docs/data-model.md`, then the schema migration in the data project |
| Ballot ids and receipt codes are random, never sequential, so storage order does not reveal voting order. The receipt code is not derived from the voting code. | Ballot creation code; no `NEWSEQUENTIALID`, `IDENTITY` or `DEFAULT SYSDATETIME` on any ballot table |
| `VotingCodes` and `Ballots` share no column except the election id. | Schema migration |
| Casting a ballot is one transaction: mark the code used, insert the ballot. Nothing records which code produced which ballot. | The cast-ballot code and its tests |
| If an election randomizes question and option order, the shuffle runs in the voter's browser. The order shown is never sent to the server, stored or logged, and is not derived from the voting code. Ballots store selections with no order. | The ballot page script (shuffle uses `crypto.getRandomValues`); the cast request body carries no order; schema has no order column |
| The voter never signs in and the voting site has no accounts, so there is no identity to attach. | Voting site has no authentication code; `app.yml` and `infra/main.tf` configure no auth for it |
| Codes are distributed by draw: the chair signs a member off on the roster, the member picks any folded slip from a pile. The roster says who voted; the slips say nothing about who holds them. | Procedure, not code. The chair must keep the roster off the system |

Tests that should prove this (planned): a ballot row contains nothing that
references a code; a code works once; a code from another election is
refused.

## 2. Codes are hard to steal or guess

| Measure | Where to check |
|---|---|
| Only a hash of each code is stored, so the database alone cannot reproduce a slip. | Code generation and the `VotingCodes.CodeHash` column |
| Codes come from a cryptographic random number generator and are long enough that guessing is infeasible. The length is stated here once the code exists. | Code generation |
| A code works once and only in its own election. | The single `UPDATE ... WHERE Used = 0` joined to an Open election in `docs/data-model.md`, and its tests |
| Wrong guesses are rate-limited, and the ballot page is behind a Cloudflare Turnstile bot check. | Rate-limit code in the vote API; `cloudflare_turnstile_widget` in `infra/dns.tf` |
| Unused codes are deleted when an election closes. | Close logic in the admin API |

## 3. The code never leaks through the network or the logs

| Measure | Where to check |
|---|---|
| The voting link is `vote.chisballot.org/v/<slug>#<code>`. The slug only names the election. The part after `#` is never sent by the browser to any server, so it cannot appear in a web server log, a CDN log or a Referer header. | `src/ChisBallot.Vote/Pages/Ballot.razor` reads the code from the address fragment only, then removes it from the address bar |
| The page sends the code only in the body of a POST to the site's own API. Never in a URL path or query string. | Client and API code; search for any use of the code in a URL |
| No code, no code hash alongside anything else, and no ballot contents are logged. The cast-ballot request does not log IP addresses. | Serilog configuration and every log call on the cast path |
| No analytics, tracking scripts or third-party fonts on the ballot page. The only third-party script is Turnstile, which sees the page visit but not the fragment. | The page's HTML and script tags |
| This repository configures no Application Insights or SQL auditing, so no extra service records requests or queries. | `infra/`: search for `insights`, `audit`, `log_analytics` |
| Traffic is encrypted: HTTPS on every site, TLS 1.2 minimum on the database. | `infra/main.tf` (`minimum_tls_version`); Pages and Static Web Apps certificates |

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
| At close, the receipt list and every anonymous ballot are published, so anyone can recount, and each voter can find their own receipt code in the list. | Results-publishing code |
| Audit numbers are published with every result: codes issued, codes used, ballots counted. Used must equal counted. | Audit calculation and its test |
| Every election has a public audit page, `/audit/{slug}`, with the audit numbers, the receipt list, every anonymous ballot as a download, and a recount that runs in the browser. Receipt lookup happens in the browser and the receipt is never sent to the server. | Audit page code; the page makes no request containing a receipt code (`docs/audit-page.md`) |
| The published ballot list is ordered by receipt code, which is random, never by storage order, and carries no time or sequence number. | Audit page query and download code |
| The page shows the commit the site was built from, linked to the repository and the deploy run. | Build step that stamps the commit; the workflows in `.github/workflows/` |
| A closed election cannot be reopened or changed. | Status transition code and its test |
| A tie that decides who wins is flagged "revote needed" and never broken by the system or the chair. The revote is a new election. | Tie-check function and its tests |
| Write-ins are counted by a fixed normalization and cannot be merged or edited by hand. | Write-in counting code and its tests |

## 6. The voting site cannot be used to attack the admin side

| Measure | Where to check |
|---|---|
| The voting site contains no admin pages and exposes no admin endpoints. | `ChisBallot.Vote` and `ChisBallot.Vote.Api` contain no admin code |
| Admin is a separate site, `admin.chisballot.org`, where every route and API call requires a signed-in, invited admin. | Static Web Apps route configuration in `ChisBallot.Admin` |
| The two sites use two database users with different rights. The admin user cannot read or change the ballots of an open election. | `chisballot_vote` and `chisballot_admin` grants in the migration; comments in `infra/main.tf` |
| Everything runs from this public repository. Infrastructure is declared in `infra/` and deployed by the workflows in `.github/workflows/`, not set up by hand. | `infra/`, `.github/workflows/` and the Actions run history |

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
- **The database administrator can see everything stored.** The server admin
  login exists and can read the tables. Neither it nor the two app users
  record who voted, but whoever holds it can read all data. Keep its use and
  the Terraform state (where the passwords live) limited to the deploy
  identity.
- **Database logs and backups.** Azure keeps backups and a transaction log on
  its side. In principle they hold when each ballot row was written, and the
  code-used update and the ballot insert share one transaction. This
  repository cannot turn that off. Nothing stored in the tables ties a code
  to a ballot, but that internal log is outside this repository's control.
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
