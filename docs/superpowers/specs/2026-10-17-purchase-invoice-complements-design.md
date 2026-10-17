# Complementos no Documento de Entrada: ICMS, valor e quantidade

Data: 2026-10-17
Status: desenho por delegação (o usuário pediu para começar direto, sem rodada de perguntas). Toda decisão abaixo
marcada **[call]** é minha e está aberta à revisão do usuário.
Modo alvo: `STANDALONE`. Em `SAPB1` só o complemento de valor de terceiro continua existindo, como hoje.

Item 2 da fila de 17/10 (memória `fiscal-entries-next-steps-cte-icms-complement`). O usuário ampliou o item:
"complemento de ICMS, complemento de valor e complemento de quantidade" — documentos recorrentes, sem caso
específico.

## O que existe hoje

| | Terceiro (escrituração) | Próprio (NF-e finNFe=2) |
|---|---|---|
| Valor | `ValueComplement`, contrato obrigatório, firme na confirmação | `ValueComplement` + `ComplementedItemKey`, contrato obrigatório (spec 2026-10-14) |
| ICMS | não existe (import de finNFe=2 com vProd 0 cai em valor e é recusado) | não existe |
| Quantidade | não existe (import descarta qCom) | não existe |

O montador do XML (`NfeXmlBuilder`) já gera os dois casos novos sem mudança: vProd = Σ Total, vBC/vICMS = Σ dos
itens com CST que carrega valor, vNF = Σ GrandTotal. As lacunas estão na regra da linha, no motor de tributos, na
importação, no pagamento e no financeiro.

## Decisões

| # | Decisão |
|---|---|
| D1 | **Dois tipos novos** em `PurchaseInvoiceType`: `IcmsComplement = 3` ("Complemento de ICMS") e `QuantityComplement = 4` ("Complemento de quantidade"). `ValueComplement = 2` continua. Os três são "complementos" (helper `PurchaseInvoiceComplements.IsComplement(type)`). **[call]** — tipo por documento, como o de valor, em vez de um sub-tipo por linha: a NF-e complementar é de uma natureza só. |
| D2 | Cada tipo existe **próprio e de terceiro**. Próprio = NF-e complementar nossa (finNFe=2) referenciando entrada própria autorizada, com as mesmas regras C1–C10 do complemento de valor (linha aponta `ComplementedItemKey`, herda produto/UM/natureza/contrato; travas de cancelamento C8; sem "Devolver" C9). Terceiro = escrituração da nota do fornecedor, sem vínculo com a original (como hoje). |
| D3 | **Natureza manda no contrato também no complemento** (regra de `other-purchases`, `UsageEffect.RequiresContract`). Natureza que exige contrato → contrato obrigatório na linha; natureza de outra compra (sem contrato + gera financeiro) → contrato recusado e título pelo caminho de outras compras. Isso estende o complemento de VALOR, que hoje exige contrato sempre: o complemento de nota de diesel/serviço passa a ser aceito. **[call]** |
| D4 | **Complemento de ICMS: o operador informa base e valor do ICMS por linha**, o motor não calcula. Quantidade, preço e valor do complemento = 0; PIS/COFINS/IBS/CBS = 0; alíquota gravada = valor ÷ base × 100 (2 casas), só informativa. CST: o da linha original quando carrega valor (00, 10, 20, 70, 90); senão 90. CFOP: o da original (próprio) ou o da natureza (terceiro). **[call]** |
| D5 | **Complemento de ICMS não gera título** (vNF = 0). Se o fornecedor cobrar a diferença, ela vem como complemento de valor. Natureza da linha não precisa "gerar financeiro". Contrato: segue D3 (referência, sem efeito financeiro). **[call]** |
| D6 | **Complemento de quantidade: quantidade e preço > 0, valor do complemento = 0**; tributos calculados pela natureza, como uma entrada normal. Próprio: preço unitário sugerido = o da original, editável. **[call]** |
| D7 | **Complemento de quantidade se comporta como entrada Normal no financeiro e na conciliação**: linha com contrato concilia com romaneio de compra (tipos 8, 10, 17) e o firme nasce do vínculo, como na Normal; documento de outras compras gera parcelas + retenções na confirmação. **[call]** |
| D8 | **Complemento de valor** mantém o firme na confirmação para linha com contrato; linha de outra compra (D3) gera parcelas + retenções pelo caminho de outras compras. Documento não mistura linha de contrato com linha de outra compra (regra existente). |
| D9 | **Importação do XML de terceiro (finNFe=2)** decide o tipo pelos itens: todos com vProd = 0 e vICMS > 0 → ICMS (carrega vBC/vICMS do XML na linha); algum com qCom > 0 e vUnCom > 0 → quantidade (carrega qCom/vUnCom); senão → valor (como hoje). **[call]** |
| D10 | NF-e própria de ICMS: sem pagamento (`tPag` 90), sem pesos, `modFrete` 9. NF-e própria de quantidade: com condição de pagamento (como a entrada própria), **sem exigir pesos** (regularização fiscal; a mercadoria já foi pesada no romaneio). **[call]** |
| D11 | Nenhum complemento tem "Devolver"; o saldo devolvível da original não muda. **[call]** — devolver mercadoria de um complemento de quantidade fica fora. |
| D12 | `ICMS` e `Quantidade` só em STANDALONE; em SAPB1 a tela não oferece e o backend recusa. **[call]** |

