.DEFAULT_GOAL := help
COMPOSE := podman compose

.PHONY: help up up-dev up-prod up-stack up-stack-prod down down-stack \
        restart build rebuild ps logs logs-arango logs-lm logs-bridge \
        logs-llm-chat logs-llm-embed shell-bridge shell-arango health \
        clean init podman-socket llm-up llm-down llm-status llm-restart

help:                              ## List available targets
	@awk 'BEGIN {FS = ":.*?## "} /^[a-zA-Z_-]+:.*?## / \
		{printf "  \033[36m%-15s\033[0m %s\n", $$1, $$2}' $(MAKEFILE_LIST)

init:                              ## First-time setup: ensure .env exists
	@test -f .env || (cp .env.example .env && \
		echo "Created .env from .env.example — fill in AI_API_KEY and verify LLM_CHAT_URL / LLM_EMBEDDING_URL")

podman-socket:                     ## Ensure the user podman socket is running (needed by compose)
	@systemctl --user is-active podman.socket >/dev/null 2>&1 || \
		systemctl --user start podman.socket

up: up-stack                       ## Bring up everything: podman stack + host llama-servers

up-stack: init podman-socket llm-up  ## Start dev compose stack + host llama-servers
	$(COMPOSE) --profile dev up -d

up-stack-prod: init podman-socket llm-up  ## Start prod compose stack + host llama-servers
	$(COMPOSE) --profile prod up -d

up-dev: init podman-socket         ## Start dev compose stack only (no llama-servers)
	$(COMPOSE) --profile dev up -d

up-prod: init podman-socket        ## Start prod compose stack only (no llama-servers)
	$(COMPOSE) --profile prod up -d

llm-up:                            ## Start host llama-servers (chat :8080, embed :8081)
	@bash scripts/llama-up.sh

llm-down:                          ## Stop host llama-servers
	@bash scripts/llama-down.sh

llm-status:                        ## Report llama-server health + PIDs
	@bash scripts/llama-status.sh

llm-restart: llm-down llm-up       ## Restart host llama-servers

logs-llm-chat:                     ## Tail chat llama-server log
	@tail -f .runtime/llama-chat.log

logs-llm-embed:                    ## Tail embedding llama-server log
	@tail -f .runtime/llama-embed.log

down: down-stack llm-down          ## Stop containers AND host llama-servers

down-stack:                        ## Stop and remove containers only
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
	@echo "--- llama-chat (host :8080):" && \
		curl -fsS http://localhost:8080/health >/dev/null && echo "  UP" || echo "  DOWN"
	@echo "--- llama-embed (host :8081):" && \
		curl -fsS http://localhost:8081/health >/dev/null && echo "  UP" || echo "  DOWN"
	@echo "--- DAIS Bridge (5000):" && \
		curl -fsS http://localhost:5000/ || echo "  DOWN"

clean:                             ## down + remove arango-data volume (DESTRUCTIVE)
	$(COMPOSE) --profile dev --profile prod down -v
	@echo "Volumes removed. Next 'make up' will start with an empty database."
