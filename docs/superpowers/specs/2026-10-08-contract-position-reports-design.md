# Relatórios de posição de contratos — pacote 3

Data: 2026-10-08 · Branch: `feature/contract-position-reports` (backend e frontend, a partir de
`main` 1ef3eb8 / f88e0d1) · Design aprovado pelo usuário em 08/10/2026.

## Contexto e objetivo

Terceiro e último pacote de relatórios de compras e vendas (o 1, documentos fiscais, e o 2,
logística de venda, estão em `main`: `docs/superpowers/specs/2026-10-08-fiscal-documents-reports-design.md`
e `docs/superpowers/specs/2026-10-08-logistics-reports-design.md`). Este pacote mostra a **posição
atual** dos contratos de compra e de venda — contratado, entregue, washout e saldo — lado a lado,
e o saldo a entregar distribuído pelo mês do término da entrega. Funciona em SAPB1 e STANDALONE.
Saída **só em PDF**.

Reaproveita a base dos pacotes 1 e 2: serviço `SiagroB1.Reports`, FastReport, `POST /reports/<Nome>`,
serviço com `BuildRowsAsync` (testável) + `ExecuteAsync` (PDF), `.frx` paisagem com
`picLogo`/`pCompanyName`, `ReportText`/`LogisticsReportText` (`StatusFilter`, `BranchNameAsync`),
`InvoiceItemGrouping`, o mecanismo `pSingleUom` + `uomSum*`/`uomMixed*` do `FastReportService`, a
regra de largura medida no PDF, telas UI5 sobre o controller-base `InvoiceReportController` com
filtros em duas colunas e itens de menu por migration do `CommonContext`.

## Escopo — 2 relatórios

| # | Nome (tela/menu) | Chave de menu = rota | Padrão da rota | Endpoint | Template / fonte de dados |
|---|---|---|---|---|---|
| 1 | Contratos — Compra x Venda | `contractPositionReport` | `contract-position/report` | `POST /reports/ContractPosition` | `ContractPosition.frx` / `ContractPositions` |
| 2 | Posição Comprado x Vendido por Mês | `contractMonthlyPositionReport` | `contract-monthly-position/report` | `POST /reports/ContractMonthlyPosition` | `ContractMonthlyPosition.frx` / `ContractMonthlyPositions` |

Fora de escopo: Excel, leitura de dados do SAP, datas de entrega realizada (a posição é a do
momento da emissão; não há corte por data de romaneio/nota). Os relatórios existentes
`PurchaseContractsByItem`/`SalesContractsByItem` (lista de negócios, sem saldo) e o legado
`PurchaseContracts.frx` continuam como estão.

## Arquitetura

- `Controllers/<Nome>Controller.cs` — `[HttpPost]`, `[FromBody] <Nome>Request`. **Sem 400 de
  período**: não há período; todo filtro é opcional. Resposta `application/pdf`, `inline`.
- `Services/<Nome>ReportService.cs` — `(IUnitOfWork db, IFastReportService reportService)`,
  registrado pelo Scrutor (sufixo `Service`), sem mexer em DI. `BuildRowsAsync(request)` e
  `ExecuteAsync(request)`; ambos têm sobrecarga com `DateTime today` (o padrão é `DateTime.Today`,
  data do servidor) para os testes fixarem "hoje".
- `Dtos/ContractPositionReportRequest.cs` (base abstrata), `Dtos/<Nome>Request.cs`, `Dtos/<Nome>RowDto.cs`.
- Base comum nova: `Dtos/ContractPosition.cs` (um contrato de qualquer lado com entregue/saldo),
  `Dtos/ContractPositionSide.cs`, `Helpers/ContractPositionData.cs` (consultas + razão),
  `Helpers/ContractPositionText.cs` (rótulos, filtros, ordem).

### Regra de modo (SAPB1 / STANDALONE)

