# Mercado — Parte 4: Posição — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Mostrar ao trader e à diretoria quanto a empresa está exposta em cada commodity e em dólar, quanto já está travado, a marcação a mercado e os limites de exposição com alerta.

**Architecture:** A posição é sempre **calculada** (nada materializado além do registro de estouro de limite): exposição física lida dos contratos aprovados (mesmos números de saldo que a tela de contratos mostra), derivativos lidos da parte 3, cotações da parte 1. Três funções puras concentram a regra (mês de bolsa, volume firme a entregar, % de hedge e limite). A leitura dos contratos é guardada em cache curto por empresa, e a marcação a mercado é refeita a cada emissão do SSE.

**Tech Stack:** .NET 10, EF Core SQL Server, OData, SSE, Hangfire, xUnit + EF InMemory; OpenUI5 TypeScript.

**Spec:** `docs/superpowers/specs/2026-11-05-market-trading-module-design.md` (§4.4, §5 "Respostas do usuário", §6, §8)

**Repos:** backend `C:\Projetos\SiagroB1\worktrees\market\backend` e frontend `C:\Projetos\SiagroB1\worktrees\market\frontend`, ambos na branch `feature/market-position` (criada a partir de `feature/market-hedges`). **Nunca push, nunca merge.** Escopo de commit: `platform` (ou `purchase-contract`/`sales-contract` quando o commit só toca contratos).

**Levantamentos:** `C:\Projetos\SiagroB1\siagro-b1-backend\.superpowers\recon\part34-backend.md` (leitura obrigatória dos Tasks 1, 4 e 6), `part2-backend.md`, `part2-frontend.md`.

## Global Constraints

- Identificadores em inglês, tabelas UPPER_SNAKE plural; textos ao usuário em pt-BR. Todo controller novo leva `[MarketModuleEnabled]`; `BusinessException` → 400 pt-BR; sem permissão → 403 pt-BR.
- **Campos novos de contrato só existem para quem tem o módulo**: colunas anuláveis (ou com default) em `PURCHASE_CONTRACTS` e `SALES_CONTRACTS`; com o módulo desligado as telas de contrato não mostram nada novo e nenhum comportamento existente muda. Nenhum fluxo existente de contrato (criar, editar, aprovar, fixar, encerrar, relatórios) pode mudar de resultado.
- **Exposição física** (decisão do usuário): só contratos `Status == Approved` e `UnitOfMeasureCode == "KG"`; só volume **com preço firme** ainda **não entregue**. Por contrato:
  - `balance = TotalVolume − AllocatedVolume − WashedOutVolume` (o "Saldo físico" da tela);
  - `confirmed` = Σ `FixationVolume` das fixações `Confirmed` (contrato fixo tem uma, com tudo);
  - `washedFixed = WashedOutVolume − WashedOutUnfixedVolume`;
  - `firmOpen = max(0, min(balance, confirmed − washedFixed − AllocatedVolume))` — o entregue consome primeiro o volume com preço;
  - toneladas = kg / 1000; compra soma, venda subtrai.
