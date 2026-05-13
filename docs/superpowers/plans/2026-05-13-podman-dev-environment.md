# Podman Dev Environment Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stand up the local DAIS Bridge memory-stack dev environment as one orchestrated unit. `make up` brings up ArangoDB 3.12 (with `--vector-index`), an LM Studio health probe sidecar, and the DAIS Bridge gateway (dev profile = mounted source + `dotnet watch`; prod profile = published binary). `make down` tears it down. Restart-on-crash on every service.

**Architecture:** Single `compose.yaml` at repo root managed by `podman compose`. Services share a default bridge network `darbees-dev`. ArangoDB and DAIS Bridge ports are host-mapped so existing host-side `dotnet test` workflows keep working unchanged. DAIS Bridge reads connection details env-var-first so the same binary works inside the compose network (`http://arango:8529`) and outside it (`http://localhost:8529`). A multi-stage `dais-bridge/Dockerfile` produces both a dev image (SDK + `dotnet watch`) and a prod image (published binary + non-root `app` user).

**Tech Stack:** Podman 4.4+ (or 5.x), Compose Spec (`compose.yaml`), Make, .NET 9 SDK / ASP.NET runtime images (`mcr.microsoft.com/dotnet/{sdk,aspnet}:9.0`), Alpine 3.20 (LM probe sidecar).

**Spec:** [docs/superpowers/specs/2026-05-13-podman-dev-environment-design.md](../specs/2026-05-13-podman-dev-environment-design.md)

---

## Pre-flight

- [ ] **Verify branch and baseline.**

```bash
git status --short
git log --oneline -5
```

Confirm: working tree is clean (or only contains expected unrelated noise like CRLF line endings on Windows-touched files); HEAD is `2c1f59a` or later (the spec commit).

- [ ] **Verify podman is healthy.**

```bash
podman version | head -3
podman ps
```

Expected: podman client + server versions print; container list shows whatever you have running (audiobookshelf is fine). If you see a Docker context that maps to a broken Docker Desktop instance, that's irrelevant — we'll use podman directly.

- [ ] **Verify .NET 9 SDK and tests baseline (host side).**

If ArangoDB is still running from prior sessions:

```bash
podman ps --filter name=arango-test
# If 'arango-test' exists, stop it so the new compose stack can claim port 8529:
podman rm -f arango-test 2>/dev/null

# Restart for the baseline test:
podman run -d --name arango-test -e ARANGO_ROOT_PASSWORD=password -p 8529:8529 \
  arangodb:3.12 --vector-index
sleep 10
curl -fsS -u root:password http://localhost:8529/_api/version
```

Expected: `{"server":"arango","license":"enterprise","version":"3.12.x",...}`.

Then run the baseline tests on host:

```bash
export ARANGO_TEST_RUN=1
export ARANGO_TEST_URL=http://localhost:8529
dotnet test dais-bridge.tests/dais-bridge.tests.csproj --nologo 2>&1 | tail -3
```

Expected: `Passed!  - Failed:     0, Passed:    29, Skipped:     0, Total:    29`.

Once verified, stop the manual container — we'll re-create it via compose later:

```bash
podman rm -f arango-test
```

- [ ] **Verify spec exists.**

```bash
test -f docs/superpowers/specs/2026-05-13-podman-dev-environment-design.md && echo OK
```

Expected: `OK`.

---

## Phase A — Code prerequisites

Goal: make the DAIS Bridge gateway readable in containerized form by env-var-izing the host-coupled config values. This is the spec's Section 11 prerequisite (the Phase 11 A6 env-var-first lookup); we land a focused, minimal version of it here so the orchestration can ship.

### Task A1: Env-var-first lookup in Program.cs

**Files:**
- Modify: `dais-bridge/Program.cs`

ArangoDB and LM Studio URLs are currently read from `appsettings.json` only. Inside the compose network, those URLs must point at service names (`http://arango:8529`) and `host.containers.internal`, not `localhost`. We add an env-var-first fallback so the same binary works both inside and outside containers.

- [ ] **Step 1: Replace the configuration-read block in `Program.cs`.**

Open `dais-bridge/Program.cs`. Find the existing block around lines 22-29:

```csharp
        var lmStudioUrl = builder.Configuration["AI:LMStudioUrl"] ?? "http://localhost:1234/v1";
        var modelId = builder.Configuration["AI:ModelId"] ?? "local-model";

        var arangoUrl = builder.Configuration["ArangoDB:Url"] ?? "http://localhost:8529";
        var arangoDb = builder.Configuration["ArangoDB:Database"] ?? "darbees_knowledge";
        var arangoUser = builder.Configuration["ArangoDB:User"] ?? "root";
        var arangoPass = builder.Configuration["ArangoDB:Password"] ?? "password";
```

Replace with env-var-first reads:

```csharp
        var lmStudioUrl = Environment.GetEnvironmentVariable("LMSTUDIO_URL")
            ?? builder.Configuration["AI:LMStudioUrl"]
            ?? "http://localhost:1234/v1";
        var modelId = Environment.GetEnvironmentVariable("AI_MODEL_ID")
            ?? builder.Configuration["AI:ModelId"]
            ?? "local-model";

        var arangoUrl = Environment.GetEnvironmentVariable("ARANGO_URL")
            ?? builder.Configuration["ArangoDB:Url"]
            ?? "http://localhost:8529";
        var arangoDb = Environment.GetEnvironmentVariable("ARANGO_DATABASE")
            ?? builder.Configuration["ArangoDB:Database"]
            ?? "darbees_knowledge";
        var arangoUser = Environment.GetEnvironmentVariable("ARANGO_USER")
            ?? builder.Configuration["ArangoDB:User"]
            ?? "root";
        var arangoPass = Environment.GetEnvironmentVariable("ARANGO_PASSWORD")
            ?? builder.Configuration["ArangoDB:Password"]
            ?? "password";
```

- [ ] **Step 2: Build to confirm it compiles.**

```bash
dotnet build dais-bridge/dais-bridge.csproj 2>&1 | tail -5
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 3: Sanity-test that env-vars override config.**

```bash
ARANGO_URL=http://example.invalid:9999 \
  dotnet run --project dais-bridge --no-build 2>&1 | head -20 &
