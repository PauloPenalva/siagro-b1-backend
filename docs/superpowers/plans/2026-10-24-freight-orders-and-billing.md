# Ordem de Frete + Fatura de Frete — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ordem de Frete com saldo consumido por CT-e, e Fatura de Frete que aglutina títulos de CT-e num título único.

**Architecture:** Entidades novas no `AppDbContext` (inline em `OnModelCreating`), serviços um-por-operação em `SiagroB1.Application/Services/FreightOrders|FreightInvoices/`, ações OData não vinculadas, ganchos no `CteLifecycleHooks`. A aglutinação é uma `FinancialSettlement` sem conta, origem nova `Aggregation`, modelada em `FinancialDocumentsDationService`. Frontend copia o padrão `transportManifests` (Main/Form/Detail).

**Tech Stack:** .NET 10, EF Core SQL Server, OData v4, xUnit + EF InMemory, FastReport.OpenSource, OpenUI5 TS.

**Spec:** `docs/superpowers/specs/2026-10-24-freight-orders-and-billing-design.md`

Paths: backend relativo a `siagro-b1-backend/`; frontend relativo a `siagro-b1-frontend/webapp/`.

## Global Constraints
- Identificadores/tabelas/colunas em inglês; tudo que o usuário lê em pt-BR (labels inline nas views, mensagens de `DefaultException`).
- Só STANDALONE: controllers começam com `if (!ErpMode.IsStandalone(configuration)) return BadRequest(...)`; serviços lançam `DefaultException(BranchCteSettingsService.StandaloneOnly)`; menus com `StandaloneOnly = true`.
- Todo arquivo novo: `git add` logo após criar. Commits no branch `feature/freight-orders-billing`, mensagem `tipo(escopo): ...` pt-BR, escopo `shipment` (TMS) ou `financial` (aglutinação); trailer `DB: <Migration>` quando houver migration. Nunca push.
- Serviços novos registrados à mão em `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs` `AddApplicationServices` (bloco CT-e ~426).
- Migrations: App em `SiagroB1.Migrations/AppContext`, menus em `SiagroB1.Migrations/CommonContext`; gerar com `dotnet ef migrations add <Nome> --context <Ctx> --project SiagroB1.Migrations --startup-project SiagroB1.Web` com `ASPNETCORE_ENVIRONMENT=Development` (nunca o profile db-migration). Índice filtrado → conferir `QUOTED_IDENTIFIER`.
- Peso = linha `TransportDocumentQuantity` de peso bruto; `UnitCode "01"` kg, `"02"` t ×1000. Reusar `TransportManifestsCreateFromCtesService.Weight` (tornar `internal static` acessível/extrair para `TransportDocumentWeight.GrossKg(TransportDocument)` se preciso).
- Tomador do CT-e = parte com `(int)Role == TakerRole` (como `TransportManifestsCreateFromCtesService.Taker`).

## Review Focus
1. CT-e cancelado devolve saldo e reabre ordem Concluída-automática — teste em Task A4.
2. Mesmo CT-e em duas faturas por corrida/duplo clique — índice único filtrado `IsActive` + teste em Task B3.
3. Título de CT-e parcialmente baixado ou com parcelas e uma baixada — inelegível; teste em Task B2.
4. Estorno isolado de baixa por aglutinação pelo Financeiro — recusado; teste em Task B4.
5. Aglutinação não pode gerar lançamento contábil nem movimento de conta — teste em Task B4.

---

## Entrega A — Ordem de Frete

### Task A1: Modelo da ordem + vínculo no CT-e + migration

**Files:**
- Create: `SiagroB1.Domain/Entities/FreightOrder.cs`, `SiagroB1.Domain/Enums/FreightOrderStatus.cs`
- Modify: `SiagroB1.Domain/Enums/TransactionCode.cs` (`FreightOrder = 19`, `FreightInvoice = 20`), `SiagroB1.Domain/Entities/TransportDocument.cs` (`Guid? FreightOrderKey`), `SiagroB1.Infra/Context/AppDbContext.cs`
- Create: migration `AddFreightOrders` (AppContext)
- Test: `SiagroB1.Application.Tests/FreightOrders/FreightOrderModelTests.cs`

