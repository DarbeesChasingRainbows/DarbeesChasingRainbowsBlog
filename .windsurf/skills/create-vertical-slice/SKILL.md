---
name: create-vertical-slice
description: Guides creation of a new vertical slice (Command/Query, MediatR Handler, Domain Event, Endpoint) in a FarmOS bounded context using MediatR, SharedKernel, and the ArangoDB event store.
---
# Create Vertical Slice

FarmOS uses **MediatR** (not Wolverine) for in-process command/query dispatch, minimal APIs for the HTTP edge, and an ArangoDB-backed event-sourced aggregate pattern via `I{Context}EventStore`.

## Layers & reference example

Use `FarmOS.Apiary.*` as the canonical reference:

- Domain: `src/FarmOS.{Context}.Domain/Aggregates/*.cs`, `Events.cs`, `Types.cs`
- Application: `src/FarmOS.{Context}.Application/Commands/{Context}Commands.cs` + `Commands/Handlers/`, `I{Context}EventStore.cs`
- Infrastructure: `src/FarmOS.{Context}.Infrastructure/{Context}EventStore.cs`, projections
- API: `src/FarmOS.{Context}.API/{Context}Endpoints.cs`, `Program.cs`
- **F# rules (Hearth only for now)**: `src/FarmOS.Hearth.Rules/*.fs` in `FarmOS.Hearth.Rules.fsproj` — pure-logic modules (e.g. `MushroomRules`, `FermentationAnalytics`, `FreezeDryerRules`, `IoTRules`) consumed by C# handlers via `using FsRules = FarmOS.Hearth.Rules.{Module}`.

## Implementation Steps

1. **Domain Layer**
   - If the slice changes aggregate state, define a Domain Event record in `Events.cs` inheriting from the SharedKernel event base.
   - Add or modify the aggregate method in `Aggregates/` so it emits the event; keep business invariants inside the aggregate.

2. **Application Layer**
   - Add a `record {Verb}{Aggregate}Command(...) : IRequest<Result<T, Error>>` (or `IRequest<Result<Unit, Error>>`) to `{Context}Commands.cs`.
   - Add a handler class in `Commands/Handlers/` implementing `IRequestHandler<TCommand, TResult>`. The handler loads the aggregate via `I{Context}EventStore`, invokes the domain method, and persists via `SaveAsync(aggregate, userId, ct)`.
   - Return `Result<T, Error>` so endpoints can `.Match(...)` on success/failure. Follow the pattern already in `ApiaryCommands.cs` + `Handlers/`.

3. **Infrastructure Layer**
   - Only touch `{Context}EventStore.cs` if the aggregate is new or requires a new load/save shape. Register new event types in the context's event type map so replay/MsgPack decoding works.
   - Add/update any projections under the same project if the slice drives a new read model.
   - **Home Assistant-fed data** (sensor readings, weather, device state) must not come in through a new Hearth/Assets/IoT endpoint. Ingest via the existing pattern — `FarmOS.IoT.API/Workers/HASensorPollingWorker.cs` dispatches `RecordTelemetryReadingCommand` on a timer, and `FarmOS.Assets.Infrastructure/HaSensorBridge.cs` proxies on-demand reads. See the `homeassistant-integration` rule.

4. **API Layer**
   - Add a `MapPost`/`MapGet` inside `{Context}Endpoints.cs` under the appropriate `MapGroup`.
   - Inject `IMediator` and call `await m.Send(cmd, ct)`; use `.Match(...)` to return `Results.Created` / `Results.NoContent` / `Results.BadRequest(err)`.
   - If the route carries an id, rebind it with `cmd with { Id = id }` before sending (see `ApiaryEndpoints.cs`).

5. **Docs**
   - Update `docs/api-reference-{context}.md` to match the new endpoint (see the `sync-api-docs` rule). Create the file if it does not yet exist for the context.

## F# rules (Hearth)

