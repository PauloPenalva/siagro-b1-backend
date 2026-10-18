# NF-e de saída de complemento de ICMS (Documento de Saída)

Data: 2026-10-18
Status: desenho por delegação (o usuário pediu para começar direto: "é recorrente"). Decisões marcadas **[call]**
são minhas e ficam abertas à revisão do usuário.
Modo alvo: `STANDALONE`, filial com regra de tributação ativa (a NF-e de saída só existe aí). Em `SAPB1` nada muda.

Item 3 da fila de 17/10 (memória `fiscal-entries-next-steps-cte-icms-complement`). Espelha, no Documento de Saída
(`SalesInvoice`), o complemento de ICMS próprio que o Documento de Entrada ganhou em 18/10
(`docs/superpowers/specs/2026-10-17-purchase-invoice-complements-design.md`, D2/D4/D5/D8).

## Problema

Quando a nota de venda autorizada saiu com ICMS a menor (base ou alíquota erradas, benefício indevido), a
diferença é lançada por uma NF-e complementar nossa (finNFe=2), sem valor de mercadoria, só com a base e o ICMS
complementares, referenciando a original. Hoje o Documento de Saída só tem `Normal` e `Return`.

## Decisões

| # | Decisão |
|---|---|
| S1 | `SalesInvoiceType.IcmsComplement = 2` ("Complemento de ICMS"). Os valores atuais ficam explícitos (`Normal = 0`, `Return = 1`). |
| S2 | **Nasce de uma ação no Detail da nota original**, como o "Devolver": "Complementar ICMS" numa venda `Normal`, Confirmada, NF-e Autorizada, `TaxDocumentKind = Nfe`. O diálogo lista os itens da original; o operador digita Base ICMS e ICMS por item (item com os dois zerados fica fora); o servidor cria o documento e a tela navega para ele. Um complemento referencia **uma** nota original. **[call]** — é o padrão da saída (sem rascunho, tudo OData) e evita escolher itens de notas diferentes. |
| S3 | Vínculo na linha: coluna nova `SalesInvoiceItem.ComplementedItemKey` (FK para `SALES_INVOICE_ITEMS`, NoAction, índice não único). **Não** reaproveita `SalesInvoiceItemOriginKey`/`SalesInvoiceOriginKey`: esses campos significam devolução e são lidos sem filtro de tipo no remanejamento de contrato e na compensação de títulos. Migration `AddSalesInvoiceItemComplementedItem`. |
| S4 | A linha herda da original produto, descrição, unidade, natureza, CFOP e NCM; quantidade, preço, frete/seguro/desconto/outras = 0; **sem contrato e sem liberação/romaneio** (o complemento não mexe em contrato). Cabeçalho herda cliente, filial, endereço de entrega; sem condição de pagamento, sem transportadora, sem pesos/volumes. **[call]** |
| S5 | Tributos como na entrada (D4 de lá): o motor roda com valor 0 (resolve CFOP e zera o resto) e um aplicador repõe Base/ICMS informados; alíquota = ICMS ÷ base × 100 (4 casas); CST = o da linha original se 00, 20 ou 90; CSOSN (3 dígitos) → 900; senão 90; PIS/COFINS/IBS/CBS com base e valor 0, **mantendo as alíquotas e reduções de IBS/CBS** (sem elas a SEFAZ-SP rejeita com 1033, visto em 18/10 na entrada). CFOP = o da linha original. |
| S6 | Edição: enquanto Pendente, o documento pode ser **excluído** e recriado; as linhas não são editáveis (nem incluir/remover), exceto Base ICMS / ICMS por PATCH de linha. Cabeçalho travado como o da devolução própria (cliente, filial, tipo, vínculo). **[call]** |
| S7 | Emissão: sai **Pendente**, como a devolução (o resultado autorizado confirma). finNFe=2, NFref = chave da original, sem pagamento (tPag 90), sem transporte (modFrete 9, sem `vol`), vProd 0, vNF 0. Readiness: não exige condição de pagamento nem pesos; exige Base/ICMS > 0 em toda linha e ICMS ≤ base; recarrega a original e recusa se deixou de estar autorizada. |
| S8 | **Nenhum efeito além do fiscal**: sem título a receber, sem alocação/razão de contrato, sem efeito em carga, liberação, romaneio, custódia, descarga, conciliação de entrega; sem "Devolver". Cada ponto que hoje dispara para "não-Return" passa a excluir `IcmsComplement`. |
| S9 | Travas na original (C8 da entrada): cancelar o documento ou a NF-e da original é recusado enquanto houver complemento não cancelado apontando para ela. Mais de um complemento para a mesma original é permitido. |
| S10 | Só STANDALONE com a regra de tributação ativa na filial; fora disso a ação não aparece e o backend recusa. |

