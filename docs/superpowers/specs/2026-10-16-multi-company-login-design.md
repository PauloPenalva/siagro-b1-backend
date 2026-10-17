# Seleção de Empresa (banco) no login — estilo SAP Business One

## Context

Hoje cada instalação do SiagroB1 aponta para **um** banco de negócio (`ConnectionStrings:SiagroDB`), fixado no startup dos três hosts (Web, Gateway, Reports). A CEAGUI vai ganhar uma segunda empresa do grupo (**LM CEREAIS**) e a Tagui Brasil outra (**UNIVIA**). Elas não podem ser filiais: filial (`BRANCHS`) é uma linha filtrada dentro do mesmo banco, compartilhando cadastros, numeração de NF-e, Hangfire e migrations. Uma empresa no sentido do SAP B1 é um **banco separado**.

Fluxo desejado (SAP B1): na tela de login o usuário escolhe a Empresa, depois informa usuário/senha. `SIAGRO_COMMON` já faz o papel da `SBO-COMMON` (usuários, perfis, menus, sessões) e já possui a tabela `COMPANIES` (Code, CompanyName, ConnectionString, SapCompanyDb), hoje sem uso em runtime.

## Decisões fechadas com o usuário

| Tema | Decisão |
|---|---|
| Usuários | Centralizados na COMMON; a string de conexão é informada no cadastro da empresa |
| Topologia | **Um** deploy (Gateway/Web/Reports) serve todas as empresas do grupo; seletor no login |
| Resolução do banco | **Empresa na sessão**: `USER_SESSIONS.CompanyCode`. O `CookieAuthMiddleware` (roda nos 3 hosts) carrega a empresa; `AppDbContext`, Dapper, lock de NF-e e FastReport abrem a conexão de `COMPANIES`. Nenhum header novo no frontend (~30 fetch/ajax + EventSource intactos) |
| Acesso e perfis | `USERS_PROFILES.CompanyCode`. Entra na empresa X se `IsAdmin` **ou** tem ≥1 perfil em X (acesso derivado; **sem** tabela USER_COMPANIES). Permissões/roles/menu da sessão vêm só dos perfis da empresa da sessão |
| Troca de empresa | Só saindo e logando de novo; login lembra a última empresa. Item "Trocar Empresa" no menu do avatar = confirmação + logout |
| Tela | "Empresas" (admin) com Código, Nome, String de conexão, `SapCompanyDb` (só SAP) e **Testar conexão**. Salvar exige conexão OK e **zero migrations pendentes** do `AppDbContext` |
| Banco novo | Vazio. Ordem operacional: criar DB → `dotnet ef database update --context AppDbContext --connection "<cs>"` → cadastrar empresa |
| `Erp` | Continua por deploy; `SapCompanyDb` por empresa fora do escopo (coluna fica) |
| Hangfire | Storage vai para `SiagroCommon`; `storage-daily-calculation`, `contract-notification-sweep` e `ContractDraftsReconcile` rodam **por empresa**; dispatch do WhatsApp carrega `companyCode`; `sap-user-sync` continua job único (independente de empresa) |
| Compatibilidade | Bootstrap idempotente no startup (Web e Gateway): `COMPANIES` vazia ⇒ insere empresa padrão da config (`Company:Code`="0001", `CompanyName`, `SiagroDB`) e atribui a ela perfis/sessões sem empresa. Sem empresa resolvível (design-time `dotnet ef`, guard, endpoint anônimo) ⇒ fallback `ConnectionStrings:SiagroDB` |

## Fatos do código que o plano depende

