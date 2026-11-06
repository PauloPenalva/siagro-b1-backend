# Mercado — Parte 3: Hedges — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Registrar o que corretoras e bancos devolvem — operações de bolsa com casamento PEPS e resultado realizado, hedge de moeda (NDF/termo) com liquidação, EFP que fixa o preço de um contrato a fixar, e ajuste diário/margem informativos.

**Architecture:** Entidades novas no `AppDbContext`. Duas funções puras concentram a aritmética: `MarketUnits` (tamanho do lote em t ou US$, valor do ponto, moeda) e `MarketFifoMatcher` (casamento PEPS). Os casamentos são **recalculados por inteiro** para a conta × instrumento a cada gravação, o que torna edição e exclusão seguras. O EFP encadeia os serviços existentes de fixação de preço (criar → aprovar) de forma retomável, como a boleta da parte 2.

**Tech Stack:** .NET 10, EF Core SQL Server, OData, xUnit + EF InMemory; OpenUI5 TypeScript.

**Spec:** `docs/superpowers/specs/2026-11-05-market-trading-module-design.md` (§4.3, §6 "EFP", §8)

**Repos:** backend `C:\Projetos\SiagroB1\worktrees\market\backend` e frontend `C:\Projetos\SiagroB1\worktrees\market\frontend`, ambos na branch `feature/market-hedges` (criada a partir de `feature/market-pricing`). **Nunca push, nunca merge.** Escopo de commit: `platform`.

**Levantamentos:** `C:\Projetos\SiagroB1\siagro-b1-backend\.superpowers\recon\part34-backend.md` (§3 fixações, §8 papéis, §10 unidades), `part2-backend.md` (§4, §8), `part2-frontend.md`.

## Global Constraints

- Identificadores em inglês, tabelas UPPER_SNAKE plural; textos ao usuário em pt-BR.
- Todo controller novo leva `[MarketModuleEnabled]`; menus novos são filhos de `market`; serviços registrados à mão; `BusinessException` → 400 texto pt-BR; sem permissão → 403 texto pt-BR (mesmos mecanismos da parte 2: `MarketPermissionGuard`, `ForbiddenException`).
- Leitura: qualquer usuário autenticado com o módulo ligado. **Toda gravação desta parte exige `MARKET_MANAGE`.**
- O sistema **registra**, não negocia: nada aqui envia ordem a corretora ou banco, e nada gera lançamento financeiro ou contábil (ajuste diário, margem e resultado são informativos). A única exceção já existente é o título provisório que a fixação de preço do contrato gera pelo fluxo atual.
- Aritmética (funções puras, sem `if` por commodity):
  - Moeda da cotação: `UsCentsPerBushel`, `UsdPerTon` → `Usd`; `BrlPerBag`, `BrlPerUsd` → `Brl`.
  - Valor do ponto: `ContractSize × 0,01` para `UsCentsPerBushel`; `ContractSize` nos demais.
  - Toneladas por lote: `UsCentsPerBushel` → `ContractSize / (TonFactor × 100)`; `BrlPerBag` → `ContractSize / TonFactor`; `UsdPerTon` → `ContractSize`; `BrlPerUsd` → não se aplica (nocional em US$ = `ContractSize` por lote).
  - Resultado de um casamento: `(preço de fechamento − preço de abertura) × lotes × valor do ponto × (+1 se a abertura foi compra, −1 se venda)`, arredondado a 2 casas (`AwayFromZero`), na moeda da cotação.
  - Exemplos: soja CBOT (5000 bu, TonFactor 0,36745): 1 lote = **136,0729 t** (4 casas); compra 10 lotes a 1180,00 e venda a 1192,50 → **+6.250,00 US$**. Milho B3 (450 sc, TonFactor 16,666667): 1 lote = **27 t**; venda 5 lotes a 68,00 e compra a 66,50 → **+3.375,00 R$**. Dólar futuro (`BrlPerUsd`, 50.000): venda 2 lotes a 5,4500 e compra a 5,4000 → **+5.000,00 R$**.