APP_PID=$!
sleep 3
# Boot log should show the override URL or at least no crash from "missing config".
# Kill the dev server:
kill $APP_PID 2>/dev/null
wait 2>/dev/null
```

Expected: app prints `🚀 Darbee Sovereign Gateway Initializing...` and listens on port 5000 (or fails downstream when something tries to talk to the bogus arango — that's fine, the env-var was read). No "Missing ... Path" or NullReferenceException from the config block.

If you'd prefer to skip the manual run, the unit-test suite alone is sufficient — Task B5 (compose smoke) will exercise the env vars end-to-end.

- [ ] **Step 4: Run the existing test suite to confirm no regressions.**

```bash
export ARANGO_TEST_RUN=1
export ARANGO_TEST_URL=http://localhost:8529
# Make sure arango is running for integration tests; if not:
podman run -d --name arango-test -e ARANGO_ROOT_PASSWORD=password -p 8529:8529 \
  arangodb:3.12 --vector-index 2>/dev/null
sleep 10
dotnet test dais-bridge.tests/dais-bridge.tests.csproj --nologo 2>&1 | tail -3
```

Expected: `Passed!  - Failed:     0, Passed:    29`.

- [ ] **Step 5: Commit.**

```bash
git add dais-bridge/Program.cs
git commit -m "feat(dais-bridge): env-var-first config lookup for ARANGO_*, LMSTUDIO_URL, AI_MODEL_ID

Prerequisite for the Phase 12 podman compose orchestration: inside the
compose network, arango is reached at http://arango:8529 (service name)
and LM Studio via http://host.containers.internal:1234. Outside the
network (host-side dotnet run / dotnet test), the existing
appsettings.json values still apply.

Lookup order per variable:
  Environment variable -> appsettings.json key -> hardcoded localhost default

This subsumes part of the Phase 11 A6 task's planned env-var work for the
gateway; A6 still owns the IEmbeddingClient + MemoryStore DI wiring and
the LMSTUDIO_API_KEY plumbing for the embedding client."
```

Tear down the test container before continuing to Phase B (compose will recreate it):

```bash
podman rm -f arango-test
```

---

## Phase B — Container images and orchestration

### Task B1: `dais-bridge/.dockerignore`

**Files:**
- Create: `dais-bridge/.dockerignore`

Keeps host-build artifacts and IDE files out of the Docker build context, so the prod-stage `COPY . ./` doesn't accidentally ship `bin/Debug/net9.0/dais-bridge.dll` from a host build.

- [ ] **Step 1: Create the file.**

Write to `dais-bridge/.dockerignore`:

```
bin/
obj/
*.user
.vs/
appsettings.Development.json
.dockerignore
Dockerfile
```

- [ ] **Step 2: Commit.**

```bash
git add dais-bridge/.dockerignore
git commit -m "chore(dais-bridge): add .dockerignore to keep host build artifacts out of image context"
```

---

### Task B2: `dais-bridge/Dockerfile` (multi-stage: dev + build + prod)

**Files:**
- Create: `dais-bridge/Dockerfile`

- [ ] **Step 1: Create the file.**

Write to `dais-bridge/Dockerfile`:

```dockerfile
# syntax=docker/dockerfile:1.7

# ============================================================
# dev — SDK + dotnet watch (consumed by compose dais-bridge-dev)
# ============================================================
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS dev
WORKDIR /src

# Pre-warm package cache. Source is volume-mounted at runtime so this
# layer's restored deps are masked by the mount, but the NuGet cache in
# /root/.nuget/packages persists and dotnet watch reuses it.
COPY dais-bridge.csproj ./
RUN dotnet restore dais-bridge.csproj

ENV DOTNET_USE_POLLING_FILE_WATCHER=true \
    ASPNETCORE_URLS=http://+:5000
EXPOSE 5000

# Compose overrides this. Included so a bare `podman run` works too.
CMD ["dotnet", "watch", "run", "--no-launch-profile", "--project", "dais-bridge.csproj"]

# ============================================================
# build — produce published binary
# ============================================================
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY dais-bridge.csproj ./
RUN dotnet restore dais-bridge.csproj

COPY . ./
RUN dotnet publish dais-bridge.csproj \
    -c Release \
    -o /publish \
    --no-restore

# ============================================================
# prod — runtime image with published binary, non-root user
# ============================================================
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS prod
WORKDIR /app

COPY --from=build /publish ./

ENV ASPNETCORE_URLS=http://+:5000
EXPOSE 5000

USER app
ENTRYPOINT ["dotnet", "dais-bridge.dll"]
```

- [ ] **Step 2: Build the `dev` target manually to validate the Dockerfile.**

```bash
podman build --target dev -t darbees-dev/dais-bridge:dev ./dais-bridge 2>&1 | tail -10
```

Expected: `Successfully tagged localhost/darbees-dev/dais-bridge:dev`. If `podman restore` fails because the .csproj can't reach NuGet, retry once — first-time SDK image pull is large.

- [ ] **Step 3: Build the `prod` target to validate the publish step.**

```bash
podman build --target prod -t darbees-dev/dais-bridge:prod ./dais-bridge 2>&1 | tail -10
```

Expected: `Successfully tagged localhost/darbees-dev/dais-bridge:prod`. The publish step compiles the .NET app inside the build stage; any compile error here would show as a normal `dotnet publish` failure.

- [ ] **Step 4: Smoke-run the prod image (no compose yet) to confirm it boots.**

```bash
podman run --rm -d --name prod-smoke -p 5000:5000 \
  -e ARANGO_URL=http://example.invalid:9999 \
  darbees-dev/dais-bridge:prod
sleep 5
curl -fsS http://localhost:5000/ ; echo
podman rm -f prod-smoke
```

Expected: `Darbee Sovereign AI Gateway Active`. The bogus ARANGO_URL is fine — the gateway boots before any Arango call is made.

- [ ] **Step 5: Commit.**

```bash
git add dais-bridge/Dockerfile
git commit -m "feat(dais-bridge): multi-stage Dockerfile with dev, build, prod targets

- dev: mcr.microsoft.com/dotnet/sdk:9.0 + dotnet watch + polling file watcher
- build: intermediate stage that publishes a Release binary
- prod: mcr.microsoft.com/dotnet/aspnet:9.0 + published binary + non-root 'app' user

