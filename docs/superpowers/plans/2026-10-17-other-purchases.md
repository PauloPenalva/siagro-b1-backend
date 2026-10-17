# Outras compras no Documento de Entrada: plano de implementação

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** escriturar no Documento de Entrada compras sem contrato (serviço, uso e consumo, insumo, ativo).
Elas geram contas a pagar firme, parcelado pela condição de pagamento, que aceita baixa direta. As retenções
viram títulos de recolhimento.

**Architecture:** a natureza de entrada passa a mandar no contrato (`UsageEffect.RequiresContract`). Uma linha
cuja natureza não exige contrato e gera financeiro é uma "outra compra". As alíquotas de retenção ficam na
natureza (`USAGES`), como o Funrural, e a fotografia fica no cabeçalho do documento.
A confirmação chama um serviço novo, enqueue-only, que cria as parcelas e os títulos de imposto com a origem
nova `OtherPurchaseInvoice`. Essa origem fica fora das regras do firme de contrato, então os títulos aceitam
baixa direta.

**Tech Stack:** .NET 10, EF Core (SQL Server), OData v4, xUnit (EF InMemory + SQLite nos testes existentes),
OpenUI5 TypeScript.

**Spec:** `docs/superpowers/specs/2026-10-17-other-purchases-design.md`

## Global Constraints

- Só STANDALONE (`ErpMode.IsStandalone(configuration)`). Em SAPB1 nada muda: o guard, o serviço de títulos e o
  value help de produto se comportam como hoje.
- Identificadores em inglês e texto ao usuário em pt-BR ([[english-naming-standard]]).
- Commit: `tipo(escopo): descrição` com escopos `invoice`, `financial` ou `master-data`. Branch
  `feature/other-purchases` nos worktrees `siagro-b1-backend-other-purchases` e
  `siagro-b1-frontend-other-purchases`. Nunca push.
- Todo arquivo novo recebe `git add` na hora. Commit com migration leva o trailer `DB: <NomeDaMigration>`.
- O caminho do grão (conciliação, complemento de valor, provisório) não muda. Os testes existentes de
  `Financials/` e `PurchaseInvoices/` continuam verdes.
- Valores monetários com `decimal.Round(x, 2, MidpointRounding.ToEven)`, como no `FinancialDocumentsFirmSyncService`.

## Review Focus

1. **Natureza antiga de entrada com "Gera financeiro" ligado.** Depois da migration ela continua exigindo contrato
   e não gera títulos na confirmação. Teste na Task 1.
2. **Condição de pagamento com meio "Sem pagamento" (90).** `Calculate` devolve zero parcelas. Deve sair um título
   único vencendo na emissão, nunca nenhum título. Teste na Task 4.
3. **Retenções que igualam o total da NF (líquido zero).** A confirmação é recusada com a mensagem de negócio, e
   nenhum título é gravado. Teste na Task 4.
4. **Confirmar duas vezes, ou confirmar → estornar → confirmar.** Nunca duplica, e o segundo ciclo recria os
   títulos cancelados. Teste na Task 5.
5. **Produto fora do grupo 105 numa linha de outra compra.** A descrição é resolvida, e a mesma troca numa linha
   de grão continua recusada. Teste na Task 2.

---

### Task 1: a natureza de entrada exige ou não contrato

**Files:**
- Modify: `SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoiceLineGuard.cs`
- Modify: os 4 chamadores de `EnsureContractIsCompatibleAsync`: `PurchaseInvoicesCreateService`,
  `PurchaseInvoicesItemsCreateService`, `PurchaseInvoicesItemsUpdateService` e o `SyncItems` de
  `PurchaseInvoicesUpdateService`.
- Create: migration `BackfillIncomingUsageRequiresContract`, em `SiagroB1.Migrations/AppContext/`.
- Test: `SiagroB1.Application.Tests/PurchaseInvoices/PurchaseInvoiceUsageContractRequirementTests.cs`

