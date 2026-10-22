# Contabilidade no modelo Protheus (SIGACTB): lançamento padrão, contabilização, fechamento e relatórios

Data: 22/10/2026 · Repos: `siagro-b1-backend` e `siagro-b1-frontend` · Branch: `feature/accounting-core`

## Intenção

O SiagroB1 precisa de uma contabilidade **mínima, mas funcional**, dentro do próprio ERP. O objetivo
é que o contador feche o mês sem um segundo sistema: os documentos que o ERP já gera (entradas, saídas,
títulos, baixas, movimentos de conta) viram lançamentos contábeis, o período é fechado, o resultado é
apurado, e saem razão, diário, balancete, DRE e balanço.

O usuário pediu o **modelo do Protheus** (fontes de referência em `C:\Projetos\fontes-protheus-12.2310`):

- regras configuráveis de **lançamento padrão** (CT5);
- **rotinas de contabilização** por módulo, que chamam o lançamento padrão pelo código;
- lançamentos gravados numa tabela única de partidas, a **CT2**.

O usuário confirmou que, no Protheus, a CT2 é a tabela de lançamentos. Ela está documentada em
`Adm\CTBXATU.PRW:155` (`CTBGrava`) e `Adm\CTBA102.PRW`.

Este documento **substitui** a parte de "determinação de contas no modelo SAP B1" do roadmap contábil
de 16/10 e a seção D da spec `2026-10-21-transport-and-managerial-accounting-design.md`, que cobria os
sub-projetos 5, 6 e 7. Continua valendo o que o sub-projeto 1 já entregou e está em `main`:

- Centro de Custo (CTT) e Item Contábil (CTD);
- rateio pré-configurado (CTJ);
- amarração pela conta (`AcceptsCostCenter`/`RequiresCostCenter`/`AcceptsAccountingItem`/`RequiresAccountingItem`);
- módulo **Contabilidade** no menu.

## Objetivos

1. **Plano de contas contábil completo**: hierarquia, classe sintética/analítica, natureza devedora/credora
   e conta de apuração de resultado.
2. **Calendário contábil** mensal com situação Aberto / Fechado / Bloqueado (CTG + CQD).
3. **Lançamento contábil** nos moldes da CT2: lote, documento, linha, partida dobrada, histórico, CC e
   Item a débito e a crédito, origem, lançamento padrão.
4. **Lançamento manual** com estorno (CTBA102).
5. **Lançamento padrão** configurável (CT5), com uma regra por código e várias sequências.
6. **Contabilização on-line**, nos eventos dos módulos, e **off-line**, como rotina de reprocessamento por
   período (CTBANFE / CTBANFS / FINA370).
7. **Apuração de resultado** (CTBA211) e **fechamento do período** (CTBA012 / CTBA400).
8. **Relatórios**: Razão (CTBR400), Diário (CTBR110), Balancete (CTBR040), DRE (CTBR510) e Balanço (CTBR500).
   A DRE e o Balanço saem de uma **visão gerencial** configurável (CTS).

## Não-objetivos

Ficam para depois:

- Moedas além do real, critério de conversão e tipos de saldo (real/gerencial/orçado). O sistema usa só
  "1 – Real".
- Classe de valor e entidades 05–09.
- Tabelas de saldo materializadas (CQ0–CQ9) e o reprocessamento de saldos (CTBA190). Os saldos são
  calculados a partir das linhas de lançamento (ver Decisões).
- ECD, ECF, SPED Contribuições e REINF. O código referencial da RFB entra no plano para não refazer
  cadastro depois, mas nada o consome agora.
- Aglutinação (CT5_HAGLUT / CT2_AGLUT), contra-prova (CTK) visível ao usuário e conferência de digitação.
- Modo SAPB1, onde a contabilidade é do SAP. Nenhuma tela, menu ou coluna nova aparece em SAPB1.
- Custo médio (o CMV usa o FIFO do estoque fiscal; ver "Custo da venda").

## Anti-objetivos (falha mesmo que funcione)

- **Lançamento desbalanceado.** Débito ≠ crédito num documento contábil é falha grave, e nenhum caminho
  pode gravá-lo.
- **Contabilidade que trava a operação por configuração ausente.** Se não existe lançamento padrão ativo
  para um código, o evento não contabiliza e o documento confirma normalmente, como o `VerPadrao` do
  Protheus.
  - **Erro dentro de um lançamento padrão ativo** (conta inexistente, conta sintética, conta bloqueada,
    CC obrigatório ausente, desbalanceado) **recusa** a operação com mensagem pt-BR que cita o código e
    a sequência do LP. É o comportamento do Protheus on-line, e evita lançamento parcial.
