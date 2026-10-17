# Escolha da série na emissão própria de NF-e — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A filial cadastra várias séries de NF-e, cada uma com seu contador, e o usuário escolhe a série ao transmitir,
com a padrão já preenchida.

**Architecture:** Nova tabela filha `BRANCH_NFE_SERIES` (chave `BranchCode + Series`) guarda descrição e próximo número
por série. A série padrão fica em `BRANCH_NFE_SETTINGS.DefaultSeries`, a antiga coluna `Series` renomeada, o que garante
uma padrão só por construção. A reserva atômica do número passa a atuar sobre a linha da série. As actions "Emitir NF-e"
recebem `Series`. No frontend, um diálogo único com a lista de séries substitui o "confirmar" nos três pontos de emissão,
e a tela de Configuração da NF-e ganha uma tabela de séries gravada junto com o ambiente.

**Tech Stack:** .NET 10, EF Core (SQL Server), Dapper, OData v8, xUnit + EF InMemory / OpenUI5 + TypeScript, QUnit.

**Spec:** `docs/superpowers/specs/2026-10-17-nfe-series-selection-design.md` (neste repo, commit `9a7ba7c`).

**Worktrees (já criados):**
- backend: `C:\Projetos\SiagroB1\siagro-b1-backend-nfe-series`, branch `feature/nfe-series-selection`;
- frontend: `C:\Projetos\SiagroB1\siagro-b1-frontend-nfe-series`, branch `feature/nfe-series-selection`.

Os dois partem de `main`. Rode cada comando dentro do worktree do repo em questão. Nunca faça push nem merge.

## Global Constraints

- Só STANDALONE: o modo SAPB1 não muda. `BranchNfeSettingsService` já recusa fora do STANDALONE.
- Código, tabela e colunas em inglês. Textos para o usuário (mensagens, labels) em pt-BR.
- Série: inteiro de 0 a 999, única na filial. Próximo número: de 1 a 999.999.999. Descrição obrigatória, até 60
  caracteres.
- Exatamente uma série padrão por filial.
- "Não volta": com o mesmo ambiente gravado, o `NextNumber` de uma série existente não pode diminuir. Se o ambiente muda
  na mesma gravação, qualquer contador pode ser ajustado.
- Série usada não se exclui. "Usada" significa aparecer em `TaxDocumentSeries` de um documento da filial com
  `NfeEnvironment` preenchido: qualquer `SalesInvoice`, ou `PurchaseInvoice` com `IssuerType == DocumentIssuerType.Own`.
- A reserva continua atômica (`UPDLOCK, HOLDLOCK` + `OUTPUT deleted.NextNumber`), numa conexão Dapper própria e fora da
  transação do EF.
- Uma migration com dado nunca perde a numeração. Cada filial existente vira uma série com o `Series` e o `NextNumber`
  atuais e a descrição `Padrão`.
- O commit segue `CLAUDE.md` de cada repo: `tipo(escopo): …` em pt-BR, escopo `invoice`, trailer `DB: AddBranchNfeSeries`
  no commit com a migration, e as linhas de Co-Authored-By/Claude-Session da sessão.

## Review Focus

1. **Filial existente com numeração em andamento**: depois da migration, a primeira emissão sai com a série e o número que
   viriam antes. Coberto no Task 1, Step 5 (ordem do script SQL), e no Task 6, Steps 1 e 3 (dados migrados e emissão real).
2. **Retentativa após rejeição com troca de série**: o número reservado na série antiga é abandonado e a nova série
   reserva o seu. Teste `Retry_with_another_series_reserves_again_in_the_chosen_series` no Task 3.
3. **Série excluída na configuração enquanto um documento rejeitado ainda a referencia**: a exclusão é recusada, porque
   o documento já tem `NfeEnvironment`. Teste `Save_refuses_removing_a_series_used_by_a_rejected_document` no Task 2.
4. **Entrada de terceiro com a mesma série** (`IssuerType.ThirdParty`, `TaxDocumentSeries = "1"`, `NfeEnvironment`
   preenchido pela consulta do fornecedor): não conta como uso. Teste
   `Save_allows_removing_a_series_only_seen_on_third_party_entries` no Task 2.
5. **Série enviada pelo cliente que não existe na filial** (tela velha, cache ou outra filial): 400 com mensagem, sem
   reservar número. Teste `Issue_refuses_a_series_not_registered_for_the_branch` no Task 3.

---

## Backend (`siagro-b1-backend-nfe-series`)

