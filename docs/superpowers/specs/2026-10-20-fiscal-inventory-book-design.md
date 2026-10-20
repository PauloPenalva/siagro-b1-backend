# Inventário em Data + Livro de Registro de Inventário — design

Data: 2026-10-20 · Modo: só STANDALONE · Repos: backend + frontend ·
Branch `feature/fiscal-inventory` (a partir de `feature/fiscal-kardex`)

## Por quê

3ª e última entrega do lugar do "livro fiscal" (1ª Conferência Fiscal, 2ª Kardex Fiscal). O usuário
quer **imprimir o Livro de Registro de Inventário** (modelo 7) para encadernar/autenticar — o
inventário de 31/12 do fechamento —, e o número tem de ser o mesmo do saldo do Kardex na data.

## Decisões do usuário

- Custo FIFO sem ICMS creditado (motor do Kardex).
- **Produto com saldo negativo aparece com a quantidade negativa** (valor = soma das camadas). (20/10)
- **Livro completo**: termo de abertura, termo de encerramento, páginas numeradas, dados da empresa. (20/10)
- Cada tela chamável direto do menu (menu Fiscal).

## O que é

Uma tela **Inventário em Data** no menu Fiscal (StandaloneOnly, ROLE_MENUS ADMIN). Filtros: filial
(obrigatória; padrão = filial de trabalho), data do inventário (obrigatória; padrão = 31/12 do ano
anterior), produto (opcional). Botão Consultar → grade com uma linha por produto: código, descrição,
NCM, unidade, quantidade, custo unitário, valor total; total geral no rodapé; Exportar Excel.
Botão **Imprimir livro** → PDF (FastReport) do Livro de Registro de Inventário com:

- **Termo de abertura** (1ª página): "Contém este livro N folhas numeradas tipograficamente, do nº X ao
  nº Y, e servirá para o lançamento do Registro de Inventário de <razão social>", com nº do livro,
  razão social, endereço, CNPJ, Inscrição Estadual, data e linhas de assinatura (responsável e
  contabilista, em branco).
- **Páginas do inventário**: cabeçalho por página com razão social, CNPJ, IE, "Inventário existente em
  dd/MM/yyyy", nº do livro e **folha nº** (sequencial a partir da página inicial informada). Colunas do
  modelo 7: classificação fiscal (NCM), discriminação (código + descrição), quantidade, unidade,
  valor unitário, valor parcial, valor total; "a transportar" / "transporte" entre páginas e total
  geral na última.
- **Termo de encerramento** (última página): mesmo texto no passado ("Contém este livro… e serviu…").
- Campos informados no momento de imprimir: **nº do livro** e **folha inicial** (padrão 1), para que a
  numeração continue de um ano para o outro. N e Y são calculados pelo total de páginas.

## Regras

- Fonte: `FiscalKardexService.OpenLayersAsync(branch, item, data)` — quantidade = Σ camadas − déficit;
  valor = Σ camadas; custo unitário = valor ÷ quantidade (vazio quando quantidade ≤ 0).
- Produto com quantidade e valor zero não entra. Quantidade negativa entra (decisão do usuário).
- Complemento de valor pendente (sem camada) não entra no valor; a linha leva aviso na tela e não
  aparece no livro.
- Ordem: NCM, depois código do produto.
- Dados da empresa da filial (`Branch.LegalName`/`BranchName`, `TaxId`, `StateRegistration`, endereço).

## Fora do escopo

Mercadoria de terceiros em nosso poder e nossa em poder de terceiros (H010 IND_PROP 1/2); arquivo
SPED H005/H010; valor para fins de IR (H020); assinatura digital do PDF; modo SAPB1.

## Como (decisões do Claude)

- Backend em `SiagroB1.Reports`: `POST /reports/FiscalInventory` (JSON, grade) e
  `POST /reports/FiscalInventory/Book` (PDF), recusa SAPB1, como Conferência e Kardex.
- Termos e páginas num único `.frx` (`FiscalInventoryBook.frx`), numeração de folha via parâmetro de
  folha inicial + número de página do FastReport; total de páginas calculado em duas passagens
  (FastReport `DoublePass`).
- Menu: migration `CommonContext`, key `fiscalInventory` (= rota), filho de `fiscal`, Order 8.

## Detalhes resolvidos (decisões do Claude, revisáveis)

1. **Data inclusiva**: o inventário em dd/MM/yyyy considera todos os movimentos até o fim desse dia
   (mesmo critério do "até" do Kardex) — o valor bate com o saldo final do Kardex com "até" = data.
2. **Complemento pendente**: não entra no livro. Se houver algum, "Imprimir livro" pede confirmação
   listando os produtos ("Há complementos de valor sem saldo… imprimir mesmo assim?").
3. **Dados obrigatórios**: sem razão social, CNPJ ou Inscrição Estadual da filial, a impressão é
   recusada com mensagem pt-BR dizendo o que falta. Produto sem NCM imprime a classificação em branco
   e vai para o fim da lista.
4. **Nº do livro e folha inicial**: digitados a cada impressão, sem gravar histórico (YAGNI).
5. **Colunas do modelo 7**: "valor parcial" = valor do item (Σ camadas); "valor total" = subtotal da
   classificação fiscal (NCM), na última linha de cada NCM. "A transportar"/"Transporte" = soma
   acumulada dos valores parciais, calculada no relatório por página.
6. **Valor autoritativo** = Σ camadas da linha; custo unitário = valor ÷ quantidade, exibido com 6
   casas (só exibição). Grade, Excel e livro mostram o mesmo valor; o total geral é a soma das linhas.
7. **Quantidade negativa**: quantidade negativa, custo unitário em branco, valor = Σ camadas (em geral
   0,00); entra no total geral.
8. **Seleção de produtos** (decisão do usuário 20/10, como o MATR460 do Protheus): a tela tem seleção
   de vários produtos; ela vale para a grade **e para o livro** (o livro sai só com os produtos
   escolhidos; vazio = todos). A tela lembra a última seleção de cada usuário.
