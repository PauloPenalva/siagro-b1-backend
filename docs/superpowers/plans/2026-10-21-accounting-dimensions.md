# Dimensões gerenciais e rateio nos documentos — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fazer cada documento de resultado do SiagroB1 STANDALONE (linhas das notas de saída e
entrada, títulos, lançamentos na conta) carregar até 5 dimensões gerenciais (centro de custo ou
regra de distribuição), com cadastros, padrões e obrigatoriedade configuráveis. Isso prepara a
contabilização (sub-projeto 6) e o DRE (7).

**Architecture:** Cinco colunas `Dim1Code`…`Dim5Code` (VARCHAR(10), anuláveis), sem FK, em
cada documento via a interface `IDimensionFields`. Um validador único confere cada código
contra centros e regras da dimensão. Um resolvedor preenche os campos vazios na ordem cabeçalho →
parceiro → natureza, e uma guarda aplica a obrigatoriedade na confirmação. As regras de
distribuição são versionadas por `ValidFrom`, e a expansão em centros usa a versão vigente na
data do documento.

**Tech Stack:** .NET 10, EF Core (SQL Server, InMemory nos testes), OData v4, xUnit; OpenUI5 +
TypeScript, QUnit.

**Spec:** `docs/superpowers/specs/2026-10-21-transport-and-managerial-accounting-design.md`
(seção A e "Detalhes do sub-projeto 1").

## Global Constraints

- Apenas STANDALONE: todo serviço, guarda e resolvedor novo retorna sem efeito quando
  `!ErpMode.IsStandalone(configuration)`, e os menus novos têm `StandaloneOnly = true`.
- Identificadores em inglês; textos de tela e mensagens de erro em pt-BR (`DefaultException`).
- `SalesInvoiceItem.CostCenterCode` é **renomeado** para `Dim1Code` (`RenameColumn`, sem
  perda de dado).
- Código de centro/regra: `VARCHAR(10)`; nome de dimensão `VARCHAR(50)`; percentual
  `DECIMAL(9,4)`, e a soma das linhas de uma versão tem que ser exatamente `100`.
- Um código é único por empresa entre `COST_CENTERS` e `DISTRIBUTION_RULES`.
- No máximo uma dimensão por papel (`Vehicle`, `Partner`); `None` pode repetir.
- `LedgerAccountCode` (VARCHAR(20), anulável, mesmo tamanho do campo existente) acompanha as dimensões em todo documento que as
  recebe; `SalesInvoiceItem.LedgerAccountCode` já existe e é mantido. Válido = `LedgerAccount`
  ativa com `AllowsPosting`.
- Obrigatoriedade por **parâmetro da empresa** (`COMPANY_SETTINGS`, uma linha no banco da
  empresa): `RequireCostCenter` exige `Dim1Code`, `RequireLedgerAccount` exige `LedgerAccountCode`.
  As duas vêm desligadas.
- Obrigatoriedade bloqueia **confirmar** (notas) ou **criar** (título manual, adiantamento,
  lançamento na conta, que nascem prontos), nunca salvar o rascunho de nota. Documentos já
  confirmados não são revalidados.
- Transferência entre contas (`FinancialAccountTransfer`) **não** recebe dimensões.
- Toda migration precisa do trailer `DB: <NomeDaMigration>` no commit. Nova tela exige
  migration de menu na Common com linha em `ROLE_MENUS` para `ADMIN`.
- Arquivo novo é preparado com `git add` logo após ser criado. Os commits vão no branch
  `feature/transport-managerial-accounting` dos dois repos, sem push e sem merge.

## Review Focus

1. **Código digitado em minúsculas ou com espaço** (`" cc01"`): espera-se que seja normalizado
   (trim + maiúsculas) antes de validar e gravar. Teste na Task 5.
2. **Centro ou regra inativados depois de usados**: o documento antigo continua abrindo e
   confirmando; só um código **novo** digitado é recusado. Teste na Task 5 (o validador só
   recebe os campos alterados) e na Task 7.
3. **Ligar "Exige centro de custo" com rascunhos antigos sem centro**: o rascunho passa a
   exigir o centro só ao confirmar, e as notas já confirmadas não são revalidadas. A dimensão 1
   nunca pode ser desativada, porque é ela que a chave exige. Testes nas Tasks 2 e 8.
4. **Regra com versão nova vigente amanhã**: um documento de hoje ainda expande pela versão
   atual. Teste na Task 4.
5. **Trocar o papel "Parceiro" para outra dimensão**: o padrão gravado no parceiro passa a ir
   para a nova dimensão (o parceiro guarda um código só, `DefaultDimensionCode`). Um código que
   não pertence à nova dimensão é ignorado no preenchimento, não dá erro. Teste na Task 5.

---

## File Map

Backend (`siagro-b1-backend-transport/`):

