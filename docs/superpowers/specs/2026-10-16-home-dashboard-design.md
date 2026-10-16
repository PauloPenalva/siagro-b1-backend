# Home como dashboard operacional — design

Data: 2026-10-16
Modo: STANDALONE apenas (decisão do usuário). Em SAPB1 a home continua como está (vazia).
Repositórios: `siagro-b1-backend` (endpoint + serviço + testes) e `siagro-b1-frontend` (view da home).

## 1. Intenção

A home (`siagrob1.view.Main`, rota `""`) hoje é uma view vazia. Ela passa a responder, assim que
abre e sem nenhum filtro, a pergunta "o que eu preciso fazer hoje?" para quem opera a comercial
de grãos: dinheiro a pagar e a receber hoje, contratos com entrega vencida, cargas em andamento,
carregamentos dos últimos dias, saldo físico por produto, posição comprado x vendido e as filas
de contratos de venda a entregar e de compra a receber.

Nas palavras do usuário: "carregamentos de ontem, anteontem, etc.; saldo de cada produto
disponível; contratos de venda que eu tenho que entregar ordenados por prazo de recebimento e
prazo de entrega; o que tenho que pagar hoje; o que tenho que receber em dinheiro hoje; contratos
de compra que tenho que receber em mercadoria; contratos de compra e de venda que já venceram o
prazo de entrega e ainda não foram entregues; comprado x vendido (semelhante ao relatório)".

## 2. Goals, non-goals, anti-goals

**Goals**

- Uma página, sem filtros, carregada numa única chamada ao backend.
- Números batendo com as telas e relatórios existentes (mesmas fórmulas, mesmos universos).
- Cada cartão/linha leva o usuário à tela onde ele age.

**Non-goals (v1)**

- Modo SAPB1 (estoque e títulos moram no SAP). O endpoint responde 404 e a home fica vazia.
- Filtros na home (período, produto, filial). Tudo é "hoje", todos os produtos, todas as filiais.
- Abrir listas já filtradas a partir de um cartão: as rotas de lista não aceitam query hoje e
  estender isso é outro trabalho. Cartões abrem a lista sem filtro; linhas abrem o detalhe.
- Gráficos. Barras/linhas podem vir depois; v1 é tabela e número.
- Personalização por perfil (esconder seções).

**Anti-goals (seria fracasso mesmo funcionando)**

- Número na home diferente do número da tela de origem (ex.: "a pagar hoje" ≠ soma da lista de
  Contas a Pagar filtrada por vencimento de hoje).
- Home lenta: a chamada única precisa responder em poucos segundos com a base da CEAGUI
  (milhares de contratos/romaneios/títulos). Nada de N+1 por produto ou por armazém.
- Estourar memória no `SiagroB1.Reports` da VPS (ver memória `ceagui-vps-memory-starvation`):
  só consultas agregadas no SQL, sem materializar coleções inteiras.

## 3. Layout (opção A, escolhida pelo usuário entre três mockups)

```
┌──────────────┬──────────────┬────────────────────┬────────────────────┬──────────────────┐
│ A pagar hoje │ A receber    │ Compras c/ entrega │ Vendas c/ entrega  │ Cargas em        │
│ R$ … · n     │ hoje R$ … · n│ vencida  n · qtd   │ vencida  n · qtd   │ andamento  n     │
│ vencidos: …  │ vencidos: …  │                    │                    │ planej/carreg/…  │
├──────────────┴──────────────┴──────┬─────────────┴────────────────────┴──────────────────┤
│ ESTOQUE FÍSICO POR PRODUTO         │ CARGAS DOS ÚLTIMOS 7 DIAS                           │
│ produto · UM · físico · compra a   │ dia · cargas · quantidade                           │
│ receber · venda a entregar·líquido │                                                     │
│                                    │ VENDAS A ENTREGAR (recebimento ↑, entrega ↑)        │
│ COMPRADO x VENDIDO POR MÊS         │ cliente · produto/safra · saldo · recebe · entrega  │
│ (mesmas linhas do relatório de     │                                                     │
│ posição mensal)                    │ COMPRAS A RECEBER (pagamento ↑, entrega ↑)          │
│                                    │ fornecedor · produto/safra · saldo · paga · entrega │
└────────────────────────────────────┴─────────────────────────────────────────────────────┘
```

