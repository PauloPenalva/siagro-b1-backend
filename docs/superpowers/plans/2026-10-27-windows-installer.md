# Assistente de Instalação Windows — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Um `SiagroB1-Setup-<versão>.exe` (WPF, .NET 10) que instala e atualiza Web, Gateway, Reports e o frontend como serviços Windows, mesclando o `appsettings.<Ambiente>.json` do cliente e aplicando migrations por bundle.

**Architecture:** `installer/SiagroB1.Installer.Core` (biblioteca sem WPF, toda a lógica testável) + `installer/SiagroB1.Installer` (WPF, MVVM sem bibliotecas, só páginas e ViewModels) + `installer/SiagroB1.Installer.Tests` (xUnit). Solução própria `installer/SiagroB1.Installer.sln`, fora do `SiagroB1.sln`. A execução é uma lista de `IInstallStep` com `ExecuteAsync`/`RollbackAsync`.

**Tech Stack:** .NET 10 (`net10.0` no Core, `net10.0-windows` + WPF no app), `System.Text.Json.Nodes`, `Microsoft.Data.SqlClient`, `System.ServiceProcess.ServiceController`, xUnit, `dotnet ef migrations bundle`, ui5 build (`yarn build`).

**Spec:** `docs/superpowers/specs/2026-10-27-windows-installer-design.md`

## Global Constraints

- Texto que o usuário lê em pt-BR; identificadores, nomes de arquivo e classes em inglês.
- Nunca gravar senha/chave em log, resumo ou relatório: connection string aparece com `Password=***`; chaves cujo nome contém `Password`, `Secret`, `Key`, `Token` aparecem `***`.
- Configuração do cliente vive em `appsettings.<Ambiente>.json` (padrão `Production`); `appsettings.json` é o padrão da versão e é sempre substituído.
- Portas padrão: Gateway 55000, Web 55100, Reports 55200. Pasta padrão `C:\IdxSistemas\SiagroB1\Producao`.
- `binPath` do serviço: `"<exe>" --environment <Amb> --urls http://*:<porta>`, início automático.
- Nomes de serviço de instância nova: `siagrob1-<instância minúscula>-web|gateway|reports`; instalação antiga mantém os nomes que tem (ex.: `siagrob1webservice`).
- Nada de `Migrate()` no código das aplicações.
- `SiagroB1.Client` fora do escopo.
- Fixtures de teste com dados reais só mascarados; nunca versionar os arquivos originais de `C:\Users\Penalva\Downloads` / `Desktop`.
- Commits: `tipo(platform): ...` em pt-BR, branch `feature/windows-installer`, nunca push. Todo arquivo novo: `git add` logo após criar.

## Review Focus

1. Atualização de uma instalação feita pelos scripts antigos (sem `install.json`, nomes `siagrob1webservice`, porta só no `binPath`) — deve ser detectada e pré-preenchida, não tratada como nova. Teste em Task 3.
2. Arquivo do cliente com JSON inválido ou com comentários/vírgula final (editado à mão) — deve parar com mensagem clara antes de parar serviços, não corromper. Teste em Task 1.
3. Porta já ocupada por outro processo (ou duas portas iguais) — validação bloqueia Avançar. Teste em Task 4.
4. Serviço que não para dentro do timeout (processo travado) — deve abortar antes do backup/cópia e religar o que parou. Teste em Task 6.
5. Banco de uma empresa em `COMPANIES` inacessível durante a migration — falha com nome da empresa e rollback dos arquivos; as empresas já migradas ficam registradas no log. Teste em Task 5.

---

### Task 1: Esqueleto da solução + `AppSettingsMerger`

**Files:**
- Create: `installer/SiagroB1.Installer.sln`, `installer/SiagroB1.Installer.Core/SiagroB1.Installer.Core.csproj`, `installer/SiagroB1.Installer.Tests/SiagroB1.Installer.Tests.csproj`
- Create: `installer/SiagroB1.Installer.Core/Settings/AppSettingsMerger.cs`, `MergeResult.cs`, `JsonFile.cs`
- Test: `installer/SiagroB1.Installer.Tests/Settings/AppSettingsMergerTests.cs`

