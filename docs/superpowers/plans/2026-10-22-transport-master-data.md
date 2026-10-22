# Cadastros de transporte (sub-projeto 2) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** O que o CT-e e o MDF-e vão escolher:
- veículo com tipo tração/reboque e os dados fiscais;
- conjunto (tração + até 3 reboques);
- motorista com CPF no código e CNH;
- configuração de transporte da filial (RNTRC, seguro padrão, AT&M com senha cifrada e teste de
  conexão).

**Architecture:**
- **Backend:** amplia `Truck`/`TruckDriver`, que já existem e são usados pela balança, carga e notas,
  com colunas aditivas. Cria `TruckCombination` (+ linhas) e `BranchTransportSettings`, só
  STANDALONE.
- **Configuração de transporte:** segue o molde de `BranchNfeSettings`/`EmailSettings` — função de
  leitura + ações por atributo, e senha cifrada com `CertificatePasswordCipher`.
- **Frontend:** amplia as telas `veiculo`/`motorista`, filtra o `TrucksSelectDialog` compartilhado
  para só tração ativa cria a tela `truckCombinations` e a seção "Transportadora" na filial.

**Tech Stack:** .NET 10, EF Core (SQL Server), OData v4, xUnit + EF InMemory; OpenUI5 1.141 + TypeScript.

**Spec:** `docs/superpowers/specs/2026-10-22-transport-master-data-design.md` (worktree backend
`siagro-b1-backend-transport-md`, branch `feature/transport-master-data`).

**Worktrees:**
- **Backend:** `C:\Projetos\SiagroB1\siagro-b1-backend-transport-md`, já criado.
- **Frontend:** criar o irmão `C:\Projetos\SiagroB1\siagro-b1-frontend-transport-md` na mesma branch,
  a partir de `main`.
  - Rode `yarn install` próprio. **Nunca** faça junction de `node_modules` (ver memória
    *worktree remove segue junction*).
  - Copie `SiagroB1.Reports/wwwroot` do principal se for rodar testes de relatório no backend.

## Global Constraints

- **Idioma:** identificadores em inglês; textos de tela e mensagens de erro em pt-BR.
- **Migrations aditivas:**
  - colunas novas anuláveis, exceto `Kind` e `OwnerType`, que são NOT NULL com default;
  - aplicar em **todas as empresas** da COMMON local (o Web recusa subir com migration pendente);
  - o commit leva o trailer `DB: <NomeDaMigration>`.
- **STANDALONE x SAPB1:**
  - `Truck` e `TruckDriver` existem nos dois modos;
  - `TruckCombination`, `BranchTransportSettings` e os campos de proprietário terceiro do veículo
    só em STANDALONE (`ErpMode.IsStandalone(configuration)`);
  - SAPB1 recusa com `"... só existe no modo STANDALONE."`.
- **Nenhum campo novo é exigido no cadastro.** A obrigatoriedade para a emissão é do sub-projeto 3/4.
  Exceções:
  - CPF válido na **inclusão** de motorista;
  - regras de coerência listadas nas tarefas.
- **Rodado (tpRod):** 01 Truck, 02 Toco, 03 Cavalo mecânico, 04 VAN, 05 Utilitário, 06 Outros.
- **Carroceria (tpCar):** 00 Não aplicável, 01 Aberta, 02 Fechada/Baú, 03 Graneleira,
  04 Porta-container, 05 Sider.
- **Tipo de proprietário (tpProp):** 0 TAC Agregado, 1 TAC Independente, 2 Outros.
- **Responsável pelo seguro:** 1 Emitente, 2 Contratante.
- **Conjunto:** no máximo **3 reboques**.
- **A senha da AT&M nunca sai pela API.** O modelo devolve só `HasAverbaPassword`.
- **Commits:**
  - mensagem `tipo(escopo): ...` em pt-BR, escopo `master-data`;
  - `git add` de todo arquivo novo logo ao criar;
  - nunca push.

## Review Focus

1. **Placa antiga com `Kind` default.** Um veículo já cadastrado (sem tipo) continua aparecendo
   no value help da balança. Teste na Tarefa 1: a linha semeada sem informar `Kind` lê `Traction`.
2. **Reboque escolhido como tração do conjunto, ou tração como reboque.** Recusa com mensagem
   clara. Testes na Tarefa 3.
3. **Salvar a configuração com a senha vazia** mantém a senha gravada. Senha nova troca a cifra.
   Testes na Tarefa 4.
