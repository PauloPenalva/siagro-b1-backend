# Conciliação Fiscal de Compras (NF do fornecedor × romaneio) — desenho

Data: 2026-10-13 · Modo: STANDALONE (nada depende de SAP, mas SAPB1 não é verificado) · Repos: backend e frontend

## 1. Objetivo

Na compra — FOB (caminhão descarrega no nosso armazém) e CIF/troca nota (Compra(8) nascida da Expedição pela
liberação do contrato de compra) — a NF de venda do fornecedor entra como Documento de Entrada, mas **nada a liga ao
romaneio** que ela cobre. A linha do Documento de Entrada só referencia o contrato (`PurchaseInvoiceItem.PurchaseContractKey`,
sem efeito).

Esta feature cria o vínculo **linha de NF ↔ romaneio**, com quantidade, feito numa tela de conciliação, **depois** do
fato e **sem travar** nenhum fluxo, para a conferência fiscal e para a entrega do contrato.

Critério de sucesso: para cada romaneio de compra do contrato, a aba Entregas e o relatório mostram quais NFs do
fornecedor o cobrem, quanto (Fiscal), a Diferença contra o peso líquido (Físico) e o Status.

Fora de escopo: o fluxo físico da troca nota (carga frete 0 → Expedição → nossa NF) e o ajuste de descarga 16/17, que
não mudam; qualquer efeito em saldo, alocação, contrato, liberação, título ou estoque fiscal.

## 2. Decisões (com o usuário, 13/10/2026)

| # | Decisão |
|---|---|
| D1 | O vínculo é **só conciliação**, posterior e não bloqueante. Serve à conferência fiscal e à entrega do contrato. |
| D2 | **N:N com quantidade**: uma NF cobre vários caminhões (comum no FOB), um caminhão pode ter várias NFs (complemento). |
| D3 | Romaneios vinculáveis: Compra (8), Dev. Compra (9), Compl. Qtde. (10), Quebra Descarga (16), Sobra Descarga (17) — "NF futura sempre em cima de romaneio". |
| D4 | O vínculo é feito numa **tela de Conciliação** própria (não no Documento de Entrada). |
| D5 | Recorte da tela: **Fornecedor + Produto obrigatórios**; Contrato, Período e Status opcionais. |
| D6 | **Emitente livre com aviso**: produtor com 2+ inscrições é cadastrado como 2+ parceiros (uma IE por `BUSINESS_PARTNERS`). Padrão = mesmo `CardCode` ou mesmo CPF/CNPJ do fornecedor; filtro "Emitente" abre para qualquer um; documento diferente do fornecedor → aviso, grava mesmo assim. |
| D7 | Vínculo grava a **quantidade fiscal, sem teto pelo peso do romaneio** (FOB: NF no peso de origem × nossa balança). Teto só do lado da NF. Status Sem NF / Parcial / Conciliado, com confirmação manual; sem tolerância configurável. |
| D8 | Saída: colunas na **aba Entregas** do contrato de compra **e** relatório PDF novo; Excel pela exportação da tabela da tela. |
| D9 | Abordagem: **tabela de ligação própria** (não conciliar só por contrato, que não diz qual caminhão; não chave no romaneio, que limitaria a uma NF). Precedente: `STORAGE_RETURN_RECONCILIATIONS`. |

## 3. Modelo de dados

### 3.1 `PURCHASE_FISCAL_RECONCILIATIONS` (entidade `PurchaseFiscalReconciliation`, `AppDbContext`)

| Coluna | Tipo | Nota |
|---|---|---|
| `Key` | Guid PK | identity, como `StorageReturnReconciliation` |
| `PurchaseInvoiceItemKey` | Guid NOT NULL | FK `PURCHASE_INVOICES_ITEMS`, `Restrict` |
| `StorageTransactionKey` | Guid NOT NULL | FK `STORAGE_TRANSACTIONS`, `Restrict` |
| `Quantity` | DECIMAL(19,6) NOT NULL | sempre em KG, > 0 |
| `CreatedAt` / `CreatedBy` | | auditoria |

