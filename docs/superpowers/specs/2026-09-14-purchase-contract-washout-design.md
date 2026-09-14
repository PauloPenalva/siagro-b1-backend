# Washout no Contrato de Compra — Design

- **Data:** 14/09/2026
- **Origem:** GAC-1164 caso 2 (falta de mercadoria do produtor) — decisões iniciais registradas em
  `2026-09-14-warehouse-reconciliation-design.md` §8
- **Status:** aprovado pelo usuário

## 1. Contexto

Às vezes o produtor não tem mais o que entregar de um contrato de compra. Ele pode já ter recebido
por adiantamento ou por nota. Hoje o sistema tem duas saídas para isso:

- **Encerrar o contrato.** O saldo não entregue é abandonado sem nenhum registro e sem acerto
  financeiro.
- **Cancelar o contrato.** É recusado se houver movimento, com a mensagem "considere fazer washout".

O washout registra no próprio contrato a desistência de parte ou de todo o volume não entregue. Ele
tira esse volume do saldo, corrige o provisório a pagar e gera um título a receber do produtor com a
diferença de mercado e a multa.

## 2. Decisões

| Tema | Decisão |
|---|---|
| Modelo | Registro filho do contrato de compra (`PurchaseContractWashout`), no molde da fixação de preço. Não é um contrato de venda "WO". |
| Valor cobrado | `max(preço de mercado − preço da fixação, 0) × volume fixado lavado + multa` |
| Mercado abaixo do contrato | A diferença vira **zero**; cobra-se só a multa. O produtor nunca recebe crédito. |
| PAF | Só o volume **fixado** usa o preço da fixação. O volume **não fixado** entra no washout só com a multa, e o usuário informa quanto lava de cada um. |
| Liberações abertas | Lava-se só o saldo não liberado: volume ≤ saldo físico **e** ≤ saldo a liberar. Para lavar volume já liberado, o usuário encerra ou cancela a liberação antes. |
| Aprovação | Em aprovação → Aprovado (efeitos) / Rejeitado, com tela própria no grupo Compras. |
| Encerramento do contrato | Manual. O washout não encerra o contrato. |
| Vencimento do título | Informado no washout; obrigatório quando o valor for maior que zero. |
| Estorno | Permitido se o título não tiver baixa e o contrato não estiver encerrado. |
| Adiantamento excedente | Fora do washout: continua nas telas de adiantamento (devolver ou revincular). O encontro de contas é da Fase 3. |

## 3. Entidade `PurchaseContractWashout`

Tabela `PURCHASE_CONTRACTS_WASHOUTS`, derivada de `BaseEntity`.

### 3.1 Campos

| Campo | Observação |
|---|---|
| `PurchaseContractKey` | FK do contrato, obrigatória |
| `Sequence` | inteiro sequencial por contrato (1, 2, 3…), atribuído na criação; exibido como "WO-{Sequence}" junto do código do contrato |
| `PriceFixationKey` | fixação de referência, anulável. No FIX é a fixação automática; no PAF é escolhida pelo usuário. Obrigatória se `FixedVolume` > 0. |
| `FixedVolume` | volume fixado lavado, DECIMAL(18,3), ≥ 0 |
| `UnfixedVolume` | volume não fixado lavado, DECIMAL(18,3), ≥ 0. Só em PAF; no FIX é sempre 0. |
| `ContractPrice` | cópia do `FixationPrice` da fixação, DECIMAL(18,8) |
| `MarketPrice` | informado, DECIMAL(18,8), ≥ 0 |
| `PenaltyAmount` | multa, DECIMAL(18,2), ≥ 0 |
| `Amount` | valor calculado, DECIMAL(18,2) |
| `DueDate` | vencimento do título, anulável; obrigatório se `Amount` > 0 |
| `Reason` | motivo/observação, obrigatório |
| `Status` | `PurchaseContractWashoutStatus`: InApproval=0, Approved=1, Rejected=2, Reversed=3 |
| `ApprovalComments`, `ReversalReason` | textos do fluxo |
| `ApprovedBy/At`, `CanceledBy/At` | herdados de `BaseEntity`; `CanceledBy/At` guarda a rejeição ou o estorno |
| `FinancialDocumentKey` | título a receber gerado, anulável |
| `RowVersion` | concorrência |

### 3.2 Cálculo

```
Amount = round(max(MarketPrice − ContractPrice, 0) × FixedVolume, 2) + PenaltyAmount
```