4. **Motorista antigo com código fora do padrão de CPF** continua editável (o PATCH não valida o
   CPF). Teste na Tarefa 2.
5. **Inativar um veículo usado em conjunto ativo** é permitido, mas o conjunto não aceita gravar
   tração ou reboque inativos. Teste na Tarefa 3.

---

### Task 1: Enums e colunas novas em `Truck` e `TruckDriver` + migration

**Files:**
- Create: `SiagroB1.Domain/Enums/TruckKind.cs`, `TruckWheelType.cs`, `TruckBodyType.cs`, `TruckOwnerType.cs`, `TruckOwnerKind.cs`
- Modify: `SiagroB1.Domain/Entities/Truck.cs`, `SiagroB1.Domain/Entities/TruckDriver.cs`
- Create: migration `AddTransportMasterData` em `SiagroB1.Migrations/AppContext/`
- Test: `SiagroB1.Application.Tests/Trucks/TruckModelTests.cs`

**Interfaces:**
- Produces:
  - **enums** (valores numéricos = código oficial; nomes em inglês):
    - `TruckKind { Traction = 0, Trailer = 1 }`;
    - `TruckWheelType { Truck = 1, Stump = 2, TractorUnit = 3, Van = 4, Utility = 5, Other = 6 }`;
    - `TruckBodyType { NotApplicable = 0, Open = 1, ClosedBox = 2, Bulk = 3, Container = 4, Sider = 5 }`;
    - `TruckOwnerType { Own = 0, ThirdParty = 1 }`;
    - `TruckOwnerKind { TacAggregate = 0, TacIndependent = 1, Other = 2 }`.
  - **`Truck` ganha:**
    - `string? Renavam` (VARCHAR(11));
    - `TruckKind Kind = Traction` (NOT NULL default 0);
    - `TruckWheelType? WheelType`;
    - `TruckBodyType? BodyType`;
    - `int? CapacityKg`;
    - `int? CapacityM3`;
    - `TruckOwnerType OwnerType = Own` (NOT NULL default 0);
    - `string? OwnerCardCode` (VARCHAR(15), sem navegação);
    - `string? OwnerRntrc` (VARCHAR(8));
    - `TruckOwnerKind? OwnerKind`;
    - `bool Inactive` (NOT NULL default 0).
  - **`TruckDriver` ganha:**
    - `string? LicenseNumber` (VARCHAR(11));
    - `string? LicenseCategory` (VARCHAR(2));
    - `DateTime? LicenseExpiresOn` (DATE);
    - `bool Inactive`.

- [ ] **Step 1: Teste que falha** — `TruckModelTests`:
  - `New_truck_defaults_to_own_traction_active()`: `new Truck { Code = "ABC1D23" }` →
    `Kind == Traction`, `OwnerType == Own`, `Inactive == false`.
  - `Seeded_truck_without_kind_reads_traction()`: grava `Truck` pelo `TestDb.CreateUnitOfWork()`
    sem informar os campos novos e relê → `Kind == Traction`.
- [ ] **Step 2:** `dotnet test SiagroB1.Application.Tests --filter FullyQualifiedName~TruckModelTests`.
  Esperado: FAIL (não compila).
- [ ] **Step 3: Implementar enums e propriedades.**
  - Enums gravados como **int** (padrão das outras enums de `Truck`/NF-e).
  - `[Column(TypeName = ...)]` como nos campos vizinhos.
  - Comentário `///` em pt-BR citando o código oficial (tpRod/tpCar/tpProp).
- [ ] **Step 4:** teste PASS.
- [ ] **Step 5: Gerar a migration.**
  - Comando: `dotnet ef migrations add AddTransportMasterData --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web`.
  - Conferir que `Kind`, `OwnerType` e `Inactive` têm `defaultValue: 0` / `false`.
  - Conferir que nenhum `DropTable`/`AlterColumn` destrutivo foi gerado.
- [ ] **Step 6: Aplicar a migration.**
  - Aplicar com `ASPNETCORE_ENVIRONMENT` **explícito** em todas as empresas locais da COMMON:
    CEAGUI_SIAGRO_DEV, LMCEREAIS_SIAGRO_DEV e IDX_SIAGRO_DEV (ver memória
    *db-migration profile é perigoso*).
  - Antes, conferir em `__EFMigrationsHistory` que não há migration de outro branch (ver memória
    *Banco local tem migration de outro branch*).
