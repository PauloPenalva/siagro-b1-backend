# Mercado — Parte 2: Preço por cidade + boleta — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Destinos, tabela de frete, tabela de preços, uma fórmula genérica de preço na origem, a grade "Ordem de Compra" ao vivo por cidade e a boleta com alçada que, aprovada, gera um contrato de compra preço fixo já aprovado.

**Architecture:** Entidades novas no `AppDbContext`. O preço é calculado por uma função pura (`MarketPriceCalculator`) a partir da cotação da parte 1, da linha da tabela de preços e do frete vigente da cidade; nada é materializado — a grade é recalculada a cada leitura e empurrada por SSE quando a `Version` do `LiveQuoteStore` da empresa muda. A boleta recalcula no servidor, congela todos os insumos e o passo a passo, e a aprovação final encadeia os serviços existentes de contrato (Create → SendApproval → Approval) de forma retomável.

**Tech Stack:** .NET 10, EF Core SQL Server, OData, SSE, xUnit + EF InMemory; OpenUI5 TypeScript.

**Spec:** `docs/superpowers/specs/2026-11-05-market-trading-module-design.md` (§4.2, §6, §8 "Parte 2")

**Repos:** backend `C:\Projetos\SiagroB1\worktrees\market\backend` e frontend `C:\Projetos\SiagroB1\worktrees\market\frontend`, ambos na branch `feature/market-pricing`. Commits separados por repo; `git add` de todo arquivo novo logo ao criar; **nunca push, nunca merge**. Escopo de commit: `platform`.

**Levantamentos (leitura obrigatória da tarefa que os cita):** `C:\Projetos\SiagroB1\siagro-b1-backend\.superpowers\recon\part2-backend.md`, `part2-frontend.md`, `part2-tagui-pricing.md` (o exemplo S1 desse arquivo tem um erro de conta no desconto; valem os valores deste plano).

## Global Constraints

- Identificadores em inglês, tabelas UPPER_SNAKE plural; textos ao usuário em pt-BR (i18n no frontend, mensagens de `BusinessException` em pt-BR no backend).
- Tudo do módulo só existe com `Modules:Market:Enabled = true`: todo controller novo leva `[MarketModuleEnabled]` (404 com módulo desligado); menus novos são filhos de `market`.
- Parceiro, produto, armazém e agente só por `IBusinessPartnerService`/`IItemService`/`IWarehouseService`/`IAgentService` (funciona em STANDALONE e SAPB1): guardar código + nome copiado, **sem FK** para eles. FK real só para `Municipality`, `HarvestSeason`, `MarketInstrument`, `MarketDestination`.
- Serviços registrados à mão em `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs`; entity sets em `ODataConfigurations.cs`; controller OData mapeia `BusinessException` → 400 (copiar o `Guard` do `MarketInstrumentsController`).
- **Permissões** (constantes em `PermissionCodes`, checadas com `IUserPermissions.HasAsync`, que respeita `IsAdmin`): `MARKET_MANAGE` (gravar instrumentos, cotação manual/liberação/fechamento manual, destinos, frete, tabela de preços, parâmetros gerais), `MARKET_TICKET_APPROVE` (aprovar/rejeitar boleta), `MARKET_BOARD_APPROVE` (alçada da diretoria e os limiares de alçada). Sem permissão → 403 com texto pt-BR. Leitura e criação de boleta: qualquer usuário autenticado com o módulo ligado.
- **Fórmula única** (qualquer instrumento; nada de `if` por commodity). Entradas e ordem exatas:
  1. `quote` = `Bid` se houver, senão `Last`; nulo ou ≤ 0 → status `MissingQuote`, sem preço.
  2. Moeda do instrumento: `UsCentsPerBushel` e `UsdPerTon` → US$; `BrlPerBag` → R$.
  3. US$: `slope = round6((curveFx − spotFx) / curveDays)`; `netFx = round4(curveFx + (dias(paymentDate − today) − curveDays) × slope − fxDiscount)`; falta de `spotFx`/`curveFx`/`curveDays>0` → status `MissingFx`. R$: `netFx = 1`.
  4. `gross = quote + premium` (prêmio na unidade da cotação).
  5. Base R$/saca 60 kg ("preço porto"): `BrlPerBag` → `gross − elevationCost × 0,06`; demais → `(gross × tonFactor − elevationCost) × netFx × 60 / 1000`.
  6. `interest = round2(base × dailyInterestPercent/100 × dias(paymentDate − receiptDate))` (pode ser negativo).
  7. `p = base × (1 − discount/100)` se desconto em %, senão `base − discount`.
  8. `p = p × (1 − marginPercent/100)`; 9. `p = p + interest`; 10. `p = p × (1 − technicalLossPercent/100)`.
  11. Frete: sem frete vigente → status `MissingFreight`; `freightPerBag = round2((freightPerTon + round2(freightPerTon × freightCoefficientPercent/100)) × 60/1000)`; `p = p − freightPerBag`.
  12. `price = round2(p × (1 − ruralTaxPercent/100))`.
  Arredondamento sempre `MidpointRounding.AwayFromZero`; percentuais divididos com precisão plena; `today` = data de São Paulo (`SaoPauloTime`), injetada.
