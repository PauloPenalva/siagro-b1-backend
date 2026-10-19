---
status: rascunho 2026-10-19 (aguardando aprovação do usuário)
branch: feature/allocation-any-supplier (backend + frontend)
---

# Design — Alocar romaneio de compra em contrato de outro fornecedor/armazém, por romaneios de compensação

## Contexto e porquê

Na **Alocação de Romaneios de Compra** (`/purchase-orders/allocations`), um romaneio de compra (tipo 8)
só podia ir para contrato do **mesmo fornecedor e produto**. Quando o romaneio é lançado com
**fornecedor e armazém errados**, não havia contrato possível: o usuário estornava a entrega e ela ficava
pendente de alocação.

A primeira versão desta branch (be `cf4c574`, fe `4324598`) liberou a lista para contratos de qualquer
fornecedor do produto e pôs um aviso de divergência — mas alocava o romaneio **como estava**, com o
fornecedor e o armazém errados. O usuário pediu (19/10/2026) que o efeito seja **igual ao "Trocar
liberação" da carga** (GAC-1177, `ShippingTransactionsChangeReleaseService`, spec
`2026-09-16-gac-1177-shipping-release-change-design.md`): a correção é **explícita**, por romaneios de
compensação — um estorno devolve a origem e um romaneio novo entra no destino.

## Decisões do usuário (19/10/2026)

1. O efeito é o da troca de liberação: original intacto e marcado, **estorno na origem**, **romaneio novo
   no destino** (fornecedor e armazém do contrato escolhido).
2. **Data** do estorno e do romaneio novo = **data do romaneio original** (`TransactionDate`/`TransactionTime`).
3. **Nota fiscal do produtor e Documento de Entrada passam para o romaneio novo.**
4. Romaneio já parcialmente alocado: troca **só o saldo** (`AvaiableVolumeToAllocate`).
5. Conferência de saldo de armazém (GAC-1164) **Aprovada** no período **trava**, como na carga.
6. **Troca parcial com nota é recusada**: se o original já tem alocação (troca parcial) **e** tem NF do
   produtor (`InvoiceNumber` ou `ChaveNFe` preenchidos) **ou** vínculo de Conciliação Fiscal de Compras,
   a troca é recusada — o usuário desaloca antes e faz a troca total.

## Objetivos

- Corrigir, pela tela de alocação, um romaneio de compra lançado com fornecedor e/ou armazém errados,
  deixando saldo de armazém, saldo de fornecedor e contrato coerentes **na data do romaneio**.
- Rastro completo: dá para ver o original, o estorno e o novo, ligados por um registro com motivo,
  usuário e data.

## Não-objetivos

- Trocar o **produto** (continua obrigatório o mesmo `ItemCode`).
- Desfazer a troca como operação própria (corrige-se trocando de novo o romaneio novo, como no GAC-1177).
- Trocar romaneios que não sejam tipo 8 (`Purchase`). Devolução (9) e complementos (10/11) e descarga
  (16/17) ficam de fora.
- Tela nova. Tudo acontece no diálogo de alocação que já existe.

## Anti-objetivos (seria fracasso mesmo "funcionando")

- Regravar fornecedor/armazém do romaneio original no lugar (foi justamente o que o usuário recusou no
  GAC-1177 v1).
- Saldo de armazém ou de contrato com resíduo depois da troca (estorno que não espelha o original).
- O original ou o estorno continuarem aparecendo na grade de alocação com saldo.
- NF do produtor ficar duplicada em dois romaneios vigentes.

## Comportamento

### Quando vira troca

No diálogo "Selecionar Contrato de Compra", ao confirmar:

- contrato com **mesmo fornecedor e mesmo armazém** (`CardCode` e `DeliveryLocationCode` = `WarehouseCode`
  do romaneio) → **alocação normal**, como hoje;
- contrato com fornecedor **ou** armazém diferente → o aviso de divergência (já existe) passa a dizer que
  serão gerados um estorno e um romaneio novo, e pede **motivo obrigatório**. OK executa a troca.

Só armazém diferente (mesmo fornecedor) também é troca.

### Os três lançamentos (todos com a data e hora do romaneio original)

Seja `S` = `AvaiableVolumeToAllocate` do original no momento da troca (saldo não alocado), e
`r = S / NetWeight` do original.