- **Lançamento em período fechado.** Nenhuma operação, automática ou manual, grava com data em período
  Fechado ou Bloqueado. O documento de origem também é recusado, com a mensagem "Período contábil
  fechado".
- **Dupla contabilização.** O mesmo evento não pode gerar dois lançamentos. O rastro de origem é único
  por (código LP, tipo de documento, chave do documento, evento).
- **Editar ou apagar lançamento.** Lançamento gravado é imutável. A correção é feita por estorno.

## Restrições

- Só STANDALONE, com gate positivo (`ErpMode.IsStandalone`). O menu usa `StandaloneOnly = true`.
- Identificadores em inglês e textos de tela em pt-BR. Os códigos de LP seguem a numeração do Protheus
  (650, 610, 510…), para que o contador reconheça.
- Migrations aditivas, aplicadas em **todas as empresas** da COMMON, porque o Web recusa subir com
  migration pendente. Contas existentes viram **analíticas sem pai**, natureza derivada do `Type` atual
  (Asset/Expense → devedora; Liability/Revenue → credora) e classe sintética = false.
- Filial: os lançamentos levam `BranchCode`, e os relatórios filtram por filial ou consolidam todas.
  O calendário é por empresa (banco), não por filial.
- Sem expressões executáveis. O CT5 do Protheus guarda ADVPL avaliado com `&()`. Aqui o lançamento padrão
  escolhe de listas fechadas de **fontes de conta** e **fontes de valor** por código, pelo motivo de
  segurança e testabilidade descrito em Decisões.

## Como funciona

### 1. Plano de contas (CT1) — amplia `LEDGER_ACCOUNTS`

Campos novos:

| Campo | Protheus | Observação |
|---|---|---|
| `ParentCode` | CT1_CTASUP | Conta superior. Sem ciclos. |
| `Class` | CT1_CLASSE | `AccountingEntityClass` Synthetic/Analytic (já existe para CC/Item). Só analítica recebe lançamento. |
| `Nature` | CT1_NORMAL | Debit / Credit. Define o sinal do saldo nos relatórios. |
| `Type` | — | O enum existente ganha **Equity** (patrimônio líquido). Asset / Liability / Equity / Revenue / Expense. |
| `ResultClosingAccountCode` | CT1_CTALP | Conta de apuração. Só faz sentido em conta de resultado; vazia = usa a conta de apuração padrão (ver 6). |
| `ReferentialCode` | CT1_NTSPED/CVD | Código do plano referencial RFB, texto livre por ora. |
| `BlockedFrom` / `BlockedTo` | CT1_DTBLIN/FI | Bloqueio por data. Já existe `Inactive`, que continua bloqueando sempre. |

`AllowsPosting` (existente) passa a ser derivado: é false para conta sintética. A tela é a lista e o
formulário atuais de Plano de Contas, com os campos acrescentados e uma visão em árvore (TreeTable) na
lista.

### 2. Calendário contábil (CTG + CQD)

`ACCOUNTING_PERIODS`: `Year`, `Month`, `StartDate`, `EndDate`, `Status` (Open=1, Closed=2, Blocked=4,
como no CQD), `ClosedAt/By`, `ReopenedAt/By`, `ReopenReason`.

- Tela **Calendário Contábil**: gera os 12 meses de um exercício e mostra a situação. As ações são
  Fechar, Bloquear e Reabrir. Reabrir exige motivo e só é permitido ao papel ADMIN, verificado via
  `HasRoleAsync` (ver a memória "Role ADMIN ≠ flag IsAdmin").
- **Guarda** `AccountingPeriodGuard.EnsureOpenAsync(date)` (modelo do `FiscalStockPeriodGuard`):
  - data **sem período cadastrado** = aberto, para não travar empresa que nunca usou a contabilidade;
  - período Fechado ou Bloqueado = recusa.
  - Chamada **em todo lançamento**, manual ou automático. A contabilização on-line chama a guarda, então a
    operação de origem também é recusada quando há LP ativo para o evento e o período está fechado. Sem
    LP ativo, o documento não é bloqueado.
- **Fechado × Bloqueado.**
  - Fechado: fechamento normal. Exige a apuração de resultado do mês, quando houver contas de resultado
    com saldo e a empresa apurar mensalmente (ver 6). Reabrir estorna nada.
  - Bloqueado: trava temporária, sem exigências.

### 3. Lançamento contábil (CT2)

