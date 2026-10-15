# Títulos firmes a receber a partir do documento de saída

Data: 2026-10-15 · Branch: `feature/sales-firm-receivables` (backend e frontend)

## Contexto

O contas a pagar de compra está completo:

- **Sub 2:** o provisório nasce da fixação, o firme nasce do documento de entrada conciliado e o operador compõe os
  firmes no provisório, onde paga.
- **Sub 2b:** complemento de valor com NF-e própria.
- **Sub 3:** compensação de adiantamento.

Na venda existem só duas coisas:

- o **provisório a receber**, gerado por `FinancialDocumentsGenerateService.EnqueueForSalesFixationAsync` quando a fixação
  é aprovada;
- o **adiantamento de venda**.

`FinancialDocumentOrigin.SalesInvoice = 4` está declarado e nunca usado, e `SalesInvoiceItem.CreatesFinancialDocument`
está "sem efeito por ora".

Este é o **sub-projeto A** do receber: o firme a receber nasce do **documento de saída confirmado** e funciona como na
compra.

O **sub-projeto B, washout de venda**, terá spec próprio logo depois. Ele espelha o de compra:

- valor `max(contrato − mercado, 0) × volume fixado + multa`;
- título a receber firme do cliente;
- reduz o provisório e o saldo;
- no contrato a fixar (PAF), o volume não fixado é lavado só com a multa;
- aprovação em tela própria em Vendas.

Enquanto B não chega, um contrato de venda entregue a menos não encerra (aceito pelo usuário).

## Decisões do usuário

| # | Decisão |
|---|---|
| D1 | Baixa igual à compra: o firme é travado para baixa direta. O operador compõe os firmes no provisório do contrato de venda e recebe no provisório. A sobra do firme sai por dação. |
| D2 | Linha sem contrato não gera título. A saída avulsa com cobrança fica fora. |
| D3 | Devolução igual à compra: natureza nova de devolução de venda, com compensação automática no firme solto da linha de origem. Se o firme de origem estiver composto, a devolução entra negativa na composição. "Compensar com firme" manual. |
| D4 | Encerrar igual à compra: recusa com firme ou devolução em aberto, com provisório não quitado ou com crédito de adiantamento. Chave de corte para contratos antigos. Reabrir recria o provisório. |
| D5 | Telas: seção "Financeiro" no contrato de venda; bloco do documento de saída com link no detalhe do título; filtros Natureza e "Com saldo a resolver" na lista de Contas a Receber. |
| D6 | Washout de venda é o sub-projeto B. |
| D7 | Realocação acompanha: realocar volume faturado entre contratos de venda desmembra o firme solto da linha. Desfazer a realocação junta de volta. Firme composto ou baixado recusa a realocação. |

## Modelo

### Um firme por linha × contrato, derivado do ledger

O firme a receber é por **(linha do documento de saída, contrato de venda)**. O ledger `SALES_CONTRACTS_ALLOCATIONS` já
diz em que contrato está o volume de cada linha, inclusive depois de realocações e devoluções. Ele é a fonte da
distribuição. O valor sai sempre da **linha** (preço faturado), nunca do contrato.

Distribuição de uma linha confirmada com valor `V = GrandTotal`:

1. Se a linha **não tem** linha de ledger com `Volume ≠ 0` (por exemplo, o complemento de preço avulso, que grava
   `Volume = 0`, ou um documento sem efeito de saldo), o contrato é `SalesInvoiceItem.SalesContractKey` e o valor é `V`.
2. Se tem, a participação de cada contrato `c` é `share_c = Σ Volume_c ÷ Σ Volume` (somas assinadas das linhas de ledger
   do item). Contrato com `share_c ≤ 0` não tem firme. O valor é `round(V × share_c, 2)`. A diferença de arredondamento
   vai para o contrato de maior participação, e no empate para o contrato da linha.

Como as linhas de devolução são todas negativas, o quociente fica positivo.

**Natureza e direção:**

