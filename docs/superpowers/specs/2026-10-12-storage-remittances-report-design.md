# Relatório "Remessas para Depósito" — desenho

Data: 2026-10-12 · Modo: só STANDALONE · Depende de: `2026-10-10-storage-deposit-remittance-design.md`

## 1. Objetivo

Hoje a conferência de remessa para depósito em armazém de terceiro só existe na seção "Remessa para depósito" do
detalhe da liberação, uma liberação por vez. O relatório consolida todas as remessas de um período — abertas **e já
totalmente retornadas** — com Remetido, Retornado, A retornar e Expedido sem retorno, e opcionalmente o detalhe de
retornos e romaneios (conciliação retorno ↔ romaneio).

Critério de sucesso: para cada remessa, os quatro números do relatório são idênticos aos da seção da liberação.

## 2. Decisões (com o usuário, 12/10/2026)

| # | Decisão |
|---|---|
| D1 | Uma linha de resumo por remessa; parâmetro **Detalhado** acrescenta retornos e romaneios abaixo dela. |
| D2 | Filtro **Situação**: Todas (padrão) / Em aberto / Totalmente retornada. |
| D3 | Período sobre a **data da remessa** (`SalesInvoice.InvoiceDate`). Retornado/A retornar são a posição atual, inclusive retornos lançados depois do período. |
| D4 | Abordagem A: consulta própria no `SiagroB1.Reports` (que não referencia a Application) + teste cruzado contra o `ShipmentReleasesStorageRemittanceSummaryService`. Não mover `StorageDepositRules`. |

## 3. Regras de dados

Espelham `StorageDepositRules` (Application) — qualquer mudança lá exige mudar aqui; o teste cruzado (§7) acusa.

- **Remessa** = `SalesInvoice` com `ShipmentReleaseKey != null` e `InvoiceStatus != Cancelled` (Pendente e
  Confirmada entram). Cancelada não aparece.
- **Remetido** = soma de `SalesInvoiceItem.Quantity` das linhas da remessa.
- **Retorno** = `PurchaseInvoiceItem` com `SalesInvoiceItemKey` apontando para uma linha da remessa e
  `PurchaseInvoice.InvoiceStatus != Cancelled`. Agrupado por nota: número (`TaxDocumentNumber ?? InvoiceNumber`),
  data (`IssueDate`), quantidade.
- **Retornado** = soma das quantidades dos retornos. **A retornar** = Remetido − Retornado.
- **Romaneios de carregamento** da liberação da remessa, mesmo eixo de `ShippedTransactions`: não cancelados; Saída(7)
  quando `ReleaseOriginRules.ShipsWithoutPurchaseLeg(origin)`, senão Compra(8) exceto `TransactionOrigin =
  WarehouseReconciliation`. Cada um com o retorno reconciliado em `STORAGE_RETURN_RECONCILIATIONS` (ou nenhum).
- **Expedido sem retorno** = soma do `NetWeight` dos romaneios sem reconciliação.
- **Situação**: Em aberto quando A retornar > 0; Totalmente retornada quando A retornar ≤ 0 (excesso de retorno
  conta como retornada). O filtro é aplicado depois do cálculo, em memória.
- `ReleaseOriginRules` está no Domain e é reaproveitado direto; o resto é reescrito no Reports.

## 4. Filtros

Request `StorageRemittancesByPeriodRequest : LogisticsReportRequest`:

| Campo | Regra |
|---|---|
| `FromDate`/`ToDate` | obrigatórios; `InvoiceDate` da remessa, fim inclusivo (validação de `LogisticsReportText.Validate`). |
| `BranchCode` | filial da remessa. |
| `ItemCode` | produto de alguma linha da remessa. |
| `CardCode` | armazém = PN da remessa (`SalesInvoice.CardCode`). |
| `ReturnSituation` | enum novo no Reports: `All` (padrão) / `Open` / `Returned`. |
| `Detailed` | bool, padrão `false`. |

O cabeçalho do PDF (`pFilters`) descreve os filtros como os demais relatórios de logística ("Data da remessa", filial,
produto, armazém, situação, "Detalhado").

## 5. Saída

### 5.1 Endpoint

`POST /reports/StorageRemittancesByPeriod` → PDF inline `storage-remittances-by-period.pdf`. Fora de STANDALONE
(`ErpMode.IsStandalone`) → 400 "Disponível somente no modo STANDALONE.". Filtro inválido → 400 com a mensagem do
`Validate`, como os demais.

### 5.2 Linhas

