---
name: implement-feature
description: The master orchestration skill for building a complete vertical slice in FarmOS autonomously. Ties together planning, backend scaffolding, frontend standardized UI, documentation, and library research via context7.
---
# Implement Feature (Master Orchestrator)

This is the **master workflow** for building new capabilities in FarmOS. When the user asks you to "implement a feature" or "execute a plan", you MUST follow this exact sequence autonomously to build the feature on the fly without requiring step-by-step micro-prompts.

## 1. Plan Analysis (The "On the Fly" Spec)
- DO NOT invent the domain rules. Read the specified plan document in `docs/plans/YYYY-MM-DD-feature.md` (or ask the user for the plan if not provided).
- Extract the Bounded Context, the Domain Event, the F# rules (if applicable), and the required UI fields.

## 2. Context & Library Research (Context7)
- **CRITICAL**: Before writing frontend UI code (Deno Fresh, Preact, Arrow.js) or utilizing external libraries, you MUST use the **context7 MCP server** to fetch current documentation.
- Do not guess library syntax; use `resolve-library-id` and `query-docs` on `context7` to ensure your implementation uses the latest, correct patterns for web frameworks and dependencies.

## 3. Backend Scaffolding (Domain & API)
- Apply the rules from the **`create-vertical-slice`** skill.
- Create the Domain Event in the SharedKernel.
- Implement the Command and Handler in the specified Bounded Context (e.g., `FarmOS.Apiary.Application`).
- Expose the HTTP boundary in the `*Endpoints.cs` file.
- Implement any required F# rules in `FarmOS.Hearth.Rules` if the plan dictates strict biological/ecological invariants.

## 4. Frontend Scaffolding (UI Generation)
- Apply the rules from **`arrow-reactivity.md`** and **`arrow-js-skill.md`**.
- Create the Deno Fresh route in the frontend micro-app (e.g., `frontend/apiary-os/routes/`).
- Scaffold the interactive UI components as Arrow.js islands (`islands/`).
- Bind the UI directly to the backend API endpoint you just created. DO NOT invent custom UI designs; stick to the standard `<StandardTable>` and `<StandardForm>` layouts defined in the project.

## 5. Documentation Sync
- Automatically apply the **`sync-api-docs.md`** rule.
- Update the relevant markdown API reference in `docs/api-reference-{context}.md` with the new endpoint, expected request body, and response types.

## 6. Verification & Walkthrough
- Provide the user with a concise summary of the generated slice, detailing the files touched across the Domain, API, and Frontend layers.
- If any part of the plan was ambiguous, highlight the assumptions made during generation.
