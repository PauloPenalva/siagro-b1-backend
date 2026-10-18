# Numeração "Automático" / "Manual" + séries para Cliente, Fornecedor e Produto

Data: 2026-10-18 · Repos: `siagro-b1-backend` e `siagro-b1-frontend` · Modos: STANDALONE e SAPB1
(só para o que vive no `AppDbContext`).

## Intenção (o porquê)

O usuário quer trabalhar a numeração como o SAP B1 faz em "Documents / Master Data": em cada
documento ou cadastro numerado, escolher a série **"Automático"** (o sistema dá o número) ou
**"Manual"** (o usuário digita).

O motivo concreto: quando um registro é excluído, a sequência fica com um "buraco", e o usuário
quer poder reaproveitar esse número para manter a sequência contínua. Outro caso dele: cadastrou o
cliente `C002448` e quer cadastrar o mesmo parceiro como fornecedor `F002448`, um código que
pode estar **à frente** da sequência automática de fornecedores.

Hoje o cadastro "Numeração de Documentos" tem séries com o nome do próprio documento ("Contrato de
Compra", "DOCUMENTO DE ENTRADA"...). O nome da série padrão deve passar a ser **"Automático"**.

Além disso, no STANDALONE, Cliente, Fornecedor e Produto passam a ter numeração automática, que
hoje não existe (o código é sempre digitado).

## Objetivos

1. Toda tela de inclusão de documento numerado oferece no Select de série: "Automático", "Manual"
   e quaisquer outras séries ativas que o usuário tenha criado para aquele documento.
2. Com uma série manual escolhida, o campo de número fica editável e obrigatório; o servidor
   grava o código digitado, recusando apenas código repetido.
3. Com uma série automática, comportamento de hoje (servidor gera, campo só leitura), **mais**:
   o gerador pula códigos que já existam na tabela de destino.
4. Novos tipos de numeração: Cliente (prefixo `C`), Fornecedor (prefixo `F`) e Produto (sem
   prefixo; o usuário cria séries `P`, `S`... se quiser), com 6 dígitos, no estilo SAP B1.
5. Documento de Entrada, Carga e Conferência de Saldo ganham o Select de série (hoje sempre usam
   a padrão sem mostrar nada).

## Não-objetivos

- **Numeração da NF-e** (`BRANCH_NFE_SERIES`, número fiscal, inutilização): não muda. Este
  trabalho trata só do número interno.
- **Títulos financeiros** (`FinancialDocument`): continuam sempre na série padrão automática,
  sem Select e sem linha "Manual" — são gerados pelo sistema, nunca digitados. *(Decisão minha.)*
- **Cadastros do SAP no modo SAPB1**: Parceiro e Produto são somente leitura vindos do SAP; não
  ganham numeração. Os tipos Cliente/Fornecedor/Produto ficam ocultos na tela de Numeração quando
  o modo é SAPB1. *(Decisão minha, coerente com o pedido "só o que é do AppDbContext".)*
- Não alterar o número de um registro depois de criado (continua imutável).
- Não "devolver" números à sequência em exclusão/rollback (o Manual é justamente a saída para isso).

## Anti-objetivos (falharia mesmo "funcionando")

- O automático gerar um código que já existe e estourar erro de chave/duplicidade depois que
  alguém digitou um número manual à frente.
- Um fluxo gerado pelo sistema (complemento de ICMS, perda técnica, etc.) travar porque herdou
  uma série manual e não há ninguém para digitar o número.
- Quebrar a numeração das bases em produção (Yokotobi, CEAGUI) na migração.

## Estado atual (levantado no código)

- Tabela `DOC_NUMBERS` (`SiagroB1.Domain/Entities/DocNumber.cs`): `TransactionCode`, `Name`,
  `FirstNumber`, `LastNumber`, `NextNumber`, `Default`, `Prefix`, `Suffix`, `NumberSize`,
  `BranchCode`, `Inactive`, **`IsManual`**. Índice único `(TransactionCode, Name)`.
  `IsManual` e `Inactive` existem mas **ninguém lê**; `IsManual` está comentado no formulário.
- Gerador: `DocNumberSequenceService` (Dapper, `UPDATE ... WITH (UPDLOCK, HOLDLOCK) OUTPUT`, fora
  da transação EF). `GetDocNumber(key)` formata `Prefix + PadLeft(NumberSize) + Suffix`.
  `GetKeyByTransactionCode` pega a série `Default=1` (nada garante uma só).
- Consumidores (serviço de criação → campo):

  | TransactionCode | Entidade | Campo | Select na tela hoje |
  |---|---|---|---|
  | PurchaseContract (1) | PurchaseContract | `Code` (único) | sim |
  | WeighingTicket (2) | WeighingTicket | `Code` (único) | sim |
  | StorageAddress (4) | StorageAddress (lote) | `Code` (PK) | sim |
  | StorageTransaction (5) | StorageTransaction (romaneios) | `Code` | sim |
  | SalesContract (7) | SalesContract | `Code` (único) | sim |
  | SalesInvoice (8) | SalesInvoice | `InvoiceNumber` | sim |
  | OwnershipTransfer (9) | OwnershipTransfer | `TransferCode` | sim |
  | StorageInvoice (10) | StorageInvoice (fatura de serviço, via ação de fechamento) | `Code` (único) | sim (no diálogo de fechamento) |
  | ShipmentLoad (11) | ShipmentLoad | `Code` (único) | **não** |
  | FinancialDocument (12) | FinancialDocument | `Code` | não (fica assim) |
  | WarehouseReconciliation (13) | WarehouseReconciliation | `Code` | **não** |
  | PurchaseInvoice (14) | PurchaseInvoice | `InvoiceNumber` VARCHAR(9) | **não** (servidor descarta a chave enviada) |

  Detalhes de arquivo:linha estão no levantamento; ponto de partida: `grep GetDocNumber` em
  `SiagroB1.Application/Services`.
- Séries só são semeadas por migration para 11, 12, 13 e 14 (nomes "CARGA", "FINANCEIRO",
  "CONFERENCIA SALDO", "DOCUMENTO DE ENTRADA"); as demais foram criadas à mão em cada base.
- Parceiro (`BUSINESS_PARTNERS.CardCode` VARCHAR(15), `CardType` "C"/"S") e Produto
  (`ITEMS.ItemCode` VARCHAR(50)): no STANDALONE o código é digitado
  (`BusinessPartnerService.cs:83-102`, `ItemService.cs:15-30`).
- Frontend: tela "Numeração de Documentos" em `webapp/view/docNumbers/` (Select "Documento" com 12
  tipos); telas de inclusão usam `<Select selectedKey="{DocNumberKey}" items="{/DocNumbers ...}">`
  + `<Input value="{Code}" editable="false"/>` e pré-selecionam a série `Default` via
  `CommonController.getDocNumberInfoByTransaction()`.

## O que foi decidido

### Modelo de séries

- **Cada tipo numerado tem uma linha "Manual"**: `Name = 'Manual'`, `IsManual = 1`,
  `Default = 0`. É a linha que, escolhida na tela, libera a digitação. Exceções: FinancialDocument e StorageTransaction (romaneios)
  não ganham linha Manual.
- **A série padrão de cada tipo chama "Automático"** (decisão do usuário: renomear as existentes).
- O usuário pode continuar criando outras séries automáticas (ex.: Produto "P", Serviço "S").
- **Só uma linha Manual por tipo**; ela não pode ser excluída nem virar série automática; pode ser
  marcada Inativa para desligar a digitação manual daquele documento. *(Decisão minha.)*
- **Série manual não pode ser `Default`** — vários fluxos usam a padrão sem perguntar (Carga via
  sistema, Financeiro, complementos). *(Decisão minha.)*
- **Uma única série `Default` por tipo**: ao marcar uma série como Padrão, as outras do mesmo tipo
  são desmarcadas (regra no serviço). *(Decisão minha.)*
- **Inativo passa a valer**: série inativa some do Select das telas e o servidor recusa criar com
  ela. A série Padrão não pode ser inativada. *(Decisão minha.)*

### Romaneios (StorageTransaction): sem Manual e sem exclusão (decisão do usuário)

Um único "Salvar" no Romaneio de Embarque e na Entrada em Armazenagem cria **dois** romaneios
(compra + venda/recebimento, via `StorageTransactionCopyFactory`), então um número digitado não
teria um dono claro. Decisão do usuário:

- **Não há linha "Manual" para `StorageTransaction`** (TransactionCode 5): o Select de série dos
  romaneios mostra só séries automáticas, e o servidor recusa série manual nesse tipo.
- **Romaneio não pode mais ser excluído; só cancelado.** Hoje a lista "Romaneios"
  (`webapp/controller/storageTransactions/Main.controller.ts` `onDelete`) apaga romaneio Pendente
  via `StorageTransactionsDeleteService` (DELETE no `StorageTransactionsController`). Remover o
  botão/ação de excluir da tela e fazer o DELETE do controller recusar sempre (mensagem pt-BR
  orientando a cancelar). O cancelamento existente (`StorageTransactionsCancelService`, aceita
  qualquer status exceto Faturado) passa a ser o único caminho, inclusive para romaneio Pendente.
  Assim a sequência dos romaneios não ganha buracos por exclusão.
- *(Decisão minha)* Isso vale só para romaneios; a exclusão dos demais documentos fica como está
  (lá o Manual existe justamente para tapar buracos).

### Comportamento na criação (servidor)

Para cada consumidor, ao criar:

1. Resolver a série (`DocNumberKey` enviado, ou a Padrão quando o serviço já fazia fallback).
   Recusar série inativa ou de outro `TransactionCode`.
2. **Série manual** → exigir o código enviado no campo do documento (`Code`, `InvoiceNumber`,
   `TransferCode`, `CardCode`, `ItemCode`), aparado (`Trim`), não vazio, dentro do tamanho da
   coluna. Recusar se já existir na tabela de destino (mensagem pt-BR "Número já utilizado").
   Prefixo/sufixo/dígitos da série **não** são aplicados: o usuário digita o código completo
   (ex.: `DE000005`, `F002448`). O contador da série automática **não** é tocado.
3. **Série automática** → gerar como hoje e **pular códigos já existentes**: se o código formatado
   já existe na tabela de destino, pegar o próximo, até achar um livre. O valor enviado pelo
   cliente no campo é ignorado (como hoje).
4. **Fluxos gerados pelo sistema** (sem usuário digitando: complemento de ICMS de saída que herda a
   série da origem, perda técnica do lote, títulos financeiros, carga criada a partir de
   planejamento, cópias automáticas etc.): se a série herdada for manual, usar a série Padrão
   automática. Nunca exigir digitação nesses caminhos.
5. Unicidade: as entidades sem índice único (`SalesInvoice.InvoiceNumber`,
   `PurchaseInvoice.InvoiceNumber`, `StorageTransaction.Code`, `OwnershipTransfer.TransferCode`,
   `WarehouseReconciliation.Code`) recebem a checagem no serviço; **não** criar índices únicos
   novos agora (bases antigas podem ter duplicados). *(Decisão minha.)*

### Cliente, Fornecedor, Produto (STANDALONE)

- Novos valores no enum `TransactionCode`: `Customer`, `Supplier`, `Item` (próximos números
  livres: 16, 17, 18).
- Migration semeia, para cada um, "Automático" (Padrão, `NumberSize = '6'`, prefixo `C` / `F` /
  vazio) e "Manual". `NextNumber` inicial = maior número já usado entre os códigos existentes que
  seguem o padrão (prefixo + só dígitos) + 1, ou 1 se não houver.
- Cadastro de Parceiro: Select de série ao lado do Código, filtrado pelo `TransactionCode` que
  corresponde ao Tipo (`C` → Customer, `S` → Supplier). Trocar o Tipo recarrega a lista e
  pré-seleciona a Padrão daquele tipo. Exemplo do usuário: Fornecedor + Manual + `F002448`.
- Cadastro de Produto: Select de série (tipo Item) ao lado do Código.
- Na edição de um cadastro existente, Select de série não aparece; código continua imutável.
- Modo SAPB1: nada muda nesses cadastros (são do SAP).
- O parceiro/produto precisa guardar qual série foi usada? Não é necessário para o
  comportamento; fica a critério do builder (ver abaixo).

### Telas (frontend)

- Em todas as telas de inclusão com Select de série: listar só séries ativas; escolher uma série
  com `IsManual = true` torna o Input de número editável e obrigatório; voltar para automática
  limpa e trava o Input (mostrando vazio/"gerado ao salvar", como hoje).
- Documento de Entrada, Carga e Conferência de Saldo ganham o mesmo par Select + Input, e o
  servidor passa a respeitar a chave enviada (hoje o Documento de Entrada a descarta).
- Fatura de serviço (fechamento): o diálogo que escolhe a série ganha o campo de número quando a
  série for manual; a ação passa a aceitar o código.
- Tela "Numeração de Documentos": novos tipos Cliente, Fornecedor, Produto (ocultos em SAPB1);
  linha Manual mostrada com os campos de numeração (próximo número, prefixo, dígitos...) ocultos
  ou travados; checkbox "Manual" descomentada só como informação (não editável); remover o
  `<Label text="Padrão"/>` solto. Listas e Selects ordenam "Automático" primeiro.

### Migração das bases existentes (decisão do usuário: renomear + criar Manual)

Para cada `TransactionCode` numerado (todos menos FinancialDocument e StorageTransaction para a linha Manual):

- Se existir série `Default = 1`: renomear para "Automático" (se houver mais de uma Padrão, manter
  como Padrão e renomear só a primeira por `Key`/menor `NextNumber`... builder escolhe um critério
  determinístico e desmarca as outras). Se já houver outra linha chamada "Automático" no mesmo
  tipo, não renomear (evita violar o índice único) e registrar via `PRINT`.
- Se o tipo não tiver nenhuma série: não criar "Automático" para tipos antigos sem séries? — ver
  "Deixado ao builder".
- Criar a linha "Manual" se ainda não existir.
- Tudo idempotente (`IF NOT EXISTS`), como as seeds anteriores.

## Restrições

- .NET 10, OData; UI5 TypeScript. Seguir os CLAUDE.md dos sub-repos (convenção de commit com
  trailer `DB:` para migration; nova migration é obrigatória no deploy — a Web recusa subir com
  migration pendente).
- Identificadores em inglês, textos ao usuário em pt-BR.
- A geração continua fora da transação EF (como hoje); o "pular existentes" deve consultar a
  tabela de destino de forma que o consumidor informe qual tabela/campo checar.
- Testes: `SiagroB1.Application.Tests` (o `FakeDocNumberSequenceService` precisará acompanhar a
  nova assinatura).

## Deixado ao builder

- Forma de expor a checagem "já existe" ao gerador (callback por consumidor, interface por
  entidade, SQL dinâmico com tabela/coluna em lista fixa...).
- Se `BusinessPartner`/`Item` ganham `DocNumberKey` persistido ou não.
- Tipos numerados que hoje não têm nenhuma série em alguma base: criar "Automático" com prefixo
  vazio e `NextNumber` calculado, ou deixar como está (a tela já exige série). Recomendo criar,
  para que a linha Manual nunca exista sozinha.
- Critério determinístico quando há várias séries Padrão no mesmo tipo.
- Textos exatos das mensagens e i18n.
- Ordem das tarefas e divisão em commits (um assunto por commit, backend e frontend separados).
