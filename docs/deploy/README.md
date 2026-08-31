# Deploying

This is the step-by-step guide. If you want to understand *why* it is built this
way — what Docker actually is, what an image is, why there are two machines
involved — read [how-it-works.md](how-it-works.md) first, or alongside. This
file assumes nothing and just tells you what to type.

## The short version

Once everything below is set up, deploying is one command, run on **your
machine**, from the repository root, in WSL:

```bash
./docs/deploy/deploy.sh
```

That takes a few minutes and leaves the live site running your latest commit.
Everything else in this document is either the one-off setup that makes that
command work, or what to do when it complains.

## The two machines

Almost every mistake in deployment comes from doing the right thing on the wrong
machine, so it is worth being clear about which is which.

**Your machine** is the Windows box you develop on, and specifically WSL running
on it. It has the source code, Docker Desktop, and the SSH key that gets you
into the server. It does all the *compiling*.

**The droplet** is the Ubuntu server at `188.166.153.242` that the public
internet talks to. It has nginx, the garage object store, and Docker. It does
none of the compiling — it only downloads and runs finished, prepared programs.

```
  YOUR MACHINE (Windows + WSL)                  GHCR - a store for prepared
  +----------------------------+                programs, part of GitHub
  |  the source code           |                +--------------------------+
  |  Docker Desktop            |  1. build and  | websheets:a1b2c3         |
  |  deploy.sh                 |---- upload --->| syntheticpdfs:a1b2c3     |
  |  your SSH key              |                +------------+-------------+
  +-------------+--------------+                             |
                |                                            |
                | 2. over SSH: "fetch the new                | 3. download
                |    version and restart"                    |
                v                                            v
  THE DROPLET (Ubuntu, 188.166.153.242)
  +-----------------------------------------------------------------+
  |                                                                 |
  |  nginx      :80 and :443, the only ports open to the world.     |
  |    |        Holds the certificates and decides which hostname   |
  |    |        is being asked for.                                 |
  |    v                                                            |
  |  WebSheets container       127.0.0.1:5000                       |
  |  the public site           reachable only from the droplet      |
  |    |                                                            |
  |    | asks it for translations, and to start a generation run    |
  |    v                                                            |
  |  SyntheticPDFs container   127.0.0.1:5432                       |
  |  the generator             reachable only from the droplet      |
  |                                                                 |
  |  garage     the object store holding the compiled PDFs.         |
  |             NOT in a container. Nothing here touches it.        |
  +-----------------------------------------------------------------+
```

The site reads the worksheet PDFs over the public
`files.matthewsmathematics.uk` address, which loops back round through nginx to
garage. That is why containerising the two services did not require touching
garage at all — the site was never talking to it directly.

## What has to be in place

A checklist. Each item is explained in the setup sections below.

**On your machine**

- [ ] WSL, with the repository visible from it
- [ ] Docker Desktop installed, running, with WSL integration enabled
- [ ] Logged in to GHCR with a token that can *write* packages
- [ ] The SSH key for the droplet at `~/.ssh/mmPrivKey.ossh`

**On the droplet**

- [ ] Docker installed
- [ ] Logged in to GHCR with a token that can *read* packages
- [ ] `/etc/mattsmaths/mmWebsite.env` — the trigger key
- [ ] `/etc/mattsmaths/deepseek_api_key` — owned by `10001`, mode `600`
- [ ] `/etc/mattsmaths/deploy_key` — owned by `10001`, mode `600`
- [ ] The old `kestrel-mmWebsite.service` stopped
- [ ] nginx and garage running as they already were — no changes needed

`deploy.sh` checks most of this before it changes anything, and stops with a
message naming what is missing. It is not possible to half-deploy by forgetting
one of these.

---

# Part 1 — one-off setup on YOUR machine

## 1.1 Docker Desktop

Install Docker Desktop for Windows, then open Settings → Resources → WSL
Integration and turn it on for your Ubuntu distribution. That last part is what
makes the `docker` command work *inside WSL*, which is where the deploy script
runs.

Check it, in WSL:

```bash
docker version
```

You want output describing both a Client and a Server. If it says it cannot
connect to the daemon, Docker Desktop is not running — start it from the Start
menu and wait for the whale icon to settle.

## 1.2 A GitHub token that can upload images

The built images are stored in GitHub's container registry, GHCR. Uploading to
it needs a token.

On github.com: Settings → Developer settings → Personal access tokens → Tokens
(classic) → Generate new token. Tick **`write:packages`** and nothing else. Copy
the token — GitHub shows it once.

Then, in WSL:

```bash
echo "<the token>" | docker login ghcr.io -u Matthew-Holmes --password-stdin
```

This is remembered, so it is genuinely one-off.

## 1.3 The SSH key for the droplet

You already have this from setting the server up. The script expects it at
`~/.ssh/mmPrivKey.ossh` inside WSL, in OpenSSH format:

```bash
ls -l ~/.ssh/mmPrivKey.ossh
```

