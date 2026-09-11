# Quotely — Development workflow

How code gets from an idea on your laptop to a customer, safely, without needing a team to
operate it.

If you read one thing, read this:

> **Branches are source control. Environments are deployment targets.**
>
> There is no `test` branch, no `uat` branch and no `prod` branch. Four branches for four
> environments means four-way merges, constant drift, and eventually a fix that exists in UAT but
> not in production. Instead, two long-lived branches carry the code, and an environment is simply
> *which commit is currently deployed where*.

---

## The branches

| Branch | Lives for | Holds | Deploys to |
| --- | --- | --- | --- |
| `main` | forever | production-ready code only | **PROD** |
| `develop` | forever | the next release, integrated | **TEST** |
| `feature/*` | days | one piece of work | nobody — runs on your machine (DEV) |
| `release/vX.Y.Z` | hours or days | a release candidate being validated | **UAT** |
| `hotfix/*` | hours | one emergency production fix | — |

```
feature/v2.5-manual-payments ─┐
feature/v2.5-overdue ─────────┼──► develop ──► release/v2.5.0 ──► main ──► tag v2.5.0
fix/invoice-total ────────────┘      (TEST)         (UAT)         (PROD)
```

### Naming

| Prefix | Use it for | Example |
| --- | --- | --- |
| `feature/` | new functionality | `feature/v2.5-manual-payments` |
| `fix/` | a bug that is not an emergency | `fix/invoice-total` |
| `chore/` | dependencies, config, tooling, docs | `chore/update-dependencies` |
| `release/` | a release candidate | `release/v2.5.0` |
| `hotfix/` | production is broken right now | `hotfix/payment-verification` |

Lowercase, hyphen-separated, short. The branch name should tell you what it is without opening it.

---

## Starting a feature

```bash
git switch develop
git pull
git switch -c feature/v2.5-manual-payments
```

Work, commit as you go. When it is ready:

```bash
git push -u origin feature/v2.5-manual-payments
gh pr create --base develop --fill
```

Note `--base develop`. **A feature branch never targets `main`.** `main` is production; work reaches
it only through a release.

Opening the pull request starts CI automatically. When it is green and you are happy with the
change, merge it. GitHub deletes the branch for you afterwards.

Locally, tidy up:

```bash
git switch develop
git pull
git branch -d feature/v2.5-manual-payments
```

---

## What CI does

`.github/workflows/ci.yml` runs on every push to `main`, `develop`, `release/**` and `hotfix/**`,
and on every pull request into `main` or `develop`.

**Backend** — `dotnet restore` → `dotnet build` → `dotnet test` (256 tests)
**Frontend** — `npm ci` → `npm run typecheck` → `npm run lint` → `npm run build`

Both must pass before you merge. That is the whole quality gate, and it is enough: the backend
suite boots the real API against in-memory SQLite and a stand-in payment provider, so payment
signature handling is genuinely exercised.

**CI uses no secrets, by design.** It never touches Razorpay, never touches a real database, and
needs no credentials to run. Nothing sensitive can leak from a workflow log because nothing
sensitive is there.

### CI is not CD

| | What it means | Do we have it? |
| --- | --- | --- |
| **CI** — Continuous Integration | Build, test, lint, typecheck. Proves the code is sound. | **Yes, working today.** |
| **CD** — Continuous Deployment | Ship validated code to a running environment. | **No.** No hosting provider has been selected, so there is nowhere to deploy. |