### Task 1: Tabela de séries, migration e reserva por série

Comportamento visível sem mudança: a emissão continua usando a série padrão.

**Files:**
- Create: `SiagroB1.Domain/Entities/BranchNfeSeries.cs`
- Modify: `SiagroB1.Domain/Entities/BranchNfeSettings.cs` (renomear `Series` → `DefaultSeries`, remover `NextNumber`,
  adicionar a navegação `SeriesList`)
- Modify: `SiagroB1.Infra/Context/AppDbContext.cs` (`DbSet<BranchNfeSeries> BranchNfeSeries`, chave composta, FK com
  cascade)
- Create: `SiagroB1.Migrations/AppContext/<timestamp>_AddBranchNfeSeries.cs` (+ Designer, + snapshot)
- Modify: `SiagroB1.Application/Services/Nfe/NfeNumberReservationService.cs` (`ReserveAsync(string branchCode, int series)`)
- Modify: `SiagroB1.Application/Services/Nfe/NfeIssueServiceBase.cs` (`ReserveNumberAsync` usa a série recebida)
- Modify: `SiagroB1.Application/Services/Nfe/BranchNfeSettingsService.cs`, apenas para compilar: `ToModel` lê
  `DefaultSeries` e o `NextNumber` da série padrão. A regra nova é o Task 2.
- Modify: `SiagroB1.Application.Tests/Support/FakeNfeNumberReservationService.cs`,
  `SiagroB1.Application.Tests/Support/NfeTestSeed.cs`, e todo seed de teste que use `Series =`/`NextNumber =` em
  `BranchNfeSettings`. O compilador aponta quais.

**Interfaces:**
- Produces:
  - `public class BranchNfeSeries { string BranchCode (VARCHAR(14)); int Series; string Description (VARCHAR(60)); int NextNumber; BranchNfeSettings? Settings; }`,
    tabela `BRANCH_NFE_SERIES`, chave `(BranchCode, Series)`.
  - `BranchNfeSettings.DefaultSeries : int` (coluna renomeada de `Series`) e
    `BranchNfeSettings.SeriesList : ICollection<BranchNfeSeries>`.
  - `NfeNumberReservationService.ReserveAsync(string branchCode, int series) : Task<int>` (virtual).
  - `FakeNfeNumberReservationService.ReservedSeries : List<int>`: as séries pedidas, em ordem.
  - `NfeIssueServiceBase.ReserveNumberAsync(TDocument invoice, BranchNfeSettings settings, int series)` (privado).

- [ ] **Step 1: Ajustar o fake e o seed, e escrever o teste que falha.** Em `SalesInvoicesNfeIssueServiceTests` adicione
  `First_issue_reserves_in_the_default_series`: seed com `DefaultSeries = 1` e `SeriesList = [{1, "Padrão", 1}]`.
  `ExecuteAsync(key, "tester")` autorizada →
  `Assert.Equal([1], reservation.ReservedSeries); Assert.Equal("1", invoice.TaxDocumentSeries); Assert.Equal("000000001", invoice.TaxDocumentNumber);`.
- [ ] **Step 2: Rodar e ver falhar.**
  `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~SalesInvoicesNfeIssueServiceTests"`. Esperado:
  erro de compilação (`DefaultSeries`, `SeriesList`, `ReservedSeries` não existem).
- [ ] **Step 3: Implementar a entidade, o `DbContext`, a reserva e o `ReserveNumberAsync`.**
  - SQL da reserva:
    `UPDATE BRANCH_NFE_SERIES WITH (UPDLOCK, HOLDLOCK) SET NextNumber = NextNumber + 1 OUTPUT deleted.NextNumber WHERE BranchCode = @BranchCode AND Series = @Series;`.
  - Sem linha: `DefaultException($"A série {series} não está cadastrada na Configuração da NF-e da filial {branchCode}.")`.
  - No `ExecuteAsync`, por enquanto, `series = context.Settings.DefaultSeries`.
  - A comparação da retentativa usa `series.ToString(CultureInfo.InvariantCulture)` no lugar de `settings.Series`.
  - Os testes InMemory precisam de `SeriesList` no seed, porque o `ToModel` e o Task 3 leem dali.