**Interfaces:**
- Produces:
  - `static JsonObject JsonFile.Load(string path)` — aceita comentários e vírgula final (`JsonDocumentOptions { CommentHandling = Skip, AllowTrailingCommas = true }`); JSON inválido lança `InvalidSettingsFileException(path, message)`.
  - `static void JsonFile.Save(string path, JsonObject obj)` — indentado, UTF-8 sem BOM.
  - `MergeResult AppSettingsMerger.Merge(JsonObject defaults, JsonObject? client, IReadOnlyDictionary<string, JsonNode?> edits, IReadOnlyCollection<string> dropPaths)`
  - `record MergeResult(JsonObject Result, IReadOnlyList<string> NewKeys, IReadOnlyList<string> ObsoleteKeys, IReadOnlyList<string> ChangedKeys)`
  - Caminhos de chave no formato de configuração do ASP.NET Core: `"ConnectionStrings:SiagroDB"`.

- [ ] **Step 1: Criar solução e projetos** (`dotnet new sln`, `classlib` net10.0, `xunit` referenciando o Core); `git add installer/`.
- [ ] **Step 2: Escrever os testes que falham** em `AppSettingsMergerTests`:
  - `Merge_NewInstall_ReturnsOnlyEdits` — client null, edits `{ "Erp": "SAPB1" }` → Result == `{ "Erp": "SAPB1" }`, NewKeys vazio.
  - `Merge_KeepsClientValue` — default `Port:587`, client `Port:2525`, sem edits → 2525.
  - `Merge_ReportsNewKeyWithoutCopying` — default tem `Nfe:TimeoutSeconds`, client não → `NewKeys` contém `"Nfe:TimeoutSeconds"` e Result não tem a chave.
  - `Merge_KeepsAndReportsObsoleteKey` — client `Old:X`, default sem → mantido e em `ObsoleteKeys`.
  - `Merge_EditOverridesClient` — edit `"Email:Host"` vence client; aparece em `ChangedKeys`.
  - `Merge_ArrayIsAtomic` — default `[1,2]`, client `[3]` → `[3]`.
  - `Merge_DropPathRemovesSubtree` — dropPaths `"ReverseProxy:Routes"` remove só `Routes`, mantém `Clusters`.
  - `Load_AcceptsCommentsAndTrailingComma` e `Load_InvalidJson_ThrowsInvalidSettingsFileException` (mensagem contém o caminho).
- [ ] **Step 3: Rodar** `dotnet test installer/SiagroB1.Installer.sln` → FAIL (tipos inexistentes).
- [ ] **Step 4: Implementar** `JsonFile` e `AppSettingsMerger` (comparação de chaves case-insensitive, como a configuração do ASP.NET Core; objetos recursivos, folhas e arrays atômicos; edit com valor `null` remove a chave).
- [ ] **Step 5: Rodar** → PASS.
- [ ] **Step 6: Commit** `feat(platform): adiciona mesclagem de appsettings do instalador`.

### Task 2: `InstallSettings` ↔ arquivos dos 3 serviços

**Files:**
- Create: `installer/SiagroB1.Installer.Core/Settings/InstallSettings.cs`, `ServiceSettingsMapper.cs`, `SecretMasker.cs`
- Test: `installer/SiagroB1.Installer.Tests/Settings/ServiceSettingsMapperTests.cs`, `SecretMaskerTests.cs`, fixtures `Fixtures/yokotobi-web.Production.json`, `Fixtures/yokotobi-gateway.Production.json` (cópias mascaradas dos arquivos reais, senhas `***`)