- PEPS por **conta × instrumento**: ordenar por `TradedAt`, depois `Id`; cada operação consome primeiro os lotes abertos mais antigos do lado oposto; o que sobra fica aberto. Operações do mesmo lado nunca se casam.
- Hedge de moeda: resultado na liquidação = venda de US$: `(taxa contratada − taxa de liquidação) × US$`; compra: o inverso; 2 casas.
- EFP só em contrato **a fixar** (`Type == ToBeDetermined`) e `Approved`, em kg; preço da fixação por kg = `(preço da bolsa + prêmio) × TonFactor × câmbio / 1000` (cotações em US¢/bu ou US$/t) ou `(preço + prêmio) / 60` (R$/sc), 8 casas. Instrumento em US$ com contrato em R$ exige câmbio informado; instrumento e contrato em US$ → câmbio 1; instrumento em R$ com contrato em US$ → recusado.
- Datas de negócio em `DATE`; carimbos em UTC com `Z`. Migration nova exige trailer `DB:`. Não rodar `database update`.
- **Lições da parte 2 (obrigatórias, cada uma foi um defeito real encontrado no navegador ou na revisão):**
  - Controller OData do Mercado deriva de `MarketODataController`; o GET por chave devolve `SingleResult` sobre `IQueryable` (senão `$expand` vem vazio); `$expand` das navegações citadas no plano precisa funcionar em lista **e** por chave.
  - Todo carimbo UTC de entidade nova recebe o conversor de `UtcDateTimeConverters` no `AppDbContext` (senão sai com +3 h pelo OData).
  - Campo que o serviço preenche sozinho (`CreatedBy`, nomes copiados, carimbos) nunca é obrigatório para o cliente.
  - Erros de endpoint REST (`market/...`) saem como `text/plain` pt-BR (400/403/404); endpoints OData mantêm o formato de erro OData. Violação de índice único e estouro de decimal viram 400 pt-BR, nunca 500 cru.
  - Operação que encadeia serviços existentes com transação própria (EFP) grava **antes** as chaves que vai criar (chave pré-atribuída) e roda sob `KeyedAsyncLock` por empresa + documento; depois de marcada a vez não honra mais o token da requisição. Invariante testado: repetir ou concorrer nunca duplica operação nem fixação.
  - Frontend: `DateTimeOffset` ligado por OData leva `constraints: { precision: 7 }`; número digitado à mão usa `parseStrictDecimal` (recusa "1.500"); tela com edição em `UpdateGroup` descarta pendências ao sair da rota; grids em `sap.ui.table.Table` sem quebra de linha; valores sempre com casas fixas (R$ 2, câmbio 4, cotação como na bolsa); a última ação do implementador é a mensagem de devolução.

## Review Focus

1. Editar ou excluir uma operação antiga refaz o PEPS de toda a conta × instrumento; resultado realizado e lotes abertos ficam coerentes com a nova sequência. Testes nos Tasks 2 e 4.
2. Operação gerada por EFP não pode ser editada nem excluída fora do estorno do EFP. Teste no Task 4/6.
3. EFP que falha no meio (operação gravada, fixação criada, aprovação da fixação falhou) é retomável e não duplica operação nem fixação. Teste no Task 6.
4. Liquidar duas vezes, liquidar hedge cancelado, editar hedge liquidado → recusados. Teste no Task 5.
5. Instrumento sem `ContractSize` ou com `TonFactor` zero → erro de negócio claro ao gravar a operação, nunca divisão por zero. Testes nos Tasks 2 e 4.

---

### Task 1: Entidades e migrations

