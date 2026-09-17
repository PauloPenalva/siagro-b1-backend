# GAC-1164 — Conferência de Saldo de Armazém (Perda/Sobra) — Design

- **Data:** 14/09/2026
- **Chamado:** GAC-1164, "Processo de conferência de saldos de armazém (saldo de estoque em posse de
  terceiros físico x sistema)"
- **Status:** aprovado pelo usuário
- **Revisão 17/09/2026:** a Perda passa a consumir as **liberações** e a Sobra sai de cena. A §9
  prevalece sobre as §§2 a 7 onde elas divergirem.

## 1. Contexto

De tempos em tempos o armazém de terceiros informa um saldo físico diferente do sistema, com quebra
ou acréscimo. Hoje a equipe escolhe um contrato de compra ao acaso, normalmente o que está
movimentando, e aplica a quebra nele. Isso mistura no contrato de um produtor um evento de estoque
que não tem nada a ver com ele, e o rastro fica errado.

O chamado descreve dois casos de natureza diferente:

1. **Quebra ou sobra de mercadoria da Yokotobi em poder de terceiros.** Exemplos: quebra técnica,
   caminhão tombado. É um evento de **estoque** e não há contraparte a cobrar. **É o escopo deste
   design.**
2. **Falta de mercadoria do produtor.** Ele não tem mais saldo a carregar e pode já ter recebido o
   pagamento, por adiantamento ou por nota. É um evento de **contrato e financeiro** (washout) e
   terá spec próprio (ver §8).

## 2. Decisões

| Tema | Decisão |
|---|---|
| Como lançar | Via **Conferência** formal; não existe romaneio de perda avulso |
| Escopo | Só armazéns de **terceiros** (`WarehouseComplement.IsOwn = false`; sem linha de complemento = terceiros) |
| Motivos | Cadastro editável |
| Aprovação | Rascunho → Em aprovação → Aprovada ou Rejeitada; uma Aprovada pode ser Cancelada. Quem enviou pode aprovar, como nos contratos |
| Anexo do extrato | Opcional |
| Parceiro do romaneio | O próprio armazém: `CardCode = WarehouseCode`, preenchido pelo servidor |

**Por que o parceiro é o próprio armazém.** Nos dois modos o armazém é um parceiro de negócio com
`QryGroup23 = 'Y'`, e o sistema usa `Warehouse.Code = CardCode` (`SAP/WarehouseService.cs:37`,
`WarehouseService.cs:146`).

## 3. Romaneio de Perda/Sobra

### 3.1 Tipos

`StorageTransactionType` ganha dois valores:

- `WarehouseLoss = 13`, "Perda Armazém"
- `WarehouseGain = 14`, "Sobra Armazém"

Nenhum valor existente foi reaproveitado:

- `Adjustment(2)` e `Transfer(3)` estão mortos, mas aparecem em rótulo de relatório.
- `TechnicalLoss(4)` tem semântica de **lote** e é gerado por
  `StorageAddressesTechnicalLossCalculatorService`, que atende a cobrança de armazenagem.

### 3.2 Onde os tipos entram

**Saldo de armazém.** `StorageTransactionsWarehouseBalanceService.CalculateAsync` passa a ser:

```
+ Purchase(8) + SalesShipmentReturn(12) + WarehouseGain(14)
− PurchaseReturn(9) − SalesShipment(7) − WarehouseLoss(13)
```

- Mantém o filtro de status Confirmed ou Invoiced.
- Ganha o parâmetro opcional `DateTime? upToDate`, com filtro `TransactionDate < upToDate.Date.AddDays(1)`.
- Linhas com `TransactionDate` nulo contam, para que o saldo "até hoje" seja igual ao saldo atual.

**Extrato.** `StorageStatementReportHelper` ganha os rótulos; a Sobra é entrada e a Perda é saída.

**Ficam fora, de propósito:**

- `StorageTransaction.IsAllocatable`
- `ShipmentReleasesRecalculateShippedService.AffectsShippedQuantity`
- `ShipmentReleaseMovementGuardService`
- os `allowedTypes` de `PurchaseContractsAllocationCreateService`
- as fórmulas de lote (`StorageAddress.Balance` e SQL `IN (0,6)/(1,7,4)`). O romaneio não tem lote,
  então elas não precisam mudar.

