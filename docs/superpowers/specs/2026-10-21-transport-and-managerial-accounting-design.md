# Transportadora (CT-e + MDF-e) e apuração de resultado por centro de custo

Data: 2026-10-21

## Intenção

Um prospect tem **duas empresas PJ com contabilidade**: uma **transportadora** (o negócio
principal) e uma **propriedade rural** (agricultura e gado). Ele quer apurar o **resultado por
centro de custo e o resultado geral**. Na transportadora, isso significa saber se cada caminhão,
cliente e rota dá lucro. As duas empresas usariam o SiagroB1 em modo **STANDALONE**.

O usuário decidiu:

- A apuração é **contábil**, no modelo do Protheus/SAP B1: **um único razão** em que cada linha
  carrega dimensões gerenciais. Os relatórios oficiais (razão, balancete, ECD) ignoram as
  dimensões, e o DRE gerencial abre o resultado por elas.
- As duas empresas seguem **o mesmo modelo**, para o usuário aprender uma vez só. Muda apenas
  o significado de cada dimensão.
- **Escopo atual:** a transportadora, com emissão de **CT-e e MDF-e**, e a contabilidade gerencial.
- **Fora do escopo:** LCDPR, produção agrícola e pecuária (rebanho, ativo biológico,
  CPC 29), consolidação do grupo e eliminação das operações entre as empresas.

O usuário dispensou as perguntas de brainstorming. **Todas as decisões abaixo que não estão na
lista acima são minhas** e estão marcadas como "(minha decisão)".

## Por que agora e o que é sucesso

Isto abre um segmento novo de clientes (transportadoras) e antecipa o módulo contábil, que já
era o próximo grande desenvolvimento do roadmap. Esse módulo foi pensado para a Tagui Brasil, no
modelo SAP B1, só em STANDALONE e com ECD/ECF no fim.

O resultado conta como sucesso quando, no fim do mês, o dono da transportadora abre um DRE e vê:

- o resultado da empresa;
- o mesmo DRE aberto **por caminhão/conjunto**, **por cliente** e **por unidade de negócio**;
- receita vinda dos CT-e emitidos no próprio SiagroB1 e custos (diesel, manutenção, pneus,
  pedágio, motorista, administrativo rateado) caindo no caminhão certo.

Tudo isso precisa sair de um razão que depois alimenta os relatórios oficiais, sem uma segunda
escrituração.

### Anti-objetivos (falha mesmo que funcione)

- Uma "contabilidade gerencial" paralela, que não bate com o razão oficial.
- O operador ter que redigitar o centro de custo em cada lançamento contábil. O centro tem que
  vir do documento de origem.
- Quebrar ou mudar a tela de alguém em modo SAPB1 (Yokotobi) ou de STANDALONE que não usa nada
  disto (MH Agro, CEAGUI). Tudo novo fica atrás de STANDALONE e de chaves de configuração.

## Decomposição em sub-projetos

São sete sub-projetos independentes. Cada um tem o próprio spec, plano, branch e merge. Este
documento fixa a arquitetura comum e a ordem. Os detalhes de cada um vêm no brainstorming do
próprio sub-projeto.

| # | Sub-projeto | Depende de | Entrega para o usuário |
|---|---|---|---|
| 1 | Centro de Custo + Item Contábil + rateio nos documentos (modelo Protheus) | — | Cada documento financeiro/fiscal leva CC/Item ou rateio |
| 2 | Cadastros de transporte | 1 | Veículo, conjunto, motorista e dados ANTT completos; veículo = centro de custo |
| 3 | Emissão de CT-e (modelo 57, versão 4.00) | 2 | Faturamento do frete com título a receber e dimensões |
| 4 | Emissão de MDF-e (modelo 58, versão 3.00) | 3 | Manifesto, encerramento e inclusão de condutor |
| 5 | Núcleo contábil | 1 | Plano, períodos, lançamento manual, estorno |
| 6 | Determinação de contas + contabilização automática | 3, 5 | Razão alimentado pelos documentos, com as dimensões |
| 7 | Relatórios contábeis e DRE por dimensão | 6 | Razão, balancete, diário e DRE gerencial |

