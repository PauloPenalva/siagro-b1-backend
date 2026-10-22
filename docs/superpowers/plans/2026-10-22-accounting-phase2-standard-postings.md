# Contabilidade — Fase 2 (lançamento padrão + contabilização on-line) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** documentos do ERP viram lançamentos contábeis automaticamente, conforme lançamentos padrão (CT5)
configurados pelo contador: entradas, saídas, títulos, baixas, movimentos de conta, transferências e o CMV no
fechamento do estoque fiscal.

**Architecture:** catálogo fixo de códigos LP (contexto, fontes permitidas, lote, código que estorna) +
cadastro das sequências (`STANDARD_POSTINGS`/`_LINES`) + um motor único `AccountingPostingService` que recebe
um `PostingContext` neutro montado por um "context builder" de cada módulo e devolve, via
`JournalEntryPostingService.AddAsync` (fase 1), um lançamento **enfileirado** na transação do evento.
Cada serviço de evento só monta o contexto e chama `PostAsync`.

**Tech Stack:** .NET 10, EF Core SQL Server, OData v4, xUnit + EF InMemory, OpenUI5 TS.

**Spec:** `docs/superpowers/specs/2026-10-22-accounting-core-protheus-design.md` — seção 5 (LP e
contabilização), seção 8 (CMV no fechamento do estoque fiscal, LP 670/675), seção 9 (fatos do código),
Decisões 3, 4, 5, 10–15.

**Base:** branch `feature/accounting-postings` (criado de `feature/accounting-core`, fase 1 não mergeada) nos
worktrees `siagro-b1-backend-accounting` e `siagro-b1-frontend-accounting`. Copiar `SiagroB1.Reports/wwwroot`
se o worktree for recriado.

## Global Constraints

- Só STANDALONE: em SAPB1 `PostAsync` é no-op; controllers recusam com `"Disponível somente no modo STANDALONE."`.
- Sem data de corte (`AccountingSettings.CutoverDate`) = módulo desligado; evento com data < corte = no-op.
- LP ausente ou inativo = no-op (o documento confirma normalmente). LP ativo com erro = exceção que derruba o
  evento, mensagem `"LP {code} seq. {seq}: ..."` (fase 1 já prefixa).
- `PostAsync` é **enqueue-only**: nunca chama `SaveChanges`/`Commit`.
- Rastro de origem: `SourceDocumentType` / `SourceDocumentKey` / `SourceEvent`; um lançamento vivo por
  (código, documento, evento) — índice filtrado da fase 1.
- Estorno: código de estorno só contabiliza se o original vivo existir (Decisão 10); código de estorno inativo
  com original vivo = estorno automático (`JournalEntriesService.AddReversalAsync`).
- Data contábil = data do evento; período fechado recusa, exceto evento externo (cancelamento de NF-e
  homologado), que vai para `AccountingPeriodGuard.FirstOpenDateOnOrAfterAsync` com o histórico
  `"... (evento de dd/MM/yyyy)"` (Decisão 12).
- Valor zero não gera linha; nenhuma linha gerada = no-op.
- Lotes: entradas `008810`, saídas `008820`, financeiro `008850`, CMV `008870` (escolha deste plano).
- Identificadores em inglês, textos pt-BR; commits `feat(financial)`/`feat(invoice)` com `DB:` quando houver migration.

## Review Focus

1. Re-confirmar um documento depois de estornar a confirmação → o 650 contabiliza de novo (o original saiu do índice).
2. Estorno de baixa (linha negativa) cujo original foi feito antes da data de corte → 527/531 é no-op, não lançamento órfão.
3. Rateio de 3 centros com 33,33% sobre R$ 100,00 → linhas 33,33 + 33,33 + 33,34, débito = crédito.
4. Cancelamento de NF-e homologado num mês fechado → lança no 1º dia aberto, não derruba o handler.
5. Excluir lançamento na conta financeira já contabilizado → 564/565 (ou estorno automático) e o lançamento continua visível como cancelado, fora do saldo.

Cada item tem teste na task dona (marcado `[RF n]`).

---

### Task 1: Catálogo de códigos LP

**Files:** Create `SiagroB1.Application/Services/Accounting/Postings/StandardPostingCatalog.cs`,
`StandardPostingDefinition.cs`, `PostingContextKind.cs`. Test `SiagroB1.Application.Tests/Accounting/Postings/StandardPostingCatalogTests.cs`.