- [ ] **Step 7:** `dotnet build SiagroB1.sln` sem erro e commit
  `feat(master-data): tipo, rodado, carroceria, capacidade e proprietário no veículo; CNH no motorista`
  com `DB: AddTransportMasterData`.

### Task 2: Regras de gravação do veículo e do motorista

**Files:**
- Modify: `SiagroB1.Application/Services/TruckService.cs`, `SiagroB1.Application/Services/TruckDriverService.cs`
- Create: `SiagroB1.Application/Services/TruckRules.cs`
- Test: `SiagroB1.Application.Tests/Trucks/TruckServiceTests.cs` (amplia), `SiagroB1.Application.Tests/Trucks/TruckDriverServiceTests.cs` (novo)

**Interfaces:**
- Consumes: Task 1; `TaxIdValidator.Normalize/IsValid` (`SiagroB1.Commons/Validation`).
- Produces:
  - `static class TruckRules { static void Validate(Truck truck, bool standalone) }`, que lança
    `DefaultException`;
  - `TruckService` e `TruckDriverService` passam a receber `IConfiguration` (para o modo).
    Atualize o registro em `ServiceCollectionExtensions` se a assinatura do construtor exigir.

**Regras de `TruckRules.Validate`** (create **e** update):
- **Reboque com rodado:** `Kind == Trailer && WheelType != null` →
  `"Reboque não tem tipo de rodado: o rodado é só do veículo de tração."`
- **Capacidade:** `CapacityKg < 0 || CapacityM3 < 0 || TareWeight < 0` →
  `"Tara e capacidade não podem ser negativas."`
- **RENAVAM:** preenchido e diferente de 9 a 11 dígitos →
  `"O RENAVAM tem de 9 a 11 dígitos."`
- **Proprietário terceiro em STANDALONE:** `OwnerType == ThirdParty` sem `OwnerCardCode` →
  `"Informe o parceiro proprietário do veículo de terceiro."`
- **RNTRC do proprietário:** `OwnerRntrc` preenchido sem ser 8 dígitos →
  `"O RNTRC tem 8 dígitos."`
- **SAPB1:** os campos de proprietário são zerados antes de gravar (`OwnerType = Own`,
  `OwnerCardCode/OwnerRntrc/OwnerKind = null`), sem erro.

**Regras de `TruckDriverService`:**
- **Inclusão:** normaliza `Code` com `TaxIdValidator.Normalize`. Se não tiver 11 dígitos ou não
  passar no `IsValid` → `"Informe um CPF válido no código do motorista."`
- **Edição:** não revalida o código (registros antigos).
- **Categoria da CNH:** só aceita A, B, C, D, E, AB, AC, AD, AE (maiúsculas); qualquer outro valor
  → `"Categoria da CNH inválida."`

- [ ] **Step 1: Testes que falham.**
  - `TruckServiceTests`:
    - `Trailer_with_wheel_type_is_rejected`;
    - `Negative_capacity_is_rejected`;
    - `Third_party_without_owner_is_rejected_in_standalone`;
    - `Third_party_fields_are_cleared_in_sapb1` (`TestConfiguration.Erp("SAPB1")`, relê e espera
      `OwnerType == Own` e `OwnerCardCode == null`);
    - `Legacy_truck_without_new_fields_updates`.
  - `TruckDriverServiceTests`:
    - `Create_with_invalid_cpf_is_rejected` (`"12345678900"`);
    - `Create_normalizes_masked_cpf` (`"529.982.247-25"` → `Code == "52998224725"`);
    - `Update_keeps_legacy_non_cpf_code` (semeia `Code = "MOT01"` e edita o nome sem erro);
    - `Invalid_license_category_is_rejected`.
- [ ] **Step 2:** rodar com `--filter "FullyQualifiedName~Trucks"`. Esperado: FAIL.
- [ ] **Step 3:** implementar `TruckRules` e as chamadas nos serviços.
- [ ] **Step 4:** PASS. Rodar também `TruckPlateRulesTests` (sem regressão).
- [ ] **Step 5:** commit `feat(master-data): regras de gravação do veículo e CPF do motorista`.

### Task 3: Conjunto (`TruckCombination`) — entidade, serviço, OData, migrations