**A ordem é minha decisão:** 1 → 2 → 3 → 4 → 5 → 6 → 7. Os sub-projetos 5 a 7 podem andar em paralelo aos 3 e 4,
porque só se encontram no 6.

- As **dimensões vêm primeiro** porque, se o CT-e e os títulos nascerem sem elas, todo o
  histórico fica sem centro de custo e o primeiro DRE sai vazio.
- O **CT-e vem antes da contabilidade** porque é a receita do prospect e o que ele vê na
  demonstração. Ele também já gera dado (título com dimensão) que a contabilização do
  sub-projeto 6 vai processar.

## Arquitetura comum

### A. Entidades contábeis no modelo do Protheus (CTT / CTD) — sub-projeto 1

**Revisão de 21/10 (decisão do usuário).** O modelo de "5 dimensões genéricas" (SAP B1) da
primeira versão foi substituído pela estrutura do SIGACTB do Protheus, que o usuário prefere:

- **Plano de Contas** (CT1, `LEDGER_ACCOUNTS`): visão contábil geral.
- **Centro de Custo** (CTT, `COST_CENTERS`): visão por departamento ou área.
- **Item Contábil** (CTD, novo `ACCOUNTING_ITEMS`): detalhe por projeto, cliente, fornecedor ou
  veículo.
- **Classe de Valor (CTH) e as entidades 05–09 (CV0) ficam fora** por decisão do usuário. Não
  devem ser criadas colunas para elas.

Na transportadora, a sugestão é: CC = área (Operação, Oficina, Administrativo) e Item = veículo ou
conjunto. Na rural, mais tarde: CC = atividade e Item = safra ou talhão.

#### Módulo Contabilidade no menu (pedido do usuário, 21/10)

O menu principal ganha o módulo raiz **Contabilidade**, logo depois de Financeiro, com Plano de
Contas, Centro de Custo, Item Contábil e Rateios Pré-configurados.

- Plano de Contas e Centro de Custo **saem de Cadastros** e vão para Contabilidade.
- O sub-projeto 5 (lançamentos, períodos, razão, DRE) também entra nesse módulo.

#### Cadastros (CTT e CTD)

`COST_CENTERS` (já existe) e `ACCOUNTING_ITEMS` (novo) têm a mesma forma:

| Campo | Protheus | Regra |
|---|---|---|
| `Code` (VARCHAR 20), `Name` | `_CUSTO`/`_ITEM`, `_DESC01` | código em maiúsculas, sem espaços |
| `Class` (Synthetic/Analytic) | `_CLASSE` 1/2 | só **analítico** recebe documento ou rateio |
| `ParentCode` | `_CCSUP`/`_ITSUP` | o pai precisa ser **sintético**; não pode haver ciclo |
| `Inactive` | `_BLOQ` | bloqueado não aceita código **novo** em documento |
| `ValidFrom`, `ValidTo` (opcionais) | `_DTEXIS`/`_DTEXSF` | o documento precisa estar dentro da vigência |

Fica fora: código reduzido, contas de lucros e perdas e de ponte, e a amarração CC→Item
(`CTT_ACITEM`), que entram com o núcleo contábil se forem necessários.

#### Amarração pela conta contábil (CT1_ACCUST / CT1_CCOBRG)

`LEDGER_ACCOUNTS` ganha:

- `AcceptsCostCenter`, `RequiresCostCenter`;
- `AcceptsAccountingItem`, `RequiresAccountingItem`.

O padrão é aceitar e não exigir.

A obrigatoriedade é **só por conta contábil** (decisão do usuário). Não há mais parâmetro da
empresa, e `COMPANY_SETTINGS` sai. A validação roda em toda linha que tem `LedgerAccountCode`:

- a conta exige CC e ele está vazio: "A conta {conta} exige centro de custo (linha …).";
- a conta não aceita CC e ele foi informado: "A conta {conta} não aceita centro de custo (linha …).";
- as mesmas duas regras valem para o Item Contábil.

Uma linha sem conta contábil não é validada agora. Ela passa a ser quando a contabilização
automática (sub-projeto 6) determinar a conta.

Quando a validação roda:

- na **confirmação feita pelo usuário** (documento de saída e de entrada);
- na **criação** de título manual, adiantamento e lançamento na conta.

Fluxos automáticos, como o faturamento de carga, a devolução e a confirmação depois da
autorização da NF-e, não são bloqueados. Essa regra foi mantida da primeira versão.

Um rateio satisfaz "exige CC" se todas as linhas dele têm CC, e "exige Item" se todas as linhas
têm item.

#### Rateio pré-configurado (CTJ)

- `DISTRIBUTION_RULES` (código, nome, inativa) mantém **versões por vigência**. Uma versão nova
  só pode começar depois de hoje, e versões vigentes ou passadas não se alteram (regra mantida).
- Cada linha tem `CostCenterCode`, `AccountingItemCode` e `Percentage`:
  - os códigos precisam ser **analíticos e ativos**, e pelo menos um dos dois tem que estar
    preenchido;
  - a soma das linhas é 100%;
  - não pode repetir o mesmo par CC+Item.
- Exemplo: diesel 60% Operação/CAM01 e 40% Operação/CAM02.
- O documento recebe `DistributionRuleCode` **em vez de** CC e Item. Os dois modos são
  exclusivos: informar a regra limpa e bloqueia CC e Item.
- A expansão em linhas usa a versão vigente na **data do documento** (regra mantida).
- A conta contábil não fica na regra. Ela vem do documento, porque o CTJ do Protheus tem conta
  mas aqui ela é determinada pelo documento (minha decisão).

#### Onde os campos ficam

Os campos são `CostCenterCode`, `AccountingItemCode`, `DistributionRuleCode` e
`LedgerAccountCode`, nos mesmos documentos da primeira versão:

- linhas e cabeçalho do documento de saída e do documento de entrada;
- `FinancialDocument`, que inclui título manual e adiantamento;
- `FinancialAccountEntry`;
- `Usage` (natureza), como padrão.

O parceiro ganha `DefaultCostCenterCode` e `DefaultAccountingItemCode`. No Protheus o item
contábil costuma ser o próprio cliente ou fornecedor.

O preenchimento padrão segue a ordem: digitado → cabeçalho → parceiro → natureza. O padrão do
veículo entra no sub-projeto 2.

Continua **apenas STANDALONE**. Os cadastros e serviços são registrados nos dois modos, mas não
fazem nada em SAPB1.

### B. Cadastros de transporte — sub-projeto 2

O `Truck` e o `TruckDriver` existentes ganham os campos exigidos pelo CT-e/MDF-e. **Não** serão
criadas entidades paralelas (minha decisão), porque o caminhão da balança e o da transportadora
são o mesmo objeto.

- **Veículo** (`Truck`):
  - tipo (tração ou reboque), RENAVAM, tara, capacidade em kg e m³;
  - tipo de rodado e de carroceria (tabelas do MDF-e), UF;
  - propriedade (própria ou de terceiro). Se for de terceiro: proprietário (parceiro),
    RNTRC e tipo de proprietário (TAC agregado, independente ou outros);
  - **centro de custo padrão**. Ao cadastrar um veículo próprio, o sistema oferece criar o
    centro de mesmo código na dimensão configurada como "Veículo".
- **Conjunto** (`VEHICLE_SETS`): um tracionador mais até três reboques, com motorista padrão.
  Os custos podem ir para o conjunto ou para o cavalo, conforme a configuração.