Cabeçalho `JOURNAL_ENTRIES` = o "documento" do Protheus (CT2_DATA + LOTE + SBLOTE + DOC):

| Campo | Protheus |
|---|---|
| `DocEntry` (PK int identity) | — |
| `PostingDate` | CT2_DATA |
| `BranchCode` | CT2_FILIAL |
| `Batch` (varchar 6) | CT2_LOTE. Por origem, conforme o catálogo em 5 (008810 entradas, 008820 saídas, 008850 financeiro, 008860 apuração, 008890 manual). |
| `SubBatch` (varchar 3) | CT2_SBLOTE, padrão "001" |
| `DocumentNumber` (varchar 6) | CT2_DOC. Sequencial por data+lote+sublote (CTF / `ProxDoc`), gerado no servidor com trava. |
| `Origin` | Manual / StandardPosting / ResultClosing / Reversal |
| `StandardPostingCode` | CT2_LP (vazio no manual) |
| `SourceDocumentType` / `SourceDocumentKey` / `SourceEvent` | CT2_ROTINA + CT2_KEY. Ex.: `PurchaseInvoice` / `1234` / `Confirm`. |
| `ReversalOfDocEntry` / `ReversedByDocEntry` | Vínculo de estorno (CT2_ESTCAN) |
| `CreatedAt/By`, `Comments` | |

Linhas `JOURNAL_ENTRY_LINES` (cada linha é uma CT2):

| Campo | Protheus |
|---|---|
| `LineNumber` (int, 1..n) | CT2_LINHA |
| `EntryType` | CT2_DC: 1 Débito, 2 Crédito, 3 Partida dobrada, 4 Continuação de histórico |
| `DebitAccountCode` / `CreditAccountCode` | CT2_DEBITO / CT2_CREDIT |
| `Amount` (decimal 19,2) | CT2_VALOR |
| `History` (varchar 40 por linha; tipo 4 continua) | CT2_HIST / CT2_SEQHIS |
| `DebitCostCenterCode` / `CreditCostCenterCode` | CT2_CCD / CT2_CCC |
| `DebitAccountingItemCode` / `CreditAccountingItemCode` | CT2_ITEMD / CT2_ITEMC |
| `StandardPostingSequence` | CT5_SEQUEN que gerou a linha |
| `BusinessPartnerCode` | Auxiliar para razão por parceiro (não existe na CT2; é minha decisão, útil para conciliação de clientes e fornecedores) |

Invariantes, verificados no serviço único `JournalEntryPostingService`, por onde passa **todo** lançamento:

- soma dos débitos = soma dos créditos, no documento inteiro;
- só contas analíticas, ativas e não bloqueadas na data;
- CC e Item respeitam a amarração da conta (aceita/obriga) e a vigência e classe analítica do CC/Item,
  reusando `AccountingEntityRules`;
- valor > 0 em linhas 1/2/3 e = 0 em linha 4;
- período aberto.

**Histórico:** o limite de 40 caracteres e a linha tipo 4 imitam a CT2 e mantêm compatibilidade com o
layout da ECD. A tela junta os pedaços para leitura.

**Imutável.** Não há PATCH nem DELETE na API. O **estorno** (CTBA102, opção estornar) cria um lançamento
com débito e crédito invertidos, na data informada (padrão: hoje; precisa de período aberto). Os dois
ficam vinculados, e um lançamento estornado não pode ser estornado de novo.

**Rateio:** quando o documento de origem tem `DistributionRuleCode`, a linha de resultado é expandida
pelo `DistributionRuleExpander` (vigente na data do documento) em várias linhas, uma por CC+Item, com o
valor rateado. A diferença de arredondamento vai para a última linha. O lançamento guarda só CC e Item,
nunca a regra.

### 4. Lançamento manual (CTBA102)

Tela **Lançamentos Contábeis**:

- **Lista** com FilterBar: data de/até, lote, documento, origem, código LP, conta, filial, documento de
  origem.
- **Detalhe** (ObjectPage) com cabeçalho, grade de linhas e ações *Estornar* e *Abrir documento de
  origem*.
- **Inclusão manual**: cabeçalho (data, filial, lote padrão "008890" editável, histórico padrão opcional)
  e grade editável de linhas com tipo 1/2/3, contas, valor, histórico, CC e Item. Um rodapé mostra
  débito, crédito e diferença, e o botão Gravar fica desabilitado enquanto a diferença ≠ 0. A validação
  final é a do servidor.
- O **saldo inicial** de implantação é um lançamento manual na data de corte, contra uma conta transitória
  de abertura escolhida pelo contador. Não há rotina dedicada.