**Files:**
- Create: `SiagroB1.Domain/Entities/TruckCombination.cs`, `SiagroB1.Domain/Entities/TruckCombinationTrailer.cs`
- Create: `SiagroB1.Domain/Interfaces/ITruckCombinationService.cs`, `SiagroB1.Application/Services/TruckCombinationService.cs`
- Create: `SiagroB1.Web/Controllers/TruckCombinationsController.cs`
- Modify: `SiagroB1.Infra/Context/AppDbContext.cs`, `SiagroB1.Web/ODataConfig/ODataConfigurations.cs`, `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs`
- Create: migrations `AddTruckCombinations` (App) e `AddTransportMasterDataMenus` (Common)
- Test: `SiagroB1.Application.Tests/Trucks/TruckCombinationServiceTests.cs`

**Interfaces:**
- Consumes: Task 1 (`Truck.Kind`, `Truck.Inactive`).
- Produces:
  - **`TruckCombination`** (tabela `TRUCK_COMBINATIONS`):
    - `string Code` (PK VARCHAR(20));
    - `string Description` (VARCHAR(100));
    - `string TractionTruckCode` (FK `TRUCKS`, Restrict);
    - `string? DefaultDriverCode` (FK `TRUCK_DRIVERS`, Restrict);
    - `bool Inactive`;
    - `ICollection<TruckCombinationTrailer> Trailers`.
  - **`TruckCombinationTrailer`** (tabela `TRUCK_COMBINATION_TRAILERS`):
    - `int Id`;
    - `string CombinationCode` (FK Cascade);
    - `int Position` (1–3);
    - `string TrailerTruckCode` (FK `TRUCKS`, Restrict);
    - índice único `(CombinationCode, TrailerTruckCode)`.
  - **Entidade OData:** `TruckCombinations` com `Trailers` expansível.
    - O controller herda o padrão de `TrucksController` (`ODataBaseController<TruckCombination, string>`).
    - Todas as ações recusam em SAPB1 (400, texto `"O conjunto só existe no modo STANDALONE."`).
  - **Rota/menu:** `MENU_ITEMS` com key `truckCombinations`, título `"Conjuntos"`,
    `StandaloneOnly = true`, mesmo `ParentKey` do item de menu da tela `veiculo` (consultar
    `MENU_ITEMS` local), ordem logo após ele, e `ROLE_MENUS` para ADMIN.
  - A seção "Transportadora" fica na tela da filial (Task 9): **sem** item de menu para ela.
  - **Ids de `ROLE_MENUS`:** GUIDs fixos no código da migration, como em `AddStockMenu`.

**Regras do serviço** (create e update; lança `DefaultException`):
- **Tração inexistente ou que não é tração:** `"Escolha um veículo de tração para o conjunto."`
- **Reboque inexistente ou que não é reboque:** `"O veículo {placa} não é reboque."`
- **Mais de 3 reboques:** `"O conjunto aceita no máximo 3 reboques."`
- **Reboque repetido:** `"O reboque {placa} aparece mais de uma vez."`
- **Tração ou reboque inativo** (só ao gravar): `"O veículo {placa} está inativo."`
- **`Position`:** renumerada 1..n na ordem recebida.
- **Mesmo reboque em vários conjuntos:** permitido (spec).
- **Update:** substitui a lista de reboques. Cuidado com a entidade já rastreada no PATCH (ver
  memória *PATCH entrega entidade já rastreada*). Prefira uma ação `TruckCombinationsSave` se o
  PATCH profundo complicar, com a mesma decisão para create.

- [ ] **Step 1: Testes que falham.**
  - `Combination_requires_traction`;
  - `Trailer_must_be_trailer_kind`;
  - `More_than_three_trailers_is_rejected`;
  - `Duplicate_trailer_is_rejected`;
  - `Inactive_vehicle_is_rejected`;
  - `Same_trailer_in_two_combinations_is_allowed`;
  - `Update_replaces_trailers_and_renumbers_positions`;
  - `Rejected_in_sapb1`.
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** implementar entidade, configuração EF, serviço, controller, EDM e registro DI.
- [ ] **Step 4:** PASS.
- [ ] **Step 5: Migrations.**
  - Gerar `AddTruckCombinations` (App) e `AddTransportMasterDataMenus` (Common, `CommonDbContext`).
  - Aplicar nas bases locais como na Tarefa 1.
  - Se a EDM tiver teste de modelo (`*EdmModelTests`), acrescentar `TruckCombinations` no mesmo
    padrão.