## Mensagens (pt-BR, exatas)

| Código | Texto |
|---|---|
| V1 | `Só uma venda normal, confirmada, com NF-e autorizada pode ser complementada.` |
| V2 | `Informe a base e o valor do ICMS de ao menos um item.` |
| V3 | `Item {item}: informe a base e o valor do ICMS do complemento.` |
| V4 | `Item {item}: o ICMS do complemento não pode ser maior que a base.` |
| V5 | `A nota original {número} não está mais autorizada: o complemento não pode ser emitido.` |
| V6 | `A nota {número} tem o complemento de ICMS {número do complemento} ativo: cancele o complemento antes.` |
| V7 | `O complemento de ICMS não aceita inclusão nem remoção de itens.` |
| V8 | `Complemento de ICMS só existe na filial que emite NF-e pelo Siagro.` |

`{número}` = número de documento exibido na tela.

## Serviços e API

- Ação OData `SalesInvoicesCreateIcmsComplement(Key, OriginItemKeys, IcmsBases, IcmsValues)` (arrays paralelos,
  como `SalesInvoicesCreateNfeReturn`) → devolve o documento criado. Recusa V1/V2/V3/V4/V8.
- Tributos: `SalesInvoicesTaxApplyService` aplica a regra S5 para `IcmsComplement` (o aplicador da entrada pode
  ser generalizado para `INfeTaxedLine`, ou um gêmeo de saída — escolha do builder, sem duplicar a lógica).
- `SalesInvoicesItemsUpdateService` preserva/aceita Base/ICMS do complemento (o `TaxSnapshot` sobrescreveria);
  `SalesInvoicesItemsCreate`/`Delete` recusam V7.
- Emissão: `SalesInvoicesNfeIssueService` (`EnsureIssuableType`, `RequiredStatus` Pendente para o complemento),
  `NfeReadinessValidator.ValidateAsync(SalesInvoice)`, `NfeIssueInputAssembler.Build(SalesInvoice)` — mesmas
  peças da entrada (`NfePurpose.Complement`, referenced keys, `WithoutPayment`).
- Confirmação (no resultado autorizado): pula alocação de contrato, recebíveis e o gancho de carga.
- Exclusões S8: `FinancialDocumentsSalesFirmSyncService.DesiredAsync`, `SalesInvoicesConfirmService`,
  `SalesInvoicesCancelService` (gancho de carga, recebíveis), e os pontos que filtram "não-Return".
- C8: `SalesInvoicesCancelService.EnsureBusinessRulesAsync` (documento e NF-e).

## Frontend (Documento de Saída)

- Detail da venda: botão "Complementar ICMS" (mesma regra de visibilidade do "Devolver", sem a restrição de
  carga) → diálogo (Produto, Descrição, Quantidade, Valor, Base ICMS, ICMS) → navega para o documento criado.
- Documento de complemento: Tipo "Complemento de ICMS" (só leitura; o Select ganha o item desabilitado);
  grade sem Incluir/Remover, com Base ICMS / ICMS visíveis (editáveis só em Pendente, no Edit); botão "Emitir
  NF-e" como o da devolução; seções de pagamento e Transporte escondidas; sem "Devolver".
- Lista: filtro, coluna e exportação com "Complemento de ICMS"; `formatSalesInvoiceType` com o novo tipo.

## Testes

- Criação: V1/V2/V3/V4/V8; herança S4; vínculo S3; um item zerado fica fora.
- Tributos S5 (CST/CFOP da original, alíquota, IBS/CBS mantidas com valor 0).
- Emissão: XML finNFe 2, NFref, vProd/vNF 0, tPag 90, modFrete 9, sem vol; readiness; V5.
- S8: confirmação do complemento não cria título nem alocação; carga/liberação intocadas.
- C8 (V6) no cancelamento do documento e da NF-e da original; complemento cancelado não trava.
- QUnit dos helpers novos (payload do diálogo, formatter).

## Verificação (CEAGUI dev + homologação SEFAZ-SP)

Emitir um complemento de ICMS sobre uma venda autorizada em homologação; registrar cStat. Cancelar a original →
V6. Conferir que nenhum título/alocação nasceu. Stack derrubada por PID.

## Fora

- Complemento de valor e de quantidade na saída; complemento de IPI/ICMS-ST; NF-e de ajuste (finNFe 3).
- Complemento sobre nota de terceiro, nota em papel (`Other`) ou SAPB1.
- Um complemento cobrindo várias notas originais.
