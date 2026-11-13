# chisBallot

[![Build, test and deploy](https://github.com/glensouza/chisBallot/actions/workflows/build-and-deploy.yml/badge.svg?branch=main)](https://github.com/glensouza/chisBallot/actions/workflows/build-and-deploy.yml)
[![Landing page](https://github.com/glensouza/chisBallot/actions/workflows/pages.yml/badge.svg?branch=main)](https://github.com/glensouza/chisBallot/actions/workflows/pages.yml)
[![License](https://img.shields.io/github/license/glensouza/chisBallot)](LICENSE)
![.NET 11](https://img.shields.io/badge/.NET-11-512BD4)
![PostgreSQL 17](https://img.shields.io/badge/PostgreSQL-17-336791)

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
| admin.chisballot.org | Admin site, invited admins with a passkey or authenticator app only |

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
- [`ops/README.md`](ops/README.md): the server, how it is set up and how it is
  deployed

## Repository layout

| Path | Contents |
|---|---|
| `src/ChisBallot.Vote` | Voting site, the part that runs in the browser (Blazor WebAssembly) |
| `src/ChisBallot.Vote.Server` | Serves the voting site and its three endpoints: a ballot, turnout, cast a ballot. No admin routes |
| `src/ChisBallot.Admin` | Admin site (Blazor Server), every page needs the admin role |
| `src/ChisBallot.Data` | Database schema scripts, the cast-ballot call, code generation |
| `src/ChisBallot.Contracts` | Shared request and response types, safe for the browser |
| `src/ChisBallot.Migrator` | Applies the database scripts and creates the two database users |
| `src/ChisBallot.AppHost` | Runs everything on a laptop with one command |
| `tests/ChisBallot.Tests` | Tests that run against a real PostgreSQL |
| `ops/` | The server: service files, deploy script, backup, and the Proxmox host's side of the backup |
| `site/` | The landing page |
| `docs/` | Design and audit documentation |
| `.github/workflows/` | Build, test, deploy and infrastructure workflows |

The admin site can create a vote (questions, options, seats, write-ins,
random order, running totals), open it, close it, and show the results, with
ties flagged for a revote.

It also makes the slips: the board chair says how many, and the admin site
shows a printable sheet of QR slips once. Only a scrambled (hashed) form of
each code is stored, so a lost sheet cannot be reprinted.

Not built yet: editing a draft vote. The ballot page has no third-party bot check; it
uses rate limits and a small proof of work the browser solves (see
`docs/anonymity-and-security.md`).

## Run it on your computer

You need the .NET 11 SDK (see `global.json`) and Docker.

```
dotnet run --project src/ChisBallot.AppHost
```

This starts PostgreSQL in a container, applies the database scripts, creates
the two database users and a practice election with eight slips, then starts
both sites (voting on port 5244, admin on 5004). The Aspire dashboard (address printed on start)
lists each part and its address, and the `migrator` log lists the practice
slips as links you can open. The admin site signs you in as a local
developer; that shortcut exists only in debug builds and a CI check keeps it
out of release builds.

To try the real sign-in locally, make an admin and start with the shortcut off:

```
DatabaseConnectionString=x IdentityDatabasePath=.local/identity.db PublicUrl=http://localhost:5004   dotnet run --project src/ChisBallot.Admin --no-launch-profile -- create-admin you@example.com
CHISBALLOT_REAL_SIGNIN=1 dotnet run --project src/ChisBallot.AppHost
```

Open the link it prints, choose a password, sign in, then add a passkey or an
authenticator app; the admin pages stay closed until you do.

## Run the tests

You need the .NET 11 SDK and Docker. The tests start a PostgreSQL container,
apply the same migration scripts and the same two database users the sites
use, and exercise the database as those users.

```
dotnet test
```

## Contributing

Pull requests are welcome, and you do not need to be an expert. If you have read this far
you can help: fix a typo, make a page easier to use on a phone, check a claim in the
security documentation against the code, add a test, translate a screen, or pick one of
the ideas in the list above. A small change that is clearly explained is a good first
pull request.

The short version of how it works: the build runs the tests, a style check and a commit
check on every pull request, and a maintainer reviews it. The rules (code style, signed
commits, what the site promises about anonymity) are in [CONTRIBUTING.md](CONTRIBUTING.md).
If something is unclear, open an issue and ask.

## License

See [LICENSE](LICENSE).
