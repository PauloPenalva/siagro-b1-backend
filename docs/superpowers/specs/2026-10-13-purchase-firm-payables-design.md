# Títulos firmes de compra: conciliação, composição no provisório e dação

Data: 2026-10-13
Status: aprovado para planejamento
Modo alvo: **os dois** (`STANDALONE` e `SAPB1`). Em `SAPB1` o Funrural da linha do documento é zero e o
firme sai pelo bruto.

Sub-projeto **2** de "título a pagar firme a partir do romaneio de compra". O sub-projeto 1
(`2026-10-13-purchase-contract-fiscal-complement-design.md`) está mergeado em `main` e é **desfeito** por este (ver abaixo).

## O que mudou em relação ao roadmap do sub-projeto 1

O roadmap registrado no fim do spec do sub-projeto 1 previa o valor entregue calculado com o **preço
do contrato** (romaneio alocado × preço da fixação). **O usuário reverteu isso** neste desenho:

> "Um conta a pagar firme só deve existir mediante a conciliação a um documento de entrada, seja nota
> fiscal ou não, para podermos ter preço. Pegar o preço pelo contrato é errado porque o produtor pode
> emitir uma nota com valor abaixo ou acima, ficando para o fiscal resolver."

Consequências:

- o preço vem do **documento de entrada** (`PURCHASE_INVOICES`), não do contrato;
- o Funrural vem da **linha do documento** (`PurchaseInvoiceItem.Funrural*`), não da natureza do
  contrato. O totalizador do sub-projeto 1 segue como **previsão** no contrato;
- a distribuição em "ordem de confirmação" deixa de existir: quem decide em qual provisório cada firme
  entra é o **operador**, compondo à mão.