**Interfaces — Produces:**
- `FreightOrderStatus { Open = 0, InExecution = 1, Completed = 2, Cancelled = 3 }`
- `[Table("FREIGHT_ORDERS")] FreightOrder`: `Guid Key`, `string BranchCode` (VARCHAR 14), `Guid DocNumberKey`, `string Code` (VARCHAR 20), `DateTime OrderDate`, `string TakerCardCode`, `string? SenderCardCode`, `string? RecipientCardCode`, `string StartMunicipalityCode`, `string StartState`, `string EndMunicipalityCode`, `string EndState`, `string ItemCode`, `string ItemName` (VARCHAR 60, vira `PredominantProduct`), `decimal ContractedWeightKg` (18,3), `FreightCalculationMethod PriceMethod`, `decimal UnitPrice` (18,6), `DateTime ValidFrom`, `DateTime? ValidTo`, `int? PaymentConditionCode`, `int? UsageCode`, `string? Observation` (2000), `FreightOrderStatus Status`, `bool ClosedManually`, `decimal TransportedWeightKg` (18,3, persistido), `string CreatedBy`, `DateTime CreatedAt`, `byte[] RowVersion`.
- Índice único `{BranchCode, Code}`; índice em `TransportDocument.FreightOrderKey`; FK CT-e→ordem `Restrict`.
- Se `FreightCalculationMethod` não tiver "por tonelada": acrescentar `PerTon` e caso em `FreightPriceCalculator.Calculate` (valor = preço × kg/1000).

- [ ] Step 1: Teste `FreightPriceCalculator_PerTon_UsesTonnes`: `Calculate(PerTon, 50m, 32_000m) == 1600m`; teste de modelo `FreightOrder_RoundTrips_WithCteLink` (salva ordem + CT-e com `FreightOrderKey`, relê via `TestDb.CreateUnitOfWork()`).
- [ ] Step 2: `dotnet test SiagroB1.Application.Tests --filter FullyQualifiedName~FreightOrderModelTests` → FAIL (tipo inexistente).
- [ ] Step 3: Criar enum/entidade/propriedade, DbSet e config inline ao lado de `TransportManifest` (~555).
- [ ] Step 4: Testes → PASS; `dotnet build SiagroB1.sln` limpo.
- [ ] Step 5: Gerar migration `AddFreightOrders`; conferir que só cria `FREIGHT_ORDERS` + coluna `FreightOrderKey`.
- [ ] Step 6: Commit `feat(shipment): adiciona modelo da ordem de frete` + `DB: AddFreightOrders`.

### Task A2: Saldo e situação da ordem

**Files:**
- Create: `SiagroB1.Application/Services/FreightOrders/FreightOrdersRecalculateService.cs`
- Test: `SiagroB1.Application.Tests/FreightOrders/FreightOrdersRecalculateServiceTests.cs`

**Interfaces — Produces:** `Task RecalculateAsync(Guid orderKey)` — soma peso bruto (kg) dos CT-e com `FreightOrderKey == orderKey` e `Status == CteStatus.Authorized`, grava `TransportedWeightKg`, e se `Status != Cancelled && !ClosedManually`: 0 CT-e autorizado → `Open`; saldo (`Contracted − Transported`) ≤ 0 → `Completed`; senão `InExecution`. Não faz SaveChanges fora do contexto do chamador? → faz `SaveChangesAsync` próprio (é chamado de hook, após o commit do CT-e).

- [ ] Step 1: Testes: `NoAuthorizedCte_IsOpen`; `OneCte_20t_of_50t_IsInExecution_Transported20000`; `CteInTonnes_Converts` (UnitCode "02", 20 → 20000); `BalanceZero_IsCompleted`; `CancelledCte_ReopensCompleted` (2 CT-e somam 50t → Completed; cancela um → InExecution); `ClosedManually_StaysCompleted`; `Cancelled_Order_IsUntouched`.
- [ ] Step 2: Rodar → FAIL.
- [ ] Step 3: Implementar.
- [ ] Step 4: Rodar → PASS.
- [ ] Step 5: Commit `feat(shipment): calcula saldo e situação da ordem de frete`.

