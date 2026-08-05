# Documento de saída de propósito geral — design

Data: 2026-08-04
Status: aprovado para planejamento
Modo alvo: **STANDALONE** (SAPB1 preparado por abstração, não implementado)

## Problema

Hoje o documento de saída (`SALES_INVOICES`) só nasce do faturamento de romaneio
(`ShipmentBillingCreateSalesInvoiceService`). Não existe caminho para lançar um documento
avulso, e por consequência não existe manutenção fiscal de contrato: não dá para emitir
complemento de preço de um contrato PAF fixado depois da entrega, corrigir uma quebra
apurada no destino, lançar uma devolução que não venha de romaneio, nem emitir uma saída
que não tenha contrato nenhum.

Duas ausências estruturais sustentam isso:

1. **Não existe natureza de operação.** `DOC_TYPES` é numerador (série + próximo número por
   filial), não natureza. Não há CFOP em lugar nenhum do modelo.
2. **A linha do documento não tem dado fiscal.** `SALES_INVOICES_ITEMS` tem quantidade,
   preço e unidade — nenhum campo de CFOP, NCM, CST, base, alíquota ou valor de imposto.

Além disso, `SALES_INVOICES` assume carga: peso bruto, peso líquido, caminhão, transportadora.
Um complemento de preço não tem nada disso, e hoje não há como expressar essa diferença.

O que **já existe** e estava sem uso: `SALES_CONTRACTS_ALLOCATIONS.PriceDifference` apura, por
contrato, a diferença entre o preço faturado e o preço do contrato, documentada como "base
para NF complementar / desconto financeiro decididos manualmente pelo usuário". A apuração do
complemento de preço está pronta desde a feature de alocação — falta o documento que a
materializa.

## Escopo

### Entra

- Cadastro de **natureza de operação** (`USAGES`), local, com CFOP de saída e os efeitos que
  a operação produz no contrato.
- Criação **avulsa** de documento de saída, com os campos obrigatórios reagindo à natureza
  escolhida.
- **Campos fiscais informados** na linha do documento (CFOP, NCM, CST/base/alíquota/valor de
  ICMS, PIS e COFINS).
- **Centro de custo e conta contábil na linha** do documento.
- **UF na filial** (`BRANCHS`), sem a qual não se escolhe entre CFOP dentro e fora do estado.
- Aplicação dos efeitos da natureza sobre o saldo e o valor do contrato, na confirmação, e
  seu estorno no cancelamento.

### Não entra

- **Motor de cálculo de tributação.** Os impostos são informados pelo usuário. Decisão
  explícita do usuário em 04/08/2026, revertendo uma escolha anterior. O motor encaixa depois
  sem refazer o documento: as colunas de imposto já ficam na linha, e o motor apenas passa a
  preencher o que hoje é digitado.
- **Emissão de NF-e.** Entregável separado, e **somente no modo STANDALONE** — em SAPB1 a
  emissão, a tributação e as naturezas ficam no ERP do cliente.
- **Documento de entrada.** Sub-projeto seguinte, reusa `USAGES`.
- **Financeiro** (títulos, baixas, movimento bancário). Sub-projeto posterior.
- **Implementação SAPB1 de `IUsage`.** Ver "Preparação para SAPB1".
- CFOP de entrada, importação e exportação. Sem operação que os leia agora.

## Decisões de design

### Estender `SALES_INVOICES` em vez de criar entidade nova

O documento avulso mora na mesma tabela do faturamento de romaneio. Reaproveita numeração por
filial, trava de duplicidade de NF-e, comentários, log de alterações e conferência de entrega
— tudo já construído e rodado. E deixa **um só** documento de saída para a NF-e e o financeiro
lerem depois.

As alternativas foram descartadas: uma entidade genérica nova obrigaria a migrar faturamento,
conferência, trava de NF-e e relatórios — muito risco para pouco ganho; e uma entidade
separada só para avulsos criaria dois documentos de saída, forçando NF-e, financeiro e
relatórios a lerem de dois lugares.

