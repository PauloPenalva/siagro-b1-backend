# Contribuição social do produtor rural pelo parceiro — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A retenção do Funrural passa a exigir que natureza (modo) e parceiro (produtor rural PF/segurado
especial) concordem, com alíquotas numa tabela global por vigência em vez de na natureza.

**Architecture:** Três peças novas de dados:
- quatro colunas no parceiro;
- um enum de modo na natureza, no lugar de três alíquotas;
- a tabela `RURAL_CONTRIBUTION_RATES` (molde de `IBS_CBS_RATES`).

Uma função pura em `SiagroB1.Fiscal` decide as alíquotas da linha. O `PurchaseInvoicesTaxApplyService`
a chama no lugar de ler `usage.Funrural*Rate`. Cálculo, colunas da linha, texto do `infCpl`, títulos e
relatório não mudam.

**Tech Stack:** .NET 10, EF Core (SQL Server), OData, xUnit + EF InMemory; OpenUI5 + TypeScript no frontend.

**Spec:** `docs/superpowers/specs/2026-10-18-rural-contribution-by-partner-design.md` (leia antes).

**Repositórios:**
- **Backend:** worktree `C:\Projetos\SiagroB1\siagro-b1-backend-rural-contribution`, branch
  `feature/rural-contribution-by-partner`.
- **Frontend:** criar o worktree irmão `C:\Projetos\SiagroB1\siagro-b1-frontend-rural-contribution`
  a partir da `main`, na branch de mesmo nome (Task 6).
- Nunca push; merge na `main` é do usuário.

## Global Constraints

- Somente STANDALONE: o backend usa `ErpMode.IsStandalone` e o gate `TaxCalculationGate`; o frontend
  usa `ui>/standalone` (parceiro) e `ui>/identityEditable` (natureza).
- Identificadores em inglês; textos de tela e mensagens de erro em pt-BR (CLAUDE.md).
- Commits no formato `tipo(escopo): descrição pt-BR`; migration exige trailer `DB: <NomeDaMigration>`.
  Todo arquivo novo recebe `git add` logo após ser criado.
- Alíquotas `DECIMAL(7,4)` em percentual (1,32 = 1,32 %); `DateOnly` em coluna `DATE`.
- O arredondamento do `FunruralCalculator` (AwayFromZero) não muda.
- Documentos não-Pendentes e NF-e Autorizada/Cancelada/Processando não são recalculados. Isso já é
  garantido em `ApplyAsync` e não pode regredir.

## Review Focus

1. **Parceiro produtor gravado sem tipo** (`IsRuralProducer = true`, `RuralProducerType = null`):
   precisa ser recusado no cadastro. Se escapar, a linha não retém, sem aviso. Teste na Task 1.
2. **Desmarcar "produtor rural"** precisa limpar tipo, CPF e opção pela folha; senão o R-2055 futuro
   lê lixo. Teste na Task 1.
3. **Natureza de saída com modo diferente de `None`** vindo pela API: o mapper força `None`. Teste na Task 3.
4. **Devolução própria de uma compra feita antes da mudança** precisa continuar copiando as alíquotas
   da linha de origem, sem olhar o parceiro atual (spec 4.4, regra 2). Teste na Task 4.
5. **Natureza com modo, documento CT-e/custódia/complemento de ICMS:** o complemento de ICMS já não
   recebe Funrural. CT-e e custódia só ficam livres pelo modo `None`. Teste na Task 4: natureza `None`
   → zero.

---

### Task 1: Parceiro de negócio — campos de produtor rural

**Files:**
- Create: `SiagroB1.Domain/Enums/RuralProducerType.cs`
- Create: `SiagroB1.Domain/Shared/BrazilianTaxId.cs` (só se não existir validador de CPF; procure antes `IsValidCpf` e similares em `SiagroB1.Domain`, `SiagroB1.Commons` e `SiagroB1.Fiscal`)
- Modify: `SiagroB1.Domain/Entities/BusinessPartner.cs` (após `PaymentConditionCode`)
- Modify: `SiagroB1.Domain/Models/BusinessPartnerModel.cs`
- Modify: `SiagroB1.Application/Services/BusinessPartnerService.cs`: projeções de `GetByIdAsync` (`:27-46`) e `QueryAll` (`:252-269`), `CreateAsync` (`:79-106`), `UpdateAsync` (`:139-161`) e validação nova ao lado de `ValidateNfeFields` (`:333`)
- Test: `SiagroB1.Application.Tests/BusinessPartners/BusinessPartnerRuralProducerTests.cs`

