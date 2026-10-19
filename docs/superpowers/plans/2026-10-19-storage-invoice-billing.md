# Faturamento da fatura de armazenagem — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Documento de saída gera título a receber pela natureza (com ou sem contrato), a NFS vira um documento de saída de número digitado, e a fatura de armazenagem passa a ser faturada por documentos de saída com situação derivada (Aberta → Faturada parcial → Faturada → Paga).

**Architecture:** Parte 1 cria um serviço irmão de `FinancialDocumentsOtherPurchaseService` para o lado de venda (origem nova `SalesInvoiceNoContract`, título por documento em parcelas da condição de pagamento), acionado pelos mesmos pontos que já acionam `FinancialDocumentsSalesFirmSyncService`. Parte 2 adiciona `TaxDocumentKind.Nfse` ao documento de saída — o caminho "termina no Confirmar" de `Other` já existe; a NFS só ganha trava de duplicidade por PN+série+número. Parte 3 adiciona `SalesInvoice.StorageInvoiceKey`, um serviço de recálculo de situação da fatura e um guard de saldo, e troca o "Encerrar" por "Faturar" na tela.

**Tech Stack:** .NET 10, EF Core (SQL Server), OData v4, xUnit + EF InMemory (`SiagroB1.Application.Tests`); OpenUI5 + TypeScript (`siagro-b1-frontend`).

**Spec:** `docs/superpowers/specs/2026-10-19-storage-invoice-billing-design.md`

## Global Constraints

- Identificadores, tabelas e colunas em inglês; só textos ao usuário (labels, mensagens de negócio) em pt-BR.
- Funciona em **STANDALONE e SAPB1** (`ErpMode.IsStandalone(configuration)`).
- "Só daqui para frente": nenhum título retroativo para documento sem contrato confirmado antes da data de corte (`Financial:SalesNoContractReceivablesCutoverDate`, mesmo estilo de `Financial:SalesFirmReceivablesCutoverDate`).
- Sem retenção de ISS/PIS/COFINS/IR/CSLL agora: título = valor bruto (`GrandTotal`) da linha; o plano de parcelas recebe o valor líquido como parâmetro para não fechar o caminho.
- Exclusões mantidas: complemento de ICMS, custódia (`CustodyRole != None`), documento de liberação de entrega (`ShipmentReleaseKey != null`).
- NFS nunca transmite à SEFAZ, nunca consome numeração da NF-e, nunca gera DANFE/XML/e-mail.
- Fatura nunca faturada além de `TotalAmount`; rascunhos (não cancelados) consomem saldo.
- Migrations: `dotnet ef migrations add <Nome> --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web`; aplicar em **todas as empresas**; commit com trailer `DB: <Nome>`.
- Todo serviço novo registrado à mão em `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs`.
- `git add` todo arquivo novo logo ao criá-lo; commits `tipo(escopo): pt-BR` terminando em `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`; backend e frontend em commits separados nos seus repos; **nunca push**.
- Branch: `feature/storage-invoice-billing` nos dois repos (criar no frontend a partir de `main`); conferir o branch antes de cada commit.

## Review Focus

1. Documento sem contrato em SAPB1: `CreatesFinancialDocumentAsync` devolve `true` sempre em SAPB1 — toda saída sem contrato passaria a gerar título. Esperado: em SAPB1 o título sem contrato só nasce após a data de corte e é o mesmo comportamento do firme com contrato (decisão documentada na Task 1; teste `Sapb1_line_without_contract_after_cutover_creates_receivable`).
2. Documento misto (linhas com e sem contrato): esperado um título por contrato (firme atual) **e** parcelas sem contrato só pela soma das linhas sem contrato — nunca o `GrandTotal` do documento inteiro (Task 1, `Mixed_document_splits_contract_and_no_contract_amounts`).
3. Estorno/cancelamento com título sem contrato já baixado: esperado recusa antes de qualquer escrita, como o firme (Task 2, `Undo_refused_when_no_contract_receivable_settled`).
4. Duas abas faturando a mesma fatura ao mesmo tempo / alterar o valor da linha de um rascunho já vinculado: esperado o guard roda também no update de linha e recusa o excesso (Task 6, `Item_update_over_balance_is_refused`).
5. Cancelar NFS/NF-e vinculada depois de a fatura estar "Paga": esperado a fatura voltar para Faturada parcial/Aberta (Task 7, `Cancel_linked_document_reopens_paid_invoice`).