- **Mês de bolsa**: `DeliveryEndDate` do contrato → vencimento pelo mapa da commodity (mês de entrega 1–12 → mês de bolsa + deslocamento de ano). Exemplo do usuário: entrega em junho → julho (soja ZSN). Produto → commodity pelo cadastro `MarketCommodityItem`; produto sem commodity fica fora da posição e é **contado e listado** como ignorado (assim como contrato em unidade diferente de KG).
- **Posição líquida** por commodity × mês de bolsa = compras firmes a entregar − vendas firmes a entregar + futuros abertos (t, com sinal; parte 3). **% de hedge** = volume coberto ÷ |físico líquido| × 100, onde coberto = `min(|físico líquido|, futuros no sentido contrário)`; físico líquido zero → sem percentual.
- **Dólar** (US$, por mês): contrato "atrelado a dólar" = `StandardCurrency == Usd` **ou** `MarketUsdPrice` preenchido. Valor = `firmOpen (kg) × preço em US$/kg` (`MarketUsdPrice`, ou o preço médio das fixações confirmadas quando a moeda do contrato é US$). Entra só se `MarketFxLocked == false`. Venda = a receber em dólar (+); compra = a pagar em dólar (−). Mês = `StandardCashFlowDate ?? DeliveryEndDate`. Hedges: NDF/termo abertos (venda −, compra +) pelo mês de `SettlementDate`; futuros de dólar abertos (`BrlPerUsd`) pelo mês do vencimento. % de hedge pela mesma regra do coberto.
- **Marcação a mercado**: derivativos = não realizado da parte 3 (ao vivo) e a mesma conta com o último fechamento diário (`MARKET_DAILY_CLOSES`); NDF aberto contra o dólar à vista: venda `(taxa contratada − spot) × US$`, compra o inverso; realizado = casamentos PEPS + NDF liquidados no ano corrente. Físico: só o componente de bolsa dos contratos que têm `MarketInstrumentCode` e `MarketExchangePrice`: `(cotação − MarketExchangePrice) × valor por tonelada × t × sinal` (valor por tonelada: `TonFactor` para US¢/bu, `1` para US$/t, `TonFactor` para R$/sc); contratos sem referência são contados à parte.
- **Limites**: por commodity (t) ou dólar (US$), no total ou num mês; `MaxLong` e `MaxShort` (positivos, opcionais). Estourado quando `líquido > MaxLong` ou `líquido < −MaxShort`. Só quem tem `MARKET_BOARD_APPROVE` grava limites. Um aviso por limite por dia.
- Números rastreáveis: cada célula da posição abre a lista de contratos/operações que a formam.
- Datas de negócio em `DATE`; "hoje" = São Paulo; carimbos em UTC com `Z`. Migration nova exige trailer `DB:`. Não rodar `database update`.
- **Lições das partes 2 e 3 (obrigatórias):** controller OData do Mercado deriva de `MarketODataController` e o GET por chave devolve `SingleResult` (senão `$expand` vem vazio); carimbo UTC novo recebe o conversor de `UtcDateTimeConverters`; campo preenchido pelo serviço nunca é obrigatório para o cliente; erros REST em `text/plain` pt-BR e erros OData no formato OData; violação de índice único vira 400 pt-BR; no frontend, `DateTimeOffset` por OData com `precision: 7`, números digitados com `parseStrictDecimal`, pendências do `UpdateGroup` descartadas ao sair da rota, grids em `sap.ui.table.Table` sem quebra de linha e valores com casas fixas; a última ação do implementador é a mensagem de devolução.

## Review Focus

1. Contrato PAF parcialmente fixado e parcialmente entregue: só o firme não entregue entra; contrato não aprovado, encerrado, cancelado ou sem preço não entra. Testes nos Tasks 2 e 4.
2. Produto sem commodity, contrato fora de KG e vencimento sem instrumento cadastrado não somem em silêncio: aparecem em "ignorados" ou em balde sem marcação. Testes nos Tasks 4 e 5.
3. Cotação ausente → exposição em toneladas aparece e a marcação a mercado daquele balde fica vazia, sem derrubar o painel. Teste no Task 5.
4. Estouro de limite avisa uma vez por dia por limite, mesmo com o job rodando várias vezes; limite desativado não avisa. Teste no Task 6.
5. Com o módulo desligado, criar/editar/aprovar contrato segue idêntico e os campos novos ficam nulos. Teste no Task 1 e conferência no navegador.

---

### Task 1: Campos de contrato, entidades da posição e migrations