| Lançamento | Tipo | Fornecedor / armazém | Pesos | Contrato |
|---|---|---|---|---|
| **Original** | 8 | intacto | intactos | alocações existentes ficam; saldo alocável cai em `S` (ver invariante) |
| **Estorno na origem** | 9 (`PurchaseReturn`) | do original | `Gross`, descontos e `Net` = valores do original × `r` (com `r = 1`, espelho exato do original, como o GAC-1177 faz) | **nenhuma alocação**; saldo alocável **0** |
| **Romaneio novo** | 8 (`Purchase`), confirmado | `CardCode/CardName` do contrato; `WarehouseCode` = `DeliveryLocationCode` do contrato | os mesmos do estorno (`Net` = `S`) | alocado no contrato escolhido com o volume informado no diálogo |

- O volume informado no diálogo pode ser menor que `S`: o romaneio novo nasce com `S` e o restante fica
  **pendente de alocação nele** (já com o fornecedor certo). Validações de volume atuais continuam
  (≤ saldo do romaneio, ≤ saldo do contrato).
- Inspeções de qualidade, `ProcessingCostCode`, royalties e demais dados de pesagem (veículo, motorista,
  ticket) são copiados do original para o romaneio novo — mesmo caminho de `CreatePurchaseLegAsync`.
- **Preços de serviço** (secagem, limpeza, recepção, expedição) **zerados** no estorno e no romaneio novo:
  as tarifas já estão no original (mesma regra do GAC-1177, senão relatórios de serviço somam em dobro).
- Lote (`StorageAddressCode`) nulo no estorno e no novo, como na perna de compra do GAC-1177.

### Nota fiscal e Documento de Entrada

- **Troca total** (original sem nenhuma alocação, `S = NetWeight`): `InvoiceNumber`, `InvoiceSerie`,
  `InvoiceQty`, `ChaveNFe` **vão** para o romaneio novo e são **limpos** no original; os vínculos de
  **Conciliação Fiscal de Compras** (`PURCHASE_FISCAL_RECONCILIATIONS`) do original são **reapontados**
  para o romaneio novo.
  - *Decisão do Claude:* só o `StorageTransactionKey` do vínculo é reapontado. A linha do Documento de
    Entrada, o fornecedor do documento e o financeiro **não** são alterados. Numa troca total o original
    não tem alocação, então os vínculos dele só podem ser de linhas **sem** contrato (linha com contrato
    exige alocação). O título firme dessa linha nasce pelo caminho normal quando o usuário informar o
    contrato na linha.
- **Troca parcial** (o original tem alocação): permitida **só** se o original não tiver NF do produtor
  nem vínculo de conciliação (decisão 6). Nesse caso não há nota a mover.

### Saldos resultantes (conferir nos testes)

- Armazém de origem: original (+) e estorno (−) se anulam na parte trocada.
- Armazém de destino: recebe o romaneio novo.
- Contrato de destino: +volume alocado. Contratos onde o original já estava alocado: inalterados.
- Grade de alocação: o original some (saldo 0 até alguém remover uma alocação dele); o estorno nunca aparece (saldo 0); o novo aparece só se
  sobrou saldo não alocado.

### Registro da troca

Entidade nova `PurchaseTransactionChange` / tabela `PURCHASE_TRANSACTION_CHANGES` (migration nova),
irmã de `ShippingReleaseChange` (aquela exige carga e não serve aqui):

Key, OriginalStorageTransactionKey, ReturnStorageTransactionKey, NewStorageTransactionKey,
PurchaseContractKey (destino), OriginalCardCode, OriginalWarehouseCode, NewCardCode, NewWarehouseCode,
Quantity (`S`), AllocatedVolume, Reason (500, obrigatório), BaseEntity (CreatedAt/By…).

`StorageTransaction` ganha `ReplacedByPurchaseTransactionChangeKey` (no original) e
`PurchaseTransactionChangeKey` (no estorno e no novo), ambos anuláveis.

*Decisão do Claude:* como um romaneio pode ser trocado mais de uma vez, a marca do original não é uma
chave única. O original é achado pelas linhas de `PURCHASE_TRANSACTION_CHANGES` que apontam para ele
(`OriginalStorageTransactionKey`). Só o estorno e o novo carregam `PurchaseTransactionChangeKey`.

**Invariante do saldo alocável** (*decisão do Claude*, fonte única em
`StorageTransaction.RecalculateAvailableVolume`):

```
original : NetWeight − Σ|alocações| − Σ Quantity das trocas em que ele é o original
estorno  : 0  (nunca alocável)
novo     : NetWeight − Σ|alocações|   (como qualquer 8)
```