**Files:**
- Create enums em `SiagroB1.Domain/Enums/`: `MarketCounterpartyType` (`Broker=0, Bank=1`), `MarketTradeSide` (`Buy=0, Sell=1`), `MarketFuturesTradeReason` (`Normal=0, Rollover=1, Efp=2`), `MarketFxHedgeKind` (`Ndf=0, Forward=1`), `MarketFxHedgeStatus` (`Open=0, Settled=1, Canceled=2`), `MarketEfpStatus` (`Processing=0, Active=1, Canceled=2`), `MarketEfpContractSide` (`Purchase=0, Sales=1`)
- Create em `SiagroB1.Domain/Entities/Market/`:
  - `MarketCounterparty` → `MARKET_COUNTERPARTIES`: `Id`, `Name` (100, único), `Type`, `TaxId` (20)?, `Active`, `Accounts`
  - `MarketCounterpartyAccount` → `MARKET_COUNTERPARTY_ACCOUNTS`: `Id`, `CounterpartyId` (FK cascade), `Code` (30), `Description` (100)?, `Active`; único (`CounterpartyId`, `Code`)
  - `MarketFuturesTrade` → `MARKET_FUTURES_TRADES`: `Id`, `TradedAt` (UTC), `TradeDate` (DATE, data local do negócio), `AccountId` (FK restrict), `InstrumentCode` (FK restrict), `Side`, `Lots` int, `Price` DECIMAL(18,6), `Fees` DECIMAL(18,2), `FeesCurrency` (`CurrencyType`), `Reason`, `BrokerReference` (50)?, `OpenLots` int (mantido pelo casamento), `Confirmed`, `ConfirmedBy` (100)?, `ConfirmedAt`?, `PurchaseContractKey` Guid?, `SalesContractKey` Guid? (sem FK), `ContractCode` (50)?, `EfpId` int? (sem FK), `Notes` (500)?, `CreatedBy/At`, `UpdatedBy/At`, `[Timestamp] RowVersion`; índice (`AccountId`, `InstrumentCode`, `TradedAt`)
  - `MarketFuturesMatch` → `MARKET_FUTURES_MATCHES`: `Id`, `AccountId`, `InstrumentCode` (30), `OpenTradeId`, `CloseTradeId` (FKs restrict para trades), `Lots`, `OpenPrice`, `ClosePrice` DECIMAL(18,6), `RealizedPnl` DECIMAL(18,2), `Currency`, `CloseDate` (DATE)
  - `MarketFxHedge` → `MARKET_FX_HEDGES`: `Id`, `TradedAt` (UTC), `TradeDate` (DATE), `AccountId` (FK restrict), `Kind`, `Side`, `AmountUsd` DECIMAL(18,2), `ContractedRate` DECIMAL(18,6), `SettlementDate` (DATE), `Status`, `SettlementRate` DECIMAL(18,6)?, `SettledAt`?, `SettledBy`?, `RealizedPnlBrl` DECIMAL(18,2)?, `BankReference` (50)?, `Confirmed`, `ConfirmedBy`?, `ConfirmedAt`?, `PurchaseContractKey`?, `SalesContractKey`?, `ContractCode`?, `Notes` (500)?, `CreatedBy/At`, `UpdatedBy/At`, `RowVersion`
  - `MarketEfp` → `MARKET_EFPS`: `Id`, `Date` (DATE), `AccountId` (FK restrict), `InstrumentCode` (FK restrict), `Side`, `Lots`, `Price` DECIMAL(18,6), `Premium` DECIMAL(18,6), `FxRate` DECIMAL(18,6)?, `ContractSide`, `ContractKey` Guid (sem FK), `ContractCode` (50), `VolumeKg` DECIMAL(18,3), `FixationPrice` DECIMAL(18,8), `FinancialDueDate` (DATE), `FuturesTradeId` int?, `FixationKey` Guid?, `Status`, `Notes` (500)?, `CreatedBy/At`, `CanceledBy/At`, `RowVersion`
  - `MarketBrokerStatement` → `MARKET_BROKER_STATEMENTS`: `Id`, `AccountId` (FK restrict), `Date` (DATE), `DailyAdjustment` DECIMAL(18,2), `MarginRequired` DECIMAL(18,2), `MarginDeposited` DECIMAL(18,2), `Currency`, `Notes` (500)?, `CreatedBy/At`; único (`AccountId`, `Date`)