**Files:**
- Modify: `SiagroB1.Domain/Entities/PurchaseContract.cs` e `SalesContract.cs` — `MarketInstrumentCode` (VARCHAR(30))?, `MarketExchangePrice` DECIMAL(18,6)?, `MarketPremium` DECIMAL(18,6)?, `MarketUsdPrice` DECIMAL(18,8)?, `MarketFxRate` DECIMAL(18,6)?, `MarketFxLocked` bool (default false) — sem FK
- Create enums: `MarketLimitScope` (`Commodity=0, Usd=1`)
- Create em `SiagroB1.Domain/Entities/Market/`:
  - `MarketCommodity` → `MARKET_COMMODITIES`: `Code` (VARCHAR(20), PK — o mesmo valor de `MarketInstrument.Commodity`), `Name` (60), `HedgeExchange` (`MarketExchange`), `Active`, `Items`, `Months`
  - `MarketCommodityItem` → `MARKET_COMMODITY_ITEMS`: `Id`, `CommodityCode` (FK cascade), `ItemCode` (VARCHAR(10), único), `ItemName` (200)
  - `MarketCommodityMonth` → `MARKET_COMMODITY_MONTHS`: `Id`, `CommodityCode` (FK cascade), `DeliveryMonth` (1–12), `FuturesMonth` (1–12), `YearOffset` (0/1); único (`CommodityCode`, `DeliveryMonth`)
  - `MarketExposureLimit` → `MARKET_EXPOSURE_LIMITS`: `Id`, `Scope`, `CommodityCode` (FK restrict)?, `Year` int?, `Month` int? (ambos nulos = total), `MaxLong` DECIMAL(18,2)?, `MaxShort` DECIMAL(18,2)?, `Active`, `UpdatedBy`, `UpdatedAt`
  - `MarketLimitBreach` → `MARKET_LIMIT_BREACHES`: `Id`, `LimitId` (FK cascade), `Date` (DATE), `Value` DECIMAL(18,2), `LimitValue` DECIMAL(18,2), `DetectedAt`, `NotifiedAt`?; único (`LimitId`, `Date`)
- Modify: `AppDbContext.cs`
- Create: migration AppContext `AddMarketPosition` (colunas anuláveis nos dois contratos + 5 tabelas); migration CommonContext `AddMarketPositionMenu`: filhos de `market`: `marketPosition` "Posição" 5, `marketExposureLimits` "Limites de Exposição" 50, `marketCommodities` "Commodities e Vencimentos" 52 — `ROLE_MENUS` para ADMIN, `MARKET_TRADER`, `MARKET_BOARD`
- Modify: controller REST do Mercado — `GET market/module` → `{ "enabled": true }` (404 com o módulo desligado, pelo filtro)
- Test: `SiagroB1.Application.Tests/Market/MarketPositionModelTests.cs`

- [ ] **Step 1:** Testes: gravar/ler as 5 entidades; criar contrato de compra pelo `PurchaseContractsCreateService` sem os campos novos → todos nulos e `MarketFxLocked == false`; contrato com os campos preenchidos mantém os valores depois de criar e de `PurchaseContractsUpdateService`.
- [ ] **Step 2:** Rodar → FAIL. **Step 3:** Implementar. **Step 4:** Rodar → PASS; suíte inteira verde (contratos são código compartilhado).
- [ ] **Step 5:** Gerar migrations; revisar o script: App = `ALTER TABLE ... ADD` anuláveis (ou `DEFAULT 0` para o bool) + 5 `CREATE TABLE`.
- [ ] **Step 6:** Commit `feat(platform): campos de mercado nos contratos e entidades da posição` com `DB: AddMarketPosition` e `DB: AddMarketPositionMenu`.

### Task 2: Regras puras da posição

**Files:**
- Create: `SiagroB1.Application/Services/Market/Position/MarketPositionMath.cs`
- Test: `SiagroB1.Application.Tests/Market/MarketPositionMathTests.cs`

