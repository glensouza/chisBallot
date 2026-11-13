#!/bin/sh
# Prepares the chisBallot server (a Debian machine with PostgreSQL 17, the .NET
# 11 runtime and cloudflared already installed) to run the two sites. Run it
# as root from this folder. It is safe to run again: it never overwrites the
# secrets it made, and it re-applies the rest.
#
# What it does
#   - makes the unprivileged users the sites, the migrator and the tunnel run as
#   - makes the database and its owner role, and the three passwords (kept only
#     in /etc/chisballot, readable by root)
#   - writes each program's settings file, readable only by the user that needs it
#   - installs the service files, the deploy script, the backup, and the one sudo
#     rule that lets the production runner call the deploy script
set -eu

HERE=$(cd "$(dirname "$0")" && pwd)

if [ "$(id -u)" -ne 0 ]; then
    echo "run as root" >&2
    exit 1
fi

export DEBIAN_FRONTEND=noninteractive
apt-get install -y -qq rsync sqlite3 age curl openssl >/dev/null

for user in chisballot-vote chisballot-admin chisballot-migrate; do
    id "$user" >/dev/null 2>&1 || useradd --system --home-dir /nonexistent --shell /usr/sbin/nologin "$user"
done
id cloudflared >/dev/null 2>&1 || useradd --system --home-dir /nonexistent --shell /usr/sbin/nologin cloudflared

install -d -m 755 /opt/chisballot /opt/chisballot/vote /opt/chisballot/admin /opt/chisballot/migrator
install -d -m 755 /etc/chisballot
install -d -o chisballot-admin -g chisballot-admin -m 700 /var/lib/chisballot-admin /var/lib/chisballot-admin/keys

# Passwords: made once, kept in one root-only file.
if [ ! -f /etc/chisballot/secrets ]; then
    umask 077
    make_password() { openssl rand -base64 48 | tr -d '/+=\n' | cut -c1-40; }
    {
        echo "OWNER_PASSWORD=$(make_password)"
        echo "VOTE_PASSWORD=$(make_password)"
        echo "ADMIN_PASSWORD=$(make_password)"
    } > /etc/chisballot/secrets
fi
chmod 600 /etc/chisballot/secrets
# shellcheck disable=SC1091
. /etc/chisballot/secrets

# The database and its owner. The owner can create roles (the migrator makes the
# two site roles) but is not a superuser. Passwords are letters and digits only.
runuser -u postgres -- psql -v ON_ERROR_STOP=1 -q <<SQL
SELECT 'CREATE ROLE chisballot_owner LOGIN CREATEROLE PASSWORD ''$OWNER_PASSWORD'''
WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'chisballot_owner') \gexec
ALTER ROLE chisballot_owner PASSWORD '$OWNER_PASSWORD';
SELECT 'CREATE DATABASE chisballot OWNER chisballot_owner'
WHERE NOT EXISTS (SELECT 1 FROM pg_database WHERE datname = 'chisballot') \gexec
SQL

CONNECTION="Host=127.0.0.1;Database=chisballot"

write_settings() {
    file=$1
    group=$2
    umask 077
    cat > "$file"
    chown root:"$group" "$file"
    chmod 640 "$file"
}

write_settings /etc/chisballot/vote.env chisballot-vote <<EOF
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://127.0.0.1:5101
DatabaseConnectionString=$CONNECTION;Username=chisballot_vote;Password=$VOTE_PASSWORD
EOF

write_settings /etc/chisballot/admin.env chisballot-admin <<EOF
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://127.0.0.1:5102
DatabaseConnectionString=$CONNECTION;Username=chisballot_admin;Password=$ADMIN_PASSWORD
IdentityDatabasePath=/var/lib/chisballot-admin/identity.db
DataProtectionKeyPath=/var/lib/chisballot-admin/keys
PublicUrl=https://admin.chisballot.org
VoteSiteUrl=https://vote.chisballot.org
EOF

write_settings /etc/chisballot/owner.env chisballot-migrate <<EOF
ConnectionStrings__chisballot=$CONNECTION;Username=chisballot_owner;Password=$OWNER_PASSWORD
ChisBallot__VotePassword=$VOTE_PASSWORD
ChisBallot__AdminPassword=$ADMIN_PASSWORD
EOF

install -d -m 755 /usr/local/lib/chisballot
install -m 644 "$HERE/chisballot-lib.sh" /usr/local/lib/chisballot/chisballot-lib.sh
install -m 755 "$HERE/chisballot-deploy" /usr/local/sbin/chisballot-deploy
install -m 755 "$HERE/chisballot-rollback" /usr/local/sbin/chisballot-rollback
install -d -m 700 /var/lib/chisballot-deploy
install -m 755 "$HERE/chisballot-backup" /usr/local/sbin/chisballot-backup
for unit in chisballot-vote.service chisballot-admin.service chisballot-backup.service chisballot-backup.timer cloudflared.service; do
    install -m 644 "$HERE/$unit" "/etc/systemd/system/$unit"
done

# The production runner may run the deploy script as root, and nothing else.
SUDOERS=/etc/sudoers.d/chisballot-deploy
echo 'runner-prod ALL=(root) NOPASSWD: /usr/local/sbin/chisballot-deploy' > "$SUDOERS.new"
visudo -cf "$SUDOERS.new" >/dev/null
chmod 440 "$SUDOERS.new"
mv "$SUDOERS.new" "$SUDOERS"

# The machine's own (root) Docker must not listen on the network. Some container
# templates switch on an unauthenticated TCP API; anyone who can reach it is root.
if [ -f /etc/docker/daemon.json ] && grep -q 'tcp://' /etc/docker/daemon.json; then
    cp /etc/docker/daemon.json /etc/docker/daemon.json.bak
    printf '%s
' '{' '  "log-driver": "journald",' '  "hosts": ["unix:///var/run/docker.sock"]' '}' > /etc/docker/daemon.json
    systemctl restart docker
fi

# Firewall rule for the pull-request user (see chisballot-pr.nft). The system's
# own nftables file loads everything in /etc/nftables.d.
install -d -m 755 /etc/nftables.d
install -m 644 "$HERE/chisballot-pr.nft" /etc/nftables.d/chisballot-pr.nft
grep -q 'include "/etc/nftables.d/\*.nft"' /etc/nftables.conf || echo 'include "/etc/nftables.d/*.nft"' >> /etc/nftables.conf
nft -c -f /etc/nftables.conf
systemctl enable nftables >/dev/null 2>&1
systemctl restart nftables

install -d -m 755 /var/backups/chisballot 2>/dev/null || true

systemctl daemon-reload
systemctl enable chisballot-vote.service chisballot-admin.service >/dev/null 2>&1
echo "Server is ready. The sites start on the first deploy."
echo "Backups: put the age public key in /etc/chisballot/backup-recipient.txt, then: systemctl enable --now chisballot-backup.timer"