- Modify: `AppDbContext.cs` (7 DbSets + mapeamentos)
- Create: migration AppContext `AddMarketHedges`; migration CommonContext `AddMarketHedgesMenu`: filhos de `market` `marketFuturesTrades` "Operações de Bolsa" 40, `marketFxHedges` "Hedge de Moeda" 42, `marketEfps` "EFP" 44, `marketBrokerStatements` "Ajustes e Margem" 46, `marketCounterparties` "Corretoras e Bancos" 48 — `ROLE_MENUS` para ADMIN, `MARKET_TRADER` e `MARKET_BOARD` (idempotente, GUIDs fixos)
- Test: `SiagroB1.Application.Tests/Market/MarketHedgesModelTests.cs`

- [ ] **Step 1:** Teste com `TestDb`: gravar corretora com 2 contas; operação de bolsa; casamento ligando duas operações; hedge de moeda; EFP; extrato — ler de volta.
- [ ] **Step 2:** Rodar → FAIL. **Step 3:** Implementar. **Step 4:** Rodar → PASS; `dotnet build SiagroB1.sln`.
- [ ] **Step 5:** Gerar as migrations e revisar o script (App só CREATE TABLE/índices; Common só linhas).
- [ ] **Step 6:** Commit `feat(platform): entidades de hedge do módulo Mercado` com `DB: AddMarketHedges` e `DB: AddMarketHedgesMenu`.

### Task 2: Aritmética pura — unidades e casamento PEPS

**Files:**
- Create: `SiagroB1.Application/Services/Market/Hedges/MarketUnits.cs`, `MarketFifoMatcher.cs`
- Test: `SiagroB1.Application.Tests/Market/MarketUnitsTests.cs`, `MarketFifoMatcherTests.cs`

**Interfaces — Produces:**
```csharp
public static class MarketUnits
{
    public static CurrencyType QuoteCurrency(MarketQuoteUnit unit);
    public static decimal PointValue(MarketQuoteUnit unit, decimal contractSize);
    public static decimal? TonsPerLot(MarketQuoteUnit unit, decimal contractSize, decimal tonFactor); // null para BrlPerUsd
    public static decimal? UsdPerLot(MarketQuoteUnit unit, decimal contractSize);                     // só BrlPerUsd
    public static decimal RealizedPnl(MarketQuoteUnit unit, decimal contractSize, MarketTradeSide openSide, decimal openPrice, decimal closePrice, int lots);
    public static void EnsureTradable(MarketInstrument instrument); // BusinessException pt-BR: ContractSize <= 0; TonFactor <= 0 quando a unidade precisa dele
}
public static class MarketFifoMatcher
{
    public sealed record Leg(int Id, DateTime TradedAt, MarketTradeSide Side, int Lots, decimal Price);
    public sealed record Match(int OpenTradeId, int CloseTradeId, int Lots, decimal OpenPrice, decimal ClosePrice, MarketTradeSide OpenSide);
    public sealed record Result(IReadOnlyList<Match> Matches, IReadOnlyDictionary<int, int> OpenLots);
    public static Result Run(IEnumerable<Leg> legs);
}
```

- [ ] **Step 1:** Testes: os três exemplos das Global Constraints (toneladas por lote e resultado); `TonsPerLot` nulo para `BrlPerUsd`; `EnsureTradable` rejeita `ContractSize 0` e `TonFactor 0`. PEPS: compra 10, venda 4 → 1 casamento de 4, aberto 6 na compra e 0 na venda; compra 5 + compra 5, venda 7 → casa 5 da primeira e 2 da segunda; venda 3, compra 10 → casa 3 (abertura = a venda), sobra 7 comprado; virada de posição (compra 5, venda 8, compra 3) → tudo zerado, 2 casamentos da venda; mesma data-hora desempata por `Id`; lista vazia; ordem de entrada embaralhada dá o mesmo resultado.
- [ ] **Step 2:** Rodar → FAIL. **Step 3:** Implementar. **Step 4:** Rodar → PASS.
- [ ] **Step 5:** Commit `feat(platform): unidades de lote e casamento PEPS das operações de bolsa`.

### Task 3: Contrapartes, contas e extratos