---

## Parte 1 — Financeiro pela natureza

### Task 1: Título a receber sem contrato no confirmar (backend)

**Repo:** siagro-b1-backend

**Files:**
- Modify: `SiagroB1.Domain/Enums/FinancialDocumentOrigin.cs` — `SalesInvoiceNoContract = 10` (doc: parcelas do documento de saída sem contrato; OriginKey = chave do documento, repetida em N títulos).
- Modify: `SiagroB1.Infra/Context/AppDbContext.cs:650-665` — o filtro do índice FirmOrigin exclui também `SalesInvoiceNoContract`; o índice de parcelas de `OtherPurchaseInvoice` passa a cobrir as duas origens (`[OriginType] IN (9,10)`).
- Create: `SiagroB1.Application/Services/Financials/FinancialDocumentsSalesNoContractService.cs`
- Modify: `SiagroB1.Application/Services/SalesInvoices/SalesInvoicesConfirmService.cs:205-215` — chamar o serviço novo ao lado de `salesFirmSync.EnqueueSyncAsync` (mesma condição `!icmsComplement`).
- Modify: `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs`, `SiagroB1.Web/appsettings.json` (chave vazia documentada).
- Create migration: `AddSalesInvoiceNoContractOrigin` (só índices).
- Test: `SiagroB1.Application.Tests/Financials/FinancialDocumentsSalesNoContractTests.cs` (seed: copiar o estilo de `SalesFirmReceivableSeed.cs` / `FinancialDocumentsOtherPurchaseTests.cs`).

**Interfaces:**
- Produces:
  - `public const string CutoverDateKey = "Financial:SalesNoContractReceivablesCutoverDate";`
  - `Task EnqueueAsync(SalesInvoice invoice, string userName)` — lê as linhas com `SalesContractKey == null` e natureza que gera financeiro; cria parcelas.
  - `Task EnsureCanUndoAsync(Guid salesInvoiceKey)` — recusa se algum título ativo da origem não está solto (mesma regra de `FinancialDocumentsFirmSyncService`/`EnsureLooseAsync`).
  - `void EnqueueUndo(Guid salesInvoiceKey, string userName)` — cancela títulos abertos com motivo `FinancialDocumentsSalesFirmSyncService.DocumentUndoneReason`.
  - `internal static bool Applies(SalesInvoice invoice, DateTime? cutover)` — G1–G3 de `DesiredAsync` + `InvoiceDate >= cutover`.

- [ ] **Step 1: Write the failing tests** (`FinancialDocumentsSalesNoContractTests`)
  - `Line_without_contract_and_nature_with_financial_creates_receivable` — 1 linha GrandTotal 1000,00, condição 30/60 → 2 títulos `Direction=Receivable`, `Nature=Firm`, `OriginType=SalesInvoiceNoContract`, `OriginKey=invoice.Key`, `SalesContractKey=null`, `SalesInvoiceKey=invoice.Key`, valores 500/500, vencimentos D+30/D+60, `InstallmentNumber` 1 e 2.
  - `No_payment_condition_creates_single_title_due_on_issue_date`.
  - `Nature_without_financial_creates_nothing`.
  - `Invoice_before_cutover_creates_nothing` — cutover 2026-10-20, InvoiceDate 2026-10-19 → 0 títulos.
  - `Missing_cutover_key_applies_to_all` — com log de aviso.
  - `Icms_complement_custody_and_release_documents_create_nothing` (3 `[InlineData]`).
  - `Mixed_document_splits_contract_and_no_contract_amounts` — linha A contrato 700 + linha B sem contrato 300 → firme de 700 no contrato (sync atual) e parcelas somando 300.
  - `Sapb1_line_without_contract_after_cutover_creates_receivable` — `Erp=SAPB1`.
  - `Requires_contract_does_not_decide_financial` — natureza `RequiresContract=false`, `CreatesFinancialDocument=true` → cria.
