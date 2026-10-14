# Compensação de adiantamento contra o provisório (compra)

Data: 2026-10-14
Status: aprovado para planejamento
Modo alvo: os dois (`STANDALONE` e `SAPB1`) — vale onde o provisório de compra recebe baixa (composição).

Sub-projeto **3** de "título a pagar firme a partir do romaneio de compra". Depende do sub-projeto 2
(`2026-10-13-purchase-firm-payables-design.md`, em produção na CEAGUI desde 14/10/2026): composição,
`ComposedAmount`, `AvailableToSettleAmount`, encontro de contas em par.

## Problema

O adiantamento a contrato de compra (Fase 1, `2026-09-07-financial-documents-design.md`) é dinheiro já
pago ao produtor. Hoje ele não abate nada: o provisório só sai por baixa em dinheiro, e o crédito do
adiantamento fica parado — ou volta ao caixa por devolução. A Fase 1 reservou a origem
`FinancialSettlementOrigin.AdvanceApplication = 3` e previu a coluna `AppliedAmount`, mas nada as escreve.

## Decisões do usuário

| # | Decisão |
|---|---|
| D1 | **O operador compensa manualmente**, por ação no provisório ("Compensar adiantamento"), como a composição. Nada automático. |
| D2 | **Encerrar contrato é recusado** enquanto algum adiantamento do contrato tiver crédito não aplicado. |
| D3 | **Revincular adiantamento parcialmente aplicado desmembra**: só o crédito não aplicado vai para um adiantamento novo no outro contrato; o original fica com o aplicado. |
| D4 | Lançamento **em par** nos dois títulos (abordagem 1), como o encontro de contas. |

## Escopo decorrente (sem decisão nova)

- **Só compra.** O provisório de venda nunca recebe baixa hoje (sem composição), e o sub-projeto 2 é só compra.
- **Teto do provisório:** compensado ≤ `AvailableToSettleAmount` (composto − baixado) — mesma trava rígida da
  baixa em dinheiro: só se compensa o que já está coberto por documento de entrada.
- **Teto do adiantamento:** compensado ≤ crédito disponível (pago − aplicado).
- Mesmo contrato e mesma moeda (regra D12 do sub-projeto 2).

## Modelo de dados

### Enum

```
FinancialSettlementOrigin += AdvanceTransfer = 7     (valor novo no fim; AdvanceApplication = 3 já existe)
```

### `FINANCIAL_DOCUMENTS` — coluna nova

| Coluna | Tipo | Observação |
|---|---|---|
| `AppliedAmount` | `DECIMAL(18,2) DEFAULT 0` | **só no adiantamento**: persistido-derivado; escritor único é o recalculador |

`[NotMapped] AvailableAdvanceAmount` passa de `SettledAmount` para
`Nature == Advance ? Max(0, Round(SettledAmount − AppliedAmount, 2, ToEven)) : 0` (já exposta no EDM).
`AppliedAmount` exposta no EDM (persistida, sem `AddProperty`).

**Sem backfill:** nenhuma linha `AdvanceApplication`/`AdvanceTransfer` existe hoje → `AppliedAmount = 0` em todos.
Migration: `AddFinancialDocumentAppliedAmount` (só a coluna).

### Recalculador (`FinancialDocumentsRecalculateBalanceService.RecalculateAsync`)

Continua escritor único e continua somando linhas pendentes do ChangeTracker.

- **Adiantamento** (`Nature == Advance`), dois eixos:
  - eixo **aplicado** = Σ linhas `Origin ∈ {AdvanceApplication, AdvanceTransfer}` **com sinal de saída** no
    adiantamento de origem + os estornos (`Reversal`) dessas linhas;
  - eixo **pago** (`SettledAmount`) = todas as demais linhas (Manual, Reversal de Manual, AdvanceRefund)
    **mais** a linha `AdvanceTransfer` de **entrada** (adiantamento nascido do desmembramento — ver abaixo).
  - Status continua derivado de `SettledAmount` × `NetAmount` (inalterado).
- **Provisório:** a linha `AdvanceApplication` entra no `SettledAmount` como qualquer baixa (reduz o disponível,
  pode quitar). `ComposedAmount` inalterado.
- **Entrada × saída da `AdvanceTransfer`:** as duas linhas gravam `Amount` positivo. A de **entrada** é a que
  está no adiantamento **novo** — reconhecido por `OriginType = FinancialDocumentOrigin.FinancialDocument`
  (valor reservado na Fase 1) e `OriginKey` = adiantamento original = `linha.RelatedDocumentKey`; ela conta no
  **pago**. Toda outra `AdvanceTransfer` é de **saída** e conta no **aplicado**. O critério depende só de dados
  do próprio documento, não da ordem de criação.

## Regras

### Compensar (D1)

