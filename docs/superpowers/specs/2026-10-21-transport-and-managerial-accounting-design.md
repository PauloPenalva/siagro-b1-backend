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
| 1 | Dimensões gerenciais e rateio nos documentos | — | Cada documento financeiro/fiscal leva centro(s) de custo |
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

### A. Dimensões (modelo SAP B1: OPRC/ODIM + OOCR) — sub-projeto 1

- **Cinco dimensões por empresa** (`ACCOUNTING_DIMENSIONS`: número 1–5, nome, ativa). O
  nome é configurável. Sugestão para a transportadora:
  - 1 = Unidade de negócio;
  - 2 = Veículo/Conjunto;
  - 3 = Cliente/Rota.

  A rural, no futuro: Atividade / Safra / Talhão.
- `COST_CENTERS` já existe (`Code`, `Name`, `Inactive`) e ganha **`DimensionNumber`** (1–5).
  Um centro de custo pertence a uma dimensão, como o OPRC. Os centros atuais vão para a
  dimensão 1 por migration.
  - Em SAPB1 o cadastro continua vindo do SAP, e nada disso aparece.
- **Regra de distribuição** (`DISTRIBUTION_RULES` + `DISTRIBUTION_RULE_LINES`): código, nome,
  dimensão, ativa, e as linhas (centro de custo + percentual, somando 100%).
  - Um centro sozinho funciona como regra implícita de 100%. Assim, todo campo de dimensão
    aceita **centro ou regra**, como no SAP B1, onde OcrCode recebe os dois.
  - Rateio por km, hora ou área calculado automaticamente fica **fora** (minha decisão). Os
    percentuais são fixos e revistos pelo usuário. A regra tem vigência (`ValidFrom`), para que
    mudar o percentual não reescreva o passado.
- **Onde as dimensões são gravadas** (minha decisão): cinco colunas `Dim1Code`…`Dim5Code`
  (centro ou regra), no mesmo formato em todo lugar:
  - linha do documento de saída e do documento de entrada (`SalesInvoiceItem` já tem
    `CostCenterCode`, que vira `Dim1Code`);
  - título financeiro (`FinancialDocument`), lançamento na conta (`FinancialAccountEntry`)
    e, no sub-projeto 3, o CT-e.
  - O cabeçalho do documento tem um **padrão** que a linha herda e pode trocar.
- **Preenchimento padrão** (minha decisão), em ordem:
  1. o que o usuário digitou;
  2. o padrão do parceiro, quando a dimensão é Cliente;
  3. o padrão do veículo, no CT-e e no MDF-e;
  4. o padrão da natureza de operação (`USAGES`).
- Duas chaves por empresa ("Exige centro de custo", "Exige conta contábil") tornam esses campos
  **obrigatórios**. Desligadas, que é o padrão, nada muda para quem já usa o sistema.

#### Detalhes do sub-projeto 1 (todos minha decisão, a partir da revisão do desenho)

- **O documento guarda só o código, não o rateio.** Uma regra de distribuição é **versionada
  por `ValidFrom`**, e uma versão usada por algum documento não pode mais ser editada; mudar o
  percentual cria uma versão nova. A contabilização (sub-projeto 6) expande a regra pela
  **versão vigente na data do documento**, não na data do lançamento contábil. Assim um
  documento antigo contabilizado depois sai com o rateio da época, sem tabela de rateio por linha.
- **Um código só por empresa para centro e regra**: o mesmo código não pode existir como
  centro e como regra (validado nos dois cadastros). Cada código pertence a uma dimensão, e o
  campo `DimNCode` só aceita códigos da dimensão N. Não há FK (a coluna aceita os dois tipos);
  a validação fica no serviço, e o value help mostra centros e regras juntos, com uma coluna Tipo.
- **Papel da dimensão:** `ACCOUNTING_DIMENSIONS` ganha `Role` (Nenhum / Veículo / Parceiro),
  com no máximo uma dimensão por papel. É assim que o sistema sabe qual dimensão recebe o padrão
  do parceiro e qual recebe o do veículo, sem número fixo.
- **Documentos que ganham dimensões no sub-projeto 1:** linhas do documento de saída e do de
  entrada, `FinancialDocument` (o que inclui título manual e adiantamento) e
  `FinancialAccountEntry`. O CT-e entra no sub-projeto 3.
  - **Ficam de fora:** contratos e fatura de armazenagem.
  - **Transferência entre contas não leva dimensão**, porque é só patrimonial.
- **Título nascido de documento:** o resultado da operação é contabilizado pelas **linhas do
  documento de origem**, não pelo título, que é patrimonial. Por isso o título só herda o
  padrão do cabeçalho da origem, como informação, e não carrega rateio por linha. As dimensões do
  título só entram no DRE quando ele **não** tem documento de resultado por trás (título manual,
  adiantamento sem contrato, juros e descontos na baixa). A baixa e o lançamento na conta herdam
  do título.
- **Conta contábil nos documentos (decisão do usuário, 21/10):** `LedgerAccountCode` entra nos
  mesmos documentos e linhas que recebem as dimensões. `SalesInvoiceItem` já tem esse campo. O
  padrão vem da natureza de operação, e o código é validado contra contas analíticas ativas
  (`AllowsPosting`). Na contabilização (sub-projeto 6), a conta informada prevalece sobre a
  determinação automática.
- **Obrigatoriedade por parâmetro da empresa (decisão do usuário, 21/10):** a tabela de uma linha
  `COMPANY_SETTINGS`, no banco da empresa, tem duas chaves: **"Exige centro de custo"**, que
  exige a **dimensão 1**, e **"Exige conta contábil"**. As duas valem para todos os documentos
  com dimensões e são editadas na tela "Parâmetros da Empresa".
  - Bloqueia **confirmar ou autorizar**, nunca salvar o rascunho.
  - Documentos já confirmados antes de ligar a obrigatoriedade continuam válidos e não são
    revalidados (evita a armadilha do campo readonly+required em linhas antigas).
- **Apenas STANDALONE (decisão do usuário, 21/10).** Todo o desenho, dimensões incluídas, só
  existe em STANDALONE. Não há mapeamento para o OcrCode do SAP nem nada a preservar em SAPB1. Em
  SAPB1 as colunas novas ficam no banco (o schema é um só), mas nenhum serviço, tela ou menu as usa.
  `SalesInvoiceItem.CostCenterCode` hoje não é lido por nenhum serviço (só por um teste), então
  **é renomeado para `Dim1Code`** na migration do sub-projeto 1.
- **Padrões:** `USAGES` ganha os cinco campos de padrão no sub-projeto 1. O padrão do parceiro
  (`BusinessPartner`, para a dimensão de papel Parceiro) também entra no 1. O padrão do veículo
  entra no sub-projeto 2.
- **Cada empresa tem o próprio plano, dimensões e regras**, no próprio banco. "Mesmo modelo"
  quer dizer a mesma tela e a mesma lógica, não uma tabela compartilhada na COMMON. Copiar a
  configuração de uma empresa para outra fica para depois.

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
  - **`Dim1Code`…`Dim5Code`**: centro ou regra, a regra é **expandida na gravação** em linhas
    por centro, com o percentual aplicado. O razão guarda só centros, nunca regras, para que o
    DRE não dependa da vigência da regra (minha decisão);
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
  migration pendente. As migrations devem ser aditivas, com centros existentes migrados para a
  dimensão 1.
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
