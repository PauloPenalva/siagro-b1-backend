# Complemento de ICMS e complemento de valor sem contrato — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Documento de Entrada passa a aceitar Complemento de ICMS (próprio, com NF-e finNFe=2, e de terceiro), e o Complemento de valor passa a seguir a natureza (contrato ou outras compras).

**Architecture:** Um valor novo em `PurchaseInvoiceType`; a regra única de linha `PurchaseInvoiceValueComplement` vira `PurchaseInvoiceComplement`, parametrizada pelo tipo; o motor de tributos ganha um aplicador para o ICMS informado; emissão, financeiro, conciliação e importação passam a reconhecer o tipo novo; frontend ajusta Select, grade e helpers.

**Tech Stack:** .NET 10 / EF Core / xUnit (InMemory) / OpenUI5 + TypeScript / QUnit.

**Spec:** `docs/superpowers/specs/2026-10-17-purchase-invoice-complements-design.md` (backend repo). Ler antes de cada tarefa.

**Worktrees:** backend `C:\Projetos\SiagroB1\siagro-b1-backend-complements`, frontend `C:\Projetos\SiagroB1\siagro-b1-frontend-complements`, branch `feature/purchase-invoice-complements` nos dois. Nunca trabalhar nos checkouts principais.

## Global Constraints

- Identificadores em inglês; texto que o usuário lê em pt-BR (mensagens exatas do spec, tabela K1–K6).
- Rótulos dos tipos: `Normal`, `Devolução`, `Complemento de valor`, `Complemento de ICMS`.
- `IcmsComplement = 3`. Sem migration.
- ICMS só em STANDALONE (K3). Em SAPB1 o complemento de valor de terceiro continua como hoje (contrato obrigatório).
- Alíquota derivada do complemento de ICMS: `Round(IcmsValue / IcmsBase * 100, 4)` (coluna DECIMAL(7,4)).
- Commits: `tipo(escopo): descrição` em pt-BR, escopo `invoice` (`financial` na Task 4), com os trailers `Co-Authored-By`/`Claude-Session` da sessão. Nunca push. Arquivo novo → `git add` na hora.
- Backend: `dotnet test SiagroB1.Application.Tests` e `dotnet test SiagroB1.Fiscal.Tests` verdes ao fim de cada tarefa (anotar o baseline antes de começar). O worktree já tem `SiagroB1.Reports/wwwroot` copiado.
- Frontend: `yarn ts-typecheck`, `yarn lint` e a suíte QUnit verdes (o gate de cobertura de `yarn test` nunca passa — não é critério). Parsear os XML de view alterados. Nada de `--` em comentário XML; enum em expression binding com `targetType:'any'`.

## Review Focus

1. **Troca de tipo com linhas já gravadas** (Normal → ICMS no Edit): recusa clara pela regra do novo tipo (K1), nunca 500 nem linha inconsistente. Teste na Task 1.
2. **Complemento de ICMS com natureza que não gera financeiro, ou de outra compra** é aceito e não gera título nem parcela. Teste na Task 4.
3. **XML complementar de ICMS de terceiro** carrega base/valor e sobrevive ao recálculo do motor no save. Testes na Task 2 (aplicador preserva) e na Task 5 (import → create).
4. **Cancelar a original com complemento de ICMS ativo** → C8, igual ao valor. Teste na Task 3.
5. **ICMS próprio emite sem condição de pagamento e sem pesos.** Teste na Task 3.

---

### Task 1: Tipo novo e regra única das linhas de complemento

**Files:**
- Modify: `SiagroB1.Domain/Enums/PurchaseInvoiceType.cs`
- Create: `SiagroB1.Domain/Enums/PurchaseInvoiceComplements.cs`
- Rename+Modify: `SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoiceValueComplement.cs` → `PurchaseInvoiceComplement.cs` (classe `PurchaseInvoiceComplement`); atualizar todos os chamadores (`grep -rn PurchaseInvoiceValueComplement`)
- Modify: `SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoiceLineGuard.cs` (`EnsureUnitOfMeasure`, `EnsureUsageContractRuleAsync`)
- Modify: `SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoicesTaxApplyService.cs` (`EnsureValueComplementAsync` → `EnsureComplementAsync`; chamadores em Create/Update/ItemsCreate/ItemsUpdate)
- Modify: `SiagroB1.Reports/Services/PurchaseInvoicesByPeriodReportService.cs` (rótulo)
- Test: `SiagroB1.Application.Tests/PurchaseInvoices/PurchaseInvoiceIcmsComplementTests.cs` (novo); os existentes `PurchaseInvoiceValueComplementTests`, `PurchaseInvoiceOwnValueComplementTests` e `PurchaseInvoiceUsageContractRequirementTests` continuam verdes (ajustar só o que D3 e C7 mudam — ver o último item do Step 1)