**Interfaces:**
- Produces:
  - `enum RuralProducerType { Individual = 1, SpecialInsured = 2, LegalEntity = 3 }`.
  - Na entidade e no model:
    - `bool IsRuralProducer`;
    - `RuralProducerType? RuralProducerType`;
    - `string? RuralProducerCpf` (`VARCHAR(11)`);
    - `bool RuralPayrollOption`.
  - `static bool BrazilianTaxId.IsValidCpf(string? digits)`, se for criado.
- O serviço SAP (`Services/SAP/BusinessPartnerService.cs`) não muda; os campos ficam no default.

- [ ] **Step 1: Escrever os testes que falham** (usar `BusinessPartnerService` real sobre `TestDb`, como os testes existentes do parceiro):
  - `Rural_producer_requires_a_type`: `IsRuralProducer = true`, tipo nulo → `DefaultException` "Informe o tipo de produtor rural.".
  - `Individual_producer_with_cnpj_requires_a_valid_cpf`:
    - `TaxId = "11222333000181"`, tipo `Individual`, CPF nulo → "Informe o CPF do produtor rural.";
    - CPF `"12345678900"` → "CPF do produtor rural inválido.".
  - `Individual_producer_with_cpf_tax_id_does_not_need_a_second_cpf`: `TaxId = "52998224725"`, CPF nulo → grava.
  - `Legal_entity_producer_drops_cpf_and_payroll_option`: tipo `LegalEntity`, CPF `"52998224725"`, `RuralPayrollOption = true` → relido com CPF nulo e `RuralPayrollOption = false`.
  - `Clearing_rural_producer_clears_type_cpf_and_payroll` (Review Focus 2): update com `IsRuralProducer = false` → tipo nulo, CPF nulo, folha `false`.
  - `Rural_fields_round_trip_through_get_and_query`: create e depois `GetByIdAsync` e `QueryAll()` devolvem os 4 campos.
- [ ] **Step 2:** `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~BusinessPartnerRuralProducerTests"`. Esperado: FAIL (compilação).
- [ ] **Step 3: Implementar**
  - O CPF é gravado só com dígitos. A normalização que limpa os campos (não produtor → tudo limpo; PJ → CPF nulo e folha `false`) roda em Create e Update **antes** de validar.
  - O CPF é obrigatório quando: produtor, tipo ≠ `LegalEntity`, e o `TaxId` (só dígitos) tem 14 posições.
- [ ] **Step 4:** Rodar o filtro do Step 2. Esperado: PASS.
- [ ] **Step 5: Commit**: `feat(partner): marca de produtor rural no parceiro STANDALONE`.

### Task 2: Tabela de alíquotas da contribuição social

**Files:**
- Create: `SiagroB1.Domain/Enums/RuralContributionScheme.cs`
- Create: `SiagroB1.Domain/Entities/RuralContributionRate.cs`
- Create: `SiagroB1.Application/Services/Taxes/RuralContributionRatesService.cs`
- Create: `SiagroB1.Web/Controllers/RuralContributionRatesController.cs`
- Modify: `SiagroB1.Infra/Context/AppDbContext.cs:46`, `SiagroB1.Web/ODataConfig/ODataConfigurations.cs:60`, `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs:356`
- Test: `SiagroB1.Application.Tests/Taxes/RuralContributionRatesServiceTests.cs`; acrescentar o entity set em `Taxes/TaxationEdmModelTests.cs`

**Interfaces:**
- Produces:
  - `enum RuralContributionScheme { Standard = 1, PayrollOption = 2 }`.
  - Entidade `RuralContributionRate`, tabela `RURAL_CONTRIBUTION_RATES`, com:
    - `int Key` identity;
    - `DateOnly StartDate` (`DATE`);
    - `RuralContributionScheme Scheme`;
    - `decimal InssRate`, `RatRate`, `SenarRate` (`DECIMAL(7,4)`);
    - `[Index(nameof(Scheme), nameof(StartDate), IsUnique = true)]`.
  - `DbSet<RuralContributionRate> RuralContributionRates`.
  - Entity set OData `RuralContributionRates`.
  - `class RuralContributionRatesService(IUnitOfWork db)` com:
    - `QueryAll()`, `GetByIdAsync(int)`, `CreateAsync`, `UpdateAsync(int, …)`, `DeleteAsync(int)`;
    - `Task<RuralContributionRate?> GetEffectiveAsync(RuralContributionScheme scheme, DateOnly date)`.