- [ ] **Step 2: Run** `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~FinancialDocumentsSalesNoContractTests"` — Expected: FAIL (tipo não existe).
- [ ] **Step 3: Implement** o serviço. Reusar `PaymentInstallmentCalculator.Calculate` como `FinancialDocumentsOtherPurchaseService.PlanInstallmentsAsync` (extrair para `internal static` compartilhado se ficar idêntico); código do título por `docNumberSequence.AssignAsync(TransactionCode.FinancialDocument …)`; `TaxDocumentNumber` via `R.TaxDocument(...)`; idempotente (não cria se já há título ativo da origem para o documento). Data de corte lida como em `SalesContractsCloseService` (inválida → aviso e sem limite).
- [ ] **Step 4: Run** o filtro acima e `--filter "FullyQualifiedName~SalesFirm|FullyQualifiedName~SalesInvoicesConfirm"` — Expected: PASS, sem regressão no firme com contrato.
- [ ] **Step 5: Migration + commit**
```bash
git add SiagroB1.Application/Services/Financials/FinancialDocumentsSalesNoContractService.cs SiagroB1.Application.Tests/Financials/FinancialDocumentsSalesNoContractTests.cs SiagroB1.Migrations/AppContext/*AddSalesInvoiceNoContractOrigin* <demais modificados>
git commit -m "feat(financial): gerar título a receber pela natureza sem contrato" # corpo: porquê (ativo/sacaria) + "DB: AddSalesInvoiceNoContractOrigin"
```

### Task 2: Desfazer títulos sem contrato no cancelamento/estorno (backend)

**Repo:** siagro-b1-backend

**Files:**
- Modify: `SiagroB1.Application/Services/SalesInvoices/SalesInvoicesCancelService.cs` (`EnsureCanCancelAsync` e o cancelamento), `SalesInvoicesReverseConfirmService.cs` (`ExecuteAsync`) — chamar `EnsureCanUndoAsync` antes de qualquer escrita e `EnqueueUndo` junto do undo atual do firme.
- Modify: `SiagroB1.Application/Services/Financials/FinancialDocumentsCancelService.cs:48` — o tratamento especial de `OtherPurchaseInvoice` vale também para `SalesInvoiceNoContract` (título dessa origem não se cancela à mão).
- Test: `SiagroB1.Application.Tests/Financials/FinancialDocumentsSalesNoContractUndoTests.cs`

**Interfaces:** Consumes Task 1 (`EnsureCanUndoAsync`, `EnqueueUndo`).

- [ ] **Step 1: Failing tests**
  - `Cancel_cancels_open_no_contract_receivables` — status `Canceled`, motivo `"Documento de saída cancelado ou estornado"`.
  - `Reverse_confirm_cancels_open_no_contract_receivables`.
  - `Undo_refused_when_no_contract_receivable_settled` — `BusinessException`, documento continua `Confirmed`, nenhum título alterado.
  - `Manual_cancel_of_no_contract_receivable_is_refused`.
- [ ] **Step 2: Run** `--filter "FullyQualifiedName~SalesNoContractUndo"` — FAIL.
- [ ] **Step 3: Implement** as chamadas.
- [ ] **Step 4: Run** o filtro + `--filter "FullyQualifiedName~SalesInvoicesCancel|FullyQualifiedName~FinancialDocumentsCancel"` — PASS.
- [ ] **Step 5: Commit** `fix(financial): cancelar títulos sem contrato ao desfazer o documento de saída`.

---

## Parte 2 — NFS como documento de saída