Nada de JOIN com `ITEMS`, `BUSINESS_PARTNERS`, `UNITS_OF_MEASURE` (vazias em SAPB1) e nem com
`HARVEST_SEASONS` (a FK é obrigatória: o `Include` viraria INNER JOIN). Lê só
`PURCHASE_CONTRACTS`, `SALES_CONTRACTS` (snapshots `CardName`, `ItemName`, `UnitOfMeasureCode`,
`HarvestSeasonCode`), `PURCHASE_CONTRACTS_ALLOCATIONS`, `PURCHASE_CONTRACTS_WASHOUTS`,
`SALES_CONTRACTS_ALLOCATIONS`, `SALES_INVOICES_ITEMS` e `BRANCHS`. Sem coluna fiscal ⇒ sem
`pStandalone`; o mesmo PDF sai nos dois modos (os testes geram nos dois).

### Entregue e saldo — iguais ao domínio depois de um recálculo

O saldo do relatório **tem de ser** o `AvaiableVolume` do domínio depois de um recálculo. As
fórmulas moram no `SiagroB1.Application`, que o `SiagroB1.Reports` **não referencia** (Reports →
Infra/Commons/Security). Decisão: **reimplementar no Reports, em lote, e travar com teste cruzado**
— o `SiagroB1.Application.Tests` referencia os dois projetos, roda as duas contas sobre o mesmo
dado e exige o mesmo número. O Application não muda.

| Lado | Entregue | Washout | Saldo |
|---|---|---|---|
| Compra | Σ `PURCHASE_CONTRACTS_ALLOCATIONS.Volume` **com sinal** (devolução já é negativa; tipos 10/11 gravam 0) = `PurchaseContractsRecalculateBalanceService.CalculateAllocatedAsync` | Σ (`FixedVolume` + `UnfixedVolume`) dos washouts **InApproval + Approved**, arredondado a 3 = `PurchaseContractsWashedOutVolumeService.ActiveVolumesAsync().Total` | `Round(TotalVolume − Entregue − Washout, 2)` = `PurchaseContract.AvaiableVolume` |
| Venda | `Round(Σ Volume com sinal − quebra, 3)`; quebra = Σ `Quantity − (DeliveredQuantity − QuantityLoss)` das linhas `OwnsDeliveryDifference` cujo item está `DeliveryStatus = Closed` = `SalesContractsRecalculateBalanceService.CalculateAllocatedAsync` | 0 | `Round(TotalVolume − Entregue, 3)` = `SalesContract.AvaiableVolume` (pode ser negativo) |

- Nunca lê `AllocatedVolume` nem `WashedOutVolume` persistidos (derivados que derivam).
- Em lote: `GROUP BY` por contrato com `IN (SELECT Key …)` da própria consulta de contratos (sem
  `Contains` sobre lista de chaves em memória). A quebra projeta antes de agrupar para o SQL Server
  traduzir a navegação. Um teste passa as consultas pelo tradutor do SQL Server
  (`ToQueryString` sem conexão), porque o InMemory aceita LINQ que o SQL Server recusa.
- O saldo de compra arredonda a **2** casas (como o domínio e a tela "Saldo (Físico)"); o de venda
  a 3.

### Filtros comuns (os dois relatórios)

| Filtro | Regra |
|---|---|
| Situação (multi) | `ContractStatus`; vazia = **só Aprovado**. Todos os 6 oferecidos. Situação nula no banco conta como Rascunho. |
| Filial | `BranchCode` do contrato |
| Produto | `ItemCode` |
| Safra | `HarvestSeasonCode` |
| Parceiro | `CardCode` — fornecedor na compra, cliente na venda (filtra os dois lados) |
| Tipo | `ContractType` (FIX/PAF); vazio = os dois |
| Término da entrega até | `DeliveryEndDate ≤ data` (inclusive o dia todo). Contrato **sem prazo** não passa. |