- Copie `IbsCbsRatesService`/`IbsCbsRatesController` (incluindo `BadRequest` fora do STANDALONE com
  "As alíquotas de contribuição social só existem no modo STANDALONE.").

- [ ] **Step 1: Testes que falham**
  - `Effective_rate_is_the_latest_start_date_of_the_same_scheme`: Standard 2025-01-01 e 2026-01-01, PayrollOption 2026-06-01. `GetEffectiveAsync(Standard, 2026-03-01)` → a de 2026-01-01; `GetEffectiveAsync(PayrollOption, 2026-03-01)` → null.
  - `Rate_outside_0_100_is_refused`: "Alíquota de INSS deve estar entre 0 e 100." (idem RAT e SENAR).
  - `Same_scheme_and_start_date_is_refused`: "Já existe uma vigência de contribuição social (Comercialização) iniciando em 01/01/2026." O texto do regime é "Comercialização" para `Standard` e "Optante pela folha" para `PayrollOption`.
  - `Same_start_date_in_another_scheme_is_accepted`.
  - No EDM: `RuralContributionRates.StartDate` é `Edm.Date`.
- [ ] **Step 2:** `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~RuralContributionRates|FullyQualifiedName~TaxationEdmModelTests"`. Esperado: FAIL.
- [ ] **Step 3:** Implementar seguindo o molde de `IbsCbsRatesService`, com unicidade por (`Scheme`, `StartDate`).
- [ ] **Step 4:** Rodar o filtro do Step 2. Esperado: PASS.
- [ ] **Step 5: Commit**: `feat(invoice): tabela de alíquotas da contribuição social do produtor rural` (sem migration ainda; ela vem na Task 5).

### Task 3: Natureza — modo no lugar das alíquotas

**Files:**
- Create: `SiagroB1.Domain/Enums/RuralContributionMode.cs`
- Modify:
  - `SiagroB1.Domain/Entities/Usage.cs:108-112`: troca as 3 alíquotas por `RuralContributionMode RuralContributionMode`, com comentário apontando a spec de 18/10.
  - `SiagroB1.Domain/Models/UsageModel.cs:65-68`.
  - `SiagroB1.Application/Services/UsageService.cs:66-68` (projeção).
  - `SiagroB1.Application/Services/Taxes/UsageTaxationMapper.cs:62-64`: `usage.RuralContributionMode = incoming ? model.RuralContributionMode : RuralContributionMode.None`.
  - `SiagroB1.Fiscal/Taxes/UsageTaxationValidator.cs:45-47`: remove as 3 `ValidatePercent` e recusa valor fora do enum com "Modo de contribuição social inválido.".
- Test:
  - Substituir `SiagroB1.Application.Tests/Usages/UsageFunruralTests.cs` por `UsageRuralContributionTests.cs`.
  - Ajustar `SiagroB1.Fiscal.Tests/Taxes/UsageTaxationValidatorTests.cs:240-254`.

**Interfaces:**
- Produces: `enum RuralContributionMode { None = 0, Full = 1, SenarOnly = 2 }`; `UsageModel.RuralContributionMode` (não anulável, default `None`).

- [ ] **Step 1: Testes que falham**
  - `Incoming_usage_round_trips_the_contribution_mode`: `SenarOnly` gravado e relido.
  - `Outgoing_usage_always_stores_none` (Review Focus 3): direção `Outgoing` com `Full` → relido `None`.
  - No validador: `Unknown_contribution_mode_is_refused`, com `(RuralContributionMode)9`.
- [ ] **Step 2:** `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~UsageRuralContributionTests"` e `dotnet test SiagroB1.Fiscal.Tests --filter "FullyQualifiedName~UsageTaxationValidatorTests"`. Esperado: FAIL.
- [ ] **Step 3: Implementar.**
  - `PurchaseInvoicesTaxApplyService.cs:177-178` vai parar de compilar.
  - Para não quebrar o build entre tasks, troque essa linha por `PurchaseInvoiceFunrural.Clear(item);` com o comentário `// Task 4: regra do parceiro`. A Task 4 substitui.
  - Os testes de `PurchaseInvoicesFunruralTests` e `PurchaseInvoicesFunruralReturnTests` que semeiam alíquotas na natureza deixam de compilar. Marque-os com `[Fact(Skip = "Task 4")]` só até a Task 4.