- Os 3 hosts chamam `UseCookieAuth()` (Web `Program.cs:228`, Gateway `:133`, Reports `:94`). O `CookieAuthMiddleware` usa **scope próprio** para consultar a sessão ⇒ a empresa tem de ser gravada em `context.RequestServices`, não no scope interno.
- `BasicAuthenticationHandler` não está no caminho normal (`DefaultAuthenticateScheme = Cookies`, ninguém faz `SignInAsync`); `login/basic` passa por `AuthService.LoginWithBasicAuthAsync`.
- `UserProfile` (`USERS_PROFILES`): PK surrogate `Guid Id`, índice único `(UserId, ProfileCode)`; rotas OData `Users({userId})/Profiles({id})` dependem do Guid ⇒ só acrescentar coluna e estender o índice.
- Permissões/roles/menu: `AuthService.GetRolesAsync/GetPermissionsAsync` (login e `GetUserInfoAsync` → `status`, `GetUserMenu`), `MenuService.GetMenuAsync`, `Application/Services/Security/UserPermissionsService` (`GetAsync`, `HasRoleAsync`).
- Consumidores de connection string: `AddDbContext<AppDbContext>` (Web 63, Gateway 31, Reports 33); `IDbConnection` scoped (Web 78, Reports 68); `NfeNumberReservationService.EmissionLockConnectionString()` (:23); `FastReportService` (:76); `ReportHeaderService` (`CompanyName`/`CompanyLogoPath`, cache estático de logo de um slot); Hangfire (Web 132).
- Interceptors criados inline no lambda (Web 72-73) ⇒ com o overload `(sp, options)` viram novos a cada scope → `ManyServiceProvidersCreatedWarning`. Precisam ser singletons.
- Hangfire 1.8.23: `IServerFilter.OnPerforming` roda **antes** de `JobActivator.BeginScope`, no mesmo fluxo síncrono ⇒ um `AsyncLocal` setado no filtro é visível quando o scope resolve `AppDbContext`. `ContractDraftsReconcileJob` cria scopes aninhados (`IServiceScopeFactory`, :70) ⇒ também precisa do ambient.
- Tests: `SiagroB1.Application.Tests` já tem `TestDb`, `FakeBackgroundJobClient`, `FakeSapUserProvisioner`, padrões InMemory para `CommonDbContext` (`UserPermissionsServiceTests`, `MenuServiceStandaloneOnlyTests`, `PasswordResetServiceTests`).

---

## Backend

### 1. Infra de contexto de empresa — `SiagroB1.Infra/Companies/` (novo)

- `CompanyInfo` record `(Code, CompanyName, ConnectionString)`.
- `CompanyAmbient`: `AsyncLocal<string?>` com `Use(code)` (IDisposable, restaura) e `Set(code)` (para o filtro do Hangfire).
- `ICompanyCatalog` / `CompanyCatalog` (singleton, usa `IServiceScopeFactory` para `CommonDbContext`): `GetAllAsync`, `GetByCodeAsync` (miss ⇒ recarrega uma vez antes de devolver null — é o que torna empresa nova visível de imediato em Gateway/Reports), `TryGetCached(code)` síncrono sobre o snapshot, `DefaultCompanyCode` (`Company:Code` ?? `"0001"`), `Invalidate()`. Cache com TTL 60 s, `AsNoTracking`, projeta para `CompanyInfo`.
- `ICompanyContext` / `CompanyContext` (scoped, settável). Ordem de resolução: valor explícito do scope (HTTP) → `CompanyAmbient` (jobs, via `TryGetCached`) → fallback config. Flag `_consumed`: se `Set()` vier depois de `ConnectionString` já lido no scope, loga warning (pega bug de ordem em dev).
- `AddCompanyContext()` em `CompanyServiceCollectionExtensions`.
- Tests: `Companies/CompanyContextTests`, `CompanyCatalogTests`; `Support/FakeCompanyContext` para os demais testes.

### 2. Schema e entidades (`SiagroB1.Domain/Entities/Common/`)

- `Company`: `ConnectionString` → `VARCHAR(500)`; `SapCompanyDb` → `VARCHAR(100)` nullable de verdade. **Não** tocar `Code` (AlterColumn em PK falha).
- `UserSession`: `+ string? CompanyCode` `VARCHAR(4)`, sem FK (sessão é histórico).
- `UserProfile`: `+ string? CompanyCode` `VARCHAR(4)` com FK → `COMPANIES` (NoAction) e nav `Company`; índice único passa a `(UserId, ProfileCode, CompanyCode)`. Nullable no schema porque é o bootstrap (não a migration) que conhece a empresa padrão; os serviços sempre preenchem.
- **Migration Common 1** `AddCompanyCodeToUserSessionsAndUsersProfiles` (gerar com `--context CommonDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web` e revisar).

### 3. DI por host

