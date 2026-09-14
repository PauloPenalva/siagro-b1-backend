# GAC-1164 — Conferência de Saldo de Armazém (Perda/Sobra) — Design

- **Data:** 14/09/2026
- **Chamado:** GAC-1164, "Processo de conferência de saldos de armazém (saldo de estoque em posse de
  terceiros físico x sistema)"
- **Status:** aprovado pelo usuário

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