O custo aceito é que campos de carga (peso, caminhão, transportadora) ficam nulos em documento
de serviço ou complemento. É a natureza de operação que passa a dizer o que é obrigatório.

### Efeito no contrato: enum, não booleano

Devolução e ajuste de quebra **devolvem** saldo ao contrato; a venda **consome**. Um booleano
`AffectsContractBalance` não expressa direção. Por isso dois enums:

- `ContractBalanceEffect`: `None` | `Consume` | `Restore`
- `ContractValueEffect`: `None` | `Add` | `Subtract`

Nenhum dos dois cria mecanismo novo: ambos são materializados como linha no ledger
`SALES_CONTRACTS_ALLOCATIONS`, que já é a fonte única do consumo de contrato. Ver
"Efeito no contrato" em Regras.

As quatro operações do escopo caem no modelo assim:

| Operação | Saldo | Valor | Contrato | Quantidade | Peso |
|---|---|---|---|---|---|
| Complemento de preço | `None` | `Add` | obrigatório | não | não |
| Ajuste de quantidade / quebra | `Restore` | `Subtract` | obrigatório | sim | sim |
| Devolução / recusa | `Restore` | `Subtract` | obrigatório | sim | sim |
| Saída avulsa sem contrato | `None` | `None` | não | sim | não |

O complemento de preço é o caso que valida o desenho: documento só de valor, sem quantidade e
sem peso.

### Uma tabela, não duas

`USAGES` guarda identidade fiscal (nome, CFOP) e efeito de negócio (os enums e as
obrigatoriedades) na mesma linha.

Em modo SAPB1 a identidade fiscal viria do `OUSG` e só os efeitos seriam do Siagro, o que
pediria duas tabelas. Como SAPB1 não é o alvo agora, a segunda tabela seria estrutura sem
consumidor. **Adiamento consciente**: quando SAPB1 entrar, o custo é uma migration que move as
colunas de CFOP para fora e rechaveia `USAGES` pelo `OUSG.ID` (int) — ver "Preparação para
SAPB1".

### Sem FK para os cadastros mestres

`UsageCode`, `CostCenterCode` e `LedgerAccountCode` são gravados **sem chave estrangeira**,
validados no serviço.

Centro de custo e conta contábil já são dual-mode: em SAPB1 as tabelas locais ficam vazias e o
dado vem de `OPRC`/`OACT`. Uma FK obrigatória para tabela local vira INNER JOIN e zera a
coleção inteira nesse modo. `UsageCode` segue a mesma regra por antecipação: se em SAPB1 o
usage vier do `OUSG`, uma FK para `USAGES` quebra pelo mesmo motivo, e o custo de validar no
serviço é idêntico.

### Sem colunas sem leitor

Não entram flags de estoque nem de financeiro na natureza de operação, mesmo sendo baratas de
adicionar agora. Coluna morta com default já custou caro nesta base. Elas entram junto com o
sub-projeto que as lê.

## Modelo de dados

### `USAGES` (nova)

| Coluna | Tipo | Observação |
|---|---|---|
| `Code` | `INT IDENTITY` PK | Int, e não string, para casar com `OUSG.ID` depois |
| `Name` | `VARCHAR(200) NOT NULL` | |
| `Description` | `VARCHAR(200) NULL` | |
| `CfopOutgoingInState` | `VARCHAR(4) NULL` | Operação dentro do estado |
| `CfopOutgoingOutState` | `VARCHAR(4) NULL` | Operação interestadual |
| `ContractBalanceEffect` | `INT NOT NULL DEFAULT 0` | `None`/`Consume`/`Restore` |
| `ContractValueEffect` | `INT NOT NULL DEFAULT 0` | `None`/`Add`/`Subtract` |
| `RequiresContract` | `BIT NOT NULL DEFAULT 0` | |
| `RequiresQuantity` | `BIT NOT NULL DEFAULT 1` | |
| `RequiresWeight` | `BIT NOT NULL DEFAULT 0` | |
| `Inactive` | `BIT NOT NULL DEFAULT 0` | |

### `BRANCHS` (alteração)

