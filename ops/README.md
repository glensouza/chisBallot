# Running chisBallot on a server

Everything the server needs is in this folder, so anyone can read exactly what
runs and rebuild the machine. The sites run on one Debian container (an LXC on a
Proxmox host) in a home lab, behind a Cloudflare tunnel. No ports are open to
the internet.

```
Internet -> Cloudflare (HTTPS, DNS) -> tunnel (outbound from the container)
   vote.chisballot.org   -> voting site   127.0.0.1:5101
   admin.chisballot.org  -> admin site    127.0.0.1:5102
PostgreSQL 17 on the same machine, listening on localhost only
www.chisballot.org      -> GitHub Pages (the landing page)
```

## On the container (`lxc/`)

| File | What it is |
|---|---|
| `setup.sh` | Makes the users, the database and owner role, the passwords (kept only in `/etc/chisballot`, root-readable), each program's settings file, and installs everything below. Safe to re-run; never overwrites the passwords. |
| `chisballot-vote.service`, `chisballot-admin.service` | The two sites, each as its own unprivileged user, sandboxed by systemd. The voting site writes nothing to disk. The admin site writes only its accounts file and sign-in keys. |
| `chisballot-deploy` | Puts a build into service: copies it, applies database changes, switches the `current` links, restarts, checks both sites answer, and goes back if they do not. The production runner may run this as root and nothing else (one sudo rule, made by `setup.sh`). |
| `chisballot-lib.sh`, `chisballot-rollback` | Shared checks, and the rollback: it restores the snapshot a deploy takes (ballot database and admin accounts file) and puts the previous programs back. A deploy refuses to start while a real vote is open. See `docs/migrations.md`. |
| `chisballot-backup`, `.service`, `.timer` | Nightly encrypted backup of the ballot database and the admin accounts file, to an outbox folder. |
| `cloudflared.service` | The tunnel, as its own unprivileged user. |

The tunnel's settings (`/etc/cloudflared/config.yml`) and its credentials are
made once with `cloudflared tunnel create` and are not in this repository.

### The two runners

A GitHub Actions runner for this repository runs on the container as two users:

- `runner-prod` (label `prod`) is used only by the deploy workflow, which needs
  a reviewer's approval (the `production` environment) and runs only for `main`.
  It is the only user with the sudo rule for `chisballot-deploy`.
- `runner-pr` (label `pr`) is for pull-request previews. It has no sudo, runs
  Docker in rootless mode, and a firewall rule stops everything it starts from
  reaching any private network (see the firewall note below).

## On the Proxmox host (`host/`)

The container has no network path to the NAS. It writes its encrypted backups to
a folder on the host; the host copies them to the NAS.

| File | What it is |
|---|---|
| `chisballot-backup-sync`, `.service`, `.timer` | Every 30 minutes, copies new `*.age` and `*.sha256` files from the container's outbox to the NAS. It only adds files, copies no links, and removes NAS copies older than 90 days. |

One-time settings in `/etc/pve/lxc/<id>.conf` for the container:

```
mp0: /srv/chisballot-backup/outbox,mp=/var/backups/chisballot,backup=0
lxc.cgroup2.devices.allow: c 10:200 rwm
lxc.mount.entry: /dev/net/tun dev/net/tun none bind,create=file
lxc.idmap: u 0 100000 196608
lxc.idmap: g 0 100000 196608
```

(`/etc/subuid` and `/etc/subgid` on the host also allow `root:100000:196608`.)
The tun device and the wider ID map are what rootless Docker needs inside an
unprivileged container. The outbox folder is owned by the container's `postgres`
user as the host sees it.

The whole container is also backed up weekly by Proxmox to the NAS, with
`tmpdir: /var/tmp/vzdump` in `/etc/vzdump.conf` (an unprivileged container
cannot be archived from a staging folder on the NAS itself).

## Docker on this machine

Two Dockers exist: the machine's own (root) one, which nothing here uses and which
`setup.sh` makes sure listens only on its local socket, and `runner-pr`'s rootless
one for previews. Some container templates turn on Docker's unauthenticated TCP
API (port 2375); `setup.sh` removes that, because anyone who can reach it is root.

## Firewall note (inside the container)

`/etc/nftables.conf` has a table that applies to the `runner-pr` user only:
internet and the router's DNS allowed, every private address range rejected, and
this machine's own services (database, both sites, the tunnel metrics, SSH, local mail)
rejected.
Pull-request code, which runs as that user, therefore cannot reach the Proxmox
host, the router, the NAS or anything else on the home network, and cannot reach
this machine's PostgreSQL either. The network itself is also separated by a VLAN
with its own firewall rules.

## Deploying

Merging to `main` starts the `Build, test and deploy` workflow. It builds and tests
first. If the change touches the sites or `ops/` and the tests pass, the test job
also publishes what it just built into one compressed bundle (kept one day, deleted
after a successful deploy). The `prod` runner downloads that bundle, checks its
checksum, waits for approval in the `production` environment, then runs
`chisballot-deploy`. Nothing is compiled twice, and the server needs only the
ASP.NET Core runtime, not the SDK. Nothing deploys without passing tests and that
approval. After a successful deploy the script also installs the scripts in `ops/lxc/`
from the same commit (`chisballot-deploy`, `-rollback`, `-backup`, `-lib.sh`), each
checked with `sh -n` first, so they never need installing by hand. The service
units, the sudo rule and the firewall file are not updated that way; change those
by running `setup.sh` again.

The first admin is made on the server:

```
systemd-run --wait --pipe --collect -p User=chisballot-admin   -p EnvironmentFile=/etc/chisballot/admin.env -p ReadWritePaths=/var/lib/chisballot-admin   -p ProtectSystem=strict -p WorkingDirectory=/opt/chisballot/admin/current   /usr/bin/dotnet ChisBallot.Admin.dll create-admin you@example.org
```

It prints a one-time link. Open it, choose a password, then add a passkey or an
authenticator app.

## The demo

Every deploy makes the two demo elections if they are missing: `/e/demo` (open,
the room votes on it) and `/e/demo-tie` (closed, ends in a tie). A deploy never
touches ballots already in them. To start a fresh demo, for example before a
meeting, delete the demos' ballots and slips and make them again (demos only,
never a real election):

```
systemd-run --wait --pipe --collect -p User=chisballot-migrate -p EnvironmentFile=/etc/chisballot/owner.env -p ProtectSystem=strict -p WorkingDirectory=$(readlink -f /opt/chisballot/migrator/current) /usr/bin/dotnet ChisBallot.Migrator.dll --reset-demo
```

## Backups

The backup is encrypted to a public key before it is written, so the files in
the outbox, on the host and on the NAS cannot be read without the private key,
which is not kept on the server. Restore commands are at the top of
`lxc/chisballot-backup`.
