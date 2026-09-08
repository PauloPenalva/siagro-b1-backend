# Contas a Pagar e Contas a Receber — Fase 1: compromisso e adiantamento

Data: 2026-09-07
Status: aprovado para planejamento
Modo alvo: **os dois** (`SAPB1` e `STANDALONE`) — os documentos financeiros vivem no banco do
Siagro em ambos, e nada é escrito no SAP B1.

## Problema

O SiagroB1 controla o ciclo físico e fiscal da originação de grãos de ponta a ponta —
contrato, liberação de entrega, romaneio, montagem de carga, documento de saída, documento de
entrada — e **não tem nenhum controle financeiro**. Uma varredura no backend não encontra
nenhuma ocorrência de `Bank`, `Receivable`, `Payable`, `Installment`, `Settlement`,
`CashFlow`, `PaymentTerm`, `DueDate` ou `JournalEntry`.

O que existe hoje, e é tudo:

- `PurchaseContract.PaymentTerms` / `SalesContract.PaymentTerms` — `VARCHAR(500)` de **texto
  livre**. Não há entidade de condição de pagamento, nem número de parcelas.
- `StandardCashFlowDate` nos dois contratos — previsão de pagamento, **opcional**, lida apenas
  por `PurchaseContractsByItemReportService` e `SalesContractsByItemReportService`.
- `PurchaseContractPriceFixation.FinancialDueDate` + `PaymentDetails` — o **único vencimento
  datado do modelo**, e a coisa mais próxima de um título que o sistema tem: volume, preço,
  vencimento e dados de pagamento em texto livre, sem valor, sem baixa e sem status financeiro.
- `TAXES` (`Code`/`Name`/`Rate`), `PURCHASE_CONTRACTS_TAXES` (com `TotalTax` calculado sobre o
  valor fixado) e `FunruralType { Livre, Bruto }` no contrato de compra — a retenção do
  produtor já está cadastrada e **não produz nenhum efeito financeiro**.
- `CurrencyType { Brl = 1, Usd = 2 }` e `StandardCurrency` nos dois contratos — moeda já
  modelada, sem nenhum consumidor financeiro.
- `LEDGER_ACCOUNTS` / `COST_CENTERS` (dual-mode `OACT`/`OPRC`) e os códigos já gravados na
  linha do documento de saída — metadados sem nenhum lançamento que os consuma.

Consequência prática: o cliente sabe quanto grão comprou e vendeu, e não sabe quanto deve nem
quanto tem a receber por causa disso.

## A decisão central: o documento financeiro nasce do CONTRATO, não do documento fiscal

O roadmap fiscal/financeiro de 04/08/2026 (`2026-08-04-general-purpose-sales-invoice-design.md`,
seção "Sequência") previa o financeiro como sub-projeto 4, alimentado pelos documentos fiscais.

**O ponto de partida mudou por decisão do usuário**: o contrato aprovado gera um documento
financeiro **provisório**, bloqueado para baixa; o documento fiscal apenas o converte em firme.

Isso não é uma concessão — é o modelo das trading companies. O *contract book* alimenta a
projeção de caixa e a exposição por contraparte **desde o compromisso**, e a entrega converte
projeção em obrigação firme. Um razão auxiliar que só enxerga o que já foi faturado não
responde à pergunta que a mesa de originação faz todo dia: quanto vamos desembolsar nos
próximos 90 dias?

## A descoberta que unifica o gerador: contrato de preço fixo JÁ tem uma fixação

`PurchaseContractsCreateService.CreatePriceFixation` e `SalesContractsCreateService.BuildAutoFixation`
criam, na criação do contrato de preço fixo, **uma fixação `Confirmed`** com
`FixationVolume = TotalVolume` e `FixationPrice = StandardPrice`. O comentário no próprio código
explica o porquê: num contrato de preço fixo o preço já foi acordado na negociação, e nascer
`InApproval` zeraria `TotalPrice` e `TotalTax`.

Consequência para esta feature: **os dois tipos de contrato usam o mesmo gerador, com origem
sempre na fixação.** Não existe `if (Type == Fixed)` em lugar nenhum.

- Contrato `Fixed` aprovado → itera as fixações `Confirmed` → encontra exatamente **uma** → gera
  1 documento provisório.
- Contrato `ToBeDetermined` aprovado → itera → encontra **nenhuma** → gera 0. Cada fixação
  confirmada depois gera o seu.

`OriginType` é **sempre** `{Purchase,Sales}ContractPriceFixation` e `OriginKey` é **sempre** a
chave da fixação — o que dá uma chave de idempotência única e uniforme (§ Idempotência).

## Escopo desta fase

### Entra

- Cadastro de **conta financeira** (caixa e banco), sem nada de CNAB/boleto.
- Tabela de **documentos financeiros**, com direção (a pagar / a receber) e natureza.
- **Documento provisório** gerado a partir de cada fixação confirmada, **bloqueado para baixa**.
- Ciclo de vida do provisório amarrado ao do contrato e ao da fixação, incluindo estorno,
  cancelamento, encerramento e reabertura.
- **Adiantamento** a contrato, a pagar e a receber, **com liquidação** — adiantamento sem baixa
  não é adiantamento.
- **Baixa e estorno de baixa** por ledger, com juros, multa e desconto. Nesta fase **o único
  documento liquidável é o adiantamento**: o provisório nasce bloqueado e só é liberado na
  Fase 2, quando o documento fiscal o converte em firme. O mecanismo de baixa entra agora
  inteiro porque o adiantamento já precisa dele, e assim a Fase 2 não reabre o ledger.
- **Geração sob demanda** para contratos já aprovados, com simulação (`DryRun`).
- Telas de Contas a Pagar, Contas a Receber, Adiantamentos e Contas Financeiras, num grupo de
  menu novo.

### Não entra

- **Documento firme e abatimento do provisório** — Fase 2, junto do documento fiscal.
- **Retenções** (bruto/retenção/líquido e documento ao órgão) — Fase 2. Só na compra:
  `SalesContract` não tem `Taxes` nem `FunruralType`. Se houver retenção sobre recebível
  (IR/CSLL retido pelo cliente), a Fase 2 precisará de uma `SALES_CONTRACTS_TAXES` **nova** —
  não é reaproveitamento. Confirmar com o negócio **antes** da Fase 2.
- **Amortização do adiantamento** contra o firme — Fase 2.
- **Cadastro estruturado de condição de pagamento e parcelamento** — Fase 2. Nesta fase cada
  fixação gera **um** documento; parcelar só faz sentido junto do firme.
