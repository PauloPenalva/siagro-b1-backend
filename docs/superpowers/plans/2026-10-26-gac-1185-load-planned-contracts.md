# GAC-1185 — Contratos planejados na Carga — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Na Carga, o usuário pode informar (opcionalmente) quais contratos de compra serão carregados e quais contratos de venda serão atendidos, antes da operação, e o Painel de Liberação de Pagamento mostra as cargas planejadas de cada contrato de compra.

**Architecture:** Duas tabelas filhas da carga (`SHIPMENT_LOADS_PLANNED_PURCHASE_CONTRACTS`, `SHIPMENT_LOADS_PLANNED_SALES_CONTRACTS`), editadas por actions OData unbound (incluir/excluir), no mesmo padrão dos comentários da carga (`ShipmentLoadsCommentCreateService`), com registro no log de alterações. No frontend, duas grids na tela de Detalhe da carga (padrão de `ShipmentLoadTransshipments`) e uma coluna no painel `paymentReleases`.

**Tech Stack:** .NET 10, EF Core (SQL Server), ASP.NET Core OData v4, xUnit; OpenUI5 + TypeScript.

**Spec:** chamado GAC-1185 (texto do usuário) + decisões registradas abaixo. O usuário dispensou o brainstorming; as decisões são minhas e estão marcadas.

**Worktrees:** `C:\Projetos\SiagroB1\wt-1185\siagro-b1-backend` e `...\wt-1185\siagro-b1-frontend`, branch `feature/gac-1185-load-planned-contracts` nos dois (a sessão principal está ocupada com GAC-1191).

## Decisões (minhas)

- Planejamento é **opcional** e **não trava** nada: não altera saldo, alocação, faturamento.
- Linha = contrato + quantidade prevista (opcional, `DECIMAL(18,3)`, > 0 se informada) + observação (opcional, até 200).
- Contrato precisa estar **Aprovado** e ter o **mesmo ItemCode da carga**; o mesmo contrato não se repete na mesma carga.
- Edição permitida em qualquer status **exceto Cancelada**.
- Cada inclusão/exclusão grava no log de alterações (`ShipmentLoadChangeLogFields.PlannedPurchaseContract` / `PlannedSalesContract`).
- Excluir a carga apaga as linhas planejadas (`ShipmentLoadsDeleteService`).
- Painel de Liberação de Pagamento: coluna "Cargas planejadas" (códigos das cargas não canceladas), via navegação `PurchaseContract.PlannedShipmentLoads`.

## Global Constraints

- Identificadores em inglês; textos ao usuário em pt-BR.
- Parâmetro numérico de action = `Parameter<double>`, nunca decimal.
- Filtro de status do contrato no `$filter` estático do fragment de value help, nunca `sap.ui.model.Filter`.
- Migration com `ASPNETCORE_ENVIRONMENT=Development`; nunca o profile db-migration.
- Commits: `feat(shipment): ...` com `Refs: GAC-1185` e `DB: <Migration>` no backend; nunca push.

## Review Focus

1. Contrato de outro produto → recusado com mensagem clara (teste no Task 2).
2. Mesmo contrato duas vezes na carga → recusado (Task 2).
3. Carga cancelada → incluir/excluir recusado (Task 2).
4. Excluir carga com planejamento → não estoura FK (Task 2, teste no DeleteService).
5. Contrato excluído/cancelado depois de planejado → linha continua visível; delete do contrato não quebra (FK `NoAction` + remoção manual nos delete services de contrato, teste no Task 2).

---

### Task 1: Entidades, EF, migration, EDM de leitura

