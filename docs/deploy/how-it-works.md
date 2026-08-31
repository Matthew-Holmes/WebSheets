# How the deployment works

This explains the machinery — what Docker is, what the pieces in this folder
actually do, and why they are arranged this way. [README.md](README.md) is the
guide that tells you what to type; this is the one that tells you what is going
on when you type it.

Nothing here is specific to this project until the second half, so the first
half is worth reading even if you end up deploying something else entirely.

---

## 1. The problem deployment solves

A program that runs on your machine does so because of a large amount of
invisible context: a particular .NET runtime is installed, `git` is on the
`PATH`, some file is in the place the program expects, an environment variable
is set. None of that is in the source code. When you copy the program to a
server and it does not work, it is almost always because one of those invisible
things is different there.

Deployment is the business of making the server's invisible context match, and
of keeping it matching over months when neither you nor the server remembers how
it got that way.

### What you were doing before

The old process was:

1. `dotnet publish` on Windows, producing a folder of compiled files
2. `scp -r` that folder to `/var/www/mmWebsite` on the droplet
3. `systemctl restart` the service that runs it

That works, and for a single service it is honestly fine. Three things about it
get uncomfortable as a project grows:

**The server's context is hand-built and undocumented.** The droplet works
because of a sequence of `apt install` commands you ran once. That sequence
exists only in your notes. If the droplet were lost you would rebuild it from
memory, and it would be subtly different.

**`scp -r` merges, it does not replace.** It copies files over the top of what
is there. If a file existed in the previous release and not in the new one, it
stays — forever. Over enough deploys the live directory is a mixture of several
versions, and nothing tells you which.

**There is no way back.** Once the new files are over the old ones, the old ones
are gone. A bad deploy is fixed by rebuilding the previous commit and copying it
across again, under pressure, with the site down.

The second service made all of this sharper, because `SyntheticPDFs` needs `git`,
`ssh` and `bash` present and working, plus two secret files and a writable
directory. That is a lot more invisible context to reproduce by hand.

---

## 2. Images and containers

This is the central idea, and everything else follows from it.

An **image** is a frozen, complete filesystem: an entire miniature Linux
installation with your program in it, plus the runtime, plus whatever tools it
needs, plus the configuration. It is read-only and it never changes. Ours are
built from Microsoft's official .NET images, which are themselves built on
Debian.

A **container** is one running instance of an image. Starting a container means:
take this frozen filesystem, give a process a view of it as if it were the whole
machine, and run the program inside.

The two words get used loosely, but the distinction is the useful part: *the
image is the thing you build and ship, the container is the thing that runs*.
One image can run as many containers; a container that dies can be replaced from
the same image and be byte-for-byte identical.

### A container is not a virtual machine

This surprises people, and it matters for understanding what you are looking at.

A virtual machine emulates hardware and boots a whole separate operating system,
with its own kernel. A container does not. The process inside a container is an
ordinary Linux process running on the droplet's own kernel — it has simply been
given a different view of the filesystem, and its own set of process IDs, so it
cannot see the rest of the machine.

You can prove this to yourself on the droplet:

```bash
ps aux | grep dotnet
```

The `dotnet` process is right there in the host's process list. There is no
virtual machine and no second kernel. This is why containers start in
milliseconds and cost almost nothing in memory beyond what the program itself
uses — an important property on a small droplet.

### What this buys you

The invisible context from section 1 stops being invisible. It is written down,
in the Dockerfile, and it travels with the program. The image for
`SyntheticPDFs` contains `git` and `ssh` because
[Dockerfile.syntheticpdfs](Dockerfile.syntheticpdfs) says to install them. The
droplet does not need `git` installed at all; if you rebuilt the droplet
tomorrow with nothing but Docker on it, the generator would still work.

---

## 3. Dockerfiles, and why they look like that

A Dockerfile is a recipe for building an image. Each instruction produces a
**layer** — a record of what changed — and the image is the stack of layers.

Look at [Dockerfile.websheets](Dockerfile.websheets). Two things about it are
deliberate and worth understanding, because they are the two things people get
wrong.

### Multi-stage builds

The file mentions two base images:

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build      # compiles
...
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime  # runs
COPY --from=build /app .
```

The SDK image contains a compiler, the NuGet tooling and a great deal else —
roughly a gigabyte. The runtime image contains only what is needed to *run* a
compiled ASP.NET application, a few hundred megabytes.

The build happens in the first, then `COPY --from=build` reaches into it and
takes only the compiled output into the second. Everything else about the first
image is thrown away. The image you ship has no compiler in it, which makes it
smaller to transfer, faster to start, and smaller as a target for anything
malicious.

That is why the droplet never needs the .NET SDK, and — since it never needs to
compile — why it needs no source code either.

### Layer caching, and the odd-looking COPY order

This part looks redundant at first:

```dockerfile
COPY WebSheets/WebSheets.csproj WebSheets/
COPY Shared/Shared.csproj       Shared/
RUN dotnet restore WebSheets/WebSheets.csproj

