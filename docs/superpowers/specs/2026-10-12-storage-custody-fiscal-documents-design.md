# Notas fiscais de custódia em armazém próprio (beneficiamento + armazenagem) — desenho

Data: 2026-10-12 · Modo: só STANDALONE · Relacionadas: `2026-10-10-storage-deposit-remittance-design.md` (caso
espelhado: nosso grão em armazém de terceiro), `2026-10-10-producer-counter-note-design.md`,
`2026-10-12-storage-remittances-report-design.md` (molde do relatório).

## 1. Objetivo

Somos o **depositário**: o produtor/cliente (depositante) entrega grão no nosso armazém, num lote de armazenagem
(`StorageAddress` com `OwnershipType = ThirdParty`). Hoje o módulo de Armazenagem controla só o físico — nenhum
romaneio do lote se liga a documento fiscal. Esta feature amarra cada romaneio à cadeia fiscal que ele exige:

- **Entrada** (por caminhão, um conjunto de notas por romaneio de Recepção):
  - lote **com** beneficiamento: entrada para beneficiamento (1.901) → devolução simbólica do beneficiamento
    (5.902) → entrada simbólica para armazenagem (1.905);
  - lote **sem** beneficiamento: só a entrada para armazenagem (1.905).
- **Saída**: retirada física pelo depositante → retorno (5.906) por carga; transferência de titularidade → retorno
  simbólico (5.907) integral da quantidade transferida e, se o destino for outro depositante, entrada para
  armazenagem no lote de destino.
- Cada **entrada** pode ser cumprida por NF-e **nossa** (Documento de Entrada Próprio) ou pela **escrituração** da
  NF-e emitida pelo depositante (Documento de Entrada De terceiro) — em SP somos responsáveis por escriturar as
  entradas.

Critério de sucesso: para todo romaneio de lote de terceiro, a tela do romaneio e o relatório mostram as mesmas
etapas esperadas e a mesma situação; o saldo fiscal em depósito por lote é comparável ao saldo físico.

## 2. Decisões (com o usuário, 12/10/2026)

| # | Decisão |
|---|---|
| D1 | Granularidade **por romaneio** (cada caminhão): uma nota por romaneio e etapa. |
| D2 | "Passa por beneficiamento" é configurado **no lote**; sem beneficiamento, entra direto para armazenagem. |
| D3 | Qualquer etapa de **entrada** (beneficiamento ou armazenagem) aceita NF-e própria **ou** escrituração da NF-e do depositante; a devolução simbólica é sempre nossa. Sem contra-nota. |
| D4 | Transferência: retorno simbólico na origem + entrada para armazenagem no lote de destino quando o destino é outro depositante; destino próprio (compra) gera só o retorno simbólico. |
| D5 | Notas geradas por **ação no romaneio**; o físico **não trava** por falta de nota. Pendências aparecem num **relatório** (não painel). |
| D6 | Preço unitário **digitado em cada ação**; natureza escolhida na ação, filtrada pelo papel. |
| D7 | Quantidades **bruto / bruto / líquido**: entrada para beneficiamento = peso bruto; devolução simbólica = mesma quantidade da entrada para beneficiamento; entrada para armazenagem = peso líquido. A quebra fica com o depositante entre a devolução e o depósito. |
| D8 | Abordagem A: vínculo no **cabeçalho** da nota (`CustodyStorageTransactionKey`) + papel na **natureza**. Rejeitadas: tabela de ligação romaneio × etapa × nota; entidade agregadora "cadeia fiscal". |

## 3. Modelo de dados

### 3.1 Natureza — `Usage.StorageCustodyRole`

Enum novo `StorageCustodyRole` (Domain/Enums), separado de `StorageDepositRole` (fluxos espelhados, não misturar):

| Valor | Etapa | Direção exigida | CFOP sugerido |
|---|---|---|---|
| `None = 0` | — | qualquer | — |
| `ProcessingEntry = 1` | Entrada para beneficiamento | `Incoming` | 1.901 / 2.901 |
| `ProcessingSymbolicReturn = 2` | Devolução simbólica do beneficiamento | `Outgoing` | 5.902 / 6.902 |
| `StorageEntry = 3` | Entrada para armazenagem | `Incoming` | 1.905 / 2.905 |
| `StorageReturn = 4` | Retorno de armazenagem (retirada) | `Outgoing` | 5.906 / 6.906 |
| `StorageSymbolicReturn = 5` | Retorno simbólico de armazenagem (transferência) | `Outgoing` | 5.907 / 6.907 |

Validação no cadastro da natureza (`UsageService`), papel ≠ None:

- a direção bate com a tabela;
- efeitos de contrato e de valor = None (mesma regra do `StorageDepositRole`);
- `StorageDepositRole` = None e `AllowsCounterNote` = false.

O CFOP continua livre (a tabela é só orientação ao cadastrador). `MovesFiscalInventory` fica a critério do cadastro.
Papel ≠ None fora do STANDALONE é recusado.

### 3.2 Lote — `StorageAddress.RequiresProcessing`

`bool`, padrão `false`. Só tem efeito em lote `OwnershipType = ThirdParty`. Editável enquanto o lote não tiver nota
de custódia **ativa** vinculada a qualquer romaneio dele; depois disso a alteração é recusada ("O lote já possui notas
fiscais de custódia; não é possível alterar o beneficiamento.").

### 3.3 Notas — `CustodyStorageTransactionKey`

`PurchaseInvoice.CustodyStorageTransactionKey` e `SalesInvoice.CustodyStorageTransactionKey`: `Guid?`, FK
**Restrict** para `STORAGE_TRANSACTIONS`, com índice não único. O nome evita confusão com
`StorageTransaction.SalesInvoiceKey` ("romaneio faturado").

- **Papel da nota** = papel da natureza das linhas. Todas as linhas de uma nota de custódia têm o mesmo papel; nota
  com linhas de papéis diferentes (inclusive None misturado com ≠ None) é recusada.
- Chave **obrigatória** quando o papel ≠ None; **proibida** quando o papel = None.
- Papéis 1 e 3 só em `PurchaseInvoice`; papéis 2, 4 e 5 só em `SalesInvoice` (decorre da direção).

**Nota ativa** = não cancelada e sem NF-e rejeitada/inutilizada (mesmo critério de "ativa" já usado pela trava de
duplicidade de documento de saída). Nota Pendente conta como ativa.

### 3.4 Etapas esperadas (derivadas, nunca gravadas)

Função pura `StorageCustodyRules.ExpectedRoles(transaction, lot, transfer?)` no Domain (reaproveitável pelo Reports):

| Romaneio (lote `ThirdParty`, não cancelado) | Etapas esperadas |
|---|---|
| Recepção (0) comum, `RequiresProcessing = true` | 1, 2, 3 |
| Recepção (0) comum, `RequiresProcessing = false` | 3 |
| Recepção (0) de transferência (`IsOwnershipTransfer = "Y"`) | 3 |
| Expedição (1) comum | 4 |
| Expedição (1) de transferência (destino outro depositante ou próprio) | 5 |
| Qualquer romaneio de lote ≠ `ThirdParty` | nenhuma |

Na transferência para nós (compra), a Recepção de destino cai em lote `OwnedInOurCustody` → nenhuma etapa.

**Unicidade**: no máximo uma nota ativa por (romaneio, papel). Validada nos serviços de criação/edição (o papel mora
na natureza, então não cabe índice filtrado).

### 3.5 Saldo fiscal em depósito (derivado)

Por lote: Σ quantidade das notas ativas de papel 3 − Σ quantidade das notas ativas de papéis 4 e 5, sobre os
romaneios do lote. Informativo, ao lado do saldo físico; nada trava por divergência.

## 4. Ações e regras

### 4.1 Ação "Gerar nota de custódia"

Action OData no romaneio (`StorageTransactions(key)/CreateCustodyDocument`), serviço
`StorageTransactionsCreateCustodyDocumentService`.

Parâmetros: `Role` (StorageCustodyRole), `UsageCode`, `UnitPrice` (> 0).

Pré-condições (cada uma com mensagem pt-BR própria):

- modo STANDALONE;
- romaneio Confirmado, não cancelado, de lote `ThirdParty`;
- `Role` ∈ etapas esperadas do romaneio e sem nota ativa nesse papel;
- natureza com `StorageCustodyRole = Role`;
- ordem (§4.3).

Efeito:

- papéis 1 e 3 → `PurchaseInvoice` com `IssuerType = Own`; papéis 2, 4 e 5 → `SalesInvoice`;
- status **Pendente**, `CustodyStorageTransactionKey` = romaneio;
- parceiro = depositante do lote (`StorageAddress.CardCode`), filial = filial do lote;
- uma linha: item e UM do lote, natureza informada, CFOP da natureza (dentro/fora do estado como hoje), quantidade
  pela §4.2, preço digitado; tributos pelo motor existente.

A resposta devolve a chave do documento criado; a tela navega para ele. Dali segue o caminho existente (conferir,
confirmar, transmitir; trava de data = hoje).

Na nota gerada, **item, parceiro, quantidade e romaneio ficam travados**; preço e campos fiscais comuns editáveis
enquanto Pendente.

### 4.2 Quantidade por papel

| Papel | Quantidade |
|---|---|
| 1 Entrada para beneficiamento | `GrossWeight` do romaneio |
| 2 Devolução simbólica | soma das quantidades da nota ativa de papel 1 do mesmo romaneio (própria ou escriturada) |
| 3 Entrada para armazenagem | `NetWeight` do romaneio (Recepção de transferência: a quantidade transferida, que é o `NetWeight` da Recepção gerada) |
| 4 Retorno | quantidade com que a Expedição baixa o lote (mesmo campo usado pelo saldo do lote) |
| 5 Retorno simbólico | quantidade transferida (quantidade da Expedição de transferência) |

O campo exato de peso que o saldo do lote usa é confirmado no plano, lendo o cálculo de saldo do `StorageAddress`.

### 4.3 Ordem e referências

- Papel 2 exige nota ativa de papel 1 **com chave de acesso** (NF-e própria autorizada ou escrituração com
  `ChaveNFe`). A devolução simbólica leva **NFref** (`refNFe`) para essa chave, montada no `NfeIssueInputAssembler`
  a partir do romaneio.
- Papel 3, em lote com beneficiamento, exige nota ativa de papel 2.
- Papéis 4 e 5 não dependem das entradas (D5: a saída não trava por cadeia incompleta).

### 4.4 Escrituração da NF-e do depositante (papéis 1 e 3)

Documento de Entrada **De terceiro** existente (digitado ou importado do XML). Quando alguma linha tem natureza com
papel 1 ou 3, o cabeçalho exige **Romaneio** (`CustodyStorageTransactionKey`). O XML não identifica o romaneio — o
vínculo é sempre escolhido pelo usuário. A quantidade é a da nota do depositante; diferença para a balança não
bloqueia e aparece no relatório.

Também vale para a escrituração (mesmo serviço de validação usado pela ação e pelas telas de nota):

- parceiro = depositante do lote; item de toda linha = item do lote; filial = filial do lote;
- romaneio Confirmado, não cancelado, com o papel entre as etapas esperadas e sem outra nota ativa nesse papel;
- ordem da §4.3 (papel 3 escriturado em lote com beneficiamento exige papel 2 ativo).

### 4.5 Cancelamento e estorno

- Cancelar nota de papel 1 é recusado com papel 2 ativo no mesmo romaneio; papel 2 recusado com papel 3 ativo
  ("Cancele primeiro a nota da etapa seguinte.").
- Estorno/cancelamento de romaneio com nota de custódia ativa é recusado; estorno de transferência de titularidade
  é recusado se qualquer romaneio dela tiver nota ativa ("Cancele as notas fiscais de custódia antes de estornar.").
- Nota cancelada/rejeitada deixa de contar: a etapa volta a pendente e aceita nova nota.
- Excluir nota Pendente segue as regras atuais de exclusão; a FK Restrict impede excluir romaneio com nota vinculada.

### 4.6 SAPB1

Fora de escopo. Papel ≠ None na natureza, a ação e o vínculo são recusados fora do STANDALONE; a seção da tela e o
campo do lote não aparecem.

## 5. Telas (frontend)

- **Natureza** (`usages` Add/Edit): Select "Papel na custódia (armazenagem)" — Nenhum, Entrada para beneficiamento,
  Devolução simbólica do beneficiamento, Entrada para armazenagem, Retorno de armazenagem, Retorno simbólico de
  armazenagem. Só STANDALONE, junto do "Papel na remessa para depósito".
- **Lote** (`storageAddresses` Add/Edit): Select Sim/Não "Passa por beneficiamento (fiscal)", só STANDALONE e lote de
  terceiro; desabilitado quando houver nota de custódia ativa.
- **Romaneio** (`storageTransactions` Detail): seção "Notas fiscais de custódia" — uma linha por etapa esperada:
  Etapa, Situação (Pendente de nota / Documento gerado / Autorizada / Escriturada), Documento (DE…/DS…, link), NF/Série,
  Chave, Quantidade. Botão "Gerar nota" abre diálogo com Etapa (sugerida = próxima pendente), Natureza (filtrada pelo
  papel) e Preço unitário; ao gravar navega para o documento. Dados vêm de uma function OData
  `StorageTransactions(key)/CustodySummary` (mesmas regras do relatório).
- **Documento de Entrada** (`purchaseInvoices` Add/Edit/Detail): campo Romaneio no cabeçalho quando alguma linha tem
  natureza de papel 1 ou 3; value help com as Recepções do mesmo depositante e item com a etapa pendente; link no
  Detail.
- **Documento de Saída** (`salesInvoices` Detail): link para o romaneio, só leitura.

## 6. Relatório "Custódia de Armazenagem"

PDF no `SiagroB1.Reports`, no molde do relatório de remessas para depósito: consulta própria (o Reports não referencia
a Application), regras de etapas reaproveitadas de `StorageCustodyRules` (Domain), endpoint
`POST /reports/StorageCustodyByPeriod` → `storage-custody-by-period.pdf`; fora do STANDALONE → 400 "Disponível somente
no modo STANDALONE.". Menu no grupo Armazenagem, com migration de menu (MENU_ITEMS + ROLE_MENUS).

**Filtros**: período (obrigatório, data do romaneio, fim inclusivo), filial, depositante, produto, lote, situação
(Todas — padrão / Com pendência fiscal / Completa).

**Layout**: agrupado por depositante e lote. Linha do romaneio (Recepção/Expedição, número, data, peso bruto,
peso líquido, "transferência" quando for o caso); abaixo, uma linha por etapa esperada: CFOP, documento, NF/série,
emissão, situação da NF-e, quantidade da nota e diferença para o peso da balança na base daquela etapa (§4.2). Etapa
sem nota ativa sai como **PENDENTE** em negrito.

**Totais por lote**: saldo físico na data final, saldo fiscal em depósito (§3.5, notas de romaneios até a data final)
e diferença.

**Situação** do romaneio: Completa quando todas as etapas esperadas têm nota ativa; senão Com pendência fiscal.

## 7. Testes

Backend (`SiagroB1.Application.Tests`, TDD com RED visto):

- natureza: papel × direção; papel com efeitos ≠ None; papel + `StorageDepositRole`/`AllowsCounterNote`; papel fora
  do STANDALONE;
- `ExpectedRoles`: um caso por linha da §3.4;
- geração: quantidades por papel com **`GrossWeight ≠ NetWeight`** (todo seed atual grava iguais); papel 2 herdando
  a quantidade da escrituração do depositante diferente da balança; NFref do papel 2; papel 2 sem chave recusado;
  papel 3 sem papel 2 em lote com beneficiamento recusado; regerar após cancelada/rejeitada aceito;
- travas: duas ativas no mesmo papel; parceiro/item/filial divergentes; romaneio cancelado; lote não `ThirdParty`;
  escrituração com papel e sem romaneio; nota com papéis misturados; `RequiresProcessing` alterado com nota ativa;
- cancelamento fora da ordem inversa; estorno de romaneio/transferência com nota ativa recusado e aceito depois de
  cancelar;
- saldo fiscal em depósito ignorando canceladas/rejeitadas;
- `NfeIssueInputAssembler`: papel 2 gera `refNFe`; papéis 1, 3, 4 e 5 não;
- relatório: teste cruzado contra `CustodySummary` nos mesmos cenários; filtro de situação.

Frontend: `ts-typecheck`, `lint`, ui5lint sem aumentar achados; QUnit para formatadores novos de etapa/situação.

## 8. Verificação ponta a ponta

CEAGUI dev (`CEAGUI_SIAGRO_DEV`, perfil `ceagui`), pela tela, com Playwright:

1. Cadastrar as cinco naturezas (1.901, 5.902, 1.905, 5.906, 5.907) e um lote de terceiro com beneficiamento.
2. Recepção com bruto ≠ líquido: gerar e transmitir a entrada para beneficiamento; gerar a devolução simbólica
   (conferir NFref no XML) e transmitir; gerar a entrada para armazenagem com o líquido.
3. Segunda Recepção: escriturar uma NF-e do depositante como papel 1 vinculada pelo value help; devolução simbólica
   herdando a quantidade dela.
4. Lote sem beneficiamento: Recepção com uma única entrada para armazenagem.
5. Expedição com retorno 5.906.
6. Transferência para outro depositante: 5.907 na origem, entrada para armazenagem no destino; tentar estornar com
   nota ativa e ver a recusa.
7. Relatório: pendências e saldos físico × fiscal batendo com as telas.

A SEFAZ de homologação deve rejeitar por CNPJ fictício (181) como nas features anteriores; vale o XML assinado
conferido (CFOP, NFref, quantidades). SAPB1 é verificado só como recusa.

## 9. Fora de escopo

SAPB1; NF de cobrança do serviço de beneficiamento/armazenagem; a nota de venda do depositante a terceiro (é dele);
vínculo automático do XML do depositante ao romaneio; notas agrupando vários romaneios.

## 10. Entrega

Branch `feature/storage-custody-fiscal-documents` nos dois repos. Migrations aditivas: `Usage.StorageCustodyRole`,
`StorageAddress.RequiresProcessing`, as duas FKs de nota e o menu do relatório — aplicadas só no `CEAGUI_SIAGRO_DEV`
(o Web recusa subir com migration pendente nos demais bancos).