- **Oráculo** (hoje = 2026-11-06; os três primeiros reproduzem o Tagui ao centavo; o quarto diverge 2 centavos de propósito — prêmio sempre antes dos ajustes):
  - S1 soja: quote 1188.25, UsCentsPerBushel, tonFactor 0.36745, prêmio 50, elevação 8, spot 5.4000, curva 5.4650 (30 d), fxDiscount 0.02, juros 0.033 %/dia, margem 1.5, quebra 0.3, funrural 1.5, coef. frete 10, desconto 0.5 %, receb. 2027-03-15, pagto 2027-04-15, frete 180 → netFx **5.7267**, base 153.588363104925, juros 1.57, frete/sc 11.88, preço **137.67**.
  - S2 soja: quote 1050.50, prêmio −20, elevação 12.5, spot 5.3125, curva 5.4010 (60 d), fxDiscount 0, juros 0.04, margem 2, quebra 0.25, funrural 0, coef 0, desconto R$ 1.20, receb. 2027-05-20, pagto 2027-05-05, frete 95.50 → netFx **5.5780**, juros −0.74, frete/sc 5.73, preço **112.15**.
  - M1 milho CBOT: quote 430.75, tonFactor 0.393678, prêmio 85, elevação 10, spot 5.4000, curva 5.4320 (30 d), fxDiscount 0.015, juros 0.03, margem 2, quebra 0.25, funrural 1.5, coef 5, desconto 1 %, receb. 2027-07-01, pagto 2027-07-31, frete 150 → netFx **5.6699**, juros 0.59, frete/sc 9.45, preço **53.87**.
  - B1 milho B3: quote 68.50, BrlPerBag, prêmio 1.50, elevação 0, juros 0.03, margem 2, quebra 0.25, funrural 0, coef 5, desconto R$ 1.00, receb. 2027-07-01, pagto 2027-07-31, frete 120 → base 70.00, juros 0.63, frete/sc 7.56, preço **60.52**.
- Todo número exibido tem origem rastreável: a linha da grade e a boleta carregam cotação usada, data-hora e origem da cotação, dólar, frete e a lista de passos.
- Contratos do SiagroB1 guardam volume em **kg** e preço em **R$/kg**; a boleta guarda `VolumeKg` e preço em **R$/saca 60 kg**; conversão do contrato: `TotalVolume = VolumeKg`, `StandardPrice = round6(NegotiatedPrice / 60)`.
- Datas de negócio em colunas `DATE` (no UI5: `sap.ui.model.odata.type.Date`); carimbos em UTC serializados com `Z`.
- Migration nova exige trailer `DB:` no commit. Não rodar `dotnet ef database update` (o controlador aplica nos bancos locais).

## Review Focus

1. Cotação ausente/zerada, dólar ou frete faltando → a linha da grade aparece com situação explícita e **sem preço**; nunca preço velho, nunca exceção que derrube a grade inteira. Testes nos Tasks 2 e 4.
2. Números enviados pelo navegador não formam preço: a boleta é recalculada no servidor na criação; o cliente só informa o preço negociado, que entra no desvio. Teste no Task 5.
3. Aprovação repetida depois de falha no meio (contrato criado, aprovação do contrato falhou) retoma de onde parou e **não duplica contrato**. Teste no Task 5.
4. Dois aprovadores ao mesmo tempo → um contrato só (token de concorrência na boleta). Teste no Task 5.
5. Linha de tabela apontando para instrumento inativo, ou instrumento em US$ sem curva/spot configurados → situação `InvalidSetup`/`MissingFx` naquela linha; as demais calculam. Teste no Task 4.

---

### Task 1: Entidades, permissões e migrations