Nothing in this repository deploys anything. When hosting is chosen, CD attaches at exactly two
points — see [Connecting CD later](#connecting-cd-later).

---

## The environments

```
DEV      your laptop                     ← you, right now
  ↓      merge into develop
TEST     shared integration              ← whatever is on develop
  ↓      cut release/vX.Y.Z
UAT      acceptance / staging            ← the release candidate
  ↓      merge into main, tag it
PROD     real customers                  ← only a tagged release from main
```

**DEV** is your machine: SQLite, seeded demo data, Razorpay Test Mode.
**TEST** is shared QA: whatever `develop` holds. Messy data is expected.
**UAT** is the rehearsal: configured exactly like production, but Razorpay stays in **Test Mode**
so acceptance testing cannot move real money.
**PROD** is real customers, real money, Razorpay **Live Mode** — and only ever a tagged commit
from `main`.

Configuration, secrets and databases for each are in
**[docs/environments.md](environments.md)**. The short version: every environment gets its own
database, its own JWT key and its own Razorpay credentials, and none of them are ever shared or
committed.

---

## Weekly releases

A weekly **cadence**, not an obligation. If a week produced nothing worth shipping, skip it — an
empty release is worse than no release.

`develop` is never merged into `main` automatically. A release is a decision someone makes.

### Cutting a release

**1. Branch from develop**

```bash
git switch develop
git pull
git switch -c release/v2.5.0
git push -u origin release/v2.5.0
```

CI runs on the push. This branch is what UAT deploys, once UAT exists.

**2. Open the release pull request**

```bash
gh pr create --base main --title "Release v2.5.0" --fill
```

> ⚠️ **Always open this from a `release/*` branch, never from `develop` itself.** This repository
> has `delete_branch_on_merge` enabled, which deletes a pull request's head branch once it merges.
> Point a pull request from `develop` straight at `main` and GitHub will delete `develop` when you
> merge it. (Ask how this warning came to be written.) A `release/*` branch is disposable, which is
> exactly what you want the head of a release pull request to be.

**3. Review it properly.** This is the moment to read the whole diff since the last release —
`git log --oneline v2.4.0..release/v2.5.0` — and ask what could go wrong in production. Check
whether it contains an EF migration.

**4. Validate in UAT** (once UAT exists). Business sign-off happens here.

**5. Merge into main** when CI is green and you are satisfied.

**6. Tag the merge commit**

```bash
git switch main
git pull
git tag -a v2.5.0 -m "v2.5.0 — manual payments"
git push origin v2.5.0
```

Pushing the tag triggers `.github/workflows/release.yml`, which re-runs the full CI pipeline
against the tagged tree, confirms the tag is actually on `main`, and publishes a GitHub Release
with generated notes. **It does not deploy.**

**7. Merge main back into develop**, so nothing that happened during the release is lost.

This goes through a pull request like everything else — `develop` is covered by the same push
guard, and a back-merge deserves the same CI run:

```bash
git switch -c chore/backmerge-v2.5.0 main
git push -u origin chore/backmerge-v2.5.0
gh pr create --base develop --title "Back-merge v2.5.0 into develop" --fill
```

Skip it if the release branch never gained commits of its own — there is then nothing to bring
back.

**8. Delete the release branch.**

---

## Version numbers

`vMAJOR.MINOR.PATCH`. You choose them; nothing generates them.

| Change | Bump | Example |
| --- | --- | --- |
| New feature milestone | **minor** | `v2.4.0` → `v2.5.0` |
| Bug fix or security fix | **patch** | `v2.5.0` → `v2.5.1` |
| Breaking or major product milestone | **major** | `v2.9.0` → `v3.0.0` |

A release candidate you want on GitHub before it is final can carry a suffix — `v2.5.0-rc.1` —
and is published as a pre-release rather than as the latest version.

The current baseline is **V2.4**, which predates this process and is untagged. The first tag will
be whatever the next release earns.

---

## Hotfixes

Production is broken and it cannot wait for the next release.

```bash
git switch main
git pull
git switch -c hotfix/payment-verification
# fix it, as small as you can make it
git push -u origin hotfix/payment-verification
gh pr create --base main --fill
```

Branch from `main`, not `develop` — `develop` may contain unreleased work you do not want in
production right now.

Then: CI must pass (**never bypass it, however urgent** — that is exactly when mistakes happen),
merge into `main`, and tag a **patch** version (`v2.5.1`).

Finally, bring the fix back into `develop`, again through a pull request:

```bash
git switch -c chore/backmerge-v2.5.1 main
git push -u origin chore/backmerge-v2.5.1
gh pr create --base develop --title "Back-merge v2.5.1 into develop" --fill
```

Forgetting this step is the classic hotfix bug: production gets fixed, then the next release
quietly ships the old broken code over the top.

---

## Branch protection — current limitation

**GitHub branch protection is not active on this repository, and it cannot be.**

Branch protection rules and rulesets are a **paid feature for private repositories**. This account
is on the GitHub Free plan, so both the classic protection API and the rulesets API refuse with
`403: Upgrade to GitHub Pro or make this repository public`. The same limitation disables GitHub's
auto-merge.

Rather than pretend, here is exactly what is and is not enforced:

| Intended rule | Status |
| --- | --- |
| `main`: direct pushes disabled | ⚠️ **Local hook only** — not enforced server-side |
| `main`: changes require a pull request | ⚠️ **Convention + local hook** |
| `main`: CI must pass before merge | ⚠️ **CI runs and reports; the merge is not blocked** |
| `main`: force pushes disabled | ❌ Not enforceable on this plan |
| `main`: branch deletion disabled | ❌ Not enforceable on this plan |
| `develop`: same as above | ⚠️ Same |
| Feature branch deleted after merge | ✅ **Enabled** (`delete_branch_on_merge`) |
| Rebase-merge disabled (fewer footguns) | ✅ **Enabled** — squash for features, merge commit for releases |

### What is actually protecting you today

A **pre-push hook** in `.githooks/pre-push` refuses a direct push to `main` or `develop`. Enable it
once per clone:

```bash
git config core.hooksPath .githooks
```

It is already enabled on the machine this was set up on. It allows tag pushes (that is how a
release is cut), and has a deliberate escape hatch:

```bash
ALLOW_DIRECT_PUSH=1 git push origin main
```

Be honest about what this is: a hook runs on *your* machine. It stops the tired-at-11pm mistake. It
does not stop a push from a different clone, from the GitHub web UI, or from a second developer.

### Turning on real protection

Two options, whenever you want it:

1. **GitHub Pro** — around $4/month, unlocks protection on private repositories. This is the
   proportionate fix.
2. **Make the repository public** — free, but Quotely is a commercial product. Not recommended.

Once on a paid plan, in **Settings → Rules → Rulesets**, for `main` and `develop`: require a pull
request, require the status checks `Backend (build + test)` and `Frontend (typecheck + lint +
build)`, block force pushes, and block deletions.

**Do not require an approving review while you are the only developer** — GitHub does not let you
approve your own pull request, so it would lock you out of your own repository. Turn on "require 1
approval" the day a second developer joins, not before.

---

## Database migrations

The rule that matters: **migrations never run automatically against production.**

| Environment | How a migration is applied |
| --- | --- |
| **DEV** | Create them freely: `dotnet ef migrations add <Name>`. SQLite builds from the model. |
| **TEST** | Automatic on start-up, so QA always matches the build under test. |
| **UAT** | Reviewed script, applied as a deliberate deployment step. |
| **PROD** | Reviewed script, **applied against a fresh backup**, as part of the release. |

`appsettings.Production.json` and `appsettings.UAT.json` both set `Database:AutoMigrate` to
`false`. Leave it that way.

For UAT and PROD:

```bash
cd backend/Quotely.Api
dotnet ef migrations script --idempotent --output migration.sql
# read migration.sql, back up the database, then apply it with your normal tooling
```

An idempotent script is safe to run against a database that is already partly up to date. Never
point `dotnet ef database update` at production from a laptop.

If a pull request adds a migration, say so in its description — the template asks for it.

---

## Secrets

**No secret ever enters git.** Not in a branch, not temporarily, not in a commit you plan to amend.

- **CI needs no secrets** and must stay that way. It never talks to Razorpay or a real database.
- **Never connect CI to Razorpay Live credentials.** There is no legitimate reason for a build to
  hold them.
- **Each environment gets its own** connection string, JWT key, Razorpay key ID, key secret,
  webhook secret, frontend API URL and public URL. Never shared between environments.
- **Deployment secrets will live in GitHub Environment secrets** (Settings → Environments). The
  `test`, `uat` and `production` environments already exist for this purpose and are currently
  empty.
- **Never echo a secret in a workflow step.** GitHub masks known secrets in logs, but a value you
  construct or decode can slip past the mask.

Full detail in [docs/environments.md](environments.md).

---

## Connecting CD later

Nothing here deploys, and nothing should pretend to. When a hosting provider is chosen, CD attaches
at exactly two points:

| Trigger | Deploys to | Uses |
| --- | --- | --- |
| Push to `develop`, CI green | **TEST** | `test` environment secrets |
| Push to `release/**`, CI green | **UAT** | `uat` environment secrets |
| Release published (tag on `main`) | **PROD** | `production` environment secrets |

Each becomes a small workflow gated on the CI job, reading its credentials from the matching
GitHub Environment. Once on a paid plan, the `production` environment can additionally require a
manual approval before it runs — which is the point at which "deploy to production" becomes a
button someone deliberately presses.

---

## Quick reference

```bash
# start work
git switch develop && git pull && git switch -c feature/<name>

# publish it
git push -u origin feature/<name>
gh pr create --base develop --fill

# check CI
gh pr checks

# cut a release
git switch develop && git pull && git switch -c release/v2.5.0
git push -u origin release/v2.5.0
gh pr create --base main --title "Release v2.5.0" --fill

# tag it after the release PR merges
git switch main && git pull
git tag -a v2.5.0 -m "v2.5.0 — <what shipped>"
git push origin v2.5.0

# bring main back into develop (also via a PR)
git switch -c chore/backmerge-v2.5.0 main
git push -u origin chore/backmerge-v2.5.0
gh pr create --base develop --fill

# enable the local push guard (once per clone)
git config core.hooksPath .githooks
```

---

## Related documentation

- [`README.md`](../README.md) — running Quotely locally
- [`docs/environments.md`](environments.md) — environments, secrets, configuration, migrations
- [`docs/architecture.md`](architecture.md) — how the system fits together
- [`docs/api.md`](api.md) — API reference