**Interfaces:**
- Consumes: `JsonFile`, `AppSettingsMerger` (Task 1).
- Produces:
  - `enum ServiceKind { Web, Gateway, Reports }`
  - `class InstallSettings` com: `SqlServer`, `SqlUser`, `SqlPassword`, `UseWindowsAuth`, `CommonDatabase`, `CompanyDatabase`, `ErpMode` (`"SAPB1"|"STANDALONE"`), `SapDatabase`, `SapCommonDatabase`, `SapServiceLayerUrl`, `CompanyName`, `GatewayPort`, `WebPort`, `ReportsPort`, `PublicBaseUrl`, `ReportsInternalApiKey`, `LogoSourcePath?`, e `Dictionary<string, JsonNode?> Integrations` (chaves livres da página Integrações, com caminho completo).
  - `IReadOnlyDictionary<string, JsonNode?> ServiceSettingsMapper.ToEdits(ServiceKind kind, InstallSettings s)`
  - `InstallSettings ServiceSettingsMapper.FromClientFiles(JsonObject? web, JsonObject? gateway, JsonObject? reports)` — pré-preenchimento na atualização.
  - `static IReadOnlyCollection<string> ServiceSettingsMapper.DropPaths(ServiceKind kind)` → Gateway: `["ReverseProxy:Routes"]`; demais vazio.
  - `static string SecretMasker.MaskConnectionString(string cs)`, `static string SecretMasker.MaskValue(string keyPath, string? value)`.

- [ ] **Step 1: Testes que falham:**
  - `ToEdits_Web_Sapb1` — connection strings `SiagroDB`, `SiagroCommon`, `SapDB`, `SapCommon` montadas com `SqlConnectionStringBuilder` (`TrustServerCertificate=True`), `Erp`, `CompanyName`, `SAPB1:ServiceLayerUrl`, `Reports:InternalBaseUrl == "http://localhost:55200"`, `Reports:InternalApiKey`, `Notifications:AppBaseUrl == PublicBaseUrl`.
  - `ToEdits_Web_Standalone_OmitsSapKeys` — sem `SapDB`/`SapCommon`/`SAPB1:*`.
  - `ToEdits_Gateway` — `Security:AppBaseUrl`, `ReverseProxy:Clusters:backend:Destinations:odata-destination:Address == "http://localhost:55100/"`, `...reports:Destinations:reports-destination:Address == "http://localhost:55200/"`.
  - `ToEdits_Reports` — mesma `Reports:InternalApiKey` do Web; `CompanyLogoPath == "wwwroot/images/logo.png"`.
  - `FromClientFiles_Yokotobi` — com as fixtures: servidor `192.168.1.144,1433`, `CommonDatabase == "IDX_SIAGRO_COMMON"`, `ErpMode == "SAPB1"`, `WebPort == 55100`, `ReportsPort == 55200`, `PublicBaseUrl == "http://192.168.1.144:55000"`.
  - `MaskConnectionString_HidesPassword` → contém `Password=***`, não contém a senha.
  - `MaskValue_ByKeyName` — `Email:Password`, `Nfe:CertificateKey`, `WhatsApp:Token` → `***`; `Email:Host` intacto.
- [ ] **Step 2: Rodar** → FAIL.
- [ ] **Step 3: Implementar.** Portas de Web/Reports no pré-preenchimento vêm dos endereços dos clusters do Gateway; a porta do Gateway vem do `binPath` (Task 3) e não deste mapper.
- [ ] **Step 4: Rodar** → PASS.
- [ ] **Step 5: Commit** `feat(platform): mapeia configuração do assistente para os três serviços`.

### Task 3: Detecção de instalação + `install.json`

**Files:**
- Create: `installer/SiagroB1.Installer.Core/Detection/InstallationDetector.cs`, `InstalledService.cs`, `InstallManifest.cs`, `BinPathParser.cs`, `IServiceQuery.cs`
- Test: `installer/SiagroB1.Installer.Tests/Detection/InstallationDetectorTests.cs`, `BinPathParserTests.cs`

**Interfaces:**
- Produces:
  - `record InstalledService(ServiceKind Kind, string ServiceName, string ExePath, string Environment, int Port)`
  - `record InstallManifest(string Version, string Instance, string BasePath, string Environment, IReadOnlyList<InstalledService> Services, DateTime InstalledAt)` + `static InstallManifest? Load(string basePath)` / `Save(string basePath)` em `<base>\install.json`.
  - `interface IServiceQuery { IEnumerable<(string Name, string BinPath)> All(); }` (implementação real lê `Win32_Service` via `System.Management` ou registro `HKLM\SYSTEM\CurrentControlSet\Services\*\ImagePath` — usar o registro, sem dependência extra).
  - `static (string ExePath, string Environment, int Port)? BinPathParser.Parse(string binPath)`
  - `record DetectionResult(InstallManifest? Manifest, IReadOnlyList<InstalledService> Services, bool IsLegacy)`
  - `DetectionResult InstallationDetector.Detect(string basePath)` (ctor recebe `IServiceQuery`).

