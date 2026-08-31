# WebSheets

Website for serving compiled LaTeX worksheets, plus the service that generates
their solutions.

Worksheet source lives in a separate repository,
[Matthews_Mathematics](https://github.com/Matthew-Holmes/Matthews_Mathematics).
Pushing there triggers a GitHub Actions workflow that compiles the changed `.tex`
files and uploads hash-suffixed PDFs to a Garage object store. This repository
holds the two .NET services either side of that: the site that presents the PDFs,
and the service that writes new LaTeX source back into the content repository.

| Project | What it is |
| --- | --- |
| `WebSheets` | The public Blazor site. Reads `manifest.txt` from the object store and presents a browsable tree of worksheets. |
| `SyntheticPDFs` | Generates worked solutions and answer keys with an LLM, then commits them to the content repository. Listens on localhost only. |
| `Agents` | LLM client library. Currently wraps the DeepSeek chat API. |
| `Shared` | Types passed between `WebSheets` and `SyntheticPDFs`. |

## Configuration and secrets

Everything that varies between machines — URLs, ports, file paths — lives in
`appsettings.json` and is safe to commit. **Secret values never go in
`appsettings.json`**: in development they go in user secrets or a gitignored
file, and in production they come from the environment.

Note the distinction for keys held on disk: the *path* to a key file is ordinary
configuration and is committed; the *file it points at* is the secret and is not.

### Required secrets

| Secret | Used by | Purpose |
| --- | --- | --- |
| `SyntheticPdfsTrigger:ApiKey` | WebSheets | Shared secret callers must present to trigger a generation run. |
| DeepSeek API key file | SyntheticPDFs | Contents are sent as the DeepSeek bearer token. Located by `LLM:DeepSeekAPIKeyFile`. |
| SSH private key | SyntheticPDFs | Pushes generated source to the content repository. Located by `ContentRepository:SshKeyPath`. |
| `AWS_ACCESS_KEY_ID` / `AWS_SECRET_ACCESS_KEY` | Content repo Actions | Uploads compiled PDFs to Garage. Set as GitHub Actions secrets on `Matthews_Mathematics`, not here. |

### WebSheets

Only `WebSheets` has a `UserSecretsId`, so `dotnet user-secrets` works there and
nowhere else in this solution.

**Development** — set it once per machine:

```bash
cd WebSheets
dotnet user-secrets set "SyntheticPdfsTrigger:ApiKey" "<random hex string>"
```

**Check** what is currently set:

```bash
dotnet user-secrets list --project WebSheets
```

**Production** — supply it as an environment variable. The `__` separator maps
to the `:` in the configuration key:

```bash
SyntheticPdfsTrigger__ApiKey=...
```

**Check** it arrived, whatever the host: the app logs a warning at startup if
the trigger key is missing —

```
warn: Program[0]
      SyntheticPdfsTrigger:ApiKey is not configured - the Synthetic PDF trigger will reject every request
```

No warning means the key was picked up.

The site needs no object store credentials at all: `manifest.txt` and the PDFs
are both served by the anonymous public endpoint, so the site reads the manifest
over plain HTTP. Browsing to `/browse` and seeing a populated tree, alongside a
`refreshed cached tree` log line, confirms it can reach the object store.

### SyntheticPDFs

This project has no `UserSecretsId`, so its two secrets are files on disk located
by configured paths.

**DeepSeek API key.** `LLM:DeepSeekAPIKeyFile` holds the *path* to a file, not the
key itself (default `run/secrets/DeepSeekAPIKey.txt`, relative to the working
directory). Create it with the key as its only contents:

```bash
mkdir -p SyntheticPDFs/run/secrets
echo -n "<deepseek api key>" > SyntheticPDFs/run/secrets/DeepSeekAPIKey.txt
```

That path is gitignored. To point elsewhere, override `LLM__DeepSeekAPIKeyFile`.

**Check**: the service validates this at startup and refuses to start otherwise,
so a clean boot is the check. A missing file throws `DeepSeek API key file not
found: <path>`, and an empty one throws `Deepseek API key file is empty`.

**SSH deploy key.** `ContentRepository:SshKeyPath` holds the path to the private
half of a key with write access to the content repository. Register the public
half as a deploy key on `Matthews_Mathematics`.

This path is resolved by the shell that runs the git commands, not by .NET. On
Windows that shell is WSL, so the development value is a *WSL* path rather than a
Windows one — which is why the two environments set it separately:

| Environment | File | Value |
| --- | --- | --- |
| Production (Linux) | `appsettings.json` | `/root/.ssh/id_ed25519` |
| Development (Windows/WSL) | `appsettings.Development.json` | `/home/matt/root/.ssh/id_ed25519` |

**Check** the key is accepted:

```bash
ssh -T git@github.com -i /root/.ssh/id_ed25519                     # Linux
wsl -e ssh -T git@github.com -i /home/matt/root/.ssh/id_ed25519    # Windows
```

A greeting naming the account means the key works; `Permission denied
(publickey)` means it does not.

### Non-secret settings

All committed, all overridable by environment variable using `__` for `:`.

**WebSheets** — `WebSheets/appsettings.json`

| Setting | Purpose |
| --- | --- |
| `WorksheetSource:PublicDownloadBaseUrl` | Public website listener. Serves `manifest.txt` and the PDF download links. |
| `WorksheetSource:GitHubRepoUrl` | Content repository, for the "source" links beside each PDF. |
| `WorksheetSource:LatexSourcePath` | Folder within that repository holding the `.tex` files. |
| `SyntheticPdfsTrigger:BaseUrl` | Where the generation service listens. Must agree with `Api:Port` below. |

**SyntheticPDFs** — `SyntheticPDFs/appsettings.json`

| Setting | Purpose |
| --- | --- |
| `ContentRepository:CloneUrl` | HTTPS URL used for the initial clone. |
| `ContentRepository:PushUrl` | SSH URL the origin remote is repointed at before pulling or pushing. |
| `ContentRepository:LocalDirectory` | Directory the repository is cloned into, relative to the working directory. |
| `ContentRepository:SourceDirectory` | Folder within the repository holding the `.tex` files. |
| `ContentRepository:SshKeyPath` | Deploy key, as described above. |
| `LLM:DeepSeekAPIKeyFile` | Path to the file holding the DeepSeek key. |
| `Generation:MaxFilesPerRun` | Ceiling on files generated in one pass. Halved on each git conflict, restored on success. Keep it low for a first run against a live repository. |
| `Api:Port` | Loopback port the service listens on. Must agree with `SyntheticPdfsTrigger:BaseUrl`. |

Every `ContentRepository` value is validated at startup; a blank one stops the
service with `ContentRepository:<Name> is not configured`.

## Migrating to the configured paths

These values used to be hardcoded in `GitRepoManager`, `GitRepoManager.GitActions`
and the two `Program.cs` files. **No new secret values are needed** — this change
moved paths and URLs, not secrets, and the committed defaults reproduce exactly
what the code did before. Existing key files and deploy keys keep working
untouched.

What to check when you first deploy this:

1. **Production SSH key path.** `appsettings.json` says `/root/.ssh/id_ed25519`,
   which is what the Linux branch of the old ternary used. If the deploy host
   keeps it elsewhere, set `ContentRepository__SshKeyPath` in the environment
   rather than editing the committed file.
2. **Development SSH key path.** `appsettings.Development.json` says
   `/home/matt/root/.ssh/id_ed25519`, the old Windows branch. Change it if your
   WSL user differs.
3. **The two ports must agree.** `Api:Port` in SyntheticPDFs and
   `SyntheticPdfsTrigger:BaseUrl` in WebSheets are set independently now, so
   changing one without the other breaks the trigger.
4. **If you change `LocalDirectory`,** update `.gitignore` — it currently pins
   the old name, `/SyntheticPDFs/Matthews_Mathematics/`.

The clone is now `git clone <url> <dir>`, so `LocalDirectory` no longer has to
match the repository name in `CloneUrl`.

## Triggering a generation run

> **This does real work.** A run pulls the content repository, deletes source it
> considers stale, and pushes the result — which in turn causes the CI to delete
> the corresponding PDFs from the object store. It is not a health check. Point a
> test instance at a scratch repository with `ContentRepository__CloneUrl` and
> `ContentRepository__PushUrl` before pinging it.

`POST /api/public/syntheticPDFs/ping` on the site starts a run, and requires the
trigger key in the `X-WebSheets-Trigger-Key` header:

```bash
curl -X POST https://matthewsmathematics.uk/api/public/syntheticPDFs/ping \
  -H "Content-Type: application/json" \
  -H "X-WebSheets-Trigger-Key: <key>" \
  -d "{}"
```

Without a valid key the endpoint returns `401` and logs the caller's IP. If no
key is configured server-side it rejects every request, so a missing environment
variable fails closed rather than leaving the endpoint open.

### What one trigger does

A pass advances each worksheet by a single step — question sheet → worked
solutions → answer key — because the answer key is derived from the worked
solutions and so cannot be written in the same commit. A pass that commits
anything queues another automatically, so **one trigger runs the repository to
completion** and stops when there is nothing left to generate.

Passes report one of five outcomes, which decide what happens next:

| Outcome | Meaning | Next |
| --- | --- | --- |
| `RemovedStaleFiles` | Source that no longer matches its parent was deleted | Queue another pass |
| `Generated` | New source was committed and pushed | Queue another pass |
| `NothingToDo` | Everything is present and causally ordered | Stop |
| `GitConflict` | The repository moved under us, so the commit was refused | Halve the batch size, wait 30s, retry |
| `GenerationFailed` | Nothing in the batch produced valid LaTeX | Stop, rather than burn API calls on the same failure |

A single file that fails to generate is logged and skipped; the rest of the batch
still commits, and the failed one is picked up on a later pass because the
repository model still shows it missing.

### What a pass chooses to do

A pass does one kind of work: the most urgent kind there is anything of. English
source is finished across the whole repository before any vocabulary key is
written, and every vocabulary key before any translation.

| Priority | Work |
| --- | --- |
| 1 | Anything asked for explicitly, oldest request first |
| 2 | English worked solutions and answer keys |
| 3 | English tier 3 vocabulary keys |
| 4 | Translated vocabulary keys |
| 5 | Translated sheets |

This is a gate rather than a preference. A vocabulary key is derived from
finished English source and a translation from a finished key, so doing a little
of each would mean writing files from parents that are about to change.

## Translated sheets for EAL learners

Each sheet can have a **tier 3 vocabulary key** — the subject specific words it
uses, with definitions — and from that, translations of the sheet itself into
any configured language, either as parallel text or with only the tier 3 words
glossed. These are configured under `L2` in `SyntheticPDFs/appsettings.json`.

| Setting | Purpose |
| --- | --- |
| `L2:GenerateVocabularyKeys` | Whether to build vocabulary keys at all. Off unless set, since each one costs an API call. |
| `L2:Colours` | Tier 3, English and translation colours, as RGB with a name. Recorded in every generated file. |
| `L2:Languages` | Keyed by ISO 639-3 code; each needs a `Font`, a `BabelName` and `RightToLeft`. |
| `L2:EagerLanguages` | Which of them are generated without being asked. Sheets only — worked solutions and answers are generated on request. |
| `ContentRepository:DictionaryPath` | Where the shared definitions live *in the content repository*. |

The shared definitions are deliberately not in settings. They live in the content
repository as `latex/dictionary/mathematicalDictionary.tex`, one
`\dictentry{word}{meaning}` per line, so a wording that reads badly to a teacher
can be changed by a commit that can be discussed — and the same file compiles to
a dictionary worth having on its own. Word forms are matched to their headword,
so a sheet saying `numerators` or `Vertices` gets the agreed wording. Rewording
an entry rebuilds the vocabulary keys that use **that word** and no others.

Generated files carry a provenance comment naming what they were built from.
The generator compares it against the current settings each pass and rebuilds
the file when they differ, so editing a colour or a shared definition rebuilds
only what it actually affects. Everything below that block is safe to edit by
hand — an edit makes the file younger than its parents, so it is left alone.

Two things to know before turning this on. The content repository needs fonts
for the scripts involved before any of it will compile — see
[docs/content-repo-translation-setup.md](docs/content-repo-translation-setup.md).
And the eager set is large: 36 roots with six languages is around 680 files.

### Finding them on the site

A sheet and everything derived from it take **one line** in `/browse`, with the rest
behind a menu that opens on hover — worked solutions, answers, glossary, a link to
the EAL page, and the source on GitHub. Hovering one of the derived files opens a
second level with the source for that file. Clicking the sheet name still goes
straight to its PDF, as it always did.

The menus are CSS only. This is a Blazor Server app, so a hover handled in C#
would be a round trip to the server for every mouse movement. They open on focus
as well as hover, so they can be reached by keyboard.

A sheet's translations live in a folder named after the sheet, which is hidden
from the listing — those files belong to the sheet beside it and are reached
through its menu.

The shared dictionary is the exception: it belongs to the repository rather than
to any sheet, so each language's copy gets a line of its own in `/browse/dictionary`,
named for whoever is looking for it — **Polish Dictionary**, not
`mathematicalDictionary_polish`.

`/eal/<path to sheet>` lists every translated form for a chosen language, with the
ones that have not been written yet greyed out rather than left out — an absence
and a language we do not do at all should not look the same. Clicking one asks for
confirmation and then queues it, along with anything it is derived from.

That request is unauthenticated by design: the generator listens on loopback, so
the site is the only way in, and the worst a flood can do is spend tokens. If that
becomes a problem the fix is a login on the site, not a key in the browser.

The site keeps no language list of its own. It reads `GET /languages` from the
generator, so it can only ever offer a language that would actually work. If the
generator is not answering, browsing carries on without the translated variants.

### Asking for one file

Most combinations are generated only when asked for. This queues the file and
everything it is derived from, ahead of whatever the pipeline would have chosen:

```bash
curl -X POST https://matthewsmathematics.uk/api/public/syntheticPDFs/generate \
  -H "Content-Type: application/json" \
  -H "X-WebSheets-Trigger-Key: <key>" \
  -d '{"rootName":"latex/worksheets/algebra/quadratics",
       "language":"urd","type":"WorkedSolutions","rendition":"Tier3Only"}'
```

`type` is `Root`, `WorkedSolutions` or `Solutions`; `rendition` is
`ParallelText` or `Tier3Only`. The response lists every file queued, in the
order they will be made. A request naming a language with no entry in
`L2:Languages`, or a file its archetype cannot have, comes back as `400` saying
which.

A file made this way is maintained only while it lasts: when its English parent
is edited it goes stale, is removed, and is **not** rebuilt until asked for
again.

### Starting again after a settings rework

```bash
curl -X POST https://matthewsmathematics.uk/api/public/syntheticPDFs/l2/purge \
  -H "Content-Type: application/json" \
  -H "X-WebSheets-Trigger-Key: <key>" \
  -d '{"scope":"TranslationsAndVocabulary"}'
```

`Translations` removes the translated files and leaves the English vocabulary
keys; `TranslationsAndVocabulary` removes those too, which is what a change to
the shared glossary wants. Either way the English source is untouched, and
whatever was eager is rebuilt on the passes that follow.

## Tests

```bash
dotnet test SyntheticPDFs.Tests
```

`SyntheticPDFs.Tests` (MSTest) covers the naming convention, the staleness rules,
batch selection, git-log parsing, the LaTeX validator, and full orchestrator
passes. `FakeGitRepoManager` and `FakeLLMService` in `SyntheticPDFs.Tests/Fakes/`
stand in for git and the LLM, so the suite touches no network and no repository
and runs in well under a second.

The suite checks the LaTeX the generator *writes*, not that it compiles. For the
translated (EAL) sheets that second question is answered by hand, because most
of the ways they fail produce a clean log and a wrong page. See
[docs/translated-sheet-latex.md](docs/translated-sheet-latex.md) for the
constraints, a probe document, and how to diagnose a language whose script will
not build.

Those sheets also need a one-off change to the content repository before any of
them will compile — fonts for the scripts involved, which the build image does
not carry. [docs/content-repo-translation-setup.md](docs/content-repo-translation-setup.md)
has the steps.

## Running locally

```bash
dotnet build WebSheets.sln
dotnet run --project WebSheets        # site, http://localhost:5008
dotnet run --project SyntheticPDFs    # generator, http://localhost:5432
```

`SyntheticPDFs` binds to loopback only by design — reach it through the site's
trigger endpoint rather than directly. On startup it deletes and re-clones the
content repository into `ContentRepository:LocalDirectory`. On Windows its git
commands run through WSL, so a working WSL install with `git` is required for
local development.

## Deploying

Both services run as containers on the droplet, behind the same nginx that was
always there. A deploy builds them here, pushes them to GitHub's container
registry, and tells the droplet to pull and restart:

```bash
./docs/deploy/deploy.sh
```

[docs/deploy/README.md](docs/deploy/README.md) is the step-by-step guide,
including the one-off setup each machine needs and what to do when the deploy
complains. [docs/deploy/how-it-works.md](docs/deploy/how-it-works.md) explains
the machinery behind it — images, registries, host networking and the rest — for
anyone who has not met Docker before.

The object store is not part of any of this. Garage runs on the droplet as an
ordinary service, and the site reads from it over its public address like any
other client, so deploying never touches it.
