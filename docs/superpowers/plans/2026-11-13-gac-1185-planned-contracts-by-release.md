# GAC-1185 (rodada 2) — Contratos planejados da carga por liberação

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** o planejamento de contratos na Carga passa a ser feito a partir das **liberações** de compra e de venda, escolhidas numa grade com as mesmas colunas da Expedição de Grãos (compra) e do diálogo de Faturamento (venda), com largura e ordem das colunas gravadas por usuário.

**Architecture:** as duas tabelas `SHIPMENT_LOADS_PLANNED_*_CONTRACTS` ganham a chave da liberação (anulável: linhas antigas ficam só com o contrato). A action `ShipmentLoadsPlannedContractAdd` passa a receber `ReleaseKey` e deriva o contrato dela. No frontend, o diálogo "Incluir" troca o value help de contratos por uma `sap.ui.table.Table` alimentada pelas functions que já existem (`ShipmentReleasesGetPurchaseContracts`, `SalesShipmentReleasesGetAvailable`); o `TableLayoutService` registra a tabela sozinho via `DialogHelper.createDialog`.

**Tech Stack:** .NET 10 / EF Core / OData v4 (backend, xUnit + EF InMemory); OpenUI5 1.141 + TypeScript (frontend, QUnit).

**Spec:** não há documento separado — o pedido é o chamado GAC-1185 e a decisão do Paulo em 13/11/2026 ("opção 1: gravar a liberação"). As decisões abaixo são o desenho.

## Decisões de desenho

Decididas pelo Paulo:
- O planejado aponta para a **liberação**; o grid da aba mostra dados dela.
- Compra: colunas da tela `shippingTransaction/SelectShipmentRelease`. Venda: colunas da tabela de `shipmentBilling/fragments/Billing`.
- `sap.ui.table.Table` com layout de colunas por usuário.

Minhas (corrigíveis):
- Linhas antigas ficam com `ReleaseKey` nulo e continuam aparecendo (colunas da liberação em branco). Sem backfill: um contrato com várias liberações não tem como ser adivinhado.
- Unicidade passa a ser por `(carga, contrato, liberação)`. Índice único comum (não filtrado): no SQL Server `NULL` conta como valor, então as linhas antigas continuam únicas por contrato.
- A action **troca** `ContractKey` por `ReleaseKey` (sem manter os dois): a tela é o único cliente e os dois repositórios são implantados juntos.
- Regras de inclusão: liberação `Actived`, contrato `Approved`, mesmo produto da carga, liberação ainda não planejada na carga, carga não cancelada. **Saldo não é validado** — o vínculo continua informativo.
- Lista de compra **não** filtra pelo armazém da carga (a function aceita `WarehouseCode` vazio); o usuário ordena/busca pelo armazém.
- Excluir uma liberação remove as linhas planejadas dela (mesma regra que o contrato já segue).
- Seleção simples (uma liberação por inclusão), como hoje.

## Global Constraints

- Identificadores em inglês; textos de tela, mensagens de erro e comentários em pt-BR.
- Branch `feature/gac-1185-planned-by-release` nos dois repositórios; conferir o branch antes de cada commit; **nunca push**, merge em `main` é do Paulo.
- Arquivo novo → `git add` imediato.
- Mensagem de commit: `tipo(shipment): ...` + `Refs: GAC-1185`; commit com migration leva `DB: AddShipmentLoadPlannedContractRelease`.
- `dotnet ef` **sempre** com `ASPNETCORE_ENVIRONMENT=Yokotobi-Development`; nunca `Development` nem o profile `db-migration`. Confirmar o alvo com `dotnet ef dbcontext info` antes de `database update`.
- Ler a migration gerada antes de aplicar: só pode conter as duas colunas, as duas FKs e a troca dos dois índices.
- Deploy: backend + migration antes do frontend.

## Review Focus

1. Linha planejada antiga (sem liberação) na aba: precisa aparecer, com colunas da liberação vazias, e poder ser excluída. → Task 2 (teste de remoção de linha legada) e verificação manual na Task 6.
2. Duas liberações do mesmo contrato na mesma carga: precisam ser aceitas; a mesma liberação duas vezes, recusada. → Task 2.
3. Busca no diálogo com uma linha selecionada: a seleção da `sap.ui.table` é por índice; filtrar não pode deixar marcada outra liberação. → Task 5 (mesmo cuidado de `ShipmentBillingDialog.onReleaseSearch`) e Task 6.
4. Excluir liberação `Pending` que já foi planejada (planejada quando ativa, depois reaberta): não pode dar erro 547. → Task 3.
5. Reabrir o diálogo depois de incluir: seleção, busca, quantidade e observação precisam voltar limpas, e a lista recarregada. → Task 5 e Task 6.