**Files:**
- Create: `SiagroB1.Domain/Enums/MarketPriceStatus.cs` (`Ok=0, MissingQuote=1, MissingFx=2, MissingFreight=3, InvalidSetup=4`), `MarketTradeTicketStatus.cs` (`Pending=0, PendingBoard=1, Approved=2, Rejected=3, Canceled=4`)
- Create: `SiagroB1.Domain/Entities/Market/MarketDestination.cs` → `MARKET_DESTINATIONS`: `Id` int identity, `Name` (VARCHAR(100), único), `Active`
- Create: `MarketFreightRate.cs` → `MARKET_FREIGHT_RATES`: `Id`, `MunicipalityCode` (VARCHAR(7), FK `Municipality`), `DestinationId` (FK), `RatePerTon` DECIMAL(18,2), `ValidFrom` DATE, `CreatedBy`, `CreatedAt`; índice único (`MunicipalityCode`, `DestinationId`, `ValidFrom`)
- Create: `MarketPriceTable.cs` → `MARKET_PRICE_TABLES`: `Id`, `Description` (100), `ItemCode` (VARCHAR(10)), `ItemName` (200), `HarvestSeasonCode` (FK), `Active`, `UpdatedBy`, `UpdatedAt`, `Lines`
- Create: `MarketPriceTableLine.cs` → `MARKET_PRICE_TABLE_LINES`: `Id`, `PriceTableId` (FK cascade), `Description` (100), `InstrumentCode` (FK `MarketInstrument`, restrict), `FxCurveInstrumentCode` (FK nullable, restrict), `DestinationId` (FK restrict), `Premium`, `ElevationCost`, `FxDiscount`, `DiscountValue` DECIMAL(18,6), `DiscountIsPercent` bool, `MarginPercent`, `DailyInterestPercent`, `TechnicalLossPercent`, `RuralTaxPercent`, `FreightCoefficientPercent` DECIMAL(18,8), `DeliveryStartDate`, `DeliveryEndDate`, `ReceiptDate`, `PaymentDate` (DATE), `SortOrder`, `Active`
- Create: `MarketTradingSettings.cs` → `MARKET_TRADING_SETTINGS` singleton `Key=1` (padrão `CmaSettings.cs`): `SpotFxInstrumentCode` (nullable), `BoardApprovalVolumeTons` DECIMAL(18,3)? , `BoardApprovalDeviationPercent` DECIMAL(18,4)? (nulo = regra desligada), `ContractUnitOfMeasureCode` (VARCHAR(4)), `ContractFreightUmCode` (VARCHAR(4))
- Create: `MarketTradeTicket.cs` → `MARKET_TRADE_TICKETS`: `Id` int identity; `Status`; `CreatedAt`, `CreatedBy`; origem: `PriceTableId`, `PriceTableLineId` (int simples, sem FK), `PriceTableDescription`, `LineDescription`; `ItemCode`, `ItemName`, `HarvestSeasonCode` (FK), `MunicipalityCode` (FK), `DestinationId` (int simples), `DestinationName`, `InstrumentCode` (VARCHAR(30), sem FK); congelados: `ExchangeQuote`, `Premium`, `NetFx`?, `SpotFx`?, `FreightPerTon`, `PortPrice`, `CalculatedPrice` DECIMAL(18,2), `StepsJson` (nvarchar(max)), `QuotedAt`?, `QuoteSource`?; `DeliveryStartDate`, `DeliveryEndDate`, `ReceiptDate`, `PaymentDate`; comprador: `CardCode` (10), `CardName` (200), `VolumeKg` DECIMAL(18,3), `NegotiatedPrice` DECIMAL(18,2), `WarehouseCode` (10), `WarehouseName` (200), `AgentCode` int, `AgentName` (100), `BranchCode` (14), `LogisticRegionCode` (10), `Notes` (500); alçada: `DeviationPercent` DECIMAL(18,4), `RequiresBoardApproval`, `BoardApprovalReason` (200); `ApprovedBy/At`, `BoardApprovedBy/At`, `RejectedBy/At`, `RejectionReason` (500), `CanceledBy/At`; `PurchaseContractKey` Guid? (sem FK), `PurchaseContractCode` (50); `[Timestamp] RowVersion`
- Modify: `SiagroB1.Infra/Context/AppDbContext.cs` — 6 DbSets + índices/relacionamentos
- Modify: `SiagroB1.Application/Interfaces/IUserPermissions.cs` — `PermissionCodes.MarketManage = "MARKET_MANAGE"`, `MarketTicketApprove = "MARKET_TICKET_APPROVE"`, `MarketBoardApprove = "MARKET_BOARD_APPROVE"`
- Create: migration AppContext `AddMarketPricing` (só CREATE TABLE/índices)
- Create: migration CommonContext `AddMarketPricingMenuAndPermissions`: filhos de `market` com `[Order]`: `marketPriceGrid` "Ordem de Compra" 12, `marketTradeTickets` "Boletas" 14, `marketPriceTables` "Tabela de Preços" 16, `marketFreightRates` "Tabela de Frete" 18, `marketDestinations` "Destinos" 19, `marketTradingSettings` "Parâmetros do Mercado" 25 (ícone `sap-icon://folder-blank`, `ROLE_MENUS` do ADMIN com GUIDs fixos, tudo idempotente, padrão `20261105223537_AddMarketMenu.cs`); as 3 permissões + papéis `MARKET_TRADER` (manage + ticket approve) e `MARKET_BOARD` (ticket approve + board approve) + todas as 3 para ADMIN, no padrão de `20261026155720_SeedPaymentReleasePermissions.cs` e `20261026172425_AddPaymentReleaseDepartmentRoles.cs`
- Test: `SiagroB1.Application.Tests/Market/MarketPricingModelTests.cs`

**Interfaces — Produces:** as entidades e enums acima (nomes de propriedade exatos), `PermissionCodes.*`.

