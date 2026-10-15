# Washout no Contrato de Venda — Design

- **Data:** 15/10/2026
- **Origem:** sub-projeto B do contas a receber (`2026-10-15-sales-firm-receivables-design.md`, D6)
- **Molde:** `2026-09-14-purchase-contract-washout-design.md` (washout de compra). Onde este texto não diz
  diferente, vale o de compra, trocando compra por venda e produtor por cliente.
- **Status:** desenho aprovado pelo usuário em conversa ("correto, escrever spec")

## 1. Contexto

Com o firme a receber (sub-projeto A), o contrato de venda só encerra com todos os títulos quitados. Quando o
cliente desiste de parte do volume, o provisório a receber da fixação fica maior que os firmes compostos e
nunca se quita: hoje o contrato fica sem encerrar.

O washout registra no contrato de venda a desistência de parte ou de todo o volume ainda não entregue. Ele:

- tira esse volume do saldo;
- reduz o provisório a receber da fixação;
- gera um título a receber do cliente com a diferença de mercado e a multa.

## 2. Decisões

| Tema | Decisão |
|---|---|
| Modelo | Registro filho do contrato de venda (`SalesContractWashout`), espelho de `PurchaseContractWashout`. |
| Valor cobrado | `max(preço da fixação − preço de mercado, 0) × volume fixado lavado + multa`. O cliente que desiste paga quando o mercado caiu abaixo do contrato. |
| Mercado acima do contrato | A diferença vira **zero**; cobra-se só a multa. O cliente nunca recebe crédito. |
| A fixar (PAF) | Só o volume **fixado** usa o preço da fixação. O **não fixado** entra só com a multa; o usuário informa quanto lava de cada um. |
| Liberações abertas | Lava-se só o saldo não liberado: volume ≤ saldo físico **e** ≤ saldo a liberar. |
| Aprovação | Em aprovação → Aprovado (efeitos) / Rejeitado, em tela própria no grupo **Vendas**. |
| Encerramento | Manual. O washout não encerra o contrato. |
| Vencimento do título | Informado; obrigatório quando o valor for maior que zero. |
| Estorno | Permitido se o título não tiver baixa e o contrato não estiver encerrado. |
| Notificações | Os mesmos eventos do washout de compra, no contrato de venda. |

## 3. Entidade `SalesContractWashout`

Tabela `SALES_CONTRACTS_WASHOUTS`, derivada de `BaseEntity`, com os mesmos campos de `PurchaseContractWashout`
(§3.1 do molde), trocando:

- `PurchaseContractKey` → `SalesContractKey` (FK `Restrict`, obrigatória);
- `PriceFixationKey` → FK para `SALES_CONTRACTS_PRICE_FIXATIONS` (`Restrict`, anulável);
- `Status` usa um enum próprio `SalesContractWashoutStatus` (InApproval=0, Approved=1, Rejected=2, Reversed=3).

Índice único (`SalesContractKey`, `Sequence`). Exibição "WO-{Sequence}".

### 3.1 Cálculo

```
Amount = round(max(ContractPrice − MarketPrice, 0) × FixedVolume, 2) + round(PenaltyAmount, 2)
```

(`SalesContractWashout.CalculateAmount`, estático, como o de compra.)

## 4. Saldos do contrato de venda

### 4.1 Colunas novas

`SalesContract.WashedOutVolume` e `SalesContract.WashedOutUnfixedVolume`, DECIMAL(18,3), persistidas e
recalculadas **só** por `SalesContractsWashedOutVolumeService`:

```
WashedOutVolume        = Σ (FixedVolume + UnfixedVolume) dos washouts InApproval e Approved
WashedOutUnfixedVolume = Σ UnfixedVolume dos mesmos
```

### 4.2 Quem passa a descontar o lavado

| Ponto | Mudança |
|---|---|
| `SalesContract.AvaiableVolume` | `round(TotalVolume − AllocatedVolume − WashedOutVolume, 3)` |
| `TotalAvailableToRelease` e `TotalAvailableToReleaseWithoutProvisioning` | subtraem `WashedOutVolume` |
| SQL do saldo a liberar de venda (o equivalente de `PurchaseContractsGetShipmentReleasesAvailableService`, se existir) | mesma fórmula |
| `SalesContractsCloseService.GuardNegativeBalanceAsync` | `TotalVolume − alocado recalculado − WashedOutVolume` |
| Disponível para fixar (`TotalVolume − FixedVolume` em `SalesContract.cs:208`) e a trava da fixação de venda | descontam `WashedOutUnfixedVolume` |
| `SalesContractsTotalsService` e DTO | campo `WashedOutVolume` |
| `FinishedContractMutationGuardInterceptor` | inclui `SalesContractWashout` |
| `SalesContractsCloseService.GuardPriceFixationAsync` (PAF) | desconta do volume confirmado o `FixedVolume` dos washouts `Approved` |

O plano confere cada consumidor no código. O que só existir na compra fica registrado no plano como "sem
equivalente".

## 5. Serviços e regras

Espelho de §5 do molde, com estes nomes: `SalesContractsWashoutCreateService`, `…ApprovalService`,
`…RejectService`, `…ReverseService`, `…WashoutsGetService` e `SalesContractsWashoutGuard`.

### 5.1 Criar

As travas são as do molde §5.1, com estas diferenças:

- **Volume fixado ainda não entregue**, quando `FixedVolume > 0`: `confirmado − faturado − lavadoFixado`.
  - `confirmado` é Σ `FixationVolume` das fixações `Confirmed`.
  - `faturado` é o **alocado no ledger** (`SalesContractsRecalculateBalanceService.CalculateAllocatedAsync`), porque
    na venda a entrega é o faturamento.
  - `lavadoFixado` são os washouts ativos, sem contar este.
- O volume cabe no saldo físico (`AvaiableVolume`) e no saldo a liberar (`TotalAvailableToRelease`), já com os
  washouts ativos.

### 5.2 Aprovar

São os efeitos do molde §5.2.

**Provisório a receber**, quando `FixedVolume > 0`: ajustado no lugar para
`round((FixationVolume − Σ FixedVolume aprovado da fixação) × FixationPrice, 2)`, com log `NetAmount`. Se o
volume zera, o provisório é cancelado com o motivo "Volume lavado por washout".

**Trava nova, por causa da composição (sub-projeto A).** Se o novo valor ficar **abaixo do já composto ou
baixado** do provisório, a aprovação é recusada:

> `O provisório {código} já tem {composto} composto: o washout o deixaria em {novo}. Desfaça a composição antes de aprovar.`

**Título a receber**, se `Amount > 0`:

| Campo | Valor |
|---|---|
| `Direction` | `Receivable` |
| `Nature` | `Firm` |
| `OriginType` | `SalesContractWashout` (**novo valor de enum, 8**) |
| `OriginKey` | o washout |
| `OriginDocNumber` | código do contrato |
| `SalesContractKey` | o contrato |
| Parceiro e filial | os do contrato |
| `Currency` | `StandardCurrency ?? Brl` |
| `DueDate` | o vencimento informado |
| `NetAmount` | `Amount` |

Esse título **não** é firme de documento de saída (`IsSalesFirmReceivable` falso): é baixado em dinheiro, direto,
como o título do washout de compra. Não entra na composição nem na trava de encerramento.

### 5.3 Rejeitar e 5.4 Estornar

Como o molde. O estorno restaura o provisório pela mesma fórmula; se ele tinha sido cancelado por volume zero,
o gerador cria um novo.

### 5.5 Gerador de provisórios

`FinancialDocumentsGenerateService.EnqueueForSalesFixationAsync` passa a calcular
`NetAmount = (FixationVolume − Σ FixedVolume dos washouts Approved da fixação) × FixationPrice` em toda
geração: aprovação, fixação, reabertura e backlog. Com volume restante zero, não gera nada. O backlog conta
`SkippedFullyWashedOut` também na venda.

### 5.6 Travas em serviços existentes

- **Encerrar o contrato de venda:** recusado com washout `InApproval`. Mensagem igual à da compra:
  `Contrato possui {n} washout(s) pendente(s) de aprovação. Aprove ou rejeite antes de encerrar.`
- **Encerrar, provisório não quitado:** a mensagem S10 do sub-projeto A ganha a saída:
  `O título {código} não está quitado: o contrato só encerra com todos os títulos quitados. Entrega a menos se resolve por washout.`
- **Estornar a fixação de venda:** recusado com washout `InApproval` ou `Approved` apontando para ela.
- **Retirar a aprovação do contrato de venda:** recusado com qualquer washout.
- **Cancelar o título na tela do financeiro:** recusado para a origem `SalesContractWashout`, como na compra.
  A mensagem pede para estornar o washout no contrato de venda.

## 6. Financeiro

- Enum: `FinancialDocumentOrigin.SalesContractWashout = 8`.
- O gerador do título a receber de washout (`FinancialDocumentsGenerateWashoutReceivableService`) ganha a
  sobrecarga de venda, ou um serviço irmão; o plano decide pelo menor diff.

## 7. Frontend

**Detalhe do contrato de venda:**

- seção **"Washouts"**, com Registrar e Estornar;
- total "Washout" no cabeçalho;
- o diálogo Registrar mostra uma prévia do valor e dos saldos.

O molde é a seção e os diálogos do contrato de compra.

**Telas e rótulos:**

- Tela **"Aprovação de Washouts"** no grupo **Vendas**, com rota própria.
  - Menu por migration no `CommonContext`, com Key igual à rota e ROLE_MENUS ADMIN, como na compra.
- Formatter de origem: `SalesContractWashout` = "Washout de contrato de venda".
- Log do contrato de venda: campo `Washout`.
- Eventos de notificação: os rótulos dos eventos de washout do contrato de venda.

## 8. Testes e verificação

**xUnit, espelho do molde §8, com os casos de venda:**

- cálculo com o sinal invertido (contrato acima do mercado cobra; abaixo cobra só a multa);
- volume fixado já faturado recusa;
- aprovação com provisório composto recusa;
- o título de washout é baixável direto e não entra na composição.

**Navegador (CEAGUI dev):**

1. Registrar washout no CV000001.
2. Aprovar na tela de Vendas.
3. Conferir o saldo e o "Washout", o provisório reduzido e o título em Contas a Receber.
4. Encerrar o contrato depois de quitar.
5. Estornar e rejeitar.

## 9. Fora de escopo

- Encontro de contas entre o título de washout e adiantamentos de venda.
- Encerramento automático do contrato.
- Cotação de mercado automática.