- **Encontro de contas** (netting a receber x a pagar do mesmo parceiro — o cenário de
  barter/troca) e **multimoeda com variação cambial** — Fase 3.
- **Pagamento em lote com aprovação por alçada**, aging, fluxo de caixa projetado e exposição
  por contraparte — Fase 4.
- **Cobrança bancária (boleto/CNAB/PIX), conciliação bancária e contabilidade** (partida
  dobrada, lançamento automático) — fora de todas as fases, por decisão explícita. O módulo
  permanece razão auxiliar puro, do mesmo jeito que o motor de tributação ficou fora do
  roadmap fiscal.
- Nada mais. O log de alterações **entra** nesta fase, por decisão do usuário — ver
  `FINANCIAL_DOCUMENT_CHANGE_LOGS`.

## Decisões de design

### Uma tabela de documentos, com enum de direção

`FINANCIAL_DOCUMENTS` guarda a pagar e a receber na mesma tabela, separadas por
`Direction { Receivable, Payable }`.

**Alternativa descartada:** duas tabelas, como o SAP faz com `OINV`/`OPCH`. O argumento a favor
de duas é real — as colunas de retenção da Fase 2 só existem no lado a pagar. Mas isso já vale
dentro de cada direção, e a casa convive com isso em escala maior (`SALES_INVOICES` tem uma
dúzia de colunas que só a devolução usa). Contra duas tabelas pesam três coisas:

1. **O encontro de contas da Fase 3 cruza as direções.** Com duas tabelas o ledger de baixa
   precisaria de duas FKs nuláveis e todo saldo viraria `UNION`.
2. **Duplicar o ledger é onde a divergência nasce.** O repositório já registrou essa lição no
   próprio código: `PurchaseInvoice` espelha `SalesInvoice` de propósito, "é o que evita a
   divergência que a antiga `CustomerReturn` criou ao virar entidade separada".
3. **Telas separadas não pedem tabelas separadas.** `$filter=Direction eq ...` resolve, e a
   Fase 4 quer as duas direções numa consulta só.

### Natureza como enum, não como tabela

O que difere entre provisório, firme, adiantamento e documento de imposto é **política**, não
**forma**: todos têm parceiro, filial, moeda, valor, vencimento, saldo, ledger de baixa, tela e
aging. Política mora em guard, não em schema. Precedente da casa: `SalesInvoiceType` guarda
Normal e Return na mesma tabela; `StorageTransactionType` guarda ~10 tipos de movimento.

