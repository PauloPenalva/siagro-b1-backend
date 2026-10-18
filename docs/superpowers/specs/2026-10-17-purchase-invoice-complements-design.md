# Complementos no Documento de Entrada: ICMS e valor

Data: 2026-10-17
Status: desenho por delegação (o usuário pediu para começar direto, sem rodada de perguntas). Toda decisão abaixo
marcada **[call]** é minha e está aberta à revisão do usuário.
Modo alvo: `STANDALONE`. Em `SAPB1` só o complemento de valor de terceiro continua existindo, como hoje.

Item 2 da fila de 17/10 (memória `fiscal-entries-next-steps-cte-icms-complement`). O usuário ampliou o item para
"complemento de ICMS, complemento de valor e complemento de quantidade" — documentos recorrentes, sem caso
específico — e depois **tirou o complemento de quantidade do escopo** ("complemento de quantidade não existe").

## O que existe hoje

| | Terceiro (escrituração) | Próprio (NF-e finNFe=2) |
|---|---|---|
| Valor | `ValueComplement`, contrato obrigatório, firme na confirmação | `ValueComplement` + `ComplementedItemKey`, contrato obrigatório (spec 2026-10-14) |
| ICMS | não existe (import de finNFe=2 com vProd 0 cai em valor e é recusado) | não existe |

O montador do XML (`NfeXmlBuilder`) já gera o complemento de ICMS sem mudança: vProd = Σ Total, vBC/vICMS = Σ dos
itens com CST que carrega valor (00, 20, 51, 90, 900), vNF = Σ GrandTotal. As lacunas estão na regra da linha, no
motor de tributos, na importação, no pagamento e no financeiro.

## Decisões

| # | Decisão |
|---|---|
| D1 | **Um tipo novo** em `PurchaseInvoiceType`: `IcmsComplement = 3` ("Complemento de ICMS"). `ValueComplement = 2` continua. Os dois são "complementos" (helper `PurchaseInvoiceComplements.IsComplement(type)`). **[call]** — tipo por documento, como o de valor: a NF-e complementar é de uma natureza só. |
| D2 | O complemento de ICMS existe **próprio e de terceiro**. Próprio = NF-e complementar nossa (finNFe=2) referenciando entrada própria autorizada, com as mesmas regras C1–C10 do complemento de valor (linha aponta `ComplementedItemKey`, herda produto/UM/natureza/contrato; travas de cancelamento C8; sem "Devolver" C9). Terceiro = escrituração da nota do fornecedor, sem vínculo com a original (como o de valor). |
| D3 | **Natureza manda no contrato também no complemento** (regra de `other-purchases`, `UsageEffect.RequiresContract`). Natureza que exige contrato → contrato obrigatório na linha; natureza de outra compra (sem contrato + gera financeiro) → contrato recusado e título pelo caminho de outras compras. Isso estende o complemento de VALOR, que hoje exige contrato sempre: o complemento de nota de diesel/serviço passa a ser aceito. Em SAPB1 (natureza sem flag) o valor continua exigindo contrato. **[call]** |
| D4 | **Complemento de ICMS: o operador informa base e valor do ICMS por linha**, o motor não calcula. Quantidade, preço e valor do complemento = 0; PIS/COFINS/IBS/CBS = 0; alíquota gravada = valor ÷ base × 100 (4 casas), só informativa. CST: o da linha original quando é 00, 20 ou 90; CSOSN (3 dígitos) → 900; senão (e no terceiro) → 90. CFOP: o da original (próprio) ou o da natureza (terceiro). **[call]** |
| D5 | **Complemento de ICMS não gera título** (vNF = 0). Se o fornecedor cobrar a diferença, ela vem como complemento de valor. A natureza da linha não precisa "gerar financeiro". Contrato: segue D3 (referência, sem efeito financeiro). **[call]** |
| D6 | **Complemento de valor** mantém o firme na confirmação para linha com contrato; linha de outra compra (D3) gera parcelas + retenções pelo caminho de outras compras. O documento não mistura linha de contrato com linha de outra compra (regra existente). |
| D7 | **Importação do XML de terceiro (finNFe=2)**: todos os itens com vProd = 0 e vICMS > 0 → ICMS (carrega vBC/vICMS do XML na linha); senão → valor (como hoje). **[call]** |
| D8 | NF-e própria de ICMS: sem pagamento (`tPag` 90), sem pesos, `modFrete` 9. **[call]** |
| D9 | Complemento de ICMS não tem "Devolver" e não entra na conciliação com romaneio. |
| D10 | ICMS só em STANDALONE; em SAPB1 a tela não oferece e o backend recusa. **[call]** |