**Interfaces (Produces):**
- `enum PostingContextKind { PurchaseInvoice, SalesInvoice, FinancialDocument, Settlement, AccountEntry, Transfer, CostOfSales }`
- `record StandardPostingDefinition(string Code, string Description, PostingContextKind Context, bool PerLine,
  string Batch, string? Reverses, string Help, IReadOnlyList<string> AccountSources, IReadOnlyList<string> ValueSources,
  IReadOnlyList<string> Placeholders)` — `AccountSources` em ordem de fallback, sempre terminando em `"Fixed"`.
- `static class StandardPostingCatalog { IReadOnlyList<StandardPostingDefinition> All; StandardPostingDefinition? Find(string code); }`

Conteúdo (da spec §5 + §8 + §9):

| Código | Contexto | PerLine | Reverses |
|---|---|---|---|
| 650 / 660 | PurchaseInvoice | sim / não | — |
| 655 / 665 | PurchaseInvoice | sim / não | 650 / 660 |
| 610 / 620 | SalesInvoice | sim / não | — |
| 630 / 635 | SalesInvoice | sim / não | 610 / 620 |
| 500 / 510 | FinancialDocument (receber / pagar) | não | — |
| 505 / 515 | FinancialDocument | não | 500 / 510 |
| 520 / 530 | Settlement (receber / pagar) | não | — |
| 527 / 531 | Settlement | não | 520 / 530 |
| 563 / 562 | AccountEntry (entrada / saída) | não | — |
| 565 / 564 | AccountEntry | não | 563 / 562 |
| 560 / 561 | Transfer | não | — / 560 |
| 670 / 675 | CostOfSales (filial + produto) | sim | — / 670 |

Fontes de conta: linha `Line.LedgerAccount`, `Item.LedgerAccount`, `Partner.LedgerAccount`, `Document.LedgerAccount`,
`FinancialAccount.LedgerAccount`, `TargetFinancialAccount.LedgerAccount` (transferência), `Fixed` — cada contexto
lista só as que existem nele. Fontes de valor:
- Compra linha: `Line.Total`, `Line.GrandTotal`, `Line.FiscalStockCost` (GrandTotal − IcmsValue se MovesFiscalInventory, senão 0), `Line.IcmsValue`, `Line.PisValue`, `Line.CofinsValue`, `Line.FunruralValue`, `Line.FreightValue`, `Line.DiscountValue`, `Line.OtherExpensesValue`.
- Compra documento: `Document.GrandTotal`, `Document.NetPayable`, `Document.TotalFunrural`, `Document.WithheldIss`, `Document.WithheldIrrf`, `Document.WithheldInss`, `Document.WithheldPis`, `Document.WithheldCofins`, `Document.WithheldCsll`, `Document.CteIcmsValue`.
- Venda linha: `Line.Total`, `Line.GrandTotal`, `Line.NetTotal`, `Line.IcmsValue`, `Line.PisValue`, `Line.CofinsValue`, `Line.FreightValue`, `Line.DiscountValue`. Venda documento: `Document.GrandTotal`.
- Título: `Document.NetAmount`. Baixa: `Settlement.Amount` (absoluto), `Settlement.InterestAmount`, `Settlement.FineAmount`, `Settlement.DiscountAmount`. Conta: `Entry.Amount` (absoluto). Transferência: `Transfer.Amount`. CMV: `Item.CostOfSales`.
Marcadores de histórico: `{DocNumber}`, `{PartnerName}`, `{PartnerCode}`, `{ItemCode}`, `{Description}`, `{Date}`.
Ajuda do 670: "Pré-requisito: saldo inicial do estoque fiscal lançado."

- [ ] Testes: `Every_reversal_points_to_existing_code_in_same_context`, `Account_sources_end_with_fixed`,
  `Codes_are_unique`, `Find_returns_null_for_unknown`. → FAIL (compilação) → implementar → PASS → commit.

### Task 2: Cadastro do lançamento padrão (entidades, migration, API)

