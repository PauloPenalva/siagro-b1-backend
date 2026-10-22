# Menu "Estoque" + Registro Kardex Modelo 3 (livro P3) — design

Data: 2026-10-22 · Repos: `siagro-b1-backend` + `siagro-b1-frontend` · Só STANDALONE.

## Intenção

As telas de estoque fiscal nasceram dentro do menu **Fiscal**. O usuário quer um módulo **Estoque**
próprio, com nomes de livro fiscal, e o Kardex precisa sair como **livro fiscal oficial** — o
Registro de Controle da Produção e do Estoque (modelo 3), impresso e encadernado, com termos de
abertura/encerramento e folhas numeradas, como o livro P7 que já existe.

## 1. Menu

Novo menu superior `stock` ("Estoque", ícone `sap-icon://folder-blank` — regra da casa para
módulo novo, ver memória top-menu-icon-folder-blank), `ParentKey = null`, `StandaloneOnly = true`,
ordem logo depois de "Fiscal" (ver `Order` dos menus de topo na migration). Itens movidos para ele
(mesma Key/rota, só muda `ParentKey`, `Title` e `Order`):

| Order | Key | Título novo | Antes |
|---|---|---|---|
| 1 | `fiscalInventoryOpeningBalances` | Saldo Inicial do Estoque | (igual) |
| 2 | `fiscalKardex` | Registro Kardex | Kardex Fiscal |
| 3 | `fiscalStockAdjustments` | Ajuste de Estoque | Ajustes de Estoque Fiscal |
| 4 | `fiscalStockPeriods` | Fechamento de Estoque Mensal | Fechamento do Estoque Fiscal |
| 5 | `fiscalInventory` | Registro de Inventário Modelo P7 | Inventário em Data |

- Migration nova no `CommonContext` (Insert do `stock` + `ROLE_MENUS` ADMIN para ele; `UpdateData`
  dos 5 itens; `Down` reverte para os valores antigos). As orders restantes do grupo Fiscal podem
  ficar com buracos — não renumerar.
- Frontend: o `title` de cada `Page` muda para o novo nome; o `sheetName` do Excel do Kardex vira
  "Registro Kardex". Textos explicativos que citam "Kardex Fiscal" passam a "Registro Kardex".
  Nada de renomear rotas, controllers, entidades ou endpoints (Key = name da rota no manifest).
- Se o frontend tiver algum mapa de módulo → menu (ex.: pesquisa de apps do ShellBar, home), conferir
  que os itens aparecem sob "Estoque".

## 2. PDF Registro Kardex – Modelo 3

Base visual: `C:\Projetos\SiagroB1\matr470.pdf` (MATR470 do Protheus). Não precisa ser idêntico.

### Endpoint
`POST /reports/FiscalKardex/Book` no `SiagroB1.Reports`, espelho de
`FiscalInventoryController.Book`. Request:
`BranchCode`, `FromDate`, `ToDate`, `ItemCode?` (vazio = todos), `BookNumber` (≥1), `FirstSheet` (≥1).
Validações iguais ao P7 (filial com razão social, CNPJ, IE; "Informe o número do livro e a folha
inicial."); sem movimento nem saldo → `BusinessException("Não há movimento de estoque no período.")`.
Template novo `Reports/Templates/FiscalKardexBook.frx`, paisagem A4, exportado por `FastReportService.ExportPdf`.

### Estrutura
- **Termo de abertura** (1ª folha) e **termo de encerramento** (última), mesmo texto do P7 trocando
  "Registro de Inventário existente em [data]" por "Registro de Controle da Produção e do Estoque
  do período de [de] a [até]". Total de folhas = `[TotalPages]`, como no P7.
- **Fichas**: uma por **produto × mês** dentro do período (ordem: mês, depois código do produto).
  Cada ficha começa em folha nova.
- **Cabeçalho de cada folha**: título "REGISTRO DE CONTROLE DA PRODUÇÃO E DO ESTOQUE", firma (razão
  social), filial, IE, CNPJ, "Folha nº" (= `pFirstSheet + Page - 1`), "Mês ou período/ano: MM/AAAA",
  produto (código – descrição), UM, classificação fiscal (NCM). Quadro lateral com a legenda
  "(*) Código de entradas e saídas: 1 – No próprio estabelecimento; 2 – Em outro estabelecimento;
  3 – Diversas". (O MATR470 mostra "Armazém"; aqui não há — omitir.)
- **Colunas**: Documento (Espécie, Série, Número, Data) · Dia · Lançamento (Codificação Contábil,
  Fiscal = CFOP) · E/S · Cód.(*) · Quantidade · Valor · IPI · Estoque (quantidade acumulada) · Observações.
- **Primeira linha** da ficha: "SALDO INICIAL:" com o estoque (quantidade) no início do mês.
- **Rodapé da ficha**: "TOTAL DO PERÍODO:" com linhas E (qtd + valor), S (qtd + valor), EST (saldo final em qtd).

### Preenchimento (decisões aprovadas)
- Fonte: `FiscalKardexService.BuildAsync` por mês (ou uma chamada no período inteiro, fatiando por
  mês com o saldo acumulado — escolha do implementador; o saldo inicial do mês = saldo final do anterior).
- Uma linha por linha do Kardex. Quantidade = `QuantityIn` ou `QuantityOut`; Valor = `ValueIn` ou
  `ValueOut` (custo PEPS); Estoque = `BalanceQuantity`. IPI sempre 0,00.
- **Cód.(*)**: `1` para entradas, saídas e devoluções de nota; `3` (Diversas) para ajuste manual e
  complemento de valor. Saldo inicial lançado no período (movimento `Opening`) sai como linha com
  Cód. 3 e "Saldo inicial" em Observações.
- **Espécie**: "NFE" para linhas de nota; vazio para ajuste/saldo inicial.
- **Série / Número**: série e número fiscais da nota. Hoje o `FiscalStockLineSource` só tem
  `DocumentNumber` já composto — adicionar `Series` (e número cru) ao record no loader
  (`TaxDocumentSeries`, `TaxDocumentNumber`), sem alterar a saída atual do Kardex.
- **Codificação contábil**: `Item.LedgerAccountCode` (B1_CONTA) **só nas entradas** (E), como no
  MATR470; vazio nas saídas e se o produto não tiver conta.
- **Observações**: ajustes → "Ajuste manual" (+ motivo, se couber); complemento → "Complemento de valor";
  devoluções → "Devolução de compra/venda". Notas comuns: vazio.
- Formatação pt-BR feita no C# (como `FiscalInventoryBookRowDto`): quantidade `N3`, valor `N2`.

### Frontend
Botão "Imprimir livro" na tela Registro Kardex abrindo diálogo com filial (da tela), período (da tela),
produto (da tela, opcional), nº do livro e folha inicial — reaproveitar o diálogo/fluxo de download do
P7 (`fiscalInventory`). PDF baixado/aberto como o P7 faz.

## Testes
- Reports: teste do serviço do livro como os do P7 (preparar relatório e conferir linhas/fichas por
  mês, saldo inicial, totais, Cód.(*), conta só nas entradas, validações).
- Migration de menu: conferir no banco local (SELECT de MENU_ITEMS) e no navegador que o menu
  "Estoque" aparece com os 5 itens e que cada um abre.
- PDF: renderizar localmente e olhar (ver memória render-report-template-locally).

## Não-objetivos
- Não mudar cálculo PEPS, rotas, entidades ou permissões além do menu novo.
- Não criar colunas separadas de produção (Cód. 1/2 de produção) — não há produção no sistema.
- Não mexer no layout do P7.