### Task A3: Salvar, encerrar e cancelar ordem

**Files:**
- Create: `Services/FreightOrders/FreightOrderSaveModel.cs`, `FreightOrdersSaveService.cs`, `FreightOrdersCloseService.cs`, `FreightOrdersCancelService.cs`
- Test: `Tests/FreightOrders/FreightOrdersSaveServiceTests.cs`, `FreightOrdersCloseCancelServiceTests.cs`

**Interfaces:**
- `FreightOrderSaveModel` = campos editáveis de A1 + `Guid? Key`, `Guid? DocNumberKey`, `string? TypedCode`.
- `Task<FreightOrder> SaveAsync(FreightOrderSaveModel m, string user)` — novo: `DocNumberSequenceService.AssignAsync(new DocNumberRequest(TransactionCode.FreightOrder, m.DocNumberKey, m.TypedCode, 20, code => exists))`.
- `Task CloseAsync(Guid key, string user)` → `Completed`, `ClosedManually = true`; recusa se `Cancelled`.
- `Task CancelAsync(Guid key, string user)` → `Cancelled`; recusa se existe CT-e autorizado vinculado: `"A ordem de frete possui CT-e autorizado e não pode ser cancelada."`

- [ ] Step 1: Testes de Save: `Create_AssignsCode_StatusOpen` (usa `FakeDocNumberSequenceService`); `ContractedWeight_MustBePositive` (`"Informe o volume contratado."`); `UnitPrice_MustBePositive`; `ValidTo_BeforeValidFrom_Rejected`; `Update_WithAuthorizedCte_CannotChangeTakerOrItem` (`"Ordem com CT-e autorizado: tomador e produto não podem ser alterados."`); `Update_WithAuthorizedCte_CanChangePriceAndObservation`; `Update_CompletedOrCancelled_Rejected`; `Sapb1Mode_Rejected`. Close/Cancel: os três comportamentos acima.
- [ ] Step 2: Rodar → FAIL.
- [ ] Step 3: Implementar; validar tomador/item existentes via `IBusinessPartnerService`/`IItemService` (STANDALONE).
- [ ] Step 4: Rodar → PASS.
- [ ] Step 5: Commit `feat(shipment): inclui, encerra e cancela ordem de frete`.

### Task A4: Ganchos do CT-e

**Files:**
- Modify: `Services/Cte/CteLifecycleHooks.cs`
- Test: `Tests/Cte/CteLifecycleHooksTests.cs` (estender)

**Interfaces — Consumes:** `FreightOrdersRecalculateService.RecalculateAsync`.

- [ ] Step 1: Testes: `OnAuthorized_WithOrder_RecalculatesOrder` (ordem vira InExecution); `OnCancelled_WithOrder_ReturnsBalance`; `OnAuthorized_WithoutOrder_DoesNothing`; `RecalcFailure_DoesNotThrow` (hooks nunca lançam — logar).
- [ ] Step 2: FAIL → Step 3: chamar recálculo em `OnAuthorizedAsync`/`OnCancelledAsync` quando `doc.FreightOrderKey != null`, em try/catch com log → Step 4: PASS.
- [ ] Step 5: Commit `feat(shipment): atualiza saldo da ordem ao autorizar ou cancelar CT-e`.

### Task A5: CT-e a partir da ordem + vínculo no rascunho

**Files:**
- Create: `Services/FreightOrders/FreightOrdersCreateCteService.cs`
- Modify: serviço de salvar rascunho do CT-e (`Services/Cte/*Save*` — o que hoje grava `TransportDocument` Draft) e seu save model (`Guid? FreightOrderKey`)
- Test: `Tests/FreightOrders/FreightOrdersCreateCteServiceTests.cs`, teste no save do CT-e