**Interfaces:**
- Produces:
  - `PurchaseInvoiceLineGuard.EnsureUsageContractRuleAsync(IUnitOfWork db, IUsage usages, IConfiguration cfg, PurchaseInvoice invoice, IReadOnlyList<PurchaseInvoiceItem> lines): Task`
  - `PurchaseInvoiceLineGuard.IsOtherPurchase(UsageModel usage): bool`, que vale
    `!usage.RequiresContract && usage.CreatesFinancialDocument`.
  - `PurchaseInvoiceLineGuard.IsOtherPurchaseDocumentAsync(IUsage usages, IConfiguration cfg, PurchaseInvoice invoice, IReadOnlyList<PurchaseInvoiceItem> lines): Task<bool>`.
    É true quando STANDALONE, tipo `Normal`, não é devolução (`IsNfeReturn == false`) e TODA linha com natureza é
    outra compra.

- [ ] **Step 1: testes que falham** (STANDALONE, documento `Normal`, criação via `PurchaseInvoicesCreateService`, seed de `PurchaseInvoiceValueComplementTests`)
  - `Line_with_usage_requiring_contract_and_no_contract_is_refused`: mensagem
    `"A natureza {UsageName} exige contrato de compra na linha do produto {ItemCode}."`
  - `Line_with_usage_not_requiring_contract_refuses_contract`: mensagem
    `"A natureza {UsageName} não aceita contrato de compra: retire o contrato da linha do produto {ItemCode}."`
  - `Mixing_contract_and_other_purchase_lines_is_refused`: mensagem
    `"Um documento não mistura linhas de contrato com linhas de outras compras."`
  - `Sapb1_mode_keeps_contract_optional`: SAPB1, linha sem contrato e natureza sem efeito. Grava.
  - `Value_complement_still_requires_contract`: o tipo `ValueComplement` mantém a recusa atual.
  - Os mesmos dois primeiros casos via ItemsCreate e ItemsUpdate, um teste cada.
- [ ] **Step 2:** `dotnet test SiagroB1.Application.Tests --filter FullyQualifiedName~PurchaseInvoiceUsageContractRequirementTests` → FAIL.
- [ ] **Step 3: implementar e chamar nos 4 pontos.** O guard roda depois de `ResolveItemNameAsync` e antes de
  `EnsureContractIsCompatibleAsync`. Linha sem natureza passa sem regra, como hoje.
- [ ] **Step 4: escrever a migration** com um SQL só, sem mudar o esquema:
  `UPDATE e SET RequiresContract = 1 FROM USAGE_EFFECTS e JOIN USAGES u ON u.Code = e.UsageCode WHERE u.Direction = <Incoming> AND u.CreatesFinancialDocument = 1`.
  O `Down` não faz nada. Teste `Backfill_sets_requires_contract_on_existing_incoming_financial_usages` em SQLite,
  aplicando o mesmo SQL, se o projeto já tiver o padrão. Senão, documente o SQL no teste do guard.
- [ ] **Step 5:** filtro da Step 2 + `--filter FullyQualifiedName~PurchaseInvoice` → PASS.
- [ ] **Step 6: commit** `feat(invoice): natureza de entrada decide se a linha exige contrato`.

### Task 2: qualquer produto ativo na linha de outra compra

**Files:**
- Modify: `SiagroB1.Application/Services/ItemService.cs` (STANDALONE)
- Modify: a interface do serviço de itens e o controller de Items, para uma função nova.
- Modify: `PurchaseInvoiceLineGuard.ResolveItemNameAsync`
- Test: `SiagroB1.Application.Tests/PurchaseInvoices/PurchaseInvoiceOtherPurchaseItemTests.cs`

**Interfaces:**
- Produces:
  - `IItemService.QueryAllActive(): IQueryable<ItemModel>` (Enabled = "SIM", qualquer grupo) e
    `GetActiveByIdAsync(string code): Task<ItemModel?>`.
  - Função OData `GET /odata/ActiveItems()`, que devolve `QueryAllActive()` com `[EnableQuery]`.
  - Em SAPB1, os dois métodos delegam ao filtro atual.

- [ ] **Step 1: testes que falham**
  - `Other_purchase_line_accepts_item_outside_grain_group`: item do grupo 200 com natureza de outra compra.
    `ItemName` fica resolvido.
  - `Contract_line_keeps_grain_group_only`: a mesma linha com natureza que exige contrato. A descrição não é
    resolvida e o comportamento atual se mantém.