### 3.3 Geração

O romaneio é criado **somente** pelo serviço de aprovação da Conferência, já **Confirmado**:

| Campo | Valor |
|---|---|
| `TransactionType` | 13 se a diferença for negativa, 14 se for positiva |
| `TransactionDate` | `ReferenceDate` da conferência |
| `GrossWeight` / `NetWeight` | `abs(Difference)` |
| `AvaiableVolumeToAllocate` | 0 |
| `StorageAddressCode` | nulo |
| `CardCode` / `WarehouseCode` / `ItemCode` / `UnitOfMeasureCode` | os da conferência |
| Origem | `TransactionCode.WarehouseReconciliation = 13` (valor novo) |
| Número | da sequência normal de `StorageTransaction` |

### 3.4 Blindagens

- **Confirmação.** `StorageTransactionsConfirmedService.Processing` ganha um `case` explícito para 13
  e 14 que lança exceção. Hoje um tipo desconhecido cai no `default`, `ExecutePurchaseTransactionAsync`,
  que aplica descontos e torna o volume alocável.
- **Cancelar e estornar.** `StorageTransactionsCancelService` (verificação de origem, linha ~67) e
  `StorageTransactionsReverseService` já recusam romaneio de outra origem. Com isso, a tela de
  Romaneios não mexe nos romaneios da Conferência. Os dois guards ganham teste.

## 4. Conferência — `WarehouseReconciliation`

Tabela `WAREHOUSE_RECONCILIATIONS`, derivada de `DocumentEntity`.

### 4.1 Campos

| Campo | Observação |
|---|---|
| `DocNumberKey` / `Code` / `BranchCode` | sequência seed com código 13 e prefixo "CS" |
| `WarehouseCode` / `WarehouseName` | obrigatório |
| `ItemCode` / `ItemName` / `UnitOfMeasureCode` | obrigatório |
| `CardCode` / `CardName` | igual ao armazém |
| `ReferenceDate` | data do extrato do armazém |
| `ReportedBalance` | saldo físico informado pelo armazém, DECIMAL(18,3), ≥ 0 |
| `SystemBalance` | snapshot do saldo de armazém na `ReferenceDate` |
| `Difference` | `ReportedBalance − SystemBalance` |
| `ReasonKey` | FK para o motivo |
| `Comments` | observação |
| `SentBy` / `SentAt` | envio para aprovação |
| `ApprovedBy` / `ApprovedAt`, `CanceledBy` / `CanceledAt` | herdados |
| `ApprovalComments` | aprovação ou rejeição |
| `CancellationReason` | obrigatório no cancelamento |
| `StorageTransactionKey` | romaneio gerado |
| `RowVersion` | concorrência |

### 4.2 Status

`WarehouseReconciliationStatus`: Draft=0, InApproval=1, Approved=2, Rejected=3, Cancelled=4.

```
Draft ──enviar──> InApproval ──aprovar──> Approved ──cancelar──> Cancelled
  ^                   │  │
  └────retirar────────┘  └──rejeitar──> Rejected (final)
Draft ──cancelar──> Cancelled
```

### 4.3 Regras

Todas as validações rodam **antes** de `BeginTransactionAsync`. Validação feita dentro do `try` vira
`DefaultException` e esconde a mensagem de negócio. As mensagens usam as chaves
`WAREHOUSE_RECONCILIATION_*` em `Resource.resx` e `Resource.pt-br.resx`.

**Criar e editar** (editar só em Draft):

- o armazém é de terceiros;
- o motivo existe e está ativo;
- `ReportedBalance ≥ 0`;
- `ReferenceDate` não está no futuro;
- `ReferenceDate` não é anterior à `ReferenceDate` da última conferência Aprovada do mesmo
  armazém+produto;
- existe no máximo uma conferência Draft ou InApproval por armazém+produto. O serviço garante isso,
  e um índice único filtrado `[Status] IN (0,1)` sobre `(WarehouseCode, ItemCode)` reforça (o
  InMemory não testa índice).

**Enviar para aprovação** (só Draft):

- recalcula `SystemBalance` e `Difference` na `ReferenceDate`;
- recusa diferença zero.

**Retirar:** InApproval → Draft.

**Rejeitar:** InApproval → Rejected, com comentários.

**Aprovar** (só InApproval):