| Arquivo | Responsabilidade |
|---|---|
| `SiagroB1.Domain/Entities/AccountingDimension.cs` | Dimensão 1–5 (nome, ativa, papel) |
| `SiagroB1.Domain/Enums/DimensionRole.cs` | `None`, `Vehicle`, `Partner` |
| `SiagroB1.Domain/Entities/DistributionRule.cs`, `DistributionRuleVersion.cs`, `DistributionRuleLine.cs` | Regra, versões, linhas |
| `SiagroB1.Domain/Entities/CompanySettings.cs` | Parâmetros da empresa (exige centro / conta) |
| `SiagroB1.Domain/Interfaces/IDimensionFields.cs` | `Dim1Code`…`Dim5Code` |
| `SiagroB1.Domain/Models/DimensionCodeModel.cs` | União centro+regra para o value help |
| `SiagroB1.Application/Services/Dimensions/*` | Serviços, validador, resolvedor, guarda, expansão |
| `SiagroB1.Web/Controllers/{AccountingDimensions,DistributionRules,DimensionCodes,CompanySettings}Controller.cs` | OData |
| `SiagroB1.Migrations/AppContext/*_AddAccountingDimensions.cs` | Tabelas, colunas, rename, seed |
| `SiagroB1.Migrations/CommonContext/*_AddAccountingDimensionMenus.cs` | Menus |
| `SiagroB1.Application.Tests/Dimensions/*` | Testes |

Frontend (worktree irmão `siagro-b1-frontend-transport/`):

| Arquivo | Responsabilidade |
|---|---|
| `webapp/view/accountingDimensions/Main.view.xml` + controller | Dimensões |
| `webapp/view/companySettings/Main.view.xml` + controller | Parâmetros da Empresa |
| `webapp/view/distributionRules/{Main,Add,Edit}.view.xml` + controllers | Regras e versões |
| `webapp/dialogs/fragments/DimensionCodesSelectDialog.fragment.xml` | Value help por dimensão |
| `webapp/view/common/fragments/DimensionFields.fragment.xml` | Bloco reutilizável com os 5 campos |
| `webapp/helpers/DimensionHelpers.ts` | Carregar dimensões, rótulo e visibilidade |

---

### Task 1: Esquema — entidades, colunas e migration

**Files:**
- Create: `SiagroB1.Domain/Interfaces/IDimensionFields.cs`, `Enums/DimensionRole.cs`, `Entities/AccountingDimension.cs`, `Entities/DistributionRule.cs`, `Entities/DistributionRuleVersion.cs`, `Entities/DistributionRuleLine.cs`, `Entities/CompanySettings.cs`
- Modify: `Entities/CostCenter.cs` (+`DimensionNumber`), `SalesInvoiceItem.cs:291` (rename + Dim2–5), `SalesInvoice.cs`, `PurchaseInvoice.cs`, `PurchaseInvoiceItem.cs`, `FinancialDocument.cs`, `FinancialAccountEntry.cs`, `Usage.cs` (todos implementam `IDimensionFields`), `BusinessPartner.cs` (+`DefaultDimensionCode`), `SiagroB1.Infra/Context/AppDbContext.cs`
- Modify: `SiagroB1.Application.Tests/SalesInvoices/TaxSnapshotTests.cs:61,68` (`CostCenterCode` → `Dim1Code`)
- Create: migration `AddAccountingDimensions` (AppContext)
- Test: `SiagroB1.Application.Tests/Dimensions/DimensionSchemaTests.cs`

**Interfaces:**
- Produces:
  - `interface IDimensionFields { string? Dim1Code; … string? Dim5Code; }` (get/set). Extensão `static string? GetDim(this IDimensionFields f, int n)` / `SetDim(int n, string? v)` em `IDimensionFieldsExtensions.cs`.
  - `AccountingDimension { int Number (PK, 1–5, não identity); string Name; bool Active; DimensionRole Role }` → `ACCOUNTING_DIMENSIONS`.
  - `CostCenter.DimensionNumber int NOT NULL DEFAULT 1`.
  - `DistributionRule { string Code PK; string Name; int DimensionNumber; bool Inactive; List<DistributionRuleVersion> Versions }` → `DISTRIBUTION_RULES`.
  - `DistributionRuleVersion { Guid Id; string RuleCode; DateTime ValidFrom (date); List<DistributionRuleLine> Lines }` → `DISTRIBUTION_RULE_VERSIONS`, índice único `(RuleCode, ValidFrom)`, cascade das linhas.
  - `DistributionRuleLine { Guid Id; Guid VersionId; string CostCenterCode; decimal Percentage }` → `DISTRIBUTION_RULE_LINES`.
  - `CompanySettings { int Id (PK, sempre 1); bool RequireCostCenter; bool RequireLedgerAccount }` → `COMPANY_SETTINGS`, linha `(1,false,false)` semeada.
  - `IDimensionFields` também tem `string? LedgerAccountCode`. `SalesInvoiceItem` mantém o seu; os outros ganham a coluna `VARCHAR(20)`.
  - `enum DimensionRole { None = 0, Vehicle = 1, Partner = 2 }`.