- [ ] **Step 1:** Teste com `TestDb`: gravar tabela de preços com 2 linhas e ler com `Include(Lines)`; gravar 2 `MarketFreightRate` do mesmo município/destino com `ValidFrom` diferentes; `MarketTradingSettings` singleton lê/grava.
- [ ] **Step 2:** `dotnet test SiagroB1.Application.Tests --filter FullyQualifiedName~MarketPricingModelTests` → FAIL.
- [ ] **Step 3:** Implementar entidades, DbSets e constantes.
- [ ] **Step 4:** Teste → PASS; `dotnet build SiagroB1.sln`.
- [ ] **Step 5:** Gerar as duas migrations (`--project SiagroB1.Migrations --startup-project SiagroB1.Web`); revisar com `dotnet ef migrations script <anterior> <nova> --context ...`: App só cria as 6 tabelas; Common só insere linhas.
- [ ] **Step 6:** Commit `feat(platform): entidades de preço por cidade e boleta do módulo Mercado` com `DB: AddMarketPricing` e `DB: AddMarketPricingMenuAndPermissions`.

### Task 2: Calculadora de preço (função pura)

**Files:**
- Create: `SiagroB1.Application/Services/Market/Pricing/MarketPriceCalculator.cs`
- Test: `SiagroB1.Application.Tests/Market/MarketPriceCalculatorTests.cs`

**Interfaces — Produces:**
```csharp
public sealed record MarketPriceInputs(
    decimal? Quote, MarketQuoteUnit QuoteUnit, decimal TonFactor, decimal Premium, decimal ElevationCost,
    decimal? SpotFx, decimal? CurveFx, int? CurveDays, decimal FxDiscount,
    DateOnly Today, DateOnly ReceiptDate, DateOnly PaymentDate,
    decimal DiscountValue, bool DiscountIsPercent, decimal MarginPercent, decimal DailyInterestPercent,
    decimal TechnicalLossPercent, decimal RuralTaxPercent, decimal? FreightPerTon, decimal FreightCoefficientPercent);
public sealed record MarketPriceStep(string Key, decimal Value, string Unit);
public sealed record MarketPriceResult(MarketPriceStatus Status, decimal? Price, decimal? NetFx, decimal? PortPrice,
    decimal? FreightPerBag, IReadOnlyList<MarketPriceStep> Steps);
public static class MarketPriceCalculator { public static MarketPriceResult Calculate(MarketPriceInputs input); }
```
Passos (`Key` estável, usado como chave i18n no frontend; `Unit` ∈ `quote`, `USD/t`, `BRL/USD`, `BRL/t`, `BRL/sc`): `exchange`, `premium`, `gross`, `perTon` (só fora de `BrlPerBag`), `elevation`, `netFx` (só US$), `portPrice`, `discount`, `margin`, `interest`, `technicalLoss`, `freight`, `ruralTax`, `price`. Cada passo de ajuste traz o **valor do ajuste em R$/sc** (ex.: `discount` = quanto saiu), e `price` o resultado. Com status ≠ `Ok`, `Price` é nulo e `Steps` traz o que foi possível calcular até ali.

- [ ] **Step 1:** Testes: os 4 casos do oráculo (Global Constraints) conferindo `NetFx`, `PortPrice` (arredondado a 6 casas), `FreightPerBag`, `Price`; `Quote` nulo e `0` → `MissingQuote`, `Price == null`; instrumento US$ sem `SpotFx`, sem `CurveFx` ou `CurveDays` 0/nulo → `MissingFx`; `FreightPerTon` nulo → `MissingFreight` com `PortPrice` preenchido; `UsdPerTon` com `TonFactor 1`; soma dos passos fecha: `portPrice − discount − margin + interest − technicalLoss − freight − ruralTax == price` com tolerância 0,01; não lança para nenhuma combinação (percentual 100, datas invertidas).
- [ ] **Step 2:** Rodar `--filter FullyQualifiedName~MarketPriceCalculatorTests` → FAIL.
- [ ] **Step 3:** Implementar seguindo a fórmula das Global Constraints.
- [ ] **Step 4:** Rodar → PASS.
- [ ] **Step 5:** Commit `feat(platform): fórmula única de preço na origem do módulo Mercado`.

### Task 3: Cadastros (destinos, frete, tabela de preços, parâmetros) e permissões

Ler antes: `part2-backend.md` §1, §4, §8.