---

## Estrutura de arquivos

Backend (`siagro-b1-backend`):
- Modify `SiagroB1.Domain/Entities/ShipmentLoadPlannedPurchaseContract.cs`, `ShipmentLoadPlannedSalesContract.cs` — chave e navegação da liberação, índice.
- Modify `SiagroB1.Infra/Context/AppDbContext.cs` — FKs `NoAction`.
- Modify `SiagroB1.Domain/Entities/ShipmentLoadChangeLogFields.cs` — descrição com o item da liberação.
- Modify `SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadsPlannedContractAddService.cs`, `...RemoveService.cs`.
- Modify `SiagroB1.Application/Services/ShipmentReleases/ShipmentReleasesDeleteService.cs`, `SalesShipmentReleases/SalesShipmentReleasesDeleteService.cs`.
- Modify `SiagroB1.Web/Actions/ShipmentLoads/ShipmentLoadsPlannedContractAddController.cs`, `SiagroB1.Web/ODataConfig/ODataConfigurations.cs`.
- Create migration `AddShipmentLoadPlannedContractRelease` (gerada).
- Tests: `SiagroB1.Application.Tests/ShipmentLoads/ShipmentLoadPlannedContractsServiceTests.cs`, `ShipmentLoadPlannedContractsEdmModelTests.cs`.

Frontend (`siagro-b1-frontend`):
- Modify `webapp/helpers/BillingReleaseSearch.ts` — extrai o filtro genérico por palavras.
- Create `webapp/helpers/PlannedReleaseHelpers.ts` (+ `webapp/test/unit/helpers/PlannedReleaseHelpers.qunit.ts`, registrado em `unitTests.qunit.ts`).
- Create `webapp/view/shipmentLoads/fragments/ShipmentLoadPlannedPurchaseReleaseDialog.fragment.xml` e `ShipmentLoadPlannedSalesReleaseDialog.fragment.xml`; delete `ShipmentLoadPlannedContractDialog.fragment.xml`.
- Modify `webapp/view/shipmentLoads/fragments/ShipmentLoadPlannedContracts.fragment.xml` — colunas da liberação.
- Modify `webapp/controller/shipmentLoads/Detail.controller.ts` — bloco "Contratos planejados".

---

### Task 1: Modelo — liberação na linha planejada (backend)

**Files:** as duas entidades, `AppDbContext.cs`, `ShipmentLoadChangeLogFields.cs`, `ShipmentLoadPlannedContractsEdmModelTests.cs`.

**Interfaces — Produces:**
- `ShipmentLoadPlannedPurchaseContract.ShipmentReleaseKey : Guid?` + `ShipmentRelease : ShipmentRelease?`
- `ShipmentLoadPlannedSalesContract.SalesShipmentReleaseKey : Guid?` + `SalesShipmentRelease : SalesShipmentRelease?`
- `[Index(ShipmentLoadKey, ContractKey, <ReleaseKey>, IsUnique = true)]` no lugar do índice `(ShipmentLoadKey, ContractKey)`.
- `ShipmentLoadChangeLogFields.DescribePlannedContract(string? contractCode, int? releaseRowId, decimal? plannedQuantity)` → `"CC000001"`, `"CC000001 (item 2)"`, `"CC000001 (item 2) — 500,000"`.

- [ ] Teste EDM (falha): `Planned_rows_expose_release_navigations` — `FindProperty("ShipmentRelease")` na de compra e `FindProperty("SalesShipmentRelease")` na de venda não nulos. Teste unitário de `DescribePlannedContract` com os três formatos acima (novo arquivo `ShipmentLoadChangeLogFieldsPlannedContractTests.cs` se não houver um de `ChangeLogFields`).
- [ ] Rodar: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~ShipmentLoadPlannedContracts|FullyQualifiedName~ShipmentLoadChangeLogFields"` → falha.
- [ ] Implementar entidades, `HasOne(x => x.ShipmentRelease).WithMany().HasForeignKey(...).OnDelete(DeleteBehavior.NoAction)` (idem venda) e a nova assinatura de `DescribePlannedContract` (ajustar os dois chamadores para compilar, passando `null` por ora).
- [ ] Rodar de novo → passa. Commit `feat(shipment): liga o contrato planejado da carga à liberação`.

### Task 2: Inclusão e remoção por liberação (backend)

**Files:** `ShipmentLoadsPlannedContractAddService.cs`, `ShipmentLoadsPlannedContractRemoveService.cs`, `ShipmentLoadPlannedContractsServiceTests.cs`.

**Interfaces — Produces:** `ShipmentLoadsPlannedContractAddService.ExecuteAsync(Guid loadKey, PlannedContractType type, Guid releaseKey, decimal? quantity, string? notes, string user)`.

