# Estoque fiscal: ajuste manual, recálculo de custo e fechamento de mês — design

Data: 2026-10-20 · Modo: só STANDALONE · Repos: backend + frontend · Base: `main` (Kardex Fiscal,
Saldo Inicial e Inventário/Livro já mergeados e no ar na CEAGUI)

## Por quê

Na CEAGUI produção o Kardex Fiscal mostra muitas linhas automáticas "Acerto de saldo negativo" com
quantidade 0, que confundem. Elas nascem porque o motor PEPS, quando uma entrada chega depois de uma
saída sem saldo, cria uma linha só de valor para cobrir o déficit. O usuário não quer que o sistema
invente lançamentos: **ajuste de estoque é decisão do departamento fiscal**, e o período entregue à
contabilidade não pode mudar depois.

## Decisões do usuário (20/10/2026)

1. **Sem acerto automático.** A linha "Acerto de saldo negativo" deixa de existir.
2. **Saída sem saldo** sai a custo zero, marcada, e o saldo fica **negativo** até chegar o documento de
   entrada (o fornecedor às vezes demora a emitir) ou o fiscal lançar um **ajuste manual**.
3. **Ajuste manual** feito pelo usuário do fiscal: entrada ou saída **valorizada** (quantidade e valor).
4. **Recálculo do custo**: quando a entrada chega, o custo dela vai para a **própria linha da saída** que
   tinha saído a zero (recálculo retroativo dentro do período aberto), não para uma linha extra.
5. **Fechamento de mês**: botão **Fechar mês**, com opção de **Reabrir**.
6. Mês fechado **não aceita entrada atrasada**: ela fica no mês corrente.
7. **O estoque/Kardex não pode fechar negativo**: o mês só fecha sem nenhum produto negativo.
8. Nota emitida num mês fechado é lançada **pedindo a data de entrada no estoque** no mês aberto
   (a emissão continua a original).
9. **Saída de ajuste: o usuário informa o valor.**
10. **Cancelamento, devolução ou estorno que mexa em mês fechado é bloqueado** até alguém reabrir o mês
    (inclusive cancelamento de NF-e dentro das 24h da SEFAZ: o fiscal reabre, cancela e fecha de novo).
11. **O Saldo Inicial marca o começo do controle**: o primeiro mês que pode ser fechado é o do Saldo
    Inicial; movimentos anteriores a ele ficam fora do Kardex (como já é hoje por produto).

## Objetivos

- Kardex sem linhas inventadas; todo número vem de documento, saldo inicial ou ajuste do usuário.
- Saída sem saldo recebe o custo da entrada que a cobre, na linha dela.
- Mês fechado congelado: Kardex, Inventário e Livro daquele mês não mudam até alguém reabrir.

## Fora do escopo

Custo médio, armazém, conversão de unidade, integração contábil, modo SAPB1, fechamento de outros
módulos (financeiro), SPED (o bloco H continua lendo o Inventário).

## Anti-objetivos

- Recálculo que altere um mês fechado.
- Lançamento automático de qualquer ajuste pelo sistema.
- Travar a operação do dia a dia (faturar, receber nota) por causa de saldo negativo no mês **aberto**.
  O negativo só impede o **fechamento**.

## Partes

### 1. Motor PEPS (`SiagroB1.Reports/Services/Fiscal/FifoLedger.cs`)

- Remover a linha de acerto (`IsNegativeBalanceAdjustment` e o ramo em `Inflow`).
- Déficit vira **fila de saídas pendentes**: cada saída (ou parte dela) que saiu sem camada guarda
  linha + quantidade faltante, em ordem.
- **Entrada** (inclusive ajuste manual de entrada, devolução de venda e saldo inicial): primeiro cobre a
  fila pendente, da mais antiga para a mais nova, pelo custo unitário dela. O valor da parte coberta
  é **somado ao `ValueOut` da linha da saída original**; o restante vira camada.
- Saldo em valor de cada linha passa a ser recalculado depois do custeio, como soma acumulada
  (Σ valor de entrada − Σ valor de saída). Assim, entre a saída e a entrada que a cobre, o saldo fica
  negativo em quantidade **e** em valor (ex.: −10 / −R$ 1.000), e volta a 0 / 0 quando a entrada chega.
- A saída coberta perde o aviso "saldo insuficiente" e ganha o aviso informativo
  "Custo pela entrada <nº interno> de <data>" (se coberta por mais de uma, lista as duas). Saída ainda
  pendente no fim do período consultado mantém "saldo insuficiente".
- Complemento de valor pendente (sem camada) continua como hoje.
- Devolução de venda usa o custo que a saída de origem tinha **no momento da devolução** no cálculo em
  ordem cronológica; não é recusteada se a saída for coberta depois (decisão do Claude, evita cadeia
  de recálculos).
- O recálculo retroativo vale para todos os meses **abertos**; um mês reaberto volta a participar.
- O Inventário/Livro continua lendo camadas − déficit; com o fechamento sem negativo, o livro de mês
  fechado nunca tem déficit.

### 2. Ajuste manual de estoque (tela nova, menu Fiscal)