**Sem prazo**: `DeliveryEndDate` é `NOT NULL`; "sem prazo" só existe como data vazia
(`0001-01-01`, ou `1900-01-01` de carga legada). Tudo antes de **1901-01-01** é sem prazo.

Linha de filtros (`pFilters`): `Posição em: dd/MM/yyyy | Lado: … (só relatório 1) | Filial: … |
Produto: … | Safra: … | Parceiro: … | Tipo: FIX - Preço Fixo | Término da entrega até: … |
Situação: …`. Situação = "todas", "todas, exceto Cancelado" ou a lista de rótulos na ordem
numérica do enum (`LogisticsReportText.StatusFilter`). Filial pela tabela local `BRANCHS`; produto
e parceiro pelos snapshots das posições lidas, com o código como fallback.

### Rótulos (iguais aos de `siagro-b1-frontend/webapp/model/formatter.ts`)

| Enum | Rótulos |
|---|---|
| `ContractStatus` | Draft "Rascunho", Approved "Aprovado", Finished "Finalizado", Canceled "Cancelado", InApproval "Em Aprovação", Rejected "Rejeitado" |
| `ContractType` | Fixed "FIX - Preço Fixo", ToBeDetermined "PAF - Preço a Fixar" (na coluna estreita: "FIX"/"PAF") |
| Lado | Both "Compra e venda", Purchase "Compra", Sales "Venda" |

## Relatórios

### 1. Contratos — Compra x Venda

- Filtros: os comuns + **Lado** (`ContractPositionSide`: 0 Ambos, 1 Compra, 2 Venda; padrão Ambos).
  Com um lado só, a seção do outro some.
- Grupo = produto + UM (`InvoiceItemGrouping`), ordenado com `StringComparer.CurrentCultureIgnoreCase`.
  Dentro do grupo, seção **Compras** e depois **Vendas** (grupo aninhado `Section`). Dentro da seção:
  Prev. pagto (`StandardCashFlowDate`; sem previsão por último), Término da entrega (sem prazo por
  último), Código (ordinal).
- Colunas: Código · Emissão (`CreationDate`) · Fornecedor / Cliente `(CardCode) CardName` · Safra
  (`HarvestSeasonCode`) · Tipo (FIX/PAF) · Prev. pagto · Entrega até (`DeliveryEndDate`; vazio sem
  prazo) · Preço (compra `StandardPrice`, venda `Price`, 2 casas) · Contratado (`TotalVolume`) ·
  Entregue · Washout (só compra; zero sai em branco) · Saldo · Situação.
- Total da seção: Contratado, Entregue, Washout, Saldo ("Total Compras:" / "Total Vendas:").
- **Saldo geral do bloco** (`SignedBalanceQuantity`): com os dois lados, saldo compra − saldo venda
  ("Saldo geral (compra - venda):"); com um lado só, o próprio saldo daquele lado, positivo
  ("Saldo geral (compra):" / "Saldo geral (venda):"). Rótulo via `pGeneralLabel`.
- Total geral: contagem de contratos sempre; saldo geral do relatório só com **uma única UM**
  (`pSingleUom`; `uomSumLabel`/`uomSumSignedBalanceQuantity`; nota `uomMixedNote`).
- Saldo mostrado é o do domínio para qualquer situação (inclusive Finalizado, se pedido).

### 2. Posição Comprado x Vendido por Mês

- Filtros: os comuns, sem Lado (os dois sempre).
- Grupo = produto + safra + UM: "SOJA EM GRÃOS (10001) - Safra 25/26 - KG", ordenado com
  `CurrentCultureIgnoreCase`.
- Cabeçalho do grupo (valores do grupo repetidos em toda linha do DTO): **Contratado** (compra,
  venda, líquido), **Entregue** (compra, venda, líquido), **Washout (compra)** (compra e líquido).
