# Documento de Saída: confirmar e depois transmitir a NF-e + "Tipo de documento" — design

Data: 06/10/2026. Branch `feature/sales-invoice-confirm-then-transmit` nos dois repos, criado de `main` (que já
contém a cadeia da NF-e STANDALONE: emissão, cancelamento e CC-e).
Base: spec `2026-10-02-nfe-standalone-issuance-design.md` (regra "emitir é o que confirma"), spec
`2026-10-05-third-party-entry-tax-and-key-validation-design.md` (D2: `TaxDocumentKind` no Documento de Entrada).

Este spec é a **peça 1 de 3** de um trabalho maior, decomposto na conversa de 06/10:

| # | Peça | Situação |
|---|---|---|
| 1 | Documento de saída nasce confirmado + "Transmitir NF-e" + "Tipo de documento" | **este spec** |
| 2 | "Expedir" e "Faturar" dentro do detalhe da carga (simplificação para a CEAGUI) | ciclo próprio |
| 3 | Saída de estoque próprio na expedição (sem perna de compra) | ciclo próprio |

## 1. O problema

O fluxo de venda com controle dos dois contratos é: liberação de embarque (compra) e liberação de entrega (venda)
→ carga → Expedição de Grãos (o par `Purchase(8)`/`SalesShipment(7)` baixa o **contrato de compra**) → vincular à
carga → Faturamento da Expedição (`ShipmentBillingCreateSalesInvoiceService`, baixa o **contrato de venda** no
ledger `SALES_CONTRACTS_ALLOCATIONS`) → NF-e.

Na filial que emite NF-e pelo Siagro (CEAGUI), o último passo não fecha:

- O faturamento grava o documento já `Confirmed` (`ShipmentBillingCreateSalesInvoiceService`, linha do
  `InvoiceStatus = Confirmed`) sem passar pelo `SalesInvoicesConfirmService`.
- A emissão recusa o que não estiver Pendente: "Só documento Pendente pode ser emitido."
  (`NfeIssueServiceBase.EnsurePreconditionsAsync`).
- Resultado: a nota da carga fica confirmada, com o contrato baixado, **sem caminho para a NF-e**.

Além disso, nem todo documento de saída da CEAGUI é NF-e: o mesmo documento precisa poder terminar sem emissão
(nota de papel/talão etc.), como já acontece no Documento de Entrada com o campo "Tipo de documento".

## 2. Decisões (do usuário, em 06/10)

| # | Decisão | Motivo |
|---|---|---|
| D1 | **O documento de saída Normal nasce/termina confirmado, e a NF-e é transmitida depois**, por um botão próprio ("Transmitir NF-e"). Vale para o documento de carga **e** o avulso. | O caminhão já saiu e já pesou: a baixa acontece no faturamento (mesmo princípio do `<remarks>` do faturamento). Uma regra só por documento. |
| D2 | **NF-e autorizada não pode ser estornada.** | Já é a regra (`SalesInvoicesReverseConfirmService`): `Processing`/`Authorized`/`Cancelled` recusam o estorno. Nada muda. |
| D3 | **"Tipo de documento" (`TaxDocumentKind`: `Nfe` / `Other`) também no documento de saída.** | Nem todo documento de saída é NF-e. |
| D4 | **Só na filial que emite NF-e pelo Siagro** (`TaxCalculationGate`). Yokotobi (SAPB1) e MH Agro não percebem mudança. | A mudança não alcança quem não pediu. |
| D5 | **Data diferente de hoje: a transmissão recusa** (regra atual mantida). O operador estorna, ajusta a data, confirma e transmite. | Mais seguro fiscalmente: a data do documento é a da NF-e. |

Decisões técnicas aceitas com o desenho:

