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

Non-secret configuration lives in each project's `appsettings.json` and is safe
to commit. **Secrets never go in `appsettings.json`** — in development they go in
user secrets or a gitignored file, and in production they come from the
environment.

### Required secrets

| Secret | Used by | Purpose |
| --- | --- | --- |
| `ObjectStoreCredentials:AccessKeyId` | WebSheets | Signs S3 requests to Garage to read `manifest.txt`. |
| `ObjectStoreCredentials:SecretAccessKey` | WebSheets | As above. |
| `SyntheticPdfsTrigger:ApiKey` | WebSheets | Shared secret callers must present to trigger a generation run. |
| DeepSeek API key file | SyntheticPDFs | Contents of the file are sent as the DeepSeek bearer token. |
| SSH private key | SyntheticPDFs | Pushes generated source to the content repository. |
| `AWS_ACCESS_KEY_ID` / `AWS_SECRET_ACCESS_KEY` | Content repo Actions | Uploads compiled PDFs to Garage. Set as GitHub Actions secrets on `Matthews_Mathematics`, not here. |

### WebSheets

Only `WebSheets` has a `UserSecretsId`, so `dotnet user-secrets` works there and
nowhere else in this solution.

**Development** — set them once per machine:

```bash
cd WebSheets
dotnet user-secrets set "ObjectStoreCredentials:AccessKeyId"     "<garage access key id>"
dotnet user-secrets set "ObjectStoreCredentials:SecretAccessKey" "<garage secret access key>"
dotnet user-secrets set "SyntheticPdfsTrigger:ApiKey"            "$(openssl rand -hex 32)"
```

**Check** what is currently set:

```bash
dotnet user-secrets list --project WebSheets
```

**Production** — supply the same values as environment variables. The `__`
separator maps to the `:` in the configuration key:

```bash
ObjectStoreCredentials__AccessKeyId=...
ObjectStoreCredentials__SecretAccessKey=...
SyntheticPdfsTrigger__ApiKey=...
```

**Check** they arrived, whatever the host: the app logs a warning at startup if
the trigger key is missing —

```
warn: Program[0]
      SyntheticPdfsTrigger:ApiKey is not configured - the Synthetic PDF trigger will reject every request
```

No warning means the key was picked up. For the object store credentials, browse
to `/browse`: a populated tree and a `refreshed cached tree` log line mean the
credentials work; an empty page with an error logged from `ManifestService` means
they did not.

### SyntheticPDFs

This project has no `UserSecretsId`, so its two secrets are handled as files.

**DeepSeek API key.** `appsettings.json` holds the *path* to a file, not the key
itself, under `LLM:DeepSeekAPIKeyFile` (default `run/secrets/DeepSeekAPIKey.txt`,
relative to the working directory). Create it with the key as its only contents:

```bash
mkdir -p SyntheticPDFs/run/secrets
printf '%s' '<deepseek api key>' > SyntheticPDFs/run/secrets/DeepSeekAPIKey.txt
```

That path is gitignored. To point at a different location, override
`LLM__DeepSeekAPIKeyFile` in the environment.

**Check**: the service validates this at startup and refuses to start otherwise,
so a clean boot is the check. A missing file throws
`DeepSeek API key file not found: <path>`, and an empty one throws
`Deepseek API key file is empty`.

**SSH key.** Pushes to the content repository authenticate with an SSH key whose
path is currently hardcoded in `GitRepoManager.GitActions.cs`:

- Linux: `/root/.ssh/id_ed25519`
- Windows (via WSL): `/home/matt/root/.ssh/id_ed25519`

The public half must be registered as a deploy key with write access on
`Matthews_Mathematics`.

**Check**:

```bash
ssh -T git@github.com -i /root/.ssh/id_ed25519
```

A greeting naming the account means the key is accepted; `Permission denied
(publickey)` means it is not.

## Triggering a generation run

`POST /api/public/syntheticPDFs/ping` on the site starts a run, and requires the
trigger key in the `X-WebSheets-Trigger-Key` header:

```bash
curl -X POST https://matthewsmathematics.uk/api/public/syntheticPDFs/ping \
  -H "Content-Type: application/json" \
  -H "X-WebSheets-Trigger-Key: <key>" \
  -d '{}'
```

Without a valid key the endpoint returns `401` and logs the caller's IP. If no
key is configured server-side it rejects every request, so a missing environment
variable fails closed rather than leaving the endpoint open.

## Running locally

```bash
dotnet build WebSheets.sln
dotnet run --project WebSheets        # site, http://localhost:5008
dotnet run --project SyntheticPDFs    # generator, http://localhost:5432
```

`SyntheticPDFs` binds to localhost only by design — reach it through the site's
trigger endpoint rather than directly. On startup it deletes and re-clones the
content repository into `SyntheticPDFs/Matthews_Mathematics/`, which is
gitignored. On Windows its git commands run through WSL, so a working WSL install
with `git` is required for local development.