Build context is ./dais-bridge so the test project is not included.
ASPNETCORE_URLS=http://+:5000 in both runtime stages to match
the host-side dev pattern."
```

---

### Task B3: `compose.yaml` (orchestration)

**Files:**
- Create: `compose.yaml`

- [ ] **Step 1: Create the file.**

Write to `compose.yaml` at the repo root:

```yaml
name: darbees-dev

services:
  arango:
    image: arangodb:3.12
    command: ["--vector-index"]
    environment:
      ARANGO_ROOT_PASSWORD: ${ARANGO_ROOT_PASSWORD:-password}
    ports:
      - "8529:8529"
    volumes:
      - arango-data:/var/lib/arangodb3
    healthcheck:
      test: ["CMD", "curl", "-fsS", "-u", "root:${ARANGO_ROOT_PASSWORD:-password}",
             "http://localhost:8529/_api/version"]
      interval: 5s
      timeout: 2s
      retries: 12
    restart: unless-stopped

  lm-probe:
    image: alpine:3.20
    command:
      - sh
      - -c
      - |
        apk add --no-cache curl >/dev/null
        while true; do
          if curl -fsS -m 3 -H "Authorization: Bearer ${LMSTUDIO_API_KEY:-}" \
              http://host.containers.internal:1234/v1/models >/dev/null 2>&1; then
            echo "[$(date -Iseconds)] lm-studio UP"
          else
            echo "[$(date -Iseconds)] lm-studio DOWN"
          fi
          sleep 30
        done
    environment:
      LMSTUDIO_API_KEY: ${LMSTUDIO_API_KEY:-}
    restart: unless-stopped

  dais-bridge-dev:
    profiles: ["dev"]
    build:
      context: ./dais-bridge
      target: dev
    command: ["dotnet", "watch", "run", "--no-launch-profile", "--project", "dais-bridge.csproj"]
    volumes:
      - ./dais-bridge:/src:Z
    working_dir: /src
    environment:
      ARANGO_URL: http://arango:8529
      ARANGO_USER: root
      ARANGO_PASSWORD: ${ARANGO_ROOT_PASSWORD:-password}
      LMSTUDIO_URL: http://host.containers.internal:1234
      LMSTUDIO_API_KEY: ${LMSTUDIO_API_KEY:-}
      DOTNET_USE_POLLING_FILE_WATCHER: "true"
    ports:
      - "5000:5000"
    depends_on:
      arango: { condition: service_healthy }
    restart: unless-stopped

  dais-bridge-prod:
    profiles: ["prod"]
    build:
      context: ./dais-bridge
      target: prod
    environment:
      ARANGO_URL: http://arango:8529
      ARANGO_USER: root
      ARANGO_PASSWORD: ${ARANGO_ROOT_PASSWORD:-password}
      LMSTUDIO_URL: http://host.containers.internal:1234
      LMSTUDIO_API_KEY: ${LMSTUDIO_API_KEY:-}
    ports:
      - "5000:5000"
    depends_on:
      arango: { condition: service_healthy }
    restart: unless-stopped

volumes:
  arango-data:
```

- [ ] **Step 2: Validate compose syntax.**

```bash
podman compose config --quiet
```

Expected: exit code 0 (no output). Any error message points to a YAML or schema issue.

- [ ] **Step 3: Commit.**

```bash
git add compose.yaml
git commit -m "feat(infra): compose.yaml for podman dev orchestration

Four services in the darbees-dev project:
- arango:        arangodb:3.12 --vector-index, port 8529, named volume,
                 curl-based healthcheck
- lm-probe:      alpine sidecar polling host.containers.internal:1234 every
                 30s, logs UP/DOWN to stdout
- dais-bridge-dev (profile: dev):  built from dais-bridge/Dockerfile dev
                 target, mounted source + dotnet watch, depends on
                 arango healthy
- dais-bridge-prod (profile: prod): built from dais-bridge/Dockerfile prod
                 target, no source mount, depends on arango healthy

All services use restart: unless-stopped per the spec's restart-on-crash
policy. ArangoDB port 8529 is host-mapped so existing host-side dotnet
test paths keep working unchanged."
```

---

### Task B4: Amend `.env.example` with the Phase 12 dev-stack vars

**Files:**
- Modify: `.env.example`

A `.env.example` already exists at the repo root with the Astro `PUBLIC_*` vars (`PUBLIC_CLOUDFLARE_ANALYTICS_TOKEN`, `PUBLIC_BUTTONDOWN_HANDLE`). `.env` is already in `.gitignore` (line 17). We **append** the dev-stack vars to the existing file so there's a single source of truth.

- [ ] **Step 1: Append to `.env.example`.**

Add to the bottom of `.env.example`:

```bash

# -----------------------------------------------------------------------------
# Phase 12 dev environment (podman compose stack)
# -----------------------------------------------------------------------------
# Used by `make up` / `compose.yaml`. See docs/dev-environment.md.

# Local ArangoDB root password. Dev default is fine; pick anything for local.
ARANGO_ROOT_PASSWORD=password

