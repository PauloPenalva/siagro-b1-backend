# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

SiagroB1 backend: a .NET 10 solution that provides an ERP/agribusiness back office (grain purchasing/sales contracts, warehouse/storage, weighing tickets, truck scales, business partners, NFe/e-invoicing) with optional integration into SAP Business One. Three independently-hosted ASP.NET Core apps plus one background worker are built from this solution.

## Build / run / test

There is no test project, no CI config, no `.editorconfig`, and no lint/formatter setup in this repo.

```bash
dotnet build SiagroB1.sln
```

Ignore `SiagroB1.Web/SiagroB1.Web.sln` — it's a stray single-project solution file; always build/open the root `SiagroB1.sln`.

To run locally, three apps need to be started (each has its own `launchSettings.json` under `<Project>/Properties/`):

| Project | Purpose | Dev URL |
|---|---|---|
| `SiagroB1.Web` | Internal OData API — not meant to be hit directly | `http://localhost:50000` |
| `SiagroB1.Gateway` | Public entry point: YARP reverse proxy + auth + SPA host | `http://localhost:5246` |
| `SiagroB1.Reports` | FastReport-based PDF report generation | `http://localhost:8081` (Gateway's proxy target; `dev` launch profile itself listens on 58000) |

`dotnet run --project SiagroB1.Web --launch-profile dev` (same pattern for Gateway/Reports). The `siagro-b1-frontend` app talks to the Gateway (`localhost:5246`), which proxies `/odata` and `/reports` onward to Web/Reports via YARP and handles `/security` (auth) itself.

`SiagroB1.Client` is a standalone Worker Service (weighbridge/truck-scale TCP reader) with no project references to anything else — run/deploy it independently of the other three.

`SiagroB1.Migrations` is a class library, not runnable — it only holds generated EF Core migration files for two DbContexts. EF migrations are applied by running `SiagroB1.Web` with the `db-migration` launch profile (`ASPNETCORE_ENVIRONMENT=Migration`), not via a separate `dotnet ef` CLI workflow — check `Program.cs` for the `Environment == "Migration"` branch before changing migration behavior.

`appsettings.json` in `SiagroB1.Web` and `SiagroB1.Gateway` contain plaintext SQL Server credentials committed to source control — be careful not to widen exposure when editing these files, and don't copy real credentials into examples/docs.

## Architecture

Project reference graph:

```
Commons ─┐
Domain ──┼─> Infra ──> Security ──┬─> Application ──> Web
         │                        ├─> Reports
         │                        └─> Gateway
Migrations ──> Infra
Client (standalone, no project references)
```

- **Domain** — entities, enums, DTOs, interfaces, exceptions. Notably depends directly on `Microsoft.EntityFrameworkCore` (not a framework-agnostic domain layer).
- **Infra** — four `DbContext`s in `Context/`: `AppDbContext` and `CommonDbContext` (this app's own SQL Server DBs), plus `SapErpDbContext`/`SapCommonDbContext` (SAP B1's own databases, only wired up in SAP mode). Uses EF Core for most access but also Dapper/Dapper.Contrib for raw SQL, `UnitOfWork`/`IUnitOfWork`, Hangfire (+ SQL Server storage), `B1SLayer` (SAP Business One Service Layer HTTP client), `Zeus.Net.NFe.NFCe` (Brazilian e-invoicing).
- **Security** — auth/user/branch/menu services. No ASP.NET Identity; a hand-rolled `BasicAuthenticationHandler` plus a cookie scheme (`SIAGROB1` cookie, 8h sliding expiration). No JWT.
- **Application** — business logic organized as one class per operation under `Services/<Feature>/`, e.g. `PurchaseContractsCreateService`, `PurchaseContractsUpdateService`, `PurchaseContractsGetService`, `PurchaseContractsDeleteService` rather than one CRUD service per feature. This is CQRS-shaped by convention only — there is no MediatR, no command/query bus. Every service is registered individually (not assembly-scanned, despite `Scrutor` being referenced) in the ~150+ line `AddApplicationServices()` in `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs` — new services must be added there by hand.
- **Web** — OData API. Controllers extend `ODataController` (one per entity set), stay thin, and call directly into an injected `I<Feature>Service` — services call EF Core directly, there is no repository abstraction layer beyond that. Swagger/OpenAPI (Swashbuckle) only in Development. Hosts a raw WebSocket endpoint (`MapTruckScaleWebSocket()`) for the truck-scale integration and one Hangfire recurring job (`storage-daily-calculation-job`, cron `0 1 * * *`) registered in `Program.cs`.
- **Gateway** — YARP reverse proxy configured in `appsettings.json` (`ReverseProxy:Routes`/`Clusters`): `/odata/**` → Web (`localhost:50000`), `/reports/**` → Reports (`localhost:8081`), both gated by an `AuthenticatedOnly` policy enforced at the gateway. Also serves the built frontend SPA as static files (`wwwroot`) with cache-busting on `index.html`. This is the BFF/API-gateway pattern — Web and Reports are not meant to be exposed publicly.
- **Reports** — separate ASP.NET Core app using FastReport.OpenSource to generate PDFs, reuses `AppDbContext`/`CommonDbContext` directly. Note: `UseAuthentication()`/`UseAuthorization()` are commented out in its `Program.cs` — auth for reports is enforced upstream at the Gateway, not in this service itself.
- **Client** — isolated Worker Service reading truck-scale weight data over TCP (`Readers/TcpScaleReader`, with a `Mock/MockScaleReader` for local dev without hardware).

**ERP integration mode (the app's "multi-tenancy")**: `SiagroB1.Web/Program.cs` reads a config key `Erp` (`SAPB1` vs `STANDALONE`) at startup and switches DI wiring between `AddSapServices()` and `AddStandAloneServices()`, registering different implementations of services like `IAgentService`/`IItemService` (SAP Business One-backed vs local-DB-backed). This is a per-deployment integration mode, decided at startup — not per-request tenant isolation. When adding a new entity/service that needs to work in both modes, check how existing services (e.g. `IAgentService`) branch between SAP and standalone implementations before adding a new one.

**DB**: SQL Server only (`Microsoft.EntityFrameworkCore.SqlServer`). Migrations use `QuerySplittingBehavior.SplitQuery`. Localization: pt-BR default, en-US supported, resource files in `SiagroB1.Commons/Resources`.

**No messaging/queue infrastructure** — async/background work goes through Hangfire (recurring jobs) or the raw WebSocket channel for the truck scale, not a broker.
