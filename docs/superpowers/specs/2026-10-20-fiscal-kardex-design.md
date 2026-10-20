# Kardex Fiscal — design

Data: 2026-10-20 · Modo: só STANDALONE · Repos: backend + frontend · Branch: `feature/fiscal-kardex` (a partir de `feature/fiscal-conference`, para reaproveitar o filtro "escriturável" e o padrão da tela)

## Por quê

2ª das 3 entregas do lugar do "livro fiscal" (1ª: Conferência Fiscal; 3ª: Inventário em Data / H010).
O usuário quer acompanhar, por produto, o estoque fiscal em quantidade **e valor**, e que o saldo do
Kardex numa data seja o mesmo número do inventário daquela data. O Siagro não calcula custo hoje.

## Decisões do usuário

- **Custo FIFO (PEPS), sem o ICMS creditado.** (18–20/10/2026)
- **Saídas valorizadas a custo FIFO** calculado pelo sistema, não pelo preço da nota; o valor da nota
  aparece na linha só para conferência. (20/10)
- **Saldo inicial por lançamento manual valorizado** (quantidade + valor). (20/10)
- **Cada tela chamável direto do menu** (menu Fiscal).

## O que é

Duas telas no menu **Fiscal** (StandaloneOnly, ROLE_MENUS ADMIN):

1. **Saldo Inicial do Estoque** — cadastro (lista + incluir/editar/excluir) de lançamentos:
   filial, produto, data do saldo, quantidade, valor total (custo unitário = valor ÷ quantidade, exibido).
   Um lançamento por filial + produto (chave única). O Kardex daquele par só considera movimentos
   **posteriores** à data do saldo (data do saldo inclusa no saldo, movimentos do dia seguinte em diante).
   Sem lançamento, o Kardex começa de zero a partir do primeiro movimento.
2. **Kardex Fiscal** — consulta. Filtros: filial (obrigatória), produto (opcional), período de/até
   (obrigatórios). Para cada produto com saldo ou movimento: linha de **saldo anterior** (na data
   "de"), uma linha por movimento e **saldo final**. Colunas por movimento: data, entrada/saída,
   documento (nº interno, número/série fiscal), parceiro, CFOP, natureza, quantidade entrada,
   quantidade saída, custo unitário, valor entrada, valor saída, saldo quantidade, saldo valor,
   valor da nota (conferência). Grade + Excel, mesmo padrão da Conferência Fiscal.
   Aviso visual em linhas com **saldo negativo**.

## Regras de movimento

- Movimentam: linhas de Documento de Entrada e de Saída com `MovesFiscalInventory = true`, mesmo
  filtro de "escriturável" da Conferência Fiscal (Confirmado/Retornado; NF-e não Cancelada/Denegada/
  Inutilizada; NF-e própria só Autorizada; saída sem documento fiscal fica fora; NFS-e e CT-e fora).
- **Direção pelo CFOP** (1/2/3 entra, 5/6/7 sai), como na Conferência; CFOP vazio usa a direção do
  documento.
- Data: entrada = `PostingDate ?? IssueDate`; saída = `InvoiceDate`. Ordem no dia: entradas antes
  de saídas, depois nº interno.
- Estoque por **filial + produto** (as linhas não guardam armazém). Quantidade na unidade gravada na
  linha.
- **Custo de entrada** (compra): `GrandTotal − IcmsValue` (valor do item + frete + seguro + outras
  − desconto, menos o ICMS creditado). PIS/COFINS permanecem no custo.
- **Saída** (venda, remessa etc.): consome camadas FIFO; custo = soma das camadas consumidas.
- **Devolução de compra** (saída com origem em linha de entrada): sai pelo custo unitário da camada
  da entrada de origem quando ela ainda tem saldo; senão FIFO normal.
- **Devolução de venda** (entrada com origem em linha de saída): entra pelo custo unitário com que a
  saída de origem foi baixada.
- **Complemento de valor** de entrada (sem quantidade): soma `GrandTotal − IcmsValue` à camada da
  entrada de origem; se a camada já foi consumida, soma ao saldo (camada mais antiga em aberto).
  Complemento de ICMS: não movimenta (só imposto).