## 4. Saldos do contrato

### 4.1 Nova coluna

`PurchaseContract.WashedOutVolume` é persistida e recalculada **só** por
`PurchaseContractsWashedOutVolumeService`, no mesmo molde de `PurchaseContractsFixedVolumeService`:

```
WashedOutVolume = Σ (FixedVolume + UnfixedVolume) dos washouts InApproval e Approved
```

Um washout em aprovação já reserva o volume. Rejeitar ou estornar libera.

### 4.2 Consumidores que passam a descontar o volume lavado

| Ponto | Mudança |
|---|---|
| `PurchaseContract.AvaiableVolume` (`PurchaseContract.cs:232`) | `round(TotalVolume − AllocatedVolume − WashedOutVolume, 2)`. Alimenta a trava de alocação, transferência de titularidade, o seletor de contratos alocáveis, os totais, a lista ("Saldo (Físico)") e o relatório `PurchaseContracts.frx`. |
| `TotalAvailableToRelease` (`:202`) e `TotalAvailableToReleaseWithoutProvisioning` (`:216`) | subtraem `WashedOutVolume` |
| SQL de `PurchaseContractsGetShipmentReleasesAvailableService` (`:20-31`) | mesma fórmula do saldo a liberar |
| `PurchaseContractsCloseService.GuardNegativeBalanceAsync` (`:79-90`) | `TotalVolume − alocado recalculado − WashedOutVolume` |
| `AvailableVolumeToPricing` (`:169`) e a trava da fixação (`PriceFixationCreateService.cs:48`) | descontam os `UnfixedVolume` dos washouts ativos (volume não fixado lavado não pode mais ser fixado) |
| `PurchaseContractsTotalsService` e DTO | novo campo `WashedOutVolume` |
| `FinishedContractMutationGuardInterceptor` | inclui `PurchaseContractWashout` (contrato encerrado não aceita washout) |
| `PurchaseContractsCloseService.GuardPriceFixationAsync` (PAF) | desconta o volume fixado lavado por washout `Approved` (`Σ FixedVolume dos washouts ativos`, que aqui são todos aprovados, porque o pendente foi barrado antes) do volume confirmado, antes de comparar com o entregue — revisão final F2, ver §5.1 |

**Sem mudança:**

- os saldos da Expedição de Grãos (trabalham por liberação);
- o relatório de contratos por produto (usa só `TotalVolume`).

## 5. Serviços e regras

### 5.1 Criar (`PurchaseContractsWashoutCreateService`)

**Travas**, todas antes de abrir transação:

- O contrato está `Approved`.
- `FixedVolume + UnfixedVolume > 0`, com os dois ≥ 0. `MarketPrice` e `PenaltyAmount` são ≥ 0, e `Reason` é obrigatório.
- **FIX:** `UnfixedVolume = 0`, e `PriceFixationKey` é obrigatória se `FixedVolume > 0` (a fixação automática).
- **PAF:**
  - `PriceFixationKey` obrigatória se `FixedVolume > 0`.
  - A fixação precisa estar `Confirmed` e pertencer ao contrato.
  - `FixedVolume ≤ FixationVolume − Σ FixedVolume dos washouts ativos dessa fixação`.
  - `UnfixedVolume ≤ AvailableVolumeToPricing` (já descontados os washouts ativos).
- **Volume com preço ainda não entregue** (revisão final F2), quando `FixedVolume > 0`, para os
  dois tipos de contrato — no preço fixo a fixação automática cobre o total contratado, então a
  conta é inofensiva:
  - `confirmado = Σ FixationVolume` das fixações `Confirmed` do contrato.
  - `entregue = Σ ShipmentRelease.ShippedQuantity` do contrato, excluindo `ReleaseOrigin.SalesReturn`.
  - `lavadoFixado = Σ FixedVolume dos washouts ativos do contrato` (excluindo este washout).
  - `FixedVolume ≤ confirmado − entregue − lavadoFixado`. Sem esta trava, um washout podia lavar
    volume fixado já ENTREGUE, deixando aquela mercadoria sem preço e sem título a pagar.
- O volume total cabe no saldo físico (`AvaiableVolume`, já com os washouts ativos) e no saldo a liberar (`TotalAvailableToRelease`).
- Se `Amount > 0`, `DueDate` é obrigatório.

**Efeitos:**