| Coluna | Tipo | Observação |
|---|---|---|
| `StateCode` | `VARCHAR(2) NULL` | UF da filial. Sem FK para `STATES`, coerente com o restante |

Nulável porque as filiais existentes não têm o dado. A tela de filial passa a exigir, e a
resolução de CFOP trata ausência como erro de negócio explícito, não como silêncio.

### `SALES_INVOICES` (alteração)

| Coluna | Tipo | Observação |
|---|---|---|
| `UsageCode` | `INT NULL` | Sem FK. Obrigatório na criação; nulável por causa do legado |

**Backfill obrigatório na migration.** Documento existente nasceu de romaneio e não tem usage.
Deixar nulo e marcar o campo como obrigatório na tela trava a edição de todo registro legado —
armadilha já vivida nesta base (campo readonly + required sem backfill). A migration cria um
usage semente ("Venda de grãos", `Consume`/`Subtract`, exige contrato, quantidade e peso) e
preenche todos os documentos existentes com ele.

### `SalesContractAllocationOrigin` (alteração)

Valor novo `FiscalAdjustment = 5`, identificando as linhas de ledger criadas por documento
avulso. Nenhuma coluna nova em `SALES_CONTRACTS_ALLOCATIONS` — `Volume` e `PriceDifference`
já cobrem os dois efeitos.

### `SALES_INVOICES_ITEMS` (alteração)

| Coluna | Tipo | Observação |
|---|---|---|
| `Cfop` | `VARCHAR(4) NULL` | Resolvido do usage no momento da gravação e congelado como histórico |
| `Ncm` | `VARCHAR(8) NULL` | Informado |
| `CstIcms` | `VARCHAR(3) NULL` | |
| `IcmsBase` | `DECIMAL(18,2) DEFAULT 0` | |
| `IcmsRate` | `DECIMAL(5,4) DEFAULT 0` | |
| `IcmsValue` | `DECIMAL(18,2) DEFAULT 0` | |
| `CstPis` | `VARCHAR(3) NULL` | |
| `PisBase` | `DECIMAL(18,2) DEFAULT 0` | |
| `PisRate` | `DECIMAL(5,4) DEFAULT 0` | |
| `PisValue` | `DECIMAL(18,2) DEFAULT 0` | |
| `CstCofins` | `VARCHAR(3) NULL` | |
| `CofinsBase` | `DECIMAL(18,2) DEFAULT 0` | |
| `CofinsRate` | `DECIMAL(5,4) DEFAULT 0` | |
| `CofinsValue` | `DECIMAL(18,2) DEFAULT 0` | |
| `CostCenterCode` | `VARCHAR(10) NULL` | Sem FK |
| `LedgerAccountCode` | `VARCHAR(20) NULL` | Sem FK |

O CFOP é **gravado na linha**, não resolvido em tempo de leitura: se o cadastro do usage mudar
depois, o documento já emitido não pode mudar junto.

Totais de imposto do cabeçalho ficam `[NotMapped]` calculados a partir das linhas, seguindo o
padrão de `TotalInvoiceItems`. Não há coluna persistida de total — evita drift, ao custo de
não poder `$filter`/`$orderby` por esses campos, limitação que o documento já tem hoje.

## Regras

### Resolução do CFOP

Compara `BRANCHS.StateCode` da filial do documento com a UF do destinatário
(`CardCode` → endereço do parceiro). Iguais, usa `CfopOutgoingInState`; diferentes,
`CfopOutgoingOutState`. Se a UF da filial ou a do parceiro estiver ausente, ou se o CFOP
correspondente não estiver preenchido no usage, o serviço rejeita com mensagem de negócio em
pt-BR — nunca grava CFOP vazio em silêncio.

### Obrigatoriedade condicional

`RequiresContract`, `RequiresQuantity` e `RequiresWeight` são validados **no serviço**, não só
na tela. `RequiresQuantity = false` permite linha com quantidade zero e preço/valor
preenchidos (complemento de preço). `RequiresWeight = false` dispensa peso bruto e líquido no
cabeçalho.

### Efeito no contrato