# LM Studio API token. Generate one in LM Studio's developer settings.
# Required from Phase 11 Task A6 onward (when the embedding client is wired
# into DI). Leave blank if you only need the arango stack and don't have LM
# Studio running yet.
LMSTUDIO_API_KEY=
```

- [ ] **Step 2: Verify `.env` would be ignored if created.**

```bash
git check-ignore -v .env
```

Expected: `.gitignore:17:.env	.env`. If nothing matches, append `.env` to `.gitignore` (this shouldn't happen — the line exists today).

- [ ] **Step 3: Commit.**

```bash
git add .env.example
git commit -m "chore: append Phase 12 dev-stack vars (ARANGO_ROOT_PASSWORD, LMSTUDIO_API_KEY) to .env.example"
```

---

### Task B5: First compose smoke (arango + lm-probe only)

We bring up just the always-on services (no profile flag) first to validate `arango` and `lm-probe` work before adding the bridge into the mix. This catches networking / health-check issues early.

**Files:**
- None (verification step using files from B1-B4).

- [ ] **Step 1: First-time setup.**

```bash
test -f .env || cp .env.example .env
# Edit .env to add your LMSTUDIO_API_KEY if LM Studio is running; otherwise leave blank.
```

- [ ] **Step 2: Bring up without a profile (only services with no `profiles:` block start).**

```bash
podman compose up -d
```

Expected: builds aren't triggered (no profile = no dais-bridge containers). `arango` and `lm-probe` start.

- [ ] **Step 3: Confirm `podman compose ps` shows arango and lm-probe.**

```bash
podman compose ps
```

Expected: 2 services listed (`arango`, `lm-probe`), both with status `Up`. Arango may show `(healthy)` after ~10s.

- [ ] **Step 4: Hit ArangoDB through the host-mapped port.**

```bash
curl -fsS -u root:password http://localhost:8529/_api/version
```

Expected: `{"server":"arango","version":"3.12.x",...}`. If you set a non-default `ARANGO_ROOT_PASSWORD` in `.env`, swap it in.

- [ ] **Step 5: Verify the lm-probe sidecar is logging status.**

```bash
podman compose logs --tail=5 lm-probe
```

Expected output (one of):
```
[2026-05-13T...] lm-studio UP        # if LM Studio is running on host
[2026-05-13T...] lm-studio DOWN      # if LM Studio is not running
```

- [ ] **Step 6: Run the existing test suite against the compose-managed ArangoDB.**

```bash
export ARANGO_TEST_RUN=1
export ARANGO_TEST_URL=http://localhost:8529
export ARANGO_TEST_USER=root
export ARANGO_TEST_PASS=password
dotnet test dais-bridge.tests/dais-bridge.tests.csproj --nologo 2>&1 | tail -3
```

Expected: `Passed!  - Failed:     0, Passed:    29`. **This proves the orchestrated Arango is functionally equivalent to the manual `docker run` baseline.**

- [ ] **Step 7: Tear down for the next task.**

```bash
podman compose down
```

Expected: arango and lm-probe stop and are removed. The `arango-data` volume persists (verify with `podman volume ls | grep arango-data`).

- [ ] **Step 8: No code changes in this task — nothing to commit.**

---

### Task B6: Full dev-profile smoke (includes dais-bridge-dev)

**Files:**
- None (verification using files from B1-B4 plus the multi-stage Dockerfile).

- [ ] **Step 1: Bring up with the dev profile.**

```bash
podman compose --profile dev up -d --build
```

Expected: builds the `dais-bridge-dev` image (cold first time, ~1-2 min for SDK pull + restore). All three services start. `--build` forces a build even if the image already exists, which we want at least once to confirm.

- [ ] **Step 2: Confirm three services are running.**

```bash
podman compose ps
```

Expected: `arango`, `lm-probe`, `dais-bridge-dev` all `Up`. Arango healthy.

- [ ] **Step 3: Confirm the gateway is reachable.**

```bash
# Wait a few seconds for dotnet watch to compile + start the app inside the container.
sleep 15
curl -fsS http://localhost:5000/
```

Expected: `Darbee Sovereign AI Gateway Active`. If `connection refused`, peek at logs:

```bash
podman compose logs --tail=30 dais-bridge-dev
```

Look for `🚀 Darbee Sovereign Gateway Initializing...`. If you see `Now listening on: http://[::]:5000`, the app is up.

- [ ] **Step 4: Confirm the container is using env-var URLs, not localhost.**

```bash
podman compose exec dais-bridge-dev printenv | grep -E "ARANGO|LMSTUDIO"
```

Expected output includes:
```
ARANGO_URL=http://arango:8529
ARANGO_USER=root
ARANGO_PASSWORD=password
LMSTUDIO_URL=http://host.containers.internal:1234
LMSTUDIO_API_KEY=    (or your token, if set)
```

- [ ] **Step 5: Verify `dotnet watch` reacts to host-side file changes.**

In one terminal, start tailing the bridge logs:

```bash
podman compose logs -f dais-bridge-dev
```

In another terminal, touch a source file:

```bash
# Trivial edit — append a newline:
echo "" >> dais-bridge/Program.cs
```

Watch the logs. Within ~5s (poll interval), expected output includes:

```
dotnet watch ⌚ File changed: /src/Program.cs
dotnet watch 🔥 Hot reload of changes succeeded.
```