`FarmOS.Hearth.Rules.fsproj` is the project's only F# module today. It holds **pure** domain logic (validation ranges, analytics, state machines over DUs) that C# MediatR handlers call into. Examples: `MushroomRules`, `FermentationAnalytics`, `FreezeDryerRules`, `IoTRules`, `ExcursionRules`, `ApothecaryRules`.

- Add new modules as `.fs` files and register them in `<Compile Include="..." />` in `FarmOS.Hearth.Rules.fsproj`. Order matters — dependent modules come later.
- Keep F# modules side-effect free: no DB, no HTTP, no logging, no `MediatR`. Return `Result<_, string>` or a project-defined DU.
- Consume from C# handlers with a `using` alias: `using FsRules = FarmOS.Hearth.Rules.{Module};`. When pattern-matching an F# DU from C#, use the generated `x.IsSafe` / `x.IsWarning` discriminator properties (see `HarvestRightCommandHandlers.MapAlertLevel`).
- Do **not** expose F# types on the HTTP boundary — map DU cases to a C# `enum` or plain record inside the handler before the endpoint returns.
- Other contexts (Apiary, Flora, IoT, etc.) are C#-only today. Create a new `FarmOS.{Context}.Rules.fsproj` following the Hearth template only when a context genuinely needs pure-logic F#.

## Quartermaster federation seam (Commerce / Ledger)

If the new slice touches `Commerce` or `Ledger` and could plausibly involve an external B2B counterparty (wholesale orders, invoicing, B2B payments), check `docs/plans/2026-04-20-quartermaster-federation-design.md` before modeling the feature locally. FarmOS federates with external Quartermaster peers (e.g. Mustang Coffee) through a dedicated `FarmOS.Federation.Quartermaster` adapter — **not** by extending Commerce or Ledger handlers to call external services directly.

- Inbound procurement / invoice / payment events from QM are projected into `Commerce` (`WholesaleAccount` / standing orders) and `Ledger` (`RevenueCategory.Wholesale`, `ExpenseCategory.Processing`) via handlers living in the federation adapter project.
- Outbound catalog events (Apiary/Hearth/Flora availability → QM catalog items) also live there, subscribing to existing domain events.
- Do not import QM types from `Commerce` / `Ledger` / supplier contexts — the federation adapter is the only place that speaks QM's protocol.
- **Wire format**: QM uses JSON (System.Text.Json), not MessagePack. The federation adapter should reference `Quartermaster.Contracts` (via local ProjectReference during nested-now phase, NuGet after extraction) for DTO shapes. See `c:\Work\MustangCoffee\Quartermaster\docs\federation-spec\` for protocol details.
- **Draft endpoints**: Federation handshake, procurement orders, and product-link endpoints are **not yet implemented** in QM. Their DTOs exist in `Quartermaster.Contracts.Drafts` (marked `[Obsolete]`). Do not build adapters against draft types.
- Coinbase Business integration lives in the QM instance, not in FarmOS. FarmOS sees `PaymentConfirmed` as a projected event only.

## Things to avoid

- Do not introduce WolverineFx, MassTransit, or MediatR.Contracts packages — the project is standardized on MediatR.
- Do not bypass the aggregate: never write events directly to the event store from a handler.
- Do not serialize HTTP bodies as MessagePack on new farm-facing endpoints — per `docs/plans/2026-04-20-msgpack-and-arrow.md`, the FarmOS HTTP boundary is JSON. MsgPack is used for event store payloads, the RabbitMQ bus, and **separately** for the Quartermaster federation boundary (which is its own wire contract).
- Do not call Home Assistant directly from a domain handler or endpoint — go through the dedicated worker (`HASensorPollingWorker`) or bridge (`HaSensorBridge`).
- Do not call an external Quartermaster instance directly from a domain handler — go through `FarmOS.Federation.Quartermaster`'s outbound client port.