**Interfaces — Produces:**
```csharp
public static class MarketPositionMath
{
    public readonly record struct FuturesMonth(int Year, int Month);
    public sealed record MonthRule(int DeliveryMonth, int FuturesMonth, int YearOffset);
    public static FuturesMonth? MapDelivery(DateOnly deliveryDate, IReadOnlyCollection<MonthRule> rules); // nulo sem regra para o mês
    public static decimal FirmOpenKg(decimal totalVolume, decimal allocated, decimal washedOut, decimal washedOutUnfixed, decimal confirmedFixed);
    public static decimal? HedgePercent(decimal physicalNet, decimal hedge);   // hedge com sinal; nulo quando physicalNet == 0
    public static decimal Covered(decimal physicalNet, decimal hedge);
    public enum LimitState { Ok, LongExceeded, ShortExceeded }
    public static LimitState Evaluate(decimal net, decimal? maxLong, decimal? maxShort);
    public static IReadOnlyList<MonthRule> DefaultRules(IReadOnlyCollection<int> futuresMonths); // cada mês de entrega → primeiro mês de bolsa >= ele; depois do último → primeiro do ano seguinte (YearOffset 1)
}
```

- [ ] **Step 1:** Testes: soja (meses 1,3,5,7,8,9,11): entrega 2027-06-20 → (2027, 7); 2027-12-10 → (2028, 1); 2027-07-01 → (2027, 7); sem regra → nulo. `FirmOpenKg`: fixo 600.000 sem entrega → 600.000; fixo com 200.000 entregues → 400.000; PAF 600.000 com 300.000 fixados e 100.000 entregues → 200.000; PAF com 300.000 fixados e 350.000 entregues → 0; washout fixado de 50.000 reduz o firme; nunca negativo; nunca maior que o saldo. `HedgePercent`: físico +1000 e futuros −600 → 60; futuros −1500 → 100 (coberto limitado ao físico, percentual 100); futuros +200 (mesmo sentido) → 0; físico −500 e futuros +500 → 100; físico 0 → nulo. `Evaluate`: 1200 com `MaxLong 1000` → `LongExceeded`; −800 com `MaxShort 500` → `ShortExceeded`; limites nulos → `Ok`. `DefaultRules` para milho CBOT (3,5,7,9,12): entrega 10 → 12 offset 0; entrega 1 → 3.
- [ ] **Step 2:** Rodar → FAIL. **Step 3:** Implementar. **Step 4:** Rodar → PASS.
- [ ] **Step 5:** Commit `feat(platform): regras de mês de bolsa, volume firme e hedge da posição`.

### Task 3: Commodities, limites, câmbio fechado e boleta preenchendo o contrato

**Files:**
- Create: `SiagroB1.Application/Services/Market/Position/MarketCommodityService.cs` (CRUD com itens e meses; `Task<int> CreateSamplesAsync(ct)`: `SOY` "Soja" CBOT meses 1,3,5,7,8,9,11; `CORN` "Milho" CBOT 3,5,7,9,12 — regras por `MarketPositionMath.DefaultRules`; idempotente; produto validado por `IItemService`), `MarketExposureLimitService.cs` (CRUD; exige `MARKET_BOARD_APPROVE`; `Scope == Commodity` exige commodity; `Usd` proíbe; `Year`/`Month` juntos ou ambos nulos; ao menos um de `MaxLong`/`MaxShort`, positivos), `MarketContractFxLockService.cs` (`Task SetAsync(MarketEfpContractSide side, Guid contractKey, bool locked, decimal? fxRate, string userName, ct)` — só contrato `Approved`; exige `MARKET_MANAGE`; registra no log de alterações do contrato quando existir serviço de log para aquele lado)
- Create: controllers OData `MarketCommoditiesController` (`$expand=Items,Months`), `MarketCommodityItemsController`, `MarketCommodityMonthsController`, `MarketExposureLimitsController`; REST `POST market/commodities/samples`, `POST market/contracts/{purchase|sales}/{key}/fx-lock` (`{ locked, fxRate }`)
- Modify: `MarketTradeTicketContractService.cs` (parte 2) — o contrato gerado recebe `MarketInstrumentCode`, `MarketExchangePrice = ExchangeQuote`, `MarketPremium = Premium`, `MarketFxRate = NetFx` (só instrumento em US$); `MarketUsdPrice` fica nulo (o contrato da boleta é em R$ com preço fixo)
- Modify: `ODataConfigurations.cs`, `ServiceCollectionExtensions.cs`
- Test: `MarketCommodityServiceTests.cs`, `MarketExposureLimitServiceTests.cs`, `MarketContractFxLockServiceTests.cs`, acréscimo em `MarketTradeTicketContractServiceTests.cs`