**Web `Program.cs`**
```csharp
builder.Services.AddCompanyContext();
builder.Services.AddSingleton<FinishedContractMutationGuardInterceptor>();
builder.Services.AddSingleton(new PurchaseFiscalReconciliationCleanupInterceptor(ErpMode.IsStandalone(builder.Configuration)));

builder.Services.AddDbContext<AppDbContext>((sp, options) =>
{
    options.UseSqlServer(sp.GetRequiredService<ICompanyContext>().ConnectionString, b => { /* MigrationsAssembly, SplitQuery */ });
    options.AddInterceptors(sp.GetRequiredService<FinishedContractMutationGuardInterceptor>(),
                            sp.GetRequiredService<PurchaseFiscalReconciliationCleanupInterceptor>());
    options.EnableSensitiveDataLogging();
});
builder.Services.AddScoped<IDbConnection>(sp => new SqlConnection(sp.GetRequiredService<ICompanyContext>().ConnectionString));
builder.Services.AddHangfire(c => c.UseSqlServerStorage(builder.Configuration.GetConnectionString("SiagroCommon")).UseFilter(new CompanyJobFilter()));
```
Após `Build()`: guard da **Common** → `CompanyBootstrap.RunAsync` → guard de **todas as empresas** (ordem importa: o bootstrap precisa das colunas novas).

**Gateway**: `AddCompanyContext()`, mesmo lambda no `AppDbContext`, `CompanyBootstrap.RunAsync` defensivo (DbException ⇒ log e segue).
**Reports**: `AddCompanyContext()`, mesmo lambda, `IDbConnection` via contexto (mantém `System.Data.SqlClient`).

Test do mecanismo: `Companies/AppDbContextPerCompanyScopeTests` — `UseInMemoryDatabase(sp.GetRequiredService<ICompanyContext>().ConnectionString)`, dois scopes com `Set()` distintos veem stores distintos; scope sem empresa vê o fallback.

### 4. Resolução por requisição (`SiagroB1.Security`)

- `CookieAuthMiddleware.InvokeAsync(..., ICompanyCatalog catalog)`: após achar a sessão, `code = session.CompanyCode ?? catalog.DefaultCompanyCode` (sessões anteriores ao deploy); `context.RequestServices.GetRequiredService<ICompanyContext>().Set(company)`; claim `CompanyCode` no principal.
- `BasicAuthenticationHandler`: mesmo `Set()` no caminho cookie; caminho Basic usa header opcional `X-Company` (ausente + uma empresa ⇒ ela; ausente + várias ⇒ falha "Informe a empresa no cabeçalho X-Company."); grava na sessão; aplica a regra de acesso via helper `CompanyAccessPolicy.CanLoginAsync(user, code, db)` em `Security/Shared/`.
- `ClaimsPrincipalExtensions.CompanyCode()` (só diagnóstico; dados sempre via `ICompanyContext`).

### 5. Login, status, info, menus

- DTOs: `LoginRequest.CompanyCode?`; `UserInfo + CompanyCode, CompanyName`; novo `CompanyOption {Code, CompanyName}`.
- `IAuthService`: `LoginAsync(username, password, companyCode?)`, `LoginWithBasicAuthAsync(header, companyCode?)`, `ListCompaniesAsync()`.
- `AuthService` (injeta catálogo + contexto): resolve empresa (código inexistente ⇒ "Empresa inválida."; null + uma ⇒ ela; null + várias ⇒ "Selecione a empresa."); após validar senha, `!IsAdmin && !Any(UserProfiles where UserId && CompanyCode)` ⇒ "Usuário sem acesso à empresa {Nome}."; `session.CompanyCode = code`; `companyContext.Set(company)` **antes** de `GetPermissionsAsync/GetRolesAsync`, que ganham `&& up.CompanyCode == companyContext.CompanyCode`; `GetUserInfoAsync` preenche empresa; claim `CompanyCode`.
- `MenuService.GetMenuAsync` e `UserPermissionsService.GetAsync/HasRoleAsync`: mesmo predicado (bypass admin em `HasAsync` inalterado).
- Gateway `AuthController`: `GET security/auth/companies` (anônimo, Code+Nome ordenado); `login` passa `CompanyCode`; `login/basic` com `[FromHeader(Name="X-Company")]`; `status` devolve `CompanyCode/CompanyName`; `info` usa `companyContext.Current?.CompanyName ?? configuration["CompanyName"]` + `CompanyCode`; `logout` inalterado.
- Tests: `Security/AuthServiceCompanyTests` (sem perfil na empresa ⇒ recusa com msg pt-BR; admin entra em qualquer; sessão grava código; permissões só da empresa; auto-seleção com uma; "Selecione a empresa." com várias), `MenuServiceCompanyScopeTests`, estender `UserPermissionsServiceTests` com perfil em outra empresa que não pode vazar.

### 6. CRUD de Empresas endurecido (`Application/Services/Companies/`, `Web`)