- [ ] **Step 2:** rodar → FAIL.
- [ ] **Step 3:** `ResolveItemNameAsync` recebe `bool otherPurchase`. Com `true`, usa `GetActiveByIdAsync`.
- [ ] **Step 4:** rodar → PASS. Também `--filter FullyQualifiedName~Item`.
- [ ] **Step 5: commit** `feat(invoice): linha de outras compras aceita qualquer produto ativo`.

### Task 3: retenções na natureza e no documento

**Files:**
- Modify: `SiagroB1.Domain/Entities/Usage.cs`, `SiagroB1.Domain/Models/UsageModel.cs` e os dois `UsageService`
  (o do SAP devolve nulo).
- Modify: `SiagroB1.Domain/Entities/PurchaseInvoice.cs`
- Create: `SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoiceWithholdings.cs`
- Create: entidade `WithholdingTaxSetting` + `DbSet` + migration `AddServiceWithholdings`, com as colunas de
  `USAGES`, de `PURCHASE_INVOICES` e a tabela `WITHHOLDING_TAX_SETTINGS`.
- Create: `SiagroB1.Domain/Enums/WithholdingTaxType.cs`
- Create: serviço e controller OData CRUD de `WithholdingTaxSettings`, no padrão de `PaymentConditions`.
- Create: migration `SeedWithholdingTaxCreditors`, só SQL e idempotente, no padrão de
  `20261012140331_MigrateWarehousesToBusinessPartners.cs`:
  - insere em `BUSINESS_PARTNERS` (`CardType = 'S'`) os códigos `TRIB-FEDERAL` "RECEITA FEDERAL DO BRASIL",
    `TRIB-ESTADUAL` "SECRETARIA DA FAZENDA ESTADUAL" e `TRIB-MUNICIPAL` "PREFEITURA MUNICIPAL", com
    `WHERE NOT EXISTS`;
  - insere em `WITHHOLDING_TAX_SETTINGS`: ISS → `TRIB-MUNICIPAL` dia 10; IRRF, INSS, PIS, COFINS e CSLL →
    `TRIB-FEDERAL` dia 20, com `WHERE NOT EXISTS` por `TaxType`;
  - o `Down` fica vazio, como na migration de referência, porque o usuário pode ter completado o parceiro.
- Modify: `PurchaseInvoicesCreateService` e `PurchaseInvoicesUpdateService`, que chamam o recálculo.
- Test: `SiagroB1.Application.Tests/PurchaseInvoices/PurchaseInvoiceWithholdingsTests.cs`

**Interfaces:**
- Produces:
  - `enum WithholdingTaxType { Iss = 0, Irrf = 1, Inss = 2, Pis = 3, Cofins = 4, Csll = 5 }`
  - Em `Usage` e `UsageModel`: `decimal? WithholdingIssRate, WithholdingIrrfRate, WithholdingInssRate, WithholdingPisRate, WithholdingCofinsRate, WithholdingCsllRate`
    (`DECIMAL(7,4)`, percentual: 5 = 5 %).
  - Em `PurchaseInvoice`: `decimal WithholdingBase` + `WithheldIss/Irrf/Inss/Pis/Cofins/Csll`
    (`DECIMAL(18,2) DEFAULT 0`), `bool WithholdingsManual`, `[NotMapped] decimal TotalWithheld`.
    `NetPayable` passa a valer `GrandTotal - TotalFunrural - TotalWithheld`.
  - `PurchaseInvoiceWithholdings.ApplyAsync(PurchaseInvoice invoice, IUsage usages, bool otherPurchase): Task`.
    - Sem outra compra: zera tudo.
    - Com `WithholdingsManual == false`: base = Σ `GrandTotal` e cada valor = round(base × alíquota / 100) da
      natureza da 1ª linha com natureza. As linhas de outra compra precisam ter as mesmas alíquotas, senão recusa
      `"As linhas têm naturezas com alíquotas de retenção diferentes."`.
    - Com `WithholdingsManual == true`: preserva os valores e só valida.
    - A validação recusa valor negativo e `TotalWithheld > GrandTotal`.
  - `WithholdingTaxSetting { WithholdingTaxType TaxType [Key]; string CardCode; string? CardName; int DueDay (1–31) }`,
    tabela `WITHHOLDING_TAX_SETTINGS`, função CRUD em `/odata/WithholdingTaxSettings`.