- Documento `Normal`: `Nature = Firm`.
- Documento `Return`: `Nature = SalesReturn` (novo, `= 5`).
- Os dois com `Direction = Receivable`, `OriginType = SalesInvoice` e `OriginKey = chave da linha`.

### Campos do título

| Campo | Valor |
|---|---|
| `SalesContractKey` | o contrato `c` |
| `OriginDocNumber` | código do contrato `c` |
| `SalesInvoiceKey` | **coluna nova**, sem FK, espelho de `PurchaseInvoiceKey` |
| `TaxDocumentNumber` | "número-série" da NF, ou o número interno do documento sem NF, como na compra |
| `CardCode` / `CardName` | do documento |
| `BranchCode` | do documento |
| `Currency` | BRL |
| `DocumentDate` / `DueDate` | data do documento (`InvoiceDate`) ou hoje. O firme não é cobrado direto: o vencimento que vale é o do provisório. |

### Idempotência

O índice único filtrado `IX_FINANCIAL_DOCUMENTS_FirmOrigin` passa a ser `(OriginType, OriginKey, SalesContractKey)`, com
o filtro estendido a `Nature IN (Firm, PurchaseReturn, SalesReturn)`. Continua excluindo os cancelados e exigindo
`OriginKey` não nulo.

Na compra `SalesContractKey` é sempre nulo, e o SQL Server trata os NULL como iguais no índice único. Por isso a
unicidade da compra não muda.

### Quando gera (regra G)

Uma linha gera firme quando **todas** as condições valem:

| # | Condição |
|---|---|
| G1 | o documento está `Confirmed` (o `Returned` mantém os firmes que já tinha) |
| G2 | o documento não é nota de custódia (`CustodyRole == None`) nem remessa para depósito |
| G3 | a linha tem contrato (D2) |
| G4 | a natureza da linha gera financeiro: `FinancialDocumentsFirmSyncService.CreatesFinancialDocumentAsync(usageCode)`, reaproveitado (STANDALONE lê `Usage.CreatesFinancialDocument`; SAPB1 conta como gera) |

## Serviço de sincronização

O serviço novo é `FinancialDocumentsSalesFirmSyncService` (Application/Services/Financials), com
`EnqueueSyncAsync(IReadOnlyCollection<Guid> salesInvoiceItemKeys, string userName)`. Ele é ENQUEUE-ONLY: quem chama
grava.

Para cada linha, ele compara o **desejado** (o modelo acima, vazio se G falha) com os títulos **ativos** da linha:

| Situação | Ação |
|---|---|
| Título ativo de um contrato que saiu do desejado | cancela, se solto |
| Valor mudou | recalcula `NetAmount` com log `NetAmount`, se solto |
| Valor igual | não toca no título nem recusa (como na compra) |
| Contrato novo no desejado | cria o título, com número de `DocNumberSequenceService` |
| Título composto ou baixado que precisaria mudar | recusa a operação inteira antes de qualquer escrita (mensagens S1–S4) |

"Solto" = sem composição e sem baixa (`PurchaseFirmPayableRules.ProvisionalOf` / `EnsureLoose`, generalizados).

Depois de criar uma devolução (`SalesReturn`), ele chama a compensação automática (regra 9).

**Numeração dentro da transação.** Os pontos de chamada já rodam dentro de transação, porque a confirmação abre a sua.
`DocNumberSequenceService` usa uma `SqlConnection` própria (autocommit), e `ShipmentBillingCreateSalesInvoiceService` já
numera o documento de saída assim, dentro da transação. O mesmo vale para o título. Um rollback deixa um buraco na
numeração dos títulos, o que é aceito, porque a nota de saída já tem esse comportamento.

### Pontos de chamada

Todos os caminhos que mudam o estado ou o ledger de uma linha de saída chamam o serviço. Ele é chamado **depois** do
ledger gravado (`SaveChangesAsync`), porque lê as alocações.