**Files:**
- Create: `SiagroB1.Application/Services/Market/Hedges/MarketCounterpartyService.cs` (`BaseService<MarketCounterparty,int>`; nome obrigatório e único; excluir com conta usada em operação/hedge/extrato → `BusinessException`), `MarketCounterpartyAccountService.cs` (código obrigatório, único por contraparte; excluir em uso → `BusinessException`), `MarketBrokerStatementService.cs` (conta de corretora ativa; única por conta+data; carimba `CreatedBy/At`)
- Create: controllers OData `MarketCounterpartiesController` (`$expand=Accounts`), `MarketCounterpartyAccountsController` (`$expand=Counterparty`), `MarketBrokerStatementsController` (`$expand=Account($expand=Counterparty)`), entity sets homônimos; gravações exigem `MARKET_MANAGE`
- Modify: `ODataConfigurations.cs`, `ServiceCollectionExtensions.cs`
- Test: `MarketCounterpartyServiceTests.cs`, `MarketBrokerStatementServiceTests.cs`

- [ ] **Step 1:** Testes: nome duplicado rejeitado; conta duplicada na mesma contraparte rejeitada e permitida em outra; excluir conta usada por operação rejeitado; extrato duplicado (conta+data) rejeitado; extrato em conta de banco rejeitado.
- [ ] **Step 2:** Rodar → FAIL. **Step 3:** Implementar. **Step 4:** Rodar `--filter FullyQualifiedName~Market` → PASS; build.
- [ ] **Step 5:** Commit `feat(platform): cadastro de corretoras, bancos, contas e extratos de margem`.

### Task 4: Operações de bolsa, conferência e posição de derivativos

**Files:**
- Create: `SiagroB1.Application/Services/Market/Hedges/MarketFuturesTradeService.cs`:
  - `CreateAsync(MarketFuturesTradeInput, userName, ct)`, `UpdateAsync(int id, MarketFuturesTradeInput, userName, ct)`, `DeleteAsync(int id, userName, ct)`; `record MarketFuturesTradeInput(DateTime TradedAt, int AccountId, string InstrumentCode, MarketTradeSide Side, int Lots, decimal Price, decimal Fees, CurrencyType FeesCurrency, MarketFuturesTradeReason Reason, string? BrokerReference, Guid? PurchaseContractKey, Guid? SalesContractKey, string? Notes)`
  - `Task RematchAsync(int accountId, string instrumentCode, ct)` (apaga e recria `MARKET_FUTURES_MATCHES` e `OpenLots` da conta × instrumento) — público, usado pelo EFP
  - `SetConfirmedAsync(int id, bool confirmed, userName, ct)`, `Task<int> ConfirmDayAsync(int accountId, DateOnly date, userName, ct)`
- Create: `MarketDerivativesPositionService.cs`:
  ```csharp
  public sealed record MarketFuturesOpenPosition(int AccountId, string AccountLabel, string InstrumentCode, string InstrumentDescription, string Commodity,
      DateOnly? ExpirationDate, MarketQuoteUnit QuoteUnit, int NetLots, decimal AvgPrice, decimal? Tons, decimal? Usd,
      decimal? LastPrice, decimal? UnrealizedPnl, CurrencyType Currency);
  public sealed record MarketRealizedRow(int MatchId, DateOnly CloseDate, int AccountId, string AccountLabel, string InstrumentCode, int Lots,
      decimal OpenPrice, decimal ClosePrice, decimal RealizedPnl, CurrencyType Currency);
  Task<IReadOnlyList<MarketFuturesOpenPosition>> GetOpenPositionsAsync(CancellationToken ct);   // NetLots, Tons e Usd com sinal (+ comprado, − vendido)
  Task<IReadOnlyList<MarketRealizedRow>> GetRealizedAsync(DateOnly from, DateOnly to, CancellationToken ct);
  ```
  `AvgPrice` = média ponderada dos lotes abertos; `UnrealizedPnl` = `(LastPrice − AvgPrice) × |NetLots| × valor do ponto × sinal`, nulo sem cotação.
- Create: `SiagroB1.Web/Controllers/MarketFuturesTradesController.cs` — OData leitura (`MarketFuturesTrades`, `$expand=Account($expand=Counterparty),Instrument`; `MarketFuturesMatches` só leitura) + REST `POST market/futures-trades`, `PUT market/futures-trades/{id}`, `DELETE market/futures-trades/{id}`, `POST market/futures-trades/{id}/confirm`, `/unconfirm`, `POST market/futures-trades/confirm-day` (`{ accountId, date }`), `GET market/futures-positions`, `GET market/futures-realized?from=&to=`
- Test: `MarketFuturesTradeServiceTests.cs`, `MarketDerivativesPositionServiceTests.cs`