Continua valendo do roadmap: um provisório por fixação (FIX e PAF) é o **único** título que recebe
baixa em dinheiro; trava rígida (paga-se só o que está coberto); a NF não precisa ser fiscal ("seja
nota fiscal ou não").

### Reversão do sub-projeto 1 (decisão do usuário ao revisar este spec)

Com o Funrural e a flag "gera financeiro" vindo da **natureza do documento de entrada** conciliado
(NF-e ou Outros), a natureza no contrato perdeu a razão de existir. O usuário escolheu **desfazer o
sub-projeto 1, sem trazer a grade de impostos de volta**:

- **sai**: `PURCHASE_CONTRACT_FISCAL_COMPLEMENTS` (migration nova `DropPurchaseContractFiscalComplement`
  — as migrations antigas já estão aplicadas na CEAGUI dev e não são reescritas), a permissão
  `PURCHASE_CONTRACT_FISCAL_EDIT` (migration `RemovePurchaseContractFiscalEditPermission`, idempotente),
  `PurchaseContractFiscalComplementService`, function/action e controllers, a trava em
  `PurchaseContractsApprovalService` (e o parâmetro `IConfiguration` que só ela usava), o totalizador
  em `PurchaseContractsTotalsService` (e os 9 campos de Funrural do DTO), `ContractChangeLogFields.Usage`,
  as seções "Complemento Fiscal" e o quadro de Funrural no Detail e na Aprovação, o KPI "Líquido ao
  produtor", o helper `PurchaseContractFunruralHelpers` e o rótulo `Usage` do formatter;
- **a grade digitada não volta em STANDALONE**: a seção "Impostos" e o "Total Impostos" ficam ocultos
  em STANDALONE (`TotalTax` volta a ser a soma da grade, que em STANDALONE não é mais editável nem
  exibida). Em SAPB1 tudo como antes;
- **fica**, por não depender da natureza: a correção do tratamento de erro da tela de Aprovação
  (mensagens de negócio na tela) e a porta única `applyContractTotals`;
- o comentário de `PurchaseContract.FunruralType` passa a dizer que o Funrural vem do documento de
  entrada;
- **o sub-projeto 4 sai do roadmap** (dependia da natureza no contrato). Restam este (2) e a
  compensação de adiantamento (3).

## Decisões do usuário

| # | Decisão |
|---|---|
| D1 | Vale nos **dois modos**. |
| D2 | O firme nasce da **conciliação** NF × romaneio (gravação do vínculo), observando a flag "gera financeiro" da natureza da linha. Sem documento de entrada não há firme. |
| D3 | A composição (aglutinar firmes no provisório) é **manual**, feita pelo operador, em FIX e PAF. |
| D4 | A composição **para no valor do provisório**. A sobra de um firme (faturado acima do contrato) é baixada por **dação** pelo **operador**, com motivo. |
| D5 | Devolução gera um título **do tipo devolução**. Se a origem estiver em aberto, compensa o firme normal; senão fica em aberto para o operador. Vale também para a devolução da diferença de descarga (troca nota). |
| D6 | Estornar fixação é recusado enquanto o provisório dela tiver composição; desfaz-se a composição primeiro, **sem apagar os firmes**, que voltam a ficar soltos. |
| D7 | Estornar romaneio é recusado se o título em que ele compõe tiver baixa ou compensação. |
| D8 | Encerrar contrato exige **todo provisório quitado** (baixa + compensação = valor). Entrega a menos se resolve por **washout**. |
| D9 | Reabrir contrato **não altera valores**. |

## Modelo de dados

### Enums (sem migration — colunas já `int`; valores novos no fim, nunca renumerar)

```
FinancialDocumentNature   += PurchaseReturn = 4
FinancialDocumentOrigin   += PurchaseFiscalReconciliation = 7
FinancialSettlementOrigin += Dation = 6
```

Já existentes e passam a ser escritos: `FinancialDocumentNature.Firm = 1`,
`FinancialSettlementOrigin.InvoiceOffset = 2` (abatimento por composição) e `Netting = 4` (encontro
de contas da devolução).

### `FINANCIAL_DOCUMENTS` — colunas novas

| Coluna | Tipo | Observação |
|---|---|---|
| `PurchaseInvoiceKey` | uniqueidentifier NULL | documento de entrada do firme. **Sem FK** (o título sobrevive ao registro que narra, como `OriginKey`) |
| `StorageTransactionKey` | uniqueidentifier NULL | romaneio do vínculo. Sem FK |
| `StorageTransactionCode` | `VARCHAR(50)` NULL | código do romaneio, desnormalizado para a lista |
| `TaxDocumentNumber` | `VARCHAR(50)` NULL | `PurchaseFiscalReconciliationRules.TaxDocument(...)`: "número-série" da NF ou o número interno do documento sem NF |
| `ComposedAmount` | `DECIMAL(18,2) DEFAULT 0` | **só no provisório**: persistido-derivado, Σ `FINANCIAL_DOCUMENT_COMPOSITIONS.Amount`. Escritor único: o recalculador |

`[NotMapped]` nova: `AvailableToSettleAmount` = provisório: `Round(ComposedAmount − SettledAmount, 2)`
(mínimo 0); demais naturezas: `OpenAmount`. Exposta no EDM por `AddProperty`, como as outras quatro.

`IsBlockedForSettlement` muda de `Nature == Provisional` para:

- provisório: `AvailableToSettleAmount <= 0`;
- `Firm` e `PurchaseReturn`: **sempre `true`** — firme só sai por composição, dação ou encontro de
  contas, nunca por baixa em dinheiro;
- adiantamento: `false` (como hoje).

O binding do botão "Baixar" (`visible="{= !${IsBlockedForSettlement} }"`) continua o mesmo.

### `FINANCIAL_DOCUMENT_COMPOSITIONS` (nova) — `FinancialDocumentComposition : BaseEntity`

| Coluna | Tipo | Observação |
|---|---|---|
| `ProvisionalDocumentKey` | uniqueidentifier NOT NULL | FK → `FINANCIAL_DOCUMENTS`, NoAction |
| `ComposedDocumentKey` | uniqueidentifier NOT NULL | FK → `FINANCIAL_DOCUMENTS` (firme ou devolução), NoAction |
| `Amount` | `DECIMAL(18,2)` | **assinado**: firme positivo, devolução **negativa** |

Índices: `ProvisionalDocumentKey`, `ComposedDocumentKey`. As duas FKs para a mesma tabela são
declaradas inline (`HasOne(...).WithMany().HasForeignKey(...)`) para a convenção do EF não emparelhar
errado — **sem coleção inversa** no `FinancialDocument` (entraria no EDM).

A linha é **removida** ao desfazer a composição; o rastro fica no ledger (abatimento + estorno) e no
log dos dois títulos.

### `FINANCIAL_SETTLEMENTS` — colunas novas

| Coluna | Tipo | Observação |
|---|---|---|
| `RelatedDocumentKey` | uniqueidentifier NULL | abatimento: o provisório; encontro de contas: o título do outro lado. Sem FK |
| `CompositionKey` | uniqueidentifier NULL | abatimento: a linha de composição que o gerou — é como "desfazer" acha a linha a estornar. Sem FK (a composição é removida; o ledger não) |

### Idempotência do firme

Índice único filtrado inline, par do `IX_FINANCIAL_DOCUMENTS_ProvisionalOrigin`:

```
IX_FINANCIAL_DOCUMENTS_FirmOrigin on (OriginType, OriginKey)
filter: [Nature] IN (1, 4) AND [Status] <> 3 AND [OriginKey] IS NOT NULL
```

## Regras

### Nascimento do firme (seção aprovada 1)

1. O firme nasce quando um vínculo `PURCHASE_FISCAL_RECONCILIATIONS` é **gravado**
   (`PurchaseFiscalReconciliationService.SaveAsync`), **não** na confirmação manual "Conciliado".
   **Um firme por vínculo**; `OriginType = PurchaseFiscalReconciliation`, `OriginKey = vínculo.Key`.
2. Só nasce se a linha do documento tem `CreatesFinancialDocument = true` e `PurchaseContractKey`
   preenchido. Contra-nota e custódia já são recusadas pela conciliação (`EnsurePair`).
3. **Natureza pelo sentido** (`PurchaseFiscalReconciliationRules.SenseMatches`): documento normal
   (romaneio 8/10/17) → `Firm`; documento de devolução (romaneio 9/16) → `PurchaseReturn`.
4. **Valor:** `Round(qtdConciliadaKg ÷ qtdLinhaKg × (linha.GrandTotal − linha.FunruralValue), 2,
   ToEven)`, com `qtdLinhaKg` pela `PurchaseFiscalReconciliationRules.KgFactor`. Em `SAPB1` o Funrural
   da linha é zero.
5. **Campos:** `Direction = Payable`; `CardCode`/`CardName` = **emitente do documento**;
   `PurchaseContractKey` = o da linha; `Currency` = a do contrato; `BranchCode` = o do documento;
   `DocumentDate` = `DueDate` = data de emissão do documento (firme não é pago direto — o vencimento
   que vale é o do provisório); `PurchaseInvoiceKey`, `StorageTransactionKey`/`Code`,
   `TaxDocumentNumber`; `OriginDocNumber` = código do contrato; `Code` pelo `DocNumberSequenceService`
   (`TransactionCode.FinancialDocument`), buscado **antes** do `SaveChanges` (conexão Dapper própria,
   ver o risco de numeração do spec da Fase 1).
6. **Vínculo alterado** (quantidade): firme **solto** é recalculado (log do campo `NetAmount`); firme
   **composto** → a alteração é recusada: `"O título {código} do vínculo já está composto no título
   {provisório}: desfaça a composição antes de alterar a conciliação."`.
7. **Vínculo removido** (quantidade 0, remoção, NF ou romaneio cancelado/excluído pelo interceptor):
   firme solto → `Canceled` com motivo `"Vínculo fiscal desfeito"`; composto → recusado com a mesma
   mensagem. Firme com baixa por dação ou encontro de contas conta como "não solto" para esta regra.
8. **Devolução nasce** → compensação automática (regras 18–19).

### Composição (seção aprovada 2)

9. **Compor** (`FinancialDocumentsComposeService`, action no provisório): recebe o provisório e a lista
   de firmes/devoluções. Pré-condições: provisório não cancelado; cada título do **mesmo contrato**
   (não do mesmo `CardCode` — produtor com várias IEs é vários parceiros), não cancelado, com
   `OpenAmount > 0`.
10. Firme entra por `min(OpenAmount do firme, espaço)`, com `espaço = NetAmount − ComposedAmount` do
    provisório, na ordem em que o operador listou. Firme que não cabe inteiro entra **parcial**; firme
    sem espaço nenhum é recusado (`"O título {provisório} já está integralmente composto."`).
11. Devolução entra pelo **valor negativo** do seu `OpenAmount` (libera espaço). Recusada se
    `ComposedAmount` ficaria abaixo do `SettledAmount` do provisório.
12. Para cada título composto, no mesmo `SaveChanges`: linha de composição; no **título composto**, uma
    baixa `Origin = InvoiceOffset`, `Amount = |valor|`, sem conta, `RelatedDocumentKey` = provisório,
    `CompositionKey` = a linha, `DocumentReference` = código do provisório; recálculo dos dois; log nos
    dois (`FinancialDocumentChangeLogFields.Composition`).
13. **Desfazer** (`FinancialDocumentsUncomposeService`, por linha de composição): grava no título
    composto a linha `Origin = Reversal` espelho do abatimento (`ReversedSettlementKey`), remove a linha
    de composição, recalcula e loga. Recusado se, sem a linha, `ComposedAmount < SettledAmount` do
    provisório: `"Estorne a baixa do título {provisório} antes de desfazer a composição."`.
14. O `FinancialDocumentsReverseSettlementService` genérico **recusa** estornar linha `InvoiceOffset`
    (`"Abatimento de composição se desfaz pela composição."`) — desfazer só pela regra 13.

### Baixa e dação (seção aprovada 2)

15. Guard (`FinancialDocumentsSettlementGuardService`): no provisório, valor ≤
    `AvailableToSettleAmount` (`"Valor acima do disponível para baixa ({x}): componha mais títulos
    firmes antes."`); firme e devolução recusam baixa manual (`"Título firme não recebe baixa direta:
    componha no provisório ou baixe por dação."`). Os demais guards (conta, moeda, cancelado) seguem.
16. **Status do provisório** continua derivado de `SettledAmount` × `NetAmount` — só fica `Settled`
    quando a baixa alcança o valor contratado. Composto e pago, mas abaixo do contratado, é
    `PartiallySettled`.
17. **Dação** (`FinancialDocumentsDationService`, action no firme): valor ≤ `OpenAmount`, motivo
    obrigatório (`Notes`), sem conta, `Origin = Dation`. Só em `Firm` (não em devolução nem
    provisório). Estornável pelo estorno genérico.

### Devolução (seção aprovada 3)

18. **Origem** da devolução: os firmes normais nascidos de vínculos da linha
    `PurchaseInvoiceItem.PurchaseInvoiceItemOriginKey` da linha de devolução.
19. **Compensação automática** ao nascer (`FinancialDocumentsNettingService`): para cada firme de
    origem **solto com saldo**, do mais antigo (`CreatedAt`, depois `RowId`), compensa
    `min(saldos)` — uma baixa `Origin = Netting` em cada título, sem conta, `RelatedDocumentKey`
    apontando o outro. Sem origem, ou origem composta/quitada → a devolução fica em aberto.
20. **Ações do operador** sobre devolução em aberto: compor negativa no provisório (regra 11) ou
    **Compensar com firme** solto do mesmo contrato (mesma mecânica da 19, valor `min(saldos)`).
21. Estornar uma linha `Netting` pelo estorno genérico estorna **o par** no mesmo ato.
22. Recebimento em dinheiro da devolução: **fora**.

### Travas (seção aprovada 4)

23. **Estornar romaneio** = excluir a alocação (`PurchaseContractsAllocationDeleteService.ExecuteAsync`
    — o único caminho de todos os estornos). Antes de remover:
    - algum firme do romaneio naquele contrato composto em provisório com `SettledAmount ≠ 0` →
      recusa: `"O romaneio {código} compõe o título {provisório}, que já tem baixa ou compensação:
      estorne a baixa antes."`;
    - senão: desfaz a composição desses firmes (regra 13), **remove os vínculos fiscais** do romaneio
      com documentos daquele contrato e cancela os firmes (regra 7). O romaneio volta a "Sem NF".
24. **Devolução (9) e quebra (16)** alocadas: **sem trava** — o efeito vem do título de devolução.
25. **Estornar fixação** (`PurchaseContractsPriceFixationsCancelService`): recusado se o provisório dela
    tem `ComposedAmount ≠ 0` ou `SettledAmount ≠ 0`: `"Desfaça a composição do título {provisório}
    antes de estornar a fixação."`.
26. **Washout** (`FinancialDocumentsAdjustProvisionalService`): recusa ajustar `NetAmount` abaixo do
    `ComposedAmount`, e recusa cancelar provisório com composição ou baixa.
27. **Cancelar contrato** e **retirar aprovação**: recusados se algum provisório do contrato tiver
    composição ou baixa (`EnqueueCancelByContractAsync` passa a checar).
28. **Encerrar contrato** (`PurchaseContractsCloseService`):
    - recusa se existir firme/devolução **do contrato** não cancelado com `OpenAmount > 0`:
      `"Há títulos firmes com saldo neste contrato: componha, compense ou baixe por dação antes de
      encerrar."`;
    - recusa se algum provisório não cancelado não estiver `Settled`: `"O título {provisório} não está
      quitado: o contrato só encerra com todos os títulos quitados. Entrega a menos se resolve por
      washout."`;
    - **não** cancela mais o provisório (remove o `EnqueueCancelByContractAsync` daqui) e **não**
      altera valores.
29. **Reabrir contrato**: nenhum valor muda; o gerador segue idempotente (pula provisório existente).
30. `FinishedContractMutationGuardInterceptor` **não** ganha `FinancialDocument` nem a composição (o
    aviso do spec da Fase 1 continua valendo).

### Onde a lógica mora

A remoção de vínculo pelo `PurchaseFiscalReconciliationCleanupInterceptor` (Infra) precisa cancelar ou
recusar o firme (regra 7), e o interceptor não enxerga `Application`. A regra pura "este vínculo pode
sumir? cancele o firme solto" fica num **estático em `SiagroB1.Infra`** (ao lado de
`PurchaseFiscalReconciliationRules`), usado pelo interceptor e pelos serviços de Application. Criação
e recálculo do firme (que precisam de numeração e da linha do documento) ficam em Application,
chamados por `PurchaseFiscalReconciliationService`.