- Copia `ContractPrice` da fixação e calcula `Amount`.
- Status `InApproval`.
- Recalcula `WashedOutVolume`.
- Registra o log de alterações do contrato (novo campo `Washout`) e a notificação `WashoutCreated`.

### 5.2 Aprovar (`PurchaseContractsWashoutApprovalService`)

**Travas:** status `InApproval`, contrato `Approved`, e o saldo revalidado conforme §5.1 (sem contar o próprio washout em duplicidade).

**Efeitos**, na mesma transação:

1. Status `Approved`, com `ApprovedBy/At` e comentário.
2. **Provisório a pagar da fixação**, quando `FixedVolume > 0`: **ajustado no lugar**, sem
   cancelar e gerar de novo, para o valor
   `round((FixationVolume − Σ FixedVolume dos washouts Approved da fixação, já contando este) × FixationPrice, 2)`.
   - **Registro do ajuste:** a mudança de `NetAmount` entra no log do documento financeiro (novo
     código `NetAmount`, com valor antigo → novo).
   - **Volume restante zero:** o provisório é cancelado com o motivo "Volume lavado por washout".
   - **Provisório inexistente** (raro, só com dado antigo): o gerador cria um já com o volume
     descontado.
   - **Por que no lugar.** Um provisório não aceita baixa, então mudar o valor não afeta nada já
     realizado. Cancelar e gerar de novo exigiria duas gravações dentro de uma transação e buscar a
     numeração (Dapper com `UPDLOCK, HOLDLOCK`) no meio dela. Além disso, a gravação dependeria da
     ordem entre o cancelamento e o novo insert no índice único filtrado dos provisórios. Com o
     ajuste, tudo cabe num único `SaveChanges`.
3. **Título a receber**, se `Amount > 0` (novo serviço de geração no módulo financeiro):
   - `Direction = Receivable`, `Nature = Firm`, `Status = Open`;
   - `OriginType = PurchaseContractWashout` (novo valor), `OriginKey = washout.Key`, `OriginDocNumber` = código do contrato;
   - `PurchaseContractKey` do contrato, `CardCode/CardName` do contrato, `BranchCode`;
   - `Currency = StandardCurrency ?? Brl`, `DueDate`, `NetAmount = Amount`.
   - Grava `FinancialDocumentKey`.
4. Log de alterações e notificação `WashoutApproved`.

### 5.3 Rejeitar (`PurchaseContractsWashoutRejectService`)

- **Trava:** status `InApproval`; o motivo é obrigatório.
- **Efeitos:** status `Rejected`, `CanceledBy/At`, recálculo de `WashedOutVolume`, log e notificação `WashoutRejected`.

### 5.4 Estornar (`PurchaseContractsWashoutReverseService`)

**Travas:**

- Status `Approved`, contrato `Approved` (não `Finished`) e motivo obrigatório.
- O título não pode ter baixa: `SettledAmount = 0` e nenhum lançamento vivo. Se tiver, a mensagem é "estorne a baixa antes".

**Efeitos**, na mesma transação:

- Status `Reversed`, com `CanceledBy/At` e `ReversalReason`.
- Cancela o título a receber (enfileirado, no mesmo `SaveChanges`).
- Reajusta o provisório da fixação pela mesma fórmula de §5.2, agora sem este washout. Se o
  provisório tinha sido cancelado por volume zero, o gerador cria um novo.
- Recalcula `WashedOutVolume`.
- Registra log e a notificação `WashoutReversed`.

### 5.5 Gerador de provisórios

`FinancialDocumentsGenerateService` (`EnqueueForPurchaseFixationAsync`) passa a calcular
`NetAmount = (FixationVolume − Σ FixedVolume dos washouts Approved da fixação) × FixationPrice` em
**toda** geração: aprovação do contrato, aprovação de fixação, reabertura e backlog. Assim, reabrir o
contrato não traz o valor cheio de volta. Se o volume restante for zero, não gera nada.

### 5.6 Travas em serviços existentes

Decididas ao escrever o plano de implementação, para fechar caminhos que furariam o washout:

- **Encerrar contrato:** recusado enquanto houver washout `InApproval`.
- **Estornar fixação:** recusado enquanto houver washout `InApproval` ou `Approved` apontando para
  ela. Voltar a fixação para `InApproval` deixaria o washout aprovado sem preço de referência.
- **Retirar contrato da aprovação:** recusado se o contrato tiver qualquer washout, de qualquer
  status. Em rascunho o contrato pode ser excluído, e a FK `Restrict` do washout o impediria com
  erro 547.
