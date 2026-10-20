# Conferência Fiscal Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Tela "Conferência Fiscal" (menu Fiscal, só STANDALONE) que mostra, para um mês e filial, os documentos fiscais escrituráveis (C100/C170), o resumo CST/CFOP/alíquota (C190), a apuração de ICMS por CFOP (base do E110), a prévia do R-2055 por produtor e as inconsistências — com exportação Excel por aba.

**Architecture:** Um serviço `FiscalConferenceService` em `SiagroB1.Reports` monta tudo via EF (`IUnitOfWork`) e um controller `POST /reports/FiscalConference` devolve JSON (padrão do `DashboardController`). O R-2055 reaproveita `FunruralWithholdingReportService.BuildAsync`. Frontend UI5 TS: página com filtros + `IconTabBar` de `sap.ui.table.Table` + `sap/ui/export/Spreadsheet`. Menu por migration em `CommonContext`.

**Tech Stack:** .NET 10, EF Core (InMemory nos testes), xUnit; OpenUI5 TypeScript.

**Spec:** `docs/superpowers/specs/2026-10-20-fiscal-conference-design.md` (leia antes — regras de inclusão, direção pelo CFOP e lista de inconsistências estão lá).

## Global Constraints

- Identificadores em inglês; textos do usuário (labels, mensagens, títulos) em pt-BR.
- Só STANDALONE: controller responde `BadRequest("Disponível somente no modo STANDALONE.")` quando `!ErpMode.IsStandalone(configuration)`.
- Fora do escopo: IPI, ICMS-ST, CT-e, NFS-e, PDF, geração de arquivo SPED/REINF.
- Branch `feature/fiscal-conference` nos dois repos; commit por tarefa (convenção do `CLAUDE.md` de cada repo; migration exige trailer `DB:`); **nunca push**; `git add` em todo arquivo novo.
- Testes: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~FiscalConference"`.

## Review Focus

1. Saída de devolução de compra lançada como `PurchaseInvoice` própria com CFOP 5/6xxx — deve contar como **débito** (direção pelo CFOP), não crédito. Teste na Task 2.
2. Entrada com `PostingDate` no mês seguinte à emissão — entra no mês do `PostingDate`. Teste na Task 1.
3. NF-e própria confirmada mas ainda não autorizada — fora dos totais, presente em Inconsistências. Teste na Task 3.
4. Mês sem nenhum documento — resposta com listas vazias e totais zero, sem erro; a tela mostra grades vazias. Teste na Task 1.
5. Arredondamento: diferença de ICMS de exatamente 0,01 **não** é inconsistência; 0,02 é. Teste na Task 3.

---

### Task 1: Serviço + aba Documentos (C100/C170)

**Files:**
- Create: `SiagroB1.Reports/Dtos/FiscalConferenceRequest.cs`, `SiagroB1.Reports/Dtos/FiscalConferenceDtos.cs`, `SiagroB1.Reports/Services/FiscalConferenceService.cs`
- Test: `SiagroB1.Application.Tests/Reports/FiscalConferenceServiceTests.cs`

**Interfaces:**
- Produces:
  - `FiscalConferenceRequest { string? BranchCode; int Year; int Month; }`
  - `FiscalConferenceData { List<FiscalDocumentLineDto> Documents; List<FiscalSummaryRowDto> Summary; List<IcmsAssessmentRowDto> Assessment; IcmsAssessmentTotalsDto AssessmentTotals; List<R2055ProducerRowDto> R2055; List<FiscalIssueDto> Issues; }`
  - `FiscalDocumentLineDto { string Direction /* "E"|"S" */; DateTime Date; string Model /* "55"|"01" */; string Series; string Number; string InternalNumber; string? AccessKey; string Partner; string ItemCode; string ItemName; string Cfop; string CstIcms; decimal OperationValue /* GrandTotal (+ComplementAmount já em Total na entrada) */; decimal IcmsBase; decimal IcmsRate; decimal IcmsValue; string CstPis; decimal PisValue; string CstCofins; decimal CofinsValue; string DocumentType /* "Entrada"|"Saída" — origem da tabela */; Guid DocumentKey; }`
  - `FiscalConferenceService(IUnitOfWork db, FunruralWithholdingReportService funrural)` com `Task<FiscalConferenceData> BuildAsync(FiscalConferenceRequest request)` (DI automática via Scrutor — nome termina em `Service`).
  - Helper interno `IEnumerable<FiscalDocumentLineDto> LoadLinesAsync(...)` retornando também as linhas **excluídas por NF-e não autorizada** separadamente (usada na Task 3).