## Serviços e API

Namespace `SiagroB1.Application/Services/Financials/`.

| Serviço | Responsabilidade |
|---|---|
| `FinancialDocumentsFirmSyncService` | enqueue-only: cria/recalcula/cancela o firme de um vínculo (regras 1–7); dispara a compensação automática da devolução |
| `FinancialDocumentsComposeService` | regras 9–12, `CommitMode` |
| `FinancialDocumentsUncomposeService` | regra 13 |
| `FinancialDocumentsDationService` | regra 17 |
| `FinancialDocumentsNettingService` | regras 19–21 (automática e manual) |
| `FinancialDocumentsRecalculateBalanceService` | + `ComposedAmount` (Σ composição) |
| `FinancialDocumentsSettlementGuardService` | regra 15 |
| `FinancialDocumentsReverseSettlementService` | regras 14 e 21 |
| `FinancialDocumentsGetCompositionService` | leitura da composição de um provisório e dos firmes soltos compostáveis |

Ganchos: `PurchaseFiscalReconciliationService` (Save/Remove), `PurchaseFiscalReconciliationCleanupInterceptor`,
`PurchaseContractsAllocationDeleteService`, `PurchaseContractsPriceFixationsCancelService`,
`FinancialDocumentsAdjustProvisionalService`, `FinancialDocumentsCancelService.EnqueueCancelByContractAsync`,
`PurchaseContractsCloseService`.

