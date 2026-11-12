# chisBallot

A small open source voting site for the Citrus Hills Intermediate School
council board. About 40 voters, one slip each, and a result anyone can check.

Trust, anonymity and verifiability matter more here than features. The board
chair runs each vote; board members are the voters. Paper ballots stay
available as the fallback.

## How a vote works

1. The chair checks a member off the roster.
2. The member picks any folded slip from a pile. Each slip has a QR code.
3. They scan it, vote on their phone, and keep a receipt code.
4. When the vote closes, the audit page lists every receipt code and every
   anonymous ballot, so anyone can find their own and recount.

A ballot stores the choices and a random receipt code. It never stores a
name, the slip's code or a time.

## Addresses

| Address | What it is |
|---|---|
| www.chisballot.org | Landing page (GitHub Pages). `chisballot.org` redirects here |
| vote.chisballot.org | Voting site. A voter's ballot is `/v/{slug}#{code}`, an election's page is `/e/{slug}`, its audit page is `/audit/{slug}` |
| admin.chisballot.org | Admin site, sign-in only |

## Documentation

- [`docs/data-model.md`](docs/data-model.md): tables, election lifecycle,
  results, ties, write-ins, random order
- [`docs/anonymity-and-security.md`](docs/anonymity-and-security.md): every
  anonymity and security measure, where to check it in the code, and the
  known limits. Start here if you want to audit this
- [`docs/audit-page.md`](docs/audit-page.md): the public audit page
- [`docs/demo.md`](docs/demo.md): demo mode and the live demo script
- [`docs/presenter-notes.md`](docs/presenter-notes.md): notes for presenting
  the proposal
- [`docs/branding.md`](docs/branding.md): colors and artwork rules
- [`infra/README.md`](infra/README.md): the infrastructure and how it is
  deployed

## Repository layout

| Path | Contents |
|---|---|
| `src/ChisBallot.Vote` | Voting site (Blazor WebAssembly) |
| `src/ChisBallot.Vote.Api` | Voting API (Azure Functions): a ballot, turnout, cast a ballot. No admin routes |
| `src/ChisBallot.Admin` | Admin site (Blazor WebAssembly), every route needs the admin role |
| `src/ChisBallot.Admin.Api` | Admin API (Azure Functions), checks the admin role on every call |
| `src/ChisBallot.Data` | Database schema scripts, the cast-ballot call, code generation |
| `src/ChisBallot.Contracts` | Shared request and response types, safe for the browser |
| `src/ChisBallot.Migrator` | Applies the database scripts and creates the two database users |
| `src/ChisBallot.AppHost` | Runs everything on a laptop with one command |
| `tests/ChisBallot.Tests` | Tests that run against a real SQL Server |
| `infra/` | Terraform for Azure and Cloudflare |
| `site/` | The landing page |
| `docs/` | Design and audit documentation |
| `.github/workflows/` | Build, test, deploy and infrastructure workflows |

Not built yet: the audit page, results, demo mode, rate limiting of code
guesses and the Cloudflare bot check, and the admin screens for creating and
running an election.

## Run it on your computer

You need the .NET 10 SDK, Docker, and the Azure Functions Core Tools (`func`).

```
dotnet run --project src/ChisBallot.AppHost
```

This starts SQL Server in a container, applies the database scripts, creates
the two database users and a practice election with eight slips, then starts
both APIs and both sites. The Aspire dashboard (address printed on start)
lists each part and its address, and the `migrator` log lists the practice
slips as links you can open. The admin site signs you in as a local
developer; that shortcut exists only in debug builds and a CI check keeps it
out of release builds.

## Run the tests

You need the .NET 10 SDK and Docker. The tests start a SQL Server container,
apply the same migration scripts and the same two database users the sites
use, and exercise the database as those users.

```
dotnet test
```

## License

See [LICENSE](LICENSE).