- [ ] **Step 4: Gerar a migration.** Use
  `dotnet ef migrations add AddBranchNfeSeries --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web`.
  Esse comando não toca no banco. Depois **edite o `Up` à mão**, nesta ordem:
  1. `CreateTable BRANCH_NFE_SERIES`;
  2. `migrationBuilder.Sql("INSERT INTO BRANCH_NFE_SERIES (BranchCode, Series, Description, NextNumber) SELECT BranchCode, Series, 'Padrão', NextNumber FROM BRANCH_NFE_SETTINGS;")`;
  3. `RenameColumn Series → DefaultSeries`. Se o EF tiver gerado drop + add, troque por rename;
  4. `DropColumn NextNumber`.

  O `Down` faz o inverso: adiciona `NextNumber`, copia de volta o contador da série padrão
  (`UPDATE s SET NextNumber = x.NextNumber FROM BRANCH_NFE_SETTINGS s JOIN BRANCH_NFE_SERIES x ON x.BranchCode = s.BranchCode AND x.Series = s.DefaultSeries`),
  renomeia de volta e apaga a tabela.
- [ ] **Step 5: Conferir o SQL da migration.**
  `dotnet ef migrations script <migration anterior> AddBranchNfeSeries --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web`.
  Esperado: o `INSERT … SELECT` aparece antes do `sp_rename` e do `DROP COLUMN`. Aplicar no banco local fica para o Task 6.
- [ ] **Step 6: Rodar a suíte inteira.** `dotnet test SiagroB1.Application.Tests` e `dotnet test SiagroB1.Fiscal.Tests`.
  Esperado: tudo PASS, inclusive o teste novo.
- [ ] **Step 7: Commit** (`git add` dos arquivos novos e alterados). Mensagem:
  `feat(invoice): guardar as séries da NF-e da filial com contador próprio` e trailer `DB: AddBranchNfeSeries`.

### Task 2: Configuração da NF-e grava ambiente + lista de séries

**Files:**
- Create: `SiagroB1.Domain/Models/BranchNfeSeriesModel.cs`
- Modify: `SiagroB1.Domain/Models/BranchNfeSettingsModel.cs` (sai `Series`/`NextNumber`, entram `DefaultSeries` e
  `SeriesList`)
- Modify: `SiagroB1.Application/Services/Nfe/BranchNfeSettingsService.cs` (`GetAsync`, `SaveAsync`, `ToModel`)
- Modify: `SiagroB1.Web/Actions/Nfe/BranchNfeSettingsSaveController.cs`
- Modify: `SiagroB1.Web/ODataConfig/ODataConfigurations.cs` (parâmetros da `BranchNfeSettingsSave`)
- Test: `SiagroB1.Application.Tests/Nfe/BranchNfeSettingsServiceTests.cs`

**Interfaces:**
- Consumes: as entidades do Task 1.
- Produces:
  - `public class BranchNfeSeriesModel { int Series; string Description; int NextNumber; bool IsUsed; }`;
  - `BranchNfeSettingsModel.DefaultSeries : int` e `BranchNfeSettingsModel.SeriesList : List<BranchNfeSeriesModel>`,
    ordenada por `Series`;
  - `public record BranchNfeSeriesInput(int Series, string Description, int NextNumber)`, em `Domain/Models`;
  - `BranchNfeSettingsService.SaveAsync(string branchCode, NfeEnvironment environment, int defaultSeries, IReadOnlyList<BranchNfeSeriesInput> series, string userName) : Task<BranchNfeSettingsModel>`;
  - Action `BranchNfeSettingsSave` com os parâmetros `BranchCode: string`, `Environment: int`, `DefaultSeries: int` e as
    coleções paralelas `Series: int[]`, `Descriptions: string[]`, `NextNumbers: int[]`. Use o padrão de
    `CollectionParameter` + leitura como em `NfeReturnActionParameters`. Coleções de tamanhos diferentes devolvem 400
    `"As listas de séries chegaram incompletas."`.