### Task 3: Tipo NFS e trava de duplicidade (backend)

**Repo:** siagro-b1-backend

Decisão: NFS = `TaxDocumentKind.Nfse = 3` (o enum é compartilhado com entrada; o lado de compra ignora). Reaproveita o caminho de `Other` ("termina no Confirmar, com número e série digitados"). Valor inteiro: sem migration de coluna.

**Files:**
- Modify: `SiagroB1.Domain/Enums/TaxDocumentKind.cs` — `/// <summary>NFS(-e) de serviço emitida no portal da prefeitura: número, série e data digitados; sem chave nem transmissão.</summary> Nfse = 3`.
- Modify: `SiagroB1.Application/Services/SalesInvoices/SalesInvoicesCreateService.cs:56` — não forçar `Nfe` quando vier `Nfse`; `Return`/`IcmsComplement` continuam `Nfe`.
- Modify: `SiagroB1.Application/Services/SalesInvoices/SalesInvoicesConfirmService.cs` — para `Nfse`: exigir `TaxDocumentNumber`, `TaxDocumentSeries`, `InvoiceDate` (mensagem `"Informe número, série e data de emissão da NFS."`); nunca chamar a numeração automática da NF-e; trava de duplicidade.
- Create: `SiagroB1.Application/Services/SalesInvoices/SalesInvoiceNfseDuplicateGuard.cs`
- Modify: `SiagroB1.Application/Services/Nfe/SalesInvoicesNfeIssueService.cs:46` — já recusa `!= Nfe`; só cobrir com teste.
- Modify: NF-e e-mail automático (`Services/NfeEmail/*` gatilho) — garantir filtro `TaxDocumentKind == Nfe` (conferir; teste se mudar).
- Test: `SiagroB1.Application.Tests/SalesInvoices/SalesInvoicesNfseTests.cs`

**Interfaces:**
- Produces: `internal static Task EnsureNotDuplicateAsync(AppDbContext context, SalesInvoice invoice)` — conflito = mesmo `CardCode` + `TaxDocumentSeries` + `TaxDocumentNumber`, `TaxDocumentKind == Nfse`, `Key != invoice.Key`, `InvoiceStatus != Cancelled`; mensagem `"Já existe a NFS {série}/{número} para este parceiro (documento {InvoiceNumber})."`
- Produces: `TaxDocumentKind.Nfse` (OData string `"Nfse"`) usado pelas Tasks 4, 6 e 9.

- [ ] **Step 1: Failing tests**
  - `Confirm_nfse_without_number_series_or_date_is_refused`.
  - `Confirm_nfse_does_not_consume_nfe_numbering` — `DOC_NUMBERS`/série de NF-e da filial inalterados; `NfeStatus == None`.
  - `Duplicate_nfse_same_partner_series_number_is_refused`; `Same_number_other_partner_is_allowed`; `Same_number_of_cancelled_document_is_allowed`.
  - `Transmit_nfse_is_refused` (via `SalesInvoicesNfeIssueService`).
  - `Confirm_nfse_with_financial_nature_creates_receivable` — integra com Task 1.
- [ ] **Step 2: Run** `--filter "FullyQualifiedName~SalesInvoicesNfseTests"` — FAIL.
- [ ] **Step 3: Implement**.
- [ ] **Step 4: Run** o filtro + `--filter "FullyQualifiedName~SalesInvoices"` — PASS.
- [ ] **Step 5: Commit** `feat(invoice): NFS como documento de saída de número digitado`.

### Task 4: NFS na tela de documento de saída (frontend)

**Repo:** siagro-b1-frontend

**Files:**
- Modify: `webapp/controller/salesInvoices/Add.controller.ts:72` e `webapp/view/salesInvoices/Add.view.xml`, `Edit.view.xml`, `Detail.view.xml` — opção "NFS (serviço)" no tipo de documento fiscal; com `Nfse`: número/série/data editáveis e obrigatórios, botões Transmitir/DANFE/XML/e-mail/CC-e/Cancelar NF-e invisíveis.
- Modify: `webapp/controller/salesInvoices/Main.controller.ts` + `Main.view.xml` — filtro e coluna "Tipo doc. fiscal" com `Nfse` → "NFS".
- Modify: `webapp/i18n/i18n.properties` (pt-BR) e `i18n_en.properties`.