- [ ] **Step 1:** Testes: exemplos idempotentes e com as regras esperadas (soja entrega 6 → 7); produto em duas commodities rejeitado; limite sem permissão de diretoria → `ForbiddenException`; validações do limite; `fx-lock` em contrato não aprovado rejeitado, em aprovado grava e pode desfazer; contrato gerado pela boleta S1 tem `MarketExchangePrice 1188.25`, `MarketPremium 50`, `MarketFxRate 5.7267`, `MarketUsdPrice` nulo.
- [ ] **Step 2:** Rodar → FAIL. **Step 3:** Implementar. **Step 4:** Rodar `--filter FullyQualifiedName~Market` → PASS; build.
- [ ] **Step 5:** Commit `feat(platform): commodities, limites de exposição e câmbio fechado dos contratos`.

### Task 4: Exposição física lida dos contratos

Ler antes: `part34-backend.md` §1–§4, §10.

**Files:**
- Create: `SiagroB1.Application/Services/Market/Position/MarketPhysicalExposureService.cs`
- Create: `SiagroB1.Commons/Market/MarketExposureCache.cs` — singleton, por empresa, validade de 30 s, `Invalidate(companyCode)`
- Modify: `ServiceCollectionExtensions.cs` / registro do singleton
- Test: `SiagroB1.Application.Tests/Market/MarketPhysicalExposureServiceTests.cs`

**Interfaces — Produces:**
```csharp
public sealed record MarketPhysicalRow(MarketEfpContractSide Side, Guid ContractKey, string ContractCode, string CardName, string ItemCode,
    string CommodityCode, int FuturesYear, int FuturesMonth, DateOnly DeliveryEndDate, decimal FirmOpenKg, decimal PricePerKg, CurrencyType Currency,
    string? MarketInstrumentCode, decimal? MarketExchangePrice, decimal? MarketPremium,
    bool UsdLinked, bool FxLocked, decimal? UsdPricePerKg, DateOnly CashFlowDate);
public sealed record MarketIgnoredContract(MarketEfpContractSide Side, Guid ContractKey, string ContractCode, string ItemCode, string Reason); // Reason pt-BR
public sealed record MarketPhysicalExposure(DateTime ReadAt, IReadOnlyList<MarketPhysicalRow> Rows, IReadOnlyList<MarketIgnoredContract> Ignored);
Task<MarketPhysicalExposure> GetAsync(CancellationToken ct);            // usa o cache
Task<MarketPhysicalExposure> ReadAsync(CancellationToken ct);           // sempre lê do banco
```
Leitura em lote (sem N+1, traduzível em SQL): contratos `Approved` de compra e de venda com as colunas necessárias; Σ fixações `Confirmed` por contrato com preço médio ponderado (`GroupBy`); cadastros de commodity. `FirmOpenKg <= 0` não gera linha. `PricePerKg` = preço médio ponderado das fixações confirmadas.