- **Disponível para fixar:** `PurchaseContract.WashedOutUnfixedVolume` é persistido e recalculado
  pelo mesmo serviço de `WashedOutVolume`. Isso deixa `AvailableVolumeToPricing` como fórmula pura
  (`TotalVolume − FixedVolume − WashedOutUnfixedVolume`), que funciona sob `$select`.

### 5.7 Cancelar contrato

O contrato com washout aprovado continua sujeito às travas atuais de cancelamento (movimento físico e
adiantamento pago). O título a receber de washout **não** é cancelado automaticamente, porque é uma
dívida real do produtor.

## 6. Financeiro

- **Novo valor de enum:** `FinancialDocumentOrigin.PurchaseContractWashout`.
- **Novo serviço:** `FinancialDocumentsGenerateWashoutReceivableService`. Só enfileira; quem chama é
  responsável por salvar, no mesmo padrão do gerador existente.
- **Baixa:** o título `Firm` passa pela trava de baixa atual, que só bloqueia `Provisional`. A baixa é
  manual pela tela de Contas a Receber.
- **Revínculo:** o título de washout não é adiantamento, então as regras de revínculo não se aplicam.
- **Pendência de telas:** a tela de Contas a Receber já filtra `Direction = Receivable` e
  `Nature ne Advance`, então o título aparece. Falta um formatter de origem (não existe hoje).

## 7. Frontend

**Detalhe do contrato de compra** (`view/purchaseContracts/Detail.view.xml`):

- Nova seção **"Washouts"**: tabela com `$$ownRequest` e as colunas código, status, volumes, preços,
  multa, valor, vencimento, motivo e título.
- **Diálogo "Registrar washout":**
  - escolha da fixação (PAF) e volumes fixado/não fixado (o não fixado só em PAF);
  - preço de mercado, multa, vencimento e motivo;
  - prévia do valor calculado e dos saldos disponíveis.
- **Botão Estornar**, visível para Approved, com diálogo de motivo.
- **Cabeçalho:** o total **"Washout"** (`WashedOutVolume`) ao lado do saldo.

**Outras telas:**

- **"Aprovação de Washouts"**, no grupo Compras: lista de washouts InApproval com o contrato expandido.
  Aprovar com comentário opcional; rejeitar com motivo obrigatório. Menu por migration no
  `CommonContext`, com Key igual à rota e ROLE_MENUS ADMIN.
- **Contas a Receber / detalhe do título:** rótulo da origem "Washout de contrato de compra".
- **Rótulos:** o log de alterações ganha o campo `Washout` e as notificações os novos eventos
  (`formatter.ts`).

## 8. Testes e verificação

**xUnit + EF InMemory:**

- **Cálculo:** diferença positiva, diferença negativa (vira zero), só multa e PAF não fixado.
- **Travas de criação:** FIX com não fixado, fixação de outro contrato ou não confirmada, volume acima
  da fixação, acima do disponível para fixar, acima do saldo físico, acima do saldo a liberar, contrato
  não aprovado e vencimento ausente com valor.
- **Saldos:** `WashedOutVolume` com InApproval, Approved, Rejected e Reversed; `AvaiableVolume`, saldo a
  liberar, disponível para fixar e trava de saldo negativo no encerramento.
- **Aprovação:** provisório ajustado no lugar com valor reduzido e log `NetAmount`; provisório
  cancelado quando o volume zera; título a receber gerado com origem, contrato e vencimento; sem título
  quando o valor é zero.
- **Rejeição e estorno:** rejeição libera o volume; estorno cancela o título, restaura o provisório e é
  recusado com baixa ou contrato encerrado.
- **Gerador:** reabrir o contrato gera o provisório descontado; o backlog é idempotente.
- **Interceptor:** contrato encerrado recusa washout.

**Verificação no navegador**, pelo caminho do usuário:

1. Registrar washout em FIX e em PAF.
2. Aprovar na tela de aprovação.
3. Conferir o saldo e o "Lavado" no contrato, o provisório reduzido em Contas a Pagar e o título em
   Contas a Receber.
4. Estornar e rejeitar.

## 9. Fora de escopo

- Encontro de contas entre o título de washout e adiantamentos ou títulos a pagar (Fase 3).
- Devolução automática de adiantamento excedente.
- Encerramento automático do contrato.
- Washout em contrato de venda.
- Cotação de mercado automática (não existe tabela de cotação; o preço é informado).