- [ ] **Step 6:** commit `feat(master-data): conjunto de veículos (tração + reboques)` com
  `DB: AddTruckCombinations, AddTransportMasterDataMenus`.

### Task 4: Configuração de transporte da filial + cliente AT&M

**Files:**
- Create: `SiagroB1.Domain/Entities/BranchTransportSettings.cs`, `SiagroB1.Domain/Enums/InsuranceResponsible.cs`, `SiagroB1.Domain/Models/BranchTransportSettingsModel.cs`
- Create: `SiagroB1.Application/Services/Transport/BranchTransportSettingsService.cs`, `SiagroB1.Application/Services/Transport/AverbaClient.cs` (+ `IAverbaClient`), `SiagroB1.Application/Services/Transport/AverbaOptions.cs`
- Create: `SiagroB1.Web/Controllers/BranchTransportSettingsController.cs`
- Modify: `AppDbContext`, `ODataConfigurations` (função + ações), `ServiceCollectionExtensions`, `SiagroB1.Web/appsettings.json` (seção `Averba`)
- Create: migration `AddBranchTransportSettings` (App)
- Test: `SiagroB1.Application.Tests/Transport/BranchTransportSettingsServiceTests.cs`, `SiagroB1.Application.Tests/Transport/AverbaClientTests.cs`

**Interfaces:**
- Consumes: `CertificatePasswordCipher` (como em `EmailSettingsService.Cipher()`, chave
  `Nfe:CertificateKey`); `StubHttpMessageHandler` (testes).
- Produces:
  - **Entidade `BranchTransportSettings`** (tabela `BRANCH_TRANSPORT_SETTINGS`, PK `BranchCode`
    VARCHAR(14), FK `BRANCHS`):
    - `string? CompanyRntrc` (VARCHAR(8));
    - `InsuranceResponsible? InsuranceResponsible` (`Issuer = 1, Contractor = 2`);
    - `string? InsurerTaxId` (VARCHAR(14));
    - `string? InsurerName` (VARCHAR(115));
    - `string? PolicyNumber` (VARCHAR(20));
    - `bool AverbaEnabled`;
    - `string? AverbaUser` (VARCHAR(100));
    - `byte[]? AverbaPasswordCipher` (VARBINARY(512));
    - `string? AverbaCode` (VARCHAR(20));
    - `NfeEnvironment AverbaEnvironment` (reaproveita o enum de ambiente);
    - `DateTime? UpdatedAt`;
    - `string? UpdatedBy`.
  - **`BranchTransportSettingsModel`:** os mesmos campos, **sem** a cifra, mais
    `bool HasAverbaPassword` e `string? AverbaPassword` (só entrada).
  - **`BranchTransportSettingsService`:**
    - `Task<BranchTransportSettingsModel> GetAsync(string branchCode)` — linha vazia se não
      existir;
    - `Task<BranchTransportSettingsModel> SaveAsync(string branchCode, BranchTransportSettingsModel model, string userName)`;
    - `Task<AverbaTestResult> TestAverbaAsync(string branchCode, string? user, string? password, string? code, NfeEnvironment environment)`.
  - **`IAverbaClient`:** `Task<AverbaLoginResult> LoginAsync(NfeEnvironment env, string user, string password, string code, CancellationToken ct)`.
    - `POST {baseUrl}/rest/Auth` com JSON `{ "usuario": user, "senha": password, "codatm": code }`.
    - Sucesso = resposta com `Bearer` preenchido; erro = `Erros.Erro[0].Descricao`.
    - Confirmar os nomes exatos dos campos em
      `C:\Projetos\EfisCloud\backend\src\main\java\br\com\idxtec\efis\cloud\services\atm\request\AtmLoginRequest.java`
      e `response/AtmLoginResponse.java` antes de escrever o DTO.
    - O cliente fica pronto para o sub-projeto 3 acrescentar `/rest/CTE`.
  - **`AverbaOptions`:** seção `Averba` com `ProductionUrl` e `HomologationUrl`.
    - Pegar a URL de produção de `idxtec.atm.url` nos `application-*.properties` do EfisCloud.
    - Se não estiver versionada, deixar as duas vazias no appsettings e o serviço responde
      `"Configure Averba:ProductionUrl/HomologationUrl no servidor."`.
  - **Rotas por atributo** (molde `EmailSettingsController`, guarda STANDALONE + papel ADMIN):
    - `GET odata/BranchTransportSettingsGet(BranchCode='x')` — copiar o formato de
      `BranchNfeSettingsGet`;
    - `POST odata/BranchTransportSettingsSave`;
    - `POST odata/BranchTransportSettingsTestAverba`.