If it is not there, copy it across and lock it down as you did originally:

```bash
cp /mnt/c/Users/Matt/Documents/Work/Keys/mmPrivKey.ossh ~/.ssh/mmPrivKey.ossh
chmod 600 ~/.ssh/mmPrivKey.ossh
```

Check it works:

```bash
ssh -i ~/.ssh/mmPrivKey.ossh root@188.166.153.242 "echo connected"
```

---

# Part 2 — one-off setup on THE DROPLET

SSH in first. Everything in this part is typed on the server.

```bash
ssh -i ~/.ssh/mmPrivKey.ossh root@188.166.153.242
```

## 2.1 Install Docker

```bash
curl -fsSL https://get.docker.com | sh
docker --version
docker compose version
```

Both commands should print a version. This installs the Docker *engine* — the
background service that actually runs containers — and starts it under systemd,
so it comes back by itself after a reboot.

## 2.2 Let the droplet download images

Another GitHub token, this one read-only. Same place on github.com, but tick
**`read:packages`** and nothing else.

```bash
echo "<the token>" | docker login ghcr.io -u Matthew-Holmes --password-stdin
```

## 2.3 The three secret files

These live outside the containers, so that a secret is never baked into an image
and never travels to the registry. The containers are told where to find them
when they start.

```bash
sudo mkdir -p /etc/mattsmaths
```

### The trigger key

`/etc/mattsmaths/mmWebsite.env` already exists from the garage migration. Check
it contains `SyntheticPdfsTrigger__ApiKey`:

```bash
sudo cat /etc/mattsmaths/mmWebsite.env
```

If it does not, add the line — [mmWebsite.env.example](mmWebsite.env.example)
shows the shape of the file. Then:

```bash
sudo chown root:root /etc/mattsmaths/mmWebsite.env
sudo chmod 600 /etc/mattsmaths/mmWebsite.env
```

### The DeepSeek API key

The key is the file's entire contents, with no trailing newline — which is what
`printf %s` gives you and `echo` does not:

```bash
sudo sh -c 'printf %s "<deepseek api key>" > /etc/mattsmaths/deepseek_api_key'
sudo chown 10001:10001 /etc/mattsmaths/deepseek_api_key
sudo chmod 600 /etc/mattsmaths/deepseek_api_key
```

### The content repository deploy key

This is the private half of an SSH key registered as a deploy key with write
access on `Matthews_Mathematics`. It is how the generator pushes the LaTeX it
writes.

If it is already on the droplet at `/root/.ssh/id_ed25519`, copy it — do not
move it, in case something else is still using it:

```bash
sudo cp /root/.ssh/id_ed25519 /etc/mattsmaths/deploy_key
sudo chown 10001:10001 /etc/mattsmaths/deploy_key
sudo chmod 600 /etc/mattsmaths/deploy_key
```

**About that `10001`.** Programs inside a container run as a numbered user, and
that number means the same thing to the host. The generator runs as user 10001
inside its container, so from the host's point of view the file has to belong to
10001 for the generator to be able to read it. There is no human user with that
number on the droplet and there does not need to be — it is a label the two
sides agree on.

The mode `600` matters because SSH refuses to use a private key that anyone but
its owner can read. That refusal is a good thing, and a confusing thing to
diagnose from inside a container, which is why `deploy.sh` checks the ownership
and mode up front and tells you plainly if they are wrong.

Verify:

```bash
ls -ln /etc/mattsmaths/
```

`deepseek_api_key` and `deploy_key` should both show `-rw------- 1 10001 10001`.

## 2.4 Stop the old service

The old systemd service and the new container both want port 5000, so they
cannot both run. This is the only moment of downtime in the whole migration.

```bash
sudo systemctl disable --now kestrel-mmWebsite.service
```