**Files:**
- Create: `SiagroB1.Application/Services/Market/MarketPermissionGuard.cs` — `Task EnsureAsync(string userName, string permissionCode)` lança `ForbiddenException` (criar em `SiagroB1.Domain/Exceptions` se não existir análoga) com "Você não tem permissão para esta operação do Mercado."
- Create: `MarketDestinationService.cs` (`BaseService<MarketDestination,int>`; nome obrigatório e único; excluir com frete ou linha de tabela → `BusinessException` pt-BR)
- Create: `MarketFreightRateService.cs` (`BaseService<MarketFreightRate,int>`): valida município/destino existentes, `RatePerTon >= 0`, duplicidade da chave única → mensagem pt-BR; `Task<IReadOnlyDictionary<int, decimal>> GetEffectiveRatesAsync(string municipalityCode, DateOnly today, CancellationToken)` (por destino: a de maior `ValidFrom <= today`); `Task<IReadOnlyList<MarketFreightCity>> GetCitiesAsync(CancellationToken)` (`record MarketFreightCity(string Code, string Name, string StateAbbreviation)`, municípios com algum frete)
- Create: `MarketPriceTableService.cs` (`BaseService<MarketPriceTable,int>`) e `MarketPriceTableLineService.cs`: valida produto (`IItemService`, copia `ItemName`), safra, instrumento ativo, destino ativo, `DeliveryStartDate <= DeliveryEndDate`; instrumento em US$ exige `FxCurveInstrumentCode` de tipo `CurvePoint` com `CurveDays > 0`; percentuais entre 0 e 100
- Create: `MarketTradingSettingsService.cs` — `GetAsync`, `SaveAsync(MarketTradingSettingsInput input, string userName)`; alterar `BoardApproval*` exige `MARKET_BOARD_APPROVE`, o resto `MARKET_MANAGE`
- Modify: `MarketInstrumentService.cs` — excluir instrumento usado em linha de tabela → `BusinessException`
- Create: controllers OData `MarketDestinationsController`, `MarketFreightRatesController`, `MarketPriceTablesController`, `MarketPriceTableLinesController` (GET livre; POST/PATCH/PUT/DELETE exigem `MARKET_MANAGE`; `ForbiddenException` → 403; `$expand` de `Lines`, `Destination`, `Municipality` habilitado) + entity sets homônimos em `ODataConfigurations.cs`. Para cabeçalho + linhas, seguir o padrão de backend que a tela `processingCostsList` do frontend consome.
- Modify: `MarketInstrumentsController.cs` e `MarketController.cs` — gravações de instrumento, `POST quotes/{code}/manual`, `release`, `daily-closes` passam a exigir `MARKET_MANAGE` (as de ADMIN continuam ADMIN)
- Modify: `MarketController.cs` (ou novo `MarketPricingController` `[Route("market")]`) — `GET market/trading-settings`, `PUT market/trading-settings`, `GET market/freight-cities`
- Toda gravação que muda a grade (destino, frete, tabela, linha, parâmetros) chama `LiveQuoteStore.NotifyChanged(companyCode)`.
- Modify: `ServiceCollectionExtensions.cs`
- Test: `SiagroB1.Application.Tests/Market/MarketFreightRateServiceTests.cs`, `MarketPriceTableServiceTests.cs`, `MarketTradingSettingsServiceTests.cs`, `MarketPermissionGuardTests.cs`

- [ ] **Step 1:** Testes: `GetEffectiveRates_PicksLatestNotInFuture` (vigências 01/10, 01/11 e 01/12 com hoje 06/11 → a de 01/11); `GetEffectiveRates_NoneYet_OmitsDestination`; `FreightRate_DuplicateKey_BusinessException`; `PriceTableLine_UsdInstrumentWithoutCurve_Rejected`; `PriceTableLine_BrlInstrument_NoCurveNeeded`; `PriceTable_UnknownItem_Rejected`; `Destination_DeleteInUse_Rejected`; `Instrument_DeleteUsedByLine_Rejected`; `Settings_ThresholdChange_RequiresBoardPermission`; `Guard_Throws_WhenPermissionMissing` / passa com a permissão; gravação de frete incrementa `LiveQuoteStore.Get(company).Version`.
- [ ] **Step 2:** Rodar `--filter FullyQualifiedName~Market` → FAIL nos novos.
- [ ] **Step 3:** Implementar.
- [ ] **Step 4:** Rodar → PASS; `dotnet build SiagroB1.sln`.
- [ ] **Step 5:** Commit `feat(platform): cadastros de destino, frete e tabela de preços com permissões do Mercado`.

### Task 4: Grade "Ordem de Compra" (serviço, REST e SSE)

Ler antes: `part2-backend.md` §1 (classes `MarketQuotesSseWriter`/`MarketQuotesStreamLoop`).

**Files:**
- Create: `SiagroB1.Application/Services/Market/Pricing/MarketPriceGridService.cs`
- Modify: `SiagroB1.Web/Market/MarketQuotesStreamLoop.cs` (+ writer) — generalizar o laço para receber a função que monta o payload (o terminal de cotações continua com o mesmo comportamento e testes), ou extrair laço genérico e manter o atual como caso dele
- Modify/Create: controller REST — `GET market/price-grid?harvest={code}&city={ibge}` e `GET market/price-grid/live?harvest=&city=` (SSE: mesmo cabeçalho, heartbeat 15 s, poll 500 ms, escopo DI novo por emissão)
- Modify: `ServiceCollectionExtensions.cs`
- Test: `SiagroB1.Application.Tests/Market/MarketPriceGridServiceTests.cs` (+ teste do laço genérico no arquivo de testes do laço existente)