### 5. Lançamento padrão (CT5) e contabilização

#### Cadastro

`STANDARD_POSTINGS` (cabeçalho por código) e `STANDARD_POSTING_LINES` (sequências):

| Campo | CT5 |
|---|---|
| `Code` (varchar 3) | CT5_LANPAD |
| `Description` | CT5_DESC |
| `Active` | CT5_STATUS ("2" = desativado) |
| linha `Sequence` (varchar 3) | CT5_SEQUEN |
| linha `EntryType` (1/2/3) | CT5_DC |
| linha `DebitAccountSource` + `DebitAccountCode` | CT5_DEBITO |
| linha `CreditAccountSource` + `CreditAccountCode` | CT5_CREDIT |
| linha `ValueSource` | CT5_VLR01 |
| linha `HistoryTemplate` | CT5_HIST |
| linha `DebitCostCenterSource` / `CreditCostCenterSource` | CT5_CCD / CT5_CCC |
| linha `DebitItemSource` / `CreditItemSource` | CT5_ITEMD / CT5_ITEMC |
| linha `Active` | — |

Em vez de expressão ADVPL, cada campo escolhe uma **fonte** de uma lista fechada, que depende do
**contexto** do código LP:

- **Fonte de conta:** `Fixed` (usa `DebitAccountCode`), ou um campo do contexto, por exemplo
  `Item.LedgerAccountCode`, `Line.LedgerAccountCode`, `Partner.DefaultLedgerAccountCode`,
  `FinancialAccount.LedgerAccountCode`, `Document.LedgerAccountCode`.
  - Ordem de fallback quando o campo do contexto está vazio: **linha do documento → produto → parceiro →
    conta fixa da sequência**. Se ainda assim ficar vazio, o LP é recusado com erro citando a
    sequência.
  - Fallback é minha decisão; o Protheus resolveria isso dentro da expressão.
- **Fonte de valor:** um campo numérico do contexto, como `Line.Total`, `Line.IcmsValue`,
  `Line.PisValue`, `Line.CofinsValue`, `Line.FunruralValue`, `Line.FreightValue`, `Line.DiscountValue`,
  `Document.GrandTotal`, `Document.NetPayable`, `Document.WithheldIrrf`, `Settlement.Amount`,
  `Settlement.InterestAmount`, `Settlement.DiscountAmount`, `Entry.Amount`, `Transfer.Amount`… A lista
  completa por contexto é do construtor, a partir dos campos que existem hoje (relatório de
  reconhecimento no fim).
  - **Valor zero não gera linha.** Equivale ao `IIF(valor>0, valor, 0)` comum no CT5.
- **Fonte de CC/Item:** `None`, `Document` (CC/Item/rateio do documento ou da linha) ou `Fixed`.
- **Histórico:** texto com marcadores, por exemplo `NF {DocNumber} {PartnerName}`. Os marcadores
  disponíveis variam por contexto. O texto é cortado em 40 caracteres, e o excedente vira linha tipo 4.

Cada código LP tem um **contexto fixo**:

- contexto de linha (itera os itens do documento, como o 650 "itens") ou de documento (uma vez, como o
  660 "total");
- tipo de documento;
- conjunto de fontes.

O catálogo de códigos é fixo no código-fonte (`StandardPostingCatalog`), e o usuário só configura as
sequências. A tela mostra o código, a descrição Protheus, o contexto e as fontes permitidas.

#### Catálogo inicial (numeração do Protheus)

| Código | Evento no SiagroB1 | Contexto | Lote |
|---|---|---|---|
| 650 | Confirmação do Documento de Entrada (inclui CT-e) | linha | 008810 |
| 660 | Confirmação do Documento de Entrada | documento (total, retenções, Funrural) | 008810 |
| 655 | Estorno da confirmação / cancelamento do Documento de Entrada | linha | 008810 |
| 665 | Estorno / cancelamento do Documento de Entrada | documento | 008810 |
| 610 | Confirmação do Documento de Saída | linha | 008820 |
| 620 | Confirmação do Documento de Saída | documento | 008820 |
| 630 | Estorno / cancelamento do Documento de Saída | linha | 008820 |
| 635 | Estorno / cancelamento do Documento de Saída | documento | 008820 |
| 500 | Inclusão de título a receber **sem documento de origem** (manual, washout, adiantamento RA) | documento | 008850 |
| 505 | Cancelamento desse título a receber | documento | 008850 |
| 510 | Inclusão de título a pagar sem documento de origem (manual, outras compras sem nota, adiantamento PA) | documento | 008850 |
| 515 | Cancelamento desse título a pagar | documento | 008850 |
| 520 | Baixa a receber (inclui compensação de adiantamento e encontro de contas) | baixa | 008850 |
| 527 | Estorno da baixa a receber | baixa | 008850 |
| 530 | Baixa a pagar | baixa | 008850 |
| 531 | Estorno da baixa a pagar | baixa | 008850 |
| 562 | Lançamento na conta financeira – saída (pagar) | movimento | 008850 |
| 563 | Lançamento na conta financeira – entrada (receber) | movimento | 008850 |
| 564 / 565 | Exclusão do lançamento na conta (saída / entrada) | movimento | 008850 |
| 560 | Transferência entre contas | transferência | 008850 |
| 561 | Exclusão da transferência | transferência | 008850 |