OData — actions (rotas literais nas duas formas, `ODataActionParameters` nulo guardado, valores
`Edm.Double`, chaves `Guid`): `FinancialDocumentsCompose(Key, ComposedKeys: Collection(Guid))`,
`FinancialDocumentsUncompose(CompositionKey)`, `FinancialDocumentsDation(Key, Amount, SettlementDate,
Notes)`, `FinancialDocumentsNetting(Key, CounterpartKey)`. Function
`FinancialDocumentsGetComposition(Key)` → `{ Lines[], Candidates[] }` com DTO PascalCase
(`[JsonPropertyName]`). Entity set `FinancialDocumentCompositions` somente leitura **não** é criado —
a function basta. Erros de negócio: `BadRequest(string)`.

## Frontend (seção aprovada 5)

- **Detail do título** (`financialDocuments/Detail`):
  - provisório: seção **"Composição"** (tabela: título, romaneio, nº NF, produtor, valor, tipo — devolução
    com sinal), botões **Compor** (diálogo com candidatos e valor sugerido) e **Desfazer** por linha;
    cabeçalho com Valor contratado, Composto, Baixado, **Disponível para baixa**;
  - firme/devolução: romaneio, nº NF, documento de entrada, "composto em" (link); **Baixar por dação**
    (firme), **Compensar com firme** (devolução); "Baixar" oculto (já pelo binding);
  - tabela de Baixas: coluna **Origem** com formatter (Manual, Estorno, Abatimento, Encontro de
    contas, Dação, Devolução de adiantamento).