## Regras da linha

Generalizar `PurchaseInvoiceValueComplement` para `PurchaseInvoiceComplement` (arquivo renomeado, mesma chamada
pelos quatro caminhos que gravam linha e pela confirmação/emissão).

Comuns aos dois tipos:
1. Vínculo `ComplementedItemKey` só existe em complemento próprio (C7 generalizada: `O item da nota original só
   existe no complemento de emissão própria.`).
2. Complemento próprio: C1–C6, C8, C10 valem para os dois tipos (as mensagens que hoje dizem "complemento de valor"
   citam o rótulo do tipo). `IsComplementableOrigin` igual. Candidatos (`PurchaseInvoicesGetComplementCandidates`)
   iguais.
3. Natureza obrigatória. Contrato conforme D3.
4. Fora do complemento: `ComplementAmount` ≠ 0 recusado (regra existente); `IcmsBase`/`IcmsValue` informados são
   sobrescritos pelo motor, como hoje.

Por tipo:

| Regra | Valor | ICMS |
|---|---|---|
| Quantidade / preço unitário | = 0 | = 0 |
| Valor do complemento | > 0 | = 0 |
| Base ICMS / ICMS | motor | informados, ambos > 0, ICMS ≤ base |
| Natureza "gera financeiro" (STANDALONE) | exigida | não exigida |
| UM | herdada / opcional | herdada / opcional |

Mensagens novas (pt-BR, exatas):

| Código | Texto |
|---|---|
| K1 | `Item {item}: no complemento de ICMS a linha não tem quantidade, preço nem valor; informe só a base e o valor do ICMS.` |
| K2 | `Item {item}: informe a base e o valor do ICMS do complemento.` |
| K3 | `Complemento de ICMS só existe no modo STANDALONE.` |
| K4 | `Item {item}: o ICMS do complemento não pode ser maior que a base.` |
| K5 | `O documento {doc} é complemento de ICMS: não se concilia com romaneio.` |
| K6 | `Complemento de ICMS não tem devolução.` |

## Motor de tributos

- `PurchaseInvoicesTaxApplyService.IsActiveForAsync`: ativo para `IcmsComplement` (próprio e terceiro).
- `IcmsComplement`: o motor roda com valor 0 (só para resolver CFOP e zerar os demais tributos) e um aplicador
  próprio (`PurchaseInvoiceIcmsComplementTaxes`) repõe `IcmsBase`/`IcmsValue` informados, grava o CST (D4) e a
  `IcmsRate` derivada. Roda nos mesmos pontos em que `ApplyAsync` roda hoje e respeita o
  `TaxSnapshot.RestoreLocked` fora de Pendente. Sem Funrural, sem DIFAL/FCP.

## Emissão (próprio)

- `EnsureIssuableType` aceita os dois complementos próprios.
- Assembler: `NfePurpose.Complement` para os dois; `WithoutPayment` também para `IcmsComplement`;
  `ReferencedKeys` como hoje.
- Readiness: complementos não exigem pesos (já é para valor); ICMS exige base/valor > 0 em toda linha (K2);
  recarrega as originais (C6) para os dois.
