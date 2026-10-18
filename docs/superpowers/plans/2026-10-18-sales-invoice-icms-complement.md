# NF-e de saída de complemento de ICMS — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** O Documento de Saída ganha o tipo "Complemento de ICMS": criado a partir de uma venda autorizada, emite NF-e complementar (finNFe=2) só com base e ICMS, sem nenhum efeito comercial ou financeiro.

**Architecture:** `SalesInvoiceType.IcmsComplement = 2` + coluna `SalesInvoiceItem.ComplementedItemKey`; ação OData que cria o documento a partir dos itens da original (padrão do "Devolver"); motor de tributos com o aplicador de ICMS informado (generalizado da entrada); emissão pelo caminho da devolução (Pendente); exclusões explícitas nos efeitos de venda; frontend com ação no Detail e diálogo.

**Tech Stack:** .NET 10 / EF Core (migration) / OData / xUnit InMemory / OpenUI5 + TypeScript / QUnit.

**Spec:** `docs/superpowers/specs/2026-10-18-sales-invoice-icms-complement-design.md` (backend repo). Ler antes de cada tarefa. Referência de implementação: o complemento de ICMS da entrada (já em `main`): `PurchaseInvoiceComplement.cs`, `PurchaseInvoiceIcmsComplementTaxes.cs`, `PurchaseInvoicesTaxApplyService.cs`, `NfeIssueInputAssembler.Build(PurchaseInvoice)`, `NfeReadinessValidator.ValidateAsync(PurchaseInvoice)`.

**Worktrees:** backend `C:\Projetos\SiagroB1\siagro-b1-backend-sales-icms`, frontend `C:\Projetos\SiagroB1\siagro-b1-frontend-sales-icms`, branch `feature/sales-invoice-icms-complement` nos dois. Nunca trabalhar nos checkouts principais.

## Global Constraints

- Identificadores em inglês; texto ao usuário em pt-BR, exatamente as mensagens V1–V8 do spec.
- Rótulo do tipo: "Complemento de ICMS". `SalesInvoiceType { Normal = 0, Return = 1, IcmsComplement = 2 }`.
- Migration única `AddSalesInvoiceItemComplementedItem` (commit com trailer `DB: AddSalesInvoiceItemComplementedItem`). Não aplicar em banco nenhum durante as tarefas 1–7.
- Alíquota derivada: `Round(IcmsValue / IcmsBase * 100, 4)`. CST: original 00/20/90 → ele; CSOSN (3 dígitos) → 900; senão 90. IBS/CBS: alíquotas e reduções mantidas, base e valores 0.
- Só STANDALONE com a regra de tributação ativa na filial (V8).
- Commits `tipo(escopo): descrição` pt-BR, escopo `invoice`, trailers `Co-Authored-By`/`Claude-Session` da sessão. Nunca push. Arquivo novo → `git add` na hora.
- Backend: `dotnet test SiagroB1.Application.Tests` e `SiagroB1.Fiscal.Tests` verdes ao fim de cada tarefa (baseline do main: 5230 / 298). Frontend: `yarn ts-typecheck`, `yarn lint`, QUnit verde (gate de cobertura não é critério); parsear os XML alterados; nada de `--` em comentário XML; enum em expression binding com `targetType:'any'`.

## Review Focus

1. **Confirmação do complemento pelo resultado autorizado** não pode criar título a receber, alocação de contrato nem mexer em carga/liberação — mesmo que alguém PATCHe um `SalesContractKey` na linha. Teste na Task 5.
2. **Complementar uma venda vinda de carga/romaneio ou com contrato** é permitido (diferente do "Devolver"), e o complemento sai sem contrato e sem liberação. Teste na Task 3.
3. **Dois complementos para a mesma venda**: os dois nascem; cancelar a original é recusado enquanto qualquer um estiver ativo. Teste na Task 4.
4. **PATCH de linha do complemento trocando produto, quantidade ou preço** é recusado ou restaurado; só Base/ICMS mudam, e só em Pendente. Teste na Task 2.
5. **Emissão com a original cancelada entre a criação e a transmissão** recusa com V5 antes de reservar número. Teste na Task 4.

---

### Task 1: Tipo, coluna de vínculo e migration