- **Contas a Pagar** (lista): filtro **Natureza** (Provisório/Firme/Devolução), filtro **"Com saldo a
  resolver"** (firme/devolução não cancelado com `OpenAmount > 0`), colunas nº NF e romaneio.
- **Contrato de compra — Detail:** seção **"Financeiro"** com `FinancialDocumentsGetByContract`
  (provisórios e firmes, valor, composto, baixado, saldo, link).
- **Conciliação Fiscal de Compras:** a linha do vínculo mostra o firme (código com link) e a situação
  (solto / composto / cancelado).
- Toda recusa nova aparece na tela: handlers aceitam as três formas (`string`, `.message`,
  `.error.message`).
- Armadilhas: `--` em comentário XML; `core:require` no próprio fragmento; enum com `targetType:
  'any'`; `$filter` cru para enum; valor monetário editável `Edm.Double`; data ISO completa.

## Migrations (seção aprovada 6)

`AppContext/AddFinancialDocumentComposition`: tabela de composição, colunas novas em
`FINANCIAL_DOCUMENTS` e `FINANCIAL_SETTLEMENTS`, índice `IX_FINANCIAL_DOCUMENTS_FirmOrigin`.
Nenhuma migration de menu (sem tela nova).

**Sem backfill.** Provisórios existentes ficam com `ComposedAmount = 0` → disponível 0 (igual ao
bloqueio de hoje). Vínculos de conciliação existentes não geram firme retroativo (hoje só há dado de
teste na CEAGUI dev); quem precisar, regrava o vínculo.