1. Reavalia as regras de criação, exceto a de "uma aberta", porque ela mesma é a aberta.
2. Recalcula `SystemBalance` e `Difference` na `ReferenceDate` e congela o snapshot.
3. Recusa diferença zero.
4. Na Perda, recusa se o saldo de armazém **atual** menos a quantidade ficar negativo. É a mesma regra
   da confirmação de SalesShipment (`StorageTransactionsConfirmedService.cs:~100`).
5. Gera o romaneio (§3.3) e grava `StorageTransactionKey`, tudo no mesmo `SaveChanges` e na mesma
   transação.

**Cancelar:**

- **Draft** → Cancelled, sem nenhum efeito.
- **Approved** → Cancelled, com três exigências:
  - ser a **última** Aprovada do armazém+produto, ordenando por `ReferenceDate` e depois por
    `ApprovedAt`;
  - na Sobra, o saldo atual menos a quantidade continuar ≥ 0;
  - `CancellationReason` preenchido.

  O romaneio é cancelado via `StorageTransactionsCancelService.ExecuteAsync(key, user,
  TransactionCode.WarehouseReconciliation)`.
- **Rejected e Cancelled** não podem ser cancelados.
- **Exclusão** não existe: o DELETE é recusado.

### 4.4 Prévia do saldo

A function `WarehouseReconciliationsGetBalancePreview(WarehouseCode, ItemCode, ReferenceDate)` recebe
`ReferenceDate` como string `yyyy-MM-dd` e devolve `WarehouseReconciliationBalancePreviewDto`:

- `SystemBalance`
- `IsOwnWarehouse`
- `LastApprovedReferenceDate`
- `HasOpenReconciliation`

### 4.5 Anexos

`WarehouseReconciliationAttachment` é uma cópia do padrão `PurchaseContractAttachment`
(`VARBINARY(MAX)`). Tem upload por action com base64, listagem e download por function, e exclusão.

## 5. Motivos — `WarehouseReconciliationReason`

- **Tabela:** `WAREHOUSE_RECONCILIATION_REASONS`, com `Key`, `Code` VARCHAR(20) único,
  `Description` VARCHAR(100), `Active` e auditoria.
- **Exclusão:** motivo em uso não pode ser excluído, apenas desativado. Motivo inativo não aparece
  para novas conferências.
- **Seed:** Quebra técnica, Sinistro/Tombamento, Umidade/Secagem, Divergência de pesagem e Sobra de
  estoque.

## 6. Frontend

**Conferência de Saldo de Armazém** (`warehouse-reconciliations`)

- Lista com filterbar (armazém, produto, status, período e motivo) e exportação Excel.
- Formulário de inclusão e edição:
  - value help de armazém; ao escolher, chama a prévia e recusa armazém próprio com MessageBox;
  - value help de produto, data de referência, saldo informado e Select de motivos ativos;
  - saldo do sistema vindo da prévia e diferença calculada ao vivo;
  - observação e anexos.
- Detalhe com botões conforme o status: Editar e Enviar para aprovação (Draft), Retirar (InApproval),
  Cancelar (Draft ou Approved, com diálogo de motivo). Mostra se a diferença virou Perda ou Sobra,
  com a quantidade. O romaneio não tem navegação na entidade, então não há link para ele; ele
  aparece na tela de Romaneios de Movimentação com o tipo "Perda Armazém" ou "Sobra Armazém".

**Aprovação** (`warehouse-reconciliations/approval`)

- Espelha `purchaseContracts/approval`: lista e detalhe com diálogo Aprovar/Rejeitar.
- O detalhe mostra o snapshot ao lado da prévia ao vivo.

**Motivos** (`warehouse-reconciliation-reasons`)

- CRUD simples com o switch Ativo.

**Menus e rótulos**

- Os três itens entram no menu "storage" por migration do `CommonContext` (`MENU_ITEMS` com
  Key = rota e `ROLE_MENUS` ADMIN).
- Os rótulos 13/14 entram em:
  - `formatter.formatStorageTransactionType`;
  - os valueMaps de exportação: ownershipTransfers, storageTransactions, storageInvoices,
    purchaseContracts/allocation e purchaseOrders/allocation;
  - o Filterbar de storageTransactions.
- Os rótulos **não** entram como opção criável nos Forms de storageTransactions e shippingTransaction.

