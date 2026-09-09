# Devolução e revínculo de adiantamento, e o guard de cancelamento de contrato

Data: 2026-09-08
Status: aprovado para planejamento

## Contexto

A Fase 1 do módulo financeiro (`2026-09-07-financial-documents-design.md`) deixou uma pendência
registrada e não resolvida: **um adiantamento já pago sobrevive ao cancelamento do contrato**, sem
guard e sem sinalização, enquanto os mesmos serviços de cancelamento *bloqueiam* o cancelamento
quando existe movimento físico de grão. Dinheiro tratado com menos cuidado que mercadoria.

A decisão de negócio chegou em 08/09/2026:

> O cancelamento do contrato só é possível se ocorrer o estorno do adiantamento ou sua compensação
> com a devolução do valor adiantado. Deve ser possível também vincular o adiantamento a um novo
> contrato.

Isso fecha a pendência e acrescenta uma capacidade nova. O vínculo entre adiantamento e contrato,
que hoje nasce em `FinancialAdvancesCreateService` e nunca mais muda, passa a ser **mutável sob
regras**.

## Escopo

Três saídas passam a existir para um adiantamento pago cujo contrato alguém quer cancelar:

1. **Estornar a baixa** — já existe (`FinancialDocumentsReverseSettlementService`), nada a fazer.
2. **Devolver o valor adiantado** — novo.
3. **Vincular o adiantamento a outro contrato** — novo.

E um guard novo no cancelamento de contrato, que só libera quando o adiantamento chegou a um desses
três estados.

### Não entra

- **Devolução parcial e troca parcial.** Decisão do usuário: devolver é devolver tudo, e o vínculo
  migra o adiantamento inteiro. Parcial exigiria partir o título em dois (ou um saldo aplicado por
  contrato), refazer o cálculo de crédito disponível e definir o que o guard faz com um adiantamento
  meio devolvido — trabalho que encosta na amortização de adiantamento que a Fase 2 já vai modelar.
- **Guard no ENCERRAMENTO de contrato.** Encerrar é outro ato: o contrato foi cumprido, e ali o
  adiantamento é matéria da amortização (Fase 2), não coisa a bloquear.
  `PurchaseContractsCloseService` e `SalesContractsCloseService` não mudam de comportamento.
- **Amortização do adiantamento contra o documento firme** — continua Fase 2.

## Modelo — nenhuma migration

Nada de coluna nova. A mudança de dados é:

- `FinancialSettlementOrigin` ganha `AdvanceRefund = 5`. A coluna `Origin` já é `int`; acrescentar
  um valor ao enum não gera migration.
- `FinancialDocumentChangeLogFields` ganha `public const string Contract = "Contract";`. É constante
  de código, não schema.

Confirmar com `dotnet ef migrations has-pending-model-changes` que o snapshot segue limpo. Se
aparecer migration pendente, alguma coisa saiu do desenho.

## Serviço 1 — `FinancialAdvancesRefundService`

Registra que o dinheiro **saiu e voltou**. É deliberadamente diferente do estorno: o estorno diz que
a baixa não deveria ter existido; a devolução diz que ela existiu e foi desfeita por fora. Reusar
`Reversal` faria o razão mentir, e destruiria a distinção que uma conciliação bancária futura
precisa.

```
ExecuteAsync(Guid documentKey, string financialAccountCode, DateTime refundDate,
             string? documentReference, string reason, string userName,
             CommitMode commitMode = CommitMode.Auto)
```

Guards, nesta ordem:

- `reason` obrigatório (mesma exigência do estorno).
- Documento existe, senão `NotFoundException`.
- `Nature == Advance` — só adiantamento se devolve. Provisório não tem dinheiro pago.
- `Status != Canceled`.
- `SettledAmount > 0` — não há o que devolver.
- Conta financeira informada existe e não está inativa. **Obrigatória**: o dinheiro volta para algum
  lugar concreto, e `FinancialSettlement.FinancialAccountCode` ser anulável no modelo existe para as
  origens não-caixa da Fase 2/3, não para esta.

Efeito, em UMA transação:

1. Insere `FinancialSettlement` com `Amount = -document.SettledAmount` (valor inteiro),
   `SettlementDate = refundDate`, `FinancialAccountCode` = a conta informada,
   `DocumentReference = documentReference`, `InterestAmount`/`FineAmount`/`DiscountAmount` zerados
   (a devolução é do principal), `Origin = AdvanceRefund`, `ReversedSettlementKey = null`,
   `Notes = reason`.
2. `FinancialDocumentsRecalculateBalanceService.RecalculateAsync` → `SettledAmount` volta a 0.
3. Cancela o documento: `Status = Canceled`,
   `CancellationReason = $"Adiantamento devolvido: {reason}"`, `CanceledAt`/`CanceledBy`.

`AvailableAdvanceAmount` é `[NotMapped]` derivado de `SettledAmount`, então o crédito desaparece
sozinho — não há segunda fonte de verdade para sincronizar.

