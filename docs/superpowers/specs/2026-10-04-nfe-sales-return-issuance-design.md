# NF-e STANDALONE — NF-e de devolução de venda (entrada, finalidade 4)

**Data:** 2026-10-04
**Branch:** `feature/nfe-sales-return-issuance` (backend e frontend), criado a partir de
`feature/nfe-standalone-issuance` (sub-projeto 2a, ainda sem merge).
**Depende de:** `2026-10-01-nfe-standalone-taxation-design.md` (sub-projeto 1: regra de ativação, natureza
fiscal com tipo Entrada/Saída, fotografia dos tributos na linha) e `2026-10-02-nfe-standalone-issuance-design.md`
(sub-projeto 2a: montador, assinatura, transmissão, numeração, "emitir é o que confirma").

## 1. O pedido

"Emitir NF-e de devolução de uma NF-e de venda." Quem pede é a **CEAGUI**, que está trocando o EfisCloud pelo
Siagro. Hoje, quando uma carga vendida volta, a CEAGUI emite no EfisCloud uma **NF-e de entrada própria**
referenciando a venda.

**O que os dados reais mostram** (snapshot do EfisCloud em `C:\Projetos\SiagroB1\dados-ceagui-efiscloud\`, 10
notas autorizadas em produção entre 23 e 30/09/2026):

| Campo | Valor nas notas reais |
|---|---|
| `tpNF` / `finNFe` | 0 (entrada) / 4 (devolução) |
| CFOP | 1202 (todas dentro do estado) |
| `NFref/refNFe` | chave da venda — mas digitada à mão (ver abaixo) |
| Destinatário | o cliente da venda (contribuinte, com IE) |
| ICMS | CST 51 + cBenef `SP053521`, igual à venda |
| PIS/COFINS | CST 72 (a venda saiu com 09) |
| IBS/CBS | CST 200, cClassTrib 200036, igual à venda |
| `pag` | `tPag 90`, `vPag 0` |
| Quantidade e preço | **sempre iguais aos da venda** (devolução total da carga) |
| `infCpl` | "NOTA DE ENTRADA REF A NF108 DO DIA 21/09" |

A referência digitada à mão erra: as notas 160–163 dizem no texto "NF 114/115/118/119" e apontam no
`refNFe` a NF 108 (a 162 devolve 40.400 kg contra uma venda de 40.300), e as notas 147 e 148 saíram **sem**
`refNFe`. Gerar a devolução a partir do documento de venda elimina essa classe de erro.

**Regra nova da SEFAZ (verificada):** a NT 2025.002-RTC criou a regra **VC02-14** — cada item da NF-e de
devolução precisa referenciar o item da nota original pelo grupo `det/DFeReferenciado` (`chaveAcesso` +
`nItem`), sob pena de rejeição 321. Vale em **homologação desde 01/07/2026** e, pela v1.52 da NT (01/10/2026),
em **produção a partir de 03/11/2026**. O XSD do repositório (`SiagroB1.Fiscal/Schemas/leiauteNFe_v4.00.xsd`,
linha 5294) e a Zeus `2026.9.24.1416` (`NFe.Classes`) já têm o grupo. As devoluções do EfisCloud de setembro
não o levam (eram anteriores à data de produção).

**A referência vai num nível só:** a devolução carrega a referência **apenas no item** (`DFeReferenciado`) e
**não leva `NFref` no cabeçalho** — a SEFAZ rejeita os dois níveis juntos (rejeição **1010**, "NF-e com
referenciamento de documento a nível de nota e a nível de item", vista em homologação em 04/10/2026).

Outras regras da SEFAZ que o desenho respeita: rejeição **871** (`finNFe` 3 ou 4 exige `tPag 90` com `vPag 0`)
e rejeição **321** (devolução sem documento referenciado).

## 2. Decisões do usuário (fechadas no brainstorming)

| # | Decisão |
|---|---|
| D1 | **Origem: só venda emitida pelo Siagro.** A devolução nasce de um documento de saída com NF-e autorizada pelo Siagro; a chave vem dele, sem digitação. Venda emitida no EfisCloud antes da virada continua sendo devolvida lá. |
| D2 | **Total ou parcial.** A tela traz o saldo de cada item já preenchido; o usuário pode diminuir. Uma venda aceita várias devoluções parciais até esgotar o vendido. |
| D3 | **Natureza de devolução vinculada à natureza de venda.** A natureza de saída ganha o campo "Natureza de devolução" (ex.: 2 VENDA SUSPENSÃO → 5 ENTRADA DEVOLUÇÃO); cada linha da devolução herda sozinha a natureza certa. Sem vínculo, a devolução é recusada com mensagem. |
| D4 | **Impostos: abordagem A — calcular e conferir.** A linha da devolução passa pelo mesmo motor da venda com a natureza de entrada; IBS/CBS pelas alíquotas da **data da venda**; uma conferência recusa a devolução se o resultado não reproduzir a tributação da linha vendida. (Rejeitada: B — copiar o imposto da venda proporcionalmente; segundo caminho de cálculo e arredondamento na parcial.) |
| D5 | **Fluxo (parte 1 aprovada):** "Devolver" no detalhe da venda → devolução **Pendente** → "Emitir NF-e" → autorizada ⇒ confirma. Mesma regra do 2a: na filial que emite NF-e, a devolução não confirma sem NF-e. |
| D6 | **XML (parte 2 aprovada):** `tpNF 0`, `finNFe 4`, `DFeReferenciado` por item (referência só no item, sem `NFref` — rejeição 1010), `tPag 90`, sem `cobr`; o `nItem` da venda passa a ser gravado na emissão. |
| D7 | **Telas, testes e verificação (parte 3 aprovada)**, incluindo a emissão em homologação pela tela com dado real, só com pedido explícito do usuário na hora. |

## 3. Escopo

**Entra:** vínculo natureza de saída → natureza de devolução; criação da devolução a partir de uma venda
autorizada pelo Siagro (total ou parcial); cálculo e conferência dos tributos da devolução; montagem, assinatura,
validação no XSD, transmissão, consulta, DANFE e XML da NF-e de devolução (reaproveitando o 2a); gravação do
`nItem` de cada linha na emissão; travas de confirmação e de digitação manual de número.

**Não entra:**
- devolução de venda emitida fora do Siagro (chave digitada);
- devolução, com NF-e, de documento que tem **romaneio ou carga** (o "Retornar" e a "Recusa de carga"); numa
  filial que emite NF-e esses dois caminhos passam a recusar documento com NF-e autorizada pelo Siagro (§9.4);
- cancelamento, CC-e e inutilização da devolução (sub-projeto 2b, junto com os da venda);
- devolução emitida pelo próprio cliente (fluxo existente "Devolução do cliente", só conciliação);
- devolução interestadual com DIFAL/ST, exterior, complemento (`finNFe` 2).

## 4. Isolamento

Tudo abaixo só vale com a **regra ativa** do sub-projeto 1 (`TaxCalculationGate.IsActiveAsync`: `Erp ==
STANDALONE` **e** `Branch.IssuesNfe`). Yokotobi (SAPB1) e MH Agro (chave desligada) não veem botão novo, campo
novo de natureza nem regra nova. O campo "Natureza de devolução" é de cadastro STANDALONE: na MH Agro ele aparece no formulário, mas nada o lê enquanto a chave "Emite NF-e" estiver desligada.
O "Retornar" e a "Recusa de carga" só mudam na filial com a regra ativa e origem com NF-e autorizada (§9.4).

## 5. Cadastro: natureza de devolução

**`USAGES.ReturnUsageCode`** — `INT NULL`, FK para `USAGES(Code)` (`OnDelete NoAction`). Propriedade
`Usage.ReturnUsageCode` + navegação `ReturnUsage`; `UsageModel.ReturnUsageCode` (nulo no SAPB1, como os campos
fiscais do sub-projeto 1).

Validações em `UsageService` (create e update, junto de `ValidateDirectionRules`), mensagens em pt-BR:
- só natureza de **Saída** pode ter natureza de devolução;
- a natureza apontada existe, é de **Entrada** e não está inativa;
- não aponta para si mesma;
- uma natureza de Entrada usada como devolução de outra não pode virar Saída (mesma família da trava de
  `UsageService.cs:135-141`).

## 6. Criar a devolução

**Serviço novo** `SalesInvoicesNfeReturnCreateService` (pasta `Services/Nfe/`), action
`POST odata/SalesInvoicesCreateNfeReturn` com `{ Key, Items: [{ OriginItemKey, Quantity }], Reason }`, devolve a
chave da devolução criada. Função de apoio `GET odata/SalesInvoicesNfeReturnableItems(Key=…)`: por item da
venda, `OriginItemKey`, `ItemCode`, `ItemName`, `SoldQuantity`, `ReturnedQuantity` (devoluções não canceladas,
Pendentes inclusive) e `Returnable`.

**Toda a validação antes de qualquer escrita** (`DefaultException`, pt-BR):
1. a filial da venda emite NF-e pelo Siagro (regra ativa);
2. a venda é `Normal`, `Confirmed`, `NfeStatus = Authorized` e tem `ChaveNFe` com 44 dígitos;
3. a venda não tem carga (`ShipmentLoadKey`) nem romaneio (`SalesTransactions`) — "Documento com
   romaneio/carga: a devolução com NF-e ainda não é suportada.";
4. motivo informado;
5. ao menos um item com quantidade > 0, e cada quantidade ≤ saldo devolvível do item (vendido − devolvido em
   devoluções não canceladas);
6. cada linha vendida tem natureza com `ReturnUsageCode` — "A natureza {Code} {Name} da venda não tem natureza
   de devolução cadastrada.";
7. cada linha vendida tem o `nItem` da NF-e de venda (§8.2).

**Montagem:** `SalesInvoiceReturnFactory.CreateFrom(origin, user, quantities)` (já existe, já deriva o peso do
cabeçalho), e então:
- `InvoiceDate` = hoje (Brasília); `IsNfeReturn = true` (§9.1); `PaymentConditionCode = null`;
- `UsageCode` de cada linha = `ReturnUsageCode` da natureza da linha vendida;
- `Comments` = "Devolução da NF-e {nNF} série {serie} (doc.saída {InvoiceNumber}). Motivo: {Reason}";
- transporte e volume copiados da venda pela fábrica (editáveis enquanto Pendente).

Grava pelo `SalesInvoicesCreateService` (que calcula os tributos, §7) numa transação; **não confirma**. A
origem não muda nada na criação — a quantidade devolvida, a entrega e o status `Returned` nascem na confirmação,
como em toda devolução (`SalesInvoicesConfirmService`, ramo Return).

## 7. Cálculo e conferência

`SalesInvoicesTaxApplyService` passa a atender a **devolução própria** (`InvoiceType = Return` **e**
`IsNfeReturn`), além do documento Normal:
- `IsActiveForAsync`: Normal (como hoje) **ou** devolução própria, sempre com a regra ativa;
- natureza: Normal exige **Saída** (mensagem de hoje); devolução própria exige **Entrada** — "A natureza {Name}
  é de saída e não pode ser usada na devolução.";
- CFOP: devolução própria lê `CfopIncomingInState` / `CfopIncomingOutState` (hoje `ResolveCfop` só lê os de
  saída);
- ICMS e PIS/COFINS: mesmas regras da natureza (`ResolveIcmsRule`, `ResolvePisCofinsRule`);
- IBS/CBS: `ResolveIbsCbsAsync(usage, data)` com a **`InvoiceDate` da venda**;
- base: `item.Total` (quantidade devolvida × preço da venda).

**Conferência** (devolução própria, depois do cálculo de cada linha), contra a linha vendida
(`SalesInvoiceItemOrigin`): `CstIcms`, `IcmsRate`, `IcmsBaseReduction`, `IcmsDeferral`, `IcmsBenefitCode`,
`IbsCbsCst`, `IbsCbsClassCode`, `CbsRate`, `CbsRateReduction`, `IbsStateRate`, `IbsMunicipalRate`,
`IbsRateReduction`. Qualquer divergência ⇒ `DefaultException`:
"Item {ItemCode}: a natureza de devolução {Name} não reproduz a tributação da venda — {campo}: venda {x},
devolução {y}." PIS/COFINS ficam fora da conferência: na entrada o CST é outro (CEAGUI: 72 contra 09).

**Travas da devolução própria** (`SalesInvoicesItemsUpdateService` / `SalesInvoicesUpdateService`):
- linha: só `Quantity` é editável (com a validação de saldo do §6.5); `ItemCode`, `UnitPrice`, `UsageCode` e
  os campos fiscais travados voltam ao valor gravado (o mesmo "sobrescreve, não recusa" de `RestoreLocked`);
- cabeçalho: `CardCode`, `BranchCode`, `InvoiceType` e `IsNfeReturn` travados; data, transporte, volume e
  observações editáveis;
- incluir linha nova na devolução própria é recusado (toda linha nasce da venda).

## 8. A NF-e de devolução

### 8.1 Entrada do montador (`SiagroB1.Fiscal/Nfe/NfeIssueInput.cs`)

- `NfeIssueInput.Purpose` — enum novo `NfePurpose { Sale, Return }` (padrão `Sale`);
- `NfeIssueInput.ReferencedKeys` — `IReadOnlyList<string>` (vazio na venda; a chave da venda na devolução);
- `NfeItem.Reference` — `NfeItemReference(string AccessKey, int ItemNumber)?` (nulo na venda).

### 8.2 O `nItem` da venda

Hoje o montador numera as linhas na ordem em que o EF as devolve (`NfeIssueInputAssembler.cs:43`, sem
`OrderBy`). Passa a ser fixo:
- **`SALES_INVOICES_ITEMS.NfeItemNumber`** — `INT NULL`. Na emissão (Normal ou devolução), antes de assinar,
  as linhas sem número recebem 1..n na ordem de `Key` (ordenação em memória, `Guid` do .NET — nunca no SQL, que ordena `uniqueidentifier` de outro jeito), e o número é gravado junto com o XML assinado; uma
  reemissão depois de rejeição reaproveita os mesmos números;
- o montador ordena as linhas por `NfeItemNumber` e usa esse número no `det/@nItem`;
- a devolução lê o `NfeItemNumber` da linha vendida para o `DFeReferenciado`. Venda autorizada antes desta
  mudança (`NfeItemNumber` nulo): com **um** item, vale 1; com vários, a criação recusa — "A NF-e de venda foi
  emitida antes da numeração dos itens; a devolução com NF-e não está disponível para ela." Hoje só existem
  notas de homologação, todas de um item.

### 8.3 `NfeXmlBuilder` — o que muda na devolução

| Grupo | Venda (hoje) | Devolução |
|---|---|---|
| `ide/tpNF` | 1 | **0** |
| `ide/finNFe` | 1 | **4** |
| `ide/NFref` | — | **ausente** — a referência vai só no item (`DFeReferenciado`); a SEFAZ rejeita nota + item juntos (rejeição 1010, homologação 04/10/2026) |
| `ide/natOp` | texto da natureza da 1ª linha | idem (é a natureza de devolução) |
| `det/prod/CFOP` | 5xxx/6xxx | **1xxx/2xxx** (da natureza de entrada) |
| `det/DFeReferenciado` | — | `chaveAcesso` = chave da venda, `nItem` = `NfeItemNumber` da linha vendida |
| `cobr` | parcelas da condição | **ausente** |
| `pag` | da condição de pagamento | **`tPag 90`, `vPag 0`** |
| `infAdic/infCpl` | textos das naturezas + digitado | **"Devolução da NF-e nº {nNF}, série {serie}, de {dd/MM/aaaa}, chave {chave}."** + textos da natureza + digitado |

Destinatário, emitente, produto (NCM, CEST, cBenef, `indEscala`), transporte, volume, totais e IBS/CBS seguem o
montador do 2a sem mudança. `idDest` sai da UF do cliente, como na venda.

### 8.4 Montador e prontidão (`SiagroB1.Application/Services/Nfe/`)

- `NfeIssueInputAssembler`: para a devolução própria preenche `Purpose = Return`, `ReferencedKeys`, o
  `Reference` de cada item (chave da venda + `NfeItemNumber` da linha vendida), o texto de referência no
  `infCpl` e `Payment = PaymentPlan("90", null, 0, [])` (o mesmo plano que `PaymentInstallmentCalculator` já gera para "90 – sem pagamento") sem condição de pagamento;
- `NfeIssueContext` ganha a venda de origem (`OriginInvoice`, com itens) para a devolução;
- `NfeReadinessValidator`: na devolução, a coerência CFOP × destino passa a aceitar 1xxx (mesma UF) e 2xxx
  (outra UF), e a exigência de condição de pagamento não se aplica; o resto (pesos, CEST, cadastro) é o mesmo.

## 9. Emissão, confirmação e travas

### 9.1 Documento de saída — coluna nova

**`SALES_INVOICES.IsNfeReturn`** — `BIT NOT NULL DEFAULT 0`. Só o serviço do §6 grava `true`. É o que
botões, travas e confirmação leem para saber que a devolução sai com NF-e própria, sem consultar a origem.

### 9.2 "Emitir NF-e" (`SalesInvoicesNfeIssueService`)

- a trava de `SalesInvoicesNfeIssueService.cs:162-163` ("só o documento Normal…") passa a aceitar a devolução
  própria; qualquer outro Return segue recusado;
- pré-condições extras da devolução, **antes de reservar o número**: a venda continua `Authorized` com chave; a
  soma desta devolução com as outras não canceladas não passa do vendido por item (a conferência do confirm —
  `ValidateLineItemBalance` — roda depois da autorização, tarde demais para impedir uma NF-e de quantidade a
  mais);
- numeração: a mesma série e sequência da venda (`NfeNumberReservationService`), como a CEAGUI faz hoje;
- o restante é o fluxo do 2a: grava o assinado, transmite, trata o retorno; autorizada ⇒
  `SalesInvoiceNfeResultHandler` confirma ⇒ ramo Return do `SalesInvoicesConfirmService` (quantidade devolvida
  da origem, ledger do contrato, entrega e status `Returned` da origem na devolução total).

### 9.3 Consulta, DANFE e XML

"Consultar situação", "Concluir confirmação", DANFE e download do XML funcionam para a devolução sem mudança
de regra (são dirigidos por `NfeStatus`). O DANFE da Zeus já imprime entrada/saída pelo `tpNF`.

### 9.4 Travas

- `SalesInvoicesConfirmService` (guarda de `:79-88`): passa a valer também para a devolução própria —
  "Na filial que emite NF-e pelo Siagro, confirme emitindo a NF-e.";
- "Informar Nota Fiscal" recusado para a devolução própria (backend em `SalesInvoicesSetDocumentNumberService`,
  frontend em `NfeHelpers.isManualTaxDocumentBlocked` e no diálogo de `Main.controller.ts:224-238`);
- `SalesInvoicesReturnService` ("Retornar") e `ShipmentLoadsRefuseService` ("Recusa de carga"): com a regra
  ativa e a origem `NfeStatus = Authorized`, recusam — "Na filial que emite NF-e pelo Siagro, a devolução de
  documento com romaneio ou carga ainda não é suportada." Sem a regra ativa, nada muda;
- devolução própria Pendente pode ser cancelada ou excluída como hoje; autorizada só sai pelo cancelamento da
  NF-e (2b).

## 10. Telas (frontend)

| Tela | Mudança |
|---|---|
| Documento de saída — detalhe (`view/salesInvoices/Detail.view.xml`) | Botão **"Devolver"**: `ui>/taxLocked`, `InvoiceType = Normal`, `InvoiceStatus = Confirmed`, `NfeStatus = Authorized`, sem carga. Abre o diálogo novo. "Emitir NF-e", "Consultar situação", "DANFE" e "XML" passam a valer também para `IsNfeReturn`; "Confirmar" fica oculto para ela. |
| Diálogo "Devolver" (fragmento novo) | Grade de `SalesInvoicesNfeReturnableItems`: Produto, Descrição, Vendido, Já devolvido, **A devolver** (Input numérico, preenchido com o saldo); **Motivo** (TextArea, obrigatório). Confirmar → `SalesInvoicesCreateNfeReturn` → navega para o detalhe da devolução. Sem saldo em nenhum item: mensagem e o diálogo não abre. |
| Documento de saída — formulário | Na devolução própria: número/série/chave travados (como na Normal com a regra ativa); cliente e filial travados; na grade de itens, só Quantidade editável. |
| Natureza — formulário (`view/usages/fragments/Form.fragment.xml`) | Campo **"Natureza de devolução"** no bloco "Dados da Natureza de Operação", visível com `Direction = Outgoing` e `ui>/identityEditable`; value help só de naturezas de Entrada ativas ($filter estático no enum), exibindo código e nome. |
| `model/ServerRoutes.ts` | `salesInvoicesCreateNfeReturn`, `salesInvoicesNfeReturnableItems`. |

## 11. Migration

Uma, aditiva, no `AppDbContext` (`AddSalesReturnNfe`): `USAGES.ReturnUsageCode` (+ FK + índice),
`SALES_INVOICES_ITEMS.NfeItemNumber`, `SALES_INVOICES.IsNfeReturn` (default 0). Nenhum backfill.

## 12. Testes e verificação

TDD com RED visto antes do GREEN.

**`SiagroB1.Fiscal.Tests`:**
- `NfeXmlBuilder`, devolução: `tpNF 0`, `finNFe 4`, sem `NFref` no cabeçalho, `DFeReferenciado` em cada item com a chave
  e o `nItem`, CFOP 1202, `tPag 90` / `vPag 0`, sem `cobr`, texto de referência no `infCpl`;
- a venda continua idêntica (sem `NFref`, sem `DFeReferenciado`, `tpNF 1`, `finNFe 1`);
- `NfeSigner`: a devolução valida no XSD oficial.

**`SiagroB1.Application.Tests`:**
- natureza: cada validação do §5;
- criação (§6): cada recusa (filial sem NF-e, venda não autorizada, com carga/romaneio, sem motivo, quantidade
  zero ou acima do saldo, natureza sem vínculo, venda antiga com vários itens), a devolução total e a parcial,
  a segunda parcial até esgotar, a natureza herdada por linha, `IsNfeReturn = true`, origem intocada;
- cálculo (§7): CFOP de entrada dentro/fora do estado, IBS/CBS pela data da venda (alíquota trocada entre as
  datas), cada campo da conferência divergindo, travas da linha e do cabeçalho, linha nova recusada;
- `nItem` (§8.2): gravado na emissão na ordem de `Key`, reaproveitado na reemissão, montador ordenando por ele;
- emissão (§9.2): a devolução própria passa, outro Return é recusado, saldo estourado recusado antes da reserva,
  autorizada ⇒ confirma e a origem recebe a quantidade devolvida;
- travas (§9.4): confirmação direta recusada, "Informar Nota Fiscal" recusado, "Retornar"/"Recusa de carga"
  recusados só com a regra ativa e origem autorizada;
- isolamento: regra inativa ⇒ nada muda (Yokotobi, MH Agro).

**Na tela (stack `ceagui`, Playwright):**
1. ligar a natureza 2 (venda) à 5 (devolução) no formulário da natureza;
2. no **DS000146** (NF-e série 9 nº 3, autorizada em homologação em 04/10), "Devolver" com quantidade parcial
   e depois a total do saldo; conferir os impostos da devolução contra a venda;
3. **emitir em homologação** (onde a VC02-14 já vale) — só com pedido explícito do usuário na hora, pela tela,
   como na emissão de 04/10 — e conferir autorização, DANFE e XML contra a devolução real 1507.xml do EfisCloud
   (`dados-ceagui-efiscloud/xml/`);
4. conferir que a venda de origem ficou com a quantidade devolvida e, na devolução total, `Returned`.

Gates: `dotnet build` sem erro, suítes Fiscal e Application verdes, `yarn ts-typecheck`, `yarn lint`, `ui5lint`
sem aumento sobre a base (920).

## 13. Riscos

- **Datas da VC02-14 podem mudar de novo.** O `DFeReferenciado` é opcional no XSD; mandá-lo sempre é aceito
  antes e depois da data de produção.
- **A SEFAZ pode exigir que a chave referenciada exista e esteja autorizada no mesmo ambiente.** O teste em
  homologação referencia uma venda de homologação; em produção a venda será de produção.
- **Ordem dos itens da venda.** A ordenação por `NfeItemNumber` muda a ordem dos `det` das vendas novas em
  relação a hoje (sem efeito fiscal); é o preço de o `nItem` ser reproduzível.
- **Venda antiga com vários itens** fica sem devolução automática (§8.2). Hoje não há nenhuma em produção.
- **Confirmação falhando depois da autorização** (ex.: dado alterado em paralelo) cai no fluxo existente de
  `NfeConfirmationError` + "Concluir confirmação"; a pré-condição de saldo do §9.2 reduz esse caso ao concorrente.