COPY WebSheets/ WebSheets/
COPY Shared/    Shared/
RUN dotnet publish ... --no-restore
```

Why copy the `.csproj` files, then copy them again as part of the whole folder?

Because Docker caches layers, and reuses a cached layer whenever the instruction
*and its inputs* are unchanged. `dotnet restore` downloads NuGet packages, which
is the slow part, and what it downloads depends only on the `.csproj` files.

By copying just those first, the restore layer's inputs change only when you
actually add or remove a package. Edit a `.cs` file and Docker reuses the cached
restore — the download is skipped entirely. Copy everything first and any source
edit at all invalidates the cache and re-downloads the world.

This is the single most useful Dockerfile habit there is: **put the things that
change rarely near the top, and the things that change constantly near the
bottom.**

### The build context and .dockerignore

When you run a build, Docker does not read your files where they sit. The
directory you point it at — the **build context** — is packaged up and handed to
the Docker engine, which then runs the recipe against that copy.

Our context is the whole repository, because the projects reference `Shared` and
`Agents` and those have to come too.

That makes [.dockerignore](../../.dockerignore) important, and for a reason
beyond speed. Two paths inside `SyntheticPDFs/` must never be sent:

- `run/secrets/` — your DeepSeek API key. Anything sent to the build could end
  up in a layer, and layers get pushed to the registry.
- `Matthews_Mathematics/` — your local clone of the content repository, which is
  large and irrelevant.

It also excludes `bin/` and `obj/`. Those hold build output from *Windows*, and
letting them into a *Linux* build is a good way to get a confusing failure.

---

## 4. Registries and tags

The droplet needs the finished image, and it is too big to sensibly email. A
**registry** is a server that stores images. GHCR (`ghcr.io`) is GitHub's, which
is convenient here because you already have a GitHub account.

An image is named `ghcr.io/matthew-holmes/websheets:a1b2c3`, which is three
parts: the registry, the name, and after the colon, the **tag**.

The deploy script uses the short git commit hash as the tag. That is a small
decision with a large payoff: it means every running container can be traced
back to the exact commit it was built from, and any previous version can be
brought back by name, because it is still sitting in the registry. This is what
makes `--rollback` possible, and why rolling back needs no compile.

Pushing and pulling are also cleverer than they look. Because images are stacks
of layers, and layers are identified by their content, the registry only stores
each layer once and only transfers the ones the other end has not got. Your
second deploy does not re-upload the Debian base or the .NET runtime — they have
not changed. Usually only the layer holding your compiled code moves.

The first push creates the package as **private**, which is why the droplet
needs its own read-only token to pull.

---

## 5. Compose, and describing what should be running

You could start each container with a long `docker run` command listing every
port, file and setting. Nobody does, because that command then lives only in
somebody's shell history.

[docker-compose.yml](docker-compose.yml) is that description written down:
which images to run, what to set, what to mount, what to do when one dies. On
the droplet, `docker compose up -d` reads it and makes reality match — starting
what is not running, replacing what is running the wrong version, leaving alone
anything already correct.

That last property is what makes it safe to run repeatedly. It describes a
destination, not a set of steps.

A few things in it are worth reading closely.

### restart: always

```yaml
restart: always
```

This is Docker doing what `Restart=always` did in your systemd unit: if the
program crashes, start it again. Because the Docker engine is itself a systemd
service that starts at boot, this also covers the droplet rebooting.

### The logging block

```yaml
logging:
  driver: json-file
  options:
    max-size: "10m"
    max-file: "3"