**Interfaces:**
- Produces:
  - `PurchaseInvoiceType.IcmsComplement = 3`
  - `public static class PurchaseInvoiceComplements { bool IsComplement(PurchaseInvoiceType t); string Label(PurchaseInvoiceType t); }` — `Label` devolve o texto em minúscula usado dentro das mensagens: "complemento de valor" / "complemento de ICMS".
  - `PurchaseInvoiceComplement`: `EnsureAsync(PurchaseInvoice, IReadOnlyList<PurchaseInvoiceItem>, IUsage, TaxCalculationGate, AppDbContext)` (mesma assinatura de hoje), `IsComplementableOrigin`, `EnsureIssuableAsync`, `EnsureNoActiveComplementAsync`, `LoadOriginalsAsync`, `MissingOriginalMessage(string? itemCode, PurchaseInvoiceType type)`, `NoLongerAuthorizedMessage(string?)`.
  - `PurchaseInvoicesTaxApplyService.EnsureComplementAsync(PurchaseInvoice, IReadOnlyList<PurchaseInvoiceItem>)`.

- [ ] **Step 1: Write the failing tests** em `PurchaseInvoiceIcmsComplementTests` (fixture igual a `PurchaseInvoiceValueComplementTests.SeedAsync/Document`, gravando por `PurchaseInvoicesCreateService`):
  - `Icms_complement_with_base_and_value_is_saved` — tipo 3, terceiro, Qty 0, Price 0, ComplementAmount 0, IcmsBase 1000, IcmsValue 120, natureza que NÃO gera financeiro, sem contrato (natureza não exige) → grava.
  - `Icms_complement_refuses_quantity_price_or_amount` (Theory: qty 1 / price 1 / amount 1) → K1 exato, `{item}` = código do produto.
  - `Icms_complement_refuses_missing_base_or_value` (base 0 / value 0) → K2.
  - `Icms_complement_refuses_value_above_base` (base 100, value 101) → K4.
  - `Icms_complement_is_refused_in_sapb1` → K3.
  - `Icms_complement_with_contract_usage_requires_contract` → mensagem existente "A natureza {nome} exige contrato de compra na linha do produto {item}.".
  - `Value_complement_with_other_purchase_usage_and_no_contract_is_saved` (D3) — natureza `RequiresContract=false, CreatesFinancialDocument=true`, sem contrato → grava.
  - `Value_complement_with_contract_usage_and_no_contract_is_refused` → a mesma mensagem da natureza.
  - `Value_complement_in_sapb1_still_requires_contract` → mensagem antiga "Item {item}: o complemento de valor exige o contrato de compra na linha.".
  - `Changing_type_to_icms_with_priced_lines_is_refused` (Review Focus 1) — cria Normal com qty/price, depois `PurchaseInvoicesUpdateService` troca `InvoiceType` para 3 → K1.
  - `Own_icms_complement_requires_original_item` (emissor Own, gate ativo, como `PurchaseInvoiceOwnValueComplementTests`) → `"Item {item}: o complemento de ICMS de emissão própria exige o item da nota original."`.
  - `Own_icms_complement_inherits_from_original` — produto/UM/natureza/contrato copiados do original (como no valor).
  - `Complemented_item_outside_own_complement_is_refused` → `"O item da nota original só existe no complemento de emissão própria."` (C7 generalizada — atualizar o assert do teste existente que usa o texto antigo).
  - D3 muda a regra antiga "o complemento de valor exige o contrato de compra na linha" em STANDALONE: os testes existentes que esperam essa recusa em STANDALONE passam a usar natureza com `RequiresContract=true` e a esperar a mensagem da natureza. Não remover cobertura.

- [ ] **Step 2: Run** `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~PurchaseInvoiceIcmsComplementTests"` → FAIL (tipo inexistente).