Mensagens (exatas):
- `"Liberação de embarque não encontrada."` / `"Liberação de entrega não encontrada."` (`NotFoundException`)
- `"A liberação {code} (item {rowId}) não está ativa."`
- `"A liberação {code} (item {rowId}) já está planejada nesta carga."`
- mantidas: `"O contrato {code} não está aprovado."`, `"O contrato {code} é de outro produto."`, `"A carga {code} está cancelada."`, `"A quantidade planejada deve ser maior que zero."`

- [ ] Reescrever os testes de inclusão para semear liberação (`SeedPurchaseReleaseAsync(contract, status = Actived, rowId)` / `SeedSalesReleaseAsync`) e chamar com a chave dela. Casos: grava `ContractKey` **e** `ReleaseKey`; log contém `"CC000001 (item 1)"`; liberação `Pending` recusada; contrato não aprovado; outro produto; mesma liberação duas vezes recusada; **duas liberações do mesmo contrato aceitas**; liberação inexistente → `NotFoundException`; carga cancelada; quantidade ≤ 0.
- [ ] Remoção: log da remoção cita `"(item N)"`; **linha legada** (inserida direto no contexto com `ReleaseKey = null`) é removida e o log sai só com o código.
- [ ] Rodar → falha; implementar; rodar → passa. Os testes de delete de carga/contrato do arquivo continuam verdes.
- [ ] Commit `feat(shipment): planeja a carga pela liberação em vez do contrato`.

### Task 3: Excluir liberação remove o planejado (backend)

**Files:** `ShipmentReleasesDeleteService.cs`, `SalesShipmentReleasesDeleteService.cs`, testes no mesmo `ShipmentLoadPlannedContractsServiceTests.cs`.

- [ ] Testes (falham em banco real por FK; no InMemory, afirmam que a linha planejada SOME): `Deleting_a_purchase_release_removes_its_planned_rows`, `Deleting_a_sales_release_removes_its_planned_rows` — planeja com a liberação ativa, volta o status para `Pending`, exclui pela service, `Assert.Empty` na tabela planejada.
- [ ] Implementar `RemoveRange` das linhas planejadas antes do `Remove` da liberação, com comentário pt-BR citando GAC-1185.
- [ ] Rodar → passa. Commit `fix(shipment): exclui a liberação junto com o planejamento de carga dela`.

### Task 4: Action, EDM e migration (backend)

**Files:** `ShipmentLoadsPlannedContractAddController.cs`, `ODataConfigurations.cs`, `ShipmentLoadPlannedContractsEdmModelTests.cs`, migration nova.

- [ ] Teste EDM (falha): a action `ShipmentLoadsPlannedContractAdd` tem parâmetro `ReleaseKey` e **não** tem `ContractKey`.
- [ ] EDM: `Parameter<Guid>("ReleaseKey")` no lugar de `ContractKey`. Controller: lê `ReleaseKey`; `BadRequest("Informe a carga e a liberação.")`.
- [ ] `dotnet build SiagroB1.sln` → 0 erros. `dotnet test SiagroB1.Application.Tests` (suíte inteira) → verde.
- [ ] Gerar: `ASPNETCORE_ENVIRONMENT=Yokotobi-Development dotnet ef migrations add AddShipmentLoadPlannedContractRelease --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web`. **Ler o arquivo**: 2 `AddColumn` anuláveis, 2 `AddForeignKey` (sem cascade), `DropIndex`+`CreateIndex` dos dois índices únicos, e os índices das FKs novas. Qualquer outra operação = drift do snapshot: remover do `Up/Down` e reportar.
- [ ] `git add` da migration, do `.Designer.cs` e do snapshot. Commit `feat(shipment): expõe a liberação na action de contrato planejado` com `DB: AddShipmentLoadPlannedContractRelease`.

### Task 5: Diálogo de seleção de liberação e grids (frontend)

**Files:** helpers, dois fragments novos, fragment da aba, `Detail.controller.ts`, testes QUnit.

**Interfaces — Produces:**
- `BillingReleaseSearch.ts`: `filterRowsByWords<T>(rows: T[], query: string, fields: (keyof T)[]): T[]` (mesma semântica de hoje: sem acento, minúsculas, todas as palavras; busca vazia devolve a própria lista; não-array devolve `[]`). `filterBillingReleases` passa a delegar.
- `PlannedReleaseHelpers.ts`:
  - `type PlannedPurchaseRelease` (campos do DTO `ShipmentRelesesPurchaseContractsResponseDto`) e `type PlannedSalesRelease` (DTO `SalesShipmentReleaseAvailableDto`);
  - `filterPlannedPurchaseReleases(rows, query)` — procura em `BranchShortName, PurchaseContractCode, FName, TaxId, City, State, DeliveryLocationCode, DeliveryLocationName, ItemName`;
  - `plannedReleaseKey(type: "Purchase" | "Sales", row): string` — `ShipmentReleaseKey` ou `SalesShipmentReleaseKey`;
  - `parsePlannedQuantity(text: string): { valid: boolean; value: number | null }` — vazio → `{true, null}`; `> 0` → `{true, n}`; resto → `{false, null}`.