O serviço entrega linhas achatadas (`StorageRemittanceRowDto`) com `RowKind` = `Remittance` | `Return` |
`Transaction`. Sem Detalhado, só `Remittance`.

- **Agrupamento** por produto + UM da remessa com `InvoiceItemGrouping.BuildGroupResolver` sobre a primeira linha da
  remessa em ordem de `ItemCode`, depois `Key` (o item não tem número de linha; remessa nasce com 1 linha; editada
  para várias, as quantidades somam todas).
- **Ordem**: grupo, data da remessa, número, chave. Dentro da remessa: retornos por data/número, depois romaneios por
  data/código. `SortOrder="None"` nos `GroupHeaderBand` (o serviço já ordena).
- **Linha de remessa**: Remessa (número), Emissão, Situação (Pendente/Confirmada), NF-e (status), Armazém
  (`ReportText.Partner`), Contrato (código do contrato de compra da liberação), Liberação (data da liberação),
  Remetido, Retornado, A retornar, Expedido sem retorno.
- **Linha de retorno** (Detalhado): uma célula de texto `Detail` — "Retorno 000000456 de 11/10/2026 - Confirmado -
  150.000,000 KG".
- **Linha de romaneio** (Detalhado): `Detail` — "Romaneio RM000048 de 10/10/2026 - placa ABC1D23 - 30.000,000 KG -
  retorno 000000456" ou "... - sem retorno".
- As colunas de quantidade são TEXTO formatado no serviço (vazio nas linhas filhas). Os **totais** por produto e
  geral somam campos decimais paralelos que valem 0 nas linhas filhas; a contagem de remessas soma
  `RemittanceCount` (1 na remessa, 0 nas filhas).
- Sem remessas no filtro → PDF com "Nenhum registro", como os demais.

### 5.3 Template

`StorageRemittancesByPeriod.frx`, paisagem, uma `DataBand` + `GroupHeaderBand` por produto, mesmo cabeçalho/rodapé dos
relatórios de logística. Linhas de detalhe impressas na mesma banda: a célula `txtDetail` (recuada, até o início das
quantidades) só tem texto nas linhas filhas, e as demais células só têm texto na linha de remessa — sem visibilidade
condicional.
Larguras pela regra do pacote 1: `(chars×5,2+4)×tam/7+4` px, Consolas 7pt, totais 6pt; `SingleUomParameter` como
nos demais.

## 6. Frontend e menu

- Migration `AddStorageRemittancesReportMenu` (CommonContext): `MENU_ITEMS` `storageRemittancesReport`, "Remessas
  para Depósito", `ParentKey = reports`, próximo `Order` livre, `StandaloneOnly = true`; `ROLE_MENUS` ADMIN.
- Rota `storageRemittancesReport` no `manifest.json` (o nome é a chave do menu).
- Tela `view/reports/storageRemittances`, no padrão de `view/reports/shipmentReleases`: Período (sem padrão, como as demais telas de relatório),
  Filial, Produto, Armazém (value help de PN marcado como armazém), Situação (Select, padrão Todas), Detalhado
  (CheckBox); botão gera o PDF pelo mesmo helper dos outros relatórios.

## 7. Testes

- **Tradução**: as consultas de `StorageRemittanceData` geram SQL Server (`ToQueryString`, padrão de
  `ContractPositionDataTests`).
- **Serviço** (InMemory, padrão de `ShipmentReleasesByPeriodReportServiceTests`): remessa cancelada fora; retorno
  cancelado não conta; Compra de conferência fora; origem sem perna de compra usa Saída(7); filtros de período,
  filial, produto, armazém; Situação Open/Returned/All, incluindo remessa totalmente retornada e excesso; Detalhado
  gera as linhas filhas na ordem; totais por grupo.
- **Cruzado**: para cada remessa de um cenário com várias liberações, os quatro números batem com
  `ShipmentReleasesStorageRemittanceSummaryService`.
- **Layout/PDF**: larguras medidas (como `LogisticsReportsLayoutTests`) e geração do PDF com e sem Detalhado.
- **Controller**: 400 fora de STANDALONE e com período inválido.
- **Verificação pela tela** na CEAGUI dev: remessas DS000175/DS000176, retorno DE000026, romaneios RM000048–051;
  conferir os números contra a seção da liberação.

## 8. Fora do escopo

- Posição numa data de corte (retornos até a data) e movimentação do período.
- Modo SAPB1 (a remessa só existe em STANDALONE).
- Exportação Excel.