- [ ] **Step 1: Testes que falham:**
  - `Parse_LegacyBinPath` — `"\"C:\IdxSistemas\SiagroB1\Producao\Web\SiagroB1.Web.exe\" --environment Production --urls http://*:55100"` → exe, `Production`, 55100.
  - `Parse_Unquoted_And_MissingArgs` — sem `--environment` → `Production`; sem `--urls` → null.
  - `Detect_WithManifest_UsesManifest`.
  - `Detect_LegacyScripts_FindsServicesByExeName` — fake com `siagrob1webservice`, `siagrob1gatewayservice`, `siagrob1reportsservice` apontando para a pasta → 3 serviços, `IsLegacy == true`, Kind deduzido do nome do exe (`SiagroB1.Web.exe` etc.).
  - `Detect_IgnoresServicesOutsideBasePath`.
  - `Detect_Nothing_ReturnsEmpty`.
- [ ] **Step 2: Rodar** → FAIL. **Step 3: Implementar.** **Step 4: Rodar** → PASS.
- [ ] **Step 5: Commit** `feat(platform): detecta instalação existente, inclusive a dos scripts antigos`.

### Task 4: Serviços Windows, portas e teste de endpoint

**Files:**
- Create: `installer/SiagroB1.Installer.Core/Services/IServiceManager.cs`, `WindowsServiceManager.cs`, `ServiceCommandBuilder.cs`, `PortChecker.cs`, `EndpointProbe.cs`, `IProcessRunner.cs`, `ProcessRunner.cs`
- Test: `installer/SiagroB1.Installer.Tests/Services/ServiceCommandBuilderTests.cs`, `PortCheckerTests.cs`, `EndpointProbeTests.cs`

**Interfaces:**
- Produces:
  - `record ProcessResult(int ExitCode, string Output)`; `interface IProcessRunner { Task<ProcessResult> RunAsync(string file, IReadOnlyList<string> args, string? workingDir, IDictionary<string,string>? env, CancellationToken ct); }`
  - `static string ServiceCommandBuilder.BinPath(string exe, string environment, int port)`
  - `static IReadOnlyList<string> ServiceCommandBuilder.CreateArgs(string name, string binPath, string displayName)` (para `sc.exe`, inclui `start= auto`), `DescriptionArgs`, `FailureArgs` (`reset= 86400 actions= restart/60000/restart/60000/restart/60000`).
  - `interface IServiceManager { bool Exists(string name); Task StopAsync(string name, TimeSpan timeout, CancellationToken ct); Task StartAsync(string name, TimeSpan timeout, CancellationToken ct); Task CreateOrReplaceAsync(string name, string binPath, string displayName, string description, CancellationToken ct); Task DeleteAsync(string name, CancellationToken ct); }` — `StopAsync` lança `ServiceTimeoutException` se não parar no tempo.
  - `static IReadOnlyList<string> PortChecker.Validate(IReadOnlyDictionary<ServiceKind,int> ports, Func<int,bool> isInUse, IReadOnlySet<int> ownPorts)` → mensagens pt-BR (vazio = ok). `ownPorts` = portas da instalação atual (não contam como ocupadas na atualização).
  - `Task<(bool Responding, int? Status)> EndpointProbe.ProbeAsync(Uri url, TimeSpan timeout, HttpMessageHandler? handler = null)` — 2xx–4xx = respondendo.
