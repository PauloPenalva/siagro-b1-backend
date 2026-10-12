# Relatório "Remessas para Depósito" — desenho

Data: 2026-10-12 · Modo: só STANDALONE · Depende de: `2026-10-10-storage-deposit-remittance-design.md`

## 1. Objetivo

Hoje a conferência de remessa para depósito em armazém de terceiro só existe na seção "Remessa para depósito" do
detalhe da liberação, uma liberação por vez. O relatório consolida todas as remessas de um período — abertas **e já
totalmente retornadas** — com Remetido, Retornado, A retornar e Expedido sem retorno, e os retornos de cada remessa
logo abaixo dela.

Critério de sucesso: para cada remessa, os quatro números do relatório são idênticos aos da seção da liberação.

## 2. Decisões (com o usuário, 12/10/2026)

| # | Decisão |
|---|---|
| D1 | Uma linha de resumo por remessa. (Originalmente com parâmetro **Detalhado** — substituído por D5/D6.) |
| D2 | Filtro **Situação**: Todas (padrão) / Em aberto / Totalmente retornada. |
| D3 | Período sobre a **data da remessa** (`SalesInvoice.InvoiceDate`). Retornado/A retornar são a posição atual, inclusive retornos lançados depois do período. |
| D5 | (ajuste do mesmo dia) Documento interno, NF/série e chave da NF-e na remessa e no retorno; retornos **sempre** abaixo da remessa, com colunas próprias e saldo corrente; Detalhado passa a acrescentar só os romaneios. |
| D6 | (segundo ajuste do mesmo dia) Coluna **CFOP** na remessa e no retorno; romaneios **não aparecem** no relatório (o parâmetro Detalhado sai; o romaneio só entra no Expedido sem retorno); sem zebrado nem sombreamento — a remessa sai em negrito com um fio acima, o retorno em itálico logo abaixo. |
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

O cabeçalho do PDF (`pFilters`) descreve os filtros como os demais relatórios de logística ("Data da remessa", filial,
produto, armazém, situação).

## 5. Saída

### 5.1 Endpoint

`POST /reports/StorageRemittancesByPeriod` → PDF inline `storage-remittances-by-period.pdf`. Fora de STANDALONE
(`ErpMode.IsStandalone`) → 400 "Disponível somente no modo STANDALONE.". Filtro inválido → 400 com a mensagem do
`Validate`, como os demais.

### 5.2 Linhas

O serviço entrega linhas achatadas (`StorageRemittanceRowDto`) com `RowKind` = `Remittance` | `Return`. Romaneios não
viram linha.

- **Agrupamento** por produto + UM da remessa com `InvoiceItemGrouping.BuildGroupResolver` sobre a primeira linha da
  remessa em ordem de `ItemCode`, depois `Key` (o item não tem número de linha; remessa nasce com 1 linha; editada
  para várias, as quantidades somam todas).
- **Ordem**: grupo, data da remessa, número, chave. Dentro da remessa: retornos por data/número. `SortOrder="None"` nos `GroupHeaderBand` (o serviço já ordena).
- **Linha de remessa** (negrito, fio acima): Documento (nº interno DS…), NF/Série ("000000038-1"; vazio sem nota),
  CFOP (distintos das linhas, "5905/6905"), Emissão, Situação (Pendente/Confirmada), NF-e (status), Armazém
  (`ReportText.Partner`), Contrato (código do contrato de compra da liberação), Liberação (data da liberação),
  Remetido, Retornado, A retornar, Expedido sem retorno.
  Segunda linha "Chave NF-e: 3526 1012 …" (44 dígitos em grupos de 4), que encolhe quando não há chave.
- **Linha de retorno** (itálico, logo abaixo da remessa): nas mesmas colunas, documento de entrada (DE…), NF/Série, CFOP,
  emissão, situação, status da NF-e e a chave na segunda linha; "Retorno" na coluna Armazém; Retornado = quantidade do
  retorno; A retornar = saldo depois dele (Remetido − retornos até ele, em ordem de emissão).
- As colunas de quantidade são TEXTO formatado no serviço (no retorno, só Retornado e A retornar). Os **totais** por produto e
  geral somam campos decimais paralelos que valem 0 nas linhas filhas; a contagem de remessas soma
  `RemittanceCount` (1 na remessa, 0 nas filhas).
- Sem remessas no filtro → PDF com "Nenhum registro", como os demais.

### 5.3 Template

`StorageRemittancesByPeriod.frx`, paisagem, uma `DataBand` + `GroupHeaderBand` por produto, mesmo cabeçalho/rodapé dos
relatórios de logística. Remessa e retorno usam as mesmas células; `Highlight` por `Kind` deixa o retorno em itálico e
esconde o fio (`txtSeparator`, borda superior) fora da remessa. A chave (`txtAccessKey`, `CanShrink`) fica na
segunda linha da banda.
Larguras pela regra do pacote 1: `(chars×5,2+4)×tam/7+4` px, Consolas 7pt, totais 6pt; `SingleUomParameter` como
nos demais.

## 6. Frontend e menu

- Migration `AddStorageRemittancesReportMenu` (CommonContext): `MENU_ITEMS` `storageRemittancesReport`, "Remessas
  para Depósito", `ParentKey = reports`, próximo `Order` livre, `StandaloneOnly = true`; `ROLE_MENUS` ADMIN.
- Rota `storageRemittancesReport` no `manifest.json` (o nome é a chave do menu).
- Tela `view/reports/storageRemittances`, no padrão de `view/reports/shipmentReleases`: Período (sem padrão, como as demais telas de relatório),
  Filial, Produto, Armazém (value help de PN marcado como armazém), Situação (Select, padrão Todas); botão gera o PDF pelo mesmo helper dos outros relatórios.

## 7. Testes

- **Tradução**: as consultas de `StorageRemittanceData` geram SQL Server (`ToQueryString`, padrão de
  `ContractPositionDataTests`).
- **Serviço** (InMemory, padrão de `ShipmentReleasesByPeriodReportServiceTests`): remessa cancelada fora; retorno
  cancelado não conta; Compra de conferência fora; origem sem perna de compra usa Saída(7); filtros de período,
  filial, produto, armazém; Situação Open/Returned/All, incluindo remessa totalmente retornada e excesso; retornos
  abaixo da remessa com saldo corrente, CFOP e chave; romaneios nunca viram linha; totais por grupo.
- **Cruzado**: para cada remessa de um cenário com várias liberações, os quatro números batem com
  `ShipmentReleasesStorageRemittanceSummaryService`.
- **Layout/PDF**: larguras medidas (como `LogisticsReportsLayoutTests`) e geração do PDF.
- **Controller**: 400 fora de STANDALONE e com período inválido.
- **Verificação pela tela** na CEAGUI dev: remessas DS000175/DS000176, retorno DE000026, romaneios RM000048–051;
  conferir os números contra a seção da liberação.

## 8. Fora do escopo

- Posição numa data de corte (retornos até a data) e movimentação do período.
- Modo SAPB1 (a remessa só existe em STANDALONE).
- Exportação Excel.