Aplicado na **confirmação** (`Pending` → `Confirmed`) e estornado no cancelamento e no estorno
de confirmação. O documento em `Pending` não move saldo.

Não há coluna nova no contrato. Ambos os efeitos viram **linha no ledger
`SALES_CONTRACTS_ALLOCATIONS`**, que já é a fonte de verdade do consumo — linhas imutáveis,
volume assinado, `AllocatedVolume` recalculado a partir delas. O enum
`SalesContractAllocationOrigin` ganha um valor novo, `FiscalAdjustment`, para distinguir essas
linhas de `Billing`, `Return`, `Reallocation`, `Backfill` e `Reconciliation`.

**Saldo** — `ContractBalanceEffect` define o sinal de `Volume` na linha:

- `Consume` — `Volume` positivo, como o faturamento grava hoje.
- `Restore` — `Volume` negativo.
- `None` — **nenhuma linha de volume é gravada**.

**Valor** — `ContractValueEffect` usa a coluna `PriceDifference`, que já existe no ledger e já
é apurada por contrato justamente como "base para NF complementar / desconto financeiro".
A apuração está pronta desde a feature de alocação; o que faltava era o documento que a
materializa e a **liquida**.

Um complemento de preço grava linha com `Volume = 0` (não toca no saldo físico) e
`PriceDifference` com o **valor oposto** ao que está sendo complementado. Assim a soma de
`PriceDifference` por contrato converge para zero conforme a diferença é faturada, e o mesmo
valor não é cobrado duas vezes. A invariante do ledger (Σ `Volume` por item = consumo nominal
do item) continua válida, porque uma linha de volume zero não a altera.

`Subtract` inverte o sinal em relação a `Add`. `None` não grava `PriceDifference`.

Consequência de `SalesContractAllocation.SalesContractKey` e `SalesInvoiceItemKey` serem não
anuláveis: operação com qualquer efeito diferente de `None` **exige** contrato — a validação
de `RequiresContract` tem que ser coerente com os efeitos configurados, e o serviço rejeita um
usage que peça efeito sem exigir contrato.

Para "saída avulsa sem contrato" (ambos os efeitos `None`) nenhuma linha de ledger é escrita.

Duas amarras que o caminho novo precisa respeitar, e que não são visíveis na entidade:

1. **O recálculo de saldo mora no serviço, não na entidade.** Escrever status direto ou usar
   `CommitMode.Deferred` desliga o hook em silêncio. O caminho avulso tem que chamar o mesmo
   recálculo que o faturamento chama.
2. **Saldo negativo é permitido na venda.** `Restore` pode legitimamente empurrar o saldo para
   cima, e `Consume` pode furar a liberação. Nenhum dos dois deve ser barrado aqui — as travas
   existentes ficam em Finalizar e Cancelar contrato.

### Cadastro de usage

Exclusão bloqueada se houver documento referenciando o código — a validação é no serviço,
já que não há FK para o banco recusar. Inativação continua permitida.

## Serviços e API

Seguindo a convenção de uma classe por operação em `Services/<Feature>/`:

- `Services/Usages/`: `UsagesGetService`, `UsagesCreateService`, `UsagesUpdateService`,
  `UsagesDeleteService`.
- `IUsage` (já existe em `Domain/Interfaces/`, hoje **sem nenhuma implementação** e não
  registrado no DI) passa a ser implementado por `UsageService`, registrado em
  `AddStandAloneServices()`. `UsageModel` ganha os campos de efeito.
- `SalesInvoicesCreateService` passa a aceitar criação sem romaneio, com `UsageCode`
  obrigatório e validação condicional.
- Resolução de CFOP e aplicação de efeito ficam em serviços próprios, chamados tanto pelo
  caminho avulso quanto pelo faturamento de romaneio — um só lugar decide.

Todo serviço novo precisa ser registrado à mão em `AddApplicationServices()`
(`SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs`); não há varredura de assembly.

Controller OData novo para `Usages`, um por entidade, fino, chamando o serviço injetado.

## Frontend

- Tela de cadastro de natureza de operação: lista, criação e edição, com os efeitos como
  campos do formulário. Padrão de tela mestre simples já existente.
