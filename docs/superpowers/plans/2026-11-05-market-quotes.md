# Mercado — Parte 1: Cotações — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Cadastro de instrumentos, cotação atual + fechamento diário (CMA ou manual), cliente de streaming da CMA por empresa e o Terminal de Cotações ao vivo, tudo atrás da chave `Modules:Market:Enabled`.

**Architecture:** Entidades no `AppDbContext` (um banco por empresa). Um `BackgroundService` no Web mantém uma sessão CMA por empresa com integração ligada e grava as cotações num store em memória (singleton, chave empresa+símbolo) que é descarregado no banco a cada 5 s; o Terminal recebe as cotações por SSE (`/market/quotes/live`), no padrão de `ScalesController`/`ScaleLiveService.ts`. Fechamento diário via Hangfire + `CompanyScopedJobRunner`.

**Tech Stack:** .NET 10, EF Core SQL Server, OData, Hangfire, xUnit + EF InMemory; OpenUI5 TypeScript.

**Spec:** `docs/superpowers/specs/2026-11-05-market-trading-module-design.md` (§4.1, §6 "Fechamento diário", "Respostas do usuário")

**Repos:** backend `C:\Projetos\SiagroB1\siagro-b1-backend` e frontend `C:\Projetos\SiagroB1\siagro-b1-frontend`, ambos na branch `feature/market-quotes` (criar no frontend). Commits separados por repo; `git add` de todo arquivo novo logo ao criar; **nunca push**. Escopo de commit do backend: `platform`.

**Referência CMA:** `C:\Projetos\Tagui\Siagro\cma-data-feed\src\main\java\br\com\idxtec\cmadatafeedservice\infrastructure\streamer\StreamerConnection.java` (login, canal persistente, ping 30 s, `QuotesRequest`) e `entities\CmaQuoteReponse.java` (campos `last10`, `bid14`, `high16`, `low17`, `open18`, `close1A`, `percentageChange01`, `askNumber03`, `datetime`).

## Global Constraints

- Identificadores em inglês, tabelas UPPER_SNAKE plural; textos ao usuário em pt-BR (i18n).
- Entidades POCO com data annotations (padrão `LogisticRegion.cs`); serviços registrados à mão em `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs`; entity sets em `SiagroB1.Web/ODataConfig/ODataConfigurations.cs`.
- Tudo do módulo só existe com `Modules:Market:Enabled = true` (default `false` no `appsettings.json` do Web): menus ocultos, hosted service ocioso, job removido (`RecurringJob.RemoveIfExists`), endpoints respondem 404.
- Valores da CMA vêm como texto: sufixo `S` indica ajuste (settlement) e deve ser removido guardando a flag; `-` ou vazio = sem valor (`null`, não 0). Decimal com ponto.
- Senha da CMA criptografada com `NfeOptions.Cipher()` (padrão `EmailSettingsService.cs:64`); nunca sai pela API.
- Origem de toda cotação registrada: `Cma` ou `Manual` + usuário.
- Precedência do fechamento: **Manual > ajuste "S" da CMA > último**; job às **18:00 America/Sao_Paulo** (passar `TimeZoneInfo` no `RecurringJob.AddOrUpdate`, o padrão do projeto é UTC).
- Menu de topo do módulo com ícone `sap-icon://folder-blank`; Key do menu = nome da rota no manifest.
- Migration nova exige trailer `DB:` no commit.

## Review Focus

1. CMA cai ou credencial errada → status "Desconectado/Erro" no terminal, reconexão com backoff (5 s → 60 s), nenhuma exceção derruba o Web. Teste no Task 5.
2. Cotação manual lançada enquanto a CMA está ligada → a CMA não sobrescreve o valor manual do dia até o usuário "liberar" (campo `ManualOverride`). Teste no Task 3.
3. Valores `"-"`, `""`, `"1188.25S"`, `"1,5"` inválido → parse tolerante: null/ajuste/rejeita sem exceção. Teste no Task 2.
4. Duas empresas com CMA → cotações de uma nunca aparecem no SSE da outra. Teste no Task 4.
5. Instrumento inativado → sai da assinatura e do terminal, mas o histórico de fechamentos permanece. Teste no Task 3.

---

### Task 1: Chave do módulo + entidades + migration