**Interfaces:**
- `Task<Guid> CreateDraftAsync(Guid orderKey, string user)` → rascunho com BranchCode, partes (tomador/remetente/destinatário como `TransportDocumentParty`, `TakerRole` = papel do tomador), municípios/UFs, `PredominantProduct = ItemName`, `UsageCode`, `PaymentConditionCode`, `FreightOrderKey`; sem quantidades/valor (operador informa peso; front calcula com `FreightHelpers` pelo `PriceMethod`/`UnitPrice`, valor editável).
- Expor no CT-e (GET) `FreightOrderKey` para o front buscar `PriceMethod`/`UnitPrice`.

- [ ] Step 1: Testes: `CreateDraft_CopiesOrderData`; `CreateDraft_OrderNotOpenOrInExecution_Rejected` (`"Só é possível emitir CT-e para ordem Aberta ou Em execução."`); `CreateDraft_OutsideValidity_Rejected` (`"Ordem de frete fora da vigência."`); `CteSave_LinkOrderOfOtherTaker_Rejected` (`"A ordem de frete é de outro tomador."`); `CteSave_LinkClosedOrder_Rejected`.
- [ ] Step 2: FAIL → Step 3: implementar → Step 4: PASS.
- [ ] Step 5: Commit `feat(shipment): emite CT-e a partir da ordem de frete`.

### Task A6: OData + menu

**Files:**
- Create: `SiagroB1.Web/Controllers/FreightOrdersController.cs`; migration Common `AddFreightOrderMenus`
- Modify: `SiagroB1.Web/ODataConfig/ODataConfigurations.cs`, `ServiceCollectionExtensions.cs`
- Test: `Tests/FreightOrders/FreightOrdersEdmModelTests.cs` (modelo `Mdfe/MdfeEdmModelTests.cs`), `Tests/FreightOrders/FreightMenuMigrationTests.cs` (modelo `Mdfe/TmsMenuMigrationTests.cs`)

**Interfaces — Produces (OData):** EntitySet `FreightOrders` (GET com `[EnableQuery]`); ações `FreightOrdersSave(Model)` → `FreightOrder`, `FreightOrdersClose(Key)`, `FreightOrdersCancel(Key)`, `FreightOrdersCreateCte(Key)` → `Guid`. Menus sob `ParentKey "tms"`: `freightOrders` "Ordens de Frete" Order 6 e `freightInvoices` "Faturas de Frete" Order 7, ambos `StandaloneOnly = true`, `ROLE_MENUS` para ADMIN + roles que já veem `transportDocuments`.

- [ ] Step 1: Testes EDM (entity set e 4 ações existem com parâmetros) e de migration (2 itens, parent `tms`, StandaloneOnly, Down remove ROLE_MENUS antes).
- [ ] Step 2: FAIL → Step 3: implementar, registrar os 5 serviços → Step 4: PASS.
- [ ] Step 5: Commit `feat(shipment): expõe ordem de frete na API e no menu do TMS` + `DB: AddFreightOrderMenus`.

### Task A7: Frontend da ordem

**Files (frontend):**
- Create: `controller/freightOrders/{Main,Form,Detail}.controller.ts`, `view/freightOrders/{Main,Form,Detail}.view.xml`
- Modify: `manifest.json` (rotas `freight-orders`, `/new`, `/{id}/edit`, `/{id}` → `freightOrders`, `freightOrdersNew`, `freightOrdersEdit`, `freightOrdersDetail`; targets como `transportManifests`), `model/ServerRoutes.ts`, `view/docNumbers/fragments/Form.fragment.xml` + `controller/docNumbers/BaseController.ts` `STANDALONE_ONLY_CODES` (FreightOrder, FreightInvoice), `controller/transportDocuments/Form.controller.ts` (value help de ordem filtrado por tomador e Status in Open/InExecution; ao escolher e informar peso, sugere valor)

- Main: tabela com Número, Data, Tomador, Origem→Destino, Produto, Contratado (t), Transportado (t), Saldo (t), Situação; FilterBar tomador, situação (multi), período, produto.
- Detail: ObjectPage com seções Dados e "CT-e" (lista dos CT-e vinculados: número, emissão, peso, valor, situação); botões Editar, Emitir CT-e (navega ao form do CT-e criado), Encerrar, Cancelar.
- Form: numeração (Automático/Manual pelo `getDocNumberInfoByTransaction("FreightOrder")`), tomador/remetente/destinatário (`openTableSelectDialog(..."BusinessPartnersSelectDialog"...)`), municípios, produto, volume (t na tela, gravar kg), método de preço, preço, vigência, condição de pagamento, natureza, observação.