**Interfaces — Consumes:** `MarketPriceCalculator.Calculate`, `MarketFreightRateService.GetEffectiveRatesAsync`, `MarketQuote` (parte 1), `MarketTradingSettings.SpotFxInstrumentCode`.
**Produces:**
```csharp
public sealed record MarketPriceGridRow(
    int LineId, int PriceTableId, string PriceTableDescription, string ItemCode, string ItemName, string LineDescription,
    string InstrumentCode, int DestinationId, string DestinationName,
    DateOnly DeliveryStartDate, DateOnly DeliveryEndDate, DateOnly ReceiptDate, DateOnly PaymentDate,
    decimal? ExchangeQuote, decimal? ExchangeLast, decimal? ChangePercent, decimal Premium, decimal? SpotFx, decimal? NetFx,
    decimal? PortPrice, decimal? FreightPerTon, decimal? Price, MarketPriceStatus Status,
    DateTime? QuotedAt, MarketQuoteSource? QuoteSource, IReadOnlyList<MarketPriceStep> Steps);
public sealed record MarketPriceGrid(string HarvestSeasonCode, string MunicipalityCode, DateTime GeneratedAt, IReadOnlyList<MarketPriceGridRow> Rows);
Task<MarketPriceGrid> GetGridAsync(string harvestSeasonCode, string municipalityCode, DateOnly today, CancellationToken ct);
Task<MarketPriceGridRow?> GetRowAsync(int priceTableLineId, string municipalityCode, DateOnly today, CancellationToken ct); // usado pela boleta
```
Linhas = linhas ativas de tabelas ativas da safra, ordenadas por produto, tabela, `SortOrder`, `PaymentDate`. Leitura com no máximo 4 consultas (linhas+instrumentos, cotações, fretes, parâmetros), `AsNoTracking`.

- [ ] **Step 1:** Testes: `Grid_CalculatesS1_FromDatabaseRows` (monta instrumento ZS, DOL COM, curva 30D, tabela, frete → preço 137.67); `Grid_UsesBid_FallsBackToLast`; `Grid_MissingQuote_RowWithoutPrice_OthersStillPriced`; `Grid_InactiveInstrument_InvalidSetup`; `Grid_NoSpotConfigured_MissingFx`; `Grid_NoFreightForCity_MissingFreight`; `Grid_ExcludesInactiveTablesAndLines_AndOtherHarvests`; `Grid_FutureFreightValidity_NotUsed`; `GetRow_ReturnsSameAsGrid`.
- [ ] **Step 2:** Rodar → FAIL.
- [ ] **Step 3:** Implementar.
- [ ] **Step 4:** Rodar `--filter FullyQualifiedName~Market` → PASS; `dotnet build SiagroB1.sln`.
- [ ] **Step 5:** Commit `feat(platform): grade de preços por cidade ao vivo`.

### Task 5: Boleta — criação, alçada, aprovação e contrato

Ler antes: `part2-backend.md` §2, §3, §6, §8 (itens 7, 8, 9).

**Files:**
- Create: `SiagroB1.Application/Services/Market/Tickets/MarketTradeTicketService.cs`:
  - `Task<MarketTradeTicket> CreateAsync(MarketTradeTicketInput input, string userName, DateOnly today, CancellationToken ct)`; `public sealed record MarketTradeTicketInput(int PriceTableLineId, string MunicipalityCode, string CardCode, decimal VolumeKg, decimal NegotiatedPrice, string WarehouseCode, int AgentCode, string BranchCode, string LogisticRegionCode, string? Notes)`
  - `Task<MarketTradeTicket> ApproveAsync(int id, string userName, CancellationToken ct)`; `RejectAsync(int id, string reason, string userName, ...)`; `CancelAsync(int id, string userName, ...)`
- Create: `SiagroB1.Application/Services/Market/Tickets/MarketTradeTicketContractService.cs` — `Task GenerateAsync(MarketTradeTicket ticket, string approvedBy, CancellationToken ct)` (retomável)
- Create: `SiagroB1.Web/Controllers/MarketTradeTicketsController.cs` — OData **só leitura** (`MarketTradeTickets`, `[EnableQuery]`) + REST `POST market/tickets`, `POST market/tickets/{id}/approve`, `/reject` (`{ reason }`), `/cancel`
- Modify: `ODataConfigurations.cs`, `ServiceCollectionExtensions.cs`
- Test: `SiagroB1.Application.Tests/Market/MarketTradeTicketServiceTests.cs`, `MarketTradeTicketContractServiceTests.cs` (fakes de `DocNumbers/DocumentCreateContractsTests.cs` l.21-47 e `Financials/FinancialDocumentHooksTests.cs`)