| # | Ponto | Onde | Efeito |
|---|---|---|---|
| 1 | Confirmação manual | `SalesInvoicesConfirmService.ExecuteAsync` (ramos Normal de romaneio, avulso e Return) | Também cobre a autorização da NF-e de devolução própria e a recusa de carga (`Deferred`). |
| 2 | Expedir e Faturar | `ShipmentBillingCreateSalesInvoiceService.ExecuteAsync` | Este caminho grava `Confirmed` direto. |
| 3 | Estornar confirmação | `SalesInvoicesReverseConfirmService` | O documento volta a `Pending`: o desejado fica vazio, então cancela ou recusa. |
| 4 | Cancelar documento | `SalesInvoicesCancelService` | Detalhado abaixo. |
| 5 | Realocar | `SalesContractsReallocationCreateService.ExecuteAsync` | Linha de origem da realocação (D7). |
| 5 | Desfazer realocação | `SalesContractsReallocationDeleteService` | Linha de origem da realocação (D7). |
| 6 | Devolução muda o estado da origem | Ver nota abaixo | — |

**Ponto 4, cancelar documento:**

- A checagem só de leitura (`EnsureNoBlockingSalesFirmsAsync`) entra em `EnsureBusinessRulesAsync`. Ela roda tanto em
  `EnsureCanCancelAsync` (ensaio antes da SEFAZ) quanto no cancelamento.
- O cancelamento dos soltos acontece em `CancelAsync`. Vale para os dois caminhos, antes e depois do cancelamento da
  NF-e.

**Ponto 6, a origem da devolução:** a origem passa a `Returned` (total) ou volta a `Confirmed` (estorno). Isso **não**
muda os firmes dela: G1 aceita `Returned`, e a devolução tem os títulos dela.

## Regras

### Nascimento e desfazer

1. A confirmação cria os firmes das linhas que passam em G, um por contrato da distribuição.
2. Idempotente: reconfirmar ou chamar de novo não duplica nada. O índice é a segunda camada.
3. Estornar a confirmação ou cancelar o documento cancela os títulos soltos, com o motivo "Documento de saída cancelado
   ou estornado". Título composto ou com baixa recusa (S1/S2), e no cancelamento a recusa vem **antes** da SEFAZ.
4. O título nascido do documento de saída não se cancela pela tela do financeiro (S5).

### Realocação (D7)

5. Criar ou desfazer uma realocação ressincroniza a linha. A parte realocada vira firme no contrato de destino, e o firme
   de origem é recalculado para menos. Firme envolvido composto ou baixado recusa (S3/S4).
6. "Conciliação" (realocação ADMIN que fura o saldo) segue a mesma regra: ela também grava o par −/+ no ledger.

### Devolução (D3)

7. A devolução confirmada cria um título `SalesReturn` por contrato da distribuição dela. As linhas de ledger da
   devolução já seguem a distribuição vigente da origem.
8. O valor é o `GrandTotal` da linha de devolução, distribuído como no modelo.
9. Compensação automática: `FinancialDocumentsNettingService.EnqueueAutoAsync`, generalizado.
   - Para a venda, as origens são os firmes ativos da **linha de origem** (`SalesInvoiceItemOriginKey`) **no mesmo
     contrato** do título de devolução, soltos e com saldo, do mais antigo para o mais novo.
   - O resto fica em aberto para o operador: compor negativo no provisório ou "Compensar com firme".
10. "Compensar com firme" (manual) vale para `SalesReturn` contra um `Firm` de venda **do mesmo contrato de venda**.
11. Nota de devolução **sem** linha de origem (legado): nasce em aberto, sem compensação automática.

### Generalização do financeiro

12. `IsPurchaseFirmPayable` dá lugar a `IsContractFirmDocument`: o valor atual mais `OriginType == SalesInvoice` com
    `Nature is Firm or SalesReturn`.
    - Dois auxiliares substituem a comparação de contrato: `IsSalesFirmReceivable` (só venda) e `ContractKey` (o
      `PurchaseContractKey ?? SalesContractKey`, `[NotMapped]`).
    - `IsBlockedForSettlement` usa o gate novo.