## 7. Testes e verificação

**Testes** (xUnit + EF InMemory, `SiagroB1.Application.Tests/WarehouseReconciliations/`)

- **Saldo de armazém:**
  - Sobra soma e Perda subtrai;
  - romaneio posterior à data fica fora;
  - a própria data entra, inclusive com hora;
  - data nula conta;
  - SalesShipment é recusado depois de uma Perda.
- **Guards:** um teste por guard de criação e edição, incluindo "sem linha de complemento =
  terceiros".
- **Envio para aprovação:** recalcula o snapshot e recusa diferença zero.
- **Aprovação:**
  - Sobra gera o tipo 14 e Perda gera o 13;
  - diferença zero é recusada;
  - Perda que negativa o saldo atual é recusada;
  - snapshot de rascunho antigo é recalculado;
  - origem, data e quantidade do romaneio saem corretas.
- **Cancelamento:**
  - Draft cancela;
  - "não é a última" é recusado;
  - Sobra que negativaria é recusada;
  - Perda cancelada devolve o saldo;
  - o romaneio fica Cancelled.
- **Guards de origem:** `StorageTransactionsCancelService` e `StorageTransactionsReverseService`
  recusam a origem 13.
- **Motivos:** código duplicado, exclusão de motivo em uso e motivo inativo são recusados.

**Verificação**

- Build, testes, has-pending-model-changes, ts-typecheck, lint e ui5lint.
- Roteiro no navegador a partir da home, conforme o plano de implementação.

## 8. Fora de escopo: washout (caso 2), já decidido para o spec futuro

- O washout é um **evento do contrato de compra**, e não um contrato de venda "WO". Um contrato de
  venda fictício:
  - inflaria volume e receita de venda em listas, relatórios, liberações, conferência e notificações;
  - deixaria um saldo que nunca é entregue;
  - não tem vínculo com a compra;
  - não resolve o encontro de contas, que ainda não existe.
- Registro de washout parcial ou total no contrato, com aprovação. O saldo passa a ser
  `TotalVolume − AllocatedVolume − WashedOutVolume`.
- Reduz o provisório a pagar do volume lavado. Gera um título **a Receber** do produtor com origem
  Washout e `PurchaseContractKey`, formando o rastro contrato → washout → título → baixa.
- **Valoração:** preço do contrato mais a diferença de mercado e/ou multa.
- **Baixa:** manual até existir o encontro de contas (Fase 3 do financeiro,
  `FinancialSettlementOrigin.Netting`).
- **Nota:** hoje a mensagem do cancelamento de contrato já sugere "considere fazer washout"
  (`PurchaseContractsCancelService.cs:28`).

## 9. Revisão 17/09/2026 — a Perda consome as liberações

### 9.1 O problema encontrado em teste

**Homologação** (CS000001, armazém F023998, produto P012755):

- a conferência gravou `SystemBalance = 0`;
- o armazém informou 175.000 kg, e a conferência gerou uma Sobra de 175.000;
- as liberações ativas somavam, no mesmo momento, **196.430 kg** a embarcar.

A causa é que, na liberação `Standard`, a Expedição registra a Compra(8) **só ao embarcar**, junto
com a Saída(7). Por isso o saldo por romaneios da §3.2 fica em zero enquanto o grão está parado no
armazém. O saldo real do armazém de terceiros é **liberado − embarcado**.

**Desenvolvimento** (F024813, P026031):

- a conferência calculou 28.600 kg, porque o grão veio de uma devolução de venda (tipo 12);
- a Expedição continuou oferecendo 29.400 kg, porque a Perda não consome liberação.

Exemplo do usuário: dois contratos de 20.000 kg e uma quebra técnica de 1.000 kg. O extrato diz
39.000 kg, mas os contratos continuam somando 40.000. Sobram 1.000 kg numa liberação que nunca serão
embarcados.

Não é washout. A quebra técnica vem da demora da **Yokotobi** em retirar o grão.

### 9.2 Decisões (usuário, 17/09/2026)

| Tema | Decisão |
|---|---|
| Quem absorve a perda | A Yokotobi. O contrato do produtor conta como **entregue** e o produtor recebe pelo volume cheio. |
| Distribuição entre liberações | O **usuário escolhe** na conferência quanto cai em cada liberação. |
| Sobra | **Sai.** A conferência aceita só diferença negativa; sobra não acontece na prática. |