Origem dos códigos:

- 650/651/655/656/660/665, 610/620/630/635, 500/505?/510/515?, 520/527, 530/531, 562/563 e 561 vêm dos
  fontes (`mata103.prw:14026`, `mata461.prx`, `mata521.prx`, `FINA040/050/070/080/100/370.PRX`).
- **Minha escolha, sem conferência nos fontes:** 505, 515, 564, 565 e 560. O construtor deve conferir na
  documentação do Protheus e ajustar a numeração se houver código oficial.
- Lotes: "008810/008820/008850/008890" seguem o padrão da tabela SX5 "09" do Protheus. Os valores exatos
  são minha decisão e ficam no catálogo.

**Regra do título com documento de origem:** título gerado por Documento de Entrada/Saída (firme de
compra ou venda) **não** passa pelo 500/510, porque a obrigação já foi contabilizada no 650/660 e no
610/620. É o mesmo que o Protheus faz ao marcar `E2_LA`/`E1_LA` dos títulos da NF. Títulos provisórios
(de fixação de contrato) **nunca** contabilizam.

**Adiantamento** (`FinancialDocument` com `Nature = Advance`):

- a inclusão contabiliza pelo 500/510;
- a criação já grava a baixa na conta (ver a memória "Adiantamento nasce pago"), e essa baixa contabiliza
  pelo 520/530;
- a compensação com o título é uma baixa (520/530).

O contexto expõe `Document.IsAdvance` como fonte de conta (conta de adiantamento do parceiro ou fixa), e
o contador separa as sequências.

#### Disparo on-line

O `AccountingPostingService.PostAsync(code, context)` é chamado pelos serviços de evento listados no
catálogo.

- Roda **dentro da mesma transação** do evento, no padrão "enqueue-only": só faz `Add`, e o `SaveChanges`
  é o do chamador. Assim, se a operação falha, o lançamento não fica; se o lançamento é recusado, a
  operação é desfeita.
- Funciona com `CommitMode.Deferred` dos serviços de saída.
- Em SAPB1 é no-op.
- Se o código não tem LP ativo, é no-op e não grava marca.

A **marca no documento de origem** é a própria existência do lançamento com aquele `(Source*, Event)`,
consultável. Não há campo `DTLANC` em cada tabela de documento (minha decisão: um índice único na origem
cobre a dupla contabilização sem migrar 6 tabelas).

O **estorno** de documento (655/665, 630/635, 505, 515, 527, 531, 564/565, 561) não apaga nada:

- gera o lançamento do código de estorno, configurado pelo contador como o 650 com lados invertidos;
- se o código de estorno **não** tem LP ativo, mas o documento original foi contabilizado, o sistema gera
  o **estorno automático** (inversão do lançamento original, vinculado) para não deixar saldo órfão.
  Minha decisão.

#### Contabilização off-line (CTBANFE / CTBANFS / FINA370)

Tela **Contabilização Off-line**: escolhe período (de/até), filial e módulos (Entradas, Saídas,
Financeiro). Ela processa, em ordem cronológica, os eventos do período que **não** têm lançamento e cujo
código LP está ativo, na data do evento.

- **Simulação** mostra a lista e os erros sem gravar.
- **Processar** grava, com uma transação por documento. Um erro não para os demais e aparece no
  relatório de saída.
- Roda como job Hangfire por empresa (`CompanyScopedJobRunner`), sem agenda, disparado pela tela. A tela
  acompanha o status.
- Serve para o **histórico**, ou seja, contabilizar desde a data de corte o que já existia antes do
  módulo, e para documentos que passaram enquanto o LP estava desativado.

A fonte de eventos do off-line é a tabela de cada documento:

| Evento | Data |
|---|---|
| Documento confirmado | data do documento |
| Cancelado | data do cancelamento |
| Baixa | data da baixa |
| Estorno da baixa | data do estorno |
| Lançamento na conta | data do lançamento |
| Transferência | data da transferência |

O construtor decide como achar a data de cada evento a partir do que as tabelas já guardam. Onde não
houver data do evento (por exemplo, data do estorno de confirmação), usa a data do documento e registra
isso no relatório.

### 6. Apuração de resultado e fechamento (CTBA211 / CTBA012)

Tela **Apuração de Resultado**: data de apuração (padrão: último dia do mês), filial (ou todas), lote
"008860", histórico padrão.

- Para cada conta de **resultado** (Revenue/Expense), com saldo ≠ 0 até a data desde a última apuração,
  gera a linha que zera o saldo contra a conta `ResultClosingAccountCode` da conta, ou, se vazia, contra a
  **conta de apuração padrão** da empresa.
  - A conta padrão é uma configuração nova, `ACCOUNTING_SETTINGS` (singleton `Key=1` como `EmailSettings`),
    com `DefaultResultClosingAccountCode` e `ClosingFrequency` (Monthly / Yearly).
  - O zeramento é **por conta, CC e Item**, para que o resultado por centro de custo continue fechando
    depois da apuração. Minha decisão: o Protheus faz isso com a opção de entidades na CTBA211.
- O lançamento leva `Origin = ResultClosing` e marca a data apurada. Os relatórios de DRE **ignoram**
  lançamentos de apuração (como `CT2_DTLP`), senão a DRE do período sairia zerada.
- **Estornar apuração**: estorna o lançamento de apuração mais recente, se o período ainda estiver
  aberto.
- **Encerramento do exercício** (CTBA400): na prática, é a apuração de dezembro mais o fechamento dos 12
  períodos. Não há transferência de saldo para o ano seguinte, porque os saldos são calculados
  cumulativamente desde a origem (ver Decisões). A transferência do resultado apurado para Lucros ou
  Prejuízos Acumulados é um lançamento manual do contador.

**Fechar período:**

- recusa se houver período anterior aberto;
- recusa se a empresa apura mensalmente e o mês tem resultado não apurado;
- mostra um aviso, sem impedir, se a contabilização off-line encontraria documentos não contabilizados
  no mês com LP ativo.

### 7. Relatórios

Todos ficam no módulo Contabilidade, em FastReport (PDF) no `SiagroB1.Reports`, com o padrão do extrato
da conta financeira (controller + DTO + service + `.frx`), mais tela no frontend com filtros e **Excel**.

Filtros comuns:

- período;
- filial (uma ou todas);
- conta de/até;
- CC e Item opcionais;
- nível de conta (sintéticas até o nível N) onde couber.

| Relatório | Protheus | Conteúdo |
|---|---|---|
| **Razão** | CTBR400 | Por conta analítica: saldo anterior; lançamentos com data, lote/doc, histórico, contrapartida, débito, crédito e saldo corrente com indicador D/C; saldo final. Opção por CC ou Item. |
| **Diário** | CTBR110 | Lançamentos em ordem de data/lote/doc, com termo de abertura e encerramento simples (empresa, CNPJ, período e número de páginas). Numeração de folhas sequencial a partir de um número inicial informado. |
| **Balancete** | CTBR040 | Por conta (analítico, ou sintético até o nível N): saldo anterior, débito, crédito e saldo atual com D/C; totais que fecham (débito = crédito). |
| **DRE** | CTBR510 | Pela visão gerencial escolhida. Colunas: período, acumulado no ano e % sobre a receita. Opção de colunas por CC (a DRE gerencial da spec da transportadora) com coluna "Sem centro" e total. |
| **Balanço Patrimonial** | CTBR500 | Pela visão gerencial escolhida, na data-base, com comparativo opcional de uma segunda data. |

Saldo de conta sintética = soma das filhas, calculada no relatório.

#### Visão gerencial (CTS) — mínima

Cadastro `MANAGEMENT_VIEWS` (código, nome, tipo DRE/Balanço) e `MANAGEMENT_VIEW_LINES`:

| Campo | CTS |
|---|---|
| `Order` | CTS_ORDEM |
| `Code` | CTS_CONTAG |
| `Description` | CTS_DESCCG |
| `ParentCode` | CTS_CTASUP |
| `Kind` (Total / Accounts / Formula) | CTS_CLASSE / CTS_FORMUL |
| `AccountFrom` / `AccountTo` | CTS_CT1INI / CTS_CT1FIM |
| `Sign` (+/−) | CTS_IDENTI |
| `Formula` (soma/subtração de códigos de linha da visão, sem outra sintaxe) | CTS_FORMUL |
| `Bold` / `Hidden` | |