- [ ] Step 1: `npm run ts-typecheck` e `npm run lint` limpos.
- [ ] Step 2: Verificar pelo caminho do usuário (stack local, login STANDALONE): home → TMS → Ordens de Frete → incluir → Emitir CT-e → autorizar em homologação ou simular → saldo cai; cancelar CT-e → saldo volta. Playwright com screenshots.
- [ ] Step 3: Commit (repo frontend) `feat(shipment): telas de ordem de frete e vínculo no CT-e`.

---

## Entrega B — Fatura de Frete

### Task B1: Modelo da fatura + origens novas + migration

**Files:**
- Create: `Domain/Entities/FreightInvoice.cs`, `FreightInvoiceItem.cs`, `Domain/Enums/FreightInvoiceStatus.cs`
- Modify: `Domain/Enums/FinancialDocumentOrigin.cs` (`FreightInvoice = 12`; conferir índice FirmOrigin — origem com parcelas fica fora), `Domain/Enums/FinancialSettlementOrigin.cs` (`Aggregation = 8`), `AppDbContext.cs`
- Create: migration `AddFreightInvoices`
- Test: `Tests/FreightInvoices/FreightInvoiceModelTests.cs`

**Interfaces — Produces:**
- `FreightInvoiceStatus { Draft = 0, Issued = 1, Settled = 2, Cancelled = 3 }`
- `[Table("FREIGHT_INVOICES")] FreightInvoice`: `Guid Key`, `BranchCode`, `Guid DocNumberKey`, `string Code`, `string TakerCardCode`, `DateTime IssueDate`, `DateTime? DueDate`, `int? PaymentConditionCode`, `decimal TotalAmount` (18,2), `string? Observation`, `FreightInvoiceStatus Status`, `DateTime? IssuedAt`, `string CreatedBy`, `RowVersion`, `ICollection<FreightInvoiceItem> Items` (Cascade).
- `[Table("FREIGHT_INVOICE_ITEMS")] FreightInvoiceItem`: `Guid Key`, `Guid InvoiceKey`, `Guid TransportDocumentKey` (FK Restrict), `int LineNum`, `decimal Amount`, `bool IsActive`; índice único `TransportDocumentKey` com `HasFilter("[IsActive] = 1")` (como `TransportManifestCte`).
- Unique `{BranchCode, Code}`.

- [ ] Step 1: Teste round-trip com 2 itens. Step 2: FAIL. Step 3: implementar. Step 4: PASS + build. Step 5: migration (conferir filtro do índice). Step 6: Commit `feat(financial): adiciona modelo da fatura de frete` + `DB: AddFreightInvoices`.

### Task B2: CT-e elegíveis

**Files:** Create `Services/FreightInvoices/FreightInvoiceEligibleCtesQuery.cs`; Test `Tests/FreightInvoices/FreightInvoiceEligibleCtesQueryTests.cs`

**Interfaces — Produces:** `Task<IReadOnlyList<FreightInvoiceEligibleCte>> ListAsync(string branchCode, string takerCardCode, DateTime? from, DateTime? to)`; record `FreightInvoiceEligibleCte(Guid TransportDocumentKey, int Number, int Series, string AccessKey, DateTime IssuedAt, string Route, decimal WeightKg, decimal Amount, Guid? FreightOrderKey)`. `Amount` = Σ `OpenAmount` dos títulos ativos de origem `TransportDocument` com `OriginKey` = CT-e.

Elegível se: CT-e `Authorized`, tomador = taker, filial, emissão no período, sem `FreightInvoiceItem` ativo, possui título ativo e **nenhuma** parcela com `SettledAmount > 0` ou `ComposedAmount > 0`.