(Or a full rebuild + restart if hot reload can't apply the change. Either is fine.)

Revert the file:

```bash
git checkout -- dais-bridge/Program.cs
```

- [ ] **Step 6: Verify restart-on-crash policy works.**

```bash
podman compose kill arango
sleep 5
podman compose ps
```

Expected: `arango` is `Up` again (`restart: unless-stopped` triggered).

- [ ] **Step 7: Tear down.**

```bash
podman compose --profile dev down
```

- [ ] **Step 8: No code changes — nothing to commit.**

---

### Task B7: Prod-profile build + smoke

**Files:**
- None (verification using the prod Dockerfile target via compose).

- [ ] **Step 1: Bring up the prod profile.**

```bash
podman compose --profile prod up -d --build
```

Expected: builds the `dais-bridge-prod` image (cold first time includes `dotnet publish` step, ~30-60s). All three services start.

- [ ] **Step 2: Confirm the gateway is reachable.**

```bash
sleep 10
curl -fsS http://localhost:5000/
```

Expected: `Darbee Sovereign AI Gateway Active`. Same response as the dev profile; under the hood it's the published binary running as the non-root `app` user.

- [ ] **Step 3: Verify the prod container runs as non-root.**

```bash
podman compose exec dais-bridge-prod id
```

Expected: `uid=<non-zero>(app) gid=<non-zero>(app) ...`. **Reject** any output that shows `uid=0(root)`.

- [ ] **Step 4: Tear down.**

```bash
podman compose --profile prod down
```

- [ ] **Step 5: No code changes — nothing to commit.**

---

## Phase C — Developer ergonomics

### Task C1: `Makefile` with self-documenting targets

**Files:**
- Create: `Makefile`

- [ ] **Step 1: Create the file.**

Write to `Makefile` at the repo root:

```makefile
.DEFAULT_GOAL := help
COMPOSE := podman compose

.PHONY: help up up-dev up-prod down restart build rebuild ps logs \
        logs-arango logs-lm logs-bridge shell-bridge shell-arango \
        health clean init

help:                              ## List available targets
	@awk 'BEGIN {FS = ":.*?## "} /^[a-zA-Z_-]+:.*?## / \
		{printf "  \033[36m%-15s\033[0m %s\n", $$1, $$2}' $(MAKEFILE_LIST)

init:                              ## First-time setup: ensure .env exists
	@test -f .env || (cp .env.example .env && \
		echo "Created .env from .env.example — fill in LMSTUDIO_API_KEY")

up: up-dev                         ## Alias for up-dev

up-dev: init                       ## Start dev stack
	$(COMPOSE) --profile dev up -d

up-prod: init                      ## Start prod stack
	$(COMPOSE) --profile prod up -d

down:                              ## Stop and remove all containers
	$(COMPOSE) --profile dev --profile prod down

restart:                           ## Restart all running services
	$(COMPOSE) restart

build:                             ## Build all images
	$(COMPOSE) --profile dev --profile prod build

rebuild:                           ## Force rebuild with no cache
	$(COMPOSE) --profile dev --profile prod build --no-cache

ps:                                ## Show service status
	$(COMPOSE) ps

logs:                              ## Tail all logs
	$(COMPOSE) logs -f --tail=50

logs-arango:                       ## Tail arangodb logs
	$(COMPOSE) logs -f --tail=50 arango

logs-lm:                           ## Tail lm-probe logs (LM Studio up/down)
	$(COMPOSE) logs -f --tail=50 lm-probe

logs-bridge:                       ## Tail dais-bridge logs
	$(COMPOSE) logs -f --tail=50 dais-bridge-dev dais-bridge-prod

shell-bridge:                      ## Open a shell inside the running dais-bridge container
	$(COMPOSE) exec dais-bridge-dev /bin/bash

shell-arango:                      ## Open arangosh inside arango container
	$(COMPOSE) exec arango arangosh \
		--server.endpoint http+tcp://localhost:8529 \
		--server.username root \
		--server.password $${ARANGO_ROOT_PASSWORD:-password}

health:                            ## Smoke-check that services are reachable
	@echo "--- ArangoDB:" && \
		curl -fsS -u root:$${ARANGO_ROOT_PASSWORD:-password} \
			http://localhost:8529/_api/version || echo "  DOWN"
	@echo "--- LM Studio (host):" && \
		curl -fsS -H "Authorization: Bearer $${LMSTUDIO_API_KEY}" \
			http://localhost:1234/v1/models >/dev/null && echo "  UP" || echo "  DOWN"
	@echo "--- DAIS Bridge (5000):" && \
		curl -fsS http://localhost:5000/ || echo "  DOWN"

clean:                             ## down + remove arango-data volume (DESTRUCTIVE)
	$(COMPOSE) --profile dev --profile prod down -v
	@echo "Volumes removed. Next 'make up' will start with an empty database."
```

- [ ] **Step 2: Verify `make help` lists all targets.**

```bash
make help
```

Expected output (truncated):
```
  help            List available targets
  init            First-time setup: ensure .env exists
  up              Alias for up-dev
  up-dev          Start dev stack
  ...
  clean           down + remove arango-data volume (DESTRUCTIVE)
```

If any expected target is missing, check that its `##` comment has at least one space after the colons (`target:  ## description`).

- [ ] **Step 3: Verify `make init` is idempotent.**

```bash
# .env should already exist from Task B5. If not:
make init
# Run a second time:
make init
```

Expected: no error, no duplicated `.env` files, no spurious messages on the second run.

- [ ] **Step 4: Commit.**

```bash
git add Makefile
git commit -m "feat(infra): Makefile with self-documenting targets for podman compose

Wraps 'podman compose' with ergonomic commands: up, down, build, rebuild,
ps, logs (all + per-service), shell-bridge, shell-arango, health, clean.
'make' with no args prints the target list. 'make init' creates .env from
.env.example on first run."
```

---

### Task C2: End-to-end Makefile smoke test

**Files:**
- None (verification of C1).

- [ ] **Step 1: Cold-start from clean state.**

```bash
make clean
make up
```

Expected: `make clean` is a no-op (nothing to tear down) but doesn't error. `make up` builds (cold first time may take ~2 min for SDK image pull) and starts the dev stack.

- [ ] **Step 2: Wait for services to settle, then health-check.**

```bash
sleep 15
make health
```

Expected output:
```
--- ArangoDB:
{"server":"arango","version":"3.12.x",...}
--- LM Studio (host):
UP            # or DOWN if LM Studio isn't running
--- DAIS Bridge (5000):
Darbee Sovereign AI Gateway Active
```

- [ ] **Step 3: Peek at the LM probe.**

```bash
podman compose logs --tail=3 lm-probe
```

Expected: one or more `lm-studio UP` / `lm-studio DOWN` lines with ISO timestamps.

- [ ] **Step 4: Confirm tests still pass against the orchestrated stack.**

```bash
export ARANGO_TEST_RUN=1
export ARANGO_TEST_URL=http://localhost:8529
dotnet test dais-bridge.tests/dais-bridge.tests.csproj --nologo 2>&1 | tail -3
```

Expected: `Passed! - Failed: 0, Passed: 29`.

- [ ] **Step 5: Tear down.**

```bash
make down
```

Expected: all containers stopped and removed.

- [ ] **Step 6: No code changes — nothing to commit.**

---

## Phase D — Documentation

### Task D1: Create `docs/dev-environment.md`

**Files:**
- Create: `docs/dev-environment.md`

- [ ] **Step 1: Create the file.**

Write to `docs/dev-environment.md`:

````markdown
# Local Dev Environment

The DAIS Bridge memory-stack runs locally via `podman compose`. One command brings everything up; one command tears it down. Restart-on-crash on every service.

> Content authoring for darbeeschasingrainbows.com happens in Obsidian — see [`OBSIDIAN-CONTENT-WORKFLOW.md`](../OBSIDIAN-CONTENT-WORKFLOW.md). This dev environment is for backend / Phase 11 work (the DAIS Bridge gateway, the memory layer, etc.). If you're only editing `.mdx` posts, you don't need any of this.

## Prerequisites

- **podman ≥ 4.4** (Fedora 39+ ships 4.x; podman 5.x has built-in `compose` subcommand). On older podman, install `podman-compose` separately: `sudo dnf install podman-compose`.
- **GNU make** — comes with `make` on most Linux distros.
- **LM Studio** (desktop app) loaded with `nomic-embed-text-v1.5` (or compatible 768-dim embedding model), running with a Bearer token. Only required from Phase 11 Task A6 onward — earlier tasks don't need it.

## First-time setup

```bash
make init           # creates .env from .env.example
# Edit .env to fill in LMSTUDIO_API_KEY if LM Studio is running.
make up             # starts the dev stack
make health         # confirms each service is reachable
```

## Daily workflow

| Command | What it does |
|---|---|
| `make up` | Start dev stack (arango + lm-probe + dais-bridge-dev with hot reload) |
| `make up-prod` | Start prod stack (same arango + lm-probe, but the published-binary gateway) |
| `make down` | Stop and remove all containers (volumes preserved) |
| `make ps` | List running services |
| `make health` | Curl each service and report UP/DOWN |
| `make logs` | Tail all service logs |
| `make logs-bridge` | Tail just dais-bridge |
| `make logs-arango` | Tail just arangodb |
| `make logs-lm` | Tail the LM Studio probe sidecar |
| `make shell-bridge` | bash inside the running gateway container |
| `make shell-arango` | arangosh inside the running arangodb container |
| `make restart` | Restart all services |
| `make build` | Rebuild images (incremental) |
| `make rebuild` | Force rebuild with `--no-cache` |
| `make clean` | **Destructive.** down + remove `arango-data` volume. Next `make up` boots with an empty database. |

## What's in the stack

| Service | Image | Ports | Notes |
|---|---|---|---|
| `arango` | `arangodb:3.12` | `8529:8529` | Started with `--vector-index` flag. Named volume `arango-data` persists between runs. |
| `lm-probe` | `alpine:3.20` | (none) | Sidecar that polls `host.containers.internal:1234/v1/models` every 30s and logs UP/DOWN. View with `make logs-lm`. |
| `dais-bridge-dev` (profile: dev) | built from `dais-bridge/Dockerfile` `dev` target | `5000:5000` | SDK + `dotnet watch` on mounted source. Hot reload via polling file watcher. |
| `dais-bridge-prod` (profile: prod) | built from `dais-bridge/Dockerfile` `prod` target | `5000:5000` | Published binary. Runs as non-root `app` user. |

## Profile switching

- `make up` / `make up-dev` — dev profile (mounted source, hot reload). Daily driver.
- `make up-prod` — prod profile (published binary, no source mount). Use for pre-merge smoke or to mirror what CI / production would run.
- `make down` covers both profiles, so switching is `make down && make up-prod`.

## Running host-side tests against the compose stack

The compose `arango` port-maps `8529` to the host, so the existing test workflow is unchanged:

```bash
make up
export ARANGO_TEST_RUN=1
export ARANGO_TEST_URL=http://localhost:8529
dotnet test dais-bridge.tests/dais-bridge.tests.csproj
```

The bridge gateway runs both on the host (`dotnet run`) and in the container (`dais-bridge-dev`); use whichever you prefer. They can coexist if you map the container to a different host port — by default both bind 5000, so you'd pick one.

## Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| `host.containers.internal` doesn't resolve | Podman version too old, or non-default network | Add `extra_hosts: ["host.containers.internal:host-gateway"]` to each service that needs it in `compose.yaml` |
| `permission denied` reading mounted source on Fedora | SELinux | Confirm `:Z` on the `./dais-bridge:/src` volume mount in `compose.yaml`; if SELinux contexts get stuck, `restorecon -Rv dais-bridge/` |
| Port 8529 already in use on `make up` | Another container or host process holding it | `podman ps` (and `docker ps` if you have both) to find culprit; `podman rm -f <name>` to remove if stale |
| `lm-probe` always logs DOWN | LM Studio not running, token wrong, or model not loaded | Open LM Studio, load the embedding model, ensure `LMSTUDIO_API_KEY` in `.env` matches the token in LM Studio's developer settings |
| `dotnet watch` doesn't pick up file changes | Polling not enabled | Confirm `DOTNET_USE_POLLING_FILE_WATCHER=true` in the service env (it's set by default in `compose.yaml`) |
| `podman compose` not found | Old podman (< 4.4) | `sudo dnf install podman-compose`, or upgrade podman; both work as `podman compose <subcmd>` once installed |
| Container exits 125 with "Operation not supported" on Arango volume | Rootless UID mapping mismatch | `podman unshare chown -R 999:999 ~/.local/share/containers/storage/volumes/darbees-dev_arango-data/_data` once after first `make up` |
| Tests fail with connection refused even though `make ps` shows arango Up | Healthcheck still pending | Wait 10-15s after `make up` before running tests; `make health` should return ArangoDB OK first |

