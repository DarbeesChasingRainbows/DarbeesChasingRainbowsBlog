---
name: update-infrastructure
description: Checklist for registering a new FarmOS .NET API service in the Compose + Caddy infrastructure.
---
# Infrastructure Registration Checklist

FarmOS runs all `.NET` domain APIs from the single `Dockerfile.api` parameterised by a `PROJECT_NAME` build arg, fronted by a shared Caddy reverse proxy on port `5050`. Follow this checklist when adding a new service (e.g. a new `FarmOS.{Context}.API` project).

## 1. `docker-compose.yml`

Add a service entry to the root `docker-compose.yml` mirroring the existing pattern (e.g. `apiary-api`, `flora-api`):

- Service name: `{context}-api` (lowercase, hyphenated).
- `build.context: .` and `build.dockerfile: Dockerfile.api`.
- `build.args.PROJECT_NAME: FarmOS.{Context}.API`.
- Env vars: `ASPNETCORE_ENVIRONMENT=Development`, `ASPNETCORE_URLS=http://+:8080`, `ArangoDB__Url=http://arangodb:8529`, `ArangoDB__Password=farmos_dev`.
- Add RabbitMQ env vars (`RABBITMQ_HOST=rabbitmq`, `RABBITMQ_PORT=5672`, `RABBITMQ_USER=farmos`, `RABBITMQ_PASS=farmos_dev`) **only if** the service publishes/consumes cross-context events.
- `depends_on.arangodb-init.condition: service_completed_successfully`; add `rabbitmq.condition: service_started` if using RabbitMQ.
- Do not publish host ports for domain APIs — they are only reached via Caddy.
- No custom network is required; all services share the default Compose network.

## 2. Caddy wiring

In the root `Caddyfile` under the `:5050` block:

- Add a `handle /api/{context}/* { reverse_proxy {context}-api:8080 }` route that mirrors the `/api/{context}` prefix used in `{Context}Endpoints.cs`.
- Keep routes grouped under the existing "Domain APIs" comment.

In `docker-compose.yml`, add the new service to the `caddy.depends_on` map with `condition: service_started` so Caddy waits on it.

## 3. Windmill stack

`windmill/docker-compose.yml` and `windmill/Caddyfile` are a separate Windmill automation stack. They only need updating if the new service is **also** consumed by Windmill flows. For standard domain APIs, skip this step.

## 4. Solution & verification

- Ensure the new API project is listed in `FarmOS.slnx`.
- Build with `docker compose build {context}-api` and smoke-test with `docker compose up {context}-api caddy arangodb arangodb-init`.
- Hit `http://localhost:5050/api/{context}/...` to confirm Caddy routing works.