- [ ] Step 1: Testes: `Authorized_WithOpenTitle_IsEligible`; `OtherTaker_Excluded`; `Cancelled_Excluded`; `AlreadyInActiveInvoice_Excluded`; `InCancelledInvoice_IsEligibleAgain`; `UsageWithoutFinancial_Excluded` (sem título); `OneInstallmentSettled_Excluded` (2 parcelas, 1 baixada); `Amount_SumsAllInstallments`.
- [ ] Step 2–4: FAIL → implementar → PASS. Step 5: Commit `feat(financial): lista CT-e pendentes de fatura`.

### Task B3: Salvar rascunho da fatura

**Files:** Create `Services/FreightInvoices/FreightInvoiceSaveModel.cs`, `FreightInvoicesSaveService.cs`; Test `Tests/FreightInvoices/FreightInvoicesSaveServiceTests.cs`

**Interfaces:** `FreightInvoiceSaveModel(Guid? Key, string BranchCode, string TakerCardCode, DateTime IssueDate, DateTime? DueDate, int? PaymentConditionCode, string? Observation, IReadOnlyList<Guid> TransportDocumentKeys, Guid? DocNumberKey, string? TypedCode)`; `Task<FreightInvoice> SaveAsync(FreightInvoiceSaveModel, string user)` — só `Draft`; revalida cada CT-e por `FreightInvoiceEligibleCtesQuery` (ignorando a própria fatura); `TotalAmount` = Σ Amount; código via `TransactionCode.FreightInvoice`. Condição padrão: a informada, senão a do primeiro CT-e.

- [ ] Step 1: Testes: `Create_Draft_TotalIsSum`; `NoCte_Rejected` (`"Selecione ao menos um CT-e."`); `IneligibleCte_Rejected` (`"CT-e {número} não pode ser faturado: {motivo}."`); `CteInOtherActiveInvoice_Rejected` — inclusive simulando duas faturas gravadas em sequência com o mesmo CT-e (a 2ª falha); `Update_Issued_Rejected`; `RemoveCte_FromDraft_Frees` (item vira IsActive=false ou removido).
- [ ] Step 2–4. Step 5: Commit `feat(financial): monta rascunho da fatura de frete`.

### Task B4: Emitir fatura (aglutinação)

**Files:**
- Create: `Services/FreightInvoices/FreightInvoicesIssueService.cs`
- Modify: `Services/Accounting/Postings/SettlementPosting.cs` (`IsPostableOrigin` exclui `Aggregation`), `Services/Financials/FinancialDocumentsReverseSettlementService.cs` (recusa `Aggregation`: `"Baixa por fatura de frete: cancele a fatura para desfazer."`), `Services/Financials/FinancialDocumentsCancelService.cs` (título origem `FreightInvoice` só cancela pela fatura), `FinancialDocumentsManualUpdateService` se edita título de origem não-manual
- Test: `Tests/FreightInvoices/FreightInvoicesIssueServiceTests.cs`

**Interfaces:** `Task IssueAsync(Guid invoiceKey, string user)`, em uma transação (`BeginTransaction`/`Commit` no UnitOfWork — ver memória: Rollback sem Begin é no-op):
1. Revalida elegibilidade de todos os itens.
2. Para cada parcela aberta dos títulos dos CT-e: `FinancialSettlement { Origin = Aggregation, FinancialAccountCode = null, Amount = OpenAmount, SettlementDate = IssueDate, RelatedDocumentKey = invoiceKey, DocumentReference = invoice.Code }` (modelo `FinancialDocumentsDationService`), depois `FinancialDocumentsRecalculateBalanceService.RecalculateAsync`.
3. Gera título(s) a receber `OriginType = FreightInvoice`, `OriginKey = invoiceKey`, `OriginDocNumber = Code`, `CardCode = Taker`, parcelado pela `PaymentConditionCode` (mesma rotina de parcelamento usada por `FinancialDocumentsTransportService`; extrair helper se for privada), ou vencimento único `DueDate`. Natureza/dimensões contábeis: as do primeiro CT-e.
4. `Status = Issued`, `IssuedAt`.