- [ ] **Step 1: Testes que falham:** `BinPath_Format` (string exata das Global Constraints); `CreateArgs_HasAutoStart`; `Validate_DuplicatePorts` → `"As portas do Gateway e do Web não podem ser iguais."`; `Validate_PortInUse` → `"A porta 55000 já está em uso por outro programa."`; `Validate_OwnPortIgnored`; `Validate_OutOfRange` (0, 70000); `Probe_401_IsResponding`; `Probe_500_NotResponding`; `Probe_Timeout_NotResponding` (handler fake).
- [ ] **Step 2: Rodar** → FAIL. **Step 3: Implementar** (`WindowsServiceManager` usa `ServiceController` para parar/iniciar e `sc.exe` via `IProcessRunner` para criar/descrever/excluir; `isInUse` real via `IPGlobalProperties.GetActiveTcpListeners()`). **Step 4: Rodar** → PASS.
- [ ] **Step 5: Commit** `feat(platform): gerencia serviços windows e valida portas do instalador`.

### Task 5: `MigrationRunner`

**Files:**
- Create: `installer/SiagroB1.Installer.Core/Migrations/MigrationRunner.cs`, `CompanyDatabase.cs`, `ICompanyLister.cs`, `SqlCompanyLister.cs`, `MigrationReport.cs`
- Test: `installer/SiagroB1.Installer.Tests/Migrations/MigrationRunnerTests.cs`

**Interfaces:**
- Consumes: `IProcessRunner` (Task 4), `SecretMasker` (Task 2).
- Produces:
  - `record CompanyDatabase(string Code, string Name, string ConnectionString)`
  - `interface ICompanyLister { Task<IReadOnlyList<CompanyDatabase>> ListAsync(string commonConnectionString, CancellationToken ct); }` — real: `SELECT Code, CompanyName, ConnectionString FROM COMPANIES` (confirmar nome de tabela/colunas no snapshot `SiagroB1.Migrations/CommonContext/*ModelSnapshot.cs` antes de escrever o SQL).
  - `record MigrationTarget(string Label, string BundlePath, string ConnectionString)`
  - `record MigrationReport(IReadOnlyList<(string Label, IReadOnlyList<string> Pending)> Pending)`
  - `Task<MigrationReport> MigrationRunner.ListPendingAsync(...)` e `Task MigrationRunner.ApplyAsync(string bundleDir, string webInstallDir, string commonCs, string? ddlUser, string? ddlPassword, IProgress<string> log, CancellationToken ct)`.
  - Ordem: `efbundle-common.exe` no Common, depois `efbundle-app.exe` em cada empresa. Argumentos: `--connection <cs>` (+ `--verbose` não); `workingDir = webInstallDir`, env `ASPNETCORE_ENVIRONMENT=<Amb>` para o host do Web achar o `appsettings.<Amb>.json`. Pendentes: o bundle não lista; usar `SELECT MigrationId FROM __EFMigrationsHistory` comparado com a lista embutida no pacote (`migrations-app.txt`, `migrations-common.txt`, gerados no build pela Task 8 com `dotnet ef migrations list --no-connect`).
  - `ddlUser` não nulo: troca só `UserID`/`Password` via `SqlConnectionStringBuilder` em todas as strings.
  - Falha: lança `MigrationFailedException(label, exitCode, maskedOutput)`; empresas já aplicadas registradas em `log`.
- [ ] **Step 1: Testes que falham** (fakes de `IProcessRunner` e `ICompanyLister`): `Apply_RunsCommonThenEachCompany` (ordem e `--connection` certos); `Apply_DdlUserReplacesCredentials`; `Apply_StopsOnFirstFailure_NamesCompany` (2ª empresa exit 1 → exceção com `Label` da empresa, 3ª não roda, log cita a 1ª como aplicada); `Apply_LogNeverContainsPassword`; `ListPending_DiffsHistory`.
- [ ] **Step 2: Rodar** → FAIL. **Step 3: Implementar.** **Step 4: Rodar** → PASS.
- [ ] **Step 5: Spike de verificação (manual, uma vez):** gerar `efbundle-app.exe` localmente e rodá-lo com `--connection` apontando para uma **cópia** do banco yktb (nunca o banco local principal — ver memória do banco compartilhado), `workingDir` = pasta publicada do Web. Esperado: "No migrations were applied. The database is already up to date." Se o host do Web exigir algo antes do `Build()` que quebre fora do projeto, registrar e ajustar (`--startup-project` alternativo ou `IDesignTimeDbContextFactory` em `SiagroB1.Migrations`).
- [ ] **Step 6: Commit** `feat(platform): aplica migrations do common e de cada empresa pelo instalador`.

