# Darbee Sovereign AI Gateway: Unified Design Specification

## Overview
The Sovereign AI Gateway is the central intelligence hub for the Darbee Family. It is a local-first, .NET 9 Minimal API service that unifies **KidSafe AI** (deterministic safety and education) and **DAIS** (agentic content intelligence). It provides a real-time, multi-tenant interface between the family's hardware (AMD Ryzen AI Max+ 395) and their lived wisdom (ArangoDB Knowledge Graph).

## 1. Unified Architecture
The gateway operates as a "Librarian Supervisor," managing a shared Semantic Kernel orchestrator that dispatches specialized tasks based on user intent and safety profiles.

### Core Hosting
- **Platform:** .NET 9 ASP.NET Core Minimal APIs.
- **Real-time Engine:** SignalR for low-latency voice, chat, and parent alerts.
- **Isolation:** Multi-tenant domain tagging (`kid:*`, `family:*`) at the database and memory layer.
- **Inference:** Local LM Studio (Reasoning, Embeddings, Vision).

## 2. Deterministic Safety Layer
The "Iron Law" of the platform is enforced by a C# Request Middleware pipeline that intercepts all traffic before it reaches the AI.

### The Safety Pipeline
1.  **Keyword & Regex Filter:** Uses `safety_policies.json` to block non-negotiable topics (guns, social media, reproductive content).
2.  **PII Redactor:** GLiNER-based detection to strip personal identifiers.
3.  **Biblical Alignment Filter:** Fast local scoring to ensure content matches family values.
4.  **Immutable Audit Log:** Every request and safety decision is logged to ArangoDB with full versioning.
5.  **Parent Alert System:** Real-time SignalR notifications to the Parent Dashboard for flagged violations.

## 3. Knowledge Graph & Multi-Tenancy (ArangoDB)
The gateway manages a single Knowledge Graph with strict tenant isolation logic.

- **Tenant Injection:** Every database query is scoped by the user's `ITenantContext`.
- **Dual-Node Strategy:** The `LegacyGraphPlugin` creates high-fidelity family nodes and simplified "Safe-Retrieval" nodes simultaneously during publishing.
- **Auto-Complexity Engine:** Adjusts content reading levels (Preschool to Middle School) on-the-fly while preserving the Darbee family voice.

## 4. Operational Workflows

### KidSafe Workflow
- **Input:** Voice/Text stream via SignalR.
- **Verify:** Deterministic middleware checks against JSON policy.
- **Process:** Semantic Kernel retrieves data, `ComplexityPlugin` adjusts reading level.
- **Output:** "Calm Storyteller" response via Piper TTS.

### DAIS Publishing Workflow
- **Source:** Obsidian `✨ Process` status change.
- **Enrich:** Image classification (local LLaVA) and GEO metadata generation.
- **Publish:** Emits clean MDX to Astro 6, commits to Git, and triggers Cloudflare deploy.
- **Legacy:** Updates ArangoDB with dual legacy/safe nodes.

## 5. Engineering Principles
- **Reliability > Capability:** Deterministic logic always overrides probabilistic LLM output for safety.
- **Sovereignty:** 100% local operation; no cloud dependencies for core safety or reasoning.
- **Legacy:** Multi-generational provenance for every piece of captured wisdom.
