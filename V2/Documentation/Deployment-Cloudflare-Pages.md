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

- **Home branch** (default): `IWSDK` — push = production; PRs targeting it = staging.
  Flow: work on a feature branch → open a PR **into `IWSDK`** (deploys **staging**,
  isolated) → merge into `IWSDK` (deploys **production**). This matches PR #1
  (`claude/…` → `IWSDK`), which deploys to staging.
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

### 4. Point the deployed app at your streaming server (online-editable)
This is **how the client knows which server to connect to** — see
[How the app finds its server](#how-the-app-finds-its-server) for the full order.
The recommended, online-editable way needs no rebuild.

**Exactly where in the dashboard** (the "env vars" you're looking for are called
**Variables and Secrets** in the current UI):

1. Cloudflare dashboard → **Workers & Pages** → click the **`questvisionstream`**
   project (or `questvisionstream-test` for staging).
2. **Settings** tab → **Variables and Secrets** (this is the env-var section —
   *not* "Bindings", which is for KV/R2/D1; *not* "Runtime", which is compat flags).
3. **+ Add** → Type **Plaintext** (not Secret) →
   - **Variable name:** `QVS_SIGNALING_URL`
   - **Value:** `wss://your-host:3000`
   - **Environment:** **Production** (add it again for **Preview** if you want the
     preview deploys to use it; on the `questvisionstream-test` project the stable
     apex is the **Production** env).
4. **Save**. Reload the app — done.

The Pages Function `functions/api/config.js` reads this variable (`context.env
.QVS_SIGNALING_URL`) and returns it at `/api/config`; the app fetches it on
startup. Editing it takes effect on the next page load — **no rebuild or
redeploy**. Use `wss://` (the app is served over HTTPS, so plain `ws://` is
blocked as mixed content). Left unset, the app falls back to a `?server=…` URL
override.

> New variables apply to **future requests** immediately (Functions read them at
> request time). If you had the app open, just reload it.

### 5. (Optional) short links
The summary always shows the **apex URL + QR** (which always work — scanning the
QR is the easiest way onto the Quest). It *also* adds a stable
[is.gd](https://is.gd) short link, **idempotently**:

1. tries to create the custom alias (`is.gd/questvisionstream` /
   `is.gd/questvisionstreamtest`);
2. if is.gd says it already exists, it checks where that code **currently
   resolves** — because the target (the apex) is *stable*, a code that already
   points at our apex is ours from a previous run, so it is **reused** as-is (no
   update needed);
3. only if the alias is taken by *someone else's* target does it fall back to an
   **auto-generated** `is.gd/xxxxx` code (which can't collide);
4. if is.gd itself is unreachable, the short-link row is simply omitted.

So the custom code is created once and reused forever, and you never get a dead or
wrong link. Change the `ALIAS=` values in the *Publish …* steps to pick a
different (available) custom name — once claimed, an is.gd custom code is yours.

### 6. Trigger a deploy
- **Production:** push a `V2/quest-client/**` change to the home branch, or run the
  workflow manually (**Actions → QuestVisionStream WebXR Deploy → Run workflow**).
- **Staging:** open a PR targeting the home branch with a `V2/quest-client/**`
  change.

Open the run's **Summary** for the links + QR.

## How the app finds its server

The WebXR client is a **static** bundle — it has no server baked in by default.
At startup it resolves the signaling URL in this order (first match wins), in
`V2/quest-client/src/config.ts`:

1. **`?server=` URL override** — e.g. `…pages.dev/?server=wss://host:3000`. Highest
   priority; handy for one-off testing.
2. **`/api/config`** — the Pages Function (`functions/api/config.js`) returns the
   project's `QVS_SIGNALING_URL` env var. **This is the online-editable path**
   (step 4): change it in the Cloudflare dashboard, reload the app, done — no
   rebuild. Production and staging each have their own value.
3. **`VITE_SIGNALING_URL`** — baked at build time (set it in the workflow's build
   step if you want a compile-time default). Optional.
4. **`ws://localhost:3000`** — dev fallback.

So the normal setup is: deploy once, then set `QVS_SIGNALING_URL` on each Pages
project. The plain apex URL / QR then "just works" because the app self-configures.

> WebRTC media still needs a reachable server + (for cross-NAT) TURN — see
> `Install-Mac-M2.md`. This only controls **which signaling URL** the client dials.

## Changing the defaults

All in `.github/workflows/quest-client-deploy.yml`:

| To change | Where |
|-----------|-------|
| **Home branch** | `on.pull_request.branches`, `on.push.branches`, and `PROD_BRANCH` in *deploy-production* — change all three together |
| **Project names** | `--project-name=` and the `PROJECT:` env in each *Ensure … project* step, plus the `APEX_URL` in the summary steps |
| **Short codes** | the `ALIAS=` value in each *Publish … short code* step (`questvisionstream` prod / `questvisionstreamtest` staging) |

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
- `V2/quest-client/functions/api/config.js` is a **Cloudflare Pages Function**
  (deployed automatically by `wrangler pages deploy` from the project dir). It is
  not part of the Vite/TypeScript build; it powers the online-editable
  `QVS_SIGNALING_URL`.