1. Action no **provisório**: `FinancialDocumentsApplyAdvance(Key, AdvanceKey, Amount, SettlementDate)`.
2. Pré-condições, nesta ordem (mensagens em §Mensagens):
   - provisório existe, `Nature = Provisional`, não cancelado, contrato de **compra**;
   - adiantamento existe, `Nature = Advance`, não cancelado, **mesmo** `PurchaseContractKey`;
   - mesma `Currency`;
   - `Amount > 0`;
   - `Amount ≤ provisório.AvailableToSettleAmount`;
   - `Amount ≤ adiantamento.AvailableAdvanceAmount`.
3. No mesmo `SaveChanges`: linha `AdvanceApplication` no **provisório** (`Amount` positivo, sem conta,
   `RelatedDocumentKey` = adiantamento, `DocumentReference` = código do adiantamento) e linha `AdvanceApplication`
   no **adiantamento** (`Amount` positivo, sem conta, `RelatedDocumentKey` = provisório, `DocumentReference` =
   código do provisório); recálculo dos dois; log nos dois (`FinancialDocumentChangeLogFields.AdvanceApplication`).
   `RowVersion` dos dois títulos protege a concorrência (dupla compensação simultânea do mesmo crédito).
4. Valor sugerido na tela: `min(AvailableToSettleAmount, AvailableAdvanceAmount)`.

### Desfazer

5. Pelo **"Estornar baixa"** existente (`FinancialDocumentsReverseSettlementService`), sobre qualquer das duas
   linhas `AdvanceApplication`: estorna **o par** (mesma busca do `Netting`: par com `FinancialDocumentKey` e
   `RelatedDocumentKey` cruzados, mesma origem e valor, ainda não estornado). Recusa se algum dos dois estiver
   cancelado (como hoje).

### Travas

6. **Estornar pagamento do adiantamento** (linha `Manual` positiva no adiantamento): recusado se, após o
   estorno, `SettledAmount < AppliedAmount`.
7. **Devolver adiantamento** (`FinancialAdvancesRefundService`): devolve só `AvailableAdvanceAmount`.
   - sem crédito disponível → recusa;
   - `AppliedAmount == 0` → comportamento atual (devolve tudo, título `Canceled`);
   - `AppliedAmount > 0` → grava `AdvanceRefund` de `−crédito` (conta no pago), **não** cancela; o título
     fica `Settled`/`PartiallySettled` pelo que sobrou pago = aplicado.
8. **Revincular** (`FinancialAdvancesRelinkContractService`), D3:
   - `AppliedAmount == 0` → comportamento atual (move o título inteiro);
   - `AppliedAmount > 0` → **desmembra**: exige crédito disponível > 0; cria adiantamento **novo** no contrato
     destino (`Nature = Advance`, `Direction = Payable`, `NetAmount` = crédito, `OriginType = FinancialDocument`,
     `OriginKey` = original, mesmos `CardCode`/`BranchCode`/`Currency`, `DueDate` = do original, `Code` pelo
     `DocNumberSequenceService` antes do `SaveChanges`); grava `AdvanceTransfer` de **saída** no original e de
     **entrada** no novo (mesmo valor, sem conta, `RelatedDocumentKey` cruzado); recalcula os dois — o novo
     fica `Settled` (pago = valor), o original fica com crédito 0; log nos dois. Mesmas validações de destino
     do revínculo atual (aprovado, mesmo produtor, filial e moeda).
9. **Desfazer o desmembramento:** "Estornar baixa" sobre qualquer das duas `AdvanceTransfer` estorna o par e
   **cancela** o adiantamento novo (motivo "Transferência de crédito estornada"); recusado se o adiantamento
   novo tiver `AppliedAmount ≠ 0` ou outras baixas além da transferência.
10. **Encerrar contrato de compra** (`PurchaseContractsCloseService`), D2: recusado se algum adiantamento do
    contrato, não cancelado, tiver `AvailableAdvanceAmount > 0`.
11. Cancelar contrato / retirar aprovação: inalterado (já recusa adiantamento pago).
12. `AdvanceTransfer` só nasce pelo revínculo; não há action própria.

## Mensagens (pt-BR, exatas)

| Código | Texto |
|---|---|
| A1 | `Título {código} não é provisório de contrato de compra.` |
| A2 | `Adiantamento {código} não pertence ao contrato do título {provisório}.` |
| A3 | `O adiantamento {código} está em {moeda} e o provisório {provisório} em {moeda do provisório}: compensação entre moedas diferentes não é suportada.` |
| A4 | `Informe um valor maior que zero.` |
| A5 | `Valor acima do disponível para baixa do título {provisório} ({x}).` |
| A6 | `Valor acima do crédito disponível do adiantamento {código} ({x}).` |
| A7 | `O adiantamento {código} tem {x} aplicado: desfaça as compensações antes de estornar o pagamento.` |
| A8 | `O adiantamento {código} não tem crédito disponível para devolver.` |
| A9 | `O adiantamento {código} tem {x} de crédito não aplicado: compense, devolva ou vincule a outro contrato antes de encerrar.` |
| A10 | `O adiantamento {novo} já foi usado: desfaça as compensações dele antes de estornar a transferência.` |
| A11 | `Título cancelado não pode ser compensado.` |