- [ ] **Step 1: Escrever o teste `DimensionSchemaTests.Persists_dimension_fields_on_all_documents`.** Grava um `SalesInvoiceItem`, um `PurchaseInvoiceItem`, um `FinancialDocument` e um `FinancialAccountEntry` com `Dim1Code="CC01"` e `Dim5Code="R5"`, e confere que voltam do `TestDb`. Na mesma classe, `Rule_version_lines_cascade`: grava uma regra com versão e 2 linhas e confere que a regra volta com `Include` das versões e das linhas.
- [ ] **Step 2: Rodar.** `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~DimensionSchemaTests"`. Esperado: falha de compilação (os tipos não existem).
- [ ] **Step 3: Criar as entidades, as colunas e os `DbSet`** conforme o bloco Interfaces. Renomear `CostCenterCode` para `Dim1Code` no `TaxSnapshotTests`.
- [ ] **Step 4: Gerar a migration.** `dotnet ef migrations add AddAccountingDimensions --project SiagroB1.Migrations --startup-project SiagroB1.Web --context AppDbContext`. Conferir à mão:
  - `RenameColumn("CostCenterCode" → "Dim1Code")` em `SALES_INVOICE_ITEMS`. O EF costuma gerar drop+add, e isso precisa ser trocado.
  - `CostCenter.DimensionNumber` com `defaultValue: 1`.
  - `InsertData` das 5 dimensões: `(1,"Dimensão 1",true,0)`, `(2,"Dimensão 2",false,0)` … `(5,"Dimensão 5",false,0)`.
- [ ] **Step 5: Rodar o teste e o build.** `dotnet build SiagroB1.sln` e o filtro do Step 2. Esperado: PASS.
- [ ] **Step 6: Commit.** `feat(master-data): esquema das dimensões gerenciais e regras de distribuição` com o trailer `DB: AddAccountingDimensions`.

### Task 2: Cadastro de dimensões

**Files:**
- Create: `SiagroB1.Application/Services/Dimensions/AccountingDimensionService.cs`, `SiagroB1.Domain/Interfaces/IAccountingDimensionService.cs`, `SiagroB1.Web/Controllers/AccountingDimensionsController.cs`
- Modify: `ServiceCollectionExtensions.cs` (`AddStandAloneServices`), `ODataConfigurations.cs` (`EntitySet<AccountingDimension>("AccountingDimensions")`)
- Test: `SiagroB1.Application.Tests/Dimensions/AccountingDimensionServiceTests.cs`

**Interfaces:**
- Produces: `IQueryable<AccountingDimension> QueryAll()`; `Task<AccountingDimension?> UpdateAsync(int number, AccountingDimension changes)`. Não há create nem delete: as 5 linhas são fixas e o controller expõe só GET e PATCH.

- [ ] **Step 1: Testes.**
  - `Update_renames_and_activates`.
  - `Second_dimension_with_same_role_is_rejected`: dim 2 é `Vehicle`, e pôr `Vehicle` na dim 3 lança `DefaultException("A dimensão Dimensão 2 já tem o papel Veículo.")`.
  - `Role_none_can_repeat`.
  - `Dimension_1_cannot_be_deactivated`: lança `DefaultException("A dimensão 1 é o centro de custo e não pode ser desativada.")`.
  - `Name_is_required`: um nome em branco lança `DefaultException("Informe o nome da dimensão.")`.
- [ ] **Step 2: Rodar.** Esperado: falha (o serviço não existe).
- [ ] **Step 3: Implementar o serviço e o controller.** Seguir o padrão de `CostCentersController`. O nome é gravado com trim. O texto do papel na mensagem vem de um mapa: `Vehicle` → "Veículo", `Partner` → "Parceiro".
- [ ] **Step 4: Rodar.** Esperado: PASS.
- [ ] **Step 5: Commit.** `feat(master-data): cadastro de dimensões gerenciais`.

### Task 3: Centro de custo com dimensão + união de códigos

**Files:**
- Modify: `SiagroB1.Domain/Models/CostCenterModel.cs` (+`DimensionNumber`), `SiagroB1.Application/Services/CostCenterService.cs`, `Services/SAP/CostCenterService.cs` (a projeção devolve `DimensionNumber = 1`)
- Create: `SiagroB1.Domain/Models/DimensionCodeModel.cs`, `SiagroB1.Application/Services/Dimensions/DimensionCodeQueryService.cs`, `SiagroB1.Web/Controllers/DimensionCodesController.cs`
- Test: `SiagroB1.Application.Tests/Dimensions/CostCenterDimensionTests.cs`, `DimensionCodeQueryServiceTests.cs`