**Regras do `SaveAsync`:**
- **RNTRC:** `CompanyRntrc` preenchido com 8 dígitos; senão `"O RNTRC tem 8 dígitos."`
- **CNPJ da seguradora:** `InsurerTaxId` preenchido precisa ser CNPJ válido (`TaxIdValidator`);
  senão `"CNPJ da seguradora inválido."`
- **Averbação ligada:** `AverbaEnabled` exige usuário e código, e senha gravada ou nova; senão
  `"Para averbar, informe usuário, senha e código da AT&M."`
- **Senha:** `AverbaPassword` vazia/nula **mantém** a cifra atual; preenchida troca a cifra.

**Regras do `TestAverbaAsync`:**
- Usa a senha recebida; se vier vazia, usa a gravada (decifrada).
- Nunca grava nada.
- Devolve `AverbaTestResult { bool Success; string Message }`. Sucesso:
  `"Conexão com a AT&M OK."`; falha: a descrição da AT&M ou do erro HTTP.

- [ ] **Step 1: Testes que falham.**
  - **Serviço:**
    - `Get_returns_empty_settings_when_missing`;
    - `Save_encrypts_password_and_never_returns_it` (o modelo devolve `HasAverbaPassword == true`
      e `AverbaPassword == null`);
    - `Save_with_empty_password_keeps_stored_cipher`;
    - `Invalid_rntrc_is_rejected`;
    - `Invalid_insurer_taxid_is_rejected`;
    - `Enabled_without_credentials_is_rejected`;
    - `Test_uses_stored_password_when_none_typed` (`IAverbaClient` fake registra a senha
      recebida);
    - `Rejected_in_sapb1`.
  - **Cliente** (`StubHttpMessageHandler`):
    - `Login_success_reads_bearer`;
    - `Login_error_reads_description`;
    - `Login_posts_to_homologation_url_when_env_is_homologation`.
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** implementar entidade, enum, modelo, serviço, cliente (`AddHttpClient<IAverbaClient, AverbaClient>`), controller, EDM e DI.
- [ ] **Step 4:** PASS.
- [ ] **Step 5:** gerar e aplicar `AddBranchTransportSettings` nas bases locais.
- [ ] **Step 6:** commit `feat(master-data): configuração de transporte da filial com seguro e AT&M` com `DB: AddBranchTransportSettings`.

### Task 5: Frontend — tela de Veículos

**Files:**
- Modify: `webapp/view/veiculo/*.view.xml`, `webapp/view/veiculo/fragments/*`, `webapp/controller/veiculo/*.ts`
- Modify: `webapp/model/formatter.ts` (textos dos enums)

**Interfaces:**
- Consumes: campos da Task 1 via `/Trucks`.
- Produces: `formatter.truckKindText`, `truckWheelTypeText`, `truckBodyTypeText`, `truckOwnerKindText` (enum → texto pt-BR das tabelas da Global Constraints).

**O que muda:**
- **Lista:** colunas Placa, Tipo (Tração/Reboque), Rodado, Carroceria, Tara, Capacidade (kg) e
  Inativo; filtro por Tipo e por Inativo.
- **Formulário (Add/Edit), em seções:**
  - **Identificação:** placa, modelo, RENAVAM, UF, cidade, inativo.
  - **Características:**
    - tipo;
    - rodado (oculto e limpo quando o tipo é Reboque);
    - carroceria;
    - tara, capacidade em kg e capacidade em m³.
  - **Proprietário:** `visible="{ui>/standalone}"`.
    - Próprio ou terceiro.
    - Parceiro, pelo value help de parceiros existente, mais RNTRC e tipo de proprietário,
      visíveis só quando é terceiro.
- **Selects de enum:** itens fixos de `JSONModel` carregados **antes** do `selectedKey` (memória
  *Select com selectedKey de JSONModel*).
- **Valores numéricos editáveis:** `Double`/`Int32` como os campos existentes (memória
  *Decimal editável usa Double*).
- **Erro do backend** (`DefaultException` → 400): exibido no `MessageBox` como as outras telas.

- [ ] **Step 1:** implementar.
- [ ] **Step 2:** `yarn ts-typecheck` e `yarn lint` sem erro novo.
- [ ] **Step 3:** commit `feat(master-data): tela de veículos com tipo, rodado, carroceria e proprietário`.