- [ ] Step 1: Testes: `Issue_SettlesCteTitles_ByAggregation_NoAccount` (títulos dos CT-e `OpenAmount == 0`, settlement `Origin == Aggregation`, `FinancialAccountCode == null`); `Issue_CreatesSingleReceivable_WithTotal`; `Issue_WithPaymentCondition_SplitsInstallments` (2 parcelas somam o total); `Issue_DoesNotEnqueueAccountingPosting` (nenhum registro pendente de posting para as baixas Aggregation); `Issue_NoAccountMovement` (`FinancialAccountMovementsQuery` vazio); `Issue_CteSettledMeanwhile_RejectedAndRollsBack`; `ReverseSettlement_Aggregation_Rejected`; `Issue_NotDraft_Rejected`.
- [ ] Step 2–4. Step 5: Commit `feat(financial): emite fatura de frete aglutinando os títulos dos CT-e`.

### Task B5: Cancelar fatura e situação Liquidada

**Files:** Create `Services/FreightInvoices/FreightInvoicesCancelService.cs`, `FreightInvoicesRefreshStatusService.cs`; Modify o ponto onde baixa/estorno de título termina (`FinancialDocumentsRecalculateBalanceService` ou chamador) para chamar refresh quando `OriginType == FreightInvoice`; Test `Tests/FreightInvoices/FreightInvoicesCancelServiceTests.cs`

**Interfaces:** `Task CancelAsync(Guid key, string user)` — Draft: itens `IsActive=false`, `Cancelled`. Issued: recusa se título da fatura tem baixa (`"A fatura possui baixa; estorne a baixa antes de cancelar."`); senão cancela o(s) título(s) da fatura, grava estorno (`Origin = Reversal`, `ReversedSettlementKey`) de cada baixa Aggregation, recalcula títulos dos CT-e, itens `IsActive=false`, `Cancelled`. `Task RefreshAsync(Guid invoiceKey)` → `Settled` quando todos os títulos da fatura `OpenAmount == 0`, volta a `Issued` se estornarem.

- [ ] Step 1: Testes: `CancelDraft_FreesCtes`; `CancelIssued_ReopensCteTitles` (OpenAmount original de volta, CT-e elegível de novo); `CancelIssued_WithSettlement_Rejected`; `FullSettlement_MarksSettled`; `ReverseSettlement_BackToIssued`.
- [ ] Step 2–4. Step 5: Commit `feat(financial): cancela fatura de frete e acompanha a liquidação`.

### Task B6: Travas no CT-e

**Files:** Modify `Services/Cte/TransportDocumentsCancelService.cs` (antes de `financial.EnsureCanUndoAsync`, ~56); Test `Tests/Cte/TransportDocumentsCancelServiceTests.cs`

- [ ] Step 1: Teste `Cancel_CteInActiveInvoice_RejectedWithInvoiceCode` → mensagem `"CT-e está na fatura {Code}; retire-o ou cancele a fatura antes."`; `Cancel_CteInCancelledInvoice_Allowed`. (CC-e não muda valor — `CteCorrectableFields` — sem trava.)
- [ ] Step 2–4. Step 5: Commit `feat(shipment): impede cancelar CT-e que está em fatura`.

### Task B7: Geração em lote

**Files:** Create `Services/FreightInvoices/FreightInvoicesBatchCreateService.cs`; Test idem

**Interfaces:** `Task<IReadOnlyList<Guid>> CreateAsync(string branchCode, DateTime from, DateTime to, string? takerCardCode, bool issue, string user)` — agrupa elegíveis por tomador, cria um rascunho por tomador via `FreightInvoicesSaveService` (numeração Automática), e emite se `issue`. Falha de um tomador não aborta os demais; retorna as criadas (erros por tomador em lista no resultado — record `FreightInvoiceBatchResult(IReadOnlyList<Guid> Created, IReadOnlyList<string> Errors)` no lugar da lista).

- [ ] Step 1: Testes: `TwoTakers_TwoInvoices`; `NoEligible_ReturnsEmpty`; `OneTakerFails_OthersCreated`. Step 2–4. Step 5: Commit `feat(financial): gera faturas de frete em lote por tomador`.

### Task B8: OData da fatura