- [ ] **Step 1: Write the failing tests** (seed com `InvoiceReportSeed`; julho/2026; `Service(db)` cria o serviço com `new FunruralWithholdingReportService(db, new RecordingFastReportService(), TestConfiguration.Erp("STANDALONE"))`)
  - `Build_ConfirmedSaleAndPurchaseOfTheMonthAreListed` → 2 linhas, Direction "S" e "E".
  - `Build_PendingAndCancelledAreLeftOut` → `InvoiceStatus.Pending`/`Cancelled` fora; `Returned` dentro.
  - `Build_NfeCancelledDeniedOrVoidedIsLeftOut` → `NfeStatus` Cancelled/Denied/Voided fora.
  - `Build_SaleWithoutTaxDocumentIsLeftOut`
  - `Build_NfseAndCteAreLeftOut` → `TaxDocumentKind.Nfse`/`Cte` fora.
  - `Build_PurchaseUsesPostingDate` → IssueDate 30/07, PostingDate 02/08: aparece em agosto, não em julho; com PostingDate nulo cai na IssueDate.
  - `Build_BranchFilterRestricts` → BranchCode "02" fora quando filtro "01"; sem filtro, todas.
  - `Build_EmptyMonthReturnsEmptyListsAndZeroTotals`
  - `Build_ModelIs55ForNfeAnd01ForOther`
  Para a direção use itens com `Cfop = "5101"`/`"1101"`.

- [ ] **Step 2: Run** `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~FiscalConferenceServiceTests"` → FAIL (tipos inexistentes).

- [ ] **Step 3: Implement** DTOs e `BuildAsync` preenchendo `Documents` (demais listas vazias por ora). Período: `[new DateTime(Year,Month,1), +1 mês)`. Regras de inclusão do spec, seção "Documentos escrituráveis". `Direction` vem do 1º dígito do CFOP (`1/2/3` → "E", `5/6/7` → "S"); CFOP vazio → direção do documento (entrada→"E", saída→"S"). Ordem: Date, DocumentType, Number, ItemCode. Valores ICMS/PIS/COFINS lidos das colunas gravadas na linha — nunca recalcular.

- [ ] **Step 4: Run** o mesmo comando → PASS.

- [ ] **Step 5: Commit** `feat(reports): cria conferência fiscal com documentos do mês` (backend).

### Task 2: Resumo C190 e Apuração ICMS (E110)

**Files:**
- Modify: `SiagroB1.Reports/Services/FiscalConferenceService.cs`, `SiagroB1.Reports/Dtos/FiscalConferenceDtos.cs`
- Test: `SiagroB1.Application.Tests/Reports/FiscalConferenceServiceTests.cs`

**Interfaces:**
- Consumes: `Documents` da Task 1.
- Produces:
  - `FiscalSummaryRowDto { string Direction; string CstIcms; string Cfop; decimal IcmsRate; decimal OperationValue; decimal IcmsBase; decimal IcmsValue; }`
  - `IcmsAssessmentRowDto { string Cfop; decimal OperationValue; decimal Debit; decimal Credit; }`
  - `IcmsAssessmentTotalsDto { decimal Debits; decimal Credits; decimal Balance /* Debits - Credits; positivo = a recolher */; }`

- [ ] **Step 1: Write the failing tests**
  - `Build_SummaryGroupsByDirectionCstCfopAndRate` → duas linhas mesma chave somam; alíquota diferente separa.
  - `Build_AssessmentDebitsOutgoingAndCreditsIncoming` → venda 5101 ICMS 120 + compra 1101 ICMS 70: Debits 120, Credits 70, Balance 50.
  - `Build_OwnPurchaseReturnWithOutgoingCfopIsDebit` → `PurchaseInvoice` `InvoiceType.Return`, `IssuerType.Own`, CFOP 5202, ICMS 30 → soma em Debits.
  - `Build_ComplementCountsInAssessment` → `SalesInvoiceType.IcmsComplement` com ICMS 10 entra em Debits.

- [ ] **Step 2: Run** → FAIL.
- [ ] **Step 3: Implement** agrupamentos a partir de `Documents` (já filtrado). Ordem do resumo: Direction ("E" antes de "S"), Cfop, CstIcms, IcmsRate. Apuração ordenada por Cfop.
- [ ] **Step 4: Run** → PASS.
- [ ] **Step 5: Commit** `feat(reports): resume CST/CFOP e apura ICMS na conferência fiscal`.

### Task 3: Inconsistências

**Files:** mesmos da Task 2.