- Linhas = prazos, com saldo **a entregar** de compra, de venda, líquido (compra − venda) e
  **líquido acumulado**:
  - **Vencido**: término antes de hoje (data do servidor na emissão). Sai **sempre** (mesmo zerado):
    é onde o acumulado começa.
  - Um **mês** (`MM/yyyy`) por mês do término, a partir de hoje (o mês atual traz o que vence de
    hoje em diante), só os meses com algum contrato, em ordem.
  - **Sem prazo**: por último, entra no acumulado.
- A entregar = saldo do contrato; **0** para Finalizado (encerrar é abrir mão do não entregue),
  Cancelado e Rejeitado — que continuam contando no Contratado e no Entregue. Saldo negativo entra
  como está (reduz o mês).
- Rodapé do grupo: "Saldo a entregar:" com Σ compra, venda, líquido. Fecha a conta:
  Contratado − Entregue − Washout = Σ a entregar (para contratos ainda vivos).
- Total geral: contagem de contratos (`pContractCount`); Σ a entregar só com uma única UM.

## Layout

Mesma regra dos pacotes 1 e 2 (`(caracteres × 5,2 + 4) × corpo/7 + 4` px; 1084 px úteis; totais em
Consolas 6pt bold numa linha só; `GroupHeaderBand SortOrder="None"`; cabeçalho nunca corta).

- Relatório 1: Código 72 (`PC2026000123`, 12) · Emissão 62 · Fornecedor / Cliente 250 · Safra 60
  (VARCHAR(10)) · Tipo 30 (cabeçalho "Tipo") · Prev. pagto 66 · Entrega até 66 · Preço 72 ·
  Contratado/Entregue/Washout 82 · **Saldo 88** · Situação 72 ("Em Aprovação", 12) = 1084.
- **Saldo negativo medido em 08/10**: "-10.000.000,000" (15 car.) saiu "-10.000.000,0…" com 86 px a
  7pt (a conta dá 86,0, no limite) e coube com 88. Os templates do pacote 2 usam Saldo de 86 px e
  provavelmente cortam o mesmo valor — registrado, fora do escopo deste pacote.
- Relatório 2: Prazo de entrega 244 · Compra/Venda/Líquido/Acumulado 210 cada (todo número pode ser
  negativo; "-999.999.999,999" cabe com folga).
- `ContractPositionReportsLayoutTests` mede tudo isso sem renderizar.

## Frontend

- Duas telas `webapp/{controller,view}/reports/{contractPosition,contractMonthlyPosition}/Main.*`
  sobre um controller-base novo e abstrato `ContractPositionReportController` (estende
  `InvoiceReportController` sem alterá-lo): `defaults()` = Situação `["1"]` (Aprovado) e Tipo vazio;
  `buildPayload` converte Tipo (e Lado, na tela 1) para número.
- **Sem período obrigatório**: o `validateForm` do `BaseController` só barra controles com
  `required="true"`; as telas não têm nenhum, então nada muda no base. "Término da entrega até" é
  `DatePicker` opcional (tipo `DateTimeOffset` via `core:require`).
- `SimpleForm` em duas colunas: "Lado e situação" (tela 1) / "Situação" (tela 2) com o fragmento
  novo `ContractPositionStatusFilters` (Situação + Término da entrega até); "Filtros" com o
  fragmento existente `InvoiceReportCommonFilters` (Filial, Produto) + o novo
  `ContractPositionFilters` (Safra, Parceiro, Tipo).
- Value helps existentes: `openHarvestSeasonsValueHelp`, `openBusinessPartnersValueHelp` (parceiro
  de qualquer tipo).
- Enums viajam como números (o Reports não tem `JsonStringEnumConverter`).

## Menu

Migration nova no `CommonContext` (`AddContractPositionReportMenus`) inserindo sob `reports`
`contractPositionReport` (Order 16) e `contractMonthlyPositionReport` (Order 17), `StandaloneOnly =
false`, e `ROLE_MENUS` ADMIN; `Down` remove. Validada com `dotnet ef migrations script` — **sem**
`database update` neste trabalho.

