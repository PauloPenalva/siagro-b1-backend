# Remessa para depósito em armazém de terceiro — desenho

**Data:** 2026-10-10
**Modo:** somente STANDALONE (SAPB1 fica para uma fase futura)
**Branch:** `feature/storage-deposit-remittance` (backend e frontend)

## 1. Contexto

Na maioria dos contratos de compra a mercadoria liberada fica em poder de terceiros. Alguns
armazéns terceiros exigem, para receber o grão em depósito, uma **NF-e de remessa para depósito**
emitida por nós com a quantidade liberada. A cada carregamento, o armazém emite uma **NF-e de
retorno** dessa remessa. Hoje o sistema não tem nada disso: não há natureza nem CFOP de
depósito semeados, a remessa só poderia ser emitida como documento de saída avulso sem vínculo,
e nada controla quanto já retornou.

Objetivo: emitir a remessa a partir da liberação, registrar os retornos pelo Documento de
Entrada (XML ou digitado), reconciliar cada retorno com os romaneios expedidos e mostrar, por
liberação, **Remetido / Retornado / A retornar / Expedido sem retorno**.

### Decisões tomadas com o usuário

| # | Decisão |
|---|---|
| D1 | Uma remessa por liberação, com a quantidade liberada. |
| D2 | O retorno vincula-se à remessa **e** aos romaneios expedidos, por reconciliação feita depois do lançamento do Documento de Entrada. |
| D3 | Um retorno cobre N romaneios; cada romaneio é reconciliado inteiro com no máximo um retorno. Diferença de peso não bloqueia: falta é quebra técnica (Conferência de Armazém, GAC-1164), excesso é nova negociação — ambos fora do escopo. |
| D4 | Sem flag de "armazém exige remessa": a ação fica disponível em toda liberação; o usuário decide. Nada bloqueia a expedição. |
| D5 | O preço unitário da remessa é digitado pelo usuário (sem sugestão). |
| D6 | Abordagem: a remessa **é** um Documento de Saída, o retorno **é** um Documento de Entrada de terceiro; nenhuma entidade de remessa própria. |
| D7 | Na entrada com natureza de Retorno, o vínculo com a remessa é **obrigatório**. |
| D8 | O romaneio reconciliado não pode ser estornado/cancelado (inclusive pela troca de liberação); é preciso desfazer a reconciliação antes. |
| D9 | A natureza **não** é fixada na filial: o usuário informa na operação. |
| D10 | Somente STANDALONE. Em SAPB1 tudo fica oculto na tela e recusado no backend. |

## 2. Modelo de dados

Uma migration: `AddStorageDepositRemittance` (commit com trailer `DB:`).

### 2.1 Papel da natureza — `USAGE_EFFECTS.StorageDepositRole`

Enum novo `StorageDepositRole { None = 0, Remittance = 1, Return = 2 }`, coluna `int NOT NULL
DEFAULT 0`. Fica em `USAGE_EFFECTS` (e não em `USAGES`) para seguir o lugar dos demais efeitos de
negócio e já nascer pronta para o modo SAPB1, chaveada pelo `OUSG.ID`. Exposta no `UsageModel`
como os demais campos de efeito.

Uma natureza com papel diferente de `None` deve ter `ContractBalanceEffect = None` e
`ContractValueEffect = None` (validado no `UsageService` ao gravar o efeito): remessa e retorno
de depósito não mexem no contrato.

### 2.2 Remessa — `SALES_INVOICES.ShipmentReleaseKey`

`Guid?`, FK para `SHIPMENT_RELEASES` (`NoAction`), com índice. Preenchido marca o documento
como "remessa para depósito da liberação X".

- Uma liberação tem no máximo **uma remessa ativa** (documento não cancelado). Garantido por
  guard no serviço, não por índice único: as canceladas permanecem e a reemissão cria outro
  documento.
- Destinatário (`CardCode`) = o parceiro do armazém (`ShipmentRelease.DeliveryLocationCode`).
- Uma linha: item do contrato da liberação, `Quantity` = `ReleasedQuantity`, `UnitPrice`
  digitado, `UsageCode` = natureza com papel `Remittance`. `SalesContractKey` nulo.
- `InvoiceType = Normal`. Não gera título (a natureza é configurada sem financeiro).

### 2.3 Retorno — `PURCHASE_INVOICES_ITEMS.SalesInvoiceItemKey` (já existe)

A linha do Documento de Entrada de terceiro com natureza de papel `Return` aponta para a linha
da remessa pelo `SalesInvoiceItemKey` já existente. Nenhuma coluna nova.

### 2.4 Reconciliação — tabela `STORAGE_RETURN_RECONCILIATIONS`