- `ICompanyDatabaseProbe`/`CompanyDatabaseProbe`: `SqlConnection.OpenAsync` + `AppDbContext` ad hoc com `UseSqlServer(cs, MigrationsAssembly)` → `GetPendingMigrationsAsync()`; devolve `CanConnect, Error, PendingMigrations, Server, Database`.
- `CompanyConnectionStrings`: `Mask(cs)` (senha → `*****` via `SqlConnectionStringBuilder`), `IsMasked`, `MergeMaskedPassword(incoming, stored)`.
- `CompaniesGetService`: devolve `ConnectionString` mascarada (projeção; `[EnableQuery]` continua). `Create`: `Code` 4 chars `[A-Z0-9]`, cs não mascarada, banco ≠ catálogo da Common, probe conecta e **sem** migrations pendentes (mensagem pt-BR com o comando `dotnet ef ... --connection`), `Invalidate()`. `Update`: senha mascarada mantém a armazenada; re-probe se mudou; `Code` imutável. `Delete`: recusa empresa padrão e empresa com perfis/sessões ativas.
- Novo `CompaniesTestConnectionService` + action OData `CompaniesTestConnection(Code, ConnectionString)` (seguir `BranchNfeSettingsTestConnection`, `ODataConfigurations.cs:91`), controller em `Web/Actions/Companies/`.
- `CompaniesController` e a action: gate admin (`User.IsAdmin() || HasRoleAsync(Roles.Admin)` ⇒ 403 pt-BR, como `UsersSyncFromSapController`).
- `UsersProfilesCreateService`: `CompanyCode ??= companyContext.CompanyCode`; valida empresa; duplicidade `(UserId, ProfileCode, CompanyCode)` ⇒ "Este perfil já está atribuído ao usuário nesta empresa." `Update` copia `CompanyCode`.
- Registrar no `AddApplicationServices()`.
- Tests: `CompanyConnectionStringsTests`, `CompaniesCreate/UpdateServiceTests` com `FakeCompanyDatabaseProbe`, `UsersProfilesCreateServiceTests`.

### 7. Hangfire (`Application/Jobs/Companies/`)

- `CompanyJobFilter : JobFilterAttribute, IServerFilter`: `OnPerforming` extrai o 1º parâmetro `string companyCode` do job e faz `CompanyAmbient.Set(code)`; `OnPerformed` limpa.
- `CompanyScopedJobRunner` (scoped, sem `AppDbContext`): dicionário `jobKey → Func<IServiceProvider, CancellationToken, Task>` para `storage-daily-calculation`, `contract-notification-sweep`, `contract-drafts-reconcile`. `RunForAllCompaniesAsync(jobKey, ct)`: para cada empresa do catálogo, `CompanyAmbient.Use(code)` + scope novo + `ICompanyContext.Set` + executa; falha de uma não impede as outras; ao final `AggregateException` se houve falha. Chave string (não método genérico) para o dashboard ficar legível.
- Registro em Web `Program.cs` (mantém os mesmos ids recorrentes para o `AddOrUpdate` substituir): os três via `RecurringJob.AddOrUpdate<CompanyScopedJobRunner>(id, r => r.RunForAllCompaniesAsync(key, CancellationToken.None), cron)`; `sap-user-sync` inalterado.
- Enqueue: `IContractNotificationDispatchJob.ExecuteAsync(string companyCode, Guid outboxKey, ct)`; `ContractNotificationSweepJob.cs:56` e `NotificationOutboxResendService.cs:37` passam `companyContext.CompanyCode`. Atualizar `FakeBackgroundJobClient` (pegar o 1º `Guid`; expor `EnqueuedCompanyCodes`).
- Tests: `CompanyScopedJobRunnerTests` (duas empresas, uma falha, a outra roda), `CompanyJobFilterTests` (`Job.FromExpression` → código extraído), ajustar testes dos jobs de notificação.

### 8. Conexões cruas e relatórios

- `NfeNumberReservationService`: injeta `ICompanyContext`; `EmissionLockConnectionString()` = `new SqlConnectionStringBuilder(companyContext.ConnectionString) { Pooling = false }`. Ajustar `NfeNumberReservationServiceTests`.
- `FastReportService:76`: `companyContext.ConnectionString`.
- `ReportHeaderService`: nome = `companyContext.Current?.CompanyName ?? config["CompanyName"]`; logo = `wwwroot/images/logo-{Code}.png` se existir, senão `CompanyLogoPath`; cache vira `ConcurrentDictionary<string, byte[]?>` por caminho (o slot único thrasharia com duas empresas). Estender `ReportHeaderServiceTests`.

