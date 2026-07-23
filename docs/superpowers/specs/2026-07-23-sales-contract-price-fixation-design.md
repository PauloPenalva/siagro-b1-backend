# Fixação de preço no contrato de VENDA (espelho do PAF de compra)

**Data:** 2026-07-23
**Status:** Implementado (não commitado — commits são manuais). Migrations ainda não aplicadas.
**Escopo:** contratos `SalesContract` do tipo `ContractType.ToBeDetermined` (PAF, "preço a fixar") e o
ciclo de vida das fixações de preço de venda, incluindo o relatório espelho.

Espelha a feature de compra descrita em
[`2026-07-20-purchase-contract-price-fixation-design.md`](./2026-07-20-purchase-contract-price-fixation-design.md).
Leia aquele doc para o racional completo dos dois eixos de aprovação, semântica de status e imutabilidade —
aqui documentamos apenas o que é **específico ou diferente** no lado da venda.

## Problema

O contrato de venda só precificava com um campo plano `SalesContract.Price` e `TotalPrice = TotalVolume ×
Price`. Não havia mecanismo de fixação: o enum `ContractType` já tinha `ToBeDetermined`, mas nada consumia.
O negócio precisa, na venda, do mesmo mecanismo de compra — fixar o preço em N parcelas, com aprovação da
diretoria — para contratos a fixar.

## Decisões (confirmadas com o usuário)

1. **Espelho completo:** entidade, migration, serviços, OData actions/controllers, fila de aprovação da
   diretoria, fragmentos no Detail e **relatório espelho** (PDF).
2. **`Price = 0` só para PAF.** Contrato `Fixed` EXIGE preço (`Price > 0`, validado antes do `try` que
   mascara exceções no create) e MANTÉM o valor; PAF nasce `Price = 0` forçado no servidor.
3. **`TotalPrice` deriva das fixações `Confirmed`** (soma `FixationPrice × FixationVolume`), igual a compras.
   Para `Fixed`, vem da fixação `Confirmed` automática (= `Price × TotalVolume`).
4. **A fixação é a fonte única do preço.** `Fixed` gera uma fixação `Confirmed` automática na criação a
   partir do `Price` (mantido); a reconciliação de alocação passa a snapshotar o preço confirmado das
   fixações, não `contract.Price` diretamente.

## Pontos não óbvios (armadilhas resolvidas)

### Snapshot de preço da alocação com fallback (o mais delicado)

A feature de alocação de venda (ledger `SALES_CONTRACTS_ALLOCATIONS`) grava `ContractPrice = contract.Price`
no faturamento e é a base da reconciliação (Total Entregue / Diferença). Trocar isso ingenuamente por
"preço das fixações" zeraria a reconciliação de todo contrato antigo/fixo.

**Solução:** `SalesContractsFixedVolumeService.ConfirmedUnitPriceAsync(contractKey, fallbackPrice)` = média
ponderada por volume das fixações `Confirmed`, **com fallback para `contract.Price` quando não há fixação
confirmada**. `SalesContractsAllocationCreateService` e `...CreateForReturnService` passam `contract.Price`
como fallback. Resultado:
- `Fixed` (real ou seed de teste): `= Price` → reconciliação idêntica à anterior, testes intactos.
- PAF: `= preço das fixações aprovadas`, ou 0 enquanto nenhuma foi confirmada (aceito nesta fase).

### `TotalPrice` zera sem `Include(PriceFixations)`

`TotalPrice` virou computado sobre a navegação `PriceFixations` e retorna 0 silenciosamente sem o Include
(ver a armadilha geral do `AvaiableVolume`). Correções:
- `SalesContractsTotalsService.GetTotals` ganhou `.Include(x => x.PriceFixations)`.
- O header do `Detail.view.xml` liga **`viewModel>/TotalPrice`** (carregado de `SalesContractsGetTotals`,
  `sap.ui.model.type.Float`), NÃO o `TotalPrice` do contexto OData. `refreshContractTotals` recarrega esse
  endpoint após cada operação de fixação.

### Backfill de paridade na migration

Como `TotalPrice` passou a derivar das fixações, TODO contrato de venda antigo (que não tem fixação) ficaria
com `TotalPrice = 0`. A migration `AddSalesContractPriceFixation` insere (SQL na `Up`) uma fixação
`Confirmed` cobrindo `TotalVolume` a `Price` para cada contrato `Fixed` (Type=0, Price>0, TotalVolume>0), e
atualiza `FixedVolume`. Espelha o `ConfirmFixedContractAutoFixations` de compra.

### Rota do Detail precisava de `:?query:`

