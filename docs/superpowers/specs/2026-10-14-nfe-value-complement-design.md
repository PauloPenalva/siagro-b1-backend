# NF-e própria de complemento de valor (documento de entrada)

Data: 2026-10-14
Status: em revisão pelo usuário
Modo alvo: `STANDALONE` com NF-e (filial com regra de tributação ativa e Emissão Própria — o "modo NF-e" do
Documento de Entrada). Em `SAPB1` nada muda.

Sub-projeto **2b** de "título a pagar firme a partir do romaneio de compra". O sub-projeto 2
(`2026-10-13-purchase-firm-payables-design.md`, D11) criou o Documento de Entrada tipo **Complemento de valor**
(`PurchaseInvoiceType.ValueComplement = 2`) só como escrituração de nota de terceiro, e deixou para cá a emissão
própria (finNFe=2).

## Problema

Quando o produtor foi faturado abaixo do contrato e a nota original foi emitida **por nós** (entrada própria ou
contra-nota), a diferença precisa de uma **NF-e complementar nossa**, só de valor, referenciando a original. Hoje
o complemento de emissão própria é recusado em três camadas (regra da linha, emissão, tributação) e a tela trava
o Tipo em "Normal" no modo NF-e.

## Decisões do usuário

| # | Decisão |
|---|---|
| D1 | A NF-e complementar própria só referencia **nota nossa**: entrada própria **autorizada** (normal ou contra-nota). Quando o produtor emite a nota dele, ele mesmo emite o complemento, e nós só escrituramos (já funciona). |
| D2 | **Uma nota de complemento; cada linha referencia um item de uma nota original.** Uma nota pode complementar itens de notas originais diferentes (do mesmo produtor). |
| D3 | Na linha, o operador escolhe o **item da original**; a linha herda produto, unidade, natureza e contrato, e o operador só digita o valor do complemento. |
| D5 | **Original sem contrato:** a linha herda produto, unidade e natureza, e o **operador escolhe o contrato** de compra (mesmo produtor), como já faz na entrada. Original com contrato: o contrato é herdado e travado. (Decidido na revisão do plano: nenhuma entrada própria autorizada do CEAGUI dev tem contrato na linha.) |
| D4 | **Cancelar a original** (documento ou NF-e) é **recusado** enquanto houver complemento não cancelado apontando para ela. A **devolução** da original continua permitida e devolve só a original; o complemento não tem "Devolver". |

## Escopo decorrente (sem decisão nova)

- O vínculo fica na **linha** (`ComplementedItemKey`); o `NFref` do cabeçalho é derivado na emissão — as chaves
  distintas das notas originais. Não se reaproveita `PurchaseInvoiceItemOriginKey`/`PurchaseInvoiceOriginKey`:
  esses campos já significam devolução/remessa e alimentam o saldo devolvível.
- O complemento de **terceiro** fica como está (sem vínculo, sem emissão).
- Título firme, Funrural e tributos: sem mudança — já saem do valor da linha (`Total` inclui `ComplementAmount`;
  `TaxCalculator` não usa quantidade). O firme continua nascendo na **confirmação**
  (`FinancialDocumentsFirmSyncService.EnqueueForValueComplementAsync`).
- Fluxo da nota igual ao da entrada própria: confirmar, transmitir, cancelar, CC-e, inutilizar.
- Mais de um complemento para o mesmo item original é permitido (complementos sucessivos); o mesmo item
  original duas vezes **na mesma nota** não.

## Modelo de dados

### `PURCHASE_INVOICE_ITEMS` — coluna nova

```
ComplementedItemKey  UNIQUEIDENTIFIER NULL   FK → PURCHASE_INVOICE_ITEMS(Key), sem cascata (NoAction)
                     índice não único
```

- Preenchida **só** em linha de documento `ValueComplement` + `Own`, e aí é **obrigatória**.
- Em qualquer outro documento, preenchida = erro (C7).
- Migration `AddPurchaseInvoiceItemComplementedItem`. Sem backfill: hoje não existe complemento próprio.
- Navegação `ComplementedItem` (item original) só para leitura na emissão e nas travas; a tela lê os dados do
  original por function (abaixo), não por `$expand`.

## Regras

### Linha do complemento próprio (`PurchaseInvoiceValueComplement`)

Removida a recusa "Complemento de valor é só escrituração por enquanto". Para `ValueComplement` + `Own`, além das
regras existentes (quantidade e preço 0, valor > 0, contrato e natureza com "gera financeiro"):