Em telas estreitas as duas colunas empilham (esquerda, depois direita). Os cinco cartões quebram
em duas linhas.

## 4. Definições dos indicadores

"Hoje" é a data do servidor (`DateTime.Today`), como nos relatórios de posição.

### 4.1 Cartões

| Cartão | Universo | Número grande | Linha menor |
|---|---|---|---|
| A pagar hoje | `FinancialDocuments` com `Direction = Payable`, `Status ∈ {Open, PartiallySettled}`, `Nature ≠ Advance` (mesmo universo da tela Contas a Pagar), `Currency = Brl` | Σ (`NetAmount − SettledAmount`) dos títulos com `DueDate.Date = hoje`, e a contagem | Σ e contagem dos com `DueDate.Date < hoje` ("vencidos") |
| A receber hoje | idem com `Direction = Receivable` (universo da tela Contas a Receber) | idem | idem |
| Compras com entrega vencida | posições de compra (`ContractPositionData.PurchasePositionsAsync`, status padrão = Aprovado) com `HasDeadline(DeliveryEndDate)` e `DeliveryEndDate.Date < hoje` e `ToDeliver > 0` | contagem de contratos | Σ `ToDeliver` por UM (uma linha por UM; normalmente só KG) |
| Vendas com entrega vencida | idem para venda | idem | idem |
| Cargas em andamento | `ShipmentLoads` com `Status ∈ {Planned, Open, PartiallyInvoiced, InTransshipment, Invoiced, RefusalPending}` | contagem total | contagem por grupo: planejadas (Planned), em carregamento (Open + PartiallyInvoiced), em transbordo (InTransshipment), em trânsito (Invoiced), recusa pendente (RefusalPending) |

**Valor somado = saldo em aberto (`NetAmount − SettledAmount`) de TODO título do universo, inclusive
o provisório ainda sem nota** (decisão do usuário, 2026-10-16: o cartão mostra o compromisso do
contrato que vence hoje, não só o que o caixa já pode liberar). Consequência aceita: um firme
ainda não composto e o provisório que vai recebê-lo contam os dois enquanto a composição não
acontece; e "vencidos" inclui provisórios cuja nota nunca chegou. Não usar
`AvailableToSettleAmount`.

Títulos em outra moeda entram na contagem mas não na soma (decisão minha; a CEAGUI só usa BRL).
A tela de Contas a Pagar não filtra status por padrão (só direção e `Nature ≠ Advance`); o
dashboard restringe a `Open`/`PartiallySettled` porque título cancelado ou liquidado não é
dinheiro a pagar. Para conferir o número, filtre a lista por status aberto e vencimento.
`Discharged`, `Completed`, `Returned` e `Cancelled` não são "em andamento" (decisão minha:
descarregada já chegou; o que falta é conferência, que é outra fila). `RefusalPending` (recusa
registrada, aguardando NF-e de retorno) é ativa e bloqueante: entra, com contagem própria.

### 4.2 Estoque físico por produto

Uma linha por produto + UM (`ItemCode`, `ItemName`, `UnitOfMeasureCode` normalizada com
`Trim().ToUpperInvariant()`).

- **Físico**: Σ por `ItemCode` da mesma fórmula de `StorageTransactionsWarehouseBalanceService`
  (status `Confirmed`/`Invoiced`, os sete tipos de transação, sinal por tipo), somando TODOS os
  armazéns (próprios e de terceiros: é tudo grão da empresa). Implementar como UMA consulta
  agrupada por item, não uma chamada do serviço por armazém.
- **Compra a receber**: Σ `ToDeliver` das posições de compra do item (todas as safras).
- **Venda a entregar**: Σ `ToDeliver` das posições de venda do item.
- **Em carga** (decisão minha, apontada pelo revisor): Σ (`TotalQuantity − InvoicedQuantity`) das
  cargas `Open`/`PartiallyInvoiced` do item. Esse grão já saiu do físico (o romaneio de embarque
  foi confirmado) mas a alocação na venda só nasce no faturamento, então "venda a entregar" ainda
  o conta. Sem esta coluna o líquido subtrairia o mesmo volume duas vezes enquanto a carga está na
  estrada.
- **Líquido**: físico + em carga + compra a receber − venda a entregar. Negativo em vermelho.