Valores formatados `N2` pt-BR. Moeda pelo `CurrencySymbol` existente (R$/US$).

## Serviços e API

| Serviço | Responsabilidade |
|---|---|
| `FinancialDocumentsApplyAdvanceService` | regras 1–4, `CommitMode` |
| `FinancialDocumentsRecalculateBalanceService` | dois eixos do adiantamento; `AppliedAmount` |
| `FinancialDocumentsReverseSettlementService` | par `AdvanceApplication` (regra 5), trava A7 (regra 6), par `AdvanceTransfer` + cancelamento do novo (regra 9) |
| `FinancialAdvancesRefundService` | regra 7 |
| `FinancialAdvancesRelinkContractService` | regra 8 |
| `PurchaseContractsCloseService` | regra 10 |
| `FinancialDocumentsGetCompositionService` / leitura | candidatos de compensação (adiantamentos do contrato com crédito) |
| `FinancialDocumentsGetByContractService` | DTO ganha `AppliedAmount` e `AvailableAdvanceAmount` |

OData: action `FinancialDocumentsApplyAdvance(Key: Guid, AdvanceKey: Guid, Amount: Edm.Double, SettlementDate: string)`;
function `FinancialDocumentsGetAdvanceCandidates(Key)` → lista PascalCase (`[JsonPropertyName]`) com
`DocumentKey, Code, DocumentDate, SettledAmount, AppliedAmount, AvailableAdvanceAmount, SuggestedAmount`.
Recusas como `BadRequest(string)`.

## Frontend

- **Detail do provisório:** botão **"Compensar adiantamento"** na barra de Baixas (visível: `Nature = Provisional`,
  não cancelado, `AvailableToSettleAmount > 0`); diálogo com a lista de adiantamentos candidatos, valor sugerido
  e data (padrão: hoje, local); recusa exibida.
- **Detail do adiantamento:** formulário com **"Aplicado"** e **"Crédito disponível"**; "Devolver adiantamento"
  mostra o valor = crédito disponível; "Vincular a outro contrato" avisa que vai desmembrar quando
  `AppliedAmount > 0`.
- **Baixas:** formatter de Origem ganha `AdvanceTransfer` → "Transferência de adiantamento" (o de
  `AdvanceApplication` já existe: "Amortização de adiantamento"); "Estornar baixa" habilitado nas duas.
- **Lista de adiantamentos:** coluna "Aplicado" ao lado de "Crédito disponível".
- **Seção Financeiro do contrato:** aplicado e crédito nos adiantamentos.
- Armadilhas da casa: `--` em comentário XML; `core:require` no fragmento (ou `.formatter.x` onde o controller
  expõe `formatter`); enum com `targetType:'any'`; Edm.Decimal como string → `Number()`; data padrão local.

## Testes

- compensar: dentro dos tetos; acima do disponível do provisório (A5); acima do crédito (A6); outro contrato
  (A2); moeda diferente (A3); adiantamento não pago (crédito 0 → A6); provisório/adiantamento cancelado;
- recalculador: pago × aplicado no adiantamento, com linhas pendentes; provisório quita com compensação;
- estorno do par de `AdvanceApplication`; estorno de pagamento recusado (A7);
- devolução: sem aplicação (cancela, como hoje), com aplicação (parcial, não cancela), sem crédito (A8);
- revínculo: simples (move) e desmembrado (novo adiantamento pago com o crédito; original com crédito 0);
  estorno da transferência (cancela o novo) e recusa com o novo já usado (A10);
- encerramento recusado com crédito (A9) e liberado após compensar/devolver/revincular.

## Verificação no navegador (CEAGUI dev)

1. Contrato de compra aprovado com adiantamento pago.
2. Compor um firme no provisório.
3. Compensar parte do adiantamento; conferir pago/aplicado/crédito e o disponível do provisório.
4. Estornar o par; conferir saldos de volta.
5. Compensar de novo até quitar o provisório.
6. Tentar encerrar com crédito sobrando → A9.
7. Revincular a sobra a outro contrato (desmembra); conferir o novo adiantamento.
8. Encerrar o contrato.
9. Derrubar a stack por PID.

## Fora

- Adiantamento de **venda**.
- Compensação automática.
- Compensar adiantamento de um contrato no provisório de outro (o caminho é revincular antes).