**Files:** Create `SiagroB1.Domain/Entities/StandardPosting.cs`, `StandardPostingLine.cs`,
`SiagroB1.Domain/Enums/DimensionSource.cs`; `SiagroB1.Application/Services/Accounting/Postings/StandardPostingsService.cs`;
`SiagroB1.Web/Controllers/StandardPostingsController.cs`; migration App `AddStandardPostings`, Common
`AddStandardPostingMenus` (`standardPostings` "Lançamento Padrão", order 10, StandaloneOnly, ADMIN).
Test `Accounting/Postings/StandardPostingsServiceTests.cs`, EDM em `AccountingCoreEdmTests`.

**Interfaces (Produces):**
- `StandardPosting` (`STANDARD_POSTINGS`): `Code VARCHAR(3)` PK, `Active bool`, `UpdatedAt`, `UpdatedBy`, `Lines`.
- `StandardPostingLine` (`STANDARD_POSTING_LINES`): `Id`, `Code` FK cascade, `Sequence VARCHAR(3)`,
  `EntryType JournalEntryLineType` (1/2/3), `DebitAccountSource VARCHAR(40)?`, `DebitAccountCode VARCHAR(20)?`,
  `CreditAccountSource`, `CreditAccountCode`, `ValueSource VARCHAR(40)`, `HistoryTemplate VARCHAR(200)`,
  `DebitCostCenterSource DimensionSource {None=0, Document=1, Fixed=2}`, `DebitCostCenterCode`, idem Credit,
  `DebitItemSource`, `DebitItemCode`, idem Credit, `Active bool`. Único (Code, Sequence).
- `IStandardPostingsService`: `IQueryable<StandardPosting> QueryAll()`, `Task<StandardPosting> SaveAsync(StandardPosting posting, string user)`
  (upsert: substitui as linhas). Valida: código existe no catálogo; fontes de conta/valor pertencem ao
  catálogo do código; `Fixed` exige conta analítica existente; tipo 1 só débito, 2 só crédito, 3 os dois;
  sequência única; `DimensionSource.Fixed` exige código.
- OData: `StandardPostings` (GET com `$expand=Lines`, POST = upsert por código); função `StandardPostingCatalogGet()`
  devolvendo o catálogo (lista de complexos `{Code, Description, Context, PerLine, Reverses, Help, AccountSources, ValueSources, Placeholders}`).

- [ ] Testes: `Rejects_unknown_code`, `Rejects_value_source_from_other_context` ("Fonte de valor Settlement.Amount não se aplica ao código 650."),
  `Fixed_requires_analytic_account`, `Save_replaces_lines`, `Double_requires_both_sides`, EDM tem `StandardPostings` e `StandardPostingCatalogGet`.
- [ ] Migrations ensaiadas nas cópias `_ACC`; commit `DB: AddStandardPostings, AddStandardPostingMenus`.

### Task 3: Motor `AccountingPostingService`

**Files:** Create `Postings/PostingContext.cs`, `Postings/AccountingPostingService.cs` (+ `IAccountingPostingService`).
Test `Accounting/Postings/AccountingPostingServiceTests.cs`.

**Interfaces:**
- Consumes: `IJournalEntryPostingService.AddAsync`, `IJournalEntriesService.AddReversalAsync`, `AccountingPeriodGuard.FirstOpenDateOnOrAfterAsync`,
  `IAccountingSettingsService.GetAsync`, `DistributionRuleExpander` (conferir a assinatura no arquivo), `StandardPostingCatalog`.
- Produces:
  - `record PostingRow(IReadOnlyDictionary<string, string?> Accounts, IReadOnlyDictionary<string, decimal> Values,
    string? CostCenterCode, string? AccountingItemCode, string? DistributionRuleCode, IReadOnlyDictionary<string, string> Placeholders)`
  - `record PostingContext(string DocumentType, string DocumentKey, string Event, DateTime Date, string BranchCode,
    string? PartnerCode, IReadOnlyList<PostingRow> Rows, bool ExternalEvent = false)`
  - `IAccountingPostingService.Task<JournalEntry?> PostAsync(string code, PostingContext context, string user, CancellationToken ct = default)`
    — devolve o lançamento enfileirado ou `null` (no-op).
  - `Task<JournalEntry?> PostReversalAsync(string code, PostingContext context, string originalEvent, string user, CancellationToken ct = default)`
    — para códigos com `Reverses`: acha o original vivo `(Reverses, DocumentType, DocumentKey, originalEvent)`
    (no banco **e** no ChangeTracker); sem original → `null`; LP de estorno ativo → monta pelas sequências e
    marca `ReversalOf`/`ReversedBy` como o estorno manual; inativo → `AddReversalAsync(original, date)`.

