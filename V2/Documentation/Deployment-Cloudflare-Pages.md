# Deploying the WebXR client to Cloudflare Pages

The GitHub Actions workflow
[`.github/workflows/quest-client-deploy.yml`](../../.github/workflows/quest-client-deploy.yml)
builds `V2/quest-client` and deploys the static bundle to Cloudflare Pages, with
per-deploy **summary details** (open link, stable URL, this-build URL) and an
auto-created **short code + QR** for opening on the Quest.

This page is the **preparation checklist** — it does not require the secrets to
exist yet. Until you add them, the build/typecheck still runs on every push/PR;
the deploy steps **skip automatically** (they gate on `CLOUDFLARE_API_TOKEN`).

## How it works

Two **isolated** Cloudflare Pages projects so a PR can never overwrite the live
site:

| | Project | Apex | Deploys when |
|---|---------|------|--------------|
| **Production** | `questvisionstream` | `questvisionstream.pages.dev` | **push** to the home branch (a merged PR) or manual **workflow_dispatch** |
| **Staging** | `questvisionstream-test` | `questvisionstream-test.pages.dev` | **pull_request** targeting the home branch |

- **Home branch** (default): `claude/yolo-streaming-unity-modernize-2stzq2` — push
  = production; PRs against it = staging.
- Projects are **created automatically** on the first deploy (the workflow calls
  the Cloudflare API, idempotently), so you don't pre-create them in the dashboard.
- The workflow only runs when `V2/quest-client/**` (excluding `*.md`/`.vscode`) or
  the workflow file itself changes.

## Preparation checklist

### 1. Get your Cloudflare Account ID
Cloudflare dashboard → **Workers & Pages** (or any zone) → the **Account ID** is in
the right-hand sidebar / URL. Copy it.

### 2. Create a scoped API token
Cloudflare dashboard → **My Profile → API Tokens → Create Token → Create Custom
Token**:
- **Permissions:** `Account` → **Cloudflare Pages** → **Edit**
- **Account Resources:** Include → your account
- Create, then **copy the token** (shown once).

This single permission is enough to create projects and publish deployments. Keep
the scope minimal.

### 3. Add the two GitHub repo secrets
Repo → **Settings → Secrets and variables → Actions → Secrets → New repository
secret**:

| Secret name | Value |
|-------------|-------|
| `CLOUDFLARE_API_TOKEN` | the token from step 2 |
| `CLOUDFLARE_ACCOUNT_ID` | the account id from step 1 |

Names must match **exactly** (the workflow reads `secrets.CLOUDFLARE_API_TOKEN`
and `secrets.CLOUDFLARE_ACCOUNT_ID`).

### 4. (Optional) Bake your signaling server into the share links
Repo → **Settings → Secrets and variables → Actions → Variables → New repository
variable**:

| Variable name | Example value | Effect |
|---------------|---------------|--------|
| `QVS_DEFAULT_SERVER` | `wss://your-host:3000` | The short code/QR target becomes `apex/?server=<value>`, so scanning it connects straight to your server. |

Leave it unset to have links open the app at the apex; then add
`?server=wss://HOST:3000` yourself. (A plain query appended to a short code is
dropped by the redirect, which is why the server is baked into the short-code
**target** — set this variable rather than editing links by hand.)

### 5. (Optional) Reserve the da.gd short codes
The workflow creates these custom [da.gd](https://da.gd) codes on first
production/staging deploy and reuses them afterwards (they stay constant across
releases):

| Code | → target |
|------|----------|
| `da.gd/questvisionstream` | production apex (+ `?server=` if the variable is set) |
| `da.gd/questvisionstreamtest` | staging apex (+ `?server=` if set) |

Custom codes are global, so if one is already taken the run **warns** and reuses
whatever exists; if a reused code points somewhere unexpected the summary flags a
`TARGET MISMATCH` — delete/recreate it or change the code names in the workflow.

### 6. Trigger a deploy
- **Production:** push a `V2/quest-client/**` change to the home branch, or run the
  workflow manually (**Actions → QuestVisionStream WebXR Deploy → Run workflow**).
- **Staging:** open a PR targeting the home branch with a `V2/quest-client/**`
  change.

Open the run's **Summary** for the links + QR.

## Changing the defaults

All in `.github/workflows/quest-client-deploy.yml`:

| To change | Where |
|-----------|-------|
| **Home branch** | `on.pull_request.branches`, `on.push.branches`, and `PROD_BRANCH` in *deploy-production* — change all three together |
| **Project names** | `--project-name=` and the `PROJECT:` env in each *Ensure … project* step, plus the `APEX_URL` in the summary steps |
| **Short codes** | the `shorten … questvisionstream` (prod) and `questvisionstreamtest` (staging) aliases |

## Custom domain (optional)

To serve production from your own domain instead of `*.pages.dev`: Cloudflare
dashboard → **Workers & Pages → questvisionstream → Custom domains → Set up a
domain**. Then update `APEX_URL` in the production summary step so the links/QR
point at it.

## Security notes

- The token is **Pages: Edit** only — it cannot touch DNS, other zones, or
  Workers.
- **Fork PRs** don't receive secrets, so they build/typecheck but don't deploy —
  by design.
- Staging is a **separate project**; a PR can never publish to `questvisionstream`.

## Requirements / assumptions

- The client builds with `npm ci` + `npx vite build` in `V2/quest-client` and
  bundles the file-linked libraries from source (Vite aliases) — no pre-build of
  the sibling packages is needed in CI.
- `V2/quest-client/package-lock.json` is committed (required by `npm ci`).
- Node 22 (pinned in the workflow).