### 9. Bootstrap e guard

- `Security/Startup/CompanyBootstrap.RunAsync(IServiceProvider)`: se `COMPANIES` vazia, insere `{Company:Code ?? "0001", Company:Name ?? CompanyName, SiagroDB}` (tolera corrida Web×Gateway via `DbUpdateException` + re-check); define `def` = código configurado se existir, senão o primeiro (com warning); preenche `UserProfiles.CompanyCode == null` e `UserSessions` ativas sem empresa (load + loop, não `ExecuteUpdateAsync`, para o teste InMemory); `Invalidate()`; `DbException` ⇒ log e segue. Tests: `CompanyBootstrapTests` (vazio ⇒ insere+atribui; 2ª execução no-op; não vazio ⇒ só atribui).
- `PendingMigrationsGuard`: dividir em `EnsureNoPendingCommonMigrationsAsync` e `EnsureNoPendingCompanyMigrationsAsync`; a segunda itera o catálogo + fallback config se não cadastrado, um scope por empresa com `ICompanyContext.Set`, chave `AppDbContext[{code}]`; `DbException` em uma empresa loga e continua. `Evaluate()` segue puro.

### 10. Migrations e commits

| Migration (CommonContext) | Conteúdo | `DB:` |
|---|---|---|
| `AddCompanyCodeToUserSessionsAndUsersProfiles` | §2 | `DB: AddCompanyCodeToUserSessionsAndUsersProfiles` |
| `AddCompaniesMenu` | `MENU_ITEMS` key `companies`, "Empresas", `sap-icon://building`, pai `admin` + `ROLE_MENUS` ADMIN (padrão `20261015214856_AddSalesContractWashoutApprovalMenu.cs`) | `DB: AddCompaniesMenu` |

Branch `feature/multi-company-login` nos dois repos. Commits sugeridos (backend): `feat(security): contexto de empresa por sessão e catálogo de empresas` (migration 1) → `feat(platform): AppDbContext, Dapper e Hangfire resolvem a conexão pela empresa da sessão` → `feat(security): login com seleção de empresa e perfis por empresa` → `feat(master-data): cadastro de empresas com teste de conexão e menu` (migration 2) → `feat(reports): nome e logo por empresa da sessão`.

---

## Frontend (`siagro-b1-frontend/webapp`)

- `model/ServerRoutes.ts`: `loginCompanies: '/security/auth/companies'`, `companies: '/odata/Companies'`, `companiesTestConnection`.
- `types/`: `UserIdentity + companyCode?, companyName?`; `SystemInfo + companyCode`; `CompanyOption`.
- `services/SessionService.ts`: `Credentials + CompanyCode?`; `LAST_COMPANY_KEY` em localStorage (sobrevive ao logout, como `THEME_KEY`); `loadLoginCompanies()`; `login()` grava a última; `applyUserIdentity` publica `/companyCode`, `/companyName`; `clearSessionState` zera os dois (não o localStorage). `/systemInfo` (título do ShellBar, `App.view.xml:16`) já vira o nome da empresa da sessão.
- Login (`view/login/Main.view.xml`, `controller/login/Main.controller.ts`): `Select` "Empresa" acima de "Usuário", `items="{auth>/Companies}"`, visível só com >1 empresa, pré-seleciona a última/primeira; `onLogin` valida ("Selecione a empresa."); falha ao carregar lista ⇒ `MessageBox.error` mas deixa logar (backend auto-seleciona quando só há uma).
- Menu do avatar (`fragments/UserMenu.fragment.xml`): "Trocar Empresa" (`sap-icon://building`) após "Trocar Filial"; `App.controller.ts.onChangeCompany()` = `DialogHelper.confirmDialog(...)` → `SessionService.logout()` (mesma forma de `onLogout`, :270).
- Usuários (`view/users/fragments/Profiles.fragment.xml`, `controller/users/BaseController.ts`): coluna "Empresa" com value help (`CommonController.openCompaniesValueHelp` → `applyValueHelp(ev, "CompaniesSelectDialog", ["Code","CompanyName"], "Code")`, novo `dialogs/fragments/CompaniesSelectDialog.fragment.xml` clonado de `ProfilesSelectDialog`), descrição via `Company/CompanyName` (`autoExpandSelect`); `onAddProfile` cria com `CompanyCode = sessionModel>/companyCode`.
- Tela `companies` (clonar `docNumbers`): `view/companies/{Main,Add,Edit}.view.xml` + `fragments/Form.fragment.xml` (Código `maxLength=4` editável só no Add, Nome, String de conexão, `SapCompanyDb` visível se `sessionModel>/erp === 'SAPB1'`, botão "Testar conexão"); `controller/companies/{BaseController,Main,Add,Edit}.controller.ts` (`onTestConnection` → `bindContext(companiesTestConnection)` → `MessageBox` com servidor/banco/pendências); `manifest.json` rotas `companies`, `companiesNew`, `companiesEdit` + targets (`clearControlAggregation: true`). Nome da rota = key do menu.
- Textos pt-BR hardcoded como nas demais telas de cadastro.