Algoritmo do `PostAsync` (ordem): SAPB1 → null; corte nulo ou `Date < corte` → null; LP ausente/inativo → null;
original vivo do mesmo (code, doc, event) já existe → null; data = `Date`, e se o período estiver fechado e
`ExternalEvent` → `FirstOpenDateOnOrAfterAsync` + sufixo no histórico; para cada row (todas se `PerLine`, a
primeira senão) × cada sequência ativa: valor 0 → pula; conta pela fonte com fallback na ordem do catálogo até
`Fixed` (vazia em todas → `DefaultException("LP {c} seq. {s}: conta de débito não encontrada para {ItemCode/DocNumber}.")`);
CC/Item: `Document` → do row; se o row tem `DistributionRuleCode`, expande em várias linhas pelo
`DistributionRuleExpander` na data, arredondando e jogando a diferença na última `[RF 3]`; `Fixed` → código da
sequência; histórico = template com marcadores. Nenhuma linha → null. Lote do catálogo, `Origin=StandardPosting`.

- [ ] Testes: `NoOp_in_sapb1`, `NoOp_without_cutover`, `NoOp_before_cutover`, `NoOp_when_lp_inactive`,
  `NoOp_when_already_posted_for_same_event`, `Per_line_lp_generates_one_line_per_row_and_sequence`,
  `Zero_value_skips_line`, `Account_fallback_line_item_partner_fixed`, `Missing_account_throws_with_lp_prefix`,
  `Distribution_rule_splits_with_rounding_on_last` **[RF 3]**, `History_template_replaces_placeholders`,
  `External_event_in_closed_period_goes_to_first_open_day` **[RF 4]**, `Internal_event_in_closed_period_throws`,
  `Reversal_noop_without_original` **[RF 2]**, `Reversal_with_active_lp_links_original`,
  `Reversal_with_inactive_lp_mirrors_original`, `Reposting_after_reversal_is_allowed` **[RF 1]**, `Does_not_save_changes`.
- [ ] Commit.

### Task 4: Documento de Entrada (650/660/655/665)

**Files:** Create `Postings/PurchaseInvoicePostingContext.cs` (builder estático `Build(PurchaseInvoice, IReadOnlyList<PurchaseInvoiceItem>, string evt, items/partners lookup)`).
Modify `PurchaseInvoicesConfirmService.cs` (antes do SC em :107; usar `lines`, não `invoice.Items`),
`PurchaseInvoicesReverseConfirmService.cs` (antes do SC :55), `PurchaseInvoicesCancelService.cs` (antes do SC :79, só se o status anterior era Confirmed; `CancelAfterNfeAsync` = `ExternalEvent`). Registrar DI.
Test `Accounting/Postings/PurchaseInvoicePostingTests.cs` (seeds `TaxTestServices.PurchaseConfirm`, `FinancialDocumentTestServices.PurchaseReverse/PurchaseCancel`).

Eventos: confirmar → `PostAsync("650", ctx("Confirm"))` e `PostAsync("660", …)`; estornar confirmação e cancelar →
`PostReversalAsync("655", …, "Confirm")` e `("665", …)`. DocumentType `"PurchaseInvoice"`, chave `Key`, data
`PostingDate ?? IssueDate` (estorno/cancelamento: hoje), filial `BranchCode`, contas do row: `Line.LedgerAccount`
= linha, `Item.LedgerAccount` = produto, `Partner.LedgerAccount` = `DefaultLedgerAccountCode`, `Document.LedgerAccount` = cabeçalho.

- [ ] Testes: `Confirm_posts_650_per_line_and_660_total`, `Confirm_without_active_lp_does_not_block`,
  `Confirm_with_broken_lp_fails_confirmation`, `Reverse_confirm_posts_655_and_reconfirm_posts_again` **[RF 1]**,
  `Cancel_of_pending_invoice_posts_nothing`, `Cte_entry_posts_with_cte_icms_source`,
  `FiscalStockCost_is_grandtotal_minus_icms_only_when_moves_inventory`. → commit.

### Task 5: Documento de Saída (610/620/630/635)