### Task 6: Frontend — tela de Motoristas

**Files:**
- Modify: `webapp/view/motorista/*`, `webapp/controller/motorista/*`, `webapp/model/formatter.ts`
- Test: `webapp/test/unit/model/formatter.qunit.ts` (ou o arquivo de testes do formatter que já existir)

**Interfaces:**
- Produces: `formatter.licenseState(expiresOn: string | Date | null, today?: Date): "Error" | "Warning" | "None"`.
  - Vencida → `Error`.
  - Vence em até 30 dias → `Warning`.
  - Senão → `None`.

**O que muda:**
- **Rótulo:** o campo Código passa a chamar **"CPF"**, com máscara livre (o backend normaliza) e
  editável só na inclusão.
- **Documento:** rótulo `"Documento (RG/CNH)"`.
- **Campos novos:** número da CNH, categoria (Select A, B, C, D, E, AB, AC, AD, AE), validade
  da CNH e inativo.
- **Lista:** a validade da CNH sai com `ObjectStatus` e `state` vindo de `licenseState`.

- [ ] **Step 1: Teste QUnit que falha.**
  - `licenseState` de ontem → `"Error"`;
  - `licenseState` de hoje+10 → `"Warning"`;
  - `licenseState` de hoje+31 → `"None"`;
  - `licenseState(null)` → `"None"`.
- [ ] **Step 2:** implementar o formatter e as telas.
- [ ] **Step 3:** rodar o QUnit no navegador (`/test/Test.qunit.html?...&test=unit/unitTests`). `yarn test` inteiro não passa por causa do gate de cobertura (memória). Rodar `yarn ts-typecheck` e `yarn lint`.
- [ ] **Step 4:** commit `feat(master-data): CPF e CNH no cadastro de motoristas`.

### Task 7: Frontend — value help de veículo só com tração ativa

**Files:**
- Modify: `webapp/dialogs/fragments/TrucksSelectDialog.fragment.xml`

**O que muda:**
- **Filtro:** no binding `items`, acrescentar
  `parameters: { $filter: "Kind eq SiagroB1.Domain.Enums.TruckKind'Traction' and Inactive eq false" }`.
  - O filtro de enum precisa de `$filter` estático; ver a memória *Filtro de enum precisa de
    $filter estático*.
  - Confirmar o namespace do enum no `$metadata`. Se o EDM expuser `Kind` como string, use
    `Kind eq 'Traction'`.
- **Coluna Tipo:** não é necessária, porque só aparecem trações.
- **Alcance:** o dialog é compartilhado pelo `CommonController.openTrucksValueHelp`, então a
  balança, o romaneio, a carga, a ordem de expedição e as notas herdam o filtro de uma vez.
- **Conferência:** procurar outros pontos que listam `/Trucks` para escolha (ex.: `ComboBox` em
  fragments de carga, `LoadForm.fragment.xml`) e aplicar o mesmo `$filter`.
  - A tela `veiculo/Main` **não** filtra.
  - Os documentos já gravados continuam mostrando a placa (só o value help filtra).

- [ ] **Step 1:** implementar.
- [ ] **Step 2:** verificar no navegador (Task 10).
- [ ] **Step 3:** commit `feat(master-data): escolha de veículo mostra só tração ativa`.

### Task 8: Frontend — tela de Conjuntos

**Files:**
- Create: `webapp/view/truckCombinations/{Main,Add,Edit}.view.xml` (+ `fragments/Form.fragment.xml`), `webapp/controller/truckCombinations/*.ts`
- Modify: `webapp/manifest.json` (rotas `truckCombinations`, `truckCombinationsNew`, `truckCombinationsEdit`; padrões `truck-combinations`, `truck-combinations/new`, `truck-combinations/{id}/edit`), `webapp/model/ServerRoutes.ts` (`truckCombinations: '/odata/TruckCombinations'`)

**Interfaces:**
- Consumes: entidade/ação da Task 3. O name da rota `truckCombinations` = key do menu.

**O que muda:**
- **Lista:** código, descrição, tração, reboques (placas juntas por ", ") e inativo.
- **Formulário:**
  - código, descrição, tração (value help de tração — o `TrucksSelectDialog` filtrado da Task 7);
  - motorista padrão (value help de motoristas existente);
  - tabela de reboques com adicionar, remover e subir/descer, limite visual de 3 (botão
    "Adicionar" desabilitado em 3);
  - value help de reboques: variação do dialog com `Kind eq ...'Trailer' and Inactive eq false`
    (fragment `TrailersSelectDialog.fragment.xml` novo + `openTrailersValueHelp` no
    `CommonController`).