**Files:**
- Modify: `SiagroB1.Domain/Enums/SalesInvoiceType.cs`
- Modify: `SiagroB1.Domain/Entities/SalesInvoiceItem.cs` (+ `ComplementedItemKey` Guid?, nav `ComplementedItem`)
- Modify: configuração EF da entidade (onde `SalesInvoiceItemOriginKey` é configurado) — FK NoAction, índice não único
- Create: migration `AddSalesInvoiceItemComplementedItem` em `SiagroB1.Migrations` (AppDbContext)
- Modify: EDM (onde `SalesInvoiceItem` é registrado) só se a propriedade não entrar sozinha
- Test: `SiagroB1.Application.Tests/SalesInvoices/SalesInvoiceIcmsComplementModelTests.cs`

**Interfaces — Produces:** `SalesInvoiceType.IcmsComplement`; `SalesInvoiceItem.ComplementedItemKey` / `.ComplementedItem`.

- [ ] Step 1: testes falhando — `Enum_values_are_explicit` (Normal 0, Return 1, IcmsComplement 2); `Complemented_item_fk_is_no_action` (metadata do EF: FK de `ComplementedItemKey` para `SalesInvoiceItem`, `DeleteBehavior.NoAction`); `Edm_exposes_complemented_item_key` (padrão dos testes de EDM existentes).
- [ ] Step 2: rodar → FAIL. Step 3: implementar + gerar a migration (`dotnet ef migrations add AddSalesInvoiceItemComplementedItem -p SiagroB1.Migrations -s SiagroB1.Web --context AppDbContext`, com `ASPNETCORE_ENVIRONMENT=Ceagui-Development`), conferir que só cria a coluna, a FK e o índice. Step 4: suítes verdes.
- [ ] Step 5: commit `feat(invoice): adiciona o complemento de ICMS ao documento de saída` + trailer `DB: AddSalesInvoiceItemComplementedItem`.

---

### Task 2: Tributos do complemento e travas de linha

**Files:**
- Modify: `SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoiceIcmsComplementTaxes.cs` → generalizar `Apply` para `INfeTaxedLine` (ou extrair `IcmsComplementTaxes` em `Services/Taxes/`), sem mudar o comportamento da entrada (testes da entrada verdes)
- Modify: `SiagroB1.Application/Services/SalesInvoices/SalesInvoicesTaxApplyService.cs`
- Modify: `SalesInvoicesItemsUpdateService.cs` (aceitar só IcmsBase/IcmsValue do complemento em Pendente; restaurar o resto), `SalesInvoicesItemsCreateService.cs` e o delete de linha (V7), `SalesInvoicesUpdateService.cs` (cabeçalho travado como a devolução própria: cliente, filial, tipo)
- Test: `SiagroB1.Application.Tests/SalesInvoices/SalesInvoiceIcmsComplementTaxesTests.cs`

**Interfaces:** Consumes Task 1. Produces `IcmsComplementTaxes.Apply(INfeTaxedLine line, string cst, decimal typedBase, decimal typedValue)` e `IcmsComplementTaxes.CstFor(string? originalCst)` (nome final à escolha do builder; registrar no relatório).

- [ ] Step 1: testes falhando — `Sales_icms_complement_keeps_typed_values_after_recalculation` (base 1000 / ICMS 120 → rate 12.0000, CST e CFOP da original, PIS/COFINS 0, IBS/CBS com alíquotas e valores 0); `Sales_icms_complement_cst_follows_original` (Theory 00→00, 20→20, 90→90, 51→90, 101→900); `Patch_changing_item_quantity_or_price_is_restored_or_refused` (Review Focus 4); `Patch_icms_values_on_pending_complement_is_kept`; `Adding_or_removing_lines_is_refused` → V7; `Purchase_icms_complement_still_passes` (rodar a suíte da entrada).
- [ ] Step 2: FAIL. Step 3: implementar (o motor roda com valor 0 e o aplicador repõe — mesmo desenho da entrada). Step 4: suítes verdes.
- [ ] Step 5: commit `feat(invoice): grava o ICMS informado no complemento de saída`.

---

### Task 3: Criar o complemento a partir da venda

**Files:**
- Create: `SiagroB1.Application/Services/SalesInvoices/SalesInvoicesCreateIcmsComplementService.cs` (+ interface, registro em `ServiceCollectionExtensions`)
- Modify: controller OData de `SalesInvoices` + EDM: ação `SalesInvoicesCreateIcmsComplement(Key, OriginItemKeys, IcmsBases, IcmsValues)` — mesmo formato de `SalesInvoicesCreateNfeReturn`
- Test: `SiagroB1.Application.Tests/SalesInvoices/SalesInvoicesCreateIcmsComplementTests.cs`