### Task 6: Passos de instalação, backup e rollback

**Files:**
- Create: `installer/SiagroB1.Installer.Core/Execution/IInstallStep.cs`, `InstallContext.cs`, `InstallExecutor.cs`, `InstallPlanBuilder.cs`, `Steps/{StopServicesStep,BackupStep,CopyFilesStep,WriteSettingsStep,MigrateStep,RegisterServicesStep,StartServicesStep,ProbeStep,WriteManifestStep}.cs`, `Logging/SetupLog.cs`
- Test: `installer/SiagroB1.Installer.Tests/Execution/InstallExecutorTests.cs`, `CopyFilesStepTests.cs`, `InstallPlanBuilderTests.cs`

**Interfaces:**
- Consumes: tudo das Tasks 1–5.
- Produces:
  - `interface IInstallStep { string Title { get; } Task ExecuteAsync(InstallContext ctx, CancellationToken ct); Task RollbackAsync(InstallContext ctx, CancellationToken ct); }`
  - `class InstallContext` — `InstallSettings Settings`, `DetectionResult Detection`, `string BasePath`, `string Instance`, `string Environment`, `string PayloadDir`, `string? BackupDir`, `IReadOnlyList<InstalledService> TargetServices`, `IProgress<string> Log`, `bool DatabaseBackupConfirmed`.
  - `static IReadOnlyList<IInstallStep> InstallPlanBuilder.Build(InstallContext ctx, ...deps)` — atualização: Stop → Backup → Copy → WriteSettings → Migrate → Register → Start → Probe → Manifest; nova: sem Stop/Backup.
  - `Task<InstallOutcome> InstallExecutor.RunAsync(IReadOnlyList<IInstallStep> steps, InstallContext ctx, CancellationToken ct)`; `record InstallOutcome(bool Success, string? FailedStep, string? Error, IReadOnlyList<string> RolledBack)` — falha executa `RollbackAsync` dos passos concluídos em ordem inversa (o passo que falhou também). `ProbeStep` não falha a instalação: só registra aviso.
  - `CopyFilesStep` preserva em cada pasta: `appsettings.*.json` exceto `appsettings.json`, `logs\`, `Reports\wwwroot\images\logo.png`; o resto é substituído (apagar arquivos da versão anterior que não vêm no pacote).
  - `BackupStep` copia `Web`, `Gateway`, `Reports` para `<base>\backups\<aaaaMMdd-HHmmss>\`; rollback de `CopyFilesStep`/`WriteSettingsStep` restaura dessa pasta.
  - `WriteSettingsStep` grava, para cada serviço, `Merge(defaults, client, ToEdits(kind), DropPaths(kind))` em `appsettings.<Amb>.json`, e copia `Settings.LogoSourcePath` (se informado) para `Reports\wwwroot\images\logo.png`.
  - `MigrateStep` recusa executar se há pendentes e `DatabaseBackupConfirmed == false`.
  - `SetupLog` grava `<base>\logs\setup-<aaaaMMdd-HHmmss>.log`, sempre passando texto por `SecretMasker`.
- [ ] **Step 1: Testes que falham:** `Run_AllSucceed`; `Run_FailureRollsBackInReverse` (steps fake registrando ordem); `Run_StopTimeout_RestartsStoppedServices` (Review Focus 4: Stop do 2º serviço lança `ServiceTimeoutException` → rollback religa o 1º; Backup/Copy não rodam); `Copy_PreservesClientFiles` (diretórios temporários); `Copy_RemovesStaleFiles`; `Build_NewInstall_HasNoStopOrBackup`; `Migrate_WithoutDbBackupConfirmation_Throws`; `Run_ProbeFailure_StillSuccessWithWarning`.
- [ ] **Step 2: Rodar** → FAIL. **Step 3: Implementar.** **Step 4: Rodar** → PASS.
- [ ] **Step 5: Commit** `feat(platform): executa instalação em passos com backup e rollback`.

### Task 7: Aplicativo WPF do assistente

**Files:**
- Create: `installer/SiagroB1.Installer/SiagroB1.Installer.csproj` (`net10.0-windows`, `UseWPF`, `app.manifest` com `requireAdministrator`), `App.xaml`, `MainWindow.xaml`, `Wizard/{WizardViewModel,PageViewModel,RelayCommand,ObservableObject}.cs`, `Pages/{Welcome,Folder,Database,Erp,CompanyPorts,Integrations,NewKeys,Summary,Progress,Done}Page.xaml(.cs)` + ViewModels, `Payload/PayloadExtractor.cs`
- Test: `installer/SiagroB1.Installer.Tests/Wizard/PageValidationTests.cs` (ViewModels não dependem de WPF — referenciados por um projeto de testes `net10.0-windows`; mudar o TFM do projeto de testes para `net10.0-windows` nesta task)

**Interfaces:**
- Consumes: Core inteiro.
- Produces: `abstract class PageViewModel { string Title; abstract IReadOnlyList<string> Validate(); virtual bool IsVisible(WizardState s) => true; }`; `class WizardState` (InstallSettings + Detection + escolhas); `PayloadExtractor.ExtractAsync(string targetTempDir)` lê o recurso embutido `payload.zip` (ou `payload.zip` ao lado do exe, se existir — fallback do spec) e retorna `PayloadInfo(string Dir, string Version)` lido de `manifest.json`.

- [ ] **Step 1: Testes de validação que falham:** `Database_RequiresServerAndCommon`; `Database_TestConnection_RequiredBeforeNext` (flag `ConnectionTested` resetada ao editar campos); `Erp_Sapb1_RequiresSapDatabasesAndServiceLayerUrl`; `CompanyPorts_UsesPortChecker`; `NewKeys_HiddenWhenNoNewKeys` (`IsVisible`); `Summary_RequiresDbBackupCheckboxWhenPendingMigrations`; `Summary_MasksSecrets`.
- [ ] **Step 2: Rodar** → FAIL. **Step 3: Implementar ViewModels** (textos pt-BR do spec: páginas 1–10). **Step 4: Rodar** → PASS.
- [ ] **Step 5: Implementar XAML** das páginas e a janela (Voltar/Avançar/Cancelar; Cancelar desabilitado durante a execução). Página Concluído: status por serviço, resultado do probe, botão "Abrir no navegador" (`PublicBaseUrl`), link do log. Botão "Remover serviços" na Boas-vindas quando há instalação (só `DeleteAsync` dos serviços, sem apagar pastas).
- [ ] **Step 6: Verificar build** `dotnet build installer/SiagroB1.Installer.sln` → 0 erros; abrir o app e navegar todas as páginas até Resumo sem executar.
- [ ] **Step 7: Commit** `feat(platform): adiciona telas do assistente de instalação`.

### Task 8: Empacotamento + rota `hangfire` no Gateway padrão

**Files:**
- Create: `installer/build-installer.ps1`, `installer/payload-sanitize.json` (lista de caminhos esvaziados por serviço)
- Create: `installer/SiagroB1.Installer.Core/Settings/DefaultsSanitizer.cs`; Test: `installer/SiagroB1.Installer.Tests/Settings/DefaultsSanitizerTests.cs`
- Create: `installer/SiagroB1.Installer.Tools/` (console `net10.0` que expõe `sanitize <in> <out>` usando `DefaultsSanitizer`, chamado pelo script)
- Modify: `SiagroB1.Gateway/appsettings.json` (adicionar rota `hangfire` → cluster `backend`, `Path: /hangfire/{**catch-all}`, `AuthorizationPolicy: AuthenticatedOnly`, igual à da produção Yokotobi)

**Interfaces:**
- Produces: `static JsonObject DefaultsSanitizer.Sanitize(JsonObject o)` — esvazia (`""`) todo valor em `ConnectionStrings:*` e toda folha cujo nome contém `Password`, `Secret`, `Key`, `Token` (case-insensitive), exceto `Logging:LogLevel:*`; não remove chaves.
- Script `build-installer.ps1 [-Version <x>] [-CertificateThumbprint <t>]`:
  1. `dotnet publish` Web, Gateway, Reports `-c Release -r win-x64 --self-contained` para `out\payload\{Web,Gateway,Reports}`.
  2. `yarn build` em `..\siagro-b1-frontend` e cópia de `dist\*` para `out\payload\Gateway\wwwroot`; versão lida de `..\siagro-b1-frontend\package.json` se `-Version` omitido.
  3. Para cada `appsettings.json` publicado: `Tools sanitize`; apagar do payload todo `appsettings.*.json` que não seja `appsettings.json` (nunca levar arquivo de cliente).
  4. `dotnet ef migrations bundle --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web --self-contained -r win-x64 -o out\payload\migrations\efbundle-app.exe` (idem `CommonDbContext` → `efbundle-common.exe`) e `dotnet ef migrations list --no-connect` → `migrations-app.txt` / `migrations-common.txt`.
  5. `manifest.json` (`version`, `backendCommit` = `git rev-parse --short HEAD`, `frontendCommit`, `builtAt`).
  6. Zip → `payload.zip`; `dotnet publish installer/SiagroB1.Installer -c Release -r win-x64 --self-contained -p:PublishSingleFile=true` com `payload.zip` como `EmbeddedResource`; se o publish falhar por tamanho, gerar `.exe` + `payload.zip` lado a lado.
  7. Se `-CertificateThumbprint`: `signtool sign /sha1 <t> /fd SHA256 /tr http://timestamp.digicert.com /td SHA256`.
  8. Saída: `out\SiagroB1-Setup-<versão>.exe`.