As linhas `Accounts` somam o saldo das contas **analíticas** cujo código está na faixa, convertido pelo
sinal (`Nature` da conta). As linhas `Total` somam as filhas. As linhas `Formula` resolvem expressões do
tipo `L01 - L02 + L05`.

A migration **semeia** uma DRE e um Balanço padrão com estrutura típica (Receita Bruta, Deduções, Receita
Líquida, CMV/CPV, Lucro Bruto, Despesas Operacionais, Resultado Financeiro, Resultado antes do IR/CS,
IR/CS, Lucro Líquido; Ativo Circulante e Não Circulante, Passivo Circulante e Não Circulante, PL). As
faixas ficam **vazias**, porque cada plano de contas é diferente. O contador preenche.

### 8. Custo da venda

O Protheus credita estoque e debita CMV no 610, pelo custo do `SD2`. O SiagroB1 tem custo FIFO só no
**estoque fiscal** (`FiscalStock`), e ele não existe para toda saída.

**Decisão do usuário (22/10): o CMV é automático no 610, pelo custo FIFO do estoque fiscal.**

- O contexto 610 expõe `Line.FiscalStockCost`, que é o custo das camadas FIFO consumidas pela linha da
  saída. Com ele, o contador configura a sequência "débito CMV / crédito estoque".
- O estorno (630) usa o custo efetivamente devolvido às camadas, e não um recálculo.
- **Ordem na confirmação.** O consumo do estoque fiscal precisa acontecer **antes** da contabilização,
  dentro da mesma transação da confirmação do Documento de Saída. Se hoje ele roda depois, ou fora da
  transação (`CommitMode.Deferred`), o construtor reordena o `SalesInvoicesConfirmService` e cobre isso
  com teste.
- **Sem camada não se inventa custo.** Saída que não movimenta estoque fiscal, ou não tem camada (saldo
  insuficiente), tem custo zero e não gera linha. Em compensação, um aviso de "saída sem custo" aparece
  na simulação off-line e num relatório de conferência simples na tela de Apuração. Assim o contador
  vê o buraco antes de fechar o mês.
- **Pré-requisito operacional, não de código:** o saldo inicial do estoque fiscal precisa estar lançado
  antes de ativar o 610 com CMV num cliente. Isso fica registrado na tela de Lançamento Padrão como
  ajuda do código 610.
- A entrada (650) debita o estoque pelo mesmo valor que alimenta a camada FIFO, para que o estoque
  contábil e o fiscal batam. O construtor confere qual campo do item de entrada forma o custo da camada
  e expõe esse campo como fonte `Line.FiscalStockCost` no 650.

## Menus (módulo Contabilidade, `StandaloneOnly`)

Os cadastros existentes continuam: Plano de Contas, Centro de Custo, Item Contábil e Rateios.

Entram:

- Calendário Contábil
- Lançamento Padrão
- Lançamentos Contábeis
- Contabilização Off-line
- Apuração de Resultado
- Visão Gerencial
- Configurações Contábeis (a conta de apuração padrão e a periodicidade)
- Relatórios:
  - Razão
  - Diário
  - Balancete
  - DRE
  - Balanço

Também entram os direitos para o perfil ADMIN.

## Decisões (minhas, para revisão)

1. **Fontes fechadas no lugar de expressão ADVPL.** É mais seguro (nada executável configurado pelo
   usuário) e testável. O custo: um código novo de fonte exige deploy. Para "mínimo funcional" é o
   trade-off certo.
2. **Saldos calculados das linhas**, sem tabelas de saldo (CQ0–CQ9). Os volumes previstos (milhares de
   linhas por mês) cabem em consulta indexada por (conta, data) e (CC, data). Se um dia não couber, entra
   uma tabela de saldo mensal com reprocessamento, sem mudar a API.
3. **Lançamento recusado derruba a operação** quando há LP ativo. Sem LP, nada acontece.
4. **Estorno automático** quando o documento foi contabilizado e o código de estorno não está
   configurado.
5. **Rastro de origem por índice único** em vez de flag `DTLANC` em cada tabela.
6. **Parceiro na linha** do lançamento (fora da CT2), para razão por parceiro.
7. **Zeramento por conta + CC + Item** na apuração.
8. Numeração dos códigos 505, 515, 560, 564 e 565, e valores dos lotes.
9. Período sem calendário = aberto.