**Files:** Create `Postings/SalesInvoicePostingContext.cs`. Modify `SalesInvoicesConfirmService.cs` (entre :220 e
o SC :229), `SalesInvoicesReverseConfirmService.cs` (antes do SC :145), `SalesInvoicesCancelService.cs` (antes do
SC :141; carregar os itens AsNoTracking para o contexto; `CancelAfterNfeAsync` e `CancelForRefusalAsync` = `ExternalEvent` só no primeiro). Test `Accounting/Postings/SalesInvoicePostingTests.cs` (seeds `SalesFirmReceivableSeed.Confirm`, `NfeCancelTestServices`).

DocumentType `"SalesInvoice"`, data `InvoiceDate`.
- [ ] Testes: `Confirm_posts_610_and_620`, `Deferred_confirm_does_not_commit_but_enqueues`,
  `Reverse_confirm_posts_630_635`, `Nfe_cancellation_in_closed_month_posts_on_first_open_day` **[RF 4]**,
  `Nfe_handler_failure_discards_enqueued_entry` (ChangeTracker.Clear). → commit.

### Task 6: Títulos (500/510/505/515)

**Files:** Create `Postings/FinancialDocumentPostingContext.cs` com `static bool IsPostable(FinancialDocument d)`
(regra da spec §9: não Provisional; Origin fora de PurchaseInvoice, PurchaseFiscalReconciliation, OtherPurchaseInvoice,
SalesInvoice, SalesInvoiceNoContract). Modify `FinancialDocumentsManualCreateService`, `FinancialAdvancesCreateService`
(núcleo :170-240, antes da baixa), `FinancialDocumentsGenerateWashoutReceivableService.EnqueueAsync`, criação de
desdobramento (`Origin=FinancialDocument`, achar o serviço), e `FinancialDocumentsCancelService.Cancel` (estático
:208 → mover o hook para os chamadores ou tornar o serviço instância; decisão do implementador, registrar ruling).
Código pela direção: Receivable 500/505, Payable 510/515. DocumentType `"FinancialDocument"`, evento `"Create"`.
Contas: `Partner.LedgerAccount`, `Document.LedgerAccount`, `Fixed`; marcador `{IsAdvance}` não — fonte de conta extra `AdvanceAccount` = `Fixed` (o contador separa por sequência; documentar na ajuda).

- [ ] Testes: `Manual_payable_posts_510`, `Advance_receivable_posts_500`, `Firm_from_invoice_does_not_post`,
  `Provisional_never_posts`, `Cancel_manual_posts_515`, `Cancel_of_unposted_title_is_noop`. → commit.

### Task 7: Baixas (520/530/527/531)

**Files:** Create `Postings/SettlementPostingHook.cs`:
`Task EnqueueAsync(IEnumerable<FinancialSettlement> rows, string user, CancellationToken ct = default)` — para cada
linha: título provisório → ignora; `Amount > 0` → 520 (Receivable) / 530 (Payable), evento `"Settle"`, chave = Key
da linha; `Amount < 0` → `PostReversalAsync("527"/"531", …, chave = ReversedSettlementKey, "Settle")`.
Conta `FinancialAccount.LedgerAccount` da `FinancialAccountCode` (vazia nas origens sem caixa), `Partner.LedgerAccount`,
`Document.LedgerAccount`, `RelatedDocument.LedgerAccount` (título de `RelatedDocumentKey`), `Fixed`.
Modify (chamar o hook logo após adicionar as linhas, antes do SC de cada um): `FinancialDocumentsSettleService` (:66),
`FinancialDocumentsReverseSettlementService` (:105), `FinancialDocumentsApplyAdvanceService`, `FinancialDocumentsNettingService`
(Execute e Enqueue*), `FinancialDocumentsDationService`, `FinancialDocumentsComposeService`, `FinancialDocumentsUncomposeService`,
`FinancialAdvancesRefundService`, `FinancialAdvancesRelinkContractService`. A chave Guid da linha precisa existir antes do SC:
conferir que o EF gera o Guid no `Add` (cliente); se não, gerar `Guid.NewGuid()` antes do Add e registrar ruling.

- [ ] Testes: `Settle_payable_posts_530_with_bank_account`, `Reverse_settlement_posts_531_linked`,
  `Reverse_of_settlement_before_cutover_is_noop` **[RF 2]**, `Advance_application_posts_both_lines_without_cash_account`,
  `Settlement_of_provisional_title_posts_nothing`, e um teste de guarda `Every_service_that_adds_settlements_calls_hook`
  (varre por reflexão/grep os serviços que fazem `FinancialSettlements.Add`/`AddRange` e confere a lista acima). → commit.