**Interfaces:** Consumes Tasks 1–2. Produces `Task<SalesInvoice> ExecuteAsync(Guid originKey, IReadOnlyList<Guid> originItemKeys, IReadOnlyList<decimal> icmsBases, IReadOnlyList<decimal> icmsValues, string userName)`.

- [ ] Step 1: testes falhando — `Creates_complement_with_inherited_lines` (cliente, filial, endereço de entrega; linhas com produto/descrição/UM/natureza/CFOP/NCM, quantidade e preço 0, `ComplementedItemKey` = item original, sem contrato/liberação, sem condição de pagamento, sem transporte; status Pendente; `InvoiceType` IcmsComplement); `Item_with_zero_base_and_value_is_left_out`; `Origin_not_normal_confirmed_authorized_is_refused` (Theory: Pendente, NF-e não autorizada, Return, `TaxDocumentKind.Other`) → V1; `All_items_zero_is_refused` → V2; `Base_or_value_missing_on_a_line_is_refused` → V3; `Value_above_base_is_refused` → V4; `Branch_without_tax_rule_is_refused` → V8; `Origin_from_shipment_load_with_contract_is_accepted` (Review Focus 2); `Two_complements_for_same_origin_are_allowed`; controller/EDM: a ação existe e devolve o documento.
- [ ] Step 2: FAIL. Step 3: implementar (os tributos do Task 2 rodam na criação). Step 4: suítes verdes.
- [ ] Step 5: commit `feat(invoice): cria o complemento de ICMS a partir da venda autorizada`.

---

### Task 4: Emissão e trava na original

**Files:**
- Modify: `SiagroB1.Application/Services/Nfe/SalesInvoicesNfeIssueService.cs` (`EnsureIssuableType`, `RequiredStatus` Pendente para o complemento, revalidação antes da reserva)
- Modify: `SiagroB1.Application/Services/Nfe/NfeReadinessValidator.cs` (`ValidateAsync(SalesInvoice)`: sem pagamento, sem pesos, V3/V4 por linha, referências e V5)
- Modify: `SiagroB1.Application/Services/Nfe/NfeIssueInputAssembler.cs` (`Build(SalesInvoice)`: `NfePurpose.Complement`, `ReferencedKeys`, `WithoutPayment`, sem transporte)
- Modify: `SiagroB1.Application/Services/SalesInvoices/SalesInvoicesCancelService.cs` (V6 no documento e na NF-e da original; complemento cancelado não trava)
- Test: `SiagroB1.Application.Tests/SalesInvoices/SalesInvoiceIcmsComplementIssueTests.cs`; Fiscal: acrescentar caso de saída se o builder ramificar por direção

- [ ] Step 1: testes falhando — `Complement_is_issuable_while_pending_without_payment_or_weights`; XML ponta a ponta (padrão do teste da entrada `Own_icms_complement…`): finNFe 2, NFref = chave da original, tPag 90, modFrete 9, sem `vol`, vProd 0, vBC/vICMS da linha, vNF 0, IBS/CBS com alíquotas (`gRed` presente quando houver redução); `Readiness_refuses_line_without_icms` → V3; `Original_cancelled_before_issue_is_refused` → V5 (Review Focus 5); `Cancelling_original_with_active_complement_is_refused` (documento e NF-e) → V6, e com dois complementos (Review Focus 3); `Cancelled_complement_does_not_block`.
- [ ] Step 2: FAIL. Step 3: implementar. Step 4: suítes verdes.
- [ ] Step 5: commit `feat(invoice): emite a NF-e de saída de complemento de ICMS`.

---

### Task 5: Complemento sem efeitos comerciais nem financeiros

**Files:**
- Modify: `SalesInvoicesConfirmService.cs` (pular alocação, razão de contrato, `loadClosureHook` e recebíveis para o complemento), `FinancialDocumentsSalesFirmSyncService.cs` (`DesiredAsync` exclui o tipo), `SalesInvoicesCancelService.cs` (gancho de carga e recebíveis), `SalesInvoicesReverseConfirmService.cs` se necessário, e os pontos que filtram "não-Return" listados no spec (S8). Cada ponto revisado e anotado no relatório (alterado / já seguro).
- Test: `SiagroB1.Application.Tests/SalesInvoices/SalesInvoiceIcmsComplementEffectsTests.cs`