## Memory and disk usage

- Cold images cached on first build: ~700 MB (Arango + .NET SDK + ASP.NET runtime + Alpine).
- `arango-data` volume grows with your memory writes; cleared by `make clean`.
- Running containers (idle): well under 1 GB resident.
````

- [ ] **Step 2: Verify the file renders correctly in any markdown viewer.**

Open `docs/dev-environment.md` in your editor's markdown preview (or just `cat` it). Confirm tables render, no broken links to other docs.

- [ ] **Step 3: Commit.**

```bash
git add docs/dev-environment.md
git commit -m "docs: add docs/dev-environment.md — local podman compose dev environment guide

Canonical reference for bringing up the DAIS Bridge memory-stack locally.
Covers prerequisites, first-time setup, daily workflow, profile switching,
running host-side tests, troubleshooting, and resource usage. Linked from
README, CLAUDE.md, OBSIDIAN-CONTENT-WORKFLOW.md, TODO-phase11.md, and the
graph-backed-rag resume guide."
```

---

### Task D2: Update `README.md` and `CLAUDE.md`

**Files:**
- Modify: `README.md`
- Modify: `CLAUDE.md`

- [ ] **Step 1: Read the current state of both files to find good insertion points.**

```bash
head -30 README.md
head -50 CLAUDE.md
```

- [ ] **Step 2: Edit `README.md` — add a "Run the local services" subsection.**

`README.md` currently has a `## Local setup` section (around line 41) for Astro local dev. Insert a new section immediately after it, before the next top-level heading. Find the line:

```markdown
npm run preview      # Preview the production build
```