- [ ] **Step 1: Testes que falham:** `Sanitize_ClearsConnectionStringsAndSecrets` (usar a forma do `SiagroB1.Web/appsettings.json` do repositório com senha falsa); `Sanitize_KeepsNonSecretValues` (`Email:Port`, `Erp`); `Sanitize_KeepsLogLevel`.
- [ ] **Step 2: Rodar** → FAIL. **Step 3: Implementar** sanitizer + Tools. **Step 4: Rodar** → PASS.
- [ ] **Step 5: Adicionar a rota `hangfire`** no `SiagroB1.Gateway/appsettings.json`; `dotnet build SiagroB1.sln` → 0 erros.
- [ ] **Step 6: Escrever o script e rodar** `pwsh installer/build-installer.ps1`. Esperado: `out\SiagroB1-Setup-<versão>.exe` gerado; inspecionar o zip: nenhum `appsettings.*.json` de cliente, nenhuma senha (`Select-String -Pattern 'Password=[^;"]'` sem resultados).
- [ ] **Step 7: Adicionar `installer/out/` ao `.gitignore`**; commit `feat(platform): empacota o instalador com payload sanitizado` (rota hangfire num commit separado: `fix(platform): inclui rota do hangfire no gateway padrão`).

### Task 9: Verificação ponta a ponta numa VM/máquina Windows