Idempotência: uma segunda chamada bate no guard de `Status != Canceled`. Não é preciso índice novo.

## Serviço 2 — `FinancialAdvancesRelinkContractService`

```
ExecuteAsync(Guid documentKey, string targetContractType, Guid targetContractKey, string userName)
```

Guards:

- `Nature == Advance`, `Status != Canceled`.
- `targetContractType` é `"Purchase"` ou `"Sales"` (mesma validação de string do
  `FinancialAdvancesCreateService`), e **casa com a direção do título**: `Payable` com `Purchase`,
  `Receivable` com `Sales`. Direção cruzada é erro de negócio, não de digitação.
- Contrato destino existe e `Status == Approved`.
- `CardCode` do destino igual ao do título — **o dinheiro é daquele parceiro**. É o guard que impede
  o erro caro e silencioso de mover crédito de um produtor para o contrato de outro.
- `BranchCode` igual — senão o título passa a somar na filial errada.
- `StandardCurrency` do destino igual à `Currency` do título.
- Destino diferente do contrato atual.

Efeito:

- Troca `PurchaseContractKey`/`SalesContractKey` (um preenchido, o outro nulo).
- `OriginDocNumber` passa a ser o `Code` do contrato destino.
- `FinancialDocumentChangeLogService.Register(documentKey, Contract, <código antigo>,
  <código novo>, userName)` — uma linha, lida pelos códigos, que é o que o usuário reconhece.

**`PaymentTermsText` NÃO muda.** É a cópia de "onde pagar" segundo o contrato de origem, e para um
adiantamento já pago foi por ali que o dinheiro saiu. Reescrevê-lo apagaria histórico.

O índice único filtrado `IX_FINANCIAL_DOCUMENTS_ProvisionalOrigin` cobre apenas
`Nature = Provisional`, então adiantamento migrando de contrato nunca colide com ele.

Consequência desejada: depois da troca, o contrato antigo fica sem adiantamento e o cancelamento
passa; o contrato novo herda o adiantamento e passa a ser protegido pelo mesmo guard.

## Guard — `FinancialDocumentsContractCancellationGuardService`

A regra é financeira, então mora no financeiro e é chamada pelos dois serviços de cancelamento, ao
lado do guard de movimento físico que já existe.

```
EnsureCanCancelAsync(Guid? purchaseContractKey, Guid? salesContractKey)
```

Bloqueia quando existe documento com `Nature == Advance && Status != Canceled && SettledAmount != 0`
vinculado ao contrato. A mensagem **nomeia os títulos e as três saídas**:

> O contrato possui adiantamento pago: FN000217 (R$ 50.000,00). Estorne a baixa, registre a
> devolução do valor adiantado ou vincule o adiantamento a outro contrato antes de cancelar.

Chamado em `PurchaseContractsCancelService.ExecuteAsync` e
`SalesContractsCancelService.ExecuteAsync`, **antes** de mudar o status do contrato — junto do guard
de `Allocations`, para que a operação inteira falhe sem efeito colateral.

### Adiantamento não pago cancela junto

`FinancialDocumentsCancelService.EnqueueCancelByContractAsync` ganha um parâmetro explícito
`bool includeUnpaidAdvances = false`. Quando `true`, o filtro passa a incluir
`Nature == Advance && SettledAmount == 0` além dos provisórios.

Só os **dois serviços de cancelamento** passam `true`. Os serviços de encerramento chamam o mesmo
método e continuam com o padrão `false` — ver "Não entra".

⚠️ O XML-doc atual desse método diz que adiantamento fica de fora "porque o dinheiro do adiantamento
pode já ter saído". Essa frase precisa ser reescrita: a razão passa a ser que o adiantamento **pago**
fica de fora (e é barrado pelo guard); o não pago é só uma promessa e cancela junto.

## Superfície OData

Duas actions novas, no molde das sete que já existem (`SiagroB1.Web/Actions/Financials/`, uma
controller por action, `[HttpPost("odata/<Nome>")]`) e declaradas em `ODataConfigurations.cs`.

Os **três** serviços novos entram como `AddScoped` no bloco `// financials` do
`AddApplicationServices()` (`SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs`): os dois de
action mais o guard, que é injetado nos dois serviços de cancelamento de contrato e por isso
também precisa de registro — ao contrário do `FinancialDocumentsSettlementGuardService`, que é
estático e deliberadamente não registrado.

| Action | Parâmetros |
|---|---|
| `FinancialAdvancesRefund` | `DocumentKey` (Guid), `FinancialAccountCode` (string), `RefundDate` (string), `DocumentReference` (string, opcional), `Reason` (string) |
| `FinancialAdvancesRelinkContract` | `DocumentKey` (Guid), `ContractType` (string), `ContractKey` (Guid) |