**Regras:** conta ativa de contraparte `Broker`; instrumento ativo de tipo `Future` e `EnsureTradable`; `Lots > 0`, `Price > 0`, `Fees >= 0`; no máximo um vínculo de contrato (compra **ou** venda), copiando `ContractCode`; `TradeDate` = data de São Paulo de `TradedAt`; operação com `EfpId` não pode ser alterada nem excluída por aqui; alterar conta ou instrumento refaz o PEPS dos dois pares (antigo e novo); operação conferida não pode ser alterada/excluída sem desmarcar a conferência.

- [ ] **Step 1:** Testes: criar compra 10 e venda 4 → 1 casamento com resultado dos exemplos, `OpenLots` 6/0; editar o preço da compra refaz o resultado; excluir a venda apaga o casamento e reabre os 10; mover a operação para outro instrumento refaz os dois lados; conta de banco, instrumento spot, instrumento inativo, `ContractSize 0`, lotes 0 → `BusinessException`; operação de EFP protegida; conferida protegida; `ConfirmDay` marca só as do dia/conta; posição aberta com preço médio e toneladas com sinal; `UnrealizedPnl` com cotação e nulo sem; realizado filtrado por período; sem permissão → `ForbiddenException`.
- [ ] **Step 2:** Rodar → FAIL. **Step 3:** Implementar. **Step 4:** Rodar `--filter FullyQualifiedName~Market` → PASS; build.
- [ ] **Step 5:** Commit `feat(platform): operações de bolsa com casamento PEPS, conferência e posição`.

### Task 5: Hedge de moeda (NDF/termo)

**Files:**
- Create: `SiagroB1.Application/Services/Market/Hedges/MarketFxHedgeService.cs`: `CreateAsync`, `UpdateAsync`, `DeleteAsync` (só `Open` e não conferido), `SettleAsync(int id, decimal settlementRate, userName, ct)`, `ReopenAsync(int id, userName, ct)` (de `Settled`/`Canceled` para `Open`, limpa liquidação), `CancelAsync`, `SetConfirmedAsync`, `Task<decimal?> SuggestSettlementRateAsync(int id, ct)` (fechamento diário do instrumento `MarketTradingSettings.SpotFxInstrumentCode` na `SettlementDate`; senão último; senão nulo); `record MarketFxHedgeInput(DateTime TradedAt, int AccountId, MarketFxHedgeKind Kind, MarketTradeSide Side, decimal AmountUsd, decimal ContractedRate, DateOnly SettlementDate, string? BankReference, Guid? PurchaseContractKey, Guid? SalesContractKey, string? Notes)`
- Create: `SiagroB1.Web/Controllers/MarketFxHedgesController.cs` — OData leitura (`MarketFxHedges`, `$expand=Account($expand=Counterparty)`) + REST `POST market/fx-hedges`, `PUT/DELETE market/fx-hedges/{id}`, `POST .../{id}/settle` (`{ rate }`), `/reopen`, `/cancel`, `/confirm`, `/unconfirm`, `GET .../{id}/suggested-rate`
- Test: `MarketFxHedgeServiceTests.cs`

**Regras:** conta ativa de contraparte `Bank`; `AmountUsd > 0`, `ContractedRate > 0`; `SettlementDate >= TradeDate`; liquidar só `Open` com taxa > 0, grava `RealizedPnlBrl`, `SettledAt/By`; alterar/excluir só `Open`.

- [ ] **Step 1:** Testes: venda US$ 100.000 a 5,50 liquidada a 5,40 → **+10.000,00**; compra na mesma situação → **−10.000,00**; liquidar duas vezes, liquidar cancelado, editar liquidado → `BusinessException`; reabrir limpa taxa e resultado; sugestão usa o fechamento diário da data, cai para o último, e nulo sem instrumento configurado; conta de corretora rejeitada; sem permissão → `ForbiddenException`.
- [ ] **Step 2:** Rodar → FAIL. **Step 3:** Implementar. **Step 4:** Rodar → PASS; build.
- [ ] **Step 5:** Commit `feat(platform): hedge de moeda com liquidação e resultado`.