```

Container output is captured by Docker rather than written to
`/var/log/mattsmaths/`. By default that capture is unbounded, and you have
already been bitten once by logs quietly eating a disk — this caps each service
at three files of 10 MB. It matters here because the disk is shared with the
garage object store, which needs the room more.

### Environment variables and the double underscore

```yaml
ContentRepository__SshKeyPath: /run/secrets/deploy_key
```

ASP.NET reads configuration from several places in order — `appsettings.json`
first, then environment variables, which win. The double underscore is how a
nested JSON key is spelled in an environment variable, so this overrides
`ContentRepository: { SshKeyPath: ... }`.

Doing it here rather than editing `appsettings.json` keeps facts about *the
server* in the file that describes the server, and leaves the committed
defaults working for local development.

---

## 6. Networking

Normally each container gets its own private network, and you publish specific
ports to make them reachable. Our compose file does something different:

```yaml
network_mode: host
```

This says: do not give this container a network of its own — let it use the
droplet's, directly. A container that binds `127.0.0.1:5000` binds *the
droplet's* `127.0.0.1:5000`, exactly as a normal program would.

That choice is what makes this migration as small as it is:

- **nginx needs no change.** It proxies to `127.0.0.1:5000` and finds the site
  there, just as it found the old systemd service.
- **The generator stays loopback-only.** `Program.cs` calls
  `ListenLocalhost(5432)`, and with host networking that remains literally true:
  the generator is reachable from the droplet and from nowhere else.
- **The site still reaches it at `localhost:5432`.** No code changed.

The alternative — a private bridge network — is tidier in some ways: the
containers would be isolated from the host's network and would address each
other by service name. But `ListenLocalhost` would then mean *that container's*
loopback, which nothing else can reach, so the generator would have to be
changed to bind `0.0.0.0`. That is a fine change to make one day. It was not
worth making at the same time as changing how everything is deployed.

**One consequence to know about.** Host networking is a Linux feature and
behaves differently under Docker Desktop on Windows, where there is a virtual
machine in the way. The compose file is written for the droplet, and running it
unchanged on your machine will not reproduce what the droplet does. To try an
image locally, run it on its own with an explicit port instead:

```bash
docker run --rm -p 8080:8080 ghcr.io/matthew-holmes/websheets:latest
```

### What is actually exposed

Worth stating plainly, because it is the security-relevant part. The firewall
allows 80, 443 and SSH. nginx holds 80 and 443, terminates TLS, and forwards to
`127.0.0.1:5000`. Ports 5000 and 5432 are bound to loopback, which means the
kernel will not accept a connection to them from off the machine at all. The
generator has no authentication of its own and does not need any: there is no
route to it from outside.

---

## 7. Files, secrets and storage

A container's filesystem comes from its image and is thrown away when the
container is replaced. That is the point — it is how you know a restart gives
you a clean, known state. But some things must outlive it, and some things must
never be in the image at all.

### Bind mounts, for the secrets

```yaml
- type: bind
  source: /etc/mattsmaths/deploy_key
  target: /run/secrets/deploy_key
  read_only: true
```

A **bind mount** makes a path on the droplet appear at a path inside the
container. The key stays a normal file on the host, owned and permissioned by
the host, and the container sees it at `/run/secrets/deploy_key`.

This is how secrets stay out of images. The image is pushed to a registry and
could in principle be pulled by anyone who gets hold of a token; it contains no
keys. The keys live on one server, in one directory, and are handed to the
container as it starts. `read_only: true` means the container cannot modify them
even if something went badly wrong inside it.

`create_host_path: false` turns off a default that would otherwise bite: if the
source path does not exist, Docker helpfully creates it — as a *directory*. The
app would then fail with something about being unable to read a key, which is
several steps removed from the real problem of a file you forgot to create.
Turning it off, plus the preflight check in `deploy.sh`, means a missing secret
is reported as a missing secret.

### A named volume, for the clone

```yaml
- type: volume
  source: generator-work
  target: /var/lib/generator
```

A **volume** is storage that Docker manages, living outside any container and
surviving them all.

The generator deletes and re-clones the content repository at every startup, so
it needs somewhere writable to do that. It should not be inside the image (which
is read-only in spirit and replaced on every deploy) and it should not be in a
directory that a deploy overwrites — the old setup's habit of cloning next to
the deployed files is exactly the kind of overlap that causes odd failures.

A named volume gives it a private working area that no deploy touches. The
contents are disposable — it is a clone, remade at every start — so nothing is
lost if it is deleted.

### Users, and the number 10001

By default a process in a container runs as `root`. Confined, but still a
sharper tool than needed. Both our images end with a `USER` instruction so the
program runs as an unprivileged user.

For the generator, [Dockerfile.syntheticpdfs](Dockerfile.syntheticpdfs) creates
that user with a *specific* number:

```dockerfile
RUN groupadd --gid 10001 generator \
 && useradd  --uid 10001 --gid 10001 ...