- [ ] **Step 4:** Rodar os dois filtros do Step 2 e `dotnet build SiagroB1.sln`. Esperado: PASS e build limpo.
- [ ] **Step 5: Commit**: `refactor(invoice): natureza diz só o modo da contribuição social`.

### Task 4: Regra da linha — natureza × parceiro × tabela

**Files:**
- Create: `SiagroB1.Fiscal/Taxes/RuralContributionRule.cs`
- Modify:
  - `SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoicesTaxApplyService.cs`: construtor (`:19-24`); bloco `else` em `:175-179`; carga do parceiro uma vez por documento, junto de `supplierState` (`:115`).
  - `SiagroB1.Application.Tests/Support/TaxTestServices.cs:40-43`: o construtor ganha `new RuralContributionRatesService(db)`.
  - `SiagroB1.Application.Tests/Support/FakeBusinessPartnerService.cs`: parâmetro novo
    `Dictionary<string, (RuralProducerType Type, bool Payroll)>? ruralProducers = null`, refletido no
    `GetByIdAsync` (`IsRuralProducer`, `RuralProducerType`, `RuralPayrollOption`).
  - `SiagroB1.Application.Tests/Support/PurchaseNfeTestSeed.cs:19-22`: `Partners()` passa a registrar
    `Supplier` como `(Individual, false)`. Acrescentar `PartnersAs(RuralProducerType? type, bool payroll = false)`;
    `null` = não produtor. O seed também grava em `RuralContributionRates`: Standard 1,32/0,11/0,20 e
    PayrollOption 0/0/0,20, ambos desde 2000-01-01.
- Test:
  - `SiagroB1.Fiscal.Tests/Taxes/RuralContributionRuleTests.cs` (novo);
  - reescrever `PurchaseInvoicesFunruralTests.cs` e `Nfe/PurchaseInvoicesFunruralReturnTests.cs` para modo + parceiro, tirando os `Skip` da Task 3.

**Interfaces:**
- Consumes:
  - Task 1: `RuralProducerType` e os campos do `BusinessPartnerModel`.
  - Task 2: `RuralContributionRatesService.GetEffectiveAsync`, `RuralContributionScheme`.
  - Task 3: `RuralContributionMode`.
- Produces (Fiscal, pura, sem I/O):
  ```csharp
  public readonly record struct RuralContributionRates(decimal Inss, decimal Rat, decimal Senar);
  public static class RuralContributionRule
  {
      /// null = a linha não retém (Clear). Lança DefaultException só quando falta a vigência.
      public static RuralContributionRates? Resolve(
          RuralContributionMode mode, bool isRuralProducer, RuralProducerType? producerType, bool payrollOption,
          RuralContributionRates? standard, RuralContributionRates? payroll, DateOnly rateDate);
      public static RuralContributionScheme? SchemeFor(RuralContributionMode mode, bool payrollOption);
  }
  ```
  Regras da spec 4.4:
  - modo `None`, não produtor ou `LegalEntity` → `null`;
  - `Full` → `payroll` se `payrollOption`, senão `standard`;
  - `SenarOnly` → `(0, 0, standard.Senar)`;
  - a vigência necessária ausente → "Cadastre as alíquotas de contribuição social vigentes em dd/MM/yyyy.".
  - `SchemeFor` diz qual vigência carregar (`null` quando nenhuma), para o serviço não ler à toa.
- No serviço: devolução própria continua em `:171-173`, sem mudança. Na entrada:
  `RuralContributionRule.Resolve(...)` → `PurchaseInvoiceFunrural.Apply(item, r.Inss, r.Rat, r.Senar)`
  ou `PurchaseInvoiceFunrural.Clear(item)`. A data é `rateDate` (já calculada em `:137-139`).

- [ ] **Step 1: Testes puros que falham** (`RuralContributionRuleTests`, `[Theory]` com uma linha por regra da spec 4.4):

  | Modo | Produtor | Tipo | Folha | Resultado |
  |---|---|---|---|---|
  | Full | sim | Individual | não | (1,32; 0,11; 0,20) |
  | Full | sim | SpecialInsured | não | standard |
  | Full | sim | Individual | sim | (0; 0; 0,20) |
  | SenarOnly | sim | Individual | sim | (0; 0; 0,20) |
  | Full | sim | LegalEntity | — | null |
  | SenarOnly | sim | LegalEntity | — | null |
  | Full | não | — | — | null |
  | None | sim | Individual | — | null |
  | Full | sim | Individual | sim, sem payroll | DefaultException com a data `01/10/2026` |
  | SenarOnly | sim | Individual | — | standard ausente → exceção |
