# Contributing to chisBallot

Thank you for wanting to help. This is a small voting site for a school council, and it
gets better when more people read it, run it and break it. Pull requests are welcome,
and so are questions and bug reports.

Trust matters more here than features, so a few rules are firm. They are short, and
they are the same for everyone.

## Before you start

- Small pull requests, one concern each, merge fastest. If the change is big, open an
  issue first and say what you plan to do.
- Read `docs/anonymity-and-security.md`. It lists what the site promises and where each
  promise is checked. A change must not weaken one. In particular: never log a voting
  code, a ballot or an address on the cast request, and a ballot never gets a time, a
  number or anything that could order or link ballots.
- Database changes follow `docs/migrations.md`: every release must work with the schema
  of the one before it. A test fails the build when a script breaks that rule.

## Run it

You need the .NET 11 SDK (see `global.json`) and Docker.

```
dotnet run --project src/ChisBallot.AppHost     # both sites and a practice vote
dotnet test                                     # tests start their own PostgreSQL
```

## Code style

The rules are in `.editorconfig`, and the **Code style** step of the build fails a pull
request that breaks them. To fix your changes automatically:

```
dotnet format style --severity warn
```

The rules, in short:

- **Explicit types.** Never `var`.
- **`this.`** on instance members. No underscore on private fields. Constants and
  `static readonly` fields are PascalCase. Acronyms over two letters are Pascal-style
  (`LmStudio`); two-letter ones stay (`Id`).
- **File-scoped namespace, with the `using` lines after it.** No unused usings.
- **One top-level type per file.** No fully-qualified type names where a `using` will do.
- **Always braces**, even for one line. Invert conditions and return early.
- Primary constructors where nothing needs validating, `init` for properties set only at
  construction, collection expressions (`[]`), `coll.Count != 0` rather than `.Any()`,
  `is null` / `is not null`.
- Switch expressions and property patterns over if-else chains.
- `await using` for async disposables, `System.Threading.Lock` for locks.
- Async methods end in `Async` and take a `CancellationToken` when they await I/O.
- No silent catches. Log and rethrow, or return the error. An empty catch has a braced
  body with a comment saying what is swallowed.
- Serilog structured logging, with properties, never string interpolation.

Razor pages follow the same rules, except the sign-in pages under
`src/ChisBallot.Admin/Account`, which came from the framework template and keep its style.

## Tests and docs

- A change to behavior comes with a test. The tests run against a real PostgreSQL, the same
  two database roles the sites use, so they check the security model too.
- A change to what the site does, or promises, updates the docs in the same pull request.

## Commits must be verified

Every commit in a pull request must show **Verified** on GitHub. The **Verified commits**
check fails otherwise. The simplest way is to sign with an SSH key:

```
ssh-keygen -t ed25519 -f ~/.ssh/signing -C "you@example.org"
git config gpg.format ssh
git config user.signingkey ~/.ssh/signing.pub
git config commit.gpgsign true
```

Then add `~/.ssh/signing.pub` to GitHub under Settings, SSH and GPG keys, as a
**Signing key**. The email in your commits must be one of your verified GitHub emails.
Already made commits can be signed with `git rebase --exec 'git commit --amend --no-edit -S' origin/main`
and pushed again with `--force-with-lease` to your own branch.

## Opening the pull request

1. Branch from `main`, make the change, run `dotnet test` and `dotnet format style`.
2. Open the pull request and say what changed and why, in a few plain sentences.
3. The build runs the tests, the style check and the commit check. A maintainer reviews it
   and merges it with a squash.

Pull requests from forks are welcome. The first build on a fork waits for a maintainer to
approve it, because it runs your code on a test machine.

## Reporting a problem

An ordinary bug is an issue. A problem that could expose how someone voted, or let a vote
be changed, should not be posted in public: contact the maintainer directly first.