- [ ] **Step 1:** `npm run ts-typecheck` e `npm run lint` — PASS antes.
- [ ] **Step 2:** Implementar; formatter único `taxDocumentKindText(kind)` (`Nfe`→"NF-e", `Other`→"Outro", `Nfse`→"NFS").
- [ ] **Step 3:** `npm run ts-typecheck && npm run lint` — PASS; verificar no navegador (stack local, profile `yktb`): criar NFS, confirmar sem número → mensagem; confirmar com número → título aparece no financeiro; botão Transmitir ausente; filtro "NFS" na lista.
- [ ] **Step 4: Commit** (frontend) `feat(invoice): documento de saída do tipo NFS`.

---

## Parte 3 — Faturar a fatura de armazenagem

### Task 5: Situações novas e vínculo documento → fatura (backend)

**Repo:** siagro-b1-backend

Decisão de enum: renomear `Closed = 2` para `Paid = 2` — as faturas encerradas viram "Paga" sem UPDATE de dados (legado do spec); novas: `PartiallyInvoiced = 4`, `Invoiced = 5`. `Open = 1`, `Cancelled = 3` mantidos.

**Files:**
- Modify: `SiagroB1.Domain/Enums/StorageInvoiceStatus.cs`.
- Modify: `SiagroB1.Domain/Entities/SalesInvoice.cs` — `public Guid? StorageInvoiceKey { get; set; }` + `public virtual StorageInvoice? StorageInvoice { get; set; }` (FK, `OnDelete Restrict`, índice).
- Modify: `SiagroB1.Domain/Entities/StorageInvoice.cs` — `public ICollection<SalesInvoice> SalesInvoices { get; set; } = [];` — saldo vem do serviço da Task 6, não de propriedade calculada.
- Delete: `SiagroB1.Application/Services/StorageInvoices/StorageInvoiceCloseService.cs`, `StorageInvoiceOpenService.cs`, `SiagroB1.Web/Actions/StorageInvoices/StorageInvoiceCloseController.cs`, `StorageInvoiceOpenController.cs`, registros em `ServiceCollectionExtensions.cs:668-669`, actions no EDM builder.
- Modify: todo uso de `StorageInvoiceStatus.Closed` (grep em Application/Reports/Web) → `Paid`.
- Create migration: `AddSalesInvoiceStorageInvoiceKey` (coluna + FK + índice).
- Create: `SiagroB1.Application/Services/StorageInvoices/StorageInvoicesRecalculateStatusService.cs`
- Test: `SiagroB1.Application.Tests/StorageInvoices/StorageInvoicesRecalculateStatusTests.cs`

**Interfaces:**
- Produces: `public static Task RecalculateAsync(AppDbContext context, Guid storageInvoiceKey)` (padrão estático de `FinancialDocumentsRecalculateBalanceService.RecalculateAsync`). Regra, nesta ordem: `Cancelled` fica; sem documento vinculado e status `Paid` → fica (legado); `invoiced = Σ GrandTotal` das linhas dos documentos vinculados com `InvoiceStatus is Confirmed or Returned`; `invoiced == 0` → `Open`; `< TotalAmount` → `PartiallyInvoiced`; `>= TotalAmount` e todo título ativo (`Status != Canceled`, `OriginType in (SalesInvoice, SalesInvoiceNoContract)`, `SalesInvoiceKey` ∈ vinculados) com `Balance == 0` e pelo menos um título → `Paid`; senão `Invoiced`.
- SAPB1: se nenhum título existir no Siagro para documentos confirmados (financeiro no SAP), `Invoiced` é o teto — "Paga" só por título local. Registrar no comentário do método.