| # | Decisão |
|---|---|
| T1 | A **devolução própria** (`IsNfeReturn`, nascida do "Devolver") **continua confirmando pela autorização**, como hoje. Ela não tem "caminhão já saído" e a regra foi provada em homologação. |
| T2 | O **Documento de Entrada** (entrada própria e devolução de compra) **não muda**: continua "emitir é o que confirma". |
| T3 | O retorno da SEFAZ **confirma só o documento que ainda está Pendente**. Isso cobre a devolução própria e o documento Normal antigo que tenha ficado Pendente com NF-e autorizada ("Concluir confirmação"), sem flag por tipo. |
| T4 | Sem código novo para rejeição/denegação: os caminhos existentes (estorno, cancelamento) bastam — ver §5. |

## 3. Modelo

`SalesInvoice.TaxDocumentKind` (enum existente `TaxDocumentKind`, `Nfe = 0`, `Other = 1`), padrão `Nfe`.

- Migration aditiva `AddSalesInvoiceTaxDocumentKind`: coluna `INT NOT NULL DEFAULT 0`. Documentos existentes ficam
  `Nfe`; nas filiais sem a regra o valor é ignorado.
- Só tem efeito com a regra ativa e em documento **Normal**. A devolução própria é sempre `Nfe` (o
  `SalesInvoicesNfeReturnCreateService` grava `Nfe`); a devolução de cliente não usa o campo.
- Entra na trava da NF-e (`SalesInvoiceNfeLock`): congelado com a NF-e `Processing`/`Authorized`/`Cancelled`.
- Editável só com o documento Pendente (o formulário já só edita Pendente).
- Registrado no EDM como as demais propriedades de enum do documento (PATCH pelo NOME, `"TaxDocumentKind":"Other"`).

## 4. Regras de serviço

### 4.1 Faturamento da carga (`ShipmentBillingCreateSalesInvoiceService`)

**Não muda o fluxo.** Continua criando, confirmando, alocando no ledger e recalculando liberação e carga numa
transação. A única mudança é o documento chegar com o `TaxDocumentKind` escolhido no diálogo (o payload já é a
entidade `SalesInvoice`).

### 4.2 Confirmação (`SalesInvoicesConfirmService`)

A trava "Na filial que emite NF-e pelo Siagro, confirme emitindo a NF-e." passa a valer **só para a devolução
própria** (`IsNfeReturn`). O documento Normal confirma pelo Confirmar em qualquer filial, com o comportamento de
hoje (romaneios `Invoiced`, ledger, liberação, carga, ajuste fiscal no avulso).

### 4.3 Transmissão (`NfeIssueServiceBase` + `SalesInvoicesNfeIssueService`)

O status exigido deixa de ser fixo na base e passa a ser decidido pelo documento — novo membro na base (por
exemplo `protected virtual InvoiceStatus RequiredStatus(TDocument document) => InvoiceStatus.Pending;`), usado no
lugar do `!= Pending` de `EnsurePreconditionsAsync`, com a mensagem montada a partir dele.

| Documento | Status exigido | Outras condições novas |
|---|---|---|
| Saída Normal | `Confirmed` | `TaxDocumentKind == Nfe`, senão "Documento do tipo Outro não é transmitido como NF-e." |
| Saída devolução própria (`IsNfeReturn`) | `Pending` | — |
| Entrada própria / devolução de compra | `Pending` | — (inalterado) |

O resto da emissão não muda: trava de concorrência, reserva do número, data = hoje em Brasília (D5), prontidão
(`NfeReadinessValidator`, recusa antes da SEFAZ), assinatura, "Em processamento" salvo antes do envio, 539 e
duplicidade pelo digest. A reentrada depois de uma rejeição reaproveita número e cNF, como hoje.

### 4.4 Retorno da SEFAZ (`NfeResultHandlerBase`)

Autorizada: grava o procNFe e a situação e salva (inalterado). Em seguida **só chama a confirmação se o documento
ainda estiver `Pending`** (T3). O documento de saída Normal, já `Confirmed`, não é confirmado de novo — o que
duplicaria a alocação no ledger. Vale igual para emissão e "Consultar situação". `NfeConfirmationError` e
"Concluir confirmação" continuam existindo para o documento Pendente.

### 4.5 Estorno e cancelamento

Sem mudança de código:

- Estorno de confirmação já recusa `Processing`/`Authorized`/`Cancelled` (D2) e aceita `None`/`Rejected`/`Denied`.
- Cancelamento do documento já recusa `Processing`/`Authorized` e aceita `Denied`, devolvendo saldo ao contrato,
  à liberação e à carga.
- Cancelamento da NF-e autorizada (2b) já cancela o documento confirmado e devolve o saldo.

## 5. Casos de erro (operação)

| Situação | Caminho |
|---|---|
| Rejeitada pela SEFAZ | Estornar confirmação → corrigir → Confirmar → Transmitir (mesmo número). |
| Prontidão recusada (cadastro, pesos) | Mesmo caminho; nada foi enviado. |
| Data ≠ hoje | Estornar → ajustar a data → Confirmar → Transmitir (D5). |
| Denegada | Número consumido. Cancelar o documento (devolve os saldos) e faturar de novo. |
| Autorizada com erro | Cancelar a NF-e (2b) ou devolução. Estorno recusado (D2). |
| Sem resposta / em processamento | "Consultar situação" (inalterado); o estorno é recusado enquanto `Processing`. |

## 6. Frontend

Só na filial com a regra ativa (`ui>/taxLocked`); fora dela nada aparece nem muda.

- **Documento de saída** (`salesInvoices`): campo **"Tipo de documento"** (`Select` NF-e / Outro) no formulário,
  visível para documento Normal, editável com o documento Pendente — mesmo padrão do `purchaseInvoices/Form`.
- **Detail, botões:**
  - **Confirmar** volta a aparecer para o documento Normal na filial com a regra (continua oculto para a
    devolução própria).
  - **"Emitir NF-e" vira "Transmitir NF-e"** para o documento Normal: visível com tipo NF-e, habilitado com
    `Confirmed` e `NfeStatus` `None`/`Rejected`. Para a devolução própria o botão continua como hoje (Pendente).
  - Tipo Outro: nenhum botão de NF-e.
- **Diálogo de faturamento da carga** (`shipmentBilling/Billing.fragment`): campo **"Tipo de documento"**
  (padrão NF-e), enviado no payload.

## 7. Dados existentes

Os 146 documentos importados do EfisCloud no `CEAGUI_SIAGRO_DEV` estão Pendentes e passam a ser `Nfe` pela
migration. Pela regra nova, cada um é confirmado antes de transmitir. Nada quebra.

## 8. Testes e verificação

Testes (`SiagroB1.Application.Tests`, xUnit + EF InMemory, RED visto antes de cada GREEN):

- Confirmação: Normal confirma na filial com a regra; devolução própria continua recusada sem autorização.
- Transmissão: recusa Normal Pendente; recusa Normal `Other`; aceita Normal Confirmado `Nfe`; devolução própria
  continua exigindo Pendente; Documento de Entrada inalterado.
- Retorno: Normal Confirmado autorizado não chama a confirmação e não duplica o ledger (emissão e consulta);
  documento Pendente autorizado continua confirmando.
- Trava: `TaxDocumentKind` congelado com NF-e autorizada.
- Faturamento da carga: grava o tipo recebido.

Gates: `dotnet build` limpo, suítes Application e Fiscal verdes, `npm run ts-typecheck` e `npm run lint` limpos.

Verificação no navegador (`ceagui` / `CEAGUI_SIAGRO_DEV`, NF-e em homologação):

1. Carga → Faturar com tipo NF-e → documento Confirmado (contrato e liberação baixados) → Transmitir → autorizada
   → estorno recusado; ledger com **uma** linha por item.
2. Documento tipo Outro → Confirmar → nenhum botão de NF-e.
3. Documento confirmado com data de ontem → Transmitir recusa.
4. Devolução própria continua emitindo e confirmando pela autorização.

## 9. Fora de escopo

- Peças 2 e 3 (Expedir/Faturar na carga; saída de estoque próprio).
- Mudar o Documento de Entrada (T2).
- Transmitir em lote, e-mail da NF-e, inutilização.