- [ ] **Step 1: Testes que falham** (substituem os de `Series`/`NextNumber` únicos):
  - `Get_returns_one_default_series_when_not_configured`: `DefaultSeries == 1`, `SeriesList` = `[{1, "Padrão", 1, IsUsed: false}]`.
  - `Save_creates_and_updates_the_series_list`: grava `[1 "Venda" 10, 300 "Remessa" 1]` com padrão 300. A leitura traz
    as duas, em ordem, e `DefaultSeries == 300`.
  - `Save_refuses_lowering_next_number_in_the_same_environment`: grava `1 → 10`, depois `1 → 5`. Mensagem com
    `"O próximo número da série 1 não pode voltar"`.
  - `Save_allows_any_next_number_when_the_environment_changes`: grava em homologação `1 → 50`, depois produção `1 → 1`.
    Resultado: OK.
  - `Save_refuses_a_default_outside_the_list`: mensagem `"Escolha a série padrão entre as séries cadastradas."`.
  - `Save_refuses_duplicate_series`: `"A série 1 aparece mais de uma vez."`.
  - `Save_refuses_empty_list`: `"Cadastre ao menos uma série."`.
  - `Save_refuses_blank_description`: `"Informe a descrição da série 300."`.
  - `Save_refuses_series_out_of_range` (1000) e `Save_refuses_next_number_out_of_range` (0): mantêm as mensagens de hoje,
    `"A série da NF-e vai de 0 a 999."` e `"O próximo número da NF-e vai de 1 a 999.999.999."`.
  - `Save_refuses_removing_a_series_used_by_a_rejected_document`: `SalesInvoice` da filial com `TaxDocumentSeries = "300"`,
    `NfeStatus = Rejected` e `NfeEnvironment = Homologation`. Gravar sem a 300 dá
    `"A série 300 já foi usada em NF-e e não pode ser excluída."`.
  - `Save_allows_removing_a_series_only_seen_on_third_party_entries`: `PurchaseInvoice` `IssuerType = ThirdParty` com
    série 300 e ambiente preenchido. A exclusão é aceita.
  - `Get_marks_used_series`: `IsUsed == true` só na série usada.
- [ ] **Step 2: Rodar e ver falhar.**
  `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~BranchNfeSettingsServiceTests"`.
- [ ] **Step 3: Implementar `SaveAsync`.**
  - Validações na ordem dos testes.
  - A regra do "não volta" compara `settings.Environment` **gravado** com o recebido, por série existente.
  - Sincroniza `SeriesList`: atualiza as existentes, adiciona as novas e remove as ausentes depois de conferir o uso.
  - A descrição é gravada com `Trim()` e tem no máximo 60 caracteres.
  - `IsUsed` usa a regra da Global Constraints, numa consulta por filial
    (`Select(i => i.TaxDocumentSeries).Distinct()` nos dois tipos de documento).
- [ ] **Step 4: Controller e OData**, como descrito em Interfaces.
- [ ] **Step 5: Rodar a suíte.** `dotnet test SiagroB1.Application.Tests`. Esperado: PASS.
- [ ] **Step 6: Commit.** `feat(invoice): cadastrar várias séries na Configuração da NF-e`.

### Task 3: "Emitir NF-e" recebe a série escolhida

**Files:**
- Modify: `SiagroB1.Application/Services/Nfe/NfeIssueServiceBase.cs` (`ExecuteAsync`)
- Modify: `SiagroB1.Application/Services/Nfe/NfeReadinessValidator.cs`
- Modify: `SiagroB1.Web/Actions/Nfe/SalesInvoicesIssueNfeController.cs`,
  `SiagroB1.Web/Actions/Nfe/PurchaseInvoicesIssueNfeController.cs`
- Modify: `SiagroB1.Web/ODataConfig/ODataConfigurations.cs` (`Parameter<int>("Series")` nas duas actions de emissão)
- Modify: `SiagroB1.Domain/Dtos/ShipmentLoadPendingRefusalReturnDto.cs` +
  `SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadsPendingRefusalService.cs` (adicionar `BranchCode`)
- Test: `SiagroB1.Application.Tests/Nfe/SalesInvoicesNfeIssueServiceTests.cs`,
  `SiagroB1.Application.Tests/Nfe/PurchaseInvoicesNfeIssueServiceTests.cs`

**Interfaces:**
- Consumes: `ReserveAsync(branchCode, series)` e `SeriesList` (Task 1).
- Produces:
  - `NfeIssueServiceBase.ExecuteAsync(Guid key, string userName, int? series = null)`. `null` significa a série padrão
    e existe para os testes e chamadas internas. Os controllers **exigem** `Series`.
  - Action `SalesInvoicesIssueNfe` / `PurchaseInvoicesIssueNfe`: `{ Key: Guid, Series: int }`. Sem `Series`, devolve 400
    `"Parâmetros obrigatórios: Key, Series."`.
  - `ShipmentLoadPendingRefusalReturnDto.BranchCode : string?` com `[JsonPropertyName("BranchCode")]`, a filial do
    documento de saída da devolução.