- [ ] **Step 3: Implement.**
  - Enum + helper.
  - `PurchaseInvoiceComplement.EnsureAsync`: o ramo "não é complemento" recusa `ComplementAmount != 0` como hoje (mensagem atual mantida). Para os dois tipos: K3 primeiro (ICMS e `!gate.IsStandalone`); próprio (`Own`) → gate ativo (`OutsideNfeModeMessage` com o rótulo do tipo) e `EnsureOwnLinesAsync` (C1–C5, C10, com o rótulo do tipo onde hoje está escrito "complemento de valor"); por linha a tabela do spec (Valor: qty/price 0 e amount > 0, mensagens atuais; ICMS: K1, K2, K4); natureza obrigatória nos dois (mensagem atual com o rótulo); "gera financeiro" exigido só para Valor em STANDALONE; contrato: em SAPB1 o Valor continua exigindo (mensagem atual); em STANDALONE a regra sai daqui e vale D3 via `EnsureUsageContractRuleAsync`.
  - `EnsureUnitOfMeasure`: pula os dois complementos.
  - `EnsureUsageContractRuleAsync`: aplica a Normal e aos dois complementos (o guard `InvoiceType != Normal` vira `!(Normal || IsComplement)`); o atalho "linhas com ComplementAmount retornam" vale só para tipo Normal.
  - Rótulo no relatório.

- [ ] **Step 4: Run** a suíte inteira `dotnet test SiagroB1.Application.Tests` → PASS.

- [ ] **Step 5: Commit** `feat(invoice): adiciona o complemento de ICMS à regra das linhas`.

---

### Task 2: Tributos do complemento de ICMS

**Files:**
- Create: `SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoiceIcmsComplementTaxes.cs`
- Modify: `SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoicesTaxApplyService.cs` (`IsActiveForAsync`, `ApplyAsync`, DIFAL)
- Test: `SiagroB1.Application.Tests/PurchaseInvoices/PurchaseInvoiceIcmsComplementTaxesTests.cs`

**Interfaces:**
- Consumes: Task 1.
- Produces: `internal static class PurchaseInvoiceIcmsComplementTaxes { static void Apply(PurchaseInvoiceItem line, string cst, decimal typedBase, decimal typedValue); static string CstFor(string? originalCst); }`
  - `CstFor`: original em {"00","20","90"} → ele; original de 3 dígitos (CSOSN) → "900"; senão (ou null, terceiro) → "90".
  - `Apply`: `IcmsBase = typedBase`, `IcmsValue = typedValue`, `IcmsRate = Round(typedValue / typedBase * 100, 4)` (0 se base 0), `CstIcms = cst`, zera redução/diferimento/operação/diferido e PIS/COFINS/IBS/CBS (bases, alíquotas e valores), Funrural 0.

- [ ] **Step 1: Failing tests:**
  - `Icms_complement_keeps_typed_base_and_value_after_recalculation` (Review Focus 3) — terceiro, filial com gate ativo, natureza com CFOP 1102 e ICMS 18%: grava IcmsBase 1000 / IcmsValue 120 → depois do Create: IcmsBase 1000, IcmsValue 120, IcmsRate 12.0000, CstIcms "90", Cfop "1102", PIS/COFINS/IBS/CBS 0.
  - `Own_icms_complement_uses_original_cst_and_cfop` — original CST "00", CFOP "1102" → linha "00"/"1102"; original "51" → "90".
  - `Icms_complement_rate_is_rounded_to_four_places` — base 333,33 e valor 40 → 12.0001.
  - `Icms_complement_has_no_difal` — fornecedor de outra UF e natureza de uso e consumo (fixture de `PurchaseInvoiceDifalTests`) → DIFAL/FCP 0.
  - `Icms_complement_typed_values_are_kept_off_pending` — documento Confirmado: PATCH de linha com outros valores não altera (trava de regressão do `TaxSnapshot.RestoreLocked`).

- [ ] **Step 2: Run** o filtro `PurchaseInvoiceIcmsComplementTaxesTests` → FAIL.

- [ ] **Step 3: Implement.** `IsActiveForAsync`: incluir `IcmsComplement` nos dois emissores. Em `ApplyAsync`, para `IcmsComplement`: guardar `IcmsBase/IcmsValue` da linha, calcular normalmente (amount = GrandTotal = 0: resolve o CFOP e zera o resto), depois `PurchaseInvoiceIcmsComplementTaxes.Apply` com `CstFor` (próprio: CST e CFOP do item original via `PurchaseInvoiceComplement.LoadOriginalsAsync`; o CFOP do original sobrepõe o resolvido). Se o gate estiver inativo (filial STANDALONE sem NF-e), o aplicador roda mesmo assim, para a alíquota e o CST existirem. DIFAL: pular `IcmsComplement`.

