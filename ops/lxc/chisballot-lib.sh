#!/bin/sh
# Shared by chisballot-deploy and chisballot-rollback. Sourced, never run.
#
# Two safety rules live here:
#
#   1. A deploy does not start while a real (non-demo) vote is open. A restore
#      from a backup would lose the ballots cast since, and a vote in progress is
#      exactly when that must never happen.
#   2. Before a deploy changes anything it takes a snapshot of the ballot database
#      and the admin accounts file. A rollback restores that snapshot, and refuses
#      if real ballots or elections exist now that the snapshot does not have.

BASE=/opt/chisballot
SNAPSHOTS=${CHISBALLOT_SNAPSHOTS:-/var/lib/chisballot-deploy/snapshots}
KEEP_SNAPSHOTS=3
IDENTITY_DB=/var/lib/chisballot-admin/identity.db
# The database name can be overridden to try these functions on a scratch copy.
DB=${CHISBALLOT_DB:-chisballot}

db() {
    runuser -u postgres -- psql -d "$DB" -v ON_ERROR_STOP=1 -Atq "$@"
}

schema_exists() {
    [ "$(db -c "SELECT to_regclass('ballot.elections') IS NOT NULL")" = t ]
}

# How many real (non-demo) votes are open. The demo is always open, so it is left out.
open_votes() {
    if schema_exists; then
        db -c "SELECT count(*) FROM ballot.elections WHERE status = 1 AND NOT is_demo"
    else
        echo 0
    fi
}

# What a restore would throw away if it ran now: the ballots and the elections
# that are not demos. Two numbers, ballots then elections.
fingerprint() {
    if schema_exists; then
        db -c "SELECT (SELECT count(*) FROM ballot.ballots b JOIN ballot.elections e ON e.id = b.election_id WHERE NOT e.is_demo) || ' ' || (SELECT count(*) FROM ballot.elections WHERE NOT is_demo)"
    else
        echo "0 0"
    fi
}

healthy() {
    vote=$(curl -s -o /dev/null -m 5 -w '%{http_code}' http://127.0.0.1:5101/api/elections/health-check || true)
    admin=$(curl -s -o /dev/null -m 5 -w '%{http_code}' http://127.0.0.1:5102/Account/Login || true)
    [ "$vote" = 404 ] && [ "$admin" = 200 ]
}

# Waits up to about 40 seconds for both sites to answer.
wait_healthy() {
    i=0
    until healthy; do
        i=$((i + 1))
        [ "$i" -lt 20 ] || return 1
        sleep 2
    done
}

switch() {
    app=$1
    target=$2
    ln -sfn "$target" "$BASE/$app/current.new"
    mv -T "$BASE/$app/current.new" "$BASE/$app/current"
}

# Saves everything a rollback needs under $SNAPSHOTS/<stamp>, readable by root only:
# the ballot database, the admin accounts file, the two programs now in service,
# and the fingerprint. Only the newest few are kept.
snapshot() {
    stamp=$1
    dir=$SNAPSHOTS/$stamp
    install -d -m 700 "$SNAPSHOTS" "$dir"

    if schema_exists; then
        runuser -u postgres -- pg_dump --format=custom --schema=ballot "$DB" > "$dir/ballot.dump"
        fingerprint > "$dir/fingerprint"
    fi

    if [ -f "$IDENTITY_DB" ]; then
        tmp=$(mktemp -d)
        chown chisballot-admin "$tmp"
        runuser -u chisballot-admin -- sqlite3 "$IDENTITY_DB" ".backup '$tmp/identity.db'"
        install -m 600 "$tmp/identity.db" "$dir/identity.db"
        rm -rf "$tmp"
    fi

    readlink "$BASE/vote/current" > "$dir/previous_vote" 2>/dev/null || true
    readlink "$BASE/admin/current" > "$dir/previous_admin" 2>/dev/null || true

    ls -1dt "$SNAPSHOTS"/* 2>/dev/null | tail -n +$((KEEP_SNAPSHOTS + 1)) | while read -r old; do
        rm -rf -- "$old"
    done
}