- [ ] **Step 1: Testes que falham** (seed com as séries 1, padrão, e 300):
  - `Issue_with_chosen_series_reserves_and_stamps_it`: `ExecuteAsync(key, "t", 300)` →
    `ReservedSeries == [300]` e `TaxDocumentSeries == "300"`.
  - `Issue_refuses_a_series_not_registered_for_the_branch`: série 55 → `DefaultException` com
    `"A série 55 não está cadastrada na Configuração da NF-e da filial 01."` e `reservation.Calls == 0`.
  - `Retry_with_same_series_reuses_the_number`: primeira tentativa rejeitada na validação local, retentativa na mesma
    série → `reservation.Calls == 1`.
  - `Retry_with_another_series_reserves_again_in_the_chosen_series`: rejeitada na série 1, retentativa na 300 →
    `ReservedSeries == [1, 300]` e `TaxDocumentSeries == "300"`.
  - Repita o primeiro e o segundo teste em `PurchaseInvoicesNfeIssueServiceTests`.
  - Na prontidão: `Readiness_reports_missing_series` com filial sem `SeriesList` → problema
    `"Nenhuma série cadastrada (Configuração da NF-e)"`.
- [ ] **Step 2: Rodar e ver falhar.** `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~NfeIssueService"`.
- [ ] **Step 3: Implementar.** Resolver `series ?? context.Settings.DefaultSeries` e conferir que a série existe em
  `BranchNfeSeries` da filial **antes** do `OpenAsync` e da reserva. A prontidão inclui `SeriesList` na leitura da
  configuração. Controllers e OData como em Interfaces. Preencher o `BranchCode` no DTO da recusa.
- [ ] **Step 4: Rodar a suíte.** `dotnet test SiagroB1.Application.Tests` e `dotnet build SiagroB1.sln`. Esperado: PASS e
  build sem erro.
- [ ] **Step 5: Commit.** `feat(invoice): escolher a série ao emitir a NF-e própria`.

---

## Frontend (`siagro-b1-frontend-nfe-series`)

### Task 4: Diálogo de série nos três pontos de emissão

**Files:**
- Create: `webapp/helpers/NfeSeriesHelpers.ts`
- Create: `webapp/dialogs/NfeIssueDialog.ts`
- Create: `webapp/test/unit/helpers/NfeSeriesHelpers.qunit.ts`, registrado em `webapp/test/unit/unitTests.qunit.ts`
- Modify: `webapp/controller/salesInvoices/Detail.controller.ts` (`onIssueNfe`)
- Modify: `webapp/controller/purchaseInvoices/Detail.controller.ts` (`onIssueNfe`)
- Modify: `webapp/controller/shipmentLoads/Detail.controller.ts` (`onIssueRefusalNfe`, `PendingRefusalRow.BranchCode`)

**Interfaces:**
- Consumes: `BranchNfeSettingsGet(BranchCode='…')` → `{ DefaultSeries, SeriesList: [{Series, Description, NextNumber, IsUsed}] }`;
  as actions de emissão com `Series` (Task 3).
- Produces:
  - `export type NfeSeries = { Series: number; Description: string; NextNumber: number; IsUsed: boolean }`;
  - `formatNfeSeries(s: NfeSeries): string`, que devolve `"300 - Remessa para armazém de terceiro"`;
  - `pickInitialSeries(list: NfeSeries[], defaultSeries: number, documentSeries?: string | null): number`, que devolve
    a série do documento se ela estiver na lista, senão a padrão;
  - `openNfeIssueDialog(owner: Control, opts: { title: string; message: string; confirmText: string; branchCode: string; documentSeries?: string | null }): Promise<number | null>`.
    A função lê a configuração com `sendJson` e mostra um `Select` com as séries pré-selecionadas por
    `pickInitialSeries` e o texto de `message`. Se a leitura falhar, mostra `MessageBox.error` e resolve `null`. Cancelar
    também resolve `null`. Siga o padrão de `NfeCancelDialog.ts`: controles criados em código, `destroy` no
    `afterClose`.

- [ ] **Step 1: Testes QUnit que falham:**
  - `formatNfeSeries` → `"300 - Remessa"`;
  - `pickInitialSeries` com documento `"300"` presente → `300`; com documento `"55"` ausente → padrão; com `null` →
    padrão; com documento `"001"` → `1` (compara pelo número).
- [ ] **Step 2: Rodar e ver falhar.** `yarn test`. A memória do projeto avisa que o gate de cobertura nunca passa:
  confira só os resultados do módulo novo no relatório do runner.