**Files:**
- Create: `SiagroB1.Infra/MarketModule.cs` — `public static class MarketModule { public const string ConfigKey = "Modules:Market:Enabled"; public static bool IsEnabled(IConfiguration c); }`
- Create: `SiagroB1.Domain/Enums/MarketInstrumentType.cs` (`Future=0, Spot=1, CurvePoint=2`), `MarketQuoteUnit.cs` (`UsCentsPerBushel=0, BrlPerBag=1, BrlPerUsd=2, UsdPerTon=3`), `MarketQuoteSource.cs` (`Cma=0, Manual=1`), `MarketExchange.cs` (`Cbot=0, B3=1, Spot=2`)
- Create: `SiagroB1.Domain/Entities/Market/MarketInstrument.cs` → `MARKET_INSTRUMENTS`: `Code` (key, VARCHAR(30), = símbolo CMA), `CmaSourceId` (int?), `Description`, `Exchange`, `Type`, `Commodity` (VARCHAR(20): `SOY`, `CORN`, `USD`…), `ItemCode` (nullable, item do SiagroB1), `ExpirationDate` (date?), `QuoteUnit`, `ContractSize` (decimal, nº de unidades por lote), `TonFactor` (decimal: multiplica a cotação para US$/t ou R$/t; soja 0,36745, milho 0,393678), `CurveDays` (int?, para CurvePoint), `GroupName` (aba do terminal, VARCHAR(40)), `SortOrder`, `Active`
- Create: `SiagroB1.Domain/Entities/Market/MarketQuote.cs` → `MARKET_QUOTES`, key `InstrumentCode` (FK): `Last`, `Bid`, `Ask`, `ChangePercent`, `High`, `Low`, `Open`, `PreviousClose`, `IsSettlement` (bool), `QuotedAt` (datetime2), `Source`, `UserName` (nullable), `ManualOverride` (bool), `UpdatedAt` — decimais `DECIMAL(18,6)` nullable
- Create: `SiagroB1.Domain/Entities/Market/MarketDailyClose.cs` → `MARKET_DAILY_CLOSES`, key `Id` int identity, índice único (`InstrumentCode`, `Date`): `Date` (date), `Close` decimal(18,6), `Source`, `IsSettlement`, `UserName`, `CreatedAt`, `UpdatedAt`
- Create: `SiagroB1.Domain/Entities/Market/CmaSettings.cs` → `CMA_SETTINGS` singleton `Key=1` (padrão `EmailSettings.cs`): `Enabled`, `Host` (default `https://strfeedrt02.cma.com.br:443`), `UserName`, `PasswordEncrypted` (varbinary)
- Modify: `SiagroB1.Infra/Context/AppDbContext.cs` — 4 DbSets
- Modify: `SiagroB1.Web/appsettings.json` — `"Modules": { "Market": { "Enabled": false } }`
- Create: migration `AddMarketQuotes` (AppDbContext), **sem seed** — os instrumentos de exemplo são criados pelo botão "Criar exemplos" (Task 3), para não pôr dados em clientes sem o módulo.
- Test: `SiagroB1.Application.Tests/Market/MarketModuleTests.cs`

- [ ] **Step 1:** Teste `IsEnabled_ReturnsFalse_WhenKeyMissing` e `IsEnabled_ReturnsTrue_WhenTrue` usando `TestConfiguration`.
- [ ] **Step 2:** `dotnet test SiagroB1.Application.Tests --filter FullyQualifiedName~MarketModuleTests` → FAIL (tipo inexistente).
- [ ] **Step 3:** Implementar `MarketModule`, enums, entidades, DbSets, chave no appsettings.
- [ ] **Step 4:** Mesmo teste → PASS; `dotnet build SiagroB1.sln` sem erros.
- [ ] **Step 5:** `dotnet ef migrations add AddMarketQuotes --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web`; revisar o SQL (`dotnet ef migrations script`) — só CREATE TABLE/índices.
- [ ] **Step 6:** Commit `feat(platform): entidades de instrumentos e cotações do módulo Mercado` com trailer `DB: AddMarketQuotes`.

### Task 2: Parser de valores da CMA