**Interfaces:**
- Produces: `FiscalIssueDto { string Direction; DateTime Date; string Number; string InternalNumber; string Partner; string? ItemCode; string Rule /* texto pt-BR */; Guid DocumentKey; }`

Textos fixos de `Rule` (exatos):
1. `"Item sem CFOP"` 2. `"Item sem CST de ICMS"` 3. `"CFOP de entrada em documento de saída"` / `"CFOP de saída em documento de entrada"` 4. `"ICMS difere de base × alíquota"` 5. `"NF-e sem chave de acesso válida"` 6. `"NF-e própria confirmada e não autorizada"`.

- [ ] **Step 1: Write the failing tests** — um teste por regra (`Build_IssueItemWithoutCfop`, `..._WithoutCstIcms`, `..._CfopDirectionMismatch`, `..._IcmsMismatch`, `..._NfeWithoutAccessKey`, `..._OwnNfeNotAuthorized`), mais:
  - `Build_IcmsDifferenceOfOneCentIsNotAnIssue` (base 1000, alíq. 12, ICMS 120,01 → sem issue; 120,02 → issue).
  - `Build_ReturnWithOppositeCfopIsNotADirectionIssue` (devoluções `InvoiceType.Return` não disparam a regra 3).
  - `Build_OwnNfeNotAuthorizedIsLeftOutOfTotals` → não está em `Documents` nem em `AssessmentTotals`.
  - `Build_OtherModelWithoutKeyIsNotAnIssue` (modelo "01" não precisa de chave).
  Regra 5: chave com 44 dígitos numéricos; regra 6: emissão própria (saída sempre; entrada `IssuerType.Own`) com `TaxDocumentKind.Nfe` e `NfeStatus != Authorized`.
- [ ] **Step 2: Run** → FAIL. **Step 3: Implement.** **Step 4: Run** → PASS.
- [ ] **Step 5: Commit** `feat(reports): aponta inconsistências na conferência fiscal`.

### Task 4: Prévia do R-2055

**Files:**
- Modify: `SiagroB1.Reports/Dtos/FunruralWithholdingRowDto.cs` (add `string CardCode`), `SiagroB1.Reports/Services/FunruralWithholdingReportService.cs` (preencher `CardCode` em `ItemRow`, `ReturnRow` — devolução herda o produtor da linha de origem — e na linha Líquido), `FiscalConferenceService.cs`, `FiscalConferenceDtos.cs`
- Test: `FiscalConferenceServiceTests.cs`; rodar também `FunruralWithholdingReportServiceTests`.

**Interfaces:**
- Consumes: `FunruralWithholdingReportService.BuildAsync(FunruralWithholdingRequest)`; chamar com `FromDate`/`ToDate` do mês, `BranchCode`, `Statuses` nulos (= padrão do relatório).
- Produces: `R2055ProducerRowDto { string CardCode; string CardName; string TaxId /* RuralProducerCpf quando houver, senão TaxId do PN */; string ProducerType /* descrição pt-BR do RuralProducerType ou "" */; decimal Base; decimal Inss; decimal Rat; decimal Senar; decimal Total; }`

- [ ] **Step 1: Write the failing tests**
  - `Build_R2055GroupsByProducer` → 2 notas do mesmo produtor somam; outro produtor em linha separada.
  - `Build_R2055DeductsReturns` → compra 1000 (INSS 12) com devolução de 400 → Base 600, Inss 7,20.
  - `Build_R2055UsesRuralProducerCpfWhenPresent`.
  Valores de Funrural via o helper `Withheld(...)` já usado em `FunruralWithholdingReportServiceTests` (mova-o para `InvoiceReportSeed` se for privado).
- [ ] **Step 2: Run** → FAIL. **Step 3: Implement** somando apenas linhas `Item` e `Return` (ignorar `Net`) por `CardCode`; buscar `BusinessPartner` por `CardCode` para TaxId/tipo. Ordenar por CardName.
- [ ] **Step 4: Run** `--filter "FullyQualifiedName~FiscalConference|FullyQualifiedName~FunruralWithholding"` → PASS.
- [ ] **Step 5: Commit** `feat(reports): prévia do R-2055 por produtor na conferência fiscal`.

### Task 5: Controller

**Files:**
- Create: `SiagroB1.Reports/Controllers/FiscalConferenceController.cs`
- Test: `SiagroB1.Application.Tests/Reports/FiscalConferenceControllerTests.cs` (modelo: `StorageRemittancesByPeriodControllerTests.cs`)