- [ ] **Step 1: testes que falham**
  - `Computes_withholdings_from_usage_rates`: total 1.000, ISS 5, IRRF 1,5. Resultado: 50,00 e 15,00, e
    `NetPayable` = 935,00.
  - `Manual_values_are_preserved_on_save`: manual = true e ISS 30 → continua 30.
  - `Withholdings_cleared_when_not_other_purchase`
  - `Withheld_above_total_is_refused`
  - `Lines_with_different_rates_are_refused`
  - `Withholding_setting_due_day_out_of_range_is_refused`: DueDay 0 e 32.
  - `Seed_creates_tax_creditors_and_settings_once`: aplica o SQL da migration duas vezes num banco SQLite ou
    InMemory relacional, se o projeto tiver esse suporte. Confere 3 parceiros e 6 configurações. Sem esse
    suporte, a conferência vai para a Task 8.
- [ ] **Step 2:** rodar → FAIL.
- [ ] **Step 3:** implementar. Gerar a migration com `dotnet ef migrations add AddServiceWithholdings` seguindo
  o padrão do backend `CLAUDE.md`, sem o profile `db-migration` ([[db-migration-profile-targets-production]]).
  Conferir o SQL gerado.
- [ ] **Step 4:** rodar → PASS, mais `--filter FullyQualifiedName~Usage`.
- [ ] **Step 5: commit** `feat(invoice): retenções de serviço na natureza e no documento de entrada`.

### Task 4: títulos na confirmação

**Files:**
- Modify: `SiagroB1.Domain/Enums/FinancialDocumentOrigin.cs`, com `OtherPurchaseInvoice = 9`.
- Modify: `SiagroB1.Infra/Context/AppDbContext.cs`. O filtro de `IX_FINANCIAL_DOCUMENTS_FirmOrigin` ganha
  `AND [OriginType] <> 9`, porque são N parcelas com o mesmo `OriginKey`. Migration
  `OtherPurchaseFirmOriginIndex`.
- Modify: `SiagroB1.Domain/Entities/FinancialDocument.cs`, com `int? InstallmentNumber` e
  `WithholdingTaxType? WithholdingTaxType`. Vão na mesma migration.
- Create: `SiagroB1.Application/Services/Financials/FinancialDocumentsOtherPurchaseService.cs` e registro no DI,
  junto do `FinancialDocumentsFirmSyncService`.
- Modify: `PurchaseInvoicesConfirmService`, que chama o serviço depois de `EnqueueForValueComplementAsync`.
- Test: `SiagroB1.Application.Tests/Financials/FinancialDocumentsOtherPurchaseTests.cs`

**Interfaces:**
- Consumes: `IsOtherPurchaseDocumentAsync` (Task 1), `NetPayable`/`Withheld*` (Task 3),
  `PaymentInstallmentCalculator.Calculate(days, startRule, paymentMeans, total, issueDate)`.
- Produces: `FinancialDocumentsOtherPurchaseService.EnqueueAsync(PurchaseInvoice invoice, IReadOnlyList<PurchaseInvoiceItem> lines, string userName): Task`,
  que é enqueue-only, fora de transação e gera numeração por `DocNumberSequenceService`, como o firme.

Regras:
- **Parcelas:** total das parcelas = `NetPayable` (`GrandTotal - TotalFunrural - TotalWithheld`), dividido pela
  condição de pagamento do documento.
  - Sem condição, ou com zero parcelas, sai 1 título vencendo em `IssueDate`.
  - Com `NetPayable <= 0`, recusa
    `"O total das retenções ({valor}) consome o valor da nota: revise os valores retidos."`.
  - Campos: `Nature = Firm`, `Direction = Payable`, `OriginType = OtherPurchaseInvoice`,
    `OriginKey = invoice.Key`, `PurchaseInvoiceKey = invoice.Key`, `InstallmentNumber = n`,
    `TaxDocumentNumber` como no firme, `Currency = Brl`, `DocumentDate = IssueDate`, `DueDate` da parcela.