Com isso, se depois alguém **remover uma alocação** do original, o volume liberado volta a ser
alocável (ou trocável de novo) e não some. A trava "não substituído antes" deixa de existir: o original
pode ser trocado de novo enquanto tiver saldo.

### Travas (todas antes de qualquer escrita; tudo numa transação, rollback total em erro)

1. Romaneio tipo 8, `Confirmed`, com `S > 0`.
1b. Troca parcial (original com alocação) com NF do produtor ou vínculo de conciliação → recusa com
   "Romaneio com nota fiscal e parte alocada: remova as alocações antes de trocar o fornecedor/armazém."
2. Contrato de destino: mesmo produto, Aprovado (não Finalizado), saldo suficiente.
3. Conferência de saldo de armazém **Aprovada**, mesmo produto, armazém de origem **ou** destino,
   `ReferenceDate >= data do romaneio` → recusa e pede o cancelamento da conferência
   (reaproveitar `ShippingReleaseChangeReconciliationGuard`, generalizando a mensagem "data da carga").
4. Romaneio com **baixa de royalty ITS ativa** → recusa (`RoyaltyRules.EnsureNoActiveWriteOffAsync`).
5. Romaneio **reconciliado em remessa para depósito** → recusa
   (`StorageDepositRules.EnsureTransactionsNotReconciledAsync`).
6. Romaneio que é perna de **Entrada em armazenagem própria** (`STORAGE_ENTRY_TRANSACTIONS`) ou de uma
   **Expedição de Grãos** (`SHIPPING_TRANSACTIONS`) → recusa: esses fluxos têm o próprio estorno/troca.

### Proteções depois da troca (*decisões do Claude*, por analogia com o GAC-1177)

- **Cancelar ou voltar a Pendente** o original, o estorno ou o romaneio novo é recusado
  (`StorageTransactionsCancelService`, `StorageTransactionsReverseService`), como já acontece com os
  romaneios de troca de liberação. Senão o estorno sobra como resíduo. Para corrigir, troca-se de novo.
- **Alocação comum não aceita mais divergência**: `PurchaseContractsAllocationCreateService` passa a
  recusar contrato com fornecedor ou armazém diferente do romaneio, para a troca não ser contornada pela
  API. Os fluxos internos que alocam de propósito em outro contrato (estornos do GAC-1177,
  `ExecuteReversalAsync`, Expedição de Grãos) **não podem** quebrar. O construtor confere cada chamador
  e, se algum alocar legitimamente com divergência, a trava vale só para a ação pública da tela.
- **Conciliação Fiscal de Compras**: o **estorno** da troca não aparece como pendente de NF. O original
  conta como pendente só pela parte não trocada (`NetWeight − Σ Quantity das trocas`). Isso vale para as
  consultas da tela e para o `PurchaseFiscalReconciliationPlanner`, que não deve sugerir par para o
  estorno.

## Restrições técnicas (aprendidas no GAC-1177)

- Alocação soma pelo **banco**: `SaveChanges` antes de cada alocação.
- O estorno 9 confirma pelo ramo de compra e recalcula pela tabela — **sobrescrever** pesos/descontos
  depois do confirm para espelhar o original.
- Usar `StorageTransactionCopyFactory`, `storageCreateService`/`storageConfirmedService` em
  `CommitMode.Deferred`, `KeepNames` (SAP fora devolve nome nulo).
- Código de romaneio não é único entre séries: testes e SQL de verificação por chave.
- Funciona em STANDALONE e SAPB1 (nada aqui depende do modo, salvo o cadastro de parceiro/armazém).

## Frontend

- Diálogo de alocação: texto do aviso de divergência passa a explicar o estorno + romaneio novo e ganha
  um campo **Motivo** obrigatório (reaproveitar `DialogHelper.promptDialog` ou pôr o TextArea no aviso).
- Ação OData nova (ex.: `PurchaseContractsAllocationsChangeSupplier(StorageTransactionKey,
  PurchaseContractKey, Volume, Reason)`), registrada em `ServerRoutes.ts`.
- Toast de sucesso cita os códigos do estorno e do romaneio novo.

## O que fica com o construtor

Nome final da ação e dos campos, arredondamento do rateio proporcional (3 casas, resíduo no `Net`
para fechar exatamente `S`), e onde exatamente reaproveitar código do `ShippingTransactionsChangeReleaseService`
(extrair helpers compartilhados é bem-vindo, sem mudar o comportamento do GAC-1177).