Janela inversa na compra (alocação nasce ao criar o romaneio, físico só ao confirmar) é
transitória, normalmente do mesmo dia: aceita como está.

Casamento físico × contratos é por item + UM. Contrato em UM diferente do estoque (ex.: saca)
vira linha própria com físico vazio. Produto com físico zero e sem contrato aberto não aparece.

### 4.3 Comprado x vendido por mês

Exatamente as linhas de `ContractMonthlyPositionReportService.BuildRowsAsync` com request vazio
(todos os produtos/safras, status Aprovado): grupo produto − safra − UM, linhas Vencido, mm/aaaa…,
Sem prazo, com compra, venda, líquido e acumulado. O cabeçalho do grupo mostra contratado e
entregue de cada lado. É o mesmo relatório na tela; se o usuário quiser o PDF, o botão abre a
tela de relatórios.

### 4.4 Cargas dos últimos 7 dias

Sete linhas, de hoje para trás (hoje, ontem, anteontem, depois a data), por `LoadDate.Date`,
`Status ∉ {Planned, Cancelled}`: contagem e Σ `TotalQuantity` por UM. Dia sem carga aparece com
zero. Planejada fica de fora porque ainda não tem romaneio, logo nada foi carregado (decisão
minha: o usuário pediu "carregamentos", não "cargas abertas"). `LoadDate` é a data de
planejamento, editável; uma carga datada de ontem e carregada hoje sai em ontem. Aceito.

### 4.5 Vendas a entregar / Compras a receber

Posições (status Aprovado) com `ToDeliver > 0`, ordenadas por `StandardCashFlowDate` crescente
(nulos por último) e depois `DeliveryEndDate` crescente (sem prazo por último). Dez primeiras;
rodapé "n contratos no total" e link para a lista completa. Colunas: parceiro, produto/safra,
saldo + UM, data de recebimento (venda) ou pagamento (compra), fim da entrega. Fim da entrega
anterior a hoje sai em vermelho. Clicar abre o detalhe do contrato.

## 5. Backend

### 5.1 Onde

No projeto **`SiagroB1.Reports`**, não no Web. Razões: a matéria-prima (`ContractPositionData`,
`ContractPosition`, `ContractPositionText`, `ContractMonthlyPositionReportService`) mora lá e
Reports não referencia Application, então mover custaria um refactor que não paga; Reports já é
o host das consultas agregadas somente-leitura e já tem `AppDbContext` e a chave `Erp`; o Gateway
já proxia `/reports/**` com a política de autenticação.

- `GET /reports/Dashboard` → `DashboardDto` em JSON (camelCase, como todo REST daqui — ver memória
  `odata-api-serializes-camelcase`).
- Em modo SAPB1 (`Erp != STANDALONE`) responde **404**. O frontend trata 404 como "sem dashboard".
- `DashboardService` (em `Services/`) com `BuildAsync(DateTime today)`; o controller passa
  `DateTime.Today`. Testes fixam a data, no padrão de `ContractMonthlyPositionReportServiceTests`.
- Cada seção é um método privado/estático com sua consulta; o `BuildAsync` chama os cinco em
  sequência (mesmo `DbContext`, sem paralelismo de EF).

### 5.2 Contrato (`Dtos/DashboardDto.cs`)

```
DashboardDto
  generatedAt: DateTime
  financial:   { payablesDueToday, receivablesDueToday, payablesOverdue, receivablesOverdue }  cada um { amount: decimal, count: int }
  overdueContracts: { purchase: OverdueSide, sales: OverdueSide }   OverdueSide { count: int, quantities: [{ unitOfMeasure, quantity }] }
  loadsInProgress: { total, planned, loading, inTransshipment, inTransit }
  recentLoads: [ { date, count, quantities: [{ unitOfMeasure, quantity }] } ]   (7 itens, hoje primeiro)
  stock: [ { itemCode, itemName, unitOfMeasure, physical: decimal?, purchaseToDeliver, salesToDeliver, net: decimal? } ]
  monthlyPosition: [ ContractMonthlyPositionRowDto ]   (reaproveitado como está)
  salesToDeliver:   { total: int, items: [ ContractQueueItemDto ] }
  purchasesToReceive: { total: int, items: [ ContractQueueItemDto ] }
ContractQueueItemDto { key, code, cardCode, cardName, itemCode, itemName, harvestSeasonCode, toDeliver, unitOfMeasure, cashFlowDate: DateTime?, deliveryEndDate: DateTime?, isOverdue }
```