(that's the last line of the existing `## Local setup` bash block, followed by a closing triple-backtick.) After the closing triple-backtick of that block, insert:

````markdown

## Run the local services (DAIS Bridge / Phase 11)

The DAIS Bridge gateway and its ArangoDB memory store run locally via `podman compose`, wrapped behind a Makefile:

```bash
make up           # starts ArangoDB, LM Studio probe, DAIS Bridge gateway
make health       # confirms each service is reachable
make down         # tears everything down
```

Full guide: [docs/dev-environment.md](docs/dev-environment.md).

Content authoring (Obsidian → `.mdx` → Astro build) doesn't require any of this — see [OBSIDIAN-CONTENT-WORKFLOW.md](OBSIDIAN-CONTENT-WORKFLOW.md).
````

- [ ] **Step 3: Edit `CLAUDE.md` — replace the "Commands (Astro)" section with two tables and refresh the "Things to be careful about" LM Studio entry.**

Find the existing "## Commands (Astro)" heading in CLAUDE.md (around line 14 per the current state). Below it is a table; **leave that table unchanged**. After the table, add a new section:

```markdown
## Commands (DAIS Bridge / Phase 11 services)

| Task                     | Command                | When             |
| ------------------------ | ---------------------- | ---------------- |
| Bring up dev stack       | `make up`              | Start of session |
| Bring up prod-mode stack | `make up-prod`         | Pre-merge smoke  |
| Tear down                | `make down`            | End of session   |
| Health check             | `make health`          | After `make up`  |
| Tail bridge logs         | `make logs-bridge`     | Debugging        |
| Run host-side tests      | `ARANGO_TEST_RUN=1 dotnet test dais-bridge.tests/dais-bridge.tests.csproj` | CI runs this |

Full guide: [docs/dev-environment.md](docs/dev-environment.md).
```

Then find the existing "## Things to be careful about" section. The LM Studio bullet currently reads:

```markdown
- **LM Studio**: Used by the Phase 11 memory layer (`dais-bridge/Memory/`) for embeddings at `http://localhost:1234/v1`. Requires a Bearer token (`LMSTUDIO_API_KEY`).
```

Replace with:

```markdown
- **LM Studio**: Used by the Phase 11 memory layer (`dais-bridge/Memory/`) for embeddings at `http://localhost:1234/v1`. Requires a Bearer token in `.env` as `LMSTUDIO_API_KEY`. The `lm-probe` sidecar in `compose.yaml` polls it every 30s and logs UP/DOWN — `make logs-lm` to watch. Inside containers, LM Studio is reached at `http://host.containers.internal:1234`.
```

- [ ] **Step 4: Commit.**

```bash
git add README.md CLAUDE.md
git commit -m "docs: add 'Run the local services' pointer to README, expand CLAUDE.md Commands

README gets a new short subsection with the make commands and a link to
docs/dev-environment.md. CLAUDE.md gets a new 'Commands (DAIS Bridge /
Phase 11 services)' table and an expanded LM Studio caveat that mentions
the lm-probe sidecar and host.containers.internal."
```

---

### Task D3: Update `OBSIDIAN-CONTENT-WORKFLOW.md`

**Files:**
- Modify: `OBSIDIAN-CONTENT-WORKFLOW.md`

- [ ] **Step 1: Find the "Why this replaces a CMS" section near the top.**

```bash
grep -n "Why this replaces a CMS" OBSIDIAN-CONTENT-WORKFLOW.md
```

Expected: a line number near the top (around line 5-7).

- [ ] **Step 2: Insert a small note immediately after that section's bullets, before "## Setup".**

The exact text to insert (find the line with `## Setup` and add this above it):

```markdown
## You only need this for content authoring

The DAIS Bridge memory-stack (Phase 11) has its own local services (ArangoDB, LM Studio probe, gateway container) orchestrated by `podman compose`. You don't need any of that to write blog posts. If you're only here to write content, follow this guide and ignore [`docs/dev-environment.md`](docs/dev-environment.md).

```

- [ ] **Step 3: Commit.**

```bash
git add OBSIDIAN-CONTENT-WORKFLOW.md
git commit -m "docs(obsidian): clarify that content authoring doesn't need the dev-environment stack"
```

---

### Task D4: Update `TODO-phase11.md` and `RESUME-graph-backed-rag.md`

**Files:**
- Modify: `TODO-phase11.md`
- Modify: `docs/superpowers/RESUME-graph-backed-rag.md`

- [ ] **Step 1: Edit `TODO-phase11.md`. Find the "Quick start (cold-start checklist)" section.**

```bash
grep -n "Quick start" TODO-phase11.md
```

Replace the entire bash block under that heading with:

````markdown
```bash
# 1. Repo
git checkout feature/graph-backed-rag
git pull --ff-only

# 2. Local services via podman compose (see docs/dev-environment.md)
make up
make health

# 3. LM Studio (required from A6 onward — A4/A5 use stub clients)
#    Load nomic-embed-text-v1.5 (768 dim) in LM Studio's server panel.
#    Put LMSTUDIO_API_KEY=<your token> in .env (make init creates a template).

# 4. Run all tests
export ARANGO_TEST_RUN=1
dotnet test dais-bridge.tests/dais-bridge.tests.csproj
# Expected: 29 passing
```
````

If anything fails, read [`docs/dev-environment.md`](docs/dev-environment.md) — it documents every environment quirk we've hit.

- [ ] **Step 2: Edit `docs/superpowers/RESUME-graph-backed-rag.md`. Find the "Verify environment" or similar block.**

```bash
grep -n "Verify environment" docs/superpowers/RESUME-graph-backed-rag.md
```

Replace the bash block under that heading with:

````markdown
```bash
# LM Studio with token
export LMSTUDIO_API_KEY="<your-token>"
# Make sure .env contains the same value
test -f .env || cp .env.example .env

# Bring up the compose-managed stack
make up
sleep 12        # wait for arango healthcheck + dotnet watch boot
make health
# Expected: ArangoDB UP, LM Studio UP (if running), DAIS Bridge UP

# Test gates
export ARANGO_TEST_RUN=1
export ARANGO_TEST_URL=http://localhost:8529
export ARANGO_TEST_USER=root
export ARANGO_TEST_PASS=password
```
````

- [ ] **Step 3: Commit.**

```bash
git add TODO-phase11.md docs/superpowers/RESUME-graph-backed-rag.md
git commit -m "docs(phase11): replace manual docker-run with make up in the cold-start checklists

Both TODO-phase11.md (project-root punchlist) and the resume guide for
the graph-backed RAG feature branch now point at 'make up' as the
canonical bring-up path. The resume guide loses its manual
'docker run ... arangodb:3.12 --vector-index' snippet in favor of the
compose-managed stack."
```