**Files:** nenhum código novo; resultados em `docs/superpowers/plans/2026-10-27-windows-installer-verification.md`.

- [ ] **Step 1: Instalação nova** na máquina local com instância `Teste`, portas 56000/56100/56200, banco = **cópia** do yktb, STANDALONE ou SAPB1 conforme o banco. Esperado: 3 serviços `siagrob1-teste-*` rodando, login funcionando em `http://localhost:56000`, `install.json` e log sem senhas.
- [ ] **Step 2: Simular instalação legada:** remover a instância, recriá-la com os 3 scripts antigos (`C:\Users\Penalva\Downloads\Install-SiagroB1-*.ps1`, parâmetros ajustados para a pasta/portas de teste) e um `appsettings.Production.json` mascarado-com-dados-locais. Rodar o setup → deve oferecer "Atualizar", pré-preencher servidor/portas/URL, manter os nomes antigos dos serviços e preservar todos os valores do cliente (diff do arquivo antes/depois só nas chaves editadas e sem `ReverseProxy:Routes`).
- [ ] **Step 3: Rollback:** forçar falha na migration (connection string de uma empresa inválida em `COMPANIES` da cópia) → versão anterior volta a rodar, arquivos iguais ao backup.
- [ ] **Step 4: Limpar** (remover serviços de teste pelo botão "Remover serviços", derrubar processos só pelo PID). Registrar resultados e commit `docs(platform): registra verificação do instalador`.