**Files:**
- Create: `SiagroB1.Application/Services/Market/Cma/CmaValueParser.cs` — `public static CmaValue Parse(string? raw)`; `public readonly record struct CmaValue(decimal? Value, bool IsSettlement)`
- Create: `SiagroB1.Application/Services/Market/Cma/CmaQuoteMapper.cs` — `public static MarketQuoteUpdate? Map(JsonElement quote)` lendo os campos CMA `symbol`, `last10`, `bid14`, `askNumber03`, `percentageChange01`, `high16`, `low17`, `open18`, `close1A`, `datetime` (nomes exatos conferidos em `CmaQuoteReponse.java` e no JSON real de `StreamerConnection.java`); `public sealed record MarketQuoteUpdate(string Symbol, decimal? Last, decimal? Bid, decimal? Ask, decimal? ChangePercent, decimal? High, decimal? Low, decimal? Open, decimal? PreviousClose, bool IsSettlement, DateTime QuotedAt)`
- Test: `SiagroB1.Application.Tests/Market/CmaValueParserTests.cs`, `CmaQuoteMapperTests.cs`

- [ ] **Step 1:** Testes (Theory):
  - `"1188.25S"` → `(1188.25m, true)`; `"1189.5"` → `(1189.5m, false)`; `"-"`, `""`, `null`, `"  "` → `(null, false)`; `"-1.16"` → `(-1.16m, false)`; `"abc"` → `(null, false)`.
  - Mapper: JSON `{"symbol":"ZSF7","last10":"1188.25S","bid14":"1189.5","percentageChange01":"-1.16","high16":"1210","low17":"1186.75","open18":"1202.25"}` → `Symbol="ZSF7"`, `Last=1188.25`, `IsSettlement=true`, `Bid=1189.5`, `ChangePercent=-1.16`, `High=1210`. Sem `symbol` → `null`.
- [ ] **Step 2:** Rodar → FAIL.
- [ ] **Step 3:** Implementar (InvariantCulture; `IsSettlement` vem do campo `last10`).
- [ ] **Step 4:** Rodar → PASS.
- [ ] **Step 5:** Commit `feat(platform): parser das cotações da CMA`.

### Task 3: Serviços de instrumentos, cotações e lançamento manual

**Files:**
- Create: `SiagroB1.Application/Services/Market/MarketInstrumentService.cs` — `: BaseService<MarketInstrument, string>`; mais `Task<int> CreateSamplesAsync(CancellationToken)` (cria, se não existirem, os exemplos: soja CBOT `ZSF7,ZSH7,ZSK7,ZSN7,ZSX6` grupo "Soja CBOT"; milho CBOT `ZCH7,ZCK7,ZCN7` "Milho CBOT"; milho B3 `CCMF27,CCMH27` "Milho B3"; `DOL COM` "Dólar comercial"; `CURVA DE DOLAR 30D/60D/90D/180D/360D` "Curva de dólar" com `CurveDays`; fatores de §Task 1)
- Create: `SiagroB1.Application/Services/Market/MarketQuoteService.cs`:
  - `Task<IReadOnlyList<MarketQuoteView>> GetBoardAsync(CancellationToken)` — instrumentos ativos + cotação, ordenados por `GroupName`, `SortOrder`
  - `Task ApplyCmaUpdatesAsync(IReadOnlyCollection<MarketQuoteUpdate> updates, CancellationToken)` — upsert; ignora símbolo desconhecido ou inativo; **não sobrescreve** se `ManualOverride`
  - `Task SetManualAsync(string instrumentCode, ManualQuoteInput input, string userName, CancellationToken)` — grava `Source=Manual`, `ManualOverride=true`
  - `Task ReleaseManualAsync(string instrumentCode, CancellationToken)` — `ManualOverride=false`
  - `public sealed record ManualQuoteInput(decimal Last, decimal? Bid, decimal? Ask, decimal? High, decimal? Low, decimal? Open, bool IsSettlement)`
  - `public sealed record MarketQuoteView(string Code, string Description, string GroupName, int SortOrder, decimal? Last, decimal? Bid, decimal? ChangePercent, decimal? High, decimal? Low, decimal? Open, bool IsSettlement, DateTime? QuotedAt, MarketQuoteSource? Source, bool ManualOverride)`
- Create: `SiagroB1.Application/Services/Market/MarketDailyCloseService.cs` — `Task<int> CloseDayAsync(DateOnly date, CancellationToken)` (um registro por instrumento ativo com cotação; precedência Manual > settlement > último; **não sobrescreve** fechamento existente de origem Manual) e `Task SetManualCloseAsync(string code, DateOnly date, decimal close, string userName, CancellationToken)`
- Modify: `ServiceCollectionExtensions.cs` — registrar os três
- Test: `SiagroB1.Application.Tests/Market/MarketQuoteServiceTests.cs`, `MarketDailyCloseServiceTests.cs`, `MarketInstrumentServiceTests.cs` (usar `TestDb`)