`deliveryEndDate` vai nulo quando `!HasDeadline` (o sentinela `DateTime.MinValue` não sai da API).

### 5.3 Testes (xUnit + EF InMemory, `SiagroB1.Application.Tests/Reports/DashboardServiceTests.cs`)

Reaproveitar `ContractPositionSeed`. Cobrir no mínimo: financeiro (hoje × vencido × status
Settled fora × Advance fora × moeda); vencidos (ToDeliver = 0 não conta; Finalizado não conta);
cargas (status por grupo; Cancelled fora dos 7 dias; dia vazio = zero; ordem hoje→trás);
estoque (sinal por tipo; status Draft fora; item+UM; linha sem físico para UM divergente);
filas (ordem recebimento→entrega, nulos por último, top 10 + total; isOverdue).
Atenção à memória `ef-inmemory-hides-untranslatable-linq`: as consultas agrupadas devem ser
validadas também contra SQL Server ao subir a stack.

## 6. Frontend

- `ServerRoutes.dashboard = '/reports/Dashboard'`; `RequestModel.get` no `onRouteMatched` da rota
  `main` — toda volta à home recarrega. Botão "Atualizar" no cabeçalho com hora da última carga.
- `Main.view.xml` reescrita com `sap.m` apenas (as libs do manifest são `sap.m` e `sap.ui.core`):
  cartões com `GenericTile` + `TileContent` + `NumericContent` (ou `Panel` estilizado, a critério
  do builder — o que importa é número grande + rótulo + linha menor), seções em `Panel`, listas
  em `sap.m.Table`, layout em `sap.ui.layout.Grid` ou `FlexBox` responsivo. Estado de carga com
  `busy`; 404 → página vazia (comportamento atual); outro erro → `MessageBox` padrão do app.
- Modelo JSON `dashboard` na view. Formatação: moeda pt-BR, quantidade com separador de milhar e
  a UM do próprio dado (nada de converter KG→t no v1).
- Navegação: cartões financeiros → rotas das listas de Contas a Pagar / a Receber; cartão de
  cargas → lista de cargas; cartões de vencidos e rodapés das filas → listas de contratos de
  compra / de venda; linha da fila → detalhe do contrato; linha de carga do dia → lista de cargas.
  (Nomes das rotas: o builder pega do `manifest.json`.)
- Textos em pt-BR seguindo o padrão das demais views do app (i18n se a view vizinha usa i18n;
  literal se usa literal). Identificadores em inglês.
- Em modo SAPB1 (`SessionService.isSapMode()`) o controller nem chama o endpoint.
- Verificação: `yarn ts-typecheck` e `yarn lint`; checagem visual na stack local (memória
  `run-stack-locally`) com a base da CEAGUI dev.

## 7. Decisões já tomadas (minhas, para o usuário conferir)

1. Endpoint no Reports, não no Web (§5.1).
2. Todas as filiais, sem filtro de filial (as listas do app também não filtram por filial).
3. "Em andamento" inclui `Invoiced` como "em trânsito" e exclui `Discharged`.
4. Estoque soma armazéns próprios e de terceiros.
5. Filas limitadas a 10 com contador total.
6. Sem gráfico no v1; cargas por dia é tabela.
7. Cartões abrem listas sem filtro; só linhas abrem detalhe.
8. Moeda: só BRL na soma.
9. `RefusalPending` conta como "em andamento", em grupo próprio (§4.1).
10. Planejada fica fora de "cargas dos últimos 7 dias" (§4.4).
11. Coluna "em carga" no estoque para não descontar duas vezes o grão em trânsito (§4.2).
12. Textos literais em pt-BR nas views (nenhuma view do app usa i18n).

## 8. Deixado ao builder

- Controle exato dos cartões (`GenericTile` × `Panel`) e classes de espaçamento.
- Como agrupar por item+UM em LINQ traduzível (uma consulta por seção, agregação no SQL).
- Nome do i18n/labels, ícones.
- Migration: não há (sem tabela nova, sem menu: a home é a rota raiz, visível para todo logado).
- Commits: um em cada repo, `feat(reports): …` no backend e `feat(platform): …` no frontend, em
  `feature/home-dashboard`.