- **Motorista** (`TruckDriver`): o CPF passa a ser obrigatório na emissão, mais CNH e validade.
  O motorista também pode ser ligado a um parceiro, para pagar frete a terceiro e a autônomo.
- **Filial** (emitente): RNTRC da empresa e dados de seguro da carga (responsável, seguradora,
  apólice, averbação), que aparecem no MDF-e.
- `Municipality` (IBGE) já existe e é a base de origem, destino e percurso.

### C. Emissão de CT-e e MDF-e — sub-projetos 3 e 4

Reaproveita o desenho da NF-e STANDALONE (`SiagroB1.Fiscal` + `Application/Services/Nfe`):

- **Biblioteca:** pacotes Zeus irmãos do que já usamos para a NF-e (`Zeus.Net.NFe.NFCe`):
  `Zeus.Net.CTe` e `Zeus.Net.MDFe`, se existirem na mesma linha de versão.
  - **Validar na fase de plano** se esses pacotes existem e estão mantidos. Se não, montar o XML
    próprio e reaproveitar só a assinatura (`System.Security.Cryptography.Xml`) e o certificado.
  - Os XSDs oficiais entram em `SiagroB1.Fiscal/Schemas`, como os da NF-e.
- **O que se reaproveita:**
  - certificado da filial (`BranchNfeSettings`, `CertificateLoader`);
  - ambiente;
  - padrão de séries e numeração com reserva sob trava (`BranchNfeSeries` /
    `NfeNumberReservationService`). Ganha o modelo 57 ou 58 na chave, porque CT-e e MDF-e têm
    numeração própria (minha decisão);
  - as bases de emitir, tratar retorno, cancelar e consultar;
  - a fila de e-mail;
  - o DANFE em FastReport, como modelo para o DACTE e o DAMDFE.
- **CT-e (sub-projeto 3):**
  - Documento novo `TransportDocuments` (o "Conhecimento"), com:
    - tomador (remetente, expedidor, recebedor, destinatário ou outro);
    - os quatro papéis como parceiros;
    - município de início e de fim;
    - **NF-e transportadas por chave**, que se pode buscar das NF-e de saída do próprio
      SiagroB1 quando a mesma empresa vende e transporta. Ver o contra-ponto em "Fora".
  - Produto predominante, quantidades, valor da carga, valor da prestação e componentes (frete
    peso, pedágio, outros).
  - ICMS pela natureza de operação (`USAGES`): CFOP pela UF de início × fim e CST
    00/20/40/60/90, mais o simples nacional e o ICMS do tomador quando há substituição
    tributária. É a mesma regra do CT-e de entrada, só que espelhada.
  - Modal rodoviário (RNTRC) com veículo/conjunto e motorista.
  - **Dimensões:** o padrão é veículo → Dim2, tomador → Dim3 e natureza → Dim1.
  - Ao autorizar, gera **título a receber** contra o tomador, pelo caminho de
    `FinancialDocument`, carregando as dimensões.
  - Cancelamento, CC-e e DACTE.
  - **Fora:** CT-e OS (modelo 67), CT-e de anulação e de substituição, CT-e complementar,
    multimodal, subcontratação e redespacho. Essas operações entram depois, se o prospect pedir.
- **MDF-e (sub-projeto 4):**
  - O manifesto tem veículo de tração, reboques e condutores; UF de carregamento, de
    descarregamento e o percurso; **CT-e (ou NF-e, para carga própria) por município de
    descarga**; seguro; produto predominante e totais.
  - Eventos de encerramento, cancelamento e inclusão de condutor.
  - Consulta de MDF-e não encerrados (a SEFAZ rejeita um manifesto novo enquanto houver outro
    aberto para o mesmo veículo e UF).
  - DAMDFE.
  - **Fora:** CIOT, vale-pedágio e pagamento de frete (informações de pagamento): campos
    opcionais numa fase posterior. Também ficam fora os modais que não são rodoviários.

### D. Contabilidade — sub-projetos 5, 6 e 7