**Regras:**
- `CreateAsync`: valida parceiro, armazém, agente (pelos `I*Service`), filial, região logística, `VolumeKg > 0`, `NegotiatedPrice > 0`, parâmetros `ContractUnitOfMeasureCode`/`ContractFreightUmCode` preenchidos; chama `GetRowAsync`; linha inexistente/inativa ou `Status != Ok` → `BusinessException` ("Linha sem preço: <situação>"); congela todos os campos da linha + `StepsJson`; `DeviationPercent = (NegotiatedPrice − CalculatedPrice) / CalculatedPrice × 100` (4 casas); `RequiresBoardApproval` = `VolumeKg/1000 >= BoardApprovalVolumeTons` **ou** `|DeviationPercent| >= BoardApprovalDeviationPercent` (limiar nulo não dispara), com `BoardApprovalReason` em pt-BR listando os motivos; status `Pending`.
- `ApproveAsync`: `Pending` exige `MARKET_TICKET_APPROVE` → grava `ApprovedBy/At`; se `RequiresBoardApproval` e o usuário **não** tem `MARKET_BOARD_APPROVE` → `PendingBoard` e para; se tem (ou não requer) → segue para final. `PendingBoard` exige `MARKET_BOARD_APPROVE` → grava `BoardApprovedBy/At`. Final: `GenerateAsync` e então `Status = Approved`. Qualquer outro status → `BusinessException`.
- `RejectAsync`: só `Pending`/`PendingBoard`; motivo obrigatório; mesma permissão do nível atual. `CancelAsync`: só `Pending`/`PendingBoard`; criador ou quem tem `MARKET_TICKET_APPROVE`.
- `GenerateAsync` (sem transação própria — `PurchaseContractsCreateService` abre a dele): se `PurchaseContractKey` nulo → cria contrato `Type=Fixed`, `MarketType=Internal`, `CardCode`, `ItemCode`, `UnitOfMeasureCode`/`FreightUmCode` dos parâmetros, `HarvestSeasonCode`, `DeliveryLocationCode=WarehouseCode`, `AgentCode`, `BranchCode`, `LogisticRegionCode`, `FreightTerms=Fob`, `FreightCostStandard=0`, datas de entrega, `TotalVolume=VolumeKg`, `StandardPrice=round6(NegotiatedPrice/60)`, `StandardCurrency=Brl`, `StandardCashFlowDate=PaymentDate`, `Comments="Gerado pela boleta nº {Id}"` e **grava a chave/código na boleta imediatamente**; depois, conforme o status atual do contrato: `Draft` → `PurchaseContractsSendApprovalService`; `InApproval` → `PurchaseContractsApprovalService.ExecuteAsync(key, "Aprovado pela boleta nº {Id}", approvedBy)`; `Approved` → nada. Chamar de novo depois de falha continua do ponto em que parou.
- Concorrência: `RowVersion` na boleta; `DbUpdateConcurrencyException` → `BusinessException` "A boleta foi alterada por outro usuário. Atualize e tente de novo."

- [ ] **Step 1:** Testes: `Create_FreezesServerCalculation_IgnoringClientNumbers` (preço calculado 137.67 gravado; passos em `StepsJson`); `Create_RowWithoutPrice_Rejected`; `Create_UnknownPartnerOrWarehouseOrAgent_Rejected`; `Create_Deviation_And_BoardFlags` (volume ≥ limiar; desvio ≥ limiar; limiares nulos → não requer); `Approve_WithoutPermission_Forbidden`; `Approve_NoBoardNeeded_GeneratesApprovedContract` (contrato `Approved`, `TotalVolume`, `StandardPrice`, `StandardCashFlowDate`, 1 título provisório; boleta `Approved` com chave e código); `Approve_BoardNeeded_FirstLevelGoesToPendingBoard_NoContract`; `Approve_BoardMember_FromPending_FinishesInOneStep`; `Approve_PendingBoard_WithoutBoardPermission_Forbidden`; `Approve_Twice_SecondFails_OneContract`; `Generate_ResumesAfterFailure` (contrato já criado em `Draft`/`InApproval` e chave na boleta → termina sem criar outro); `Reject_RequiresReason_AndOpenStatus`; `Cancel_ByCreator_Ok_ByOther_Forbidden`.
- [ ] **Step 2:** Rodar → FAIL.
- [ ] **Step 3:** Implementar.
- [ ] **Step 4:** Rodar `--filter FullyQualifiedName~Market` → PASS; suíte `SiagroB1.Application.Tests` inteira verde.
- [ ] **Step 5:** Commit `feat(platform): boleta de compra com alçada e geração do contrato aprovado`.

### Task 6: Frontend — Destinos, Tabela de Frete, Tabela de Preços, Parâmetros

Repo frontend. Ler antes: `part2-frontend.md` inteiro.

**Files:**
- Modify: `webapp/model/ServerRoutes.ts`, `webapp/manifest.json` (rotas = Keys do menu: `marketDestinations`, `marketFreightRates`, `marketPriceTables` (+New/Edit), `marketTradingSettings`), `webapp/i18n/i18n.properties` + `i18n_en.properties`
- Create: `webapp/view|controller/marketDestinations/*` (CRUD no molde `marketInstruments`)
- Create: `webapp/view|controller/marketFreightRates/*` — lista com FilterBar (município, destino, vigência), incluir/editar em diálogo: município (value help `MunicipalitiesSelectDialog`), destino (Select), R$/t, vigência a partir de
- Create: `webapp/view|controller/marketPriceTables/*` — lista + página de cabeçalho (descrição, produto e safra por value help, ativa) com tabela editável de linhas (molde `processingCostsList`: `create()` na binding de linhas e um `submitBatch`); linha: descrição, instrumento (Select dos ativos de tipo futuro/spot), curva de dólar (Select dos `CurvePoint`), destino, prêmio, elevação, desconto do dólar, entrega início/fim, recebimento, pagamento, desconto + "em %", margem, juros % ao dia, quebra técnica, funrural, coeficiente de frete, ordem, ativa
- Create: `webapp/view|controller/marketTradingSettings/Main.*` — Form (ColumnLayout): dólar à vista (Select de instrumentos), limiares de alçada (volume em t, desvio %), unidade do contrato e unidade do frete
- Modify: `webapp/controller/marketInstruments/*` e terminal — esconder gravações sem `MARKET_MANAGE` (`SessionService.hasPermission`); `marketFetch` aceita `DELETE`; 403 mostra o texto do servidor
- Create: tipos em `webapp/types/Market*.ts`
- Test: `webapp/test/unit/helpers/MarketHelpers.qunit.ts` para qualquer helper novo (registrado em `unitTests.qunit.ts`)

