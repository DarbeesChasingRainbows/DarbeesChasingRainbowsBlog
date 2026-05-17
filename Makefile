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
		echo "Created .env from .env.example — fill in AI_API_KEY and verify LLM_CHAT_URL / LLM_EMBEDDING_URL")

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