- [ ] **Step 1:** Testes: compra fixa aprovada 600 t sem entrega em junho/2027 de produto da soja → linha `SOY` 2027/7 com 600.000 kg; mesma com `AllocatedVolume` 200.000 → 400.000; PAF com fixação confirmada parcial → só o fixado não entregue; fixação `InApproval` não conta; rascunho/encerrado/cancelado/`Status` nulo não entram; venda gera linha `Sales`; produto sem commodity → `Ignored` "Produto sem commodity configurada"; contrato fora de KG → `Ignored`; mês sem regra → `Ignored`; contrato em US$ → `UsdLinked`, `UsdPricePerKg` = preço das fixações; contrato em R$ com `MarketUsdPrice` → `UsdLinked`; `CashFlowDate` cai para `DeliveryEndDate` quando não há previsão; `GetAsync` devolve o cache dentro da validade e relê depois de `Invalidate`.
- [ ] **Step 2:** Rodar → FAIL. **Step 3:** Implementar. **Step 4:** Rodar → PASS; build.
- [ ] **Step 5:** Commit `feat(platform): exposição física a partir dos contratos aprovados`.

### Task 5: Painel de posição (serviço, REST e SSE)

**Files:**
- Create: `SiagroB1.Application/Services/Market/Position/MarketPositionService.cs`
- Create/Modify: controller REST — `GET market/position`, `GET market/position/live` (SSE pelo laço genérico da parte 2, com intervalo mínimo de 2 s entre emissões), `GET market/position/contracts?commodity=&year=&month=` (linhas físicas do balde), `GET market/position/usd-contracts` (contratos atrelados a dólar com câmbio aberto)
- Test: `SiagroB1.Application.Tests/Market/MarketPositionServiceTests.cs`

**Interfaces — Consumes:** `MarketPhysicalExposureService.GetAsync`, `MarketDerivativesPositionService.GetOpenPositionsAsync/GetRealizedAsync` (parte 3), `MarketFxHedges` abertos, cotações e fechamentos diários, limites, `MarketPositionMath`, `MarketUnits`.
**Produces:**
```csharp
public sealed record MarketCommodityBucket(int Year, int Month, string? InstrumentCode, decimal PurchaseTons, decimal SalesTons, decimal FuturesTons,
    decimal NetTons, decimal CoveredTons, decimal? HedgePercent, decimal? LastPrice, decimal? PreviousClose,
    decimal? FuturesUnrealized, decimal? FuturesUnrealizedAtClose, decimal? PhysicalExchangeMtm, int PhysicalWithoutReference, CurrencyType? Currency,
    string LimitState, decimal? MaxLong, decimal? MaxShort);
public sealed record MarketCommodityPosition(string Code, string Name, IReadOnlyList<MarketCommodityBucket> Buckets, MarketCommodityBucket Total);
public sealed record MarketUsdBucket(int Year, int Month, decimal ReceivableUsd, decimal PayableUsd, decimal FxHedgeUsd, decimal UsdFuturesUsd,
    decimal NetUsd, decimal CoveredUsd, decimal? HedgePercent, decimal? FxHedgeUnrealizedBrl, string LimitState, decimal? MaxLong, decimal? MaxShort);
public sealed record MarketUsdPosition(IReadOnlyList<MarketUsdBucket> Buckets, MarketUsdBucket Total, decimal? SpotFx, decimal OpenCommodityValueUsd);
public sealed record MarketResultSummary(decimal RealizedUsd, decimal RealizedBrl, decimal UnrealizedUsd, decimal UnrealizedBrl);
public sealed record MarketLimitAlert(int LimitId, string Description, decimal Value, decimal LimitValue, string State);
public sealed record MarketPositionSnapshot(DateTime GeneratedAt, IReadOnlyList<MarketCommodityPosition> Commodities, MarketUsdPosition Usd,
    MarketResultSummary Results, IReadOnlyList<MarketLimitAlert> Alerts, IReadOnlyList<MarketIgnoredContract> Ignored);
Task<MarketPositionSnapshot> GetSnapshotAsync(DateOnly today, CancellationToken ct);
```
`OpenCommodityValueUsd` (informativo, fora do líquido) = Σ dos baldes com instrumento em US$ de `NetTons × cotação em US$/t`. O total de cada commodity soma os baldes e reavalia o limite "total". Futuros de instrumento cuja commodity não tem cadastro entram numa commodity com `Name` = o próprio código.