Leave the unit file itself alone for now — it is your way back if the containers
misbehave. See [Going back](#going-back) at the end.

## 2.5 nginx and garage

**Do nothing.** This is the point of the host-networking choice: as far as nginx
is concerned the site is still a program listening on `127.0.0.1:5000`, exactly
as the systemd service was. Garage is not involved at all.

There is a snapshot of the server's nginx config in
[nginx/sites-available-default.conf](nginx/sites-available-default.conf) for
reference, so the box can be rebuilt from something other than memory. The
deploy script does not apply it — certbot edits the real file in place, and
overwriting it would undo the certificate wiring.

---

# Part 3 — the first deploy

Back on **your machine**, in WSL, from the repository root:

```bash
cd /mnt/c/Users/Matt/source/repos/WebSheets
./docs/deploy/deploy.sh
```

The first run is the slow one — it downloads the .NET base images, several
hundred megabytes. Later runs reuse them.

You will see it work through five stages:

| Stage | What is happening |
| --- | --- |
| `checking this machine and the droplet` | Confirms Docker is running, SSH works, and all three secrets are in place with the right ownership. Nothing has changed yet. |
| `building and pushing websheets` | Compiles the site inside a container and uploads the result to GHCR. |
| `building and pushing syntheticpdfs` | The same for the generator. |
| `deploying <tag>` | Copies the compose file over, then tells the droplet to download the new images and restart. |
| `checking what came up` | Asks the generator and the site whether they are answering. |

If it ends with `deployed a1b2c3`, you are live.

## What the tag means

`a1b2c3` is the short hash of the commit you deployed. Every deploy is labelled
with the commit it came from, which is what makes rolling back possible.

If your working tree has uncommitted changes the tag gets `-dirty` on the end,
and the script warns you. It still deploys — sometimes you need it to — but no
commit reproduces that image, so a `-dirty` build is not something to leave
running on the live site.

---

# Part 4 — every deploy after this

```bash
./docs/deploy/deploy.sh
```

That is the whole thing. Commit your work first, so the tag means something.

## Rolling back

Tags are commit hashes, so any previous deploy can be brought back without
rebuilding anything — that image is already sitting in the registry:

```bash
./docs/deploy/deploy.sh --rollback a1b2c3
```

To see what is running at the moment, on the droplet:

```bash
cat /opt/mattsmaths/.env
```

Rolling back is quick, because it skips the compile entirely.

## Checking on it

Logs no longer go to `/var/log/mattsmaths/`. Docker keeps them now, capped at
10 MB per service with three files kept, so they cannot quietly fill the disk
the way the systemd journal did. On the droplet:

```bash
cd /opt/mattsmaths

docker compose ps                      # what is running, and since when
docker compose logs -f websheets       # follow the site's log
docker compose logs -f syntheticpdfs   # follow the generator's log
docker compose logs --since 1h         # both, for the last hour
```

`Ctrl-C` stops following; it does not stop the service.

Disk, which is shared with garage and worth an occasional look:

```bash
docker system df
df -h /
```

## Restarting without deploying

```bash
cd /opt/mattsmaths
docker compose restart syntheticpdfs
```

Worth knowing: restarting the generator makes it delete and re-clone the content
repository, which takes a moment. It does not generate anything — nothing
happens until something pings it.

---

# Troubleshooting

The preflight is written so that each failure names its own fix.

| Message | What it means | What to do |
| --- | --- | --- |
| `the docker daemon is not answering` | Docker Desktop is not running on your machine | Start it, wait for the whale icon to settle |
| `no docker on PATH` | WSL cannot see Docker Desktop | Settings → Resources → WSL Integration, enable for Ubuntu |
| `cannot ssh to root@...` | Key missing, wrong permissions, or wrong path | `chmod 600 ~/.ssh/mmPrivKey.ossh`, and check Part 1.3 |
| `docker is not installed on the droplet` | Part 2.1 not done | Do Part 2.1 |
| `/etc/mattsmaths/... is missing` | One of the three secrets is not there | Do Part 2.3 for that file |
| `deploy_key must be owned by uid 10001 with mode 600` | Ownership or permissions wrong | `sudo chown 10001:10001` and `sudo chmod 600` on it |
| `unauthorized` while pushing | Your GHCR token is missing or lacks `write:packages` | Redo Part 1.2 |
| `denied` or `manifest unknown` while pulling | The droplet's token lacks `read:packages` | Redo Part 2.2 |
| `the site did not answer` | The container started but the app failed | `docker compose logs websheets` on the droplet |
| `the generator did not answer` | Usually a secret the app cannot read | `docker compose logs syntheticpdfs` on the droplet |

## A container that keeps restarting

`docker compose ps` showing `Restarting` means the app is starting, failing, and
being started again. The log says why:

```bash
docker compose logs --tail 50 syntheticpdfs
```

The generator checks its configuration at startup and refuses to run rather than
half-working, so the message is usually explicit — a missing key file, an empty
one, or a setting it needs and has not got.

## Port 5000 already in use

The old systemd service came back. Part 2.4:

```bash
sudo systemctl disable --now kestrel-mmWebsite.service
```

---

# Going back

If the containers turn out to be a mistake, the old setup is still there:

```bash
cd /opt/mattsmaths && docker compose down
sudo systemctl enable --now kestrel-mmWebsite.service
sudo systemctl status kestrel-mmWebsite.service
```

That runs the last version you copied across by hand, from
`/var/www/mmWebsite/publish`. Once the containers have run happily for a while,
that directory and the unit file can go.

---

# What the deploy deliberately does not do

After restarting, the script asks the generator for `GET /languages` and the
site for its front page. Both are read-only questions.

It does **not** call `/ping`. That starts a real generation run against the live
content repository — deleting stale source, writing new source, pushing it, and
spending DeepSeek credits. A deploy should not do that on its own. Trigger a run
yourself, when you want one, as described in the repository README.

Starting the generator is safe by itself: it clones the content repository and
then waits.

# Adding a third service later

Add a `Dockerfile.<name>` beside the two here, a service block in
`docker-compose.yml`, and the name to the build loop in `deploy.sh`.
