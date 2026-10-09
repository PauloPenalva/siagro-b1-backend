# Relatórios de logística de venda — pacote 2

Data: 2026-10-08 · Branch: `feature/logistics-reports` (backend e frontend, a partir de `main`
125aa5a / 733937c) · Design aprovado pelo usuário em 08/10/2026.

## Contexto e objetivo

Segundo dos três pacotes de relatórios de compras e vendas (o 1, documentos fiscais, está em
`main`: `docs/superpowers/specs/2026-10-08-fiscal-documents-reports-design.md`). Este pacote cobre
a logística de venda: cargas, liberações (venda e compra) e romaneios de venda, funcionando em
SAPB1 e STANDALONE. Saída **só em PDF**.

Reaproveita a base do pacote 1: serviço `SiagroB1.Reports`, FastReport, `POST /reports/<Nome>`,
serviço com `BuildRowsAsync` (testável) + `ExecuteAsync` (PDF), `.frx` paisagem com
`picLogo`/`pCompanyName`, telas UI5 sobre o controller-base `InvoiceReportController` com filtros em
duas colunas ("Período e situação" | "Filtros") e itens de menu por migration do `CommonContext`.

## Escopo — 4 relatórios

| # | Nome (tela/menu) | Chave de menu = rota | Padrão da rota | Endpoint | Template / fonte de dados |
|---|---|---|---|---|---|
| 1 | Cargas por Período | `shipmentLoadsReport` | `shipment-loads/report` | `POST /reports/ShipmentLoadsByPeriod` | `ShipmentLoadsByPeriod.frx` / `ShipmentLoads` |
| 2 | Liberações de Venda | `salesShipmentReleasesReport` | `sales-shipment-releases/report` | `POST /reports/SalesShipmentReleasesByPeriod` | `SalesShipmentReleasesByPeriod.frx` / `SalesShipmentReleases` |
| 3 | Liberações de Compra | `shipmentReleasesReport` | `shipment-releases/report` | `POST /reports/ShipmentReleasesByPeriod` | `ShipmentReleasesByPeriod.frx` / `ShipmentReleases` |
| 4 | Romaneios de Venda | `salesShipmentsReport` | `sales-shipments/report` | `POST /reports/SalesShipmentsByPeriod` | `SalesShipmentsByPeriod.frx` / `SalesShipments` |

Os quatro são **agrupados por produto + UM** (mesma regra do pacote 1), com subtotal por grupo e
total geral. Fora de escopo: Excel, colunas fiscais, leitura de dados do SAP.

O relatório antigo "Romaneios de Saída" (`storageTransactionsShipmentsReport`) continua existindo;
"Romaneios de Venda" é um relatório novo, com chave própria.

## Arquitetura

Mesmo padrão do pacote 1:

- `Controllers/<Nome>Controller.cs` — `[HttpPost]`, `[FromBody] <Nome>Request`; 400 com mensagem
  pt-BR quando falta período ("Informe o período.") ou o fim é anterior ao início ("A data final
  não pode ser anterior à inicial."). Resposta `application/pdf`, `inline`.
- `Services/<Nome>ReportService.cs` — `(IUnitOfWork db, IFastReportService reportService)`;
  `BuildRowsAsync(request)` devolve DTOs; `ExecuteAsync(request)` gera o PDF com `pFilters` e
  `pSingleUom`. Registrado pelo Scrutor (sufixo `Service`), sem mexer em DI.
- `Dtos/<Nome>Request.cs` (herda de `LogisticsReportRequest`) e `Dtos/<Nome>RowDto.cs`.
- `Reports/Templates/<Nome>.frx` — paisagem A4, cabeçalho da empresa aplicado pelo
  `ReportHeaderService`.

### Regra de modo (SAPB1 / STANDALONE)

- **Nada de JOIN com `ITEMS`, `BUSINESS_PARTNERS`, `WAREHOUSES`** (vazias em SAPB1). Lê apenas
  tabelas locais e snapshots: `SHIPMENT_LOADS`, `SALES_INVOICES` (cliente da carga),
  `SALES_SHIPMENT_RELEASES`, `SHIPMENT_RELEASES`, `SALES_CONTRACTS`, `PURCHASE_CONTRACTS`,
  `LOGISTIC_REGIONS`, `STORAGE_TRANSACTIONS`, `BRANCHS`.
- Nenhuma coluna fiscal ⇒ sem `pStandalone`; os serviços nem recebem `IConfiguration`. O mesmo
  PDF sai nos dois modos (os testes geram nos dois para provar).

### Consultas

- A query EF aplica só filtros traduzíveis (igualdade de escalares, faixa de data, `Contains`
  sobre o **array** de situações). Tudo o que depende de regra (cliente da carga, saldo, rótulos,
  agrupamento) é feito em C# sobre o resultado já limitado pelo período.
- Nada de `Contains` sobre lista grande de chaves: o cliente da carga vem por
  `Include(l => l.Invoices)` (e `Include(t => t.ShipmentLoad).ThenInclude(l => l!.Invoices)` nos
  romaneios), não por uma segunda query com `IN (...)` de chaves.
- Saldos reaproveitam o domínio: `ShipmentLoad.AvailableQuantity`,
  `SalesShipmentRelease.AvailableQuantity`, `ShipmentRelease.AvailableQuantity` (zero quando
  cancelada; sem clamp de negativo — o domínio não clampa).
- Período com fim exclusivo `ToDate.Date.AddDays(1)`.

### Textos comuns

- **`Helpers/ReportText`** (novo, genérico): `Date`, `Period`, `ValidatePeriod`, `Partner`,
  `Product`, `BranchName`, `DocumentNumber`, `NameOrCode`, `JoinDistinct`, `Describe`,
  `JoinFilters`, `IsSingleUnit`, `ProductOfGroup`, `NoProduct`.
- `Helpers/InvoiceReportText` (pacote 1) passa a **delegar** a `ReportText` o que é genérico,
  mantendo a mesma API pública e as mesmas strings — os testes do pacote 1 continuam passando sem
  alteração.
- **`Helpers/LogisticsReportText`** (novo): rótulos pt-BR, situações padrão, linha de filtros,
  cliente da carga, nome da filial.
- Agrupamento: `Helpers/InvoiceItemGrouping.BuildGroupResolver` é reaproveitado como está (já é
  genérico por `Func`): chave `ItemCode` + UM, nome = o mais frequente não vazio.

### Rótulos (iguais aos de `siagro-b1-frontend/webapp/model/formatter.ts`)

| Enum | Rótulos |
|---|---|
| `ShipmentLoadStatus` | Planned "Planejada", Open "Carregada", PartiallyInvoiced "Faturada Parcial", Invoiced "Faturada", Cancelled "Cancelada", Returned "Devolvida", Completed "Concluída", InTransshipment "Em Transbordo", Discharged "Descarregada" |
| `ShipmentLoadType` | Normal "Normal", Removal "Remoção" |
| `ReleaseStatus` | Pending "Pendente", Actived "Ativo", Completed "Finalizado", Cancelled "Cancelado", Paused "Pausado" |
| `ReleaseOrigin` | Standard "Compra", OwnershipTransfer "Transferência", SalesReturn "Devolução", Transshipment "Transbordo" |
| `StorageTransactionsStatus` | Pending "Pendente", Confirmed "Confirmado", Cancelled "Cancelado", Invoiced "Faturado", Returned "Devolvido" |

### Cliente da carga (relatórios 1 e 4)

- Cliente(s) = `CardName` (sem código, para caber) das **notas de saída vivas** da carga
  (`SalesInvoice.ShipmentLoadKey`; viva = `InvoiceStatus` ≠ Cancelado, nulo conta como Pendente),
  distintos, em ordem ordinal, separados por ", ".
- Carga sem nota viva: `CardName` do planejamento (`ShipmentLoad.CardName`, fallback
  `CardCode`) com o sufixo " (planejado)"; sem planejamento, vazio.
- Romaneio sem carga: Cliente vazio.
- Filtro de Cliente (`CardCode`) usa o mesmo critério: casa com o código de alguma nota viva; se a
  carga não tem nota viva, casa com o código do planejamento. Romaneio sem carga nunca casa.
  Aplicado em C#.

### Filtros comuns

| Filtro | Obrigatório | Regra |
|---|---|---|
| Período (de/até) | sim | Cargas: `LoadDate`; liberações: `ReleaseDate`; romaneios: `TransactionDate`. Fim exclusivo. |
| Filial | não | `BranchCode` do documento |
| Produto | não | `ItemCode` da carga / do contrato / do romaneio |
| Situação | não | lista do enum; vazia = todas menos Cancelada/Cancelado. A tela já envia esse padrão marcado. |

Linha de filtros (`pFilters`): `<Rótulo do período>: dd/MM/yyyy a dd/MM/yyyy | Filial: … |
Produto: … | Situação: … | <filtros próprios>`. Situação = "todas" quando todas marcadas,
"todas, exceto Cancelada/Cancelado" no padrão, senão a lista de rótulos em ordem numérica do enum.
Filial descrita pela tabela local `BRANCHS` (ShortName → BranchName → código); demais descrições
saem das linhas do resultado, com o código como fallback.

### Agrupamento e totais

- Grupo = produto + UM (`InvoiceItemGrouping`), ordenado com `StringComparer.CurrentCultureIgnoreCase`
  (como no pacote 1); dentro do grupo, por data e código (ordinal). O `.frx` usa
  `GroupHeaderBand SortOrder="None"` — o serviço já ordena e o FastReport reordenaria.
- Subtotal por grupo de todas as quantidades/pesos (e do frete nas cargas).
- **Total geral de quantidades só quando o resultado inteiro tem uma única UM.** O serviço passa
  `pSingleUom` (`ReportText.IsSingleUnit`); no `.frx` os totais gerais de quantidade se chamam
  `uomSum*` e a nota "UMs diferentes: quantidades só nos subtotais." se chama `uomMixedNote`. O
  `FastReportService` esconde `uomSum*` quando `pSingleUom = false` e `uomMixed*` quando `true`
  (mesmo mecanismo do prefixo `fiscal` do pacote 1, sem script no `.frx`). Totais de dinheiro
  (frete) sempre aparecem. Contagem de registros sempre aparece.

### Layout

- Paisagem, 1084 px úteis. Regra de largura **medida no PDF gerado**: `(caracteres × 5,2 + 4) ×
  corpo/7 + 4` px — 5,2 px por caractere Consolas a 7pt, ~4 px que o GDI+ reserva ao aparar com
  reticências e 4 px de padding. A regra "5,2 + 4" do pacote 1 não basta: em 08/10,
  `99.999.999,999` saiu "99.999.999,9…" em 78 e em 80 px, a data cortou em 56 px e
  `9.999.999,99` em 70 px, enquanto `9.999.999,999` (13 car.) coube em 78.
- Quantidades no dado: até `99.999.999,999` (14 car.) ⇒ 82 px; dinheiro `9.999.999,99` (12) ⇒
  72 px; datas ⇒ 62 px.
- Subtotais e total geral em **Consolas 6pt bold numa linha só**, alinhados às colunas
  (`999.999.999,999` = 15 car. a 6pt ⇒ 74,3 px ≤ 82).
- Campos de texto curtos dimensionados pelo maior valor real: datas (10), códigos de carga
  `CG000051` (8), códigos de romaneio (10), contratos (12), placas (8), NF/série `000123456/001`
  (13), situações (maior rótulo de cada enum). Cabeçalhos nunca cortam ("Transp." nas cargas,
  "Entrega até" nas liberações de venda). Textos longos (nomes) com
  `Trimming="EllipsisCharacter"`; nas cargas, com sete colunas numéricas, transportadora,
  armazém e clientes ficam estreitos e são aparados.
- `LogisticsReportsLayoutTests` mede tudo isso sem renderizar.

## Relatórios

### 1. Cargas por Período

- Filtros próprios: Situação (multi; padrão todas menos Cancelada), Tipo (`ShipmentLoadType`;
  vazio = ambos), Armazém (`WarehouseCode`), Transportadora (`CarrierCardCode`), Placa
  (`TruckCode`), Cliente (regra acima).
- Linha = carga. Colunas: Código · Data · Tipo · Situação · Placa · Transportadora (nome) ·
  Armazém (nome) · Cliente(s) · Total (`TotalQuantity`) · Faturado (`InvoicedQuantity`) ·
  Devolvido (`ReturnedToWarehouseQuantity`) · Transbordado (`TransshippedQuantity`) ·
  Descarregado (`DischargedQuantity`) · Saldo (`AvailableQuantity`) · Frete (`FreightPrice`,
  nulo = 0).
- Subtotal por produto/UM e total geral de todas as quantidades e do frete; contagem de cargas.

### 2. Liberações de Venda

- Filtros próprios: Situação (multi; padrão todas menos Cancelado), Contrato (`SalesContract.Code`,
  texto), Cliente (`SalesContract.CardCode`), Vendedor (`SalesContract.AgentCode`), Região logística
  (`SalesContract.LogisticRegionCode`).
- Linha = liberação. Não há código próprio da liberação: mostra o código do contrato.
- Colunas: Data · Entrega até (`SalesContract.DeliveryEndDate`) · Contrato · Cliente
  `(CardCode) CardName` do contrato · Vendedor (`AgentName`, fallback `AgentCode`) · Local de entrega
  (`DeliveryLocationName`, fallback código) · Região (`LogisticRegion.Name`, fallback código) ·
  Situação · Liberado (`ReleasedQuantity`) · Consumido (`ShippedQuantity`) · Saldo
  (`AvailableQuantity`).
- Produto/UM do grupo vêm do contrato (`ItemCode`/`ItemName`/`UnitOfMeasureCode`).

### 3. Liberações de Compra

- Filtros próprios: Situação (multi; padrão todas menos Cancelado), Contrato
  (`PurchaseContract.Code`, texto), Fornecedor (`PurchaseContract.CardCode`), Armazém de retirada
  (`ShipmentRelease.DeliveryLocationCode`), Origem (`ReleaseOrigin`; vazio = todas).
- Colunas: Data · Contrato · Fornecedor `(CardCode) CardName` · Armazém de retirada · Origem ·
  Situação · Liberado · Retirado (`ShippedQuantity`) · Saldo (`AvailableQuantity`).
- Produto/UM do grupo vêm do contrato de compra.

### 4. Romaneios de Venda

- Fonte: `StorageTransaction` com `TransactionType = SalesShipment (7)`.
- Filtros próprios: Situação (multi; padrão todas menos Cancelado), Armazém (`WarehouseCode`),
  Placa (`TruckCode`), Cliente (regra acima), Vínculo com carga (`HasLoad`: com / sem / ambos).
- Colunas: Data · Código · Carga (`ShipmentLoad.Code`) · Placa · Fornecedor (origem)
  `(CardCode) CardName` do romaneio — o `CardCode` do romaneio de venda é o **fornecedor** da
  perna de compra · Cliente (da carga) · Armazém (nome) · Peso bruto (`GrossWeight`) · Descontos
  (`DryingDiscount + CleaningDiscount + OthersDicount` — o nome do campo tem o typo) · Peso
  líquido (`NetWeight`) · NF fornecedor (`InvoiceNumber/InvoiceSerie`, sem "/" solto) · Situação.

## Frontend

- Quatro telas `webapp/{controller,view}/reports/{shipmentLoads,salesShipmentReleases,shipmentReleases,salesShipments}/Main.*`,
  cada uma estendendo `InvoiceReportController` (sem alterá-lo): a situação padrão vem do
  `defaults()` de cada tela (o spread do `defaults()` sobrescreve o `Statuses` de notas do base).
- `SimpleForm` em duas colunas, como as telas do pacote 1: "Período e situação" (fragmento novo
  `ReportDateRangeFilters` com "Data de/até" + `MultiComboBox` de situação da própria tela) e
  "Filtros" (fragmento existente `InvoiceReportCommonFilters` com Filial e Produto + filtros
  próprios).
- Value helps existentes do `CommonController`: `openWarehouseValueHelp`, `openSuppliersValueHelp`
  (transportadora e fornecedor), `openTrucksValueHelp`, `openCostumersValueHelp`,
  `openAgentsValueHelp`, `openLogisticRegionsValueHelp`.
- **Enums viajam como números**: o Reports não registra `JsonStringEnumConverter`. As chaves dos
  `MultiComboBox`/`Select` são os números em string; `buildPayload` converte com `Number(...)`.
  "Vínculo com carga" viaja como booleano.
- Rotas/targets no `manifest.json`, entradas em `model/ServerRoutes.ts`.

## Menu

Migration nova no `CommonContext` (`AddLogisticsReportMenus`) inserindo sob `reports` os 4 itens
(`Order` 12–15, o último usado é 11; `StandaloneOnly = false`) e `ROLE_MENUS` ADMIN; `Down`
remove. Validada com `dotnet ef migrations script` — **sem** `database update` neste trabalho.

## Tratamento de erros

- 400 com mensagem pt-BR: período ausente ou invertido.
- Resultado vazio: PDF com cabeçalho, filtros e "Nenhum registro encontrado." (não é erro).

## Testes

- `SiagroB1.Application.Tests/Reports/` com EF InMemory, por serviço: período com último dia
  inteiro, situação padrão sem cancelado, cada filtro opcional, formatação das colunas, regra do
  cliente da carga (várias notas, nota cancelada, sem nota, filtro), saldos pelo domínio,
  agrupamento por produto+UM, `pSingleUom` e linha de filtros.
- `ReportTextTests`, `LogisticsReportTextTests`, `FastReportServiceUomTests`.
- `LogisticsReportsPdfTests`: PDF real com o template, nos dois modos, vazio e valores grandes.
- `LogisticsReportsLayoutTests`: geometria dos 4 templates.
- `ReportTemplateHeaderTests`/`ReportTemplateRenderSmokeTests` descobrem os templates novos sozinhos.
- Testes do pacote 1 continuam verdes sem alteração.
- Expectativas de ordem só com nomes cuja ordem é a mesma em ordinal e em cultura (o pacote 1 teve
  um teste que dependia disso).
- Verificação final pelo caminho do usuário nas duas bases dev, acessando as telas pela URL da
  rota (o item de menu só aparece depois de aplicar a migration, o que exige autorização).

## Decisões tomadas no planejamento

1. Rótulos de situação/origem: os do `formatter.ts` (ex.: carga `Open` = "Carregada", liberação
   `Actived` = "Ativo", origem `Standard` = "Compra"), não os provisórios do rascunho do design.
2. "Limite entrega" = `SalesContract.DeliveryEndDate` (a liberação não tem prazo próprio);
   cabeçalho "Entrega até" para caber na coluna.
3. Total geral de quantidade só com UM única (`pSingleUom` + prefixos `uomSum`/`uomMixed`).
4. Cargas também têm filtro de Cliente (a regra do design fala em "nesses relatórios").
5. Liberações de Compra: Situação também multi com padrão sem Cancelado, como as demais.
6. Nas cargas, transportadora, armazém e clientes saem só pelo nome (falta largura para o código);
   nas liberações e romaneios, parceiros com `(código) nome`.
7. `InvoiceItemGrouping` reaproveitado sem renomear; `ReportText` novo com `InvoiceReportText`
   delegando.
8. Testes de PDF e de layout em classes novas (`LogisticsReportsPdfTests`,
   `LogisticsReportsLayoutTests`), com regra de layout por formato/banda em vez de por nome, sem
   mexer nas classes do pacote 1.
9. "Quantidade nula" não existe nas liberações (`decimal` não anulável): o caso de risco coberto é
   liberação cancelada com retirada parcial (saldo 0), sem retirada (saldo = liberado) e retirada
   acima do liberado (saldo negativo, sem clamp).
10. Larguras recalibradas pela medição no PDF (regra acima), com cabeçalho "Transp." nas cargas.
    O mesmo corte afeta os templates do pacote 1 (quantidade em 78 px corta a partir de 14
    caracteres); fica registrado, fora do escopo deste pacote.