**Files:** Create `SiagroB1.Web/Controllers/FreightInvoicesController.cs`; Modify `ODataConfigurations.cs`, `ServiceCollectionExtensions.cs`; Test `Tests/FreightInvoices/FreightInvoicesEdmModelTests.cs`

**Produces:** EntitySet `FreightInvoices` (expand Items); função `FreightInvoiceEligibleCtes(BranchCode=,TakerCardCode=,From=,To=)`; ações `FreightInvoicesSave(Model)`, `FreightInvoicesIssue(Key)`, `FreightInvoicesCancel(Key)`, `FreightInvoicesBatchCreate(BranchCode,From,To,TakerCardCode,Issue)`. Parâmetros string OData chegam nulos — tratar (memória do projeto).

- [ ] Step 1: Teste EDM. Step 2–4. Step 5: Commit `feat(financial): expõe fatura de frete na API`.

### Task B9: Impresso

**Files (SiagroB1.Reports):** Create `Controllers/FreightInvoicesReportController.cs` (`[Route("reports/FreightInvoicesReport")]`, `[HttpPost("{key:guid}/print")]`), `Services/FreightInvoicePrintDataService.cs`, `Services/FreightInvoiceReportService.cs`, `Templates/FreightInvoice.frx`; Test em `SiagroB1.Application.Tests` no padrão dos testes de relatório existentes (ver memória: copiar `SiagroB1.Reports/wwwroot` em worktree novo)

- Fontes `Invoice` (emitente: razão, CNPJ, endereço, dados bancários da filial; tomador; código, emissão, vencimento(s), total, observação) e `InvoiceItems` (CT-e número/série, chave, emissão, origem→destino, peso kg, valor). Modelo: `StorageInvoiceReportService`/`StorageInvoice.frx`. Fontes Liberation (memória Linux).
- [ ] Step 1: Teste do print data (2 CT-e → 2 linhas, total confere) e teste que gera PDF não vazio. Step 2–4. Step 5: renderizar localmente e inspecionar visualmente. Step 6: Commit `feat(reports): impresso da fatura de frete`.

### Task B10: Frontend da fatura

**Files (frontend):** Create `controller/freightInvoices/{Main,Form,Detail}.controller.ts`, `view/freightInvoices/{Main,Form,Detail}.view.xml`, `view/freightInvoices/fragments/BatchDialog.fragment.xml`; Modify `manifest.json` (rotas `freight-invoices…` → `freightInvoices`, `freightInvoicesNew`, `freightInvoicesEdit`, `freightInvoicesDetail`), `model/ServerRoutes.ts`

- Main: Número, Tomador, Emissão, Vencimento, Total, Qtd CT-e, Situação; FilterBar tomador, situação, período; botão "Gerar em lote" (dialog filial/período/tomador opcional/"emitir já").
- Form ("Gerar fatura"): filial, tomador, período → tabela de CT-e elegíveis com seleção múltipla e total selecionado ao vivo; emissão, vencimento/condição, observação; Salvar (rascunho) e Salvar e emitir.
- Detail: dados, itens, título(s) gerados (link ao Financeiro), botões Emitir, Cancelar, Imprimir (`PdfReportHelpers`).
- CT-e Detail: mostrar "Fatura: {Code}" quando houver.

- [ ] Step 1: `npm run ts-typecheck` e `npm run lint` limpos.
- [ ] Step 2: Verificar pelo caminho do usuário: TMS → Faturas de Frete → Gerar → selecionar 2 CT-e → Salvar e emitir → no Financeiro, títulos dos CT-e baixados e título da fatura aberto → Imprimir PDF → Cancelar → títulos dos CT-e abertos de novo. Screenshots.
- [ ] Step 3: Commit (repo frontend) `feat(financial): telas da fatura de frete`.

### Task B11: Fechamento
- [ ] `dotnet test SiagroB1.Application.Tests` e `SiagroB1.Fiscal.Tests` verdes; `dotnet build SiagroB1.sln` sem avisos novos.
- [ ] Revisão do branch inteiro (`superpowers:requesting-code-review`).
- [ ] Atualizar memória do projeto com a feature. Merge em main é decisão do usuário.
