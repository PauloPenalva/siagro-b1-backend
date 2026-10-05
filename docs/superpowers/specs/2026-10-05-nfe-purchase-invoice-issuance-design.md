# NF-e STANDALONE — NF-e de entrada própria no Documento de Entrada e devolução de compra

**Data:** 2026-10-05
**Branch:** `feature/nfe-purchase-invoice-issuance` (backend e frontend), criado a partir de
`feature/nfe-standalone-issuance` (sub-projeto 2a + devolução de venda, ainda sem merge).
**Depende de:** `2026-10-01-nfe-standalone-taxation-design.md` (SP1: regra de ativação, natureza fiscal com tipo
Entrada/Saída, motor, fotografia na linha), `2026-10-02-nfe-standalone-issuance-design.md` (2a: montador,
assinatura, transmissão, numeração, "emitir é o que confirma") e `2026-10-04-nfe-sales-return-issuance-design.md`
(devolução de venda: `DFeReferenciado`, conferência, natureza de devolução, `nItem` gravado).

É o **sub-projeto 3** previsto no SP1 §3 ("NF-e de entrada de emissão própria — campos fiscais na linha do documento
de entrada, reaproveitando o cálculo do 1 e a transmissão do 2") e a **Fase 2** (camada fiscal) do Documento de
Entrada (`2026-08-06-purchase-invoice-design.md`), restrita à emissão própria.

## 1. O pedido

> "Integrar o documento de entrada com a NF-e. Implementar também devoluções de entrada."

O usuário delegou todas as decisões ("pode tomar as decisões por mim") e revisa o projeto na manhã seguinte. Cada
decisão abaixo traz o motivo, para ser revista.

**O que os dados reais mostram** (snapshot do EfisCloud em `C:\Projetos\SiagroB1\dados-ceagui-efiscloud\`, só
agregado — são dados pessoais): a CEAGUI emitiu **23 NF-e de entrada próprias de compra** entre as 179 autorizadas:

| Campo | Valor nas notas reais |
|---|---|
| `tpNF` / `finNFe` | 0 (entrada) / 1 (normal) |
| CFOP | 1102 (todas dentro do estado, `idDest` 1) |
| natOp | "COMPRA DE MERCADORIA" |
| Destinatário | o produtor/fornecedor (CNPJ; contribuinte ou não contribuinte) |
| ICMS | CST 51 + cBenef `SP053521` |
| PIS/COFINS | CST 74 |
| IBS/CBS | CST 200, cClassTrib 200036 |
| `NFref/refNFe` | 18 das 23 referenciam a **NF-e do próprio produtor** (modelo 55, emitida pelo destinatário) |
| `infCpl` | texto do Funrural ("Contribuição Social denominada Funrural 1,5% = R$ …") ou "Produtor optante pelo recolhimento pela folha de pagamento" |
| Transporte | `modFrete 9`, sem transportadora; `vol` com pesos |
| Pagamento | `cobr` com uma duplicata (o `tPag 05` é o bug do EfisCloud já registrado) |

**Nenhuma devolução de compra** existe no snapshot: a devolução de entrada é capacidade nova, sem caso real para
copiar. As devoluções do snapshot (CFOP 1202) são devoluções de **venda**, já cobertas pelo spec de 04/10.

## 2. Decisões (tomadas por delegação do usuário)

| # | Decisão | Motivo |
|---|---|---|
| D1 | **Duas entregas:** (a) emitir a NF-e de entrada própria (compra, `finNFe 1`, `tpNF 0`) a partir do Documento de Entrada com Emissão **Própria** e tipo Normal; (b) **devolução de compra** (`finNFe 4`, `tpNF 1`) a partir de um Documento de Entrada próprio com NF-e autorizada pelo Siagro, total ou parcial. | (a) é o uso real da CEAGUI (23 notas); (b) é o pedido "devoluções de entrada". |
| D2 | **Origem da devolução de compra: só entrada emitida pelo Siagro.** Documento de terceiro (NF-e do fornecedor) não é devolvido por aqui nesta entrega. | Mesma escolha que o usuário fez para a venda em 04/10 (D1 daquele spec). A devolução precisa reproduzir o imposto da entrada, e o documento de terceiro não tem fotografia de tributos; o `nItem` da nota do fornecedor também não é gravado hoje. Fica como próxima etapa (§15). |
| D3 | **Emitir é o que confirma** também na entrada: na filial com a regra ativa, documento de Emissão Própria (Normal ou devolução de compra) só confirma com NF-e autorizada. Documento de **terceiro** confirma como hoje. | Regra do 2a (D2). O documento de terceiro é a nota do fornecedor: não há o que emitir. |
| D4 | **Natureza de devolução vale nos dois sentidos.** A natureza de **Entrada** (compra) passa a poder apontar uma natureza de devolução de **Saída** (ex.: COMPRA 1102 → DEVOLUÇÃO DE COMPRA 5202), como a de Saída já aponta uma de Entrada. A regra vira "a natureza de devolução tem o sentido oposto". | Reaproveita o vínculo `USAGES.ReturnUsageCode` de 04/10: cada linha da devolução herda sozinha a natureza certa. |
| D5 | **Impostos: calcular e conferir**, como na devolução de venda. A entrada própria é calculada pelo motor com a natureza de Entrada. A devolução de compra é calculada com a natureza de devolução (Saída), **com as UFs da operação original** (fornecedor → filial) e o IBS/CBS pelas alíquotas da **data da entrada**; a conferência recusa se o resultado não reproduzir a tributação da linha comprada (mesmos campos da devolução de venda). | Abordagem A, escolhida pelo usuário em 04/10. Usar as UFs da compra é o que faz a devolução de uma compra interestadual repetir a alíquota creditada (12% de BA→SP, e não 7% de SP→BA). |
| D6 | **Na entrada interestadual a alíquota segue o sentido da mercadoria:** origem = UF do fornecedor, destino = UF da filial. Os parâmetros do motor passam a se chamar `OriginState`/`DestinationState`. | A Resolução do Senado olha origem e destino da mercadoria; na compra a mercadoria vem do fornecedor. Os nomes atuais (`BranchState`/`CustomerState`) induziriam ao erro. A CEAGUI só tem operação interna hoje: nada muda nos números reais. |
| D7 | **NF-e referenciada (nota do produtor):** campo opcional no cabeçalho, `ReferencedAccessKey` (44 dígitos, dígito verificador conferido), que vai no `ide/NFref/refNFe` da entrada. | 18 das 23 notas reais referenciam a NF-e do produtor. Nota de produtor em papel (`refNFP`, modelo 04) fica fora: as 3 notas reais sem chave citam a nota no `infCpl`, que continua livre. |
| D8 | **Pagamento:** o Documento de Entrada ganha `PaymentConditionCode` (padrão = a do fornecedor), obrigatório para emitir a entrada (`cobr` + `pag` pelo `PaymentInstallmentCalculator` do 2a). A devolução de compra sai com `tPag 90`, `vPag 0`, sem `cobr` (rejeição 871). | Mesmo modelo da venda; as notas reais de compra levam duplicata. |
| D9 | **Numeração:** mesma série e sequência da filial que a venda (`NfeNumberReservationService`). | A numeração da NF-e modelo 55 é por emitente e série, para entrada e saída. É o que a CEAGUI faz hoje. |
| D10 | **A filial passa a ser gravada no Documento de Entrada** (`BranchCode`, que existia e nunca era enviado), com padrão = filial da sessão, editável enquanto Pendente e travada depois da emissão. | Sem filial não há regra ativa, emitente nem numeração. Documento antigo sem filial continua como está (só o de Emissão Própria precisa dela para emitir). |
| D11 | **A devolução de compra mora no Documento de Entrada**: `InvoiceType = Return` + `IsNfeReturn = true` + `IssuerType = Own`, apontando a entrada de origem por `PurchaseInvoiceOriginKey` / `PurchaseInvoiceItemOriginKey`. | Simetria exata com 04/10, que o usuário aprovou: a devolução de venda (uma NF-e de entrada) mora no Documento de Saída, junto da origem. |
| D12 | **A devolução de compra não mexe na entrada de origem.** O saldo devolvível é calculado na hora (comprado − devolvido em devoluções não canceladas, Pendentes inclusive); a origem não ganha coluna nova nem muda de status. A confirmação da devolução só transiciona o status. | O Documento de Entrada ainda não tem efeito (contrato, estoque, financeiro: Fase 3). Guardar quantidade devolvida na origem seria estado sem consumidor. |
| D13 | **Arquitetura: um pipeline de NF-e, dois documentos.** O 2a é generalizado por duas interfaces (`INfeDocument` no cabeçalho, `INfeTaxedLine` na linha) e por classes base de emissão, consulta e tratamento de retorno. Os serviços do documento de saída mantêm nome, construtor e comportamento; os do de entrada são subclasses finas. | A lógica sensível do 2a (reservar e salvar antes de assinar, "Em processamento" gravado antes do envio, 539, duplicidade por digest, confirmação depois de salvar a autorização) não pode existir em duas cópias. |
| D14 | `NfePurpose { Sale, Return }` vira **`{ Normal, Return }`** (`finNFe`), e nasce **`NfeDirection { Outgoing, Incoming }`** (`tpNF`). Hoje `Return` implica `tpNF 0`; com a devolução de compra (`tpNF 1`, `finNFe 4`) os dois eixos se separam. | Uma entrada de compra não é "Sale"; e as quatro combinações agora existem. |
| D15 | **`indFinal = 1` só na saída (`tpNF 1`) para destinatário não contribuinte.** | A regra 696 da SEFAZ vale para a saída. Na entrada o destinatário é o vendedor, nunca consumidor final. Única mudança de comportamento para o já existente: a devolução de venda (entrada) para cliente não contribuinte passa a sair com `indFinal 0` — não há nenhuma em produção. |
| D16 | **Sem campos de volume no Documento de Entrada**; os pesos (que já existem) são obrigatórios para emitir, com bruto ≥ líquido. | As notas reais levam pesos. Quantidade/espécie/marca de volume entraram na venda a pedido; na entrada não houve pedido (YAGNI). |
| D17 | **Documento de terceiro e "Devolução do cliente" ficam exatamente como hoje**: sem cálculo, sem guarda nova, sem campo novo obrigatório. | Isolamento: a camada fiscal da entrada de terceiro (escrituração) é outro trabalho. |
| D18 | **Efeito no contrato de compra fica fora** (o SP1 §3 cita "efeito da natureza de entrada em contrato de compra"). | É a Fase 3 do Documento de Entrada; hoje a amarração ao contrato é só referência, por decisão de 07/08. |

## 3. Escopo

**Entra:**
- campos fiscais na linha do Documento de Entrada (a mesma fotografia da linha de saída) e cálculo pelo motor;
- filial, condição de pagamento, NF-e referenciada e estado da NF-e no cabeçalho do Documento de Entrada;
- "Emitir NF-e", "Consultar situação", "Concluir confirmação", DANFE e XML da NF-e de entrada própria;
- devolução de compra: "Devolver" no detalhe da entrada própria autorizada, criação total ou parcial, cálculo e
  conferência, emissão (`finNFe 4`, `tpNF 1`, `DFeReferenciado` por item), consulta, DANFE e XML;
- generalização do pipeline do 2a (D13) sem mudar o comportamento do documento de saída;
- natureza de devolução nos dois sentidos (D4).

**Não entra:**
- devolução de documento de **terceiro** (D2) e qualquer camada fiscal na entrada de terceiro (D17);
- cancelamento, CC-e e inutilização (sub-projeto 2b, para os dois documentos);
- nota de produtor em papel (`refNFP`), Funrural calculado (o texto continua digitado), contingência;
- efeito em contrato, estoque fiscal ou financeiro (D18; as flags "Movimenta estoque" e "Gera financeiro" são
  copiadas para a linha, sem efeito, como no SP1 D14);
- numerador automático do número interno (`InvoiceNumber` continua digitado, Fase 3);
- volume (D16).

## 4. Isolamento

Tudo abaixo só vale com a **regra ativa** (`TaxCalculationGate.IsActiveAsync(BranchCode)`: `Erp == STANDALONE`
**e** `Branch.IssuesNfe`) **e** documento de **Emissão Própria**. Yokotobi (SAPB1) e MH Agro (chave desligada) não
veem botão novo nem regra nova; documento de terceiro nunca entra na regra.

| Onde | Comportamento |
|---|---|
| Documento de Entrada, Própria, regra ativa | Natureza por linha obrigatória (só Entrada), tributos calculados e travados, condição de pagamento, NF-e referenciada; número/série/chave vêm da emissão; "Emitir NF-e" no lugar de "Confirmar". |
| Documento de Entrada, Própria, regra inativa | Como hoje: número/série/chave digitados, sem cálculo, "Confirmar". |
| Documento de Entrada, terceiro | Como hoje, em qualquer modo. |
| Campo "Filial" | Aparece para todos (é dado do documento), padrão = filial da sessão. |
| Campos novos de cabeçalho/linha | Visíveis só no modo NF-e (`ui>/taxLocked` + Emissão Própria). |
| Devolução de compra manual | Na filial com a regra ativa, documento Própria + tipo Devolução só nasce pelo "Devolver" (§9.2). Fora dela, como hoje. |

## 5. Modelo de dados

### 5.1 `PURCHASE_INVOICES` — colunas novas

| Coluna | Tipo | Uso |
|---|---|---|
| `PaymentConditionCode` | INT NULL (sem FK, como na venda) | condição de pagamento; padrão = do fornecedor na criação |
| `ReferencedAccessKey` | VARCHAR(44) NULL | NF-e referenciada (nota do produtor), D7 |
| `IsNfeReturn` | BIT NOT NULL DEFAULT 0 | devolução de compra criada pelo "Devolver" (só o serviço do §9.2 grava `true`) |
| `NfeStatus` | INT NOT NULL DEFAULT 0 | enum `NfeStatus` do 2a |
| `NfeEnvironment` | INT NULL | |
| `NfeRandomCode` | VARCHAR(8) NULL | `cNF` |
| `NfeProtocol` | VARCHAR(20) NULL | |
| `NfeAuthorizedAt` | DATETIME2 NULL | |
| `NfeStatusCode` | VARCHAR(4) NULL | último `cStat` |
| `NfeStatusReason` | VARCHAR(500) NULL | último `xMotivo` ou mensagem local |
| `NfeConfirmationError` | VARCHAR(500) NULL | autorizada mas a confirmação falhou |

`BranchCode` já existe (herdado de `DocumentEntity`).

### 5.2 `PURCHASE_INVOICES_ITEMS` — a fotografia (igual à da linha de saída, SP1 §5.6)

`UsageCode` INT NULL, `UsageName` VARCHAR(200), `Cfop` VARCHAR(4), `Ncm` VARCHAR(8), `CstIcms` VARCHAR(3),
`IcmsBase`/`IcmsValue`/`IcmsOperationValue`/`IcmsDeferredValue` DECIMAL(18,2), `IcmsRate`/`IcmsBaseReduction`/
`IcmsDeferral` DECIMAL(7,4), `IcmsBenefitCode` VARCHAR(10), `CstPis`/`CstCofins` VARCHAR(3),
`PisBase`/`PisValue`/`CofinsBase`/`CofinsValue` DECIMAL(18,2), `PisRate`/`CofinsRate` DECIMAL(7,4), `GoodsOrigin`
TINYINT NULL, `IbsCbsCst` VARCHAR(3), `IbsCbsClassCode` VARCHAR(6), `IbsCbsBase`/`CbsValue`/`IbsStateValue`/
`IbsMunicipalValue` DECIMAL(18,2), `CbsRate`/`CbsRateReduction`/`IbsStateRate`/`IbsMunicipalRate`/`IbsRateReduction`
DECIMAL(7,4), `MovesFiscalInventory`/`CreatesFinancialDocument` BIT NOT NULL DEFAULT 0, `NfeItemNumber` INT NULL.

Valores com `DEFAULT 0` como na linha de saída. `[NotMapped]`: `TotalTaxes`, `TotalIbsCbs` (com `AddProperty` no EDM,
como na saída); no cabeçalho `TotalInvoiceTaxes` e `TotalInvoiceIbsCbs`.

Sem centro de custo e conta contábil nesta entrega (a linha de entrada não os tinha; ficam com o financeiro).

### 5.3 `PURCHASE_INVOICE_NFE_XMLS` (nova)

Espelho de `SALES_INVOICE_NFE_XMLS`: `Key` GUID PK, `PurchaseInvoiceKey` FK (cascade), `Kind` INT (o mesmo enum
`SalesInvoiceNfeXmlKind`: Signed, Authorized, Denied — renomeá-lo para `NfeXmlKind` faz parte de D13), `Xml`
NVARCHAR(MAX), `CreatedAt`. Índice em (`PurchaseInvoiceKey`, `Kind`).

### 5.4 Interfaces de domínio (D13)

- **`INfeDocument`** (cabeçalho): `Key`, `BranchCode`, `CardCode`, `InvoiceStatus`, `TaxDocumentNumber`,
  `TaxDocumentSeries`, `ChaveNFe`, `NfeStatus`, `NfeEnvironment`, `NfeRandomCode`, `NfeProtocol`, `NfeAuthorizedAt`,
  `NfeStatusCode`, `NfeStatusReason`, `NfeConfirmationError`. Implementada por `SalesInvoice` e `PurchaseInvoice`.
- **`INfeTaxedLine`** (linha): `Key`, `ItemCode`, `ItemName`, `UnitOfMeasureCode`, `Quantity`, `UnitPrice`, `Total`,
  `UsageCode`, `UsageName`, os campos da fotografia e `NfeItemNumber`. Implementada por `SalesInvoiceItem` e
  `PurchaseInvoiceItem`.

As propriedades já existem nas entidades com os mesmos nomes (a linha de entrada as ganha no §5.2); a interface não
muda o mapeamento do EF nem o EDM.

## 6. Cálculo

### 6.1 Peças compartilhadas (extraídas do `SalesInvoicesTaxApplyService`, sem mudar o comportamento da saída)

- **`TaxLineCalculator`** (`Services/Taxes`): dado natureza (`UsageModel`), produto, sentido da natureza esperado,
  UF de origem e de destino, regime, data das alíquotas IBS/CBS e valor, devolve CFOP + `TaxCalculationResult`.
  Concentra `ResolveCfop`, `ResolveIcmsRule`, `ResolvePisCofinsRule`, `ResolveIbsCbsAsync`, `LoadProductAsync` e as
  mensagens de hoje.
- **`TaxSnapshot`** (genérico sobre `INfeTaxedLine`): `Write`, `LockedProperties`, `RestoreLocked` (hoje
  `SalesInvoiceTaxSnapshot`).
- **`NfeReturnConference`** (genérico): conferência linha devolvida × linha de origem, com o rótulo da operação
  ("venda"/"compra") na mensagem (hoje `SalesInvoiceNfeReturnConference`).
- **`NfeItemNumbering`** (genérico): `Ordered`, `Renumber`, `OriginNumber` (hoje `SalesInvoiceNfeItemNumbering`).

### 6.2 `PurchaseInvoicesTaxApplyService`

Age só com a regra ativa, documento Pendente e **Emissão Própria** em dois casos:

| Caso | Natureza esperada | CFOP | UFs (origem → destino) | Data do IBS/CBS | Conferência |
|---|---|---|---|---|---|
| Entrada própria (`Normal`) | Entrada | `CfopIncomingInState`/`OutState` | fornecedor → filial | `IssueDate` | — |
| Devolução de compra (`Return` + `IsNfeReturn`) | Saída | `CfopOutgoingInState`/`OutState` | fornecedor → filial (as da compra) | `IssueDate` da entrada de origem | contra a linha comprada |

- `InState` = UF da filial == UF do fornecedor (endereço de faturamento, mesma resolução da venda).
- Linha sem natureza na entrada própria ⇒ erro "O item {ItemCode} está sem natureza de operação." (não há natureza
  padrão de entrada: `IsDefault` é proibido em natureza de Entrada pelo SP1). Linha da devolução sem natureza ⇒
  "O item {ItemCode} da devolução está sem natureza de devolução."
- Natureza de sentido errado ⇒ "A natureza de operação {Name} é de saída e não pode ser usada na entrada." /
  "A natureza de operação {Name} é de entrada e não pode ser usada na devolução de compra."
- Produto obrigatório com NCM e origem (mensagens do SP1); linha sem produto ⇒ "Informe o produto do item {n}."
- Conferência: os mesmos campos da devolução de venda (CST/alíquota/redução/diferimento/cBenef do ICMS; CST,
  classificação, alíquotas e reduções do IBS/CBS); mensagem
  "Item {ItemCode}: a natureza de devolução {Name} não reproduz a tributação da compra — {campo}: compra {x}, devolução {y}."

**Quando calcula** (espelho do SP1 §7.1): `PurchaseInvoicesCreateService`, `PurchaseInvoicesItemsCreateService`,
`PurchaseInvoicesItemsUpdateService` e `PurchaseInvoicesUpdateService` (recalcula todas as linhas se mudar
`IssueDate`, `CardCode` ou `BranchCode`, e as linhas que o PATCH do cabeçalho trouxer). A confirmação não recalcula.

## 7. Travas e guardas do Documento de Entrada

Espelho de `SalesInvoiceNfeLock` (2a §9.4), num `PurchaseInvoiceNfeLock`, sobre uma base comum de comparação de
campos:

- **Criação:** campos de emissão sempre zerados (`NfeStatus = None` etc.), `IsNfeReturn` só pelo serviço do §9.2.
- **Cabeçalho fiscal** (`BranchCode`, `CardCode`, `IssueDate`, `InvoiceType`, `IssuerType`, `TruckingCompanyCode`,
  `TruckCode`, `FreightTerms`, `PaymentConditionCode`, `GrossWeight`, `NetWeight`, `TaxPayerComments`,
  `ReferencedAccessKey`): imutável com NF-e autorizada; nada editável em processamento.
- **Linha fiscal** (`ItemCode`, `UnitOfMeasureCode`, `Quantity`, `UnitPrice`, `UsageCode`): idem; incluir/excluir
  linha recusado em processamento ou autorizada.
- **Campos da emissão** (situação, protocolo, retorno, cNF, ambiente) nunca escritos pela API; número, série e chave
  também não depois da primeira emissão (ou com número reservado).
- **Fotografia** travada e sobrescrita pelo cálculo (o "sobrescreve, não recusa" do SP1 §7.2).
- **Excluir:** recusado com NF-e em processamento, autorizada ou denegada. **Cancelar:** recusado em processamento e
  com NF-e autorizada ("o cancelamento precisa ser feito na SEFAZ, recurso da próxima etapa").
- **Confirmar** (`PurchaseInvoicesConfirmService`): com a regra ativa, documento de Emissão Própria só confirma com
  `NfeStatus = Authorized` — "Na filial que emite NF-e pelo Siagro, confirme emitindo a NF-e."
- **Estornar:** sem mudança (só transiciona o status; os campos fiscais continuam travados pela NF-e autorizada).
- **NF-e referenciada:** se informada, 44 dígitos, dígito verificador válido e diferente da `ChaveNFe` do próprio
  documento — "A chave da NF-e referenciada é inválida." Só no documento de Emissão Própria Normal.
- **Devolução manual:** com a regra ativa, `IssuerType = Own` + `InvoiceType = Return` sem `IsNfeReturn` ⇒ "Na filial
  que emite NF-e pelo Siagro, a devolução de compra é feita pelo botão Devolver, no detalhe do documento de entrada."

## 8. A NF-e de entrada própria

### 8.1 `SiagroB1.Fiscal` (D14, D15)

- `NfeIssueInput.Direction` (`NfeDirection`, padrão `Outgoing`) ⇒ `ide/tpNF` (1 / 0).
- `NfeIssueInput.Purpose` (`NfePurpose { Normal, Return }`) ⇒ `ide/finNFe` (1 / 4). Deixa de decidir o `tpNF`.
- `indFinal` = 1 só com `Direction = Outgoing` e destinatário `NonTaxpayer`.
- `NFref`: continua a regra de 04/10 — sai quando há `ReferencedKeys` e **nenhum** item tem `Reference` (rejeição 1010).

| Documento | `Direction` | `Purpose` | `NFref` | `DFeReferenciado` |
|---|---|---|---|---|
| Venda | Outgoing | Normal | — | — |
| Devolução de venda | Incoming | Return | — | por item (chave e `nItem` da venda) |
| **Entrada própria** | **Incoming** | **Normal** | `ReferencedAccessKey`, se houver | — |
| **Devolução de compra** | **Outgoing** | **Return** | — | por item (chave e `nItem` da entrada) |

### 8.2 Montador e prontidão (`Services/Nfe`)

- `NfeIssueInputAssembler.Build(PurchaseInvoice, …)`: o destinatário é o fornecedor (`CardCode`); `natOp` da natureza
  da 1ª linha; `infCpl` = textos padrão das naturezas + `TaxPayerComments` (na devolução, a referência
  "Devolução da NF-e nº {nNF}, série {serie}, de {dd/MM/aaaa}, chave {chave}." primeiro); sem `entrega`; sem volume
  (só pesos); sem `infAdFisco`; `Payment` pela condição (entrada) ou `tPag 90` (devolução).
- `NfeReadinessValidator.ValidateAsync(PurchaseInvoice)`: as mesmas conferências do 2a sobre um núcleo comum, com
  "Fornecedor" no lugar de "Cliente"; produto e unidade em toda linha; CFOP × destino com 1xxx/2xxx na entrada e
  5xxx/6xxx na devolução; condição de pagamento só na entrada; pesos obrigatórios; na devolução, a entrada de origem
  autorizada, confirmada, com chave e o `nItem` de cada item devolvido.
- `TotalDocumentValue` do documento próprio recebe o `vNF` (soma dos itens) na emissão, antes de assinar.

### 8.3 Emissão, consulta, conclusão, DANFE e XML

Mesmo fluxo do 2a (§9.2–§9.3), pelas classes base de D13 (§10):
`PurchaseInvoicesIssueNfe(Key)`, `PurchaseInvoicesConsultNfe(Key)`, `PurchaseInvoicesCompleteNfeConfirmation(Key)`
(actions), `PurchaseInvoicesNfeXml(Key)` (função, `<chave>-procNFe.xml`) e, no Reports,
`POST /reports/Danfe/purchase-invoices/{key}/print`. Pré-condições próprias da entrada: regra ativa, Emissão
Própria, Normal ou devolução de compra, Pendente, `IssueDate` = hoje em Brasília, toda linha calculada; na
devolução, o saldo devolvível conferido **antes** de reservar o número. Autorizada ⇒ `PurchaseInvoicesConfirmService`.

## 9. Devolução de compra

### 9.1 Natureza (D4)

`UsageService` (create/update): "só natureza de Saída pode ter natureza de devolução" passa a ser "a natureza de
devolução tem o sentido oposto ao desta natureza" — Saída aponta Entrada (como hoje), Entrada aponta Saída. Continuam:
existe, ativa, não aponta para si mesma, e a natureza usada como devolução de outra não pode trocar de sentido.

### 9.2 Criar (`PurchaseInvoicesNfeReturnCreateService`)

Action `POST odata/PurchaseInvoicesCreateNfeReturn` com `{ Key, OriginItemKeys[], Quantities[], Reason }` (arrays
paralelos, como a de venda) → `Guid` da devolução. Função `GET odata/PurchaseInvoicesNfeReturnableItems(Key=…)`:
`OriginItemKey`, `ItemCode`, `ItemName`, `PurchasedQuantity`, `ReturnedQuantity`, `Returnable`.

Validação antes de qualquer escrita:
1. a filial da entrada emite NF-e pelo Siagro;
2. a entrada é Emissão Própria, `Normal`, `Confirmed`, `NfeStatus = Authorized`, com `ChaveNFe` de 44 dígitos;
3. motivo informado;
4. ao menos um item com quantidade > 0, cada uma ≤ saldo devolvível (sem tolerância);
5. cada linha comprada tem natureza com `ReturnUsageCode` — "A natureza {Code} {Name} da compra não tem natureza de
   devolução cadastrada.";
6. cada linha comprada tem o `nItem` da NF-e de entrada (gravado na emissão; regra de `OriginNumber`).

Montagem: cópia do cabeçalho (fornecedor, filial, transporte), `IssueDate`/`PostingDate` = hoje, `IssuerType = Own`,
`InvoiceType = Return`, `IsNfeReturn = true`, `PaymentConditionCode = null`, `ReferencedAccessKey = null`,
`PurchaseInvoiceOriginKey` = entrada; uma linha por item devolvido (produto, unidade, preço da compra, quantidade
devolvida, `PurchaseInvoiceItemOriginKey`, `UsageCode` = `ReturnUsageCode` da natureza da linha comprada); pesos =
os da entrada × (quantidade devolvida / quantidade comprada), 3 casas; `Comments` = "Devolução da NF-e {nNF} série
{serie}. Motivo: {Reason}". Grava pelo `PurchaseInvoicesCreateService` (que calcula e confere, §6.2), sem confirmar.

### 9.3 Travas da devolução de compra

Linha: só `Quantity` editável (com o saldo do §9.2); o resto volta ao gravado. Cabeçalho: fornecedor, filial, tipo,
emissão e `IsNfeReturn` travados; datas, transporte, pesos e observações editáveis. Incluir linha recusado. Cancelar
ou excluir enquanto Pendente: permitido (a origem não foi tocada, D12).

## 10. Generalização do pipeline (D13)

| Hoje (só saída) | Depois |
|---|---|
| `SalesInvoicesNfeIssueService` (fluxo inteiro) | `NfeIssueServiceBase<TDocument>` com o fluxo e ganchos (carregar, pré-condições do tipo, data do documento, prontidão, montagem, gravar XML, XMLs assinados); `SalesInvoicesNfeIssueService` e `PurchaseInvoicesNfeIssueService` herdam |
| `SalesInvoiceNfeResultHandler` | `NfeResultHandlerBase<TDocument>` (ganchos: gravar XML, confirmar, reler) + as duas subclasses |
| `SalesInvoicesNfeConsultService`, `…CompleteConfirmationService`, `…XmlDownloadService` | base genérica + subclasses finas |
| `NfeIssueOutcomeDto.From(SalesInvoice)` | `From(INfeDocument)` |
| `NfeReadinessValidator.ValidateAsync(SalesInvoice)` | núcleo neutro + `ValidateAsync(SalesInvoice)` (inalterado por fora) e `ValidateAsync(PurchaseInvoice)` |
| `NfeIssueInputAssembler.Build(SalesInvoice, …)` | núcleo neutro + `Build(SalesInvoice, …)` e `Build(PurchaseInvoice, …)` |

Regra de ouro: **os testes do documento de saída passam sem alteração de asserção**. Só mudam as fábricas de teste
(se um construtor mudar) e as renomeações mecânicas de identificador (`SalesInvoiceNfeXmlKind` → `NfeXmlKind`,
`NfePurpose.Sale` → `NfePurpose.Normal`, `SalesInvoiceTaxSnapshot` → `TaxSnapshot` etc.). A única asserção que muda
de propósito é a da D15 (`indFinal` da devolução de venda para não contribuinte), se houver teste que a fixe.

## 11. Telas (frontend)

| Tela | Mudança |
|---|---|
| Documento de Entrada — formulário (`fragments/Form`) | **Filial** (Select, padrão = filial da sessão). No modo NF-e (`ui>/taxLocked` e Emissão Própria): número/série/chave somente leitura; **Condição de pagamento** (value help); **NF-e referenciada** (44 dígitos); "Informações complementares" editável (é o `infCpl`). Na devolução de compra, fornecedor/filial/tipo/emissão travados. |
| Documento de Entrada — itens (`fragments/Items`) | No modo NF-e: coluna **Natureza** (value help só de naturezas de Entrada ativas; somente leitura na devolução), coluna **CFOP** e botão do **diálogo fiscal** (somente leitura, fragmento novo com os campos da fotografia). Na devolução, só Quantidade editável e sem incluir/excluir. |
| Documento de Entrada — detalhe | Painel **NF-e** (situação, chave, protocolo, último retorno); **Emitir NF-e**, **Consultar situação**, **Concluir confirmação**, **DANFE**, **XML**; "Confirmar" oculto no modo NF-e; **Devolver** (Própria, Normal, Confirmada, Autorizada) com o diálogo de quantidades e motivo (o de 04/10, rótulo "Comprado"). |
| Documento de Entrada — lista | Coluna **Situação NF-e** (STANDALONE). |
| Natureza — formulário | "Natureza de devolução" visível nos dois sentidos; value help do sentido oposto. |
| `model/ServerRoutes.ts` | rotas novas do §8.3 e §9.2. |

## 12. Migration

Uma, aditiva, no `AppDbContext` (`AddPurchaseInvoiceNfe`): colunas do §5.1 e §5.2, tabela do §5.3. Nada no comum
(sem menu novo). Nenhum backfill.

## 13. Testes e verificação

TDD com RED visto antes do GREEN.

**`SiagroB1.Fiscal.Tests`:** entrada própria (`tpNF 0`, `finNFe 1`, `NFref` com a chave do produtor, CFOP 1102,
`indFinal 0` com destinatário não contribuinte, `cobr`/`pag` da condição); devolução de compra (`tpNF 1`, `finNFe 4`,
`DFeReferenciado` por item, sem `NFref`, `tPag 90`, sem `cobr`, `indFinal 1` com não contribuinte); venda e devolução
de venda idênticas ao XML de antes (exceto D15); as duas novas assinadas e validadas no XSD oficial; motor com UFs
de origem/destino (entrada BA→SP = 12%).

**`SiagroB1.Application.Tests`:** cálculo da entrada (CFOP de entrada dentro/fora, sentido da natureza, UFs da
entrada, sem natureza, sem produto); isolamento (regra inativa, terceiro, devolução do cliente: nada muda);
recálculo ao mudar data/fornecedor/filial; travas (§7) e guardas de confirmação/cancelamento/exclusão; NF-e
referenciada; devolução manual recusada; emissão (cada retorno do 2a pela base comum, `TotalDocumentValue`, número
da mesma sequência da venda, `nItem` gravado); consulta e conclusão; devolução (cada recusa do §9.2, total e parcial,
natureza herdada, pesos proporcionais, conferência divergindo, UFs e data da compra, saldo antes da reserva,
autorizada ⇒ confirmada, origem intocada); natureza de devolução nos dois sentidos; **toda a suíte da saída verde
sem mudar asserções**.

**Na tela (stack `ceagui`, Playwright):** natureza de compra (Entrada, 1102) ligada a uma de devolução de compra
(Saída, 5202); Documento de Entrada Própria com fornecedor de teste; tributos no diálogo fiscal; emissão em
**homologação** com dados fictícios (fornecedor de teste); DANFE e XML; "Devolver" parcial; emissão da devolução em
homologação; conferir a VC02-14 (`DFeReferenciado`) no XML autorizado. Se a emissão for barrada pelo ambiente, a
verificação para no XSD e isso é dito.

Gates: `dotnet build` sem erro; Fiscal e Application verdes; `yarn ts-typecheck`, `yarn lint`, `ui5lint` sem
aumento sobre a base (920); QUnit verde.

## 14. Riscos

- **Refatoração do 2a (D13):** é o maior risco de regressão. Mitigação: a suíte do documento de saída (emissão,
  consulta, retorno, devolução) como rede, sem mudar asserção, e a emissão de venda refeita na tela.
- **`indFinal` (D15):** muda a devolução de venda para não contribuinte (nenhuma em produção).
- **NF-e referenciada de outro ambiente:** em homologação, uma chave de produção como `refNFe` pode ser recusada;
  o teste usa entrada sem referência e, se possível, uma chave de homologação.
- **VC02-14 na devolução de compra:** mesma regra e mesmas datas da devolução de venda (produção em 03/11/2026).
- **VC02-50 (rejeição 1194) na devolução de entrada própria:** na verificação em homologação (05/10/2026, 03h) a
  devolução da entrada nº 5 foi rejeitada com "Destinatário da NF-e deve ser igual ao Emitente da NF referenciada",
  porque a referenciada é a nossa própria entrada. A NT 2025.002 v1.52 (01/10/2026) corrigiu a VC02-50 para valer só
  na "devolução de saída que referencia nota de saída" (a entrada própria tem `tpNF 0`), com entrada em homologação
  em 05/10/2026 e em produção em 03/11/2026 — a SP ainda rodava a redação antiga. O XML não mudou; reemitir a devolução
  (série 9 nº 6, número reservado). Se a SEFAZ continuar rejeitando depois da correção, a devolução de entrada própria
  terá de referenciar a NF-e do produtor (`ReferencedAccessKey`), o que exige o `nItem` daquela nota.
- **À vista sem `cobr` (rejeição 853):** com condição à vista (`indPag 0`) o montador não manda `cobr`; achado na
  emissão da entrada em homologação e corrigido no montador comum (vale também para a venda).
- **Produtor não contribuinte com IE:** o EfisCloud manda IE com indicador 9; o Siagro só manda IE com indicador 1
  (regra do 2a). Se a SEFAZ exigir, o cadastro do fornecedor precisa do indicador 1.

## 15. Próximas etapas (fora daqui)

Devolução de documento de terceiro (ler a tributação e o `nItem` do XML do fornecedor); nota de produtor em papel
(`refNFP`); Funrural calculado; efeito da entrada no contrato de compra (Fase 3); 2b (cancelamento, CC-e,
inutilização) para os dois documentos.