- Cadastro (lista + incluir/editar/excluir) **"Ajustes de Estoque Fiscal"**: filial, data, produto,
  tipo (Entrada / Saída), quantidade (> 0), valor total (≥ 0), motivo (obrigatório, texto livre),
  número interno sequencial (série própria, como o DE da entrada), usuário e data de criação.
- Entra no motor como movimento do tipo novo `Adjustment` (entrada ou saída). No Kardex aparece como
  "Ajuste manual — entrada/saída", com o motivo na coluna de natureza/observação.
- **Entrada de ajuste** = entrada normal (cobre pendentes, depois camada) pelo valor informado.
- **Saída de ajuste**: valor informado pelo usuário. Baixa a quantidade das camadas mais antigas (FIFO)
  e tira delas o valor informado, na proporção da quantidade de cada uma (decisão do Claude); se o valor
  passar do valor das camadas consumidas, recusa. Sem camada suficiente, recusa (ajuste de saída não
  pode gerar saldo negativo — decisão do Claude).
- Editar/excluir só com a data em mês aberto.

### 3. Fechamento de mês (tela nova, menu Fiscal)

- Tela **"Fechamento do Estoque Fiscal"** por filial: lista dos meses (aberto/fechado, quem e quando
  fechou/reabriu), botões **Fechar mês** e **Reabrir**.
- **Fechar** o mês M da filial F exige: M−1 fechado, ou M ser o mês do Saldo Inicial mais antigo da
  filial (o controle começa nele; sem Saldo Inicial lançado, nenhum mês fecha); nenhum produto da filial
  com saldo negativo em quantidade no último dia de M; nenhum produto com quantidade 0 e valor ≠ 0 nem
  complemento de valor pendente (decisão do Claude: o livro congelado não pode ter valor sem quantidade).
  Saldo Inicial com data dentro de mês fechado não pode ser incluído/editado/excluído. Se houver, recusa e
  lista produto, saldo e a saída pendente, para o fiscal lançar a nota ou um ajuste.
- **Reabrir** M exige que M+1 esteja aberto (reabre do mais recente para trás). Pede motivo.
- Tabela de períodos por empresa (`FISCAL_STOCK_PERIODS`: filial, ano-mês, status, fechado por/em,
  reaberto por/em, motivo) + histórico de eventos (fechar/reabrir) para auditoria.
- **Instantâneo no fechamento** (decisão do Claude): ao fechar, grava as camadas em aberto de cada
  produto no último dia do mês (`FISCAL_STOCK_PERIOD_LAYERS`). Kardex/Inventário de período
  posterior partem desse instantâneo em vez de recalcular do início; de período fechado, leem dele.
  Isso congela o mês mesmo que algum dado antigo mude por um caminho não travado, e deixa a consulta
  mais leve. Reabrir apaga o instantâneo daquele mês.
- Permissão: ADMIN via ROLE_MENUS, como o Kardex (decisão do Claude); menu com migration no Common.

### 4. Trava de período fechado

Nenhum movimento de estoque fiscal pode nascer, mudar ou sumir com data num mês fechado da filial.
Data que vale: entrada = `PostingDate ?? IssueDate`; saída = `InvoiceDate`; ajuste = data dele;
saldo inicial = data dele. Pontos a travar (o builder confirma a lista completa):

- Documento de Entrada: confirmar, editar linha/data, cancelar, excluir, estornar, complemento de valor.
- Documento de Saída: confirmar, cancelar NF-e, estornar, devoluções.
- Ajuste manual e Saldo Inicial: incluir/editar/excluir.

Mensagem: "O estoque fiscal de MM/AAAA da filial X está fechado. Use uma data de entrada no estoque
a partir de DD/MM/AAAA ou peça a reabertura do mês."

### 5. Data de entrada no estoque no Documento de Entrada

- O campo existente `PostingDate` passa a ser rotulado **"Data de entrada no estoque"** na tela e é
  validado: não pode cair em mês fechado. Ao confirmar com a data em mês fechado, o sistema recusa e
  pede a data (sugere o 1º dia aberto). A emissão (`IssueDate`) não muda.
- Hoje `PostingDate` nasce com a data de hoje; mantém.

## Como (decisões do Claude, revisáveis)

- Entidades novas em `AppDbContext` (migration por empresa — rodar em **todas** as empresas, a Web
  recusa subir com migration pendente). CRUDs via OData no `SiagroB1.Web`, cálculo no `SiagroB1.Reports`
  como o Kardex.
- Validação de período num serviço único (`FiscalStockPeriodGuard`) chamado por todos os pontos da
  parte 4; o Reports usa o instantâneo.
- Menus: migration Common, filhos de `fiscal`, ordem depois do Kardex.
- Testes: motor PEPS (cobrir pendência, saída coberta por duas entradas, entrada maior que a pendência,
  saída ainda pendente, saldo em valor negativo intermediário), fechamento (recusa com negativo, ordem
  de fechar/reabrir), trava em cada ponto.

## Implantação na CEAGUI

A produção não tem saldo inicial lançado. Após o deploy, o fiscal lança o Saldo Inicial e ajustes
antes de fechar o primeiro mês. Nada é fechado automaticamente.