### Task 6: EFP — troca de bolsa por físico

Ler antes: `part34-backend.md` §3 (serviços de fixação, transações) e a boleta da parte 2 (`MarketTradeTicketContractService`) como modelo de operação retomável.

**Files:**
- Create: `SiagroB1.Application/Services/Market/Hedges/MarketEfpService.cs`:
  - `record MarketEfpInput(DateOnly Date, int AccountId, string InstrumentCode, MarketTradeSide Side, int Lots, decimal Price, decimal Premium, decimal? FxRate, MarketEfpContractSide ContractSide, Guid ContractKey, decimal VolumeKg, DateOnly? FinancialDueDate, string? Notes)`
  - `record MarketEfpPreview(decimal FixationPricePerKg, decimal FixationPricePerBag, CurrencyType Currency, decimal? TonsFutures, decimal TonsPhysical)`; `Task<MarketEfpPreview> PreviewAsync(MarketEfpInput, ct)`
  - `Task<MarketEfp> CreateAsync(MarketEfpInput, userName, ct)`, `Task<MarketEfp> ResumeAsync(int id, userName, ct)`, `Task<MarketEfp> CancelAsync(int id, userName, ct)`
- Create: `SiagroB1.Web/Controllers/MarketEfpsController.cs` — OData leitura (`MarketEfps`) + REST `POST market/efps/preview`, `POST market/efps`, `POST market/efps/{id}/resume`, `POST market/efps/{id}/cancel`
- Test: `MarketEfpServiceTests.cs`

**Idempotência (lição da boleta):** a chave da fixação (`Guid`) é gerada e gravada em `MarketEfp.FixationKey` na mesma gravação que cria o EFP `Processing`, e passada ao serviço de criação da fixação; a operação de bolsa é localizada por `EfpId` (única por EFP) antes de criar outra. Criar, retomar e estornar rodam sob `KeyedAsyncLock` por empresa + EFP e, depois da primeira gravação, não honram mais o token da requisição.

**Regras:** contrato `Approved`, `Type == ToBeDetermined`, `UnitOfMeasureCode == "KG"`; `VolumeKg > 0` e ≤ saldo a fixar do contrato (`AvailableVolumeToPricing`); regras de câmbio das Global Constraints; vencimento = `FinancialDueDate ?? StandardCashFlowDate` do contrato, obrigatório. `CreateAsync` (sem transação ambiente): grava o EFP `Processing` → cria a operação de bolsa (`Reason=Efp`, `EfpId`, vínculo ao contrato) e guarda `FuturesTradeId` → cria a fixação pelo serviço existente de compra ou venda (`FixationVolume=VolumeKg`, `FixationPrice`, `FixationDate=Date`, `FinancialDueDate`, `PaymentDetails="EFP nº {Id}"`) e guarda `FixationKey` → aprova a fixação pelo serviço existente → `Active`. `ResumeAsync` continua do primeiro passo não concluído. `CancelAsync` (só `Active`/`Processing`): fixação `Confirmed` → estorno pelo serviço existente; fixação `InApproval` → exclusão pelo serviço existente; exclui a operação de bolsa e refaz o PEPS; `Canceled`. Falha no meio deixa o EFP no estado em que parou, e chamar de novo continua.