10. **Estorno só existe se houve contabilização.** O código de estorno (655, 665, 630, 635, 505, 515,
    527, 531, 564, 565, 561) só contabiliza se o evento original tem lançamento. Original não
    contabilizado → estorno é no-op, tanto no on-line quanto no off-line. Isso evita estorno sem
    contrapartida.
11. **Data de corte.** `ACCOUNTING_SETTINGS.CutoverDate`, que entra na migration da fase 1. Eventos com
    data contábil anterior ao corte nunca contabilizam, nem on-line nem off-line, porque já estão no
    saldo inicial. Corte vazio = módulo desligado: nenhum lançamento automático.
12. **Data contábil = data do evento** em todos os códigos (data do cancelamento, da baixa, do estorno),
    nunca a do documento original.
    - Se essa data cair em período fechado, a operação é recusada com a mensagem de período fechado.
    - **Exceção:** eventos impostos de fora, como o cancelamento de NF-e já homologado pela SEFAZ e o
      retorno de evento assíncrono, não podem ser recusados. Eles contabilizam no **primeiro dia do
      primeiro período aberto** posterior, com o histórico citando a data real.
13. **Filial.** Todo lançamento tem uma única filial e fecha débito = crédito dentro dela. Não há
    lançamento manual entre filiais nesta versão. A apuração "todas" gera **um documento por filial**,
    zerando por filial + conta + CC + Item, e o fechamento avalia "resultado não apurado" por filial.
14. **Exclusão de lançamento na conta e de transferência vira cancelamento lógico** (`CanceledAt/By`),
    não `Remove`. É uma migration da fase 2. Assim, o off-line e o estorno acham o evento e a data.
    As listas e o saldo da conta ignoram os cancelados.
15. **Quais títulos contabilizam no 500/510:**
    - **Contabilizam:** títulos **firmes** sem documento fiscal de origem — manual, washout, outras
      compras sem nota, retenções avulsas e adiantamentos.
    - **Não contabilizam no 500/510:**
      - títulos nascidos de Documento de Entrada ou de Saída, incluindo o complemento de ICMS e de
        valor, que já estão no 650/660 e no 610/620;
      - títulos **provisórios** de contrato e de fixação. Status ou nature provisório nunca
        contabiliza, nem na inclusão nem na baixa. O construtor confere o campo exato que distingue
        provisório de firme em `FinancialDocument`.

## Ordem de entrega sugerida (o construtor pode reordenar)

1. Plano de contas ampliado + calendário + lançamento manual + estorno + guarda de período + Razão e
   Balancete. **Já é uma contabilidade manual utilizável.**
2. Lançamento padrão + catálogo + contabilização on-line (Entradas, Saídas, Financeiro).
3. Contabilização off-line.
4. Apuração de resultado + fechamento + visão gerencial + DRE + Balanço + Diário.

## O que fica para quem constrói

- Layouts de tela (seguir os padrões atuais: lista com FilterBar, ObjectPage, Detail com seções), nomes
  de classes e ordem das tarefas.
- A lista exata de fontes de valor e de conta por contexto, a partir dos campos existentes:
  - `PurchaseInvoice`/`PurchaseInvoiceItem`: ICMS, PIS, COFINS, IBS/CBS, Funrural, retenções, frete,
    desconto, `GrandTotal`, `NetPayable`, `CteIcmsValue`;
  - `SalesInvoiceItem`: ICMS, PIS, COFINS, IBS/CBS, frete, desconto, `GrandTotal`, `NetTotal`;
  - `FinancialSettlement`;
  - `FinancialAccountEntry`;
  - `FinancialAccountTransfer`.
  
  IPI não existe no sistema.
- Onde exatamente pendurar a chamada on-line em cada serviço (lista de serviços no reconhecimento:
  `PurchaseInvoicesConfirmService`, `PurchaseInvoicesReverseConfirmService`, `PurchaseInvoicesCancelService`,
  `SalesInvoicesConfirmService` e afins, `FinancialDocumentsManualCreateService`, `FinancialDocumentsSettleService`,
  `FinancialDocumentsReverseSettlementService`, `FinancialAccountEntriesCreate/DeleteService`,
  `FinancialAccountTransfersCreate/DeleteService`, `FinancialAdvancesCreateService`, `FinancialDocumentsApplyAdvanceService`,
  washout e outras compras).
- Testes no projeto `SiagroB1.Application.Tests`, com TDD por invariante (balanço, período, imutabilidade,
  dupla contabilização, rateio e arredondamento, estorno automático, apuração por CC).