- **Recolhimento:** um título por imposto com valor > 0.
  - `Nature = TaxWithholding`, mesma origem e chave, `WithholdingTaxType` preenchido.
  - `CardCode`/`CardName` vêm de `WithholdingTaxSetting`.
  - `DueDate` = dia `DueDay` do mês seguinte a `IssueDate`, limitado ao último dia do mês.
  - Sem configuração, recusa `"Configure o credor da retenção de {ISS} em Configuração de Retenções."`.
- Idempotente: se houver título ativo com `OriginType = OtherPurchaseInvoice` e `OriginKey = invoice.Key`,
  não cria nada.
- Chamar `FinancialDocumentsRecalculateBalanceService.RecalculateAsync` para cada título, como no firme.

- [ ] **Step 1: testes que falham**
  - `Confirm_creates_installments_from_payment_condition`: 1.000, condição "30,60", ISS 50 e IRRF 15.
    Líquido 935,00, com parcelas 467,50 (emissão + 30) e 467,50 (emissão + 60).
  - `Installment_rounding_closes_on_net`: líquido 1.000,00 em "30,60,90" → 333,33 + 333,33 + 333,34.
  - `Confirm_creates_one_withholding_document_per_tax`: ISS 50,00 para o credor ISS e IRRF 15,00 para o credor
    IRRF, com vencimento no dia `DueDay` do mês seguinte.
  - `Due_day_beyond_month_end_is_clamped`: DueDay 31 e emissão 15/01 → 28/02.
  - `No_payment_condition_creates_single_title_due_on_issue`
  - `No_payment_means_condition_creates_single_title_due_on_issue`, para o meio 90 (Review Focus 2).
  - `Withholdings_consuming_whole_invoice_are_refused_and_nothing_is_saved`: o documento continua `Pending`
    (Review Focus 3).
  - `Missing_withholding_setting_is_refused`
  - `Other_purchase_title_accepts_direct_settlement`: `FinancialDocumentsSettlementGuardService.EnsureCanSettle`
    não lança, e `IsBlockedForSettlement == false`.
  - `Contract_documents_create_no_other_purchase_titles`: complemento de valor e documento de grão seguem como
    hoje.
  - `Sapb1_mode_creates_nothing`
- [ ] **Step 2:** rodar → FAIL.
- [ ] **Step 3:** implementar. Conferir `IsContractFirmDocument`/`IsPurchaseFirmPayable`, que NÃO podem incluir a
  origem 9.
- [ ] **Step 4:** rodar → PASS, mais `--filter FullyQualifiedName~Financial`.
- [ ] **Step 5: commit** `feat(financial): títulos a pagar e de retenção das outras compras na confirmação`.

### Task 5: estorno e cancelamento desfazem os títulos

**Files:**
- Modify: `PurchaseInvoicesReverseConfirmService.cs` e `PurchaseInvoicesCancelService.cs`. No Cancel, entra no
  ensaio `EnsureCanCancelAsync` e na execução.
- Modify: `FinancialDocumentsOtherPurchaseService`, com `EnqueueUndo(Guid invoiceKey, string userName)` e
  `EnsureCanUndoAsync(Guid invoiceKey)`.
- Test: `SiagroB1.Application.Tests/Financials/FinancialDocumentsOtherPurchaseUndoTests.cs`

**Interfaces:**
- Produces:
  - `EnsureCanUndoAsync(Guid invoiceKey): Task`. Recusa se algum título ativo da origem tiver
    `SettledAmount > 0`, com a mensagem
    `"O título {Code} já tem baixa: estorne a baixa no Financeiro antes de desfazer o documento."`.
  - `EnqueueUndo` grava `Status = Canceled` nos títulos ativos da origem, com change log, como faz
    `PurchaseFirmPayableRules.CancelLooseFirms`.

- [ ] **Step 1: testes que falham**
  - `Reverse_confirm_cancels_open_titles`
  - `Reverse_confirm_with_settled_title_is_refused`: o documento continua `Confirmed`.
  - `Cancel_cancels_open_titles`
  - `Confirm_reverse_confirm_recreates_titles`: o novo ciclo cria um conjunto novo e o índice não colide
    (Review Focus 4).
  - `Double_confirm_does_not_duplicate`