### 9.3 Saldo do sistema

`SystemBalance` passa a ser o saldo a embarcar das liberações do armazém+produto, apurado na
`ReferenceDate`. O saldo por romaneios da §3.2 deixa de ser usado aqui.

- **Liberações consideradas:** `DeliveryLocationCode = WarehouseCode`, produto do contrato
  (`PurchaseContract.ItemCode`, como em `ShipmentReleasesBalanceService`), com status `Actived` ou
  `Paused`, e também `Completed` quando o saldo na data for > 0. Pausada entra porque o grão existe
  fisicamente. `Pending` e `Cancelled` ficam de fora.
- **Cálculo na data:**
  - parte do `ReleasedQuantity − ShippedQuantity` de hoje;
  - soma de volta o que foi romaneado contra essas liberações com `TransactionDate` posterior ao fim
    da `ReferenceDate`, pelos mesmos tipos e sinais de
    `ShipmentReleasesRecalculateShippedService.CalculateShippedAsync` conforme a origem, sem os
    cancelados;
  - exclui as liberações com `ReleaseDate` posterior à data.
- **Liberação `Completed`:** `ShipmentReleasesCloseService` finaliza sem zerar o Released − Shipped
  que sobrou — aquele saldo já foi dado por encerrado e não conta mais. Contribui SÓ com o que foi
  romaneado contra ela depois da data (`balanceAtDate = consumedAfter`); o saldo de hoje sai 0. Não
  recebe perda (§9.4).

Com a data de hoje, o número é igual ao da `/shipping-transaction` somado ao das liberações pausadas.

### 9.4 Distribuição — `WarehouseReconciliationRelease`

Tabela filha nova `WAREHOUSE_RECONCILIATION_RELEASES`:

| Campo | Observação |
|---|---|
| `Key` | PK |
| `WarehouseReconciliationKey` | FK para a conferência, `Cascade` |
| `ShipmentReleaseKey` | FK para a liberação, `Restrict` |
| `Quantity` | DECIMAL(18,3), > 0 |
| `PurchaseStorageTransactionKey` | Compra(8) gerada; nula na origem sem perna de compra |
| `LossStorageTransactionKey` | Perda(13) gerada |

Único por `(WarehouseReconciliationKey, ShipmentReleaseKey)`. A coleção é gravada com a conferência
(edição em Draft).

**Regras** (conferidas no envio e novamente na aprovação, antes da transação):

- `Difference < 0`; zero ou positiva é recusada;
- `Σ Quantity = |Difference|`, com a folga de arredondamento do módulo;
- cada liberação pertence ao armazém+produto, está **`Actived`** e tem saldo de **hoje** ≥ `Quantity`
  (a pausada aparece na grade, mas não aceita perda, porque `ShipmentReleaseMovementGuardService`
  recusa movimento nela);
- o contrato de compra da liberação `Standard` não está `Finished`, com mensagem própria antes de
  `PurchaseContractsAllocationCreateService`;
- com uma única liberação elegível, a tela preenche a linha sozinha.

### 9.5 Aprovação — romaneios gerados por linha

Todos já **Confirmados**, com `TransactionDate = ReferenceDate`, origem
`TransactionCode.WarehouseReconciliation` e `ShipmentReleaseKey` da linha.

- **Liberação `Standard`:** o mesmo par de `ShippingTransactionsCreateService`, com a Perda no lugar da
  Saída:
  1. **Compra(8)** de `Quantity`, confirmada e alocada no contrato via
     `PurchaseContractsAllocationCreateService`. Consome a liberação e dá o contrato por entregue.
  2. **Perda(13)** copiada da Compra, sem lote, que dá a saída no armazém.
- **`OwnershipTransfer` / `SalesReturn`** (`ReleaseOriginRules.ShipsWithoutPurchaseLeg`): o grão já
  entrou antes, então gera só a **Perda(13)**.
- **Recálculo:** o recálculo do embarcado de cada liberação roda **depois do commit**, como em
  `ShippingTransactionsCreateService`.
- **`WarehouseReconciliation.StorageTransactionKey`:** passa a ser anulável e não é preenchida em
  conferência nova; as chaves vivem nas linhas.