1. `ComplementedItemKey` obrigatório (C1).
2. O item original existe e o **documento original**: é `Own`, tipo `Normal`, `NfeStatus = Authorized`,
   `InvoiceStatus` diferente de Cancelado (C2).
3. Documento original com o **mesmo `CardCode` e a mesma filial** do complemento (C3) — vale também quando o
   operador troca o produtor ou a filial do cabeçalho depois de incluir linhas (a regra roda no Update).
4. O mesmo item original não aparece em duas linhas da mesma nota (C4).
5. Produto, unidade e natureza da linha são os do item original. O backend **copia** do original no
   Create/ItemsCreate e **recusa** divergência no Update/ItemsUpdate (C5) — a tela trava esses campos.
5a. Contrato (D5): se o item original tem contrato, a linha herda e a divergência é recusada (C10); se não tem,
   o contrato é o que o operador informar, com as regras de contrato que a linha de entrada já aplica (mesmo
   produtor etc.), e continua obrigatório (regra existente do complemento).

### Emissão

6. `PurchaseInvoicesNfeIssueService.EnsureIssuableType` aceita `ValueComplement` + `Own`.
7. `PurchaseInvoicesTaxApplyService.IsActiveForAsync` passa a calcular para `ValueComplement` + `Own`.
8. Readiness (`NfeReadinessValidator`): o complemento próprio exige, como a entrada própria, produto e unidade
   (herdados) e condição de pagamento, mas **não exige pesos** (não há mercadoria transportada; o montador já omite
   `vol` com peso 0); e na emissão **recarrega** cada original e recusa se algum deixou de
   estar autorizado (C6) — a regra 2 vale no rascunho, mas a original pode ter sido cancelada antes da
   transmissão.
9. `NfePurpose` ganha `Complement` → `FinalidadeNFe.fnComplementar` (finNFe=2) no `NfeXmlBuilder`. O assembler
   escolhe `Complement` quando o documento é `ValueComplement`.
10. `ReferencedKeys` = chaves (`ChaveNFe`) distintas das notas originais das linhas, em ordem de primeira
    aparição. Itens **sem** `DFeReferenciado` (o complemento não é devolução), então o `NFref` vai no cabeçalho.
11. Itens com `qCom = qTrib = 0` e `vUnCom = vUnTrib = 0`, `vProd = ComplementAmount`; `uCom`/`uTrib` = unidade
    herdada. ⚠️ Risco a confirmar em homologação: a validação qCom × vUnCom = vProd (rejeição 629) não se aplica a
    finNFe=2 segundo o leiaute; se a SEFAZ-SP rejeitar, o ajuste é no montador (fora do escopo de decisão).
12. natOp: o da natureza da linha, como na entrada própria.

### Travas na original (D4)

13. Cancelar o **documento** original (`PurchaseInvoicesCancelService`) e cancelar a **NF-e** original
    (`PurchaseInvoicesNfeCancelService`) são recusados enquanto existir linha de complemento apontando para um
    item dela cujo documento não esteja cancelado (C8). Complemento cancelado (documento cancelado) não trava.
14. "Devolver" a original continua igual: o saldo devolvível conta só a original (o complemento não tem
    `PurchaseInvoiceItemOriginKey`). O complemento não oferece "Devolver" (backend recusa — C9 — e a tela
    esconde).

## Mensagens (pt-BR, exatas)

| Código | Texto |
|---|---|
| C1 | `Item {item}: o complemento de valor de emissão própria exige o item da nota original.` |
| C2 | `Item {item}: a nota original {número} precisa ser uma entrada própria normal, com NF-e autorizada e não cancelada.` |
| C3 | `Item {item}: a nota original {número} é de outro produtor ou de outra filial.` |
| C4 | `O item {número da nota original}/{nº do item} já está complementado nesta nota.` |
| C5 | `Item {item}: produto, unidade e natureza vêm do item da nota original e não podem ser alterados.` |
| C6 | `A nota original {número} não está mais autorizada: o complemento não pode ser emitido.` |
| C7 | `O item da nota original só existe no complemento de valor de emissão própria.` |
| C8 | `A nota {número} tem o complemento {número do complemento} ativo: cancele o complemento antes.` |
| C9 | `Complemento de valor não tem devolução.` |
| C10 | `Item {item}: o contrato vem do item da nota original e não pode ser alterado.` |

`{número}` = número de documento exibido na tela (o mesmo das demais mensagens do Documento de Entrada).

## Serviços e API