- [ ] **Step 2:** `dotnet test SiagroB1.Fiscal.Tests --filter "FullyQualifiedName~RuralContributionRuleTests"`. Esperado: FAIL.
- [ ] **Step 3:** Implementar `RuralContributionRule`. Rodar o filtro do Step 2. Esperado: PASS.
- [ ] **Step 4: Reescrever os testes de integração** (via `PurchaseInvoicesCreateService`, mesmos valores de hoje: base 1500 → INSS 19,80, RAT 1,65, SENAR 3,00).
  - `Own_entry_from_individual_producer_withholds_standard_rates`: modo `Full`, parceiro `Individual` → (19,80; 1,65; 3,00), `TotalFunrural` 24,45, `NetPayable` 1475,55.
  - `Senar_only_nature_withholds_only_senar`: `SenarOnly` → (0; 0; 3,00).
  - `Payroll_option_producer_withholds_only_senar`: `Full` + `PartnersAs(Individual, payroll: true)` → (0; 0; 3,00).
  - `Legal_entity_producer_is_not_withheld`: `PartnersAs(LegalEntity)` → `FunruralValue` 0.
  - `Supplier_not_marked_as_producer_is_not_withheld`: `PartnersAs(null)` → 0, sem exceção.
  - `Nature_none_leaves_the_line_without_funrural` (Review Focus 5).
  - Manter, adaptados: `Third_party_entry_is_withheld_too` (agora `Full` + PF: 19,80/1,65/3,00), `Funrural_in_the_body_is_discarded_when_the_rule_is_inactive`, `Confirmed_document_keeps_its_funrural`.
  - `PurchaseInvoicesFunruralReturnTests`: a compra nasce com `Full`/`SenarOnly` + parceiro PF. Novo teste `Return_copies_origin_rates_even_after_partner_changes` (Review Focus 4): depois da compra, o parceiro vira `LegalEntity`; a devolução ainda retém as alíquotas da linha de origem.
- [ ] **Step 5:** `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~Funrural"`. Esperado: PASS, sem `Skip` restante (`grep -rn "Task 4" SiagroB1.Application.Tests` vazio).
- [ ] **Step 6: Commit**: `feat(invoice): Funrural pela natureza e pelo cadastro do produtor rural`.

### Task 5: Migration com seed e backfill

**Files:**
- Create (gerado): `SiagroB1.Migrations/AppContext/<timestamp>_ReplaceUsageFunruralRatesWithContributionMode.cs` e o snapshot atualizado.

- [ ] **Step 1:** Gerar a migration:
  `dotnet ef migrations add ReplaceUsageFunruralRatesWithContributionMode --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web`.
- [ ] **Step 2: Editar o `Up`** para que a ordem fique:
  1. criar `RURAL_CONTRIBUTION_RATES` e o índice;
  2. adicionar as 4 colunas do parceiro (`IsRuralProducer`/`RuralPayrollOption` NOT NULL default 0) e `USAGES.RuralContributionMode` INT NOT NULL default 0;
  3. **antes** de dropar as alíquotas:
     ```sql
     UPDATE USAGES SET RuralContributionMode = CASE
       WHEN ISNULL(FunruralInssRate,0) > 0 OR ISNULL(FunruralRatRate,0) > 0 THEN 1
       WHEN ISNULL(FunruralSenarRate,0) > 0 THEN 2 ELSE 0 END;
     ```
  4. seed idempotente:
     ```sql
     IF NOT EXISTS (SELECT 1 FROM RURAL_CONTRIBUTION_RATES WHERE Scheme = 1 AND StartDate = '2000-01-01')
       INSERT INTO RURAL_CONTRIBUTION_RATES (StartDate, Scheme, InssRate, RatRate, SenarRate)
       VALUES ('2000-01-01', 1, 1.32, 0.11, 0.20);
     ```
     e o mesmo para `Scheme = 2` com `(0, 0, 0.20)`;
  5. dropar as 3 colunas da `USAGES`.

  O `Down` recria as 3 colunas nulas e desfaz o resto. O backfill reverso fica de fora, porque a informação de PJ se perdeu de propósito.