## Testes (`SiagroB1.Application.Tests`)

- firme: valor proporcional com Funrural da linha; flag "gera financeiro" desligada não gera; linha
  sem contrato não gera; devolução gera `PurchaseReturn`; idempotência; ajuste e cancelamento com o
  vínculo; recusa com firme composto; interceptor cancelando firme solto ao cancelar NF/romaneio;
- composição: teto; parcial; devolução negativa; recusa sem espaço; desfazer; desfazer recusado com
  baixa; estorno genérico de abatimento recusado;
- baixa: disponível = composto − baixado; firme recusa baixa manual; dação e seu estorno;
- devolução: compensação automática com origem solta (par de `Netting`); origem composta deixa em
  aberto; compensação manual; estorno do par;
- travas: estorno de romaneio com baixa (recusa) e sem baixa (desfaz composição, vínculo e firme);
  devolução/quebra sem trava; estorno de fixação com composição; washout abaixo do composto;
  cancelar/retirar aprovação com composição; encerrar com firme solto, com provisório não quitado, e
  quitado (passa, sem alterar valor); reabrir sem alterar valor;
- `SAPB1`: firme pelo bruto;
- EDM: actions, parâmetros `Edm.Double`/`Guid`, `AvailableToSettleAmount` presente.

## Verificação no navegador (CEAGUI)

1. Contrato fixo aprovado, romaneio alocado.
2. Conciliar a NF → firme solto com nº NF na lista "Com saldo a resolver".
3. Compor no provisório → Disponível para baixa = valor do firme. Baixar.
4. Tentar estornar o romaneio → recusa na tela.
5. Devolução conciliada com origem solta → par de encontro de contas.
6. Documento faturado acima do contrato → composição parcial; dação da sobra.
7. Encerrar com provisório não quitado → recusa; washout cobrindo a entrega a menos; quitar; encerrar.
8. Seção Financeiro do contrato e coluna Origem nas baixas.
9. Derrubar a stack por PID.

## Fora

- Compensação de adiantamento contra o provisório (sub-projeto 3 — usará `AvailableToSettleAmount`).
- Recebimento em dinheiro de devolução.
- Natureza do contrato como padrão na Entrada — removido do roadmap junto com o sub-projeto 1.
- Retenção de Funrural na Yokotobi (`SAPB1`).
- Contas a Receber (venda) — este desenho é só compra.