- [ ] **Step 2:** rodar → FAIL.
- [ ] **Step 3:** implementar.
- [ ] **Step 4:** rodar a suíte inteira, `dotnet test SiagroB1.Application.Tests` → PASS, sem regressão.
- [ ] **Step 5: commit** `feat(financial): estorno do documento de entrada cancela os títulos das outras compras`.

### Task 6: frontend, natureza e configuração de retenções

**Files (frontend worktree):**
- Modify: `webapp/view/usages/fragments/Form.fragment.xml`:
  - "Exige contrato ?" também para `Incoming`;
  - grupo "Retenções de serviço" com os 6 campos, visível para `Incoming`, com tipo `Double`
    ([[odata-rejects-decimal-as-string]]).
- Modify: `webapp/controller/usages/Add.controller.ts`, com os defaults.
- Create: `webapp/controller/withholdingTaxSettings/Main.controller.ts` + `webapp/view/withholdingTaxSettings/Main.view.xml`.
  É uma lista editável com Imposto, Credor (value help de parceiro) e Dia de vencimento, mais a rota em
  `manifest.json`.
- Backend: migration de menu `AddWithholdingTaxSettingsMenu` sob o grupo do Financeiro
  ([[new-screen-needs-menu-migration]]), com o título "Configuração de Retenções".

- [ ] **Step 1:** implementar.
- [ ] **Step 2:** `yarn ts-typecheck` e `yarn lint` → sem erros.
- [ ] **Step 3: commits**, um por repo:
  - `feat(master-data): retenções e exigência de contrato na natureza de entrada`;
  - `feat(financial): tela de configuração de retenções`.

### Task 7: frontend, Documento de Entrada

**Files:**
- Modify: `webapp/controller/purchaseInvoices/BaseController.ts`:
  - o value help de produto usa `/ActiveItems()` quando a natureza da linha não exige contrato;
  - a coluna/Input de contrato fica desabilitada nessas linhas e é limpa ao escolher uma natureza de outra compra
    (padrão `openLineItemValueHelp`, ver [[purchase-invoice-contract-reference-feature]], armadilha 1).
- Modify: `webapp/view/purchaseInvoices/fragments/Form.fragment.xml`, com a seção "Retenções":
  - Base e os 6 valores editáveis;
  - editar liga `WithholdingsManual`;
  - o botão "Recalcular pelas alíquotas" desliga a flag e salva;
  - Total retido e Líquido a pagar somente leitura.
- Modify: `webapp/view/purchaseInvoices/Detail.view.xml` + controller, com a seção "Títulos gerados" ligada a
  `/FinancialDocuments` com `$filter=PurchaseInvoiceKey eq {key} and OriginType eq 'OtherPurchaseInvoice'`.
  O filtro de enum é estático ([[odata-enum-filter-needs-static-filter]]), e a seção tem link para o detalhe do
  título.

- [ ] **Step 1:** implementar.
- [ ] **Step 2:** `yarn ts-typecheck` e `yarn lint` → sem erros.
- [ ] **Step 3: commit** `feat(invoice): outras compras e retenções no documento de entrada`.

### Task 8: verificação pelo caminho do usuário

- [ ] Subir a stack em STANDALONE ([[run-stack-locally]], profile `ceagui`; frontend `yarn start:dev`) e aplicar
  as migrations no banco dev.
- [ ] Roteiro da spec, a partir da home:
  1. Cadastrar a natureza "Serviço tomado" (sem contrato, gera financeiro, ISS 5, IRRF 1,5). Conferir na tela de
     configuração os credores semeados `TRIB-FEDERAL` e `TRIB-MUNICIPAL`.
  2. Lançar uma NFS-e (`TaxDocumentKind = Other`) de R$ 1.000 com produto fora do grupo 105 e condição 30/60.
     Ajustar o ISS à mão e conferir que a flag manual persiste.
  3. Confirmar e conferir no Financeiro as 2 parcelas e os 2 títulos de recolhimento com os valores da Task 4.
  4. Baixar a 1ª parcela. Estornar a confirmação deve ser recusado.
  5. Estornar a baixa e então estornar a confirmação. Os títulos ficam cancelados.
  6. Regressão: lançar uma linha de grão com natureza antiga e conferir que exige contrato.
- [ ] Derrubar a stack pelo PID ([[subagent-must-kill-by-pid]]).