- [ ] **Step 1: Failing tests**
  - `No_documents_is_open`; `Draft_documents_keep_open`.
  - `Confirmed_below_total_is_partially_invoiced` — total 1000, NF-e 400 → `PartiallyInvoiced`.
  - `Confirmed_equal_total_is_invoiced`; `All_titles_settled_is_paid`; `Cancelled_titles_ignored_for_paid`.
  - `Legacy_paid_without_documents_stays_paid`; `Cancelled_invoice_stays_cancelled`.
- [ ] **Step 2: Run** `--filter "FullyQualifiedName~StorageInvoicesRecalculateStatusTests"` — FAIL.
- [ ] **Step 3: Implement**; `dotnet build SiagroB1.sln` sem referência a `Closed`.
- [ ] **Step 4: Run** filtro + `dotnet test SiagroB1.Application.Tests` — PASS.
- [ ] **Step 5: Commit** `feat(storage): situações de faturamento da fatura de armazenagem` + `DB: AddSalesInvoiceStorageInvoiceKey`; corpo: "Encerrar" removido, Closed=2 virou Paid=2.

### Task 6: Guard de saldo da fatura e trava (backend)

**Repo:** siagro-b1-backend

**Files:**
- Create: `SiagroB1.Application/Services/StorageInvoices/StorageInvoiceBillingGuardService.cs`
- Modify: `SalesInvoicesCreateService.cs`, `SalesInvoicesUpdateService.cs`, `SalesInvoicesItemsCreateService.cs`, `SalesInvoicesItemsUpdateService.cs`, `SalesInvoicesItemsDeleteService.cs` (só recálculo, não bloqueia) — chamar o guard quando o documento tem `StorageInvoiceKey`.
- Modify: `StorageInvoiceCancellationService.cs:36` — recusar com `"Cancele antes os documentos de saída desta fatura."` se houver documento vinculado com `InvoiceStatus != Cancelled`.
- Modify: `StorageInvoiceClosingService.cs` — fatura com documento vinculado não é recalculada/reaberta (hoje o fechamento cria fatura nova; garantir que nenhum caminho altera `TotalAmount`/itens de fatura com vínculo).
- Test: `SiagroB1.Application.Tests/StorageInvoices/StorageInvoiceBillingGuardTests.cs`

**Interfaces:**
- Produces: `Task<decimal> GetBalanceAsync(Guid storageInvoiceKey, Guid? ignoringSalesInvoiceKey = null)` — `TotalAmount − Σ GrandTotal` de documentos vinculados `InvoiceStatus != Cancelled` (rascunho conta).
- Produces: `Task EnsureCanBillAsync(SalesInvoice invoice, decimal newDocumentTotal)` — recusa se fatura `Cancelled`/`Paid`, PN ≠ `CardCode` da fatura, ou `newDocumentTotal > GetBalanceAsync(key, invoice.Key)`; mensagem `"Valor excede o saldo a faturar da fatura {Code} (saldo R$ {saldo:N2})."`
- Produces: `bool IsLocked(StorageInvoice)` = existe documento vinculado (para edição manual/fechamento).

- [ ] **Step 1: Failing tests**
  - `Create_within_balance_is_allowed`; `Create_over_balance_is_refused`.
  - `Draft_consumes_balance` — rascunho 600 de 1000 → segundo de 500 recusado.
  - `Cancelled_document_returns_balance`.
  - `Item_update_over_balance_is_refused`.
  - `Other_partner_is_refused`; `Cancelled_or_paid_invoice_is_refused`.
  - `Cancel_storage_invoice_with_active_document_is_refused`.
- [ ] **Step 2: Run** `--filter "FullyQualifiedName~StorageInvoiceBillingGuardTests"` — FAIL.
- [ ] **Step 3: Implement**.
- [ ] **Step 4: Run** filtro + `--filter "FullyQualifiedName~SalesInvoices|FullyQualifiedName~StorageInvoice"` — PASS.
- [ ] **Step 5: Commit** `feat(storage): impedir faturar a fatura de armazenagem além do total`.