O "Ver Contrato" da fila de aprovação abre o contrato com `?readonly=true`. A rota `salesContractsDetail` no
`manifest.json` estava sem o segmento `:?query:` (a de compra tinha), então o parâmetro nunca chegava ao
controller e nenhum botão era escondido. Corrigido para `sales-contracts/{id}/detail:?query:`. Com
`ui>/readonly`, TODOS os botões de mutação (header + fixação + anexos) somem; ficam só os de leitura
(Download, Detalhes).

## Arquitetura / arquivos

### Domínio
- `SalesContractPriceFixation` (`SALES_CONTRACTS_PRICE_FIXATIONS`, herda `BaseEntity`; reusa o enum
  `PriceFixationStatus`).
- `SalesContract`: coluna persistida `FixedVolume` (protegida pelo `RowVersion` já existente), nav
  `PriceFixations`, computado `AvailableVolumeToPricing`, `TotalPrice` reescrito.
- DbSet `SalesContractsPriceFixations` em `AppDbContext`.

### Application (`Services/SalesContracts/`)
- `SalesContractsFixedVolumeService` — ponto ÚNICO de recálculo de `FixedVolume`; +
  `ConfirmedVolumeAsync`, `DeliveredVolumeAsync` (Σ `SalesShipmentRelease.ShippedQuantity`),
  `ConfirmedUnitPriceAsync`.
- `SalesContractsPriceFixationCreateService`, `...Approval/Reject/Cancel/Update/Delete/Get` — 1:1 com compra.
- Ajustes: `SalesContractsCreateService` (validação Price + auto-fixação `Fixed`, `Price=0` PAF),
  `SalesContractsCloseService` (guarda PAF), `SalesContractsAllocationCreate/CreateForReturnService`
  (snapshot via `ConfirmedUnitPriceAsync`), `SalesContractsTotalsService` (Include).
- Todos registrados à mão em `AddApplicationServices()`.

### Web
- EntitySet `SalesContractsPriceFixations` + computado `AvailableVolumeToPricing` no `ODataConfigurations`.
- Actions `SalesContractsPriceFixation{Create,Delete,Approval,Reject,Cancel}` + controllers em
  `Actions/SalesContracts/`; CRUD/fila em `Controllers/SalesContractsPriceFixationsController`.

### Migrations (hand-edited; geradas com `dotnet ef migrations add`, diff limpo)
- AppContext `20260723143258_AddSalesContractPriceFixation` — tabela + coluna `FixedVolume` + backfill.
- CommonContext `20260723143444_AddSalesPriceFixationApprovalMenu` — `MENU_ITEMS` (key
  `salesContractsPriceFixationApproval` = rota) + `ROLE_MENUS` ADMIN.
- Aplicar com `ASPNETCORE_ENVIRONMENT` explícito (o perfil `db-migration` pode apontar para produção).

### Frontend (`webapp/`)
- Fragmentos `salesContracts/fragments/SalesContractPriceFixations` / `PriceFixationDialog` /
  `PriceFixationDetailsDialog`; seção "Fixações de Preço" no `Detail.view.xml`.
- Handlers no `SalesContractsBaseController.ts` (substituíram o `onAddPriceFixation` com status fantasma
  "Pending"); actions via `bindContext`.
- Fila de aprovação `view|controller/salesContracts/priceFixationApproval/` + rota/target no manifest.
- Campo Preço do form gated por `Type === 'Fixed'`; rota do Detail com `:?query:` e gating readonly.
- `ServerRoutes`: rotas das actions + `salesPriceFixationReport`.

### Reports
- `SalesPriceFixationReportService` + `SalesPriceFixationController` (`/reports/SalesPriceFixation/{key}/print`)
  + template `Reports/Templates/SalesPriceFixation.frx` (cópia do de compra, rótulo "Cliente"). Reusa
  `PriceFixationPrintDto`; parceiro via `IPartnerSource`; só emite `Confirmed`. Auto-registrado (Scrutor);
  o `.frx` é auto-descoberto pelos testes de template.

## Testes

`SiagroB1.Application.Tests/SalesContracts/`: create (saldo/guardas), approval/reject, cancel (estorno →
InApproval, volume reservado), mutability (update/delete só InApproval), FixedVolume (recálculo, delivered,
`ConfirmedUnitPrice` com/sem fallback, QueryPending), TotalPrice (só Confirmed), close guard PAF, snapshot de
alocação por fixação. O `.frx` novo entra automaticamente no header test + render smoke.

**Resultado:** 365/365 testes passam. Backend build limpo; frontend `tsc`/`eslint` limpos; `yarn build` ok.

## Pendências (não são código)

- Aplicar as duas migrations num ambiente de dev (env explícito).
- Verificação pelo caminho do usuário no browser (subir Web+Gateway+Reports+frontend e clicar o fluxo).
- Commit (manual).