### Task 8: Lançamento na conta e transferência (562/563/564/565, 560/561) + cancelamento lógico

**Files:** Modify `FinancialAccountEntry`/`FinancialAccountTransfer` (+ `CanceledAt DateTime?`, `CanceledBy VARCHAR(100)?`),
`FinancialAccountEntriesDeleteService` e `FinancialAccountTransfersDeleteService` (cancelar em vez de `Remove`),
todas as leituras de saldo/lista/extrato/conciliação de lançamentos (`FinancialAccountMovementsQuery` e afins —
achar todas por grep de `FinancialAccountEntries`) filtrando `CanceledAt == null`; migration App `AddFinancialEntryCancellation`.
Create `Postings/AccountEntryPostingContext.cs`. Filial: `FinancialAccount.BranchCode` ?? menor código de `Branchs`.
Hooks: criar lançamento → 563 (Amount > 0) / 562; cancelar → `PostReversalAsync("565"/"564", …, "Create")`;
transferência criar → 560; cancelar → `PostReversalAsync("561", …, "Create")`. Lançamentos-perna da transferência
(`TransferKey` preenchido) **não** disparam 562/563.

- [ ] Testes: `Delete_entry_marks_canceled_and_keeps_row` **[RF 5]**, `Canceled_entry_leaves_balance_and_statement`,
  `Entry_posts_563_or_562_by_sign`, `Cancel_posted_entry_posts_565` **[RF 5]**, `Transfer_posts_560_not_legs`,
  `Branch_falls_back_to_lowest_branch`. Suite completa verde (o cancelamento lógico mexe em telas existentes).
- [ ] Migration ensaiada nas cópias; commit `DB: AddFinancialEntryCancellation`.

### Task 9: CMV no fechamento do estoque fiscal (670/675)

**Files:** Create `Postings/CostOfSalesPostingContext.cs`. Modify `FiscalStockPeriodsService.CloseLockedAsync` (montar
por produto o custo FIFO das saídas de venda do mês menos devoluções de venda, a partir do ledger já calculado
no fechamento — conferir em `FifoLedgerModels` o tipo de movimento e `ValueOut`) e `ReopenAsync`
(`PostReversalAsync("675", …, "Close")`). DocumentType `"FiscalStockPeriod"`, chave = `Key` do período, data = último
dia do mês, contas `Item.LedgerAccount`, `Fixed`.

- [ ] Testes: `Close_posts_670_per_item_with_fifo_cost`, `Sales_return_reduces_cost_of_sales`,
  `Reopen_reverses_670`, `Close_without_active_670_does_not_block`. → commit.

### Task 10: Frontend — tela Lançamento Padrão

Rotas `standard-postings` (lista do catálogo com colunas Código, Descrição, Contexto, Situação configurada) e
`standard-postings/{code}` (cabeçalho com Ativo + ajuda do código; grade JSONModel de sequências com Selects
alimentados pelo catálogo do código para fontes de conta/valor, Input com value help de conta analítica quando
a fonte for `Fixed`, Selects de CC/Item `Nenhum/Do documento/Fixo`, histórico com a lista de marcadores na ajuda).
Gravar = POST `StandardPostings` (upsert). Enum em expressão com `%{…}`; datas Edm.Date como texto; nada de
navegação OData (`$expand=Lines` + JSONModel, como o detalhe do lançamento da fase 1).
- [ ] typecheck + lint; commit `feat(financial): cadastro do lançamento padrão`.

### Task 11: Verificação ponta a ponta

Stack `ceagui` nas cópias `_ACC` (atualizar com as migrations). Configurar corte 01/01/2026, LPs 650/660, 610/620,
510, 530, 562, 670 com contas do plano de teste. Pelo menu: confirmar uma entrada e uma saída existentes (ou criar),
ver os lançamentos; estornar a confirmação da entrada e confirmar de novo; baixar um título manual; criar e excluir
um lançamento na conta (fica cancelado); fechar o mês do estoque fiscal e conferir o 670; Razão batendo.
Derrubar a stack por PID. Revisão final do branch.