- [ ] **Step 3: Implementar os helpers e o diálogo.** Nos três controllers, troque o `confirmDialog` pelo
  `openNfeIssueDialog`, mantendo o texto e o verbo atuais em `title`/`message`/`confirmText`. Passe
  `branchCode = ctx.getProperty("BranchCode")` (na carga, `row.BranchCode`) e
  `documentSeries = TaxDocumentSeries`. Envie `{ Series }` no `extra` do `runNfeAction` (na carga, no `runRefusalNfeAction`).
- [ ] **Step 4: Rodar as verificações.** `npm run ts-typecheck`, `npm run lint` e o QUnit novo. Esperado: sem erros e os
  testes novos PASS.
- [ ] **Step 5: Commit.** `feat(invoice): escolher a série ao emitir a NF-e`.

### Task 5: Tabela de séries na Configuração da NF-e

**Files:**
- Modify: `webapp/view/nfeSettings/Main.view.xml`
- Modify: `webapp/controller/nfeSettings/Main.controller.ts`
- Modify: `webapp/helpers/NfeSeriesHelpers.ts` (+ testes)

**Interfaces:**
- Consumes: `BranchNfeSettingsSave` com `DefaultSeries`, `Series[]`, `Descriptions[]` e `NextNumbers[]` (Task 2).
- Produces: `buildSeriesSavePayload(rows: NfeSeriesRow[]): { Series: number[]; Descriptions: string[]; NextNumbers: number[] }`,
  em que `NfeSeriesRow = NfeSeries & { isNew: boolean }`.

- [ ] **Step 1: Teste QUnit que falha.** `buildSeriesSavePayload` converte as strings dos inputs em números e faz `trim`
  nas descrições, mantendo a ordem.
- [ ] **Step 2: Implementar a tela.**
  - Os campos "Série" e "Próximo número" do formulário saem. Abaixo do formulário entra um `sap.m.Table` ligado a
    `nfe>/SeriesList`, com as colunas:
    - **Série**: `Input` habilitado só quando `isNew`;
    - **Descrição**: `Input`, `maxLength` 60;
    - **Próximo número**: `Input type="Number"`;
    - **Padrão**: `RadioButton` do grupo `nfeDefaultSeries`, com `selected` ligado a uma comparação com `nfe>/DefaultSeries`;
      ao selecionar, gravar `DefaultSeries`;
    - **ação de excluir**: `Button` desabilitado quando `IsUsed` ou quando a linha é a padrão.
  - A toolbar da tabela tem "Adicionar série", que cria `{ Series: "", Description: "", NextNumber: 1, IsUsed: false, isNew: true }`.
  - O `onSave` envia `Environment`, `DefaultSeries` e o payload. As mensagens de validação vêm do servidor
    (`MessageBox.error`).
- [ ] **Step 3: Rodar as verificações.** `npm run ts-typecheck`, `npm run lint` e o QUnit. Esperado: sem erros.
- [ ] **Step 4: Commit.** `feat(invoice): tabela de séries na Configuração da NF-e`.

---

### Task 6: Verificação ponta a ponta (sem commit de código)

- [ ] **Step 1: Aplicar a migration no banco local de desenvolvimento** com `ASPNETCORE_ENVIRONMENT` explícito
  (memória: o profile `db-migration` aponta para produção, nunca use). Confira `SELECT * FROM BRANCH_NFE_SERIES`: há uma
  linha "Padrão" por filial configurada, com o `NextNumber` anterior.
- [ ] **Step 2: Subir a stack local** (memória "Subir a stack local") a partir dos dois worktrees.
- [ ] **Step 3: Pelo caminho do usuário, a partir da home:**
  1. Em Configuração da NF-e, cadastrar a série 300 "Remessa", gravar e reabrir. A lista persiste.
  2. Tentar baixar o próximo número da série 1 e ver a mensagem de recusa.
  3. Em um Documento de Saída confirmado, em homologação, emitir: o diálogo vem com a série 1. Trocar para 300 e emitir.
     O documento mostra a série 300 e o número 1 dela.
  4. Tentar excluir a 300: o botão fica desabilitado.
  5. Repetir o diálogo no Documento de Entrada e na recusa da Montagem de Carga, pelo menos até a abertura do diálogo
     com a série certa.
- [ ] **Step 4: Derrubar a stack** pelos PIDs (memória "Subagente mata só pelo PID").
- [ ] **Step 5:** relatar o resultado ao usuário, com o que não pôde ser verificado. A SEFAZ de homologação pode estar
  fora do ar.