- [ ] Step 1: testes falhando — `Authorized_complement_confirms_without_receivable_or_allocation` (Review Focus 1, inclusive com `SalesContractKey` forçado na linha e natureza com `CreatesFinancialDocument`); `Complement_does_not_touch_shipment_load_or_release`; `Cancelling_complement_runs_no_load_hook`; `Complement_has_no_return` (o "Devolver" recusa origem IcmsComplement).
- [ ] Step 2: FAIL. Step 3: implementar. Step 4: suítes verdes.
- [ ] Step 5: commit `fix(invoice): complemento de ICMS de saída sem efeito em contrato, título e carga`.

---

### Task 6: Frontend — helpers e formatter

**Files (frontend worktree, `webapp/`):**
- Create: `helpers/SalesIcmsComplementHelpers.ts` (`complementRowsFrom(items)`, `buildIcmsComplementPayload(rows)` → `{ OriginItemKeys, IcmsBases, IcmsValues }` só com as linhas com base ou valor > 0; `canComplementIcms(state)` com a mesma regra do backend: Normal, Confirmada, NF-e Autorizada, Kind Nfe, taxLocked)
- Modify: `model/formatter.ts` (`formatSalesInvoiceType`: IcmsComplement → "Complemento de ICMS")
- Modify: `model/ServerRoutes.ts` (rota da ação)
- Test: `test/unit/helpers/SalesIcmsComplementHelpers.qunit.ts` (+ registrar no `unitTests.qunit.ts`), `test/unit/model/formatter.qunit.ts`

- [ ] Step 1: QUnit falhando (payload filtra zerados; arrays paralelos na ordem; `canComplementIcms` por estado; formatter). Step 2: FAIL. Step 3: implementar. Step 4: QUnit + ts-typecheck + lint verdes.
- [ ] Step 5: commit `feat(invoice): helpers do complemento de ICMS de saída`.

---

### Task 7: Frontend — ação no Detail e o documento de complemento

**Files (`webapp/`):**
- Create: `view/salesInvoices/fragments/IcmsComplementDialog.fragment.xml` (Produto, Descrição, Quantidade, Valor, Base ICMS, ICMS — inputs Double 2 casas)
- Modify: `view/salesInvoices/Detail.view.xml` + `controller/salesInvoices/Detail.controller.ts` (botão "Complementar ICMS" via `canComplementIcms`; abre o diálogo com os itens da venda; chama a ação; navega para o documento criado; "Emitir NF-e" também para IcmsComplement Pendente; "Devolver" escondido no complemento)
- Modify: `view/salesInvoices/fragments/Form.fragment.xml` (item "Complemento de ICMS" no Select, desabilitado; condição de pagamento e Transporte escondidos no complemento), `fragments/Items.fragment.xml` (Incluir/Remover escondidos; colunas Base ICMS / ICMS visíveis só no complemento, editáveis só em `ui>/editable`; Quantidade/Preço/encargos escondidos no complemento)
- Modify: `fragments/Filterbar.fragment.xml`, `controller/salesInvoices/BaseController.ts` (valueMap da exportação; flag `ui>/icmsComplement`)

- [ ] Step 1: implementar; parse dos XML; ts-typecheck; lint; QUnit sem regressão.
- [ ] Step 2: commit `feat(invoice): tela do complemento de ICMS de saída`.

---

### Task 8: Verificação pelo caminho do usuário (CEAGUI dev + homologação)

Stack do worktree com profile `ceagui` (Gateway, Web, Reports), `yarn start:dev` do worktree do frontend; aplicar a migration `AddSalesInvoiceItemComplementedItem` em CEAGUI_SIAGRO_DEV **e** LMCEREAIS_SIAGRO_DEV (o Web confere todas as empresas) — já autorizado pelo usuário para bancos locais? **Perguntar antes.** Navegar pelo menu.

- [ ] Venda Normal autorizada em homologação (existente ou emitida para o teste) → Detail → "Complementar ICMS" → base/ICMS em um item → documento criado → "Emitir NF-e" → registrar cStat; conferir XML (finNFe 2, NFref, tPag 90, vNF 0).
- [ ] Nenhum título, alocação ou mudança de carga.
- [ ] Cancelar a venda original → V6 na tela (não cancelar de fato).
- [ ] Lista: filtro e exportação; Detail do complemento sem "Devolver".
- [ ] Derrubar a stack por PID e conferir as portas.