- [ ] **Step 1:** Testes:
  - `ApplyCmaUpdates_Upserts_KnownActiveInstrument`; `ApplyCmaUpdates_Ignores_UnknownOrInactive`; `ApplyCmaUpdates_DoesNotOverwrite_ManualOverride` (manual 1200 → CMA 1188.25 → continua 1200); `ReleaseManual_ThenCmaOverwrites`.
  - `GetBoard_ExcludesInactive_ButDailyClosesRemain` (inativar após fechamento → board sem ele, `MARKET_DAILY_CLOSES` com 1 linha).
  - `CloseDay_Precedence`: manual → `Source=Manual`; settlement → close = Last com `IsSettlement`; senão Last. `CloseDay_DoesNotOverwrite_ManualClose`. `CloseDay_IsIdempotent` (rodar 2× = 1 linha por instrumento/data).
  - `CreateSamples_IsIdempotent` (2× → mesmo total; `ZSF7.TonFactor == 0.36745m`).
- [ ] **Step 2:** Rodar `--filter FullyQualifiedName~Market` → FAIL.
- [ ] **Step 3:** Implementar.
- [ ] **Step 4:** Rodar → PASS.
- [ ] **Step 5:** Commit `feat(platform): serviços de cotação, lançamento manual e fechamento diário`.

### Task 4: Store em memória + SSE + endpoints REST/OData

**Files:**
- Create: `SiagroB1.Commons/Market/LiveQuoteStore.cs` — singleton: `void Publish(string companyCode, IReadOnlyCollection<MarketQuoteUpdate> updates)`, `void SetStatus(string companyCode, CmaConnectionStatus status, string? message)`, `LiveQuoteSnapshot Get(string companyCode)` com `long Version` incremental; `public enum CmaConnectionStatus { Disabled, Connecting, Connected, Error }`. (Se `MarketQuoteUpdate` não puder ficar em Commons por referência de projeto, mover o record para `SiagroB1.Commons/Market`.)
- Create: `SiagroB1.Web/Controllers/MarketController.cs` — `[Authorize][Route("market")]` ControllerBase:
  - `GET market/quotes/live` — SSE (cabeçalhos idênticos a `ScalesController.Live`); emite `data:{status, message, quotes:[MarketQuoteView]}` quando a `Version` da empresa atual muda, checando a cada 500 ms; primeiro evento com `GetBoardAsync`.
  - `GET market/quotes` → board; `POST market/quotes/{code}/manual` (body `ManualQuoteInput`); `POST market/quotes/{code}/release`; `POST market/daily-closes` (`{code,date,close}`); `POST market/instruments/samples`.
  - `GET market/cma-settings` (sem senha, `HasPassword`), `PUT market/cma-settings` (`{enabled, host, userName, password?}`; password vazio = mantém), `POST market/cma/reconnect`. Escrita de settings, reconnect e samples exigem role ADMIN (`HasRoleAsync`, ver memória "Role ADMIN ≠ flag IsAdmin").
  - Tudo retorna 404 se `MarketModule.IsEnabled` = false.
- Create: `SiagroB1.Application/Services/Market/CmaSettingsService.cs` — `GetAsync`, `SaveAsync`, `Task<CmaCredentials?> GetCredentialsAsync()` (decifra); `public sealed record CmaCredentials(string Host, string UserName, string Password)`
- Modify: `ODataConfigurations.cs` — `EntitySet<MarketInstrument>("MarketInstruments")` + `SiagroB1.Web/Controllers/MarketInstrumentsController.cs : ODataBaseController<MarketInstrument, string>`
- Modify: `SiagroB1.Gateway/appsettings.json` — rota `market-route` `/market/{**catch-all}` → cluster do Web, política `AuthenticatedOnly`, igual à rota `/scales` (linha ~54); conferir se a rota de scales desliga buffering de resposta e copiar.
- Test: `SiagroB1.Application.Tests/Market/LiveQuoteStoreTests.cs`, `CmaSettingsServiceTests.cs`