**Interfaces:** `[Route("/reports/FiscalConference")]`, `[HttpPost] Task<IActionResult> Get([FromBody] FiscalConferenceRequest request)` → `Ok(FiscalConferenceData)`.

- [ ] **Step 1: Tests** `Post_RefusesInSapb1Mode` (BadRequest com o texto do Global Constraints); `Post_RejectsInvalidMonth` (Month 0 ou 13, Year < 2000 → BadRequest `"Informe um mês válido."`); `Post_ReturnsDataInStandalone`.
- [ ] **Step 2: Run** → FAIL. **Step 3: Implement.** **Step 4: Run** → PASS; `dotnet build SiagroB1.sln` sem erros.
- [ ] **Step 5: Commit** `feat(reports): expõe a conferência fiscal`.

### Task 6: Menu

**Files:**
- Create: `SiagroB1.Migrations/CommonContext/<timestamp>_AddFiscalConferenceMenu.cs` (+ `.Designer.cs`) via `dotnet ef migrations add AddFiscalConferenceMenu --context CommonDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web` com `ASPNETCORE_ENVIRONMENT=Development` explícito; corpo escrito à mão como `20261012165822_AddStorageRemittancesReportMenu.cs`.

Valores: `MENU_ITEMS` Key `"fiscalConference"`, Title `"Conferência Fiscal"`, Icon `"sap-icon://inspection"`, Enabled true, Expanded false, Order 5, ParentKey `"fiscal"`, StandaloneOnly true; `ROLE_MENUS` RoleCode `"ADMIN"` com Guid novo fixo. `Down` remove ROLE_MENUS antes de MENU_ITEMS.

- [ ] **Step 1:** gerar e editar a migration; o snapshot não deve mudar além do que o EF gerar (seed via InsertData não altera o modelo).
- [ ] **Step 2:** aplicar no banco local de dev **numa cópia** se houver migration de outro branch (ver memória `shared-local-db-other-branch-migrations`); conferir `SELECT * FROM MENU_ITEMS WHERE [Key]='fiscalConference'`.
- [ ] **Step 3: Commit** `feat(reports): adiciona Conferência Fiscal ao menu Fiscal` com trailer `DB: AddFiscalConferenceMenu`.

### Task 7: Tela (frontend)

**Files (repo `siagro-b1-frontend`):**
- Create: `webapp/controller/fiscalConference/Main.controller.ts`, `webapp/view/fiscalConference/Main.view.xml`
- Modify: `webapp/model/ServerRoutes.ts` (`fiscalConference: '/reports/FiscalConference'`), `webapp/manifest.json` (rota `{ "pattern": "fiscal-conference", "name": "fiscalConference", "target": "fiscalConference" }` + target `level: 1`, `name: "siagrob1.view.fiscalConference.Main"`, `clearControlAggregation: true`)

**Interfaces:** consome o JSON da Task 5 (camelCase — ver memória `odata-api-serializes-camelcase`: `documents`, `summary`, `assessment`, `assessmentTotals`, `r2055`, `issues`).

- [ ] **Step 1:** View: `Page` título "Conferência Fiscal"; filtro Filial (mesmo Input + `.openBranchsValueHelp` do fragment `InvoiceReportCommonFilters`) e Mês (`DatePicker` `displayFormat="MM/yyyy"`, obrigatório, padrão = mês anterior); botão "Consultar". `IconTabBar` com abas "Documentos", "Resumo CST/CFOP", "Apuração ICMS" (grade + painel com Débitos/Créditos/Saldo), "R-2055", "Inconsistências" (contador no `count` da aba). Cada aba: `sap.ui.table.Table` sobre um JSONModel `conference` e botão "Exportar Excel". Valores com `sap.ui.model.type.Float` 2 casas.
- [ ] **Step 2:** Controller estende `CommonController`: `onSearch()` faz `fetch(ServerRoutes.fiscalConference, POST, {branchCode, year, month})`; erro → `MessageBox.error(await response.text())`; `onExport(tab)` com `Spreadsheet` como `financialAccountMovements/Main.controller.ts` (converter datas ISO para `Date`).
- [ ] **Step 3: Verify:** `npm run ts-typecheck` e `npm run lint` limpos; subir a stack (memória `run-stack-locally`, profile CEAGUI) e, **a partir da home**, abrir Fiscal → Conferência Fiscal, consultar um mês com notas da CEAGUI, conferir que as 5 abas carregam e o Excel baixa. Derrubar a stack pelo PID.
- [ ] **Step 4: Commit** (frontend) `feat(reports): tela de conferência fiscal` — seguir a convenção do `CLAUDE.md` do frontend.