**Files:**
- Create: `SiagroB1.Domain/Entities/ShipmentLoadPlannedPurchaseContract.cs`, `ShipmentLoadPlannedSalesContract.cs`
- Modify: `ShipmentLoad.cs` (coleções `PlannedPurchaseContracts`, `PlannedSalesContracts`), `PurchaseContract.cs` (`PlannedShipmentLoads`), `SalesContract.cs` (`PlannedShipmentLoads`), `AppDbContext.cs` (DbSets + FKs `NoAction`), `ODataConfigurations.cs` (entity sets `ShipmentLoadsPlannedPurchaseContracts`/`ShipmentLoadsPlannedSalesContracts`)
- Create: read controllers `ShipmentLoadsPlannedPurchaseContractsController.cs`, `ShipmentLoadsPlannedSalesContractsController.cs` (rotas `odata/ShipmentLoads({key})/PlannedPurchaseContracts` etc.)
- Migration: `AddShipmentLoadPlannedContracts`
- Test: `SiagroB1.Application.Tests/ShipmentLoads/ShipmentLoadPlannedContractsEdmModelTests.cs`

Entity fields (both): `Guid? Key` (identity), `Guid ShipmentLoadKey`, `Guid ContractKey` + nav `Contract`, `decimal? PlannedQuantity`, `string? Notes` VARCHAR(200), `DateTime CreatedAt`, `string CreatedBy` VARCHAR(100). Unique index (`ShipmentLoadKey`,`ContractKey`).

- [ ] EDM test: entity sets exist, `ShipmentLoad` exposes both navs, `PurchaseContract` exposes `PlannedShipmentLoads` → fails
- [ ] Implement; test passes
- [ ] `dotnet ef migrations add AddShipmentLoadPlannedContracts --context AppDbContext`; inspect; apply on local db
- [ ] Commit

### Task 2: Serviços + actions incluir/excluir

**Files:** `Services/ShipmentLoads/ShipmentLoadsPlannedContractAddService.cs` (`ExecuteAsync(Guid loadKey, PlannedContractType type, Guid contractKey, decimal? quantity, string? notes, string user)`), `ShipmentLoadsPlannedContractRemoveService.cs` (`ExecuteAsync(PlannedContractType type, Guid key, string user)`); enum `PlannedContractType { Purchase, Sales }` em Domain/Enums; actions `ShipmentLoadsPlannedContractAdd(LoadKey, ContractType, ContractKey, Quantity: double?, Notes)` e `ShipmentLoadsPlannedContractRemove(ContractType, Key)`; DI; change-log fields; `ShipmentLoadsDeleteService` e delete services de contrato removem as linhas.
**Test:** `ShipmentLoadPlannedContractsServiceTests.cs`

- [ ] Tests: adds purchase & sales; rejects not-approved ("O contrato {code} não está aprovado."), other item ("O contrato {code} é de outro produto."), duplicate ("O contrato {code} já está planejado nesta carga."), cancelled load ("A carga {code} está cancelada."), quantity <= 0; remove works; both write change log; load delete removes rows
- [ ] Implement; tests pass; full `dotnet test` green
- [ ] Commit

### Task 3: Frontend — grids no Detalhe da carga

**Files:** `view/shipmentLoads/fragments/ShipmentLoadPlannedContracts.fragment.xml` (duas `sap.ui.table.Table` em painéis "Contratos de compra planejados"/"Contratos de venda planejados": Contrato, Parceiro, Quantidade prevista, Saldo do contrato, Observação, Incluído por/em; botões Incluir/Excluir), `ShipmentLoadPlannedContractDialog.fragment.xml` (contrato via `PurchaseContractsApprovedSelectDialog`/`SalesContractsSelectDialog` filtrado pelo ItemCode da carga, quantidade, observação); `Detail.view.xml` (nova aba/seção); handlers em `Detail.controller.ts` usando `bindContext("/ShipmentLoadsPlannedContractAdd(...)")`.

- [ ] `yarn ts-typecheck` e `yarn lint` limpos
- [ ] Verificar no navegador pela home → Carga → Detalhe: incluir, ver na grid, excluir, ver no log
- [ ] Commit

### Task 4: Painel de Liberação de Pagamento

**Files:** `view/paymentReleases/Main.view.xml` (coluna "Cargas planejadas"), `controller/paymentReleases/Main.controller.ts` (`$expand PlannedShipmentLoads($expand=ShipmentLoad($select=Code,Status))`, formatter que junta códigos de cargas não canceladas; incluir na exportação).

- [ ] Verificar no navegador com contrato planejado numa carga
- [ ] Commit