- [ ] **Step 1:** Testes: `Publish_IsolatedPerCompany` (publish em "A" → `Get("B").Version == 0`); `Publish_IncrementsVersion`; `SetStatus_Visible_InSnapshot`; `CmaSettings_Save_EncryptsPassword_AndGetNeverReturnsIt`; `CmaSettings_SaveWithEmptyPassword_KeepsPrevious`.
- [ ] **Step 2:** Rodar → FAIL.
- [ ] **Step 3:** Implementar.
- [ ] **Step 4:** Rodar → PASS; `dotnet build SiagroB1.sln`.
- [ ] **Step 5:** Commit `feat(platform): endpoints de cotação, SSE ao vivo e configuração da CMA`.

### Task 5: Cliente de streaming da CMA + hosted service

**Files:**
- Create: `SiagroB1.Application/Services/Market/Cma/ICmaStreamerClient.cs` — `Task<string> LoginAsync(CmaCredentials, CancellationToken)` (sessionId); `IAsyncEnumerable<JsonElement> OpenChannelAsync(CmaCredentials, string sessionId, CancellationToken)`; `Task SubscribeAsync(CmaCredentials, string sessionId, IReadOnlyCollection<(int? SourceId, string Symbol)> symbols, CancellationToken)`; `Task PingAsync(CmaCredentials, string sessionId, CancellationToken)`
- Create: `SiagroB1.Application/Services/Market/Cma/CmaStreamerClient.cs` — `HttpClient` tipado; porta fiel de `StreamerConnection.java`: GET `{host}/execute?<param>=<json>`; mesmas chaves JSON de login/quotes/ping; canal persistente lido linha a linha.
- Create: `SiagroB1.Application/Services/Market/Cma/CmaCompanySession.cs` — loop de uma empresa: credenciais → login → subscribe (instrumentos ativos com `CmaSourceId`) → lê canal, `CmaQuoteMapper.Map`, acumula, `LiveQuoteStore.Publish`; ping 30 s; a cada 5 s grava o lote acumulado via `MarketQuoteService.ApplyCmaUpdatesAsync` em scope com `CompanyAmbient.Use` + `ICompanyContext.Set` (padrão `CompanyScopedJobRunner`); erro → `SetStatus(Error, msg)`, backoff 5 s dobrando até 60 s; reassina quando a lista de instrumentos muda (recarrega a cada 60 s) e quando chega `POST market/cma/reconnect`.
- Create: `SiagroB1.Web/Market/CmaStreamingHostedService.cs` — `BackgroundService`: se módulo desligado, sai; a cada 60 s lista `ICompanyCatalog`, inicia/encerra `CmaCompanySession` por empresa conforme `CmaSettings.Enabled`. Exceção nunca escapa de `ExecuteAsync`.
- Modify: `ServiceCollectionExtensions.cs` / `Program.cs` — `AddHttpClient<ICmaStreamerClient, CmaStreamerClient>`, `AddHostedService<CmaStreamingHostedService>` (só se o módulo estiver ligado).
- Test: `SiagroB1.Application.Tests/Market/CmaCompanySessionTests.cs` com `FakeCmaStreamerClient`

- [ ] **Step 1:** Testes: `Session_PublishesMappedQuotes` (fake entrega 2 JSON → store tem 2 símbolos, status Connected); `Session_LoginFails_SetsErrorAndRetries` (fake lança 2×, depois ok → status final Connected, 3 chamadas de login; backoff injetado como `Func<int, TimeSpan>` retornando zero no teste); `Session_SubscribesOnlyActiveInstrumentsWithSourceId`; `Session_FlushesToDatabase` (após flush, `MARKET_QUOTES` tem os valores).
- [ ] **Step 2:** Rodar → FAIL.
- [ ] **Step 3:** Implementar.
- [ ] **Step 4:** Rodar → PASS; suíte `SiagroB1.Application.Tests` inteira verde.
- [ ] **Step 5:** Commit `feat(platform): cliente de streaming da CMA por empresa`.

### Task 6: Job de fechamento diário + menu + filtro do menu pela chave