```

User names do not cross the container boundary, but user *numbers* do — there is
one set of numbers, shared between host and container, and the kernel checks
permissions using those. So when the container's `generator` user (10001) reads
the bind-mounted key, the host sees a read by user 10001 and applies the file's
ownership accordingly.

That is why the setup instructions say `chown 10001:10001`. And it has to be
right, rather than merely close, because SSH refuses to use a private key that
is readable by anyone but its owner — a rule that has saved a great many people
and is baffling the first time you meet it through two layers of abstraction.

Pinning the number ourselves, rather than relying on whatever the base image
happens to use, means the instructions stay true when the base image changes.

---

## 8. What one deploy actually does

Tying it together. When you run `./docs/deploy/deploy.sh`:

**1. Preflight, changing nothing.** Checks Docker is running here, that SSH to
the droplet works, that Docker is installed there, that the three secret files
exist, and that the deploy key has the right ownership and mode. Anything wrong
stops the deploy before it has done a thing.

**2. Work out a tag.** The short hash of your current commit, plus `-dirty` and
a warning if you have uncommitted changes.

**3. Build, twice.** Your repository is packaged as a build context, minus the
`.dockerignore` entries, and handed to Docker. Each Dockerfile is followed:
restore, compile, copy the output into a clean runtime image. Unchanged layers
come from cache.

**4. Push, twice.** Each image goes to GHCR under two tags — the commit hash and
`latest`. Only layers the registry lacks are transferred.

**5. Copy the compose file over,** and write `/opt/mattsmaths/.env` recording
which tag to run. That file is why a reboot brings back the version you
deployed, rather than whatever `latest` has since become.

**6. Pull and restart.** Over SSH, `docker compose pull` downloads the new
images and `docker compose up -d` replaces the running containers with ones
built from them. The old containers stop; the new ones start. This is the few
seconds of downtime in a deploy.

**7. Tidy up.** Images older than a week are pruned, so a disk shared with the
object store does not slowly fill with superseded versions.

**8. Check.** The generator is asked for `GET /languages` and the site for its
front page, both with retries because a .NET application takes a moment to
start. Both are read-only. Notably absent is `/ping`, which would start a real
generation run — a deploy should not do that by itself.

---

## 9. What is deliberately not containerised

**nginx** stays a normal service on the droplet. It holds the certificates,
knows which hostname is which, and is wired to certbot for renewals. That
arrangement works and containerising it would put the TLS setup at risk for no
gain.

**garage** stays a normal service too. It holds the compiled PDFs — real data,
the only genuinely irreplaceable thing on the droplet. It also runs completely
independently: the site reads from it over its public HTTPS address like any
other client. Leaving it alone was the right call and remains one.

**certbot** stays as it is, for the same reason as nginx.

The general principle: containers are most useful for the things you replace
often, and least useful for the things that hold state and rarely change. The
two .NET services are redeployed constantly and hold nothing. nginx and garage
are the opposite.

---

## 10. If you want to learn more

In roughly the order that will be useful:

- **`docker run`, by hand.** Start something small — `docker run --rm -it
  debian:12 bash` — and look around. It is the fastest way to make containers
  concrete: you are root in a Debian system, on your own machine, and it
  disappears when you exit.
- **Docker's own [getting started guide](https://docs.docker.com/get-started/).**
  Good, and short.
- **`docker compose` documentation** for the file format, once the ideas are
  familiar.
- **Bridge networking**, when you want to see the more conventional setup, and
  as background for the `ListenLocalhost` change described in section 6.
- **Healthchecks**, a Compose feature we do not yet use: Docker can poll an
  endpoint and mark a container unhealthy, which is a tidier version of the
  checks the deploy script currently does from outside.
- **GitHub Actions**, if you later want deploys to happen on push rather than
  when you run a script. The build steps would be nearly identical; the
  difference is where they run and where the SSH key lives.

## Glossary

| Term | Meaning |
| --- | --- |
| **Image** | A frozen filesystem containing a program and everything it needs. Read-only, built once, shipped. |
| **Container** | One running instance of an image. An ordinary process, given its own view of the filesystem. |
| **Layer** | One step of an image's construction. Cached and reused when its inputs have not changed. |
| **Dockerfile** | The recipe for building an image. |
| **Build context** | The directory sent to Docker for a build. Trimmed by `.dockerignore`. |
| **Registry** | A server storing images. GHCR is GitHub's. |
| **Tag** | The label after the colon in an image name. Ours are git commit hashes. |
| **Push / pull** | Upload an image to a registry / download it. |
| **Compose** | The tool that runs several containers from a written description. |
| **Bind mount** | A host path made visible inside a container. How our secrets get in. |
| **Volume** | Docker-managed storage that outlives containers. Where the clone lives. |
| **Host networking** | Letting a container use the host's network directly, rather than a private one. |
| **Daemon / engine** | The background service that actually builds and runs containers. |