Segue o desenho já acordado no roadmap contábil (modelo SAP B1, só STANDALONE). Este
documento só acrescenta as dimensões:

- **Plano de contas:** amplia `LEDGER_ACCOUNTS` com:
  - hierarquia (sintética/analítica, nível, pai) e natureza devedora/credora;
  - código do plano referencial da RFB;
  - **linha do DRE** (receita bruta, deduções, custo, despesa operacional, financeiro,
    impostos sobre o lucro), que define onde a conta entra no DRE gerencial (minha decisão: o
    DRE vem do plano, não de uma estrutura separada).
- **Lançamento** (`JOURNAL_ENTRIES` / `JOURNAL_ENTRY_LINES`):
  - linhas com conta, débito, crédito e parceiro;
  - **`CostCenterCode` e `AccountingItemCode`** (como CT2_CCD/CCC e ITEMD/ITEMC). O rateio do
    documento é **expandido na gravação** em linhas por CC+Item, com o percentual aplicado. O razão
    guarda só CC e Item, nunca a regra, para que o DRE não dependa da vigência dela (minha decisão);
  - origem (tipo e chave do documento);
  - invariantes: débito = crédito; imutável depois de lançado; cancelamento = lançamento
    inverso; período bloqueado não aceita lançamento.
- **Sem "tipo de saldo" separado** (minha decisão). O Protheus tem saldo real/gerencial/orçado,
  mas o modelo SAP B1 resolve com dimensões no mesmo lançamento, e isso basta para o pedido.
  Orçado fica para depois.
- **Contabilização automática** (sub-projeto 6): na autorização do CT-e, na confirmação do
  documento de entrada e de saída, na baixa, no lançamento na conta e na transferência. As
  dimensões do documento passam para as linhas de resultado. Linhas patrimoniais (banco,
  clientes, fornecedores) não levam dimensão.
- **DRE gerencial** (sub-projeto 7):
  - período livre e uma ou mais dimensões escolhidas como colunas (ou filtro);
  - uma coluna "Sem centro" para o que não foi rateado;
  - coluna de total igual ao DRE oficial do período, o que é a prova de que não existe
    escrituração paralela;
  - margem por linha e percentual sobre a receita;
  - tela no frontend (tabela com expansão por conta) mais PDF em FastReport e Excel.

## Restrições

- Somente modo **STANDALONE** e gate positivo (`ErpMode.IsStandalone`). Em SAPB1 nenhuma
  tela, menu ou coluna nova aparece.
- A emissão de CT-e e MDF-e é ligada por filial (chaves "Emite CT-e" e "Emite MDF-e"), como a
  "Emite NF-e".
- Toda migration roda em **todas as empresas** da COMMON, porque o Web recusa subir com
  migration pendente. As migrations devem ser aditivas; os centros de custo existentes viram
  analíticos sem pai.
- Identificadores em inglês e textos de tela em pt-BR.
- Homologação na SEFAZ antes de qualquer produção. A SEFAZ autorizadora do CT-e e do MDF-e
  depende da UF do emitente (SVRS para várias UFs, SP própria no CT-e). Isso é **pendente**:
  qual é a UF do prospect.

## O que fica para quem constrói

- Leiautes de tela, desde que sigam os padrões atuais: lista com FilterBar, ObjectPage para
  documento, Detail com seções.
- O formato do DACTE e do DAMDFE, desde que siga o manual oficial.
- A ordem interna de tarefas de cada sub-projeto e o nome das classes.

## Pendências que dependem do prospect (não bloqueiam o sub-projeto 1)

1. UF e regime tributário da transportadora (define SEFAZ e ICMS do CT-e).
2. Plano de contas atual e data de corte com saldos iniciais (necessário no sub-projeto 5).
3. Se o custo vai para o **conjunto** ou para o **cavalo**.
4. Se há agregados ou terceiros (muda o MDF-e e o pagamento de frete).