- **Function** `PurchaseInvoicesGetComplementCandidates(CardCode, BranchCode)` → coleção de itens
  originais elegíveis (regra 2 e 3), DTO PascalCase (`[JsonPropertyName]`): `ItemKey`, `InvoiceKey`,
  `InvoiceNumber`, `InvoiceDate`, `AccessKey`, `LineNumber`, `ItemCode`, `ItemName`, `UnitOfMeasure`,
  `Quantity`, `UnitPrice`, `Total`, `UsageCode`, `PurchaseContractKey` e `PurchaseContractCode` (anuláveis: item
  sem contrato também é candidato — D5).
- `PurchaseInvoiceItem` no EDM com `ComplementedItemKey` (Edm.Guid, anulável).
- Create/Update/ItemsCreate/ItemsUpdate do Documento de Entrada aceitam `ComplementedItemKey` e aplicam as regras.
- `NfeIssueInputAssembler.Build(PurchaseInvoice)`: propósito e `ReferencedKeys` (regras 9–10).
- Recusas como `BadRequest(string)`, como o resto do Documento de Entrada.

## Frontend

- **Tipo no modo NF-e:** o Select deixa de travar; oferece "Normal" e "Complemento de valor" (Devolução segue só
  pelo "Devolver"). `mustFallBackToNormal` mantém Complemento de valor. Trocar o tipo com linhas já incluídas
  segue a regra que a tela já tem para troca de tipo.
- **Incluir item no complemento próprio:** abre um diálogo de seleção (`openTableSelectDialog`, ver memória do
  contrato do helper) com os itens de `PurchaseInvoicesGetComplementCandidates` do produtor e filial do
  cabeçalho (nota, data, item, produto, quantidade, valor, contrato). Escolhido o item, a linha nasce com produto,
  unidade e natureza preenchidos e **travados**; o contrato vem preenchido e travado quando o original tem, e
  editável (value help de contrato atual) quando não tem; quantidade e preço 0, e o foco no "Valor do
  complemento". Sem produtor no cabeçalho: aviso "Informe o fornecedor antes de incluir itens.".
- Linha do complemento mostra "Nota original" (número/item).
- `canIssuePurchaseNfe` e `isPurchaseTaxMode` alinhados ao backend (complemento próprio emite e calcula).
- Detail do complemento sem "Devolver"; Detail da original sem mudança visual (a trava C8 aparece na recusa).
- Armadilhas: `--` em comentário XML; `targetType: 'any'` em enum; Edm.Decimal chega como string; parsear os XML
  de view alterados (ts-typecheck e lint não pegam XML inválido).

## Testes

- Linha: C1–C5, C7, C10; original sem contrato aceita contrato escolhido e recusa linha sem contrato; cópia dos campos do original; troca de produtor no cabeçalho dispara C3; complemento de
  terceiro inalterado (sem vínculo, sem emissão própria recusada pela regra antiga).
- Emissão: `EnsureIssuableType` aceita; tributação ativa; readiness C6; XML com finNFe=2, `NFref` com chaves
  distintas na ordem, itens sem `DFeReferenciado`, qCom/vUnCom 0, vProd = valor, uCom herdada.
- Travas: C8 no cancelamento do documento e da NF-e da original; complemento cancelado não trava; C9.
- Confirmação: firme criado como hoje (regressão do sub-projeto 2).
- EDM: function nova e `ComplementedItemKey` no item.
- `SiagroB1.Fiscal.Tests`: montador com `NfePurpose.Complement`.

## Verificação no navegador (CEAGUI dev) e homologação

1. Entrada própria autorizada de referência: série 9 nº 5 (FTESTE01, homologação).
2. Documento de Entrada → Incluir, Emissão Própria, tipo Complemento de valor, fornecedor FTESTE01 → Incluir
   item → escolher o item da nº 5 (sem contrato) → escolher o contrato CC000002 → valor → salvar. Produto,
   unidade e natureza travados.
3. Confirmar (firme criado) → transmitir em **homologação SEFAZ-SP** → autorizada, finNFe=2 e `NFref` com a chave
   da nº 5 no XML. Se rejeitar, registrar código e motivo.
4. Na nº 5: cancelar → recusa C8 na tela. "Devolver" a nº 5 continua disponível.
5. Stack derrubada por PID.

## Fora

- Complemento de **imposto** (ICMS/IPI, finNFe=2 sem valor de mercadoria) e ajuste (finNFe=3).
- Referenciar nota do produtor (refNFe de terceiro) ou Nota de Produtor modelo 4 (`refNFP`).
- Complemento de venda.
- Complemento em `SAPB1`.