Datas como `string`, seguindo o que já está feito. Não há dinheiro nestes parâmetros — o valor da
devolução é sempre o saldo baixado, lido no servidor, nunca informado pela tela.

⚠️ `ODataActionParameters` chega **nulo** quando nenhum parâmetro do EDM é enviado, e `TryGetValue`
devolve `true` com valor `null` para parâmetro string ausente. Repetir as duas proteções que as
controllers existentes já fazem.

## Telas

Nenhuma tela nova, nenhuma rota nova, nenhuma migration de menu. Tudo no detalhe do título
(`webapp/view/financialDocuments/Detail.view.xml` + `Detail.controller.ts`), no rodapé, visível só
quando `Nature == Advance` e `Status != Canceled`:

- **Devolver adiantamento** — visível quando `SettledAmount > 0`. Diálogo no molde do
  `SettleDialog.fragment.xml`: conta financeira (value help `FinancialAccountsSelectDialog`,
  obrigatória, com a conta da última baixa como sugestão), data (hoje), documento, motivo
  (obrigatório).
- **Vincular a outro contrato** — visível enquanto não cancelado. Diálogo com o value help de
  contrato já usado no adiantamento (`PurchaseContractsApprovedSelectDialog` /
  `SalesContractsSelectDialog`), escolhido pela direção do título — o tipo não é editável aqui,
  porque a direção do título já o determina.
  - O value help precisa vir **filtrado pelo parceiro do título**: `openTableSelectDialog` aceita
    `defaultFilters`, que existe exatamente para filtro que muda a cada abertura.

`formatFinancialDocumentCancelable` não muda. Depois da devolução o título fica `Canceled`, e o
botão de cancelar some pela regra que já existe.

A mensagem do guard sobe como MessageBox nas telas de cancelamento de contrato que já existem —
nada a construir ali.

## Testes

Em `SiagroB1.Application.Tests/Financials/`, no molde dos 11 arquivos existentes (xUnit + EF
InMemory, fakes em `Support/FinancialDocumentTestServices.cs`):

**`FinancialAdvancesRefundServiceTests`** — grava a linha com `Origin = AdvanceRefund` e
`Amount = -SettledAmount`; zera o saldo; deixa o título `Canceled` com o motivo; recusa título já
cancelado; recusa quando `SettledAmount == 0`; recusa `Nature != Advance`; recusa sem motivo; recusa
conta financeira inexistente.

**`FinancialAdvancesRelinkContractServiceTests`** — troca a chave e o `OriginDocNumber`; grava UMA
linha de log com campo `Contract` e os dois códigos; não altera `PaymentTermsText`; recusa parceiro
diferente, contrato não aprovado, direção cruzada, filial diferente, moeda diferente, e destino
igual à origem.

**`FinancialDocumentsContractCancellationGuardServiceTests`** e complemento em
`FinancialDocumentUndoHooksTests` — cancelar contrato com adiantamento pago lança e **não muda o
status do contrato**; com o adiantamento estornado passa; com o adiantamento devolvido passa; depois
do revínculo passa (e o contrato novo passa a bloquear); adiantamento não pago é cancelado junto com
os provisórios; encerramento de contrato **não** é afetado por nada disso.

## Riscos e armadilhas

- **`RollbackAsync`/`CommitAsync` sem `BeginTransactionAsync` estouram NRE.** O
  `FinancialAdvancesRefundService` compõe duas escritas e um recálculo; seguir o padrão `CommitMode`
  de `FinancialDocumentsSettleService`, com `Begin` no `Auto` e nada no `Deferred`.
- **`SumAsync` não enxerga entidade rastreada ainda não salva.** O recálculo de saldo já roda depois
  de `SaveChangesAsync`; manter essa ordem.
- **O guard tem de rodar antes do `contract.Status = Canceled`**, senão uma exceção posterior deixa o
  contrato marcado em memória — e o serviço de cancelamento salva tudo num `SaveChangesAsync` só.
- **Verificar no navegador, não por teste verde.** A entrega só está pronta quando os dois botões
  aparecem no detalhe do adiantamento, o value help abre filtrado pelo parceiro, e o cancelamento de
  contrato mostra a mensagem com o código do título. A Fase 1 mostrou que gate verde não prova que a
  tela funciona: um `--` em comentário XML matou um value help inteiro sem que nenhum gate acusasse.

## Decisões registradas

1. Devolução é origem própria (`AdvanceRefund`) e cancela o título — não reaproveita `Reversal`.
2. Adiantamento não pago cancela junto com o contrato; só o pago bloqueia.
3. Revínculo exige mesmo parceiro, mesma direção, destino aprovado, mesma filial e mesma moeda.
4. A troca fica disponível sempre no detalhe do adiantamento, não só durante o cancelamento.
5. Devolução e troca são sempre pelo valor inteiro.
6. `PaymentTermsText` não muda no revínculo.
7. Encerramento de contrato fica fora do guard e do cancelamento automático de adiantamento.