| Coluna | Tipo | Nota |
|---|---|---|
| `Key` | `uniqueidentifier` PK | |
| `PurchaseInvoiceKey` | `uniqueidentifier` | FK `PURCHASE_INVOICES`, `NoAction`, índice |
| `StorageTransactionKey` | `uniqueidentifier` | FK `STORAGE_TRANSACTIONS`, `NoAction`, **índice único** (D3) |
| `CreateDate`, `UserSign` | | padrão do projeto |

Entidade `StorageReturnReconciliation`. A remoção junto com o retorno é feita no serviço, não
por cascade no banco.

### 2.5 Saldos (calculados, não persistidos)

Serviço `StorageDepositRemittanceBalanceService`, por liberação:

- **Remetido** = `Quantity` da linha da remessa ativa (0 sem remessa).
- **Retornado** = soma de `Quantity` das linhas de entrada com `SalesInvoiceItemKey` = linha da
  remessa, em documentos não cancelados (Pendente + Confirmado), no padrão de
  `PurchaseInvoiceNfeReturnBalance`.
- **A retornar** = Remetido − Retornado.
- **Expedido sem retorno** = soma de `NetWeight` dos romaneios candidatos (§3.5) da liberação sem
  reconciliação.

## 3. Regras e guardas

Todas as regras novas recusam com mensagem de negócio em pt-BR. Todas as ações novas recusam em
SAPB1 ("Disponível somente no modo STANDALONE.").

### 3.1 Emitir remessa (`ShipmentReleasesCreateStorageRemittanceService`)

Entrada: chave da liberação, `UsageCode`, `UnitPrice`. Recusa quando:

- modo SAPB1;
- liberação cancelada;
- já existe remessa ativa para a liberação;
- o armazém da liberação não tem parceiro de negócio cadastrado (armazém interno);
- natureza não informada ou com papel diferente de `Remittance`;
- `UnitPrice <= 0`.

Cria o Documento de Saída Pendente (§2.2) pelo mesmo caminho de criação do documento avulso, com
a filial do contrato, e devolve a chave. Confirmar, transmitir, cancelar, CC-e e DANFE seguem o
fluxo existente do Documento de Saída. Reemitir depois de cancelar (ou de rejeição) é a mesma
ação. Não existe remessa complementar.

### 3.2 Guard de papel na saída (`SalesInvoicesUsageGuardService`)

- Linha com natureza `Remittance` exige `ShipmentReleaseKey` no cabeçalho.
- Documento com `ShipmentReleaseKey` só aceita linhas com natureza `Remittance`.
- Na saída, natureza `Return` é recusada.

### 3.3 Guard de retorno na entrada (`PurchaseInvoiceLineGuard`)

Linha com natureza `Return` exige:

- `SalesInvoiceItemKey` preenchido, apontando para uma linha de documento com
  `ShipmentReleaseKey` (uma remessa), não cancelado;
- o mesmo `ItemCode` da remessa;
- `CardCode` da entrada = `CardCode` da remessa (o armazém);
- `Quantity` ≤ A retornar da remessa, descontando as demais linhas do próprio documento e
  excluindo a própria linha na edição. Retornar mais do que foi remetido é recusado.

Na entrada, natureza `Remittance` é recusada.

### 3.4 Guardas em fluxos existentes

| Fluxo | Regra |
|---|---|
| Liberação: cancelar (`ShipmentReleasesCancelationService`) ou alterar `ReleasedQuantity` (`ShipmentReleasesUpdateService`) | Recusa se há remessa ativa: "Cancele a remessa nº X antes." |
| Remessa: cancelar (`SalesInvoicesCancelService`) ou estornar a confirmação (`SalesInvoicesReverseConfirmService`) | Recusa se há linha de retorno em documento não cancelado. |
| Retorno: cancelar (`PurchaseInvoicesCancelService`) ou excluir (`PurchaseInvoicesDeleteService`) | Apaga as reconciliações do documento na mesma transação. |
| Romaneio: estornar/cancelar (`StorageTransactionsReverseService`, `StorageTransactionsCancelService`), inclusive pela troca de liberação (GAC-1177) | Recusa se reconciliado: "Desfaça a reconciliação com o retorno nº X." |

### 3.5 Reconciliação (`StorageReturnReconciliationsCreateService` / `...DeleteService`)

- Só em retorno não cancelado com ao menos uma linha de natureza `Return`.
- **Candidatos:** romaneios `Confirmed` cujo `ShipmentReleaseKey` é a liberação de alguma remessa
  referenciada pelas linhas do retorno, que consomem a liberação (mesmo critério de sinal do
  `ShipmentReleasesRecalculateShippedService`: compra (8) e os demais tipos de embarque que
  contam como expedido) e que ainda não estão reconciliados.
- Aceita vários romaneios de uma vez; recusa romaneio fora dos candidatos ou já reconciliado.
- Diferença entre a quantidade da nota e a soma dos romaneios é informativa, nunca bloqueia (D3).
- Desfazer é livre.