- [ ] **Step 1:** Testes: compra firme 1.000 t (2027/7) + venda de 4 lotes de soja (−544,2916 t) → líquido 455,7084, coberto 544,2916, hedge 54,43 %; venda física 300 t no mesmo balde reduz o físico líquido para 700 e o hedge sobe para 77,76 %; balde sem cotação → toneladas preenchidas e marcações nulas; `FuturesUnrealized` bate com a parte 3; `FuturesUnrealizedAtClose` usa o último fechamento; `PhysicalExchangeMtm` para contrato com referência (compra 1.000 t a 1188,25, cotação 1200 → `(1200 − 1188,25) × 0,36745 × 1000 = +4.317,54` US$) e `PhysicalWithoutReference` contando os demais; dólar: venda atrelada com câmbio aberto + NDF de venda → a receber, hedge negativo, líquido e % coerentes; contrato com `MarketFxLocked` fora; limite de commodity total estourado gera `Alert` e `LimitState`; limite inativo não; ignorados repassados.
- [ ] **Step 2:** Rodar → FAIL. **Step 3:** Implementar. **Step 4:** Rodar `--filter FullyQualifiedName~Market` → PASS; build.
- [ ] **Step 5:** Commit `feat(platform): painel de posição com hedge, marcação a mercado e limites`.

### Task 6: Alerta de limite (job + notificação) e alçada da boleta por limite

Ler antes: `part34-backend.md` §7 e §9.

**Files:**
- Modify: `SiagroB1.Domain/Enums/NotificationDocumentType.cs` (`MarketPosition = 2`), `NotificationEventType.cs` (`ExposureLimitExceeded = 30`), `NotificationEventLabels.cs` (rótulos pt-BR "Posição de mercado", "Limite de exposição excedido" + ícone)
- Create: `SiagroB1.Application/Services/Market/Position/MarketLimitAlertService.cs` — `Task<int> CheckAsync(DateOnly today, CancellationToken ct)`: calcula a posição (sem cache), e para cada alerta sem `MarketLimitBreach` no dia grava o registro e enfileira uma mensagem na outbox (`DocumentType = MarketPosition`, `EventType = ExposureLimitExceeded`, `DocumentKey` sintético, `DocumentCode = "LIMITE-{LimitId}"`, payload JSON `{ "text": "<mensagem pt-BR pronta>" }`), tudo no mesmo `SaveChanges`
- Create: `SiagroB1.Application/Jobs/MarketLimitAlertJob.cs` + entrada em `CompanyScopedJobRunner` + `RecurringJob` em `Program.cs` (`"market-limit-alert-job"`, cron `*/15 8-19 * * 1-5`, fuso de São Paulo, sem retry, dentro do bloco `MarketModule.IsEnabled`, com `RemoveIfExists` no else)
- Modify: `ContractNotificationDispatchJob.cs` — quando `DocumentType == MarketPosition`, o texto enviado é o `text` do payload (os contratos continuam como hoje)
- Modify: `MarketTradeTicketService.CreateAsync` (parte 2) — se a commodity do produto tem limite e `líquido atual + toneladas da boleta` estoura o limite total ou o do mês de bolsa da entrega, `RequiresBoardApproval = true` e o motivo ganha "Estoura o limite de exposição de <commodity>"
- Test: `MarketLimitAlertServiceTests.cs`, acréscimos em `MarketTradeTicketServiceTests.cs` e no teste do dispatch job existente

- [ ] **Step 1:** Testes: `Check` com limite estourado grava 1 `MarketLimitBreach` e 1 mensagem na outbox; rodar de novo no mesmo dia não duplica; no dia seguinte grava outro; limite que voltou ao normal não gera nada; mensagem traz commodity/mês, valor e limite em pt-BR; dispatch de `MarketPosition` envia o `text`; dispatch de contrato inalterado; boleta que estoura limite exige diretoria com o motivo; sem limite cadastrado nada muda.
- [ ] **Step 2:** Rodar → FAIL. **Step 3:** Implementar. **Step 4:** Rodar suíte `SiagroB1.Application.Tests` inteira → verde.
- [ ] **Step 5:** Commit `feat(platform): alerta de limite de exposição e alçada da boleta por limite`.