A regra 4 da aprovação na §4.3 (Perda limitada ao saldo por romaneios) **sai**; o limite passa a ser o
saldo das liberações.

### 9.6 Mudanças nos pontos que a §3.2 deixava de fora

- **`ShipmentReleasesRecalculateShippedService`:**
  - `AffectsShippedQuantity` inclui `WarehouseLoss`;
  - no ramo sem perna de compra, `CalculateShippedAsync` soma `SalesShipment + WarehouseLoss −
    SalesShipmentReturn`.
  - No ramo `Standard` nada muda, porque quem consome é a Compra(8).
- **`ShipmentReleaseMovementGuardService`:** passa a cobrir `WarehouseLoss`.
- **`StorageTransactionsWarehouseBalanceService`:** a fórmula continua igual. Na liberação `Standard`,
  a Compra + Perda se anulam, como a Compra + Saída da Expedição.
- **`allowedTypes` da alocação:** não muda, porque a perna alocada é a Compra(8).

### 9.7 Cancelamento de conferência aprovada

Continua valendo só para a **última** Aprovada do armazém+produto. Para cada linha, numa única
transação:

- cancela a Perda(13);
- se houver Compra(8), apaga a alocação do contrato e cancela a Compra, na mesma sequência de
  `ShippingTransactionsReverseService`.

Depois do commit, recalcula o embarcado de cada liberação tocada. A exigência "Sobra que negativaria o
saldo" sai.

**Legado:** conferência Aprovada **sem** linhas (anterior a esta revisão) cancela como antes, pelo
`StorageTransactionKey`.

### 9.8 Prévia do saldo

`WarehouseReconciliationBalancePreviewDto` ganha `Releases`, com uma linha por liberação considerada:

- `ShipmentReleaseKey`, código da liberação e status;
- contrato de compra (código) e produtor (`CardCode`/`CardName`);
- `BalanceAtReferenceDate`, `CurrentBalance` e `CanReceiveLoss` (`Actived` e com saldo hoje).

### 9.9 Frontend

- **Formulário:** a grade "Distribuição da perda" fica abaixo do saldo do sistema.
  - Colunas: contrato, produtor, liberação, saldo na data, saldo hoje e Quantidade da perda
    (editável só se `CanReceiveLoss`).
  - Rodapé: "Distribuído X de Y".
  - Enviar para aprovação fica bloqueado enquanto não fechar.
- **Detalhe e aprovação:** a mesma grade, só leitura, com os códigos dos romaneios gerados.
- **Diferença positiva:** a tela avisa que a conferência aceita apenas perda.
- **Expedição:** não muda.
- **Rótulo "Sobra Armazém":** continua nos formatters para exibir o histórico.

### 9.10 Testes

- **Saldo na data:**
  - romaneio posterior à data volta a somar;
  - liberação emitida depois da data fica de fora;
  - pausada entra;
  - `Completed` depois da data entra.
- **Aprovação `Standard`:**
  - gera Compra + Perda;
  - consome a liberação;
  - aloca o contrato;
  - o saldo por romaneios não muda.
- **Aprovação sem perna de compra:** gera só a Perda e consome a liberação (recálculo com o tipo 13).
- **Distribuição:** 600 + 400 em duas liberações consome cada uma na medida certa.
- **Recusas:**
  - soma diferente da diferença;
  - linha acima do saldo;
  - liberação pausada;
  - diferença positiva;
  - contrato encerrado;
  - saldo alterado entre o envio e a aprovação.
- **Cancelamento:**
  - devolve o saldo das liberações;
  - remove a alocação;
  - deixa os romaneios cancelados;
  - conferência legada sem linhas cancela pelo `StorageTransactionKey`.
- **Testes de Sobra existentes:** viram testes de recusa.
- **Navegador:** cenário do usuário, a partir da home. Dois contratos de 20.000 kg, perda de 1.000 kg
  distribuída, e a Expedição mostra 39.000 kg.

### 9.11 Dados existentes

- **Localhost:** CS000001 e CS000002 (F024813/P026031) são Perdas aprovadas sem linha. Antes da
  verificação, cancelar pela tela; a regra de legado da §9.7 cobre isso.
- **Homologação:** CS000001 já está cancelada.
- **Produção:** antes do deploy, verificar se alguma conferência foi aprovada lá.