## 4. Importação do XML do retorno

`SupplierNfeXmlReader` passa a ler `ide/NFref/refNFe`. Em `PurchaseInvoicesImportXmlService`,
para cada chave referenciada que corresponda a uma remessa (documento com `ShipmentReleaseKey` e
`ChaveNFe` igual) do mesmo fornecedor, as linhas do rascunho com o mesmo `ItemCode` recebem
`SalesInvoiceItemKey` = a linha da remessa. Sem correspondência, o rascunho vem sem vínculo e o
usuário escolhe no value help (o guard de §3.3 só impede salvar com natureza de retorno sem
vínculo).

## 5. Telas (frontend, somente STANDALONE via `getSystemInfo().erp`)

### 5.1 Detalhe da liberação de compra

- Botão **"Emitir remessa"** no cabeçalho, visível com liberação não cancelada e sem remessa
  ativa. Diálogo: armazém (somente leitura), natureza (value help filtrado por papel Remessa,
  obrigatória), quantidade (somente leitura = liberada), preço unitário (obrigatório). Confirmar
  cria o documento e navega para o Documento de Saída.
- Seção **"Remessa para depósito"**: nº e situação da remessa (link), Remetido, Retornado,
  A retornar, Expedido sem retorno; tabela dos retornos (nº NF, data, quantidade) e tabela dos
  romaneios expedidos com o retorno reconciliado ou "Sem retorno".

### 5.2 Documento de Saída (a remessa)

Campo "Liberação" somente leitura no cabeçalho, com link para a liberação. Nada mais muda.

### 5.3 Documento de Entrada (o retorno)

- Coluna **"Remessa"** na grade de itens: preenchida pela importação (§4) ou escolhida em value
  help (remessas do fornecedor e produto da linha com A retornar > 0).
- Botão **"Reconciliar romaneios"** no documento salvo e não cancelado com linha de retorno:
  diálogo com os candidatos (nº, data, placa, peso líquido) em seleção múltipla e rodapé
  "Nota × Romaneios × Diferença".
- Seção **"Romaneios reconciliados"** com a lista e a ação de desfazer.

### 5.4 Naturezas de Operação

Campo **"Papel no depósito"** (Nenhum / Remessa / Retorno) junto dos demais efeitos.

## 6. Testes (`SiagroB1.Application.Tests`, TDD)

- Emitir remessa: caminho feliz (destinatário = armazém, quantidade = liberada, natureza e preço
  gravados, `ShipmentReleaseKey` preenchido); cada recusa de §3.1; reemissão após cancelar.
- Guard de papel da saída (§3.2) nos três sentidos.
- Guard de retorno (§3.3): sem vínculo, vínculo com documento que não é remessa, remessa
  cancelada, produto diferente, fornecedor diferente, quantidade acima do saldo (várias linhas e
  vários documentos), retorno cancelado devolvendo saldo, edição da própria linha.
- Saldos (§2.5) com retornos Pendente, Confirmado e Cancelado e romaneios reconciliados ou não.
- Reconciliação: candidatos certos, recusa de romaneio de outra liberação e de já reconciliado,
  cancelar/excluir o retorno apaga as reconciliações, desfazer.
- Guardas cruzados de §3.4, um teste por linha da tabela (incluindo a troca de liberação).
- Importação: `NFref` com a chave da remessa preenche o vínculo; chave desconhecida não preenche.
- Recusa em SAPB1 de todas as ações novas.
- Consultas de saldo e de candidatos: cobertas também pelo teste de tradução SQL Server do
  projeto (InMemory esconde LINQ intraduzível).

## 7. Verificação end-to-end (CEAGUI_SIAGRO_DEV, STANDALONE, homologação)

1. Cadastrar natureza de remessa para depósito (5905/6905, papel Remessa) e de retorno
   (1906/2906, papel Retorno).
2. Liberação para armazém de terceiro → "Emitir remessa" → confirmar → transmitir em
   homologação; esperado: Autorizada.
3. Expedir dois romaneios da liberação.
4. Montar o XML de retorno do armazém com `NFref` = chave da remessa; importar; conferir o
   vínculo automático.
5. Reconciliar com os dois romaneios; conferir os quatro saldos na liberação.
6. Pela tela: tentar cancelar a remessa com retorno e estornar o romaneio reconciliado — ambos
   recusados com a mensagem certa.
7. Subir em modo SAPB1 e conferir que nada da feature aparece.

## 8. Fora do escopo

- Modo SAPB1.
- Relatório consolidado de remessas por armazém.
- Tratamento fiscal da quebra técnica (fica na Conferência de Armazém) e do excesso.
- Remessa complementar ou parcial.
- Natureza padrão de remessa na filial (D9).