- **Saldo negativo**: a saída sem camada suficiente baixa o que houver e o restante sai a custo zero,
  com a linha marcada como "saldo insuficiente". O saldo fica negativo em quantidade.

## Motor compartilhado

O cálculo FIFO fica num serviço único (`FiscalInventoryLedger` ou similar) que devolve, para
filial + produto(s) + data final, os movimentos valorizados e as camadas em aberto. O Kardex usa os
movimentos; o Inventário em Data (próxima entrega) usará as camadas/saldo — por isso o número bate.
Calculado na hora, sem tabela de custo gravada.

## Fora do escopo

Inventário em Data / H010 (próxima entrega), custo médio, armazém, conversão de unidade,
custo gravado/fechamento de período, integração contábil, modo SAPB1.

## Como (decisões do Claude)

- Saldo inicial: entidade nova em `AppDbContext` (tabela `FISCAL_INVENTORY_OPENING_BALANCES`),
  migration por empresa; CRUD via OData no `SiagroB1.Web` (serviços registrados à mão).
- Kardex: serviço + `POST /reports/FiscalKardex` (JSON) em `SiagroB1.Reports`, recusa SAPB1, como
  a Conferência Fiscal.
- Menu: migration em `CommonContext`, keys `fiscalInventoryOpeningBalances` e `fiscalKardex`
  (= nomes das rotas), filhos de `fiscal`, Order 6 e 7 (a Conferência Fiscal usa 5).

## Detalhes resolvidos (decisões do Claude, revisáveis)

1. **Quantidade** = `Quantity` gravada na linha da nota (estoque fiscal segue o documento; quebra
   apurada é física e não entra). Vale para entrada e saída.
2. **ICMS creditado** = `IcmsValue` gravado (em diferimento ele já vem zero). Funrural e retenções
   não são custo. IPI/ICMS-ST não existem no modelo.
3. **Vínculos de origem**: devolução de compra → `PurchaseInvoiceItem.PurchaseInvoiceItemOriginKey`;
   devolução de venda → linha de entrada com `SalesInvoiceItemKey` / `SalesInvoiceItem.SalesInvoiceItemOriginKey`
   (o builder confirma qual campo a devolução de venda grava); complemento de valor →
   `InvoiceType == ValueComplement` + `ComplementedItemKey`, valor = `GrandTotal − IcmsValue`
   (inclui `ComplementAmount`). Sem vínculo (ou origem antes do saldo inicial) → FIFO normal.
4. **Saldo inicial** = camada mais antiga, na data do lançamento. Movimentos até essa data (inclusive)
   são ignorados para aquele filial + produto. Se o "de" for anterior à data do saldo, o saldo
   anterior é zero e o saldo inicial aparece como primeira linha na sua data. Editável a qualquer
   momento (tudo é recalculado na hora; sem trava). Quantidade > 0 e valor ≥ 0 obrigatórios.
5. **Saldo negativo**: a saída sem camada baixa o que houver; o restante sai a custo zero e a linha é
   marcada. A próxima entrada primeiro cobre o déficit: a parte que cobre sai numa linha automática
   "Acerto de saldo negativo" pelo custo dessa entrada. Assim saldo em quantidade e valor sempre
   = soma das camadas, e o inventário lê só as camadas.
6. **Devolução de compra**: sai pelo custo unitário da camada de origem e baixa dessa camada até o
   saldo dela; excedente → FIFO normal. **Devolução de venda**: entra como camada nova pelo custo
   unitário com que a saída de origem foi baixada (zero, se ela saiu a zero).
7. Linha sem `ItemCode` não entra. Unidades diferentes para o mesmo produto: não converte, mas a
   linha leva aviso "Unidade diferente".
8. Período "de/até" inclusivo em dias inteiros. Cálculo refeito do saldo inicial (ou do primeiro
   movimento) a cada consulta — volume da CEAGUI é pequeno.
9. Filtro "escriturável": extrair o da Conferência Fiscal para um ponto único reutilizado pelos dois.
10. Direção pelo CFOP também nas devoluções (devolução de venda com CFOP 1.2xx é entrada).