### Task 7: Recalcular a situação nos gatilhos (backend)

**Repo:** siagro-b1-backend

**Files:**
- Modify: `SalesInvoicesConfirmService.cs`, `SalesInvoicesCancelService.cs`, `SalesInvoicesReverseConfirmService.cs`, `SalesInvoicesCreateService.cs`, `SalesInvoicesDeleteService.cs`, `Financials/FinancialDocumentsSettleService.cs`, `Financials/FinancialDocumentsReverseSettlementService.cs` (e compensação/encontro de contas se zerarem saldo: `FinancialDocumentsNettingService`, `FinancialDocumentsApplyAdvanceService`) — após a escrita, `StorageInvoicesRecalculateStatusService.RecalculateAsync` para a fatura do documento (no título: via `SalesInvoiceKey` → `StorageInvoiceKey`).
- Test: `SiagroB1.Application.Tests/StorageInvoices/StorageInvoiceStatusTriggersTests.cs`

**Interfaces:** Consumes Task 5 `RecalculateAsync`; Task 1 títulos.

- [ ] **Step 1: Failing tests**
  - `Confirm_linked_document_sets_partially_invoiced`.
  - `Settle_last_receivable_sets_paid`; `Reverse_settlement_returns_to_invoiced`.
  - `Cancel_linked_document_reopens_paid_invoice` — Paga → Aberta (único documento) / Faturada parcial (havia dois).
  - `Settling_unrelated_title_does_not_touch_storage_invoice`.
- [ ] **Step 2: Run** `--filter "FullyQualifiedName~StorageInvoiceStatusTriggersTests"` — FAIL.
- [ ] **Step 3: Implement**.
- [ ] **Step 4: Run** filtro + `dotnet test SiagroB1.Application.Tests` — PASS.
- [ ] **Step 5: Commit** `feat(storage): recalcular a situação da fatura ao confirmar, cancelar e baixar`.

### Task 8: Leitura para a tela da fatura (backend)

**Repo:** siagro-b1-backend

**Files:**
- Create: `SiagroB1.Application/Services/StorageInvoices/StorageInvoicesBillingGetService.cs`
- Create: `SiagroB1.Web/Actions/StorageInvoices/StorageInvoiceBillingController.cs` — função OData bound `GetBilling` em `StorageInvoices({key})`; registrar no EDM e em DI.
- Create: `SiagroB1.Domain/DTOs/StorageInvoices/StorageInvoiceBillingDto.cs`
- Test: `SiagroB1.Application.Tests/StorageInvoices/StorageInvoicesBillingGetTests.cs`

**Interfaces:**
- Produces: `Task<StorageInvoiceBillingDto> ExecuteAsync(Guid key)`; `record StorageInvoiceBillingDto(decimal TotalAmount, decimal BilledAmount, decimal Balance, List<StorageInvoiceBillingDocumentDto> Documents, List<StorageInvoiceBillingReceivableDto> Receivables)`; `record StorageInvoiceBillingDocumentDto(Guid Key, string? InvoiceNumber, TaxDocumentKind TaxDocumentKind, string? TaxDocumentNumber, string? TaxDocumentSeries, DateTime? InvoiceDate, decimal Total, InvoiceStatus? InvoiceStatus)`; `record StorageInvoiceBillingReceivableDto(Guid Key, string? Code, int? InstallmentNumber, DateTime DueDate, decimal NetAmount, decimal Balance, FinancialDocumentStatus Status)`. Resposta REST em camelCase.
- Produces: o pré-preenchimento do "Faturar": `StorageInvoiceBillingDto` + `CardCode`/`PaymentConditionCode` do PN vêm do GET do PN existente no frontend.