**Interfaces:**
- Produces: `DimensionCodeModel { string Code; string Name; int DimensionNumber; string Kind ("CostCenter"|"Rule"); bool Inactive }`; `IQueryable<DimensionCodeModel> DimensionCodeQueryService.QueryAll()` (Concat de centros e regras), exposto como `EntitySet<DimensionCodeModel>("DimensionCodes")` só leitura com chave `Code`.

- [ ] **Step 1: Testes.**
  - `Create_rejects_code_used_by_rule`: lança `DefaultException("O código R01 já é uma regra de distribuição.")`.
  - `Create_rejects_dimension_out_of_range`: `DimensionNumber=6` lança `"Dimensão inválida."`.
  - `Create_normalizes_code`: `" cc01 "` vira `"CC01"`.
  - `DimensionCodes_lists_centers_and_rules_of_each_dimension`.
- [ ] **Step 2: Rodar.** Esperado: falha.
- [ ] **Step 3: Implementar.** O `CreateAsync` normaliza o código (`Trim().ToUpperInvariant()`) e valida a faixa 1–5 e a colisão com regra. O `UpdateAsync` permite trocar a dimensão.
- [ ] **Step 4: Rodar.** Esperado: PASS.
- [ ] **Step 5: Commit.** `feat(master-data): centro de custo por dimensão e consulta de códigos`.

### Task 4: Regras de distribuição, versões e expansão

**Files:**
- Create: `SiagroB1.Application/Services/Dimensions/DistributionRuleService.cs`, `DistributionRuleExpander.cs`, `SiagroB1.Domain/Interfaces/IDistributionRuleService.cs`, `SiagroB1.Web/Controllers/DistributionRulesController.cs`
- Test: `SiagroB1.Application.Tests/Dimensions/DistributionRuleServiceTests.cs`, `DistributionRuleExpanderTests.cs`

**Interfaces:**
- Consumes: as entidades da Task 1.
- Produces:
  - `DistributionRuleService`:
    - `Task<DistributionRule> CreateAsync(DistributionRule rule)`: grava a regra já com a primeira versão;
    - `Task<DistributionRule?> UpdateHeaderAsync(string code, string name, bool inactive)`;
    - `Task<DistributionRuleVersion> SaveVersionAsync(string code, DistributionRuleVersion version, DateTime today)`: cria uma versão, ou substitui uma versão **futura** com o mesmo `ValidFrom`;
    - `Task DeleteVersionAsync(Guid versionId, DateTime today)`.
  - OData: `DistributionRules` com `$expand=Versions($expand=Lines)`, mais as ações não ligadas `distributionRulesSaveVersion(Code, ValidFrom, Lines)` e `distributionRulesDeleteVersion(VersionId)`.
  - `DistributionRuleExpander.ExpandAsync(int dimensionNumber, string code, DateTime documentDate) : Task<IReadOnlyList<(string CostCenterCode, decimal Percentage)>>`. Um centro devolve `[(code, 100)]`. Uma regra devolve as linhas da versão com o maior `ValidFrom <= documentDate`. Sem versão vigente, lança `DefaultException("A regra {code} não tem versão vigente em {data:dd/MM/yyyy}.")`.

- [ ] **Step 1: Testes do serviço.**
  - `Lines_must_sum_100`: 60 + 30 lança `"A soma dos percentuais da regra deve ser 100%."`.
  - `Lines_must_be_active_centers_of_rule_dimension`: um centro da dimensão 2 numa regra da dimensão 1 lança `"O centro CC09 não pertence à dimensão Dimensão 1."`.
  - `Duplicate_center_in_version_rejected`.
  - `Code_cannot_collide_with_cost_center`.
  - `Current_or_past_version_is_immutable`: `SaveVersionAsync` com `ValidFrom <= today` sobre uma versão existente na mesma data lança `"A versão vigente em {data} já está em uso e não pode ser alterada; crie uma versão com nova data."`.
  - `Future_version_can_be_replaced_and_deleted`.
- [ ] **Step 2: Testes do expander.**
  - `Center_expands_to_itself`.
  - `Uses_version_valid_at_document_date`: versões em 01/10 (60/40) e 22/10 (50/50); o documento de 21/10 sai 60/40 e o de 22/10 sai 50/50.
  - `No_version_before_first_ValidFrom_throws`.
  - `Unknown_code_throws`.
- [ ] **Step 3: Rodar.** Esperado: falha.
- [ ] **Step 4: Implementar.** O "hoje" é recebido por parâmetro, para o teste ser determinístico. No controller, usar a data de Brasília, como no NF-e (`TimeZoneInfo` "E. South America Standard Time").
- [ ] **Step 5: Rodar.** Esperado: PASS.
- [ ] **Step 6: Commit.** `feat(master-data): regras de distribuição versionadas por vigência`.