- [ ] QUnit (falha): `PlannedReleaseHelpers.qunit.ts` cobrindo os três helpers (busca por "cargill 763" em campos diferentes, acento, vazio, `null`; chave por tipo; quantidade `""`, `"  "`, `"10"`, `"0"`, `"-1"`, `"abc"`). Registrar em `unitTests.qunit.ts`. Os testes de `BillingReleaseSearch.qunit.ts` continuam passando sem alteração.
- [ ] Implementar helpers.
- [ ] Fragments. Ambos: `Dialog` `contentWidth="90%"` `resizable` `draggable`, título de `viewModel>/plannedContractDialog/title`; `t:Table` (`selectionMode="Single"`, `selectionBehavior="Row"`, `enableSelectAll="false"`, `visibleRowCount="10"`, `class="sapUiSizeCondensed"`, `alternateRowColors`) com `rows="{plannedReleases>/}"`, toolbar com título + `SearchField` (`liveChange`/`search` = `.onPlannedReleaseSearch`); abaixo, "Quantidade prevista" (`Input type="Number"`, opcional) e "Observação" (`TextArea maxLength="200"`) ligados ao mesmo buffer JSON; botões "Incluir" (`.onConfirmPlannedContract`) e "Fechar".
  - Compra — id `plannedPurchaseReleasesTable`, colunas **idênticas** às de `SelectShipmentRelease.view.xml` (mesmos rótulos, larguras, `sortProperty`/`filterProperty`, templates e formatters `releaseOriginText/State`), trocando o modelo `contracts>` por `plannedReleases>`.
  - Venda — id `plannedSalesReleasesTable`, colunas **idênticas** às de `Billing.fragment.xml` (`shipmentBillingSalesContractsTable`), trocando `billingReleases>` por `plannedReleases>`.
  - Sem `--` dentro de comentário XML.
- [ ] Controller: `openPlannedContractDialog(type)` zera o buffer, carrega as liberações pela function do tipo com o `ItemCode` da carga (compra sem `WarehouseCode`), guarda a lista completa, limpa busca/seleção/primeira linha visível e abre o diálogo do tipo (um `Dialog` por tipo, criados por `DialogHelper.createDialog`). `onPlannedReleaseSearch` filtra preservando a seleção pelo objeto, como `ShipmentBillingDialog.onReleaseSearch`. `onConfirmPlannedContract` exige linha selecionada (`"Selecione a liberação."`), valida a quantidade e envia `LoadKey, ContractType, ReleaseKey, Quantity, Notes`. Remover `openPlannedContractValueHelp`.
- [ ] Aba: `$expand` das duas grids ganha a liberação (`ShipmentRelease` / `SalesShipmentRelease`, `$select=Key,RowId,DeliveryLocationCode,DeliveryLocationName,ReleasedQuantity,ShippedQuantity,Status,AvailableQuantity`); colunas novas "Item" (`RowId`), "Armazém" (`(código) nome`) e "Saldo da liberação" (`AvailableQuantity`), entre Parceiro e Quantidade prevista.
- [ ] `git rm` do fragment antigo; `git add` dos novos. `yarn ts-typecheck` e `yarn lint` limpos; `yarn test` verde.
- [ ] Commit `feat(shipment): planeja a carga pela liberação em vez do contrato` (`Refs: GAC-1185`).

### Task 6: Verificação ponta a ponta

- [ ] Conferir `__EFMigrationsHistory` do `IDX_SIAGRO_DEV` local contra o branch; aplicar a migration no banco local (`Yokotobi-Development`, alvo confirmado por `dbcontext info`) ou numa cópia se houver migration de outro branch.
- [ ] Subir Web + Gateway (profile `yktb`) e `yarn start:dev`; login `admin`/`1234`.
- [ ] Numa carga não cancelada: incluir liberação de compra e de venda; conferir colunas contra a Expedição e o Faturamento; buscar com linha selecionada; incluir a mesma liberação de novo (recusa); incluir outra liberação do mesmo contrato (aceita); excluir; conferir o log de alterações.
- [ ] Redimensionar e reordenar colunas do diálogo, recarregar a página (F5) e reabrir: o layout volta.
- [ ] Linha legada (se houver no banco) aparece com colunas da liberação vazias.
- [ ] Console e rede sem erro.