## Regras da linha

Generalizar `PurchaseInvoiceValueComplement` para `PurchaseInvoiceComplement` (mesmo arquivo renomeado, mesma
chamada pelos quatro caminhos que gravam linha e pela confirmação/emissão).

Comuns aos três tipos:
1. Vínculo `ComplementedItemKey` só existe em complemento próprio (C7 generalizada: "O item da nota original só
   existe no complemento de emissão própria.").
2. Complemento próprio: C1–C6, C8, C10 valem para os três tipos (textos trocam "complemento de valor" por
   "complemento"). `IsComplementableOrigin` igual. Candidatos (`PurchaseInvoicesGetComplementCandidates`) iguais.
3. Natureza obrigatória (todos os tipos). Contrato conforme D3.
4. Fora do complemento: `ComplementAmount` ≠ 0 recusado (regra existente); `IcmsBase`/`IcmsValue` informados são
   sobrescritos pelo motor, como hoje.

Por tipo:

| Regra | Valor | ICMS | Quantidade |
|---|---|---|---|
| Quantidade | = 0 | = 0 | > 0 |
| Preço unitário | = 0 | = 0 | > 0 |
| Valor do complemento | > 0 | = 0 | = 0 |
| Base ICMS / ICMS | motor | informados, ambos > 0 | motor |
| Natureza "gera financeiro" (STANDALONE) | exigida | não exigida | exigida (como Normal) |
| UM | herdada / opcional | herdada / opcional | **obrigatória** (fator kg da conciliação) |

Mensagens novas (pt-BR, exatas):

| Código | Texto |
|---|---|
| K1 | `Item {item}: no complemento de ICMS a linha não tem quantidade, preço nem valor; informe só a base e o valor do ICMS.` |
| K2 | `Item {item}: informe a base e o valor do ICMS do complemento.` |
| K3 | `Item {item}: no complemento de quantidade informe a quantidade e o preço unitário.` |
| K4 | `Item {item}: no complemento de quantidade a linha não tem valor de complemento.` |
| K5 | `Complemento de ICMS e de quantidade só existem no modo STANDALONE.` |
| K6 | `Item {item}: o ICMS do complemento não pode ser maior que a base.` |

## Motor de tributos

- `PurchaseInvoicesTaxApplyService.IsActiveForAsync`: ativo para `QuantityComplement` (próprio e terceiro, como
  Normal) e para `ValueComplement` (já é).
- `IcmsComplement`: **o motor não roda** na linha. Um aplicador próprio (`PurchaseInvoiceIcmsComplementTaxes`)
  grava CST (D4), CFOP, `IcmsRate` derivada e zera PIS/COFINS/IBS/CBS, preservando `IcmsBase`/`IcmsValue`
  informados. Roda nos mesmos pontos em que `ApplyAsync` roda hoje, e respeita o `TaxSnapshot.RestoreLocked` fora
  de Pendente.
- DIFAL/FCP: não se aplica a complemento de ICMS; quantidade segue a regra da Normal.

## Emissão (próprio)

- `EnsureIssuableType` aceita os três complementos próprios.
- Assembler: `NfePurpose.Complement` para os três; `WithoutPayment` também para `IcmsComplement`; `ReferencedKeys`
  como hoje.
- Readiness: complementos não exigem pesos (já é para valor); quantidade exige UM; ICMS exige base/valor > 0 em
  toda linha; recarrega originais (C6) para os três.
- Itens: valor → qCom/vUnCom 0 (hoje); ICMS → qCom/vUnCom/vProd 0, vBC/vICMS da linha; quantidade → qCom/vUnCom da
  linha. ⚠️ Confirmar em homologação SEFAZ-SP: ICMS com vProd 0 e vNF 0, e se exige `pICMS` coerente (rejeição 528
  "valor do ICMS difere de base × alíquota") — por isso a alíquota é derivada com arredondamento compatível.

## Financeiro

- `FinancialDocumentsFirmSyncService.EnqueueForValueComplementAsync`: só linhas de valor com contrato (D8).
- `PurchaseInvoiceLineGuard.IsOtherPurchaseDocumentAsync`: aceita `ValueComplement` e `QuantityComplement` além de
  `Normal` (D3/D7/D8); `IcmsComplement` nunca.
- Conciliação fiscal (`PurchaseFiscalReconciliationRules.SenseMatches` e a query de linhas oferecidas): 
  `QuantityComplement` ↔ 8/10/17, como Normal; próprio só confirmado, como Normal. `IcmsComplement` e
  `ValueComplement` continuam fora.
- Cancelamento/estorno: as travas e limpezas existentes cobrem (firme do vínculo, firme de valor, títulos de outras
  compras). C8 vale para os três tipos.

## Importação de terceiro

- `SupplierNfeXmlReader`: já lê vBC/vICMS/qCom/vUnCom.
- `PurchaseInvoicesImportXmlService`: tipo por D9; ICMS carrega `IcmsBase`/`IcmsValue` do XML; quantidade
  carrega `Quantity`/`UnitPrice`.

## Frontend

- Select Tipo: `Normal`, `Devolução` (como hoje), `Complemento de valor`, `Complemento de ICMS`,
  `Complemento de quantidade` (os dois novos escondidos fora de STANDALONE). Em modo NF-e os três complementos
  ficam disponíveis.
- `ui>/ownComplement` e `isOwnComplement` passam a valer para os três; "Incluir item" no complemento próprio usa o
  mesmo diálogo de candidatos. Linha do complemento de quantidade nasce com o preço da original e o foco na
  Quantidade; a de ICMS com foco na Base ICMS.
- Grade de itens por tipo: Quantidade/Preço visíveis em Normal, Devolução e Quantidade; Valor do complemento em
  Valor; colunas **Base ICMS** e **ICMS** editáveis só no ICMS. Natureza visível nos três complementos.
- `clearHiddenAmountColumns` por tipo; `canIssuePurchaseNfe`, `isPurchaseTaxMode`, `missingUnitOfMeasureMessage`,
  `formatPurchaseInvoiceType`, filtro da lista e exportação com os tipos novos. Total da linha: ICMS mostra 0.
- Contrato da linha segue a natureza (como a Normal de outras compras); "Contrato (obrigatório)" só quando a
  natureza exige.

## Dados

- Enum novo (valores 3 e 4) — coluna `InvoiceType` é int, sem migration de esquema. Nenhuma coluna nova.
- **[call]** sem migration: tudo cabe nos campos existentes (`IcmsBase`, `IcmsValue`, `ComplementedItemKey`).

## Testes

- Regra da linha por tipo (tabela acima, K1–K6), D3 nos três, C7 generalizada, próprio C1–C10 para ICMS e quantidade.
- Motor: ICMS preserva base/valor informados, CST/alíquota derivados, PIS/COFINS zero; quantidade calcula.
- Assembler/Fiscal: finNFe=2 nos três, ICMS sem pagamento e vNF 0, quantidade com qCom.
- Financeiro: valor sem contrato vira outras compras; ICMS sem título; quantidade concilia e gera firme pelo
  vínculo; quantidade de outras compras gera parcelas.
- Import: classificação D9 com três XML.
- QUnit dos helpers com os tipos novos.

## Verificação (CEAGUI dev)

1. Terceiro: importar XML complementar de ICMS e de quantidade (gerados para o teste) → tipo certo, valores
   carregados, confirmar.
2. Próprio: complemento de ICMS e de quantidade sobre a série 9 nº 5 → emitir em homologação SEFAZ-SP; registrar
   cStat. Complemento de valor sem contrato (natureza de outra compra) → parcelas na confirmação.
3. Quantidade com contrato → conciliar com romaneio → firme.
4. Stack derrubada por PID.

## Fora

- Complemento de IPI/ICMS-ST, NF-e de ajuste (finNFe=3), notas de débito/crédito da reforma (finNFe 5/6).
- Vínculo da nota complementar de terceiro com a original (registro C113 do SPED) — entra com o SPED Fiscal.
- Devolução de complemento de quantidade.
- Complemento no documento de saída (item 3 da fila).