- [ ] **Step 4: Run** `dotnet test SiagroB1.Application.Tests` → PASS.

- [ ] **Step 5: Commit** `feat(invoice): grava o ICMS informado no complemento de ICMS`.

---

### Task 3: Emissão da NF-e complementar de ICMS

**Files:**
- Modify: `SiagroB1.Application/Services/Nfe/PurchaseInvoicesNfeIssueService.cs` (`EnsureIssuableType`, `EnsureBeforeReservationAsync`)
- Modify: `SiagroB1.Application/Services/Nfe/NfeIssueInputAssembler.cs` (purpose, `WithoutPayment`)
- Modify: `SiagroB1.Application/Services/Nfe/NfeReadinessValidator.cs` (refs, pesos, base/valor, condição de pagamento)
- Modify: `SiagroB1.Application/Services/Nfe/PurchaseInvoicesNfeReturnCreateService.cs` (K6)
- Test: `SiagroB1.Application.Tests/PurchaseInvoices/PurchaseInvoiceOwnIcmsComplementIssueTests.cs`; no `SiagroB1.Fiscal.Tests/Nfe`, o arquivo de testes do builder que já cobre `NfePurpose.Complement` (acrescentar lá)

**Interfaces:** Consumes Tasks 1–2. Nenhuma nova.

- [ ] **Step 1: Failing tests:**
  - Fiscal: `Icms_complement_item_has_zero_product_and_icms_values` — item Quantity 0, UnitPrice 0, Total 0, CST "90", IcmsBase 1000, IcmsRate 12, IcmsValue 120, `NfePurpose.Complement`, `WithoutPayment: true` → XML: finNFe 2, qCom 0, vProd 0.00, vBC 1000.00, vICMS 120.00, ICMSTot vNF 0.00, `pag/detPag/tPag` = 90.
  - App: `Own_icms_complement_is_issuable_without_payment_condition_or_weights` (Review Focus 5) — readiness sem problemas com condição de pagamento vazia e pesos 0; input com `WithoutPayment = true` e `ReferencedKeys` = chave da original.
  - App: `Own_icms_complement_readiness_refuses_line_without_icms` — linha com base 0 → K2 (recusa direta, como C6).
  - App: `Own_icms_complement_refuses_original_no_longer_authorized` → C6.
  - App: `Cancelling_original_with_active_icms_complement_is_refused` (Review Focus 4) → C8 exato.
  - App: `Icms_complement_has_no_return` → K6 ("Complemento de valor não tem devolução." continua igual).

- [ ] **Step 2: Run** os dois filtros → FAIL.

- [ ] **Step 3: Implement.** `EnsureIssuableType` aceita `IsComplement`; `EnsureBeforeReservationAsync` chama `PurchaseInvoiceComplement.EnsureIssuableAsync` para os dois. Assembler: `complement = returnOrigin is null && IsComplement(type)`; `WithoutPayment` também quando `IcmsComplement`. Readiness: `LoadComplementReferencesAsync` e `RequiresWeights = false` para os dois; para ICMS, toda linha com `IcmsBase > 0 && IcmsValue > 0`, senão `DefaultException` K2; se o validador exigir condição de pagamento, pular para ICMS.

- [ ] **Step 4: Run** as duas suítes inteiras → PASS.

- [ ] **Step 5: Commit** `feat(invoice): emite NF-e complementar de ICMS`.

---

### Task 4: Financeiro e conciliação dos complementos

**Files:**
- Modify: `SiagroB1.Application/Services/Financials/FinancialDocumentsFirmSyncService.cs` (`EnqueueForValueComplementAsync`)
- Modify: `SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoiceLineGuard.cs` (`IsOtherPurchaseDocumentAsync`)
- Modify: `SiagroB1.Infra/Queries/PurchaseFiscalReconciliationQuery.cs:225`
- Modify: `SiagroB1.Application/Services/PurchaseFiscalReconciliations/PurchaseFiscalReconciliationService.cs:247`
- Modify: `SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoiceDeclaredTotal.cs`, `SupplierNfeKeyGuard.cs` (incluir o ICMS de terceiro)
- Test: `SiagroB1.Application.Tests/PurchaseInvoices/PurchaseInvoiceComplementFinancialTests.cs`

**Interfaces:** Consumes Task 1. Nenhuma nova.