Os quatro valores do enum são **reservados agora**, escrevendo só dois na Fase 1 — porque o
repositório proíbe renumerar (`ShipmentLoadMovementType`: "valor novo entra sempre no fim,
nunca renumerando os existentes"). Valor de enum reservado não é coluna sem leitor.

### O saldo é derivado-e-persistido sobre um ledger

`FINANCIAL_SETTLEMENTS` é o ledger de baixas: uma linha por evento, com `Amount` **assinado**
(baixa positiva, estorno negativo), origem enumerada e **insert-only** — é dinheiro, nunca se
apaga; estorno é linha negativa. `FinancialDocument.SettledAmount` é persistido e **recalculado
sempre por soma do ledger, nunca incrementalmente**, protegido por `RowVersion`.

É literalmente o padrão de `SALES_CONTRACTS_ALLOCATIONS` / `SalesContract.AllocatedVolume` /
`AvaiableVolume`, com o mesmo par estático/instância de `SalesContractsRecalculateBalanceService`.

O ponto que unifica as fases: o **mesmo** `SettledAmount` vai absorver a baixa financeira (F1), o
abatimento do provisório pelo firme (F2, origem `InvoiceOffset`) e o netting (F3). É isso que faz
`OpenAmount` de um provisório significar exatamente "saldo a faturar" sem nenhuma coluna nova.

### O provisório é escrito por um outbox, não por um serviço transacional

Os pontos de gancho **não usam `IUnitOfWork`**:

- `PurchaseContractsApprovalService` e `SalesContractsApprovalService` injetam `AppDbContext`
  e chamam um único `SaveChangesAsync()`, **sem transação explícita**;
- os serviços de aprovação de fixação abrem `context.Database.BeginTransactionAsync()` na mão.

Portanto o gerador é **enqueue-only**: injeta `AppDbContext`, faz `AddAsync(...)` e **nunca**
chama `SaveChangesAsync`. O documento e a mudança de status caem no **mesmo `SaveChanges`** —
atômico sem transação explícita, porque o EF envolve cada `SaveChanges` na sua própria
transação. É o idioma que a casa já usa em `ContractNotificationOutboxService.Register` e
`ShipmentLoadsMovementLogService.Register`.

**Alternativa descartada:** `CommitMode.Deferred`. Seria o padrão certo se os ganchos usassem
`IUnitOfWork` — mas eles não usam, e ali `CommitMode` não teria o que comitar. A vantagem
extra do outbox é que **nenhum dos serviços de contrato precisa ser convertido para
`IUnitOfWork`**: zero mudança estrutural em fluxo que funciona.

`CommitMode` **entra** em `FinancialDocumentsSettleService`, `...ReverseSettlementService` e
`FinancialAdvancesCreateService` — são pontos de entrada de tela **e** serão compostos pelo
pagamento em lote da Fase 4.

### Vencimento é obrigatório, e a origem depende do tipo de contrato

```
DueDate = fixation.FinancialDueDate ?? contract.StandardCashFlowDate;
```

Uma expressão cobre os dois casos, porque a auto-fixação do contrato de preço fixo tem
`FinancialDueDate` nulo e cai no `StandardCashFlowDate`.

**O resultado não pode ser nulo**, e a validação é feita no ponto de geração, **antes de
qualquer escrita**, com mensagem que nomeia o campo certo conforme o tipo do contrato:

- contrato `Fixed` sem `StandardCashFlowDate` → aprovação recusada, pedindo a previsão de
  pagamento;
- contrato `ToBeDetermined` com fixação sem `FinancialDueDate` → confirmação da fixação
  recusada, pedindo o vencimento financeiro.

É uma mudança de comportamento visível: `StandardCashFlowDate` passa a ser obrigatório para
aprovar contrato de preço fixo, e `FinancialDueDate` para confirmar fixação. Deliberado —
documento financeiro sem vencimento não entra em fluxo de caixa nenhum e nasce linha morta.
Contratos antigos sem o campo não são afetados: não há backfill automático (§ Backfill).

### Sem colunas sem leitor

Critério: uma coluna entra na Fase 1 se **(a)** tem leitor agora, **ou (b)** omiti-la força
depois uma migration *destrutiva ou que muda semântica*. Migration puramente aditiva fica de
fora.

| Coluna | F1? | Por quê |
|---|---|---|
| `Currency` | **SIM** | A tela mostra a moeda e o guard de baixa a compara com a da conta. Sem ela, toda agregação escrita na F1 assume BRL implicitamente e a F3 teria de auditar cada uma. `CurrencyType` já existe — custo zero |
| `NetAmount` (com esse **nome**) | **SIM** | É o valor da F1. O **nome** é o que não se muda barato: o frontend inteiro se liga a ele, e nascer `Amount` para virar `NetAmount` na F2 é rename com quebra de binding |
| `OriginType` / `OriginKey` | **SIM** | É a chave de idempotência do índice único filtrado. Sem ela, aprovar duas vezes duplica o documento — o risco nº 1 da feature |
| `Status` persistido | **SIM** | As telas filtram e ordenam por ele **no servidor**. Mesma justificativa de `ShipmentLoad.Status` |
| `RowVersion` | **SIM** | Concorrência real: duas baixas simultâneas passam pelos dois guards e só aqui a segunda falha |
| `GrossAmount`, `WithholdingAmount` | **NÃO** | Retenções são F2. Backfill da F2 é `GrossAmount = NetAmount, WithholdingAmount = 0` — um `UPDATE`, não destrutivo |
| `ExchangeRate`, `AmountLocalCurrency` | **NÃO** | Sem leitor (tudo BRL) e **sem fonte** — não existe tabela de cotação |
| `AppliedAmount` (adiantamento) | **NÃO** | Não há documento firme para amortizar. Recuperável do ledger pelo `Origin` — ver abaixo |
| `InstallmentNumber` | **NÃO** | Sem parcelamento. A F2 amplia o índice único |
| `FinancialAccount.LedgerAccountCode` | **NÃO** | Não há lançamento contábil na F1. Quando entrar, será **VARCHAR sem FK** (dual-mode) |
| `FinancialAccount.CurrentBalance` | **NÃO** | Leitor só na posição de caixa (F4). É a coluna mais tentadora — e a que mais custaria manter sem ninguém ler |

**O adiantamento tem dois eixos e a Fase 1 entrega só um.** Pagar o adiantamento consome o
documento (eixo caixa → `SettledAmount`); amortizá-lo contra o firme consome o *crédito* (eixo
aplicação → `AppliedAmount`). Somar os dois no mesmo campo daria `OpenAmount` negativo. Como não
existe firme na Fase 1, `AppliedAmount` seria coluna sem leitor: fica fora, e
`AvailableAdvanceAmount` é `[NotMapped] = SettledAmount`. A Fase 2 adiciona a coluna e o
recalculador (`Σ Amount WHERE Origin = AdvanceApplication`), e **o backfill sai do próprio
ledger** — o `Origin` já distingue os eixos desde o dia 1, sem perda de informação.

## Modelo de dados

Quatro tabelas novas no `AppDbContext`. DataAnnotations na entidade (não existe nenhuma
`IEntityTypeConfiguration` no projeto); o que anotação não cobre vai inline no `OnModelCreating`.
**Nenhuma FK para cadastro dual-mode** (`CardCode`, `ItemCode`, `LedgerAccountCode`): em
`Erp=SAPB1` as tabelas locais estão vazias e a FK obrigatória vira INNER JOIN que zera a coleção.

### Enums novos (`SiagroB1.Domain/Enums/`)

```
FinancialDirection        { Receivable = 0, Payable = 1 }
FinancialDocumentNature   { Provisional = 0, Firm = 1, Advance = 2, TaxWithholding = 3 }
FinancialDocumentStatus   { Open = 0, PartiallySettled = 1, Settled = 2, Canceled = 3 }
FinancialDocumentOrigin   { Manual = 0, PurchaseContractPriceFixation = 1,
                            SalesContractPriceFixation = 2, PurchaseInvoice = 3,
                            SalesInvoice = 4, FinancialDocument = 5 }
FinancialSettlementOrigin { Manual = 0, Reversal = 1, InvoiceOffset = 2,
                            AdvanceApplication = 3, Netting = 4 }
FinancialAccountType      { Cash = 0, Bank = 1 }
```

A Fase 1 **escreve** apenas `Provisional`/`Advance`, `Manual`/`{Purchase,Sales}ContractPriceFixation`
e `Manual`/`Reversal`. Os demais valores estão reservados.

E `TransactionCode` += `FinancialDocument = 12` (hoje o enum para em `ShipmentLoad = 11`).

### `FINANCIAL_ACCOUNTS` (nova) — `FinancialAccount`

**Sem classe base**, com `[Key] Code` — é o padrão de cadastro simples da casa (`COST_CENTERS`,
`LEDGER_ACCOUNTS`, `TAXES`), e não `MasterEntity`, cujo `Code` é `VARCHAR(50)` fixo.

| Coluna | Tipo | Observação |
|---|---|---|
| `Code` | `[Key] VARCHAR(10) NOT NULL` | digitado pelo usuário, como todo cadastro da casa |
| `Name` | `VARCHAR(100) NOT NULL` | |
| `Type` | int (`FinancialAccountType`) | caixa x banco; decide quais campos a tela exige |
| `BankCode` | `VARCHAR(3)` | código FEBRABAN; nulo em caixa |
| `BankName` | `VARCHAR(100)` | desnormalizado — **não vale criar cadastro de bancos na Fase 1** |
| `BankBranch` | `VARCHAR(10)` | agência com dígito, texto |
| `BankAccountNumber` | `VARCHAR(20)` | conta com dígito, **texto** — nunca numérico, por causa dos zeros à esquerda |
| `Currency` | int (`CurrencyType`) DEFAULT 1 | conta é monomoeda; o guard recusa baixar documento USD em conta BRL |
| `BranchCode` | `VARCHAR(14)` NULL, FK → `BRANCHS`, NoAction | `BRANCHS` é tabela **local** nos dois modos, então FK é segura. Nulo = conta corporativa |
| `Inactive` | bit DEFAULT 0 | mesmo padrão de `CostCenter`/`LedgerAccount` |

Índice: `[Index(nameof(BranchCode))]`.

### `FINANCIAL_DOCUMENTS` (nova) — `FinancialDocument : DocumentEntity`

`DocumentEntity` traz `Key`, `RowId`, os quatro pares de auditoria (preenchidos **manualmente**
pelos serviços), `DocNumberKey`/`DocNumber` e `BranchCode`/`Branch`. `ApprovedAt`/`ApprovedBy`
ficam livres para a alçada da Fase 4.

| Coluna | Tipo | Observação |
|---|---|---|
| `Code` | `VARCHAR(50) NOT NULL` | número do documento, de `DocNumberSequenceService` com `TransactionCode.FinancialDocument` |
| `Direction` | int (`FinancialDirection`) | eixo das duas telas |
| `Nature` | int (`FinancialDocumentNature`) | `Provisional` ou `Advance` na F1 |
| `Status` | int (`FinancialDocumentStatus`) DEFAULT 0 | **persistido-derivado**; escritor único é o recalculador. Exceção: `Canceled`, que só o cancelamento grava — mesmo desenho de `ShipmentLoadsRecalculateInvoicedService` |
| `CardCode` | `VARCHAR(15) NOT NULL` | **sem FK** (dual-mode) |
| `CardName` | `VARCHAR(200)` | desnormalizado pelo serviço via `IBusinessPartnerService` |
| `DocumentDate` | datetime NOT NULL | emissão: data da aprovação ou da fixação |
| `DueDate` | datetime **NOT NULL** | validado no gerador; ver a decisão de vencimento |
| `Currency` | int (`CurrencyType`) DEFAULT 1 | copiado de `contract.StandardCurrency` |
| `NetAmount` | `DECIMAL(18,2) DEFAULT 0` | o valor a pagar/receber |
| `SettledAmount` | `DECIMAL(18,2) DEFAULT 0` | **persistido-derivado**: Σ `FINANCIAL_SETTLEMENTS.Amount` |
| `OriginType` | int (`FinancialDocumentOrigin`) | o que gerou |
| `OriginKey` | uniqueidentifier NULL | PK da linha geradora — a **fixação**, na F1. **Sem FK e sem navegação**: a linha precisa sobreviver ao registro que ela narra, como `ShipmentLoadMovement.SalesInvoiceKey` |
| `OriginDocNumber` | `VARCHAR(50)` | `contract.Code` desnormalizado — a lista mostra o contrato sem join |
| `PurchaseContractKey` | uniqueidentifier NULL | FK opcional + navegação, `NoAction`, `.WithMany()` **sem coleção inversa** — contrato é tabela local, e FK nulável é LEFT JOIN, seguro |
| `SalesContractKey` | uniqueidentifier NULL | idem |
| `PaymentTermsText` | `VARCHAR(1000)` | cópia de `fixation.PaymentDetails ?? contract.PaymentTerms` — ver a decisão de condição de pagamento |
| `Comments` | `VARCHAR(500)` | |
| `CancellationReason` | `VARCHAR(500)` | obrigatório na action de cancelar, como `ShipmentLoad.CancellationReason` |
| `RowVersion` | rowversion | `[Timestamp]` |

`[NotMapped]`, todas derivadas de campo **persistido** — funcionam sob `$select`, sem navegação:

| Propriedade | Fórmula |
|---|---|
| `OpenAmount` | `Round(NetAmount - SettledAmount, 2, ToEven)` |
| `IsBlockedForSettlement` | `Nature == Provisional` — **sem coluna**, para não criar segunda fonte de verdade |
| `IsOverdue` | `DueDate.Date < DateTime.Now.Date && OpenAmount > 0` |
| `AvailableAdvanceAmount` | `Nature == Advance ? SettledAmount : 0` |

Índices por anotação: `Code` único; `(Direction, Status, DueDate)` — a consulta das duas telas;
`CardCode`; `PurchaseContractKey`; `SalesContractKey`.

### Idempotência: o índice que impede a geração dupla

Anotação não cobre índice filtrado, então vai inline no `OnModelCreating`:

```csharp
modelBuilder.Entity<FinancialDocument>()
    .HasIndex(x => new { x.OriginType, x.OriginKey }, "IX_FINANCIAL_DOCUMENTS_ProvisionalOrigin")
    .IsUnique()
    .HasFilter($"[Nature] = {(int)FinancialDocumentNature.Provisional} " +
               $"AND [Status] <> {(int)FinancialDocumentStatus.Canceled} " +
               "AND [OriginKey] IS NOT NULL");
```

O cenário que ele cobre: dois cliques em "Aprovar", ou um retry do frontend — sem trava, dois
provisórios idênticos, e ninguém percebe até o mês fechar com o dobro.

Duas camadas, de propósito: o gerador consulta antes (`AnyAsync`) e devolve mensagem amigável;
o índice é o que sobrevive a um caminho novo que esqueça a consulta. Mesma filosofia de
`IX_SALES_CONTRACTS_ALLOCATIONS_DeliveryDifferenceOwner`.

Filtrado por `Nature = Provisional` porque **adiantamento pode repetir no mesmo contrato** (dois
adiantamentos ao mesmo produtor são legítimos), e por `Status <> Canceled` porque cancelar
precisa **liberar** a origem para regeneração na reabertura — mesmo precedente de
`PurchaseInvoice.ChaveNFe`. Quando a Fase 2 trouxer parcelamento, o índice é *ampliado* com
`InstallmentNumber`: migration barata, não remodelagem.

### `FINANCIAL_SETTLEMENTS` (nova) — `FinancialSettlement : BaseEntity`

| Coluna | Tipo | Observação |
|---|---|---|
| `FinancialDocumentKey` | uniqueidentifier NOT NULL | FK + navegação, NoAction |
| `FinancialAccountCode` | `VARCHAR(10)` NULL | FK → `FINANCIAL_ACCOUNTS`, NoAction. Nulável para as origens não-caixa das Fases 2/3; na F1 o guard a exige |
| `SettlementDate` | datetime NOT NULL | data-caixa **efetiva**, distinta de `CreatedAt` (quando foi digitada) |
| `Amount` | `DECIMAL(18,2) DEFAULT 0` | **assinado**: baixa positiva, estorno negativo |
| `InterestAmount` | `DECIMAL(18,2)` | juros |
| `FineAmount` | `DECIMAL(18,2)` | multa |
| `DiscountAmount` | `DECIMAL(18,2)` | desconto |
| `Origin` | int (`FinancialSettlementOrigin`) | |
| `ReversedSettlementKey` | uniqueidentifier NULL | a linha que esta estorna. **Sem FK**: auto-relação deixaria a convenção do EF ambígua |
| `DocumentReference` | `VARCHAR(50)` | nº do comprovante ou da OP |
| `Notes` | `VARCHAR(500)` | motivo, obrigatório no estorno |

Juros, multa e desconto **não entram** em `Amount`: `Amount` é o que abate o documento; os três
são a composição do valor efetivamente pago ou recebido. Somá-los faria o documento liquidar por
valor diferente do devido.

Índices: `FinancialDocumentKey`, `SettlementDate`, `FinancialAccountCode`; e inline, para impedir
estorno duplo da mesma baixa:

```csharp
.HasIndex(x => x.ReversedSettlementKey).IsUnique()
.HasFilter("[ReversedSettlementKey] IS NOT NULL")
```

### `FINANCIAL_DOCUMENT_CHANGE_LOGS` (nova) — `FinancialDocumentChangeLog`

Cópia estrutural de `SALES_INVOICES_CHANGE_LOGS`: registro campo a campo, respondendo "quem mudou
o quê, quando, e o que estava lá antes". **Sem classe base**, como o log de saída.

| Coluna | Tipo | Observação |
|---|---|---|
| `Key` | uniqueidentifier | `[Key]`, Identity |
| `FinancialDocumentKey` | uniqueidentifier NULL | FK + navegação `ChangeLogs` |
| `ChangedAt` | datetime | default `DateTime.Now` |
| `ChangedBy` | `VARCHAR(100)` | |
| `Field` | `VARCHAR(50) NOT NULL` | **código** do campo, não o rótulo traduzido — a tela resolve por formatter, para não travar o i18n |
| `OldValue` | `VARCHAR(500)` | nulo quando a linha registra inclusão |
| `NewValue` | `VARCHAR(500)` | nulo quando a linha registra remoção |

Os códigos de campo vivem numa classe de constantes própria
(`FinancialDocumentChangeLogFields`), no molde de `ContractChangeLogFields`.

**Ele não duplica os carimbos de ciclo de vida.** Criação, alteração e cancelamento já estão nos
quatro pares de auditoria de `DocumentEntity` e em `CancellationReason`; o dinheiro está no ledger
de baixas, que é insert-only. O log cobre a alteração **de campo** — na Fase 1, `DueDate` (pela
action de correção) e `Comments`; na Fase 2, os campos que o documento firme trouxer. É a mesma
divisão de responsabilidade que o log do documento de saída documenta no próprio XML-doc.

## Regras

1. Toda fixação de preço `Confirmed` gera **um** documento provisório. Contrato `Fixed` tem uma
   auto-fixação desde a criação, então aprová-lo gera 1; contrato `ToBeDetermined` aprovado gera
   0, e cada fixação confirmada depois gera o seu.
2. `Direction` segue o lado do contrato: compra → `Payable`, venda → `Receivable`.
3. `NetAmount = Round(FixationVolume * FixationPrice, 2, ToEven)`. **O arredondamento explícito é
   obrigatório**: preço é `DECIMAL(18,8)` e volume `DECIMAL(18,3)`; sem ele o valor diverge do
   `TotalPrice` que a tela do contrato mostra, que já arredonda para 2.
4. `DueDate = fixation.FinancialDueDate ?? contract.StandardCashFlowDate`, **obrigatório** —
   nulo recusa a operação, antes de qualquer escrita, com mensagem que nomeia o campo certo.
5. Todo provisório nasce `Nature = Provisional`, `Status = Open`, e é bloqueado para baixa por
   `IsBlockedForSettlement`, que é derivado da natureza — não há coluna a manter em sincronia.
6. `Status` é derivado de `SettledAmount` pelo recalculador: zero → `Open`, entre zero e
   `NetAmount` → `PartiallySettled`, igual a `NetAmount` → `Settled`. `Canceled` é gravado só
   pelo cancelamento.
7. **Estorno de fixação confirmada** (`PriceFixationsCancelService`, que leva `Confirmed` de
   volta a `InApproval`) cancela o provisório dela. Reaprovar a fixação gera um documento
   **novo**, com o valor novo — funciona porque o cancelamento marca `Status = Canceled` e o
   índice único ignora cancelados.
8. **Não há gancho em `Reject`.** `PriceFixationsRejectService` exige `InApproval` e nunca vê uma
   fixação com documento; enganchar ali é código morto que deixaria o provisório órfão em
   silêncio. `PriceFixationStatus.Canceled` (= 2) **não é escrito por serviço nenhum** — não
   construa lógica sobre ele.
9. **Cancelar o contrato** cancela todos os provisórios dele. Seguro: os dois serviços de
   cancelamento já recusam contrato com movimento, e provisório nunca tem baixa.
10. **Encerrar o contrato** (`Finished`) cancela o saldo provisório remanescente. Coerente com o
    provisório significar "saldo a faturar": encerrar quer dizer que não haverá mais entrega,
    então não há mais o que faturar. Deixá-lo aberto empilharia na tela um "a faturar" que jamais
    sairia, envenenando o total.
11. **Reabrir o contrato** regenera pelo **mesmo** gerador idempotente, recomputando o valor do
    estado atual das fixações.
12. Cancelar documento com baixa é recusado. Não acontece com provisório (é bloqueado), mas o
    guard cobre o adiantamento.
13. Baixa é recusada em documento bloqueado, cancelado, sem conta financeira, com moeda
    divergente da conta, ou quando excede `OpenAmount`.
14. **Cancelar, nunca apagar.** Apagar o documento no estorno funcionaria e mataria o rastro —
    é a tentação óbvia desta feature; recuse.

Regras próprias do adiantamento:

15. Nasce `Nature = Advance`, `Origin = Manual` e **desbloqueado** — é o único documento que se
    paga nesta fase.
16. Direção segue o lado do contrato, como o provisório. Não há adiantamento sem contrato nesta
    fase, e o contrato precisa estar `Approved`.
17. `NetAmount` e `DueDate` são **informados pelo usuário**; `CardCode`/`CardName`,
    `Currency` e `BranchCode` são copiados do contrato pelo serviço.
18. **Dois adiantamentos no mesmo contrato são legítimos** — o índice único não os barra, porque
    filtra por `Nature = Provisional`.
19. O adiantamento **não abate o provisório** nesta fase.

## Serviços e API

Namespace `SiagroB1.Application/Services/Financials/`. Um serviço por operação, construtor
primário, sem interface, sem MediatR, sem repositório. Validação antes da transação; regra de
negócio como `ApplicationException` com texto pt-BR, que o controller devolve como
`BadRequest(ex.Message)`.

| Serviço | Responsabilidade | Transação |
|---|---|---|
| `FinancialAccountsCreateService` | cria conta, validando código único e campos bancários quando `Type = Bank` | `IUnitOfWork`, `Auto` |
| `FinancialAccountsUpdateService` | atualiza; recusa trocar `Currency` se já houver baixa na conta | `IUnitOfWork` |
| `FinancialAccountsGetService` | leitura `IQueryable` para o controller | — |
| `FinancialAccountsDeleteService` | exclui conta **sem nenhuma baixa**; com baixa, manda inativar | `IUnitOfWork` |
| `FinancialDocumentsGetService` | leitura com `Include(Settlements)`, base das três telas | — |
| **`FinancialDocumentsGenerateService`** | **enfileira** o provisório de UMA fixação confirmada; idempotente por `(OriginType, OriginKey)` | **nenhuma** — enqueue-only |
| `FinancialDocumentsCancelService` | cancela documento com motivo, recusando se `SettledAmount != 0`; expõe `EnqueueCancelByOriginAsync` para os hooks | `Auto` na entrada por Key; enqueue nos hooks |
| **`FinancialDocumentsRecalculateBalanceService`** | **escritor único** de `SettledAmount` e `Status`, sempre por soma do ledger | par estático (sem save) + instância |
| `FinancialDocumentsSettlementGuardService` | regra única de "pode baixar?", estático e sem estado, como `SalesContractsPostApprovalGuard` | — |
| `FinancialDocumentsSettleService` | grava a linha de baixa e dispara o recálculo | `CommitMode` |
| `FinancialDocumentsReverseSettlementService` | grava a linha negativa espelho e recalcula | `CommitMode` |
| `FinancialAdvancesCreateService` | cria o adiantamento amarrado a contrato aprovado | `CommitMode` |
| `FinancialDocumentsSetDueDateService` | corrige `DueDate` sem reabrir o contrato | `Auto` |
| `FinancialDocumentsGenerateBacklogService` | geração deliberada e filtrada para contratos já aprovados, com `DryRun` | `Auto` |
| `FinancialDocumentsGetTotalsService` | totais de cabeçalho das telas | — |
| `FinancialDocumentChangeLogService` | **porta única de escrita do log**: `Register(...)` só enfileira, quem salva é o serviço de mutação que chamou | **nenhuma** — enqueue-only |

### Ganchos nos serviços existentes

| Gatilho | Arquivo | O que fazer |
|---|---|---|
| Aprovação de contrato de compra | `Services/PurchaseContracts/PurchaseContractsApprovalService.cs` | acrescentar `.Include(x => x.PriceFixations)` (hoje é `FirstOrDefaultAsync` puro); depois de `Status = Approved` e **antes** do `SaveChangesAsync`, iterar as fixações `Confirmed` e enfileirar |
| Aprovação de contrato de venda | `Services/SalesContracts/SalesContractsApprovalService.cs` | idêntico |
| Confirmação de fixação de compra | `Services/PurchaseContracts/PurchaseContractsPriceFixationsApprovalService.cs` | depois de `Status = Confirmed`, antes do primeiro `SaveChangesAsync` |
| Confirmação de fixação de venda | `Services/SalesContracts/SalesContractsPriceFixationsApprovalService.cs` | idêntico |
| Estorno de fixação | `PurchaseContractsPriceFixationsCancelService` e o par de venda | `EnqueueCancelByOriginAsync(..., "Fixação estornada")` |
| Cancelamento de contrato | `PurchaseContractsCancelService`, `SalesContractsCancelService` | cancela todos os provisórios do contrato |
| Encerramento de contrato | `PurchaseContractsCloseService`, `SalesContractsCloseService` | cancela o saldo provisório remanescente |
| Reabertura de contrato | `PurchaseContractsReopenService`, `SalesContractsReopenService` | chama o mesmo gerador — idempotente |

Como reconfirmar os alvos, se os nomes mudarem: são os únicos serviços que escrevem
`ContractStatus.Approved` / `PriceFixationStatus.Confirmed` —

```
grep -rn "ContractStatus.Approved;\|PriceFixationStatus.Confirmed;" SiagroB1.Application/Services/
```

### OData

`SiagroB1.Web/ODataConfig/ODataConfigurations.cs`: entity sets `FinancialAccounts`,
`FinancialDocuments`, `FinancialSettlements`, e `AddProperty` para as **quatro** `[NotMapped]`
(`OpenAmount`, `IsBlockedForSettlement`, `IsOverdue`, `AvailableAdvanceAmount`) — sem isso o
`$select` devolve 400.

**Um único entity set serve as três telas** — Contas a Pagar filtra
`Direction eq Payable and Nature ne Advance`, Contas a Receber o espelho, e Adiantamentos
`Nature eq Advance`. Registrar o mesmo tipo CLR sob dois entity sets tornaria o roteamento de
navegação ambíguo no OData v4.

Actions em `SiagroB1.Web/Actions/Financials/`, uma por controller: `FinancialDocumentsSettle`,
`FinancialDocumentsReverseSettlement` (recebe a chave da **baixa**, não a do documento),
`FinancialDocumentsCancel`, `FinancialDocumentsSetDueDate`, `FinancialAdvancesCreate`,
`FinancialDocumentsRecalculateBalance`, `FinancialDocumentsGenerateBacklog`.
Functions: `FinancialDocumentsGetByContract` (aba Financeiro do contrato) e
`FinancialDocumentsGetTotals`.

Regras de EDM, todas com histórico neste repositório:

- **valor monetário sempre `Edm.Double`, nunca `Edm.Decimal`** — o UI5 v4 serializa decimal como
  string e o backend devolve 400 sem nomear o campo;
- **data sempre `string`** em parâmetro de action, como `storageAddressesDailyCalculationJob`;
- **enum sempre `string`** em parâmetro (`"Purchase"` / `"Sales"`, `"Payable"` / `"Receivable"`),
  como `purchaseContractsSetSignatureStatus`. Isso vale só para **parâmetro**: propriedade de
  entidade continua enum e o `$filter` a compara pelo nome qualificado;
- rotas literais declaradas à mão **nas duas formas** — a não declarada toma 404;
- `ODataActionParameters` pode chegar **null**, e parâmetro opcional volta `true` com valor
  `null` no `TryGetValue`. Guardar os dois casos.

Controllers de `FinancialDocuments` e `FinancialSettlements` são **somente leitura**
(`[EnableQuery]` GET); mutação só por action, para que a mutação e o recálculo do saldo caiam no
mesmo `SaveChanges` de um serviço único. `FinancialAccountsController` é CRUD direto no entity
set, como `LogisticRegionsController` — cadastro simples não vira action.

DI: uma `AddScoped` por serviço em `AddApplicationServices()` — não há assembly scanning.

## Frontend

Grupo de menu **novo**, raiz: `MENU_ITEMS` `financial` / "Financeiro" / `ParentKey = null` /
`Order = 7` (raízes ocupados: main 0, admin 1, registers 2, storage 3, purchases 4, sales 5,
reports 6), com linha própria em `ROLE_MENUS` para `ADMIN` — sem ela nem o grupo aparece.
Filhos: `financialAccounts`, `accountsPayable`, `accountsReceivable`, `financialAdvances`.
**A `MENU_ITEMS.Key` tem de ser idêntica ao `name` da rota no `manifest.json`.**

Telas espelhando `purchaseInvoices`: `sap.f.DynamicPage` com FilterBar no header e
`sap.ui.table` no conteúdo; Add/Edit/Detail em `ObjectPageLayout`. Armadilhas obrigatórias:

- filtro como **`$filter` string crua** via `changeParameters` — `sap.ui.model.Filter` estoura
  sobre enum;
- enum em binding sempre com `targetType: 'any'` mais formatter;
- campo monetário **editável** com `sap.ui.model.odata.type.Double`; exibição pode usar `Decimal`;
- toda propriedade editável presente no `create()` inicial, nem que seja `null`;
- data no payload em ISO completo;
- botão "Baixar" oculto quando `IsBlockedForSettlement`, com expression binding **explícito** —
  binding indefinido avalia como `true` e o botão apareceria antes de carregar;
- `super.onBeforeRendering()` se a tela sobrescrever o hook, senão a persistência de largura de
  coluna morre.

Nos formulários de contrato, marcar `StandardCashFlowDate` como obrigatório em contrato de preço
fixo, e `FinancialDueDate` como obrigatório na fixação — o servidor recusa de qualquer jeito, mas
descobrir isso só ao aprovar é experiência ruim.

## Migrations

`AppContext/`:

- `CreateFinancialModule` — as quatro tabelas, com os índices filtrados inline.
- `SeedFinancialDocumentDocNumber` — SQL **idempotente** em `DOC_NUMBERS`
  (`IF NOT EXISTS ... WHERE TransactionCode = 12`), no molde de `SeedShipmentLoadDocNumber`, com
  GUID fixo e `Down` deletando só a linha semeada.

`CommonContext/`:

- `AddFinancialMenus` — grupo e 4 telas em `MENU_ITEMS`, `ROLE_MENUS` para `ADMIN` (inclusive
  para o grupo), GUIDs literais fixos, `Down` apagando `ROLE_MENUS` antes de `MENU_ITEMS`.

### Backfill: não automático, sim sob demanda

**Não** na migration. Ela roda no deploy e criaria em silêncio milhares de documentos, com
`DueDate` majoritariamente nulo (o campo é opcional e contratos antigos não o têm) e incluindo
contratos **já entregues e pagos fora do sistema** — o módulo estrearia com um backlog falso e
impagável, com risco real de pagamento em duplicidade. O precedente da casa não cobre este caso:
`BackfillWarehouseComplementIsOwn` e `BackfillShipmentLoadStatusPlanned` **preenchem coluna de
linha que já existia**; criar documento financeiro não é conserto de dado, é evento de negócio.
E a numeração via Dapper num laço de migration queimaria milhares de números sob `UPDLOCK`.

**Sim** por `FinancialDocumentsGenerateBacklogService` + action, com filtro explícito (filial,
faixa de data, só `Status == Approved`, só com vencimento resolvível) e **`DryRun`** devolvendo o
que *seria* criado sem gravar nada. Idempotente pelo mesmo índice único. O financeiro roda por
filial, confere o dry-run, e só então efetiva. A migration entrega apenas o schema vazio e o seed.

## Testes (`SiagroB1.Application.Tests/Financials/`)

xUnit e EF InMemory, `TestDb.CreateUnitOfWork()`, dublês `Fake*` escritos à mão.

- contrato `Fixed` aprovado gera 1 provisório, com `OriginKey` = chave da auto-fixação, valor
  `TotalVolume * StandardPrice` arredondado a 2 e vencimento de `StandardCashFlowDate`;
- contrato `ToBeDetermined` aprovado gera 0;
- fixação confirmada gera 1, com vencimento de `FinancialDueDate`; a segunda gera outro sem tocar
  no primeiro;
- geração repetida da mesma origem não duplica;
- contrato `Fixed` sem `StandardCashFlowDate` é recusado com mensagem que nomeia o campo;
- fixação sem `FinancialDueDate` é recusada com mensagem que nomeia o campo;
- estorno de fixação cancela o provisório; reaprovar gera um **novo**, não ressuscita o antigo;
- cancelar e encerrar contrato cancelam os provisórios; reabrir regenera;
- baixa em provisório é recusada; baixa acima do saldo é recusada; baixa em conta de moeda
  divergente é recusada;
- `SettledAmount` bate com a soma do ledger depois de baixa parcial, baixa total e estorno, e
  `Status` deriva corretamente nos três pontos;
- dois adiantamentos no mesmo contrato são aceitos;
- corrigir o vencimento grava **uma** linha de log com o valor antigo e o novo, e a correção e o
  log caem no mesmo `SaveChanges`;
- `FinancialEdmModelTests`: entity sets no EDM real, as quatro `[NotMapped]` presentes,
  parâmetros monetários em `Edm.Double`, nenhum parâmetro de enum.

## Riscos e armadilhas

### `FinishedContractMutationGuardInterceptor` — **não estenda**

`SiagroB1.Infra/Interceptors/FinishedContractMutationGuardInterceptor.cs` derruba qualquer
`SaveChanges` que toque `PurchaseContractBroker`, `PurchaseContractTax`,
`PurchaseContractQualityParameter` ou `PurchaseContractPriceFixation` de contrato `Finished`.

**Não adicione `FinancialDocument` ao `switch` de `CollectContractKeys`.** Quem "seguir o padrão"
vai adicionar — e aí o encerramento do contrato (que cancela o provisório no **mesmo**
`SaveChanges` em que grava `Finished`) e qualquer baixa de documento de contrato encerrado passam
a lançar "Contrato encerrado: não é possível alterar dados vinculados ao contrato.", sem saída
pela tela. Vale um comentário explícito no interceptor.

### Numeração fora da transação

`DocNumberSequenceService` roda `UPDATE DOC_NUMBERS WITH (UPDLOCK, HOLDLOCK)` via Dapper numa
`SqlConnection` **própria**, que não participa da transação do EF. Duas consequências ao
chamá-lo de dentro da aprovação de contrato:

- se a aprovação falhar, o número já foi consumido → **gap na numeração**. Convivível (todo
  documento do sistema se comporta assim) mas precisa estar escrito;
- o `UPDLOCK` fica com uma conexão fora da transação do EF: uma aprovação lenta segura o lock e
  **serializa a criação de todo documento do sistema**. Mitigação: buscar o número o mais tarde
  possível, imediatamente antes do `AddAsync`, e nunca dentro de laço — o backlog busca N números
  numa passada ou aceita N chamadas curtas fora de transação longa.

### Reabertura de contrato na Fase 2

Contrato reaberto que já emitiu documentos **firmes** regeneraria o provisório pelo valor cheio,
contando duas vezes. O gerador da Fase 2 terá de calcular
`NetAmount = valorDaFixação − Σ firmes já emitidos contra essa origem`. Na Fase 1 essa soma é
sempre 0 e a consulta seria código morto — fica registrada no XML-doc do serviço, não no código.

### Modo SAPB1 é o ambiente de teste local

`yktb` aponta para `Yokotobi-Development`, que roda em **SAPB1** com `BUSINESS_PARTNERS` vazia.
Qualquer FK ou `Include` para parceiro zera a lista inteira, e o sintoma é "a tela não tem
linhas", não um erro.

### Outras

- **Recálculo sempre por soma do ledger, nunca incremental**, sob `RowVersion`.
- Ao declarar as FKs opcionais de contrato, `.HasOne(...).WithMany()` **sem coleção inversa** —
  uma coleção a mais no contrato entraria no EDM do OData sem ninguém pedir.
- A varredura global põe tudo em `NoAction`, e as duas FKs de contrato **ficam com `NoAction`**,
  declaradas inline apenas porque a convenção do EF emparelharia errado duas navegações para
  entidades diferentes. Esta linha dizia `Restrict` numa versão anterior e estava errada: embora
  `Restrict` e `NoAction` emitam o MESMO DDL no SQL Server (`ON DELETE NO ACTION`), são valores
  diferentes do enum `DeleteBehavior` e o EF os registra no snapshot do modelo — trocar um pelo
  outro faz `has-pending-model-changes` acusar mudança e exigiria uma migration que não altera nada
  no banco. Verificado na execução, alternando os dois sentidos.

## Verificação

Gates de código — necessários, nunca suficientes: neste projeto build verde já conviveu com
Detail nascendo em branco.

```
dotnet build SiagroB1.sln
dotnet test SiagroB1.Application.Tests
dotnet ef migrations has-pending-model-changes --context AppDbContext \
  --project SiagroB1.Migrations --startup-project SiagroB1.Web
yarn ts-typecheck && yarn lint     # no frontend, com o dev server parado
```

`yarn test` do frontend não passa neste repo (gate de cobertura de 50% contra ~2,4% reais) e não
é regressão desta feature.

Roteiro no navegador, que é o que vale:

1. Web e Gateway no profile `yktb`, frontend com `yarn start:dev`. Login `admin` / `1234`.
2. Financeiro → Contas Financeiras: cadastrar um caixa e um banco.
3. Contrato de compra de preço fixo **sem** previsão de pagamento: aprovar deve ser recusado com
   mensagem nomeando o campo. Preencher e aprovar: 1 documento a pagar, `Nature = Provisional`,
   `Code` numerado, `CardName` preenchido, `OriginKey` = chave da auto-fixação, botão Baixar
   indisponível.
4. Aprovar duas vezes (dois cliques): erro de negócio, **não** duplicata.
5. Contrato de venda: espelho, documento a receber.
6. Contrato a fixar: aprovar → 0 documentos. Confirmar 2 fixações → 2 documentos, com os
   vencimentos das fixações. Estornar uma → documento `Canceled`; reaprovar → documento **novo**.
7. Encerrar um contrato → provisórios `Canceled`, **sem** disparar "Contrato encerrado…".
   Reabrir → regenerados.
8. Adiantamento a pagar contra um contrato: criar, liquidar por uma conta financeira, ver o saldo
   ir a zero, estornar e ver o saldo voltar. Estornar o mesmo estorno → recusado.
9. Dois adiantamentos no mesmo contrato → ambos aceitos.
10. Derrubar a stack ao terminar, **matando por PID** nas portas 50000/5246/8080 — parar a task
    não basta, o file watcher já ressuscitou os três processos neste projeto.

## O que NÃO entra nesta fase

- Documento firme, abatimento do provisório e retenções — Fase 2.
- Amortização de adiantamento contra o firme (`AppliedAmount`) — Fase 2.
- Condição de pagamento estruturada e parcelamento — Fase 2.
- Encontro de contas e multimoeda com variação cambial — Fase 3.
- Pagamento em lote com alçada, aging, fluxo de caixa e exposição — Fase 4.
- Cobrança bancária, conciliação bancária e contabilidade — fora do roadmap.

## Sequência

1. Enums novos, `TransactionCode.FinancialDocument = 12`, as quatro entidades, `DbSet`s e os
   índices filtrados inline. Migration `CreateFinancialModule`.
2. Migration idempotente de seed do `DOC_NUMBERS`.
3. Cadastro de conta financeira ponta a ponta.
4. Recalculador de saldo, serviço de leitura, entity sets e `AddProperty` das quatro derivadas.
5. Gerador enqueue-only e cancelador, **sem gancho ainda**, com os testes da regra pura de valor
   e vencimento.
6. Ganchos de geração nos quatro serviços de aprovação.
7. Ganchos de desfazimento: estorno, cancelamento, encerramento e reabertura, mais o comentário
   no interceptor.
8. Baixa e estorno: guard, serviços, actions e controllers.
9. Adiantamento.
10. Totais, consulta por contrato, correção de vencimento e as linhas de menu.
11. Geração sob demanda com `DryRun`.