- [ ] **Step 1:** implementar.
- [ ] **Step 2:** `yarn ts-typecheck`, `yarn lint` e `yarn ui5lint` sem erro novo.
- [ ] **Step 3:** commit `feat(master-data): tela de conjuntos de veículos`.

### Task 9: Frontend — seção "Transportadora" na tela da filial

**Files:**
- Modify: `webapp/view/branchs/fragments/Form.fragment.xml`, `webapp/controller/branchs/*.ts` (Add e Edit), `webapp/model/ServerRoutes.ts`

**Interfaces:**
- Consumes: as rotas da Task 4.
- O formulário da filial grava `Branch` por OData. A seção usa um `JSONModel` próprio
  (`transport`): carregado por `BranchTransportSettingsGet` ao abrir a edição e gravado por
  `BranchTransportSettingsSave` **depois** que a gravação da filial der certo.

**Decisão do usuário (22/10):** fica na tela da filial com o título **"Transportadora"**.

**O que muda:**
- **Visibilidade:** só com `{ui>/standalone}`. Na inclusão de filial, mostrar só o aviso
  `"Grave a filial para configurar a transportadora."` (precisa do código já gravado).
- **Campos:**
  - RNTRC;
  - seguro padrão: responsável, CNPJ e nome da seguradora, apólice;
  - averbação AT&M: ligado, ambiente, usuário, senha e código.
    - Senha com `type="Password"`.
    - Placeholder `"Senha gravada — deixe em branco para manter"` quando `HasAverbaPassword`.
- **Botão "Testar conexão AT&M":** sucesso → `MessageToast`; falha → `MessageBox.error`.
- **Falha ao gravar o transporte:** a filial já foi gravada. Mostrar `MessageBox.error` com a
  mensagem do backend e manter a tela.
- **Cabeçalho:** truque do "Emitente da NF-e":
  `core:Title text="{= ${ui>/standalone} ? 'Transportadora' : '' }"`.

- [ ] **Step 1:** implementar.
- [ ] **Step 2:** `yarn ts-typecheck` e `yarn lint`.
- [ ] **Step 3:** commit `feat(master-data): seção Transportadora na filial com seguro e AT&M`.

### Task 10: Verificação ponta a ponta

- [ ] **Step 1:** backend completo, `dotnet test SiagroB1.Application.Tests`. Esperado: todos
  passam, exceto as falhas conhecidas de relatório quando falta `wwwroot`.
- [ ] **Step 2:** subir a stack STANDALONE (`ceagui`) a partir dos worktrees e testar pelo
  caminho do usuário, a partir da home (memória *Verificar pelo caminho do usuário*):
  1. **Menu:** Conjuntos aparece.
  2. **Veículos:**
     - cadastrar um cavalo (Cavalo mecânico, sem carroceria) e duas carretas (Reboque, Baú,
       tara e capacidade);
     - um veículo antigo abre e grava sem preencher nada novo.
  3. **Motoristas:**
     - incluir com CPF inválido → mensagem;
     - incluir com CPF mascarado válido → grava só os dígitos;
     - CNH vencida aparece em vermelho.
  4. **Conjuntos:**
     - montar cavalo + 2 carretas e gravar;
     - tentar uma 4ª carreta → botão desabilitado;
     - tentar tração inativa → mensagem.
  5. **Filial → seção Transportadora:**
     - gravar RNTRC, seguro e AT&M;
     - reabrir: a senha não volta e o placeholder aparece;
     - "Testar conexão" com credenciais inválidas → mensagem de erro da AT&M (não testar com as
       credenciais reais do cliente).
  6. **Balança (primeira pesagem) e Carga:** o value help de veículo não mostra as carretas.
- [ ] **Step 3:** subir em modo SAPB1 (`yktb`). Veículos e Motoristas abrem sem a seção
  Proprietário, Conjuntos não aparece no menu, a filial não mostra a Transportadora, e a pesagem continua
  escolhendo veículo.
- [ ] **Step 4:** derrubar a stack, matando só pelos PIDs iniciados (memória).
- [ ] **Step 5:** superpowers:verification-before-completion e a revisão final do branch.