---

## Riscos conhecidos

1. **Empresa setada tarde demais no scope** ⇒ cai no fallback silenciosamente. Mitigação: warning `_consumed` + ordem middleware→controller; em jobs, ambient antes do scope; novo código que use `IServiceScopeFactory` depende do `AsyncLocal`.
2. **OData `$batch`**: `UseODataBatching` vem antes de `UseCookieAuth`; sub-requests reentram o pipeline. Verificar no dev run que um PATCH batched acerta o banco (se compartilham `RequestServices`, o 1º `Set()` vence — mesma sessão, mesmo resultado).
3. **Entradas sem sessão ficam no fallback**: webhook D4Sign (`/hooks/d4sign/{secret}`), WebSocket da balança, `scales/{code}/capture` sem sessão. Multi-empresa aí é fora de escopo; documentar.
4. **Storage do Hangfire muda de banco**: DDL na Common na primeira subida; jobs em voo no schema antigo se perdem (só dispatch de WhatsApp, re-varrido a cada minuto).
5. **Staleness entre processos**: empresa nova é imediata (miss ⇒ reload); edição de cs existente propaga em até 60 s.
6. **Cookie `SIAGROB1.User` sem sessão válida** em `GetStatus` devolve `Authenticated=true` — pré-existente; não tratado aqui.
7. **`dotnet ef` design time** usa o fallback config; `--connection` sobrescreve.
8. Vigiar `ManyServiceProvidersCreatedWarning` no log após a mudança dos interceptors.

## Verificação

1. `dotnet build SiagroB1.sln`; `dotnet test SiagroB1.Application.Tests` (novos e ajustados conforme seções).
2. Bancos locais: `CEAGUI_SIAGRO_DEV` (existe) + `LMCEREAIS_SIAGRO_DEV` novo; `dotnet ef database update --context AppDbContext --connection "<cs2>"`; `dotnet ef database update --context CommonDbContext` (2 migrations).
3. Subir Web/Gateway/Reports com `--launch-profile ceagui`. Logs: "Empresa padrão 0001 cadastrada", guard lista `AppDbContext[0001]`, sem `ManyServiceProvidersCreatedWarning`.
4. Admin: Administração → Empresas → cadastrar `0002`/"LM CEREAIS"; "Testar conexão" mostra servidor/banco; cs com senha errada ⇒ erro; banco sem migration ⇒ recusa com instrução.
5. Usuário não-admin com perfil só em `0002`: login mostra Select com duas empresas e lembra a última; recusado em `0001` ("Usuário sem acesso à empresa ..."), aceito em `0002`; menu/permissões só da `0002`.
6. ShellBar mostra o nome da empresa; "Trocar Empresa" sai e volta ao login com a empresa anterior pré-selecionada.
7. Diálogo de filial lista as filiais do banco certo; documento criado em `0002` não aparece em `0001`.
8. `/hangfire`: recorrentes mostram `CompanyScopedJobRunner.RunForAllCompaniesAsync("...")`; disparar o sweep com outbox pendente em `0002` ⇒ dispatch enfileirado com args `("0002", <guid>)`.
9. Reports: PDF em cada empresa com cabeçalho diferente; `logo-0002.png` troca o logo (DANFE incluso).
10. NF-e: reservar número em `0002` move `NextNumber` só no segundo banco.
11. Frontend: `yarn ts-typecheck && yarn lint && yarn ui5lint && yarn test`; verificar pelo caminho do usuário no navegador (Playwright/Chrome).

## Processo após aprovação

Este plano é o **spec**. Ao aprovar: salvar em `siagro-b1-backend/docs/superpowers/specs/2026-10-16-multi-company-login-design.md` (commit em `feature/multi-company-login`), invocar `superpowers:writing-plans` para o plano de implementação passo a passo com TDD, e executar por tarefas com commits por etapa. Nunca push; merge em `main` é decisão do usuário.