13. "Mesmo contrato" passa a comparar o **par** `(PurchaseContractKey, SalesContractKey)`. Um título de compra nunca casa
    com um de venda. Valem os serviços abaixo:

    | Serviço | O que muda |
    |---|---|
    | `FinancialDocumentsComposeService.EnsureComposable` | compara o par |
    | `FinancialDocumentsGetCompositionService` | candidatos do provisório e da devolução; firmes e devoluções do lado do provisório |
    | `FinancialDocumentsDationService` | a dação passa a valer para firme de venda (`Nature == Firm`) |
    | `FinancialDocumentsNettingService` | ver regras 9–10 |
    | `FinancialDocumentsSettlementGuardService` | usa o gate novo |
    | `FinancialDocumentsApplyAdvanceService` | adiantamento de venda no provisório a receber: o provisório precisa de contrato (compra ou venda), mesmo contrato e mesma direção |
    | `FinancialDocumentsGetAdvanceCandidatesService` | idem |
    | `FinancialDocumentsCancelService` | regra 4 |

14. Os textos de recusa da compra **não mudam**: os testes e o operador já os conhecem. Onde o texto cita "conciliação
    fiscal", a venda usa o texto próprio (mensagens S6–S8).

### Contrato de venda (D4)

15. `SalesContractsCloseService` deixa de cancelar o provisório e ganha as travas da compra, na ordem:
    1. firme ou devolução de venda (origem `SalesInvoice`) com saldo → S9;
    2. cancelamento dos provisórios legados intocados (corte);
    3. provisório não cancelado e não quitado → S10;
    4. adiantamento com crédito não aplicado → S11, mesmo texto da compra.
16. Chave de corte **própria**: `Financial:SalesFirmReceivablesCutoverDate` (yyyy-MM-dd).
    - Provisório criado antes dela, com composto 0 e baixado 0, é cancelado no encerramento com o motivo do legado da
      compra.
    - Sem a chave, ou com a chave inválida (log de aviso), nada é isento.
17. Reabrir: `SalesContractsReopenService` já chama o gerador idempotente. O provisório legado cancelado no encerramento
    é recriado, como o D15 da compra.
18. Cancelar uma fixação de venda (`SalesContractsPriceFixationsCancelService`) recusa enquanto o provisório dela tiver
    composição ou baixa (S12). Retirar a aprovação e cancelar o contrato já recusam, por
    `EnqueueCancelByContractAsync`.

### Documentos legados (decisão do controlador, a confirmar na revisão do spec)

19. Documento de saída confirmado **antes** do deploy não tem firme. Para o contrato em andamento conseguir quitar o
    provisório, existe a action `FinancialDocumentsGenerateSalesFirmBacklog(BranchCode, DateFrom, DateTo)`:
    - restrita a ADMIN, no molde de `FinancialDocumentsGenerateBacklog`;
    - roda a sincronização nas linhas confirmadas da janela cujo contrato está `Approved`;
    - devolve gerados e pulados;
    - é idempotente.

    Contrato encerrado não é tocado.

## Mensagens (pt-BR, exatas)

| # | Mensagem |
|---|---|
| S1 | `O título {firme} do documento de saída {número} já está composto no título {provisório}: desfaça a composição antes de cancelar ou estornar o documento.` |
| S2 | `O título {firme} do documento de saída {número} já tem baixa por dação ou encontro de contas: estorne-a antes de cancelar ou estornar o documento.` |
| S3 | `O título {firme} da linha realocada já está composto no título {provisório}: desfaça a composição antes de realocar.` |
| S4 | `O título {firme} da linha realocada já tem baixa por dação ou encontro de contas: estorne-a antes de realocar.` |
| S5 | `Título firme do documento de saída nasce da confirmação: cancele ou estorne o documento de saída para cancelá-lo.` |
| S6 | Compor um título que não é firme nem devolução (venda): `O título {código} não é firme nem devolução de documento de saída.` |
| S7 | Dação em título de venda que não é firme: `Dação só se aplica a título firme do documento de saída.` (a compra mantém o texto atual) |
| S8 | Compensar com um título que não é firme de venda: `O título {código} não é um título firme do documento de saída.` |
| S9 | `Há títulos firmes com saldo neste contrato: componha, compense ou baixe por dação antes de encerrar.` (igual à compra) |
| S10 | `O título {código} não está quitado: o contrato só encerra com todos os títulos quitados.` |
| S11 | Igual à compra (adiantamento com crédito). |
| S12 | `O título {código} da fixação tem composição ou baixa: desfaça a composição e estorne as baixas antes de cancelar a fixação.` |