- [ ] **Step 1: Failing tests:**
  - `Value_complement_of_other_purchase_creates_installments_on_confirm` — natureza de outra compra, sem contrato, com condição de pagamento → títulos `OtherPurchaseInvoice`, nenhum firme de origem `PurchaseInvoice`.
  - `Value_complement_with_contract_still_creates_firm_on_confirm` (regressão).
  - `Icms_complement_creates_no_title` (Theory: natureza sem financeiro; natureza de outra compra; natureza com contrato) (Review Focus 2) → nenhum `FinancialDocument` depois de confirmar.
  - `Icms_complement_line_is_not_offered_for_reconciliation` → a query não lista a linha.
  - `Icms_complement_line_cannot_be_linked` → K5.
  - `Third_party_icms_complement_requires_supplier_key_and_declares_zero` → `SupplierNfeKeyGuard.AppliesTo` e `PurchaseInvoiceDeclaredTotal.AppliesTo` true; total declarado 0.

- [ ] **Step 2: Run** os filtros → FAIL.

- [ ] **Step 3: Implement.** Firm sync: só `ValueComplement` e só linhas com `PurchaseContractKey` (as demais seguem outras compras). `IsOtherPurchaseDocumentAsync`: tipo Normal ou ValueComplement. Conciliação: a query troca `!= ValueComplement` por "não é complemento" (`IsComplement` não traduz em LINQ → comparar os dois valores); `EnsurePair` recusa ICMS com K5 (a mensagem do valor continua igual).

- [ ] **Step 4: Run** `dotnet test SiagroB1.Application.Tests` → PASS.

- [ ] **Step 5: Commit** `feat(financial): leva o complemento de valor sem contrato a outras compras`.

---

### Task 5: Importação do XML complementar de ICMS de terceiro

**Files:**
- Modify: `SiagroB1.Domain/Dtos/PurchaseInvoiceDraftDto.cs` (`PurchaseInvoiceDraftItemDto` + `IcmsBase`, `IcmsValue`, ambos `decimal`, no mesmo estilo de serialização do DTO)
- Modify: `SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoicesImportXmlService.cs`
- Create: `SiagroB1.Application/Services/PurchaseInvoices/SupplierNfeComplementKind.cs`
- Test: `SiagroB1.Application.Tests/PurchaseInvoices/PurchaseInvoicesImportXmlServiceTests.cs` (acrescentar)

**Interfaces:**
- Produces: `public static class SupplierNfeComplementKind { static PurchaseInvoiceType Classify(SupplierNfe nfe); }` — Normal se `!IsComplement`; ICMS se todo item tem `ProductValue == 0 && IcmsValue > 0`; senão Valor.

- [ ] **Step 1: Failing tests** (XML montado como nos testes existentes do import):
  - `Icms_complement_xml_imports_base_and_value` → InvoiceType IcmsComplement, Quantity/UnitPrice/ComplementAmount 0, IcmsBase/IcmsValue do XML.
  - `Value_complement_xml_still_imports_as_value` (regressão).
  - `Imported_icms_complement_survives_create` (Review Focus 3) — draft do import → `PurchaseInvoicesCreateService` → a linha mantém base e valor.

- [ ] **Step 2: Run** → FAIL. **Step 3: Implement.** **Step 4: Run** a suíte inteira → PASS.

- [ ] **Step 5: Commit** `feat(invoice): importa NF-e complementar de ICMS do fornecedor`.

---

### Task 6: Frontend — helpers e formatter

**Files (frontend worktree, `webapp/`):**
- Modify: `helpers/PurchaseInvoiceNfeHelpers.ts` (`canIssuePurchaseNfe`, `isPurchaseTaxMode`, `isOwnComplement`; novo `isComplementType`; `PURCHASE_ITEM_SELECT` com `IcmsBase`/`IcmsValue` se ainda não tiver)
- Modify: `helpers/PurchaseInvoiceDraftHelpers.ts` (`missingUnitOfMeasureMessage`, `blankItemRow`, `complementLineFrom(candidate, invoiceType)`, `draftItemRows` levando `IcmsBase`/`IcmsValue` do draft)
- Modify: `model/formatter.ts` (`formatPurchaseInvoiceType`)
- Test: `test/unit/helpers/PurchaseInvoiceNfeHelpers.qunit.ts`, `test/unit/helpers/PurchaseInvoiceDraftHelpers.qunit.ts`, `test/unit/model/formatter.qunit.ts`