### Task 7: Frontend — tela Posição

**Files:**
- Create: `webapp/view|controller/marketPosition/Main.*` (rota `marketPosition`, `market/position`), `webapp/types/MarketPosition.ts`, i18n, `ServerRoutes.ts`, `manifest.json`
- Layout: faixa de alertas de limite (`MessageStrip` vermelho por alerta); cartões-resumo por commodity e para o dólar (**Exposto**, **Travado**, **% hedge** com `ProgressIndicator`, marcação a mercado); `IconTabBar`:
  - **Commodities** — grid `sap.ui.table.Table` sem quebra, agrupado por commodity: Vencimento, Compras (t), Vendas (t), Futuros (t), Líquido (t), Travado (t), % hedge, Cotação, Fech. anterior, MTM futuros, MTM físico (bolsa), Limite (semáforo); linha de total por commodity; a cotação e as marcações piscam verde/vermelho como no terminal; clicar numa linha abre os contratos do balde (`GET market/position/contracts`)
  - **Dólar** — grid por mês: A receber, A pagar, NDF/termo, Futuros de dólar, Líquido, Travado, % hedge, MTM dos NDF, Limite; abaixo, "Commodities em aberto valem US$ X na bolsa (informativo)"; botão "Contratos com câmbio aberto" abre a lista com a ação **Fechar câmbio** / **Reabrir** (`POST market/contracts/.../fx-lock`, só com `MARKET_MANAGE`)
  - **Resultado** — realizado × não realizado por moeda
  - **Fora da posição** — contratos ignorados com o motivo
- Atualização ao vivo por SSE (`MarketLiveService.start(url, ...)`), fechado ao sair da rota; aviso de "sem conexão" como no terminal
- Test: QUnit dos helpers (formatação de t/US$, estado do limite, agrupamento)

- [ ] **Step 1:** Implementar. **Step 2:** `yarn ts-typecheck`, `yarn lint` limpos; `ui5lint` sem problema novo.
- [ ] **Step 3:** Commit `feat(platform): painel de posição do módulo Mercado`.

### Task 8: Frontend — Limites, Commodities e a seção "Mercado" nos contratos

**Files:**
- Create: `webapp/view|controller/marketExposureLimits/*` (lista + diálogo; gravações só com `MARKET_BOARD_APPROVE`), `webapp/view|controller/marketCommodities/*` (lista + página com produtos e a tabela dos 12 meses; botão "Criar exemplos")
- Create: `webapp/services/MarketModuleService.ts` — `isEnabled(): Promise<boolean>` (`GET /market/module`; 404 → false; resultado guardado por sessão)
- Create: `webapp/view/fragments/market/ContractMarketSection.fragment.xml` — seção "Mercado": Instrumento de referência, Cotação da bolsa, Prêmio, Preço em US$ (por saca na tela, por kg no banco), Dólar, Câmbio fechado (somente leitura fora do rascunho)
- Modify: telas de contrato de compra e de venda (formulário e detalhe) — incluem a seção só quando `MarketModuleService.isEnabled()`; com o módulo desligado, nenhum pedido novo de dados e nenhuma diferença visual
- Modify: tela de assinaturas de notificação — rótulos do documento "Posição de mercado" e do evento "Limite de exposição excedido"
- Modify: `manifest.json`, `ServerRoutes.ts`, i18n

- [ ] **Step 1:** Implementar. **Step 2:** `yarn ts-typecheck`, `yarn lint` limpos; `ui5lint` sem problema novo.
- [ ] **Step 3:** Commit `feat(platform): limites, commodities e dados de mercado nos contratos`.