**Files:**
- Create: `SiagroB1.Application/Jobs/MarketDailyCloseJob.cs` — `ExecuteAsync(CancellationToken)` chama `CloseDayAsync(hoje em America/Sao_Paulo)`
- Modify: `CompanyScopedJobRunner.cs` — const `MarketDailyClose` + entrada no dicionário
- Modify: `Program.cs` (~290-370) — se `MarketModule.IsEnabled`: `RecurringJob.AddOrUpdate<CompanyScopedJobRunner>("market-daily-close-job", r => r.RunForAllCompaniesAsync(CompanyScopedJobRunner.MarketDailyClose, CancellationToken.None), "0 18 * * 1-5", new RecurringJobOptions { TimeZone = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo") })`; senão `RemoveIfExists`
- Create: migration CommonContext `AddMarketMenu` (padrão `20261026155843_AddPaymentReleaseMenu.cs`): pai `market` "Mercado" ícone `sap-icon://folder-blank`; filhos `marketQuotesTerminal` "Terminal de Cotações", `marketInstruments` "Instrumentos", `marketCmaSettings` "Integração CMA"; `ROLE_MENUS` para ADMIN
- Modify: serviço de menu do `SiagroB1.Security` (localizar onde o menu do usuário é montado) — remover a árvore `market*` quando `MarketModule.IsEnabled` = false
- Test: `MarketDailyCloseJobTests.cs` (relógio injetado; data local de São Paulo: `2026-11-05T21:00Z` → `2026-11-05`; `2026-11-06T01:00Z` → `2026-11-05`) e teste do filtro de menu no projeto onde o serviço de menu já tem testes (se não houver, testar a função de filtro extraída como estática)

- [ ] **Step 1:** Testes acima → FAIL.
- [ ] **Step 2:** Implementar; rodar → PASS.
- [ ] **Step 3:** `dotnet ef migrations add AddMarketMenu --context CommonDbContext ...`; revisar script.
- [ ] **Step 4:** Commit `feat(platform): fechamento diário das cotações e menu do Mercado` com `DB: AddMarketMenu`.

### Task 7: Frontend — Terminal, Instrumentos e Integração CMA

Repo `siagro-b1-frontend`, branch `feature/market-quotes`.

**Files:**
- Modify: `webapp/model/ServerRoutes.ts` — `marketQuotesLive: '/market/quotes/live'`, `marketQuotes`, `marketManualQuote(code)`, `marketReleaseQuote(code)`, `marketCmaSettings`, `marketCmaReconnect`, `marketInstrumentSamples`, `marketInstruments: 'odata/MarketInstruments'`
- Create: `webapp/services/MarketLiveService.ts` — `EventSource` como `ScaleLiveService.ts`; `start(onData)`, `stop()`; fecha ao sair da rota
- Create: `webapp/types/MarketInstrument.ts`, `MarketQuote.ts`
- Create: `webapp/controller/marketQuotesTerminal/Main.controller.ts` + `webapp/view/marketQuotesTerminal/Main.view.xml` — `IconTabBar` com uma aba por `GroupName` (dinâmica), tabela `sap.m.Table` com Código, Descrição, Último (sufixo "S" quando ajuste), Compra, Var % (verde/vermelho), Máxima, Mínima, Abertura, Origem (ícone "manual"); `ObjectStatus` do status da conexão ("Conectado"/"Desconectado"/"Erro: …"); ação "Lançar cotação" (dialog com campos de `ManualQuoteInput`) e "Liberar para CMA" quando `manualOverride`; botão "Reconectar" só para ADMIN
- Create: `webapp/controller/marketInstruments/{Main,Add,Edit}.controller.ts` + views — CRUD padrão `logisticRegions`, + botão "Criar exemplos"
- Create: `webapp/controller/marketCmaSettings/Main.controller.ts` + view — Form (ColumnLayout) Ativo, Host, Usuário, Senha (placeholder "•••• (mantida)" quando `hasPassword`)
- Modify: `webapp/manifest.json` — rotas/targets `marketQuotesTerminal` (`market/quotes`), `marketInstruments` (+ `/new`, `/{id}/edit`), `marketCmaSettings`
- Modify: `webapp/i18n/i18n.properties` (+ `_en`)

- [ ] **Step 1:** Implementar serviços e telas.
- [ ] **Step 2:** `yarn ts-typecheck` e `yarn lint` → sem erros; `yarn ui5lint` sem erros novos.
- [ ] **Step 3:** Verificação ponta a ponta (memórias "Subir a stack local" e "Verificar pelo caminho do usuário"): stack local com `Modules:Market:Enabled=true`, migrations aplicadas; da home → menu Mercado → Instrumentos → "Criar exemplos" → Terminal: lançar cotação manual de ZSF7 e vê-la aparecer ao vivo em outra aba sem recarregar; desligar a chave → menu some e `/market/quotes` dá 404. CMA real só se houver credencial de teste; senão registrar como não verificado.
- [ ] **Step 4:** Commit frontend `feat(platform): terminal de cotações e cadastros do módulo Mercado` (convenção do `CLAUDE.md` do frontend).