- **Rota nova `sales-invoices/add`** — hoje só existem `sales-invoices`, `{id}/detail`,
  `{id}/edit`, `/reconciliation` e `/report`. `storage-invoices/add` serve de modelo.
- Seleção de usage por value help; os campos obrigatórios do formulário reagem ao usage
  escolhido (peso e quantidade somem ou ficam opcionais conforme as flags).
- Campos fiscais e centro de custo / conta contábil no diálogo de item, com value help para
  os dois cadastros (serviços dual-mode já prontos).
- **Migration de menu**: cadastrar em `MENU_ITEMS` (chave = rota) e liberar em `ROLE_MENUS`
  para o perfil ADMIN, para as duas telas novas. Sem isso a tela existe e não é alcançável.

## Testes

Em `SiagroB1.Application.Tests` (xUnit + EF InMemory):

- Resolução de CFOP: mesma UF, UF diferente, UF da filial ausente, CFOP não cadastrado.
- Obrigatoriedade condicional: complemento de preço sem quantidade e sem peso passa; ajuste de
  quebra sem quantidade falha; saída sem contrato passa com `RequiresContract = false`.
- Efeito no saldo: `Consume` grava linha de `Volume` positivo, `Restore` negativo, `None` não
  grava linha nenhuma, e o cancelamento estorna exatamente o que a confirmação aplicou.
- Efeito no valor: complemento de preço grava `Volume = 0` com `PriceDifference` de sinal
  oposto, e a soma de `PriceDifference` do contrato converge para zero.
- A invariante do ledger (Σ `Volume` por item = consumo nominal do item) sobrevive à linha de
  volume zero.
- Usage com efeito diferente de `None` e `RequiresContract = false` é rejeitado no cadastro.
- Documento em `Pending` não move saldo.
- Exclusão de usage referenciado é bloqueada.

## Verificação

Teste passando não é conclusão. A verificação é pelo caminho do usuário: a partir da home,
chegar à tela pelo menu, cadastrar um usage de complemento de preço, lançar o documento contra
um contrato com diferença de preço apurada, confirmar, e ver essa diferença ser liquidada — com
o saldo **físico** intacto. Depois cancelar e ver o estorno. Enquanto isso não for feito no
navegador, a feature está pendente, não pronta.

## Preparação para SAPB1

Não é implementado agora, mas o desenho não fecha a porta:

- `OUSG` já está mapeado (`Domain/Entities/SAP/Usage.cs`) com os seis CFOPs — entrada e saída,
  dentro e fora do estado, importação e exportação. `IUsage` e `UsageModel` já existem.
- `IUsage` é a costura: em SAPB1 uma implementação lê `OUSG` (somente leitura, cadastro
  mantido no SAP, como `OPRC`/`OACT`) e o Siagro guarda só os efeitos de negócio, que o `OUSG`
  não tem.
- **Custo do adiamento**: uma migration que separa as colunas de CFOP de `USAGES` para a
  implementação STANDALONE e rechaveia a tabela de efeitos pelo `OUSG.ID`. Como `Code` já é
  `INT`, a rechaveação é direta.
- Ausência de efeito configurado para um usage do `OUSG` deve **barrar a seleção** no
  documento, com uma tela que lista os usages do SAP e os efeitos configuráveis ao lado. Não
  cair em default silencioso.

## Sequência

Este é o primeiro de quatro sub-projetos, cada um com spec próprio:

1. **Documento de saída de propósito geral** — este.
2. **Documento de entrada** — NF de terceiro e emissão própria; reusa `USAGES`, acrescenta os
   CFOPs de entrada e a manutenção fiscal do contrato de compra.
3. **NF-e de entrada e saída** — certificado, SEFAZ, DANFE, cancelamento, CC-e, inutilização,
   contingência. **Somente STANDALONE.**
4. **Financeiro** — títulos gerados pelos documentos, baixas e movimento bancário, consumindo
   o centro de custo e a conta contábil que já vêm da linha.

O motor de cálculo de tributação não está nessa fila. Quando entrar, encaixa sem refazer o
documento.