- [ ] **Step 3:** `dotnet build SiagroB1.sln` e `dotnet ef migrations script <migration anterior> ReplaceUsageFunruralRatesWithContributionMode --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web`. Conferir no SQL que o `UPDATE` vem antes dos `DROP COLUMN` e que o seed está lá.
- [ ] **Step 4: Commit** (com trailer):
  `feat(invoice): migration da contribuição social do produtor rural` + corpo explicando o backfill + `DB: ReplaceUsageFunruralRatesWithContributionMode`.

### Task 6: Frontend — parceiro, natureza e tela de alíquotas

**Files** (worktree `siagro-b1-frontend-rural-contribution`, criado com `git -C C:/Projetos/SiagroB1/siagro-b1-frontend worktree add -b feature/rural-contribution-by-partner ../siagro-b1-frontend-rural-contribution main`):
- **Parceiro:**
  - Modify `webapp/view/parceirosNegocio/fragments/Form.fragment.xml` (bloco STANDALONE `:63-101`): CheckBox "Produtor rural", Select "Tipo de produtor" (Pessoa física / Segurado especial / Pessoa jurídica), Input "CPF do produtor" e CheckBox "Optante pela contribuição sobre a folha", todos com `visible="{ui>/standalone}"`. Os três últimos ficam habilitados só com "Produtor rural" marcado.
  - Modify `webapp/controller/parceirosNegocio/Add.controller.ts:32-45`: os 4 campos no payload do `create()` (`IsRuralProducer: false`, `RuralProducerType: null`, `RuralProducerCpf: null`, `RuralPayrollOption: false`).
- **Natureza:**
  - Modify `webapp/view/usages/fragments/Taxation.fragment.xml:141-149`: título "Contribuição Social"; as 3 entradas viram um Select "Contribuição social" com os itens "Não incide" (0), "Contribuição completa (INSS + RAT + SENAR)" (1) e "Somente SENAR" (2).
  - Modify `webapp/controller/usages/Add.controller.ts:83-85`: `RuralContributionMode: 0` no lugar dos 3 nulos.
- **Tela de alíquotas** (copiar `view/ibsCbsRates/*` e `controller/ibsCbsRates/Main.controller.ts`):
  - Create `webapp/view/ruralContributionRates/Main.view.xml`, `fragments/RateDialog.fragment.xml` e `webapp/controller/ruralContributionRates/Main.controller.ts`.
  - Colunas: Início da vigência, Regime (Comercialização / Optante pela folha), INSS %, RAT %, SENAR %.
  - Entity set `/RuralContributionRates`.
- **Botão e rota:**
  - Modify `webapp/view/usages/Main.view.xml:201`: botão "Alíquotas de contribuição social" ao lado do de IBS/CBS, `visible="{ui>/identityEditable}"`.
  - Modify `webapp/controller/usages/Main.controller.ts:66`: `navTo("ruralContributionRates")`.
  - Modify `webapp/manifest.json` (`:831-835` e `:2080-2085`): rota `usages/rural-contribution-rates` e target.

- [ ] **Step 1:** Fazer as alterações acima. Os textos ficam em pt-BR direto no XML, como o restante do app.
- [ ] **Step 2:** `npm run ts-typecheck` e `npm run lint`. Esperado: sem erros.
- [ ] **Step 3:** Commit no repositório do frontend: `feat(partner): produtor rural, modo da contribuição social e tela de alíquotas`.

### Task 7: Verificação final

- [ ] **Step 1:** `dotnet test SiagroB1.Fiscal.Tests` e `dotnet test SiagroB1.Application.Tests`. Esperado: tudo verde. Compare com a contagem da `main` se alguma falha pré-existente aparecer.
- [ ] **Step 2:** `grep -rn "FunruralInssRate\|FunruralRatRate\|FunruralSenarRate" SiagroB1.Domain/Models SiagroB1.Domain/Entities/Usage.cs SiagroB1.Application/Services/Taxes SiagroB1.Application/Services/UsageService.cs`. Esperado: vazio. Os campos de mesmo nome na **linha** (`PurchaseInvoiceItem`) continuam existindo.
- [ ] **Step 3: Verificação pelo caminho do usuário** (subir a stack com o profile `ceagui`, conforme a memória "Subir a stack local", e derrubar ao final):
  - cadastrar um parceiro PF produtor com CNPJ + CPF;
  - cadastrar uma natureza "Contribuição completa";
  - lançar uma entrada e ver INSS/RAT/SENAR;
  - trocar o parceiro para PJ e ver zero.
- [ ] **Step 4:** Commitar só se algo mudou na verificação. Avisar o usuário do checklist de implantação da spec 4.5 (marcar produtores na CEAGUI antes de implantar).