## Tratamento de erros

- Sem 400 de negócio: nenhum filtro é obrigatório. Enum inválido no corpo vira o 400 automático do
  `[ApiController]`.
- Resultado vazio: PDF com cabeçalho, filtros e "Nenhum registro encontrado.".

## Testes

- `ContractPositionDataTests`: teste cruzado com o Application (compra com devolução negativa,
  washout em todos os status, persistido defasado, sobre-entrega; venda com quebra na linha dona,
  linha não dona, item aberto, devolução), filtros nos dois lados, situação padrão e nula, sem prazo
  fora do "Término até", tradução para SQL Server.
- `ContractPositionTextTests`: rótulos, situações, sem prazo, linha de filtros, ordem da seção.
- `ContractPositionReportServiceTests` e `ContractMonthlyPositionReportServiceTests` (InMemory).
- `ContractPositionReportsPdfTests`: PDF real nos dois modos, vazio e valores grandes/negativos.
- `ContractPositionReportsLayoutTests`: geometria dos 2 templates.
- `ReportTemplateHeaderTests`/`ReportTemplateRenderSmokeTests` descobrem os templates sozinhos.
- Testes de ordem só com nomes cuja ordem é a mesma em ordinal e em cultura (`MILHO` < `SOJA`,
  `KG` < `TN`).
- Verificação final pelo caminho do usuário na CEAGUI dev (o item de menu só aparece depois de
  aplicar a migration, o que exige autorização).

## Decisões tomadas no planejamento

1. Fórmula de saldo **reimplementada no Reports em lote** (`ContractPositionData`) e travada por
   teste cruzado contra os métodos estáticos do Application; o Application não muda e o Reports não
   ganha referência proibida.
2. Washout recalculado da tabela `PURCHASE_CONTRACTS_WASHOUTS` (InApproval + Approved), não do
   `WashedOutVolume` persistido — mesma desconfiança do `AllocatedVolume`.
3. Saldo de compra com 2 casas e de venda com 3, exatamente como o domínio.
4. "Sem prazo" = `DeliveryEndDate` antes de 1901; sai em branco no relatório 1, por último na
   ordem, na linha "Sem prazo" do relatório 2, e fica de fora do filtro "Término da entrega até".
5. Situação nula = Rascunho; padrão só Aprovado; as 6 situações oferecidas na tela.
6. Relatório 2: Finalizado/Cancelado/Rejeitado contam no Contratado e no Entregue, mas a entregar = 0.
   O relatório 1 mostra o saldo do domínio para qualquer situação.
7. Relatório 2: linha "Washout (compra)" no cabeçalho do grupo, para a conta fechar.
8. Relatório 2: Vencido sempre presente; meses só com contrato; Sem prazo por último e no acumulado.
9. Relatório 1 com um lado só: saldo geral = saldo daquele lado, positivo, com rótulo próprio.
10. Coluna Tipo com a sigla (FIX/PAF); rótulo inteiro na linha de filtros. Cabeçalho "Entrega até"
    para o término (como no pacote 2). Safra = `HarvestSeasonCode`, sem JOIN.
11. Washout com `HideZeros`: em branco na venda e na compra sem washout.
12. Saldo do relatório 1 com 88 px (medido); o corte provável do pacote 2 fica registrado.
13. Parceiro = `CardCode` nos dois lados; value help de parceiros de qualquer tipo.
14. "Hoje" por sobrecarga `ExecuteAsync(request, today)` em vez de `TimeProvider` (o Reports não o
    registra em DI).
15. Preço com 2 casas (como os relatórios de contrato por produto).
16. Total geral do relatório 1 = só o saldo geral (contratado de compra e de venda não se somam).
17. Controller-base de tela novo (`ContractPositionReportController`) e dois fragmentos novos; o
    `InvoiceReportController` não muda.