---

### Task D5: Update `HANDOFF.md` with a Phase 12 entry

**Files:**
- Modify: `HANDOFF.md`

- [ ] **Step 1: Find the "Phase 11" section in HANDOFF.md.**

```bash
grep -n "### Phase 11" HANDOFF.md
```

Add a new Phase 12 section immediately after Phase 11's content. Find the next `### Phase` heading (or the section break before "## Project file map" — whichever comes first) and insert before it:

```markdown
### Phase 12 — Podman Dev Environment (2026-05-13)

Files: `compose.yaml`, `Makefile`, `.env.example`, `.gitignore`, `dais-bridge/Dockerfile`, `dais-bridge/.dockerignore`, `dais-bridge/Program.cs`, `docs/dev-environment.md`, plus pointer updates in `README.md`, `CLAUDE.md`, `OBSIDIAN-CONTENT-WORKFLOW.md`, `TODO-phase11.md`, `docs/superpowers/RESUME-graph-backed-rag.md`.

Spec: [docs/superpowers/specs/2026-05-13-podman-dev-environment-design.md](docs/superpowers/specs/2026-05-13-podman-dev-environment-design.md). Plan: [docs/superpowers/plans/2026-05-13-podman-dev-environment.md](docs/superpowers/plans/2026-05-13-podman-dev-environment.md).

Replaces the manual `docker run arangodb:3.12 --vector-index` flow with a `podman compose`-managed dev environment. `make up` brings up ArangoDB (with the required vector-index flag), an LM Studio health probe sidecar, and the DAIS Bridge gateway in either a dev profile (mounted source + `dotnet watch`) or a prod profile (published binary). `make down` tears it down. Restart-on-crash on every service.

**Completed enhancements:**
- **Env-var-first config in DAIS Bridge:** `Program.cs` reads `ARANGO_URL`, `ARANGO_USER`, `ARANGO_PASSWORD`, `ARANGO_DATABASE`, `LMSTUDIO_URL`, `AI_MODEL_ID` from env first, falling back to `appsettings.json`, then to localhost defaults. Same binary works inside and outside the compose network.
- **Multi-stage Dockerfile:** `dais-bridge/Dockerfile` has `dev`, `build`, `prod` targets. Dev uses `mcr.microsoft.com/dotnet/sdk:9.0` + `dotnet watch` with polling file watcher; prod uses `mcr.microsoft.com/dotnet/aspnet:9.0` running the published binary as the non-root `app` user.
- **Compose file:** `compose.yaml` defines four services: `arango`, `lm-probe`, `dais-bridge-dev` (profile: dev), `dais-bridge-prod` (profile: prod). Bridge network, `restart: unless-stopped`, healthcheck on Arango, `depends_on: service_healthy` on the bridge. ArangoDB port 8529 is host-mapped so existing host-side `dotnet test` flows are unchanged.
- **Makefile:** Self-documenting (`make` → help). Targets: `init`, `up{,-dev,-prod}`, `down`, `restart`, `build`, `rebuild`, `ps`, `logs{,-arango,-lm,-bridge}`, `shell-{bridge,arango}`, `health`, `clean`.
- **Documentation:** New `docs/dev-environment.md` with full setup / daily-workflow / troubleshooting tables. README, CLAUDE.md, OBSIDIAN-CONTENT-WORKFLOW.md, TODO-phase11.md, and the graph-backed-rag resume guide all point at the new flow.

**Out of scope (deferred):**
- Astro dev server containerization (Phase 13 candidate)
- Obsidian-in-container (Phase 14 candidate)
- Boot-time autostart via Quadlets
- CI integration — local-only this round; CI continues to use GitHub Actions service containers per the Phase 11 G2 plan.
```

- [ ] **Step 2: Commit.**

```bash
git add HANDOFF.md
git commit -m "docs(handoff): add Phase 12 entry — podman compose dev environment

Records the orchestration work: env-var-first config in dais-bridge,
multi-stage Dockerfile, compose.yaml, Makefile, and the documentation
updates that point at make up as the canonical bring-up flow."
```

---

## Final verification

- [ ] **Run the full smoke from a clean state.**

```bash
make clean
make up
sleep 15
make health
```

Expected: ArangoDB UP, LM Studio UP (or DOWN if not running — acceptable), DAIS Bridge UP.

- [ ] **Run the entire test suite against the compose stack.**

```bash
export ARANGO_TEST_RUN=1
export ARANGO_TEST_URL=http://localhost:8529
dotnet test dais-bridge.tests/dais-bridge.tests.csproj --nologo 2>&1 | tail -3
```

Expected: `Passed! - Failed: 0, Passed: 29`.

- [ ] **Tear down.**

```bash
make down
```

- [ ] **Confirm the branch is ready to merge / PR.**

```bash
git log --oneline master..HEAD
git status
```

Expected: a clean chain of ~12 commits implementing this plan, no uncommitted changes outside known noise (CRLF line endings on a few Windows-touched files).

- [ ] **(Optional) Open PR.**

If the development branch is ready to integrate, use the `superpowers:finishing-a-development-branch` skill to decide between merge / PR / further work.

---

## Task summary

```
[ ] Pre-flight                                      (verify environment)
[ ] A1  — Env-var-first config in Program.cs        (commit)
[ ] B1  — dais-bridge/.dockerignore                 (commit)
[ ] B2  — dais-bridge/Dockerfile + image builds     (commit)
[ ] B3  — compose.yaml                              (commit)
[ ] B4  — Amend .env.example with dev-stack vars    (commit)
[ ] B5  — arango+lm-probe smoke (no commit)
[ ] B6  — dev-profile smoke (no commit)
[ ] B7  — prod-profile smoke (no commit)
[ ] C1  — Makefile                                  (commit)
[ ] C2  — End-to-end Makefile smoke (no commit)
[ ] D1  — docs/dev-environment.md                   (commit)
[ ] D2  — README.md + CLAUDE.md                     (commit)
[ ] D3  — OBSIDIAN-CONTENT-WORKFLOW.md              (commit)
[ ] D4  — TODO-phase11.md + RESUME-graph-backed-rag (commit)
[ ] D5  — HANDOFF.md Phase 12 entry                 (commit)
[ ] Final verification + (optional) PR
```

Total: ~11 commits across 8 modified files and 6 created files.