- [ ] **Step 1: Failing tests** — `Returns_documents_receivables_and_balance` (saldo = total − não cancelados); `Cancelled_receivables_listed_with_status`; `Unknown_key_throws_not_found`.
- [ ] **Step 2–4:** run `--filter "FullyQualifiedName~StorageInvoicesBillingGetTests"` FAIL → implementar → PASS; `dotnet build SiagroB1.sln`.
- [ ] **Step 5: Commit** `feat(storage): expor documentos e títulos da fatura de armazenagem`.

### Task 9: Tela da fatura — Faturar, seções e situações (frontend)

**Repo:** siagro-b1-frontend

**Files:**
- Modify: `webapp/view/storageInvoices/Detail.view.xml:31` — remover botão "Encerrar" (`onClose`); adicionar "Faturar" (visível se `Status` ∈ `Open`, `PartiallyInvoiced` e saldo > 0) ; seções "Documentos de saída" (Tipo NF-e/NFS, número, série, data, valor, situação) e "Títulos a receber" (vencimento, valor, saldo, situação), e cabeçalho com Faturado/Saldo a faturar.
- Modify: `webapp/controller/storageInvoices/Detail.controller.ts` — remover `onClose`/reabrir; `onBill()` navega para a rota de criação de documento de saída com parâmetros `{ storageInvoiceKey }`; carregar `GetBilling` num JSONModel `billing`.
- Modify: `webapp/controller/storageInvoices/Main.controller.ts`, `Main.view.xml`, `BaseController.ts` — formatter `statusText`: `Open`→"Aberta", `PartiallyInvoiced`→"Faturada parcial", `Invoiced`→"Faturada", `Paid`→"Paga", `Cancelled`→"Cancelada"; filtro de situação com as 5.
- Modify: `webapp/controller/salesInvoices/Add.controller.ts` — se receber `storageInvoiceKey`: preencher `CardCode`/`CardName` da fatura, `PaymentConditionCode` padrão do PN, `StorageInvoiceKey`, PN somente leitura; mostrar "Saldo a faturar"; usuário escolhe NF-e ou NFS, natureza, item e valor.
- Modify: `webapp/view/salesInvoices/Detail.view.xml` — link "Fatura de armazenagem {Code}".
- Modify: `webapp/i18n/i18n.properties`, `i18n_en.properties`.

- [ ] **Step 1:** `npm run ts-typecheck && npm run lint` — PASS antes.
- [ ] **Step 2:** Implementar.
- [ ] **Step 3:** `npm run ts-typecheck && npm run lint` — PASS. Verificar pela home (stack local): abrir fatura Aberta → Faturar → NFS de 400 de 1000 → confirmar → Detail mostra "Faturada parcial", documento e título; tentar 700 → mensagem de saldo; NF-e de 600 → "Faturada"; baixar títulos → "Paga"; fatura antiga encerrada aparece "Paga"; sem botão Encerrar.
- [ ] **Step 4: Commit** (frontend) `feat(storage): faturar a fatura de armazenagem por documentos de saída`.

---

## Self-review

- Cobertura: parte 1 → T1/T2 (corte, natureza, condição de pagamento, exclusões, desfazer); parte 2 → T3/T4 (sem transmissão/numeração, duplicidade PN+série+número, financeiro); parte 3 → T5–T9 (vínculo no cabeçalho, situações, legado Paga, teto incl. rascunho, trava, cancelar fatura, recálculo nos gatilhos, remover Encerrar, Detail, Faturar pré-preenchido). Retenção: só o parâmetro de valor líquido (YAGNI).
- Tipos: `SalesInvoiceNoContract`, `TaxDocumentKind.Nfse`, `StorageInvoiceStatus.{Paid,PartiallyInvoiced,Invoiced}`, `StorageInvoiceKey`, `RecalculateAsync`, `GetBalanceAsync` usados com o mesmo nome em todas as tasks.
- Migrations: `AddSalesInvoiceNoContractOrigin`, `AddSalesInvoiceStorageInvoiceKey` — aplicar em todas as empresas antes de subir o Web (o `PendingMigrationsGuard` recusa subir).