### Task 5: Validador de códigos e resolvedor de padrões

**Files:**
- Create: `SiagroB1.Application/Services/Dimensions/DimensionCodeValidator.cs`, `DimensionDefaultsResolver.cs`
- Test: `SiagroB1.Application.Tests/Dimensions/DimensionCodeValidatorTests.cs`, `DimensionDefaultsResolverTests.cs`

**Interfaces:**
- Produces:
  - `DimensionCodeValidator(IUnitOfWork db, IConfiguration configuration)`
    - `.NormalizeAndValidateAsync(IDimensionFields target, IDimensionFields? previous = null) : Task`
    - Normaliza todos os campos: trim, maiúsculas, vazio vira null.
    - Valida só os campos cujo valor difere de `previous`. Se `previous` é null, valida todos os preenchidos.
    - O código precisa ser um centro **ativo** ou uma regra **ativa** da dimensão N, e a dimensão N precisa estar **ativa**.
    - Erro: `"O código {code} não é centro de custo nem regra de distribuição ativa da dimensão {Name}."`.
    - `LedgerAccountCode` alterado precisa ser uma `LedgerAccount` ativa com `AllowsPosting`. Erro: `"A conta contábil {code} não existe, está inativa ou não aceita lançamento."`.
    - Em SAPB1, não faz nada.
  - `DimensionDefaultsResolver(IUnitOfWork db, IConfiguration configuration)`
    - `.ApplyAsync(IDimensionFields target, IDimensionFields? header, string? partnerCode, int? usageCode) : Task`
    - Para cada dimensão ativa N com `target` vazio, preenche na ordem: `header`, depois o `BusinessPartner.DefaultDimensionCode` (só se N for a dimensão de papel `Partner`), depois `Usage.DimNCode`. A `LedgerAccountCode` vazia é preenchida com a do `header` e depois com `Usage.LedgerAccountCode`.
    - Um candidato inválido para a dimensão (inexistente ou inativo) é pulado em silêncio, e o resolvedor tenta o próximo.
    - Nunca sobrescreve um valor já preenchido.

- [ ] **Step 1: Testes do validador.**
  - `Accepts_active_center_and_rule_of_dimension`.
  - `Rejects_code_of_other_dimension`.
  - `Rejects_inactive_dimension`.
  - `Normalizes_lowercase_and_spaces`.
  - `Unchanged_inactive_code_is_accepted_when_previous_given` (Review Focus 2).
  - `Noop_in_sapb1` (configuração `Erp=SAPB1`).
- [ ] **Step 2: Testes do resolvedor.**
  - `Header_wins_over_partner_and_usage`.
  - `Partner_default_goes_to_partner_role_dimension_only`.
  - `Typed_value_is_never_overwritten`.
  - `Invalid_partner_default_falls_through_to_usage` (Review Focus 5).
  - `Inactive_dimension_is_not_filled`.
  - `Ledger_account_from_usage`, `Rejects_non_posting_ledger_account` (no teste do validador).
- [ ] **Step 3: Rodar.** Esperado: falha.
- [ ] **Step 4: Implementar.** Carregar uma vez por chamada as dimensões ativas e os códigos válidos por dimensão (`DimensionCodeQueryService`).
- [ ] **Step 5: Rodar.** Esperado: PASS.
- [ ] **Step 6: Commit.** `feat(financial): validação e preenchimento padrão das dimensões`.

### Task 6: Notas de saída e de entrada

**Files:**
- Modify: `Services/SalesInvoices/SalesInvoicesItemsCreateService.cs`, `SalesInvoicesItemsUpdateService.cs`, o serviço de create/update do cabeçalho de `SalesInvoice`, e os equivalentes em `Services/PurchaseInvoices/`; `ServiceCollectionExtensions.cs` (registrar o validador e o resolvedor em `AddApplicationServices`)
- Test: `SiagroB1.Application.Tests/Dimensions/InvoiceDimensionsTests.cs`

**Interfaces:**
- Consumes: `DimensionCodeValidator.NormalizeAndValidateAsync`, `DimensionDefaultsResolver.ApplyAsync` (Task 5).
- Comportamento:
  - Ao criar ou alterar uma linha: primeiro `ApplyAsync(item, header: invoice, partnerCode: invoice.CardCode, usageCode: item.UsageCode)`, depois `NormalizeAndValidateAsync(item, previousSnapshot)`.
  - No cabeçalho: `ApplyAsync(invoice, null, invoice.CardCode, null)` e validar.
  - Mudar o cabeçalho **não** reescreve as linhas existentes.

- [ ] **Step 1: Testes.**
  - `New_sales_line_inherits_header_dims`.
  - `New_purchase_line_gets_usage_default`.
  - `Invalid_line_code_rejected_on_update`.
  - `Changing_header_does_not_touch_existing_lines`.
