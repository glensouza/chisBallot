# Changing the database, and going back

The database scripts are in `src/ChisBallot.Data/Migrations/`. They run in order,
once each, every time a release is deployed (`ChisBallot.Migrator`), and each script
runs in one transaction, so a script that fails changes nothing.

## The rule: every release works with the schema of the one before

A rollback puts the previous programs back. That is only safe if the previous
programs still work with the database the newer release left behind. So a script
may:

- add a table, a column (nullable, or with a default), an index, a function;
- replace a function **with the same signature**;
- add or change grants.

A script may **not**, in the same release that stops using it:

- drop or rename a table, column, function, trigger or role;
- change a column's type, or make a column `NOT NULL`;
- add a `NOT NULL` column with no default;
- truncate a table;
- delete or rewrite ballots, codes or selections. Ballots are never edited.

To remove something, stop using it in one release and drop it in a later one. A
test (`MigrationRulesTests`) fails the build when a script breaks a rule. A script
that really has to can carry a line `-- breaking-change-approved: <reason>`; a
reviewer will see that line, and the deploy that carries it is a deploy to plan
(no open vote, a rollback that restores the database).

There are no "down" scripts. An untested down script is worse than none. A mistake
is fixed by a new script, or by restoring the snapshot below.

## What a deploy does to protect a vote

1. It **waits while a real vote is open** (the always-open demo does not count). The
   deploy job stays paused, up to four hours, and carries on by itself when the vote
   closes; if the vote is still open after that it stops without changing anything. To
   deploy anyway, as root on the server:
   `CHISBALLOT_FORCE_DEPLOY=1 /usr/local/sbin/chisballot-deploy <build folder>`.
2. It takes a **snapshot** before touching anything, in
   `/var/lib/chisballot-deploy/snapshots/<release>/` (root only, newest three kept):
   the ballot database, the admin accounts file, which programs were in service, and
   a count of the real ballots and elections. An encrypted copy for the NAS is also
   made.
3. It applies the database changes, switches to the new programs, and waits for both
   sites to answer. If they do not, it **rolls back by itself**.

## Rolling back

```
sudo chisballot-rollback            # to before the newest deploy
sudo chisballot-rollback <release>  # to before a given deploy (a folder name in the snapshots)
```

It restores the ballot database and the admin accounts file from the snapshot and
puts the previous programs back. It refuses, and changes nothing, if that would lose
a real vote: a real vote is open, or the real ballots and elections now are not
exactly the ones the snapshot has. `--force` overrides the refusal and discards
everything since the snapshot, so use it only knowing what that is.

The automatic rollback after a failed deploy does the same, except that when a real
vote somehow exists it leaves the database alone and puts only the programs back,
which is safe because of the rule above.

Things a rollback does not bring back: admin accounts invited, or passwords changed,
after the snapshot (the admin accounts file is restored whole); and demo data made
after it (the demo is reset by the next deploy).

The snapshots are for the last few deploys. The nightly encrypted backups, the weekly
whole-server backup and the NAS snapshots are for everything older; restoring those
is described at the top of `ops/lxc/chisballot-backup`.