Índice único (`PurchaseInvoiceItemKey`, `StorageTransactionKey`): o mesmo par não se repete. Gravar um par que já
existe **substitui** a quantidade dele (upsert); o diálogo abre o par já com a quantidade atual somada à sugerida.

### 3.2 Confirmação manual no romaneio

`STORAGE_TRANSACTIONS` ganha `FiscalReconciledAt` (DATETIME NULL) e `FiscalReconciledBy` (VARCHAR(100) NULL).

Migration única no `AppDbContext`: `AddPurchaseFiscalReconciliations` (tabela + 2 colunas). Migration de menu no
`CommonDbContext`: `AddPurchaseFiscalReconciliationMenu` (`MENU_ITEMS` + `ROLE_MENUS`), título "Conciliação Fiscal de
Compras".

## 4. Regras do vínculo

Moram num único serviço/guard (`PurchaseFiscalReconciliationGuard`), chamado por toda gravação.

1. **Linha de NF** elegível: `PurchaseInvoice.InvoiceStatus != Cancelled` (Pendente, Confirmada e Devolvida entram);
   **não é contra-nota** (`PurchaseInvoice.ProducerInvoiceKey == null`) — o vínculo vai na escrituração da NF do
   produtor, senão a mesma carga conta duas vezes.
2. **Romaneio** elegível: `TransactionType ∈ {8, 9, 10, 16, 17}` e `TransactionStatus != Cancelled`.
3. **Sentido**: NF `Normal` ↔ 8, 10, 17; NF `Return` ↔ 9, 16. Outro par é recusado.
4. **Produto**: `PurchaseInvoiceItem.ItemCode == StorageTransaction.ItemCode`.
5. **Contrato**: se a linha tem `PurchaseContractKey`, o romaneio precisa ter alocação nesse contrato
   (`PURCHASE_CONTRACTS_ALLOCATIONS`). Sem contrato na linha, não há checagem.
6. **Emitente** (D6): nunca recusa. É "do fornecedor" quando `PurchaseInvoice.CardCode == StorageTransaction.CardCode`
   ou os `TaxId` coincidem (CPF inteiro; CNPJ pela raiz de 8 dígitos). Caso contrário o vínculo é gravado e a tela
   marca a linha com aviso.
7. **Unidade**: a quantidade é KG. Linha de NF em `KG` → direto. Outra UoM → `Quantity × ItemComplement.CommercialFactor`
   quando a UoM da linha é a `CommercialUnitOfMeasureCode` do produto; sem fator ou UoM diferente → recusa
   ("Cadastre o fator da unidade X no Complemento do Produto").
8. **Teto da NF**: Σ `Quantity` dos vínculos da linha ≤ quantidade da linha (em KG). Sem teto do lado do romaneio (D7).
9. **Confirmação derrubada**: criar, alterar ou remover qualquer vínculo de um romaneio zera `FiscalReconciledAt/By`
   dele.

Mensagens de recusa em pt-BR, como `DefaultException` (o controller traduz).

### 4.1 Números e status do romaneio

- **Físico** = `StorageTransaction.NetWeight`.
- **Fiscal** = Σ `Quantity` dos vínculos com linha de NF não cancelada.
- **Diferença** = Fiscal − Físico.
- **Status**: `Sem NF` (nenhum vínculo) · `Conciliado` (Fiscal = Físico, ou `FiscalReconciledAt` preenchido) ·
  `Parcial` (demais).
- **Marcar como conciliado**: exige ≥ 1 vínculo; grava `FiscalReconciledAt/By`. **Desfazer conciliação** limpa.

### 4.2 Limpeza automática (nada bloqueia — D1)