- [ ] **Step 2: Rodar.** Esperado: falha.
- [ ] **Step 3: Implementar a chamada nos serviços.** Construir um `previousSnapshot` com os 5 valores antes do PATCH, porque a entidade já vem rastreada (ver a armadilha do PATCH).
- [ ] **Step 4: Rodar a classe nova e as suítes `SalesInvoices` e `PurchaseInvoices`.** `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~Invoice"`. Esperado: PASS, sem regressão.
- [ ] **Step 5: Commit.** `feat(invoice): dimensões gerenciais nas notas de saída e entrada`.

### Task 7: Financeiro, natureza e parceiro

**Files:**
- Modify: `Services/Financials/FinancialDocumentsManualCreateService.cs`, `FinancialDocumentsManualUpdateService.cs` + `ManualFinancialDocumentChanges` (+`Dim1Code`…`Dim5Code`), `FinancialAdvancesCreateService.cs` (as duas rotas), `FinancialAccountEntriesCreateService.cs`; `UsageModel` + `UsageService` (create/update copiam os 5 campos); `BusinessPartnerModel` + `BusinessPartnerService` (`DefaultDimensionCode`, validado contra a dimensão de papel `Partner`)
- Test: `SiagroB1.Application.Tests/Dimensions/FinancialDimensionsTests.cs`, `UsagePartnerDimensionDefaultsTests.cs`

**Interfaces:**
- Título manual e adiantamento: `ApplyAsync(doc, null, doc.CardCode, null)` e depois validar.
- Lançamento na conta: só validar (sem parceiro nem natureza).
- Título nascido de nota/contrato (serviços de sincronização de firmes e provisórios): **sem mudança** neste sub-projeto. O spec diz que esses títulos herdam o cabeçalho da origem só como informação; essa cópia fica para o sub-projeto 6, que é quem a consome. Isso é uma decisão minha, registrada aqui para o revisor.
- Parceiro com `DefaultDimensionCode` quando não há dimensão de papel Partner: `"Nenhuma dimensão tem o papel Parceiro."`.

- [ ] **Step 1: Testes.**
  - `Manual_title_gets_partner_default`.
  - `Manual_title_update_validates_changed_dims`.
  - `Advance_without_contract_validates_dims`.
  - `Account_entry_rejects_invalid_code`.
  - `Usage_saves_five_defaults`.
  - `Partner_default_requires_partner_role_dimension`.
  - `Old_title_with_since_inactivated_center_can_still_be_updated` (Review Focus 2).
- [ ] **Step 2: Rodar.** Esperado: falha.
- [ ] **Step 3: Implementar.**
- [ ] **Step 4: Rodar as suítes `Financials`, `Usages` e `BusinessPartners` e a classe nova.** Esperado: PASS.
- [ ] **Step 5: Commit.** `feat(financial): dimensões nos títulos, lançamentos, natureza e parceiro`.

### Task 8: Parâmetros da empresa e obrigatoriedade

**Files:**
- Create: `Services/Dimensions/CompanySettingsService.cs`, `DimensionRequirementGuard.cs`, `SiagroB1.Web/Controllers/CompanySettingsController.cs` (GET e PATCH da linha única, chave 1)
- Modify: `SalesInvoicesConfirmService.cs` (junto do `usageGuard.ValidateAsync`, ~linha 199), `PurchaseInvoicesConfirmService.cs` (depois do `StorageCustodyGuard`, ~linha 75), e os serviços de create da Task 7
- Test: `SiagroB1.Application.Tests/Dimensions/DimensionRequirementGuardTests.cs`

**Interfaces:**
- `CompanySettingsService`: `Task<CompanySettings> GetAsync()` (cria a linha 1 se faltar) e `Task<CompanySettings> UpdateAsync(bool requireCostCenter, bool requireLedgerAccount)`.
- `DimensionRequirementGuard.EnsureAsync(IEnumerable<IDimensionFields> rows, Func<int,string>? rowLabel) : Task`.
  - Com `RequireCostCenter`, exige `Dim1Code`. Erro: `"Informe {Nome da dimensão 1} na linha {rowLabel(i)}."`.
  - Com `RequireLedgerAccount`, exige `LedgerAccountCode`. Erro: `"Informe a conta contábil na linha {rowLabel(i)}."`.
  - Para documento sem linhas (`rowLabel` nulo), a mensagem termina sem "na linha …".
  - Em SAPB1, não faz nada.

