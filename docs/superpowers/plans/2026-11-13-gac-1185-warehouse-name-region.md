# GAC-1185 rodada 3 — nome do armazém e Região Logística — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Mostrar nome fantasia do armazém e Região Logística do contrato no diálogo de seleção de liberação de compra do planejamento da Carga.

**Architecture:** A function `ShipmentReleasesGetPurchaseContracts` passa a devolver três campos a mais (aditivo); o diálogo do frontend ganha duas colunas ligadas a eles.

**Tech Stack:** .NET 10 / EF Core / xUnit (backend); OpenUI5 1.141 + TypeScript / QUnit (frontend).

**Spec:** `docs/superpowers/specs/2026-11-13-gac-1185-warehouse-name-region-design.md`

## Global Constraints

- Branch `feature/gac-1185-warehouse-name-region` nos dois repos; nunca push; merge é do usuário.
- Identificadores em inglês, textos de tela em pt-BR.
- Sem migration.
- Commits: `tipo(shipment): ...` em pt-BR + `Refs: GAC-1185`.

## Review Focus

- Armazém sem AliasName → mostra a razão social do cadastro (teste na Task 1).
- Armazém fora do cadastro (código órfão) → mostra o nome gravado na liberação (teste na Task 1).
- Contrato sem região → campos nulos, coluna vazia, sem "() " (teste na Task 1; o formatter já é testado).
- Busca digitada por nome do armazém e por região (teste na Task 2).
- Outras telas que usam a function (Expedição, troca de liberação) seguem funcionando: mudança só aditiva; os testes existentes do serviço continuam passando.

---

### Task 1: Backend — campos novos na function

**Files:**
- Modify: `SiagroB1.Domain/Dtos/ShipmentRelesesPurchaseContractsResponseDto.cs`
- Modify: `SiagroB1.Application/Services/ShipmentReleases/ShipmentReleasesPurchaseContractsService.cs`
- Modify: `SiagroB1.Application.Tests/Support/FakeWarehouseService.cs` (`LoadWarehousesAsync` devolve um mapa configurável `Warehouses`, vazio por padrão)
- Modify: os 3 testes existentes `ShipmentReleasesPurchaseContractsService*Tests.cs` (novo parâmetro do construtor)
- Test: `SiagroB1.Application.Tests/ShipmentReleases/ShipmentReleasesPurchaseContractsServiceWarehouseRegionTests.cs`

**Interfaces:**
- Produces (JSON da function, PascalCase via `JsonPropertyName`): `WarehouseFName: string?`, `LogisticRegionCode: string?`, `LogisticRegionName: string?`.
- Construtor: `(IUnitOfWork db, IBusinessPartnerService businessPartnerService, IWarehouseService warehouseService, ILogger<...> logger)`.

- [ ] **Step 1: testes que falham** — `WarehouseFName_UsesAliasName_WhenFilled`, `WarehouseFName_FallsBackToCardName_WhenAliasNameIsEmpty`, `WarehouseFName_FallsBackToReleaseName_WhenWarehouseIsNotInRegistry`, `LogisticRegion_ComesFromContract`, `LogisticRegion_IsNull_WhenContractHasNone`.
- [ ] **Step 2: rodar e ver falhar** — `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~ShipmentReleasesPurchaseContractsService"`.
- [ ] **Step 3: implementar** — regra do nome: `AliasName` → `CardName` do cadastro → `DeliveryLocationName` da liberação; região vem de `PurchaseContract.LogisticRegionCode` / `LogisticRegion.Name` na projeção.
- [ ] **Step 4: rodar e ver passar** (mesmo filtro) + `dotnet build SiagroB1.sln`.
- [ ] **Step 5: commit** (spec + plano em commit `docs` próprio).

### Task 2: Frontend — colunas e busca

**Files:**
- Modify: `webapp/helpers/PlannedReleaseHelpers.ts` (tipo + `PURCHASE_SEARCHED_FIELDS`)
- Modify: `webapp/view/shipmentLoads/fragments/ShipmentLoadPlannedPurchaseReleaseDialog.fragment.xml`
- Modify: `package.json` (versão 1.1.314, commit `chore` próprio)
- Test: `webapp/test/unit/helpers/PlannedReleaseHelpers.qunit.ts`

**Interfaces:**
- Consumes: os três campos da Task 1.

- [ ] **Step 1: teste que falha** — a busca encontra a liberação por `WarehouseFName`, por `LogisticRegionCode` e por `LogisticRegionName`.
- [ ] **Step 2: implementar** — coluna "Nome Armazém" (`WarehouseFName`, 14rem) logo após "Armazem"; coluna "Região Logística" (`formatter.formatCodeAndName(LogisticRegionCode, LogisticRegionName)`, 12rem, ordena pelo código, filtra pelo nome) após ela; placeholder da busca cita armazém e região.
- [ ] **Step 3: verificar** — `yarn ts-typecheck`, `yarn lint`, `yarn test`.
- [ ] **Step 4: verificar no navegador** — stack local, abrir uma Carga, Contratos Planejados → Incluir (compra): colunas preenchidas, ordenação, filtro e busca.
- [ ] **Step 5: commit.**