- **NF cancelada ou excluída** → seus vínculos são removidos e os romaneios afetados perdem a confirmação. Ganchos em
  `PurchaseInvoicesCancelService` e `PurchaseInvoicesDeleteService`, ao lado da limpeza de
  `STORAGE_RETURN_RECONCILIATIONS`.
- **Linha de NF removida** (`SyncItems` do Update / ItemsDelete) → idem para a linha.
- **Quantidade da linha reduzida abaixo do vinculado** → recusa ("desvincule antes"). É a única recusa fora da tela,
  e só na edição da NF.
- **Romaneio cancelado** → vínculos removidos e confirmação limpa. Há 6 serviços que cancelam romaneio
  (`StorageTransactionsCancelService`, `ShippingTransactionsReverseService`, `SalesInvoicesReverseConfirmService`,
  `ShipmentLoadsTransshipmentReverseService`, `WarehouseReconciliationsCancelService`,
  `PurchaseDischargeTransactionsSyncService`); cada um chama `PurchaseFiscalReconciliationService.RemoveForTransactionsAsync`.
  Defesa em profundidade: toda consulta ignora vínculo de romaneio cancelado.
- ⚠️ Consequência aceita: a Conferência de Entregas **recria** os 16/17 a cada gravação (spec 2026-09-19), então NF
  vinculada a um 16/17 perde o vínculo quando a conferência é regravada, e precisa ser vinculada de novo.

## 5. Backend

- **Entidade + config EF** (`AppDbContext`), DTOs no Domain.
- **`PurchaseFiscalReconciliationService`** (Application/Services/PurchaseFiscalReconciliations):
  - `ListTransactionsAsync(filtro)` — romaneios do fornecedor/produto (+ contrato, período sobre `TransactionDate`,
    status) com Físico, Fiscal, Diferença, Status, contrato(s), placa e NFs vinculadas.
  - `ListInvoiceLinesAsync(filtro)` — linhas de NF elegíveis do produto, com Quantidade (KG), Vinculada, Disponível,
    flag `IssuerMatchesSupplier`; padrão = emitentes do fornecedor (D6), `IssuerCardCode` opcional substitui; período
    sobre `IssueDate`; só `Disponível > 0` salvo filtro "incluir totalmente vinculadas".
  - `SuggestAsync(linhas, romaneios)` — distribuição sugerida (§6.2), sem gravar.
  - `SaveAsync(pares com quantidade)` — grava atômico; par com quantidade 0 remove o vínculo existente.
  - `RemoveAsync(key)`, `ConfirmAsync(transactionKey)`, `UnconfirmAsync(transactionKey)`.
  - `RemoveForInvoiceAsync`, `RemoveForInvoiceItemAsync`, `RemoveForTransactionsAsync` — limpeza (§4.2).
- **Controller OData** `PurchaseFiscalReconciliations` com functions/actions para as operações acima (rotas declaradas
  à mão quando forem navegação, como de costume).
- Registro manual em `AddApplicationServices()`.

## 6. Frontend — tela "Conciliação Fiscal de Compras"

Rota e view novas (`purchaseFiscalReconciliations/Main`), menu pela migration.

### 6.1 Layout

- **FilterBar**: Fornecedor* (value help de parceiro), Produto* (value help de produto), Contrato (value help filtrado
  por fornecedor + produto), Período (DateRangeSelection, "até" inclusivo), Status (multi), Emitente da NF.
- **Tabela de romaneios**: Código, Data, Tipo, Contrato, Placa, Físico, Fiscal, Diferença, Status (ObjectStatus),
  NFs vinculadas. Seleção múltipla. Botão Exportar (Spreadsheet).
- **Tabela de linhas de NF**: NF/Série, Emitente (⚠ quando `!IssuerMatchesSupplier`), Emissão, Natureza, Contrato,
  Quantidade, Vinculada, Disponível. Seleção múltipla.