- [ ] **Step 1: Testes.**
  - `Blocks_sales_confirm_when_line_missing_cost_center`.
  - `Blocks_purchase_confirm_when_line_missing_ledger_account`.
  - `Both_flags_off_by_default_nothing_blocked`.
  - `Draft_save_is_not_blocked`.
  - `Already_confirmed_invoice_is_not_revalidated` (Review Focus 3): ligar a chave e depois estornar ou consultar a nota já confirmada não lança erro.
  - `Manual_title_create_blocked_when_required`.
  - `Noop_in_sapb1`.
- [ ] **Step 2: Rodar.** Esperado: falha.
- [ ] **Step 3: Implementar e plugar nos pontos listados.** O rótulo da linha de nota é `"{LineNum} ({ItemCode})"`.
- [ ] **Step 4: Rodar a suíte inteira.** `dotnet test SiagroB1.Application.Tests`. Esperado: PASS, com o mesmo número de testes de antes mais os novos.
- [ ] **Step 5: Commit.** `feat(financial): parâmetros da empresa exigindo centro de custo e conta contábil`.

### Task 9: Menus

**Files:**
- Create: migration `AddAccountingDimensionMenus` (CommonContext), no padrão de `20261021021429_AddFiscalStockAdjustmentAndClosingMenus.cs`

- [ ] **Step 1: Gerar e escrever a migration.**
  - Inserir em `MENU_ITEMS`, sob o pai de cadastros `registers`, logo depois de `costCenters`:
    - `accountingDimensions` / "Dimensões Gerenciais";
    - `distributionRules` / "Regras de Distribuição";
    - `companySettings` / "Parâmetros da Empresa".
  - Os três com `StandaloneOnly = true`, mais `ROLE_MENUS` para `ADMIN` e `UpdateData` deslocando o `Order` dos irmãos.
  - O `Down` remove as linhas.
- [ ] **Step 2: Aplicar numa cópia e conferir.** `dotnet build SiagroB1.sln`, depois `dotnet ef database update` nas duas contexts contra um banco **cópia** do CEAGUI dev (nunca no compartilhado; ver a memória do banco local com migration de outro branch). Esperado: as duas migrations aplicadas e `SELECT Dim1Code FROM SALES_INVOICE_ITEMS` com os valores antigos de `CostCenterCode`.
- [ ] **Step 3: Commit.** `feat(master-data): menus de dimensões e regras de distribuição` com o trailer `DB: AddAccountingDimensionMenus`.

### Task 10: Frontend — worktree, helper e value help

**Files:**
- Setup: `git -C ../siagro-b1-frontend worktree add -b feature/transport-managerial-accounting ../siagro-b1-frontend-transport main`, seguido de `yarn install` **próprio** no worktree (nunca junction de `node_modules`).
- Create: `webapp/helpers/DimensionHelpers.ts`, `webapp/dialogs/fragments/DimensionCodesSelectDialog.fragment.xml`, `webapp/view/common/fragments/DimensionFields.fragment.xml`, `webapp/test/unit/helpers/DimensionHelpers.qunit.ts`
- Modify: `webapp/Component.ts` (carrega as dimensões no JSONModel `dims` quando STANDALONE), `webapp/controller/common/CommonController.ts` (handler `onDimensionValueHelp`), `webapp/model/ServerRoutes.ts`, `i18n*.properties`

**Interfaces:**
- `DimensionHelpers`:
  - `buildDimsModel(rows: AccountingDimension[]): { items: {Number, Name, Active, Role}[] }`;
  - `isVisible(dims, n: number): boolean`;
  - `label(dims, n: number): string`.
- `DimensionFields.fragment.xml`: 5 pares `Label`/`Input`. O `visible` vem de `{dims>/items/N/Active}`, o rótulo de `{dims>/items/N/Name}` e o value help chama `onDimensionValueHelp` com `customData` `dim=N`. O fragmento é reutilizado com o binding relativo ao objeto do contexto (`Dim1Code`…).
- O value help filtra `DimensionCodes` por `DimensionNumber eq N and Inactive eq false` e mostra a coluna Tipo (Centro/Regra).

- [ ] **Step 1: Teste QUnit.** `DimensionHelpers.qunit.ts`: `isVisible` é falso para uma dimensão inativa, e `label` devolve o nome configurado.
- [ ] **Step 2: Rodar.** `yarn test-ui5` filtrado ou o runner de unidade. Esperado: falha.
- [ ] **Step 3: Implementar o helper, os fragmentos e o carregamento no Component.**
- [ ] **Step 4: Rodar `yarn ts-typecheck`, `yarn lint` e o teste.** Esperado: limpos e PASS. O gate de cobertura do `yarn test` nunca passa neste repo (memória); conferir só os testes.
- [ ] **Step 5: Commit.** `feat(master-data): componente de dimensões gerenciais`.

### Task 11: Frontend — telas de dimensões, regras e centro de custo