**Interfaces:**
- Produces: `isComplementType(invoiceType: string): boolean` (ValueComplement | IcmsComplement); `isOwnComplement(nfeMode, invoiceType)` = `nfeMode && isComplementType(invoiceType)`; `complementLineFrom(candidate, invoiceType)` — ICMS: `IcmsBase: 0, IcmsValue: 0, ComplementAmount: 0`; Valor: como hoje. `blankItemRow` ganha `IcmsBase: 0, IcmsValue: 0`.

- [ ] **Step 1: Failing QUnit:** `formatPurchaseInvoiceType("IcmsComplement") === "Complemento de ICMS"`; `canIssuePurchaseNfe` true para `IcmsComplement` (Own, Pending, None); `isPurchaseTaxMode` true para `IcmsComplement` nos dois emissores; `isOwnComplement(true, "IcmsComplement")` true; `missingUnitOfMeasureMessage(…, "IcmsComplement")` = ""; `complementLineFrom` por tipo; `draftItemRows` com IcmsBase/IcmsValue.
- [ ] **Step 2: Run** a suíte QUnit (ignorar o gate de cobertura) → FAIL.
- [ ] **Step 3: Implement.** **Step 4: Run** → PASS + `yarn ts-typecheck` + `yarn lint`.
- [ ] **Step 5: Commit** `feat(invoice): helpers do complemento de ICMS`.

---

### Task 7: Frontend — Select, grade, lista e importação

**Files (`webapp/`):**
- Modify: `view/purchaseInvoices/fragments/Form.fragment.xml:67-77` (item `IcmsComplement` "Complemento de ICMS", escondido fora de STANDALONE — usar o indicador de modo ERP que a tela já tem; se não houver, criar `ui>/standalone` a partir da mesma fonte que outras telas usam)
- Modify: `view/purchaseInvoices/fragments/Items.fragment.xml` (Qtd/Preço escondidos nos dois complementos; Valor do complemento só no Valor; colunas novas **Base ICMS** (`IcmsBase`) e **ICMS** (`IcmsValue`), `sap.ui.model.odata.type.Double` com 2 casas, visíveis e editáveis só em IcmsComplement; Natureza visível nos dois complementos; o rótulo "Contrato (obrigatório)" segue a natureza)
- Modify: `view/purchaseInvoices/fragments/Filterbar.fragment.xml:27-32`, `controller/purchaseInvoices/Main.controller.ts:236-245` (tipo novo no filtro e na exportação)
- Modify: `controller/purchaseInvoices/BaseController.ts` (`refreshNfeMode`: `ui>/icmsComplement`; `ui>/ownComplement` via `isOwnComplement`; `clearHiddenAmountColumns` por tipo — ICMS zera Qty/Price/Amount, Valor zera Qty/Price, os demais zeram Amount, IcmsBase/IcmsValue informados e `ComplementedItemKey`; `addOwnComplementLine` passa o tipo a `complementLineFrom` e foca a Base ICMS no ICMS)
- Modify: `controller/purchaseInvoices/Add.controller.ts:169` (o tipo do draft vem do backend: usar `draft.InvoiceType` em vez de derivar de finNFe)

- [ ] **Step 1:** Implementar; parse dos XML alterados (`node -e` com um parser XML do `node_modules`); `yarn ts-typecheck`; `yarn lint` → limpos.
- [ ] **Step 2:** QUnit inteira → sem regressão.
- [ ] **Step 3: Commit** `feat(invoice): telas do complemento de ICMS`.

---

### Task 8: Verificação pelo caminho do usuário (CEAGUI dev + homologação)

Seguir a memória `run-stack-locally` (profile `ceagui`; Gateway antes do Web; backend do worktree; `yarn start:dev` do worktree do frontend); navegar pelo menu.

- [ ] Terceiro: importar um XML complementar de ICMS (montado a partir de `C:\Projetos\SiagroB1\nfe-homologacao-testes\fornecedor-ftestepj-nfe-791.xml`: finNFe 2, NFref da 791, vProd 0, vICMS > 0, chave com DV válido) → tipo, valores, confirmar, nenhum título.
- [ ] Próprio: complemento de ICMS sobre a série 9 nº 5 (FTESTE01) → emitir em homologação SEFAZ-SP; registrar cStat/xMotivo. Se rejeitar, corrigir no montador e repetir.
- [ ] Complemento de valor sem contrato (natureza de outra compra) → parcelas na confirmação.
- [ ] Cancelar a nº 5 → C8 na tela.
- [ ] Derrubar a stack por PID (portas 50000, 5246, 8081, 8080) e conferir as portas de novo.