- Itens do ICMS: qCom/vUnCom/vProd 0, vBC/vICMS da linha. ⚠️ Confirmar em homologação SEFAZ-SP: vProd 0 e vNF 0, e
  a coerência base × alíquota (por isso a alíquota é derivada com 4 casas).

## Financeiro

- `FinancialDocumentsFirmSyncService.EnqueueForValueComplementAsync`: só linhas de valor com contrato (D6).
- `PurchaseInvoiceLineGuard.IsOtherPurchaseDocumentAsync`: aceita `ValueComplement` além de `Normal` (D3/D6);
  `IcmsComplement` nunca.
- Conciliação fiscal: `IcmsComplement` fica fora, como o valor (query de linhas oferecidas e `EnsurePair`, K5).
- Cancelamento/estorno: as travas e limpezas existentes cobrem. C8 vale para os dois tipos.
- `PurchaseInvoiceDeclaredTotal` e `SupplierNfeKeyGuard` incluem o ICMS de terceiro (chave obrigatória; total
  declarado = 0).

## Importação de terceiro

- `SupplierNfeXmlReader`: já lê vBC/vICMS.
- `PurchaseInvoicesImportXmlService`: tipo por D7; ICMS carrega `IcmsBase`/`IcmsValue` do XML.

## Frontend

- Select Tipo: `Normal`, `Devolução` (como hoje), `Complemento de valor`, `Complemento de ICMS` (o novo escondido
  fora de STANDALONE). Em modo NF-e os dois complementos ficam disponíveis.
- `ui>/ownComplement` e `isOwnComplement` passam a valer para os dois; "Incluir item" no complemento próprio usa o
  mesmo diálogo de candidatos; a linha de ICMS nasce com o foco na Base ICMS.
- Grade de itens: Quantidade/Preço escondidos nos dois complementos; Valor do complemento só no Valor; colunas
  **Base ICMS** e **ICMS** editáveis só no ICMS. Natureza visível nos dois complementos.
- `clearHiddenAmountColumns` por tipo; `canIssuePurchaseNfe`, `isPurchaseTaxMode`, `missingUnitOfMeasureMessage`,
  `formatPurchaseInvoiceType`, filtro da lista e exportação com o tipo novo. O total da linha do ICMS mostra 0.
- Contrato da linha segue a natureza; "Contrato (obrigatório)" só quando a natureza exige.

## Dados

- Enum novo (valor 3); a coluna `InvoiceType` é int: sem migration. Nenhuma coluna nova. **[call]**

## Testes

- Regra da linha (tabela acima, K1–K4), D3 nos dois tipos, C7 generalizada, próprio C1–C10 para ICMS.
- Motor: ICMS preserva base/valor informados, CST/alíquota derivados, PIS/COFINS zero.
- Assembler/Fiscal: finNFe=2, ICMS sem pagamento e vNF 0.
- Financeiro: valor sem contrato vira outras compras; ICMS sem título; ICMS fora da conciliação.
- Import: classificação D7.
- QUnit dos helpers com o tipo novo.

## Verificação (CEAGUI dev)

1. Terceiro: importar um XML complementar de ICMS (gerado para o teste) → tipo certo, valores carregados,
   confirmar, nenhum título.
2. Próprio: complemento de ICMS sobre a série 9 nº 5 → emitir em homologação SEFAZ-SP; registrar o cStat.
3. Complemento de valor sem contrato (natureza de outra compra) → parcelas na confirmação.
4. Cancelar a nº 5 com complemento ativo → C8.
5. Stack derrubada por PID.

## Fora

- **Complemento de quantidade** (decisão do usuário, 17/10).
- Complemento de IPI/ICMS-ST, NF-e de ajuste (finNFe=3), notas de débito/crédito da reforma (finNFe 5/6).
- Vínculo da nota complementar de terceiro com a original (registro C113 do SPED) — entra com o SPED Fiscal.
- Complemento no documento de saída (item 3 da fila).