- Ação **Vincular** (habilita com ≥ 1 de cada lado) → diálogo (§6.2).
- Detalhe do romaneio (popover/diálogo pela linha): vínculos com **Desvincular**, botões **Marcar como conciliado** /
  **Desfazer conciliação**.

### 6.2 Distribuição sugerida

Linhas de NF por `IssueDate` (empate: número); romaneios por `TransactionDate` (empate: código). Para cada linha de NF,
enquanto houver Disponível: preenche o saldo aberto do romaneio (`max(Físico − Fiscal, 0)`, já descontado o que esta
mesma sugestão atribuiu) e passa ao próximo. Sobrou Disponível depois do último romaneio → soma no último (caso FOB).
Romaneios 9/16 com NF Return pelo mesmo algoritmo; pares de sentido inválido não são sugeridos.

O diálogo lista os pares com quantidade editável (Double, 3 casas); zerar descarta o par. Gravar chama `SaveAsync`;
recusa do guard aparece em MessageBox e nada é gravado.

## 7. Aba Entregas do contrato de compra

`PurchaseContractsGetAllocationsByContractService` (SQL Dapper):

- `CASE` do tipo ganha `WHEN 10 THEN 'Compl.Qtde.'` (Volume positivo) — hoje o 10 nem aparece.
- Colunas novas: `SupplierInvoiceNumbers` (NFs vinculadas, `STRING_AGG` de `TaxDocumentNumber ?? InvoiceNumber`),
  `FiscalQuantity`, `FiscalDifference`, `FiscalStatus`, `IsSplitAcrossContracts`.
- Números do **romaneio inteiro** (vínculo é do romaneio, não da alocação); `Volume` continua o alocado neste contrato.
  Romaneio rateado em mais de um contrato mostra indicativo.
- Fragmento `PurchaseContractAllocations` ganha as colunas e um link "Conciliar" que abre a tela filtrada por
  fornecedor + produto + contrato.

## 8. Relatório "Entregas × NF do Fornecedor"

`SiagroB1.Reports` (que não referencia a Application — consulta própria, com teste cruzado contra
`PurchaseFiscalReconciliationService.ListTransactionsAsync`), `BuildRowsAsync` + `.frx` retrato.

- Filtros = os da tela (Fornecedor, Produto, Contrato, Período, Status).
- Agrupado por contrato: linha do romaneio (Código, Data, Tipo, Placa, Físico, Fiscal, Diferença, Status) e, abaixo,
  as NFs vinculadas (NF/série, emitente, quantidade vinculada). Totais por contrato e geral.
- Seção final "NFs do fornecedor com quantidade não vinculada": linhas de NF do recorte com Disponível > 0.
- Romaneio de mais de um contrato aparece em cada contrato com os números do romaneio inteiro e indicativo; o total
  geral conta cada romaneio uma vez.
- Entrada no menu de relatórios (mesma migration de menu).

## 9. Testes e verificação

- **Guard/serviço** (xUnit + InMemory, contexto próprio): cada regra de §4 (status da NF, contra-nota, tipo, sentido,
  produto, contrato, unidade com e sem fator, teto da NF, emitente diferente grava com flag), confirmação derrubada,
  status calculado, limpeza por NF cancelada/excluída/linha removida/romaneio cancelado (um teste por serviço de
  cancelamento), recusa da redução de quantidade.
- **Distribuição sugerida**: 1 NF × 2 romaneios (FOB, sobra no último), 2 NFs × 1 romaneio, NF Return × 9/16.
- **Aba Entregas**: tipo 10 aparece; colunas fiscais; romaneio rateado.
- **Relatório**: rows + teste cruzado com o serviço.
- **Navegador** (profile `ceagui`, STANDALONE): NF FOB cobrindo 2 caminhões; caminhão com 2 NFs; emitente com outra
  IE (aviso); troca nota com quebra 16 e NF de devolução; marcar/desfazer conciliação; cancelar a NF e ver os
  vínculos sumirem; aba Entregas e PDF batendo com a tela.