- [ ] **Step 1:** Implementar. Decimais editáveis com `sap.ui.model.odata.type.Double`; datas `Edm.Date` com `sap.ui.model.odata.type.Date`; nunca `SimpleForm`.
- [ ] **Step 2:** `yarn ts-typecheck` e `yarn lint` limpos; `yarn ui5lint` sem problema novo.
- [ ] **Step 3:** Commit `feat(platform): cadastros de destino, frete e tabela de preços do Mercado`.

### Task 7: Frontend — grade "Ordem de Compra" e criação da boleta

**Files:**
- Modify: `webapp/services/MarketLiveService.ts` — `start(url, onData, onError)` genérico (o terminal passa a URL dele)
- Modify: `webapp/helpers/MarketHelpers.ts` — generalizar `withFlashes`/`clearFlashes` para chave e campos configuráveis (assinatura atual do terminal preservada por wrapper); CSS de flash passa a valer também sob a classe da nova tela
- Create: `webapp/view|controller/marketPriceGrid/Main.*` (rota `marketPriceGrid`, `market/price-grid`): cabeçalho com Safra (value help) e Cidade (Select de `GET market/freight-cities`), lembrados em `localStorage` (try/catch); `sap.ui.table.Table` sem quebra de linha: Contrato, Produto, Destino, Entrega, Pagamento, **Preço R$/sc**, Preço porto, Bolsa, Var %, Prêmio, Dólar, Frete R$/t, Situação (`ObjectStatus` pt-BR para cada `MarketPriceStatus`), Atualizado; piscam verde/vermelho Preço, Preço porto, Bolsa e Dólar; SSE aberto só com safra+cidade escolhidas e fechado ao sair da rota; seleção preservada entre eventos
- Create: `fragments/PriceBreakdown.fragment.xml` — "Composição do preço": lista dos `steps` (rótulo i18n `marketStep_<key>`, valor, unidade) + cotação usada, origem e hora
- Create: `fragments/TradeTicketDialog.fragment.xml` — "Comprar": resumo da linha (somente leitura) e campos Parceiro, Quantidade (sacas; mostra o equivalente em t), Preço negociado R$/sc (inicia com o preço da grade), Armazém, Comprador/Agente, Filial, Região logística, Observações; envia `POST market/tickets` com `volumeKg = sacas × 60`; mostra aviso quando o servidor devolve `requiresBoardApproval`; ao gravar navega para o detalhe da boleta
- Test: QUnit dos helpers generalizados

- [ ] **Step 1:** Implementar.
- [ ] **Step 2:** `yarn ts-typecheck`, `yarn lint` limpos; `yarn ui5lint` sem problema novo.
- [ ] **Step 3:** Commit `feat(platform): grade de ordem de compra ao vivo e emissão de boleta`.

### Task 8: Frontend — Boletas (lista, detalhe, aprovar/rejeitar/cancelar)

**Files:**
- Create: `webapp/view|controller/marketTradeTickets/Main.*` — lista OData com FilterBar (situação, safra, parceiro, período), colunas Nº, Data, Situação, Parceiro, Produto, Cidade, Quantidade (sc), Preço negociado, Preço calculado, Desvio %, Contrato
- Create: `webapp/view|controller/marketTradeTickets/Detail.*` (rota `marketTradeTicketsDetail`, `market/tickets/{id}`) — ObjectPage: cabeçalho com situação e motivo de alçada; seções Negócio, Preço congelado (composição a partir de `StepsJson`), Aprovação (quem/quando, motivo de rejeição), Contrato gerado (link para o contrato de compra); ações Aprovar, Rejeitar (`DialogHelper.promptDialog` para o motivo) e Cancelar, visíveis conforme situação e permissões (`MARKET_TICKET_APPROVE`, `MARKET_BOARD_APPROVE`); erro 400/403 mostra o texto do servidor
- Create: helper `ticketActions(status, permissions, isCreator)` em `MarketHelpers.ts` + QUnit
- Modify: `manifest.json`, `ServerRoutes.ts`, i18n

- [ ] **Step 1:** Implementar.
- [ ] **Step 2:** `yarn ts-typecheck`, `yarn lint` limpos; `yarn ui5lint` sem problema novo.
- [ ] **Step 3:** Commit `feat(platform): boletas de compra com aprovação por alçada`.