**Files:**
- Create: `webapp/view/accountingDimensions/Main.view.xml` + `controller/accountingDimensions/Main.controller.ts`; `webapp/view/distributionRules/{Main,Add,Edit}.view.xml` + controllers
- Modify: `webapp/manifest.json` (rotas `accountingDimensions` → `accounting-dimensions`, `distributionRules`/`distributionRulesNew`/`distributionRulesEdit` → `distribution-rules`, `/new`, `/{id}/edit`), `webapp/view/costCenters/*` (coluna e Select de Dimensão)

**Comportamento:**
- **Dimensões:** tabela das 5 linhas, com edição de Nome, Ativa e Papel (Select: Nenhum / Veículo / Parceiro). O Salvar faz o PATCH de cada dimensão alterada. A dimensão 1 tem o "Ativa" travado.
- **Parâmetros da Empresa:** página com dois Switch, "Exige centro de custo" e "Exige conta contábil", e Salvar fazendo PATCH em `CompanySettings(1)`. Rota `companySettings` → `company-settings`. Lembrar `targetType:'any'` nos booleanos com formatter (memória).
- **Regras:** lista com Código, Nome, Dimensão e Inativa. A edição tem o cabeçalho e uma tabela de versões (ValidFrom). Ao selecionar uma versão, aparecem as linhas: centro com value help filtrado pela dimensão da regra, percentual e total ao vivo. A versão vigente ou passada fica só leitura, e o botão "Nova versão" pede a data.
- **Centro de custo:** coluna Dimensão, mostrando o nome da dimensão, e Select no Add/Edit.

- [ ] **Step 1: Implementar as telas.**
- [ ] **Step 2: Rodar `yarn ts-typecheck` e `yarn lint`.** Esperado: limpos.
- [ ] **Step 3: Verificar no navegador** (stack local STANDALONE, perfil `ceagui`, sobre o banco cópia da Task 9). Partindo da home: abrir os dois menus, renomear a Dimensão 2 para "Veículo" com papel Veículo, criar os centros `CAM01` e `CAM02` na dim 2, criar a regra `FROTA` 50/50 e a versão futura, e conferir que a versão vigente fica travada.
- [ ] **Step 4: Commit.** `feat(master-data): telas de dimensões gerenciais e regras de distribuição`.

### Task 12: Frontend — dimensões nos documentos

**Files:**
- Modify:
  - `webapp/view/salesInvoices/fragments/ItemFiscalDialog.fragment.xml` e `Items.fragment.xml`: trocar `CostCenterCode` por `Dim1Code`, e incluir o fragmento de dimensões no diálogo do item e na seção do cabeçalho. O fragmento também tem o campo Conta contábil, com value help em `LedgerAccounts` filtrando ativas que aceitam lançamento;
  - os equivalentes em `purchaseInvoices/`;
  - `financialDocuments/fragments/Form.fragment.xml`;
  - `financialAccountMovements/fragments/AddEntryDialog.fragment.xml`;
  - `usages/fragments/*` (seção "Dimensões padrão");
  - `parceirosNegocio/fragments/*`: um campo "Padrão de {nome da dimensão Parceiro}", visível só se existe dimensão com esse papel;
  - os controllers correspondentes. Em `salesInvoices/{Add,Detail,BaseController}.controller.ts`, trocar `CostCenterCode` por `Dim1Code`. Incluir as 5 propriedades no `create()` inicial (memória: propriedade ausente estoura o binding).

- [ ] **Step 1: Implementar.** `grep -rn CostCenterCode webapp` tem que voltar vazio.
- [ ] **Step 2: Rodar `yarn ts-typecheck` e `yarn lint`.** Esperado: limpos.
- [ ] **Step 3: Verificar no navegador, pelo caminho do usuário.**
  - Natureza com Dim1 padrão: a linha nova da nota de entrada vem preenchida.
  - Cabeçalho da saída com Dim2 = `CAM01`: a linha nova herda.
  - Ligar "Exige centro de custo" nos Parâmetros da Empresa: confirmar uma nota sem Dim2 mostra "Informe {nome da dimensão 1} na linha …", e salvar o rascunho continua funcionando.
  - O título manual pega o padrão do parceiro.
  - Um lançamento na conta com código de outra dimensão é recusado.
  - Tirar prints de cada passo.
- [ ] **Step 4: Commit.** `feat(invoice): campos de dimensões nos documentos e cadastros`.

### Task 13: Verificação final

- [ ] **Step 1: Rodar a suíte completa do backend.** `dotnet test SiagroB1.sln`. Esperado: tudo PASS, número de testes ≥ o anterior mais os novos.
- [ ] **Step 2: Subir em SAPB1 (perfil `yktb`) e conferir.** Os menus novos não aparecem, e a nota de saída abre e confirma como antes, sem campo de dimensão. Derrubar a stack pelo PID.
- [ ] **Step 3: Atualizar a memória do projeto** (`transport-managerial-accounting-design.md`) com os commits e o estado.