- [ ] **Step 1:** Testes: `Preview` soja (preço 1190, prêmio 60, TonFactor 0,36745, câmbio 5,40) → `(1250 × 0,36745 × 5,40) / 1000 = 2,48028750` por kg e 148,82 por saca; milho B3 (68,00 + 1,50) → 1,15833333 por kg; contrato fixo, não aprovado, volume acima do saldo a fixar, câmbio ausente (US$→R$) e combinação R$→US$ → `BusinessException`; `Create` deixa operação com `Reason=Efp`, fixação `Confirmed` com volume e preço, 1 título provisório, EFP `Active`; `Create` com falha na aprovação da fixação → EFP `Processing` com as chaves gravadas, e `Resume` conclui sem duplicar operação nem fixação; `Cancel` estorna/exclui a fixação, exclui a operação, refaz o PEPS e marca `Canceled`; `Cancel` duas vezes → `BusinessException`; EFP de venda usa os serviços de venda.
- [ ] **Step 2:** Rodar → FAIL. **Step 3:** Implementar. **Step 4:** Rodar `--filter FullyQualifiedName~Market` → PASS; suíte `SiagroB1.Application.Tests` inteira verde.
- [ ] **Step 5:** Commit `feat(platform): EFP fixa o preço do contrato pela bolsa da troca`.

### Task 7: Frontend — Corretoras e Bancos, Ajustes e Margem, Operações de Bolsa

**Files:**
- Modify: `ServerRoutes.ts`, `manifest.json` (rotas `marketCounterparties` (+New/Edit), `marketBrokerStatements`, `marketFuturesTrades`), i18n
- Create: `webapp/view|controller/marketCounterparties/*` — lista + página com dados e tabela de contas (molde da Tabela de Preços da parte 2)
- Create: `webapp/view|controller/marketBrokerStatements/*` — lista com FilterBar (conta, período) e diálogo
- Create: `webapp/view|controller/marketFuturesTrades/Main.*` — `IconTabBar` com 3 abas: **Operações** (grid `sap.ui.table.Table` sem quebra: Data/hora, Corretora – conta, Instrumento, C/V, Lotes, Preço, Toneladas, Abertos, Motivo, Taxas, Ref. corretora, Contrato, Conferida; FilterBar por período, conta, instrumento, só não conferidas; ações Incluir, Editar, Excluir, Conferir/Desmarcar, "Conferir o dia"), **Posição aberta** (`GET market/futures-positions`: conta, instrumento, vencimento, lotes líquidos, preço médio, toneladas/US$, último, resultado não realizado com cor), **Resultado realizado** (`GET market/futures-realized` por período, com total por moeda)
- Create: `fragments/FuturesTradeDialog.fragment.xml` — campos do `MarketFuturesTradeInput`; mostra as toneladas equivalentes enquanto digita; vínculo opcional a contrato de compra ou venda por value help
- Test: QUnit de helpers novos (toneladas por lote, formatação de sinal)

- [ ] **Step 1:** Implementar. **Step 2:** `yarn ts-typecheck`, `yarn lint` limpos; `ui5lint` sem problema novo.
- [ ] **Step 3:** Commit `feat(platform): operações de bolsa, posição e cadastro de corretoras`.

### Task 8: Frontend — Hedge de Moeda e EFP

**Files:**
- Create: `webapp/view|controller/marketFxHedges/Main.*` — grid: Data, Banco – conta, Tipo, C/V, US$, Taxa contratada, Liquidação, Situação, Taxa de liquidação, Resultado R$, Ref. banco, Contrato, Conferido; ações Incluir, Editar, Excluir, Liquidar (diálogo que traz a taxa sugerida e mostra o resultado antes de confirmar), Reabrir, Cancelar, Conferir
- Create: `webapp/view|controller/marketEfps/Main.*` — lista (Nº, Data, Contrato, Volume, Instrumento, C/V, Lotes, Preço, Preço fixado R$/sc, Situação) + diálogo "Novo EFP": escolhe o lado (compra/venda) e o contrato a fixar por value help (só aprovados a fixar), volume em sacas, perna de bolsa, prêmio, câmbio (habilitado conforme a regra), vencimento; botão "Calcular" chama `POST market/efps/preview` e mostra preço por saca e toneladas das duas pernas; ações Estornar e "Retomar" (quando `Processing`)
- Modify: `manifest.json`, `ServerRoutes.ts`, i18n

- [ ] **Step 1:** Implementar. **Step 2:** `yarn ts-typecheck`, `yarn lint` limpos; `ui5lint` sem problema novo.
- [ ] **Step 3:** Commit `feat(platform): hedge de moeda e EFP do módulo Mercado`.