Motivo de cancelamento do firme: `Documento de saída cancelado ou estornado`. Na realocação, o firme que sai:
`Volume realocado para outro contrato`.

## Telas (frontend)

20. **Contrato de venda, seção "Financeiro"** (`view/salesContracts/Detail.view.xml`).
    - Mesmas colunas da seção da compra, só leitura, via `FinancialDocumentsGetByContract`, que já aceita venda.
    - Sem as colunas Romaneio. Com "Documento de saída" (número) e NF.
    - O fragmento da compra é generalizado se couber limpo; senão, cópia com o nome de venda.
21. **Detalhe do título** (`view/financialDocuments/fragments/Form.fragment.xml`).
    - Bloco do documento de saída (número, NF) e o link "Abrir documento de saída" (rota `salesInvoicesDetail`) quando
      `OriginType = SalesInvoice`.
    - O bloco de compra continua como está.
22. **Ações do detalhe:**
    - "Baixar por dação" aparece também para o firme de venda.
    - "Compensar com firme" aparece também para `SalesReturn`.
    - A composição do provisório a receber usa os mesmos diálogos.
    - Os formatters ficam em `financialDocuments`.
23. **Natureza `SalesReturn`** aparece como "Devolução de venda" na lista, no detalhe, no filtro e na seção do contrato.
24. **Contas a Receber:** `setPayablesFilters(true)`. A lista ganha o filtro Natureza, com os itens do lado do receber
    (Provisório, Firme, Devolução de venda), e "Com saldo a resolver".

## Testes e verificação

**Application.Tests:**

- Nascimento nos dois caminhos (confirmação manual e Expedir e Faturar).
- G1–G4: sem contrato, natureza sem flag, custódia, SAPB1.
- Idempotência.
- Complemento de preço (`Volume = 0`).
- Realocação: desmembra, junta e recusa composto.
- Estorno e cancelamento: solto cancela, composto recusa antes da SEFAZ.
- Devolução:
  - compensação automática;
  - origem composta fica em aberto;
  - manual;
  - contrato diferente recusa.
- Composição, dação, guard de baixa e adiantamento de venda no provisório a receber.
- Encerrar e reabrir com corte.
- Cancelar fixação com composição.
- Backlog.
- Regressão da compra: 4706 testes hoje.

**Fiscal.Tests:** sem mudança esperada.

**Migration:**

- coluna `SalesInvoiceKey`;
- índice `IX_FINANCIAL_DOCUMENTS_FirmOrigin` recriado com `SalesContractKey` e o filtro novo.

Aplicada só no CEAGUI_SIAGRO_DEV, com `ASPNETCORE_ENVIRONMENT=Ceagui-Development` explícito. Nunca com o profile
db-migration.

**Navegador (CEAGUI dev):**

1. Expedir e Faturar: o firme aparece no contrato de venda.
2. Compor no provisório e receber no provisório.
3. Dação da sobra.
4. Devolução compensa.
5. Realocar desmembra.
6. Estornar recusa com composto.
7. Encerrar recusa e depois permite.

## Fora

- Washout de venda (sub-projeto B).
- Saída avulsa sem contrato com cobrança.
- Parcelamento pela condição de pagamento.
- Recebimento em dinheiro de devolução.
- Retenções na venda.
- Variação cambial (contrato em USD não compõe com firme em BRL, igual à compra).
