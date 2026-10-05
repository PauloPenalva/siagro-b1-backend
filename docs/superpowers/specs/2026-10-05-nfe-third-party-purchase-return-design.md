# Devolução de compra de Documento de Entrada de terceiro (NF-e STANDALONE, CEAGUI) — design

Data: 05/10/2026. Branch `feature/nfe-third-party-purchase-return` nos dois repos, criado de
`feature/nfe-purchase-invoice-issuance` (sem merge em main; mesclar depois dele).
Base: spec `2026-10-05-nfe-purchase-invoice-issuance-design.md` (entrada própria e devolução de compra), cuja §15
deixou para depois a "devolução de documento de terceiro (ler a tributação e o `nItem` do XML do fornecedor)".

## 1. O pedido

"Implementar a devolução de entrada de terceiro." Na filial que emite NF-e pelo Siagro, o Documento de Entrada
de terceiro (a NF-e que o fornecedor emitiu para nós) hoje não tem como ser devolvido: o "Devolver" só existe para
a entrada emitida pelo Siagro, e a devolução própria manual é recusada. A devolução é uma NF-e PRÓPRIA de saída
(`tpNF 1`, `finNFe 4`) para o fornecedor, que referencia a nota dele item a item (`DFeReferenciado`: chave +
`nItem`). O usuário completou: **"os tributos na saída/devolução devem espelhar a entrada."**

Sucesso: uma entrada de terceiro importada pelo XML ganha o "Devolver"; a devolução, total ou parcial, é emitida e
autorizada em homologação, com a tributação conferida contra a nota do fornecedor.

## 2. Decisões (do usuário, em 05/10)

| # | Decisão | Motivo |
|---|---|---|
| D1 | **Tributação: motor + conferência pelo XML.** O motor calcula a devolução pela natureza de devolução; a conferência compara o resultado com a tributação da linha de entrada (lida do XML do fornecedor) e recusa se não espelhar, nomeando item e campo. | Um modelo de imposto só (o mesmo da devolução da entrada própria); o XML é a régua. "Espelhar a entrada" é garantido pela recusa. |
| D2 | **Origem: importado E digitado.** Documento importado pelo XML traz `nItem` e tributação de cada linha. Documento digitado à mão (ou linha incluída depois da importação) também é devolvível: o operador informa o `nItem` no diálogo, e a linha sai **sem conferência** (não há com o que comparar). | Cobre nota recebida sem arquivo. O risco (nItem errado) só aparece na SEFAZ. |
| D3 | **Natureza: padrão da filial.** A filial ganha "Natureza de devolução de compra de terceiro" (natureza de Saída), aplicada sozinha a todas as linhas; o operador não escolhe. | Menos cliques. Fornecedor com tributação diferente da natureza é recusado pela conferência (D1) — aí troca-se a configuração. |
| D4 | **A tributação do fornecedor vai para a fotografia fiscal da linha de entrada** (os mesmos campos da entrada própria: CFOP, NCM, origem, ICMS, PIS/COFINS, IBS/CBS) e o `nItem` para `NfeItemNumber`. Em documento de terceiro nada disso é recalculado: é o registro da nota do fornecedor. | Reaproveita a conferência existente (`NfeReturnConference`), que compara linha de devolução × linha de origem. |
| D5 | **O servidor é a fonte da tributação do fornecedor:** ao gravar um documento de terceiro com o XML guardado (`XmlData`), cada linha com `NfeItemNumber` recebe a tributação do `det` daquele `nItem`, lida do XML no servidor. O cliente só carrega o `nItem` (vindo do rascunho da importação). | Não confia em imposto vindo da tela; regravar é idempotente. |
| D6 | **O `nItem` digitado no "Devolver" fica na linha de entrada** (`NfeItemNumber`), para as próximas devoluções parciais. É a única escrita na origem (o resto da origem continua intocado, como na D12 da spec anterior). | O `nItem` é identidade da linha na nota do fornecedor, não dado da devolução. |
| D7 | **Fora do "Devolver": IPI e ICMS-ST.** Linha de entrada cujo `det` no XML tem IPI (`vIPI > 0`) ou ST (`vICMSST > 0`) é recusada antes de criar a devolução. | O montador do Siagro não emite IPI (`vIPIDevol`) nem ICMS-ST. |
| D8 | **Fornecedor do Simples Nacional fica fora na prática:** o XML traz CSOSN, a devolução da filial de regime normal sai com CST, e a conferência recusa ("CST do ICMS: compra 101, devolução 00"). Registrado como limite conhecido, sem tratamento especial. | Exigiria regra própria de crédito do Simples; não há caso real conhecido. |

## 3. Escopo

**Dentro:** leitura completa do `det` do XML do fornecedor (um leitor só, usado pela importação, pela gravação e
pelo "Devolver"); `nItem` e tributação do fornecedor na linha de entrada; natureza padrão na filial; "Devolver" em
entrada de terceiro (total ou parcial), com `nItem` digitável onde faltar; cálculo pela natureza padrão com as UFs
e a data da entrada; conferência contra a linha importada; emissão, consulta, DANFE e XML pelo pipeline existente.

**Fora:** IPI e ICMS-ST (D7); regra de devolução para fornecedor do Simples (D8); tabela de-para de produto do
fornecedor (a importação continua copiando `cProd` para `ItemCode`; o operador ajusta o produto na entrada antes
de devolver); exibir a tributação do fornecedor no diálogo "Tributos do item" do documento de terceiro;
reprocessar documentos de terceiro antigos (a CEAGUI ainda não usa o Siagro em produção).

## 4. Isolamento

Tudo só vale na filial com a regra ativa (`TaxCalculationGate.IsActiveAsync(BranchCode)`). Yokotobi (SAPB1) e
MH Agro não veem nada novo. Na filial com a regra ativa:

| Onde | Comportamento |
|---|---|
| Importar XML (qualquer filial) | O rascunho passa a trazer o `nItem` de cada linha. Inofensivo fora da filial com NF-e. |
| Gravar documento de terceiro com `XmlData` | Linhas com `NfeItemNumber` recebem a tributação do fornecedor (D5). Não muda nenhum valor que o operador digita (quantidade, preço, produto). |
| Documento de terceiro digitado | Como hoje. Ganha `nItem` só se o operador o informar no "Devolver". |
| Devolução do cliente (De terceiro + Devolução) | Como hoje: não é devolução de compra, não tem "Devolver". |
| Entrada própria e sua devolução | Como hoje (spec anterior). |

## 5. Modelo de dados

### 5.1 `BRANCHS` — coluna nova

| Coluna | Tipo | Uso |
|---|---|---|
| `ThirdPartyPurchaseReturnUsageCode` | INT NULL, sem FK (como as demais referências a natureza) | Natureza de devolução de compra de terceiro (D3). Precisa ser de Saída e ativa. |

### 5.2 `PURCHASE_INVOICES_ITEMS` — sem coluna nova

Reaproveita `NfeItemNumber` (o `nItem` do fornecedor, D4/D6) e a fotografia fiscal que já existe (`Cfop`, `Ncm`,
`GoodsOrigin`, `CstIcms`, `IcmsBase`, `IcmsBaseReduction`, `IcmsRate`, `IcmsValue`, `IcmsDeferral`,
`IcmsOperationValue`, `IcmsDeferredValue`, `IcmsBenefitCode`, PIS/COFINS, IBS/CBS). Em documento de terceiro
`UsageCode` continua nulo: a natureza é da devolução, não da compra.

### 5.3 Contratos

- `PurchaseInvoiceDraftItemDto` (rascunho da importação) ganha `NfeItemNumber`.
- `PurchaseInvoiceNfeReturnItem(Guid OriginItemKey, decimal Quantity, int? ItemNumber)` — `ItemNumber` só vale
  para linha de origem sem `NfeItemNumber`.
- Action `PurchaseInvoicesCreateNfeReturn` ganha `CollectionParameter<int>("ItemNumbers")`, paralela a
  `OriginItemKeys`/`Quantities`, com `0` = não informado. **Sempre enviada** pela tela (parâmetro declarado que
  falta faz o `ODataActionParameters` chegar nulo).
- Função `PurchaseInvoicesNfeReturnableItems` ganha `ItemNumber` por linha (o `NfeItemNumber` da origem, nulo
  quando falta).

## 6. Leitor do XML do fornecedor

`SupplierNfeXmlReader` (Application, `Services/PurchaseInvoices`), lido com `XDocument` como a importação de hoje
(sem Zeus). Entrada: o XML (`nfeProc` ou `NFe`). Saída: chave, cabeçalho que a importação já lê, e uma lista de
itens `SupplierNfeItem`:

- `nItem`, `cProd`, `xProd`, `uCom`, `qCom`, `vUnCom`, `vProd`, `CFOP`, `NCM`, `cBenef`;
- ICMS: grupo (`ICMS00`, `ICMS20`, `ICMS40`, `ICMS51`, `ICMS90`, `ICMSSN101`…), `orig`, `CST` ou `CSOSN`, `vBC`,
  `pRedBC`, `pICMS`, `vICMS`, `pDif`, `vICMSOp`, `vICMSDif`, `vICMSST`;
- IPI: `vIPI` (só para recusar, D7);
- PIS/COFINS: `CST`, `vBC`, alíquota, valor;
- IBS/CBS (NT 2025.002): `CST`, `cClassTrib`, `vBC`, `pIBSUF`, `pIBSMun`, `pCBS` e as reduções de alíquota.

`PurchaseInvoicesImportXmlService` passa a usar o leitor (o rascunho ganha `NfeItemNumber`); nenhum outro campo do
rascunho muda.

## 7. Documento de terceiro: `nItem` e tributação na gravação

`PurchaseInvoiceSupplierTaxes.Apply(invoice, items)` roda no Create e no Update do Documento de Entrada quando o
documento é **de terceiro**, **Normal** e tem **`XmlData`**: para cada linha com `NfeItemNumber`, acha o `det` do
mesmo `nItem` e grava a fotografia fiscal (mapeamento direto do §6: CST/CSOSN em `CstIcms`, `pRedBC` em
`IcmsBaseReduction`, `pDif` em `IcmsDeferral`, etc.). `nItem` que não existe no XML → recusa ("Item X: o item N não
existe na NF-e do fornecedor"). Linha sem `NfeItemNumber` fica com a fotografia vazia. Sem `XmlData`, não faz nada.

Tela: o rascunho da importação leva o `NfeItemNumber` até o payload do `create()` (as linhas entram pelo binding da
tabela, com a chave presente desde o início); a linha em branco e o "Incluir Item" nascem com `NfeItemNumber: null`.

## 8. "Devolver" em entrada de terceiro

### 8.1 Quando aparece

Filial com a regra ativa + documento **de terceiro**, **Normal**, **Confirmado**, com `ChaveNFe` de 44 dígitos.
(A entrada própria continua com a regra atual: Normal, Confirmada, NF-e Autorizada.)

### 8.2 Diálogo

O mesmo de hoje (Produto, Descrição, Comprado, Já devolvido, A devolver, Motivo) com a coluna **"Item na NF"**:
mostra o `ItemNumber` da origem; onde vier nulo, vira campo editável (inteiro de 1 a 990). Só para documento de
terceiro; na entrada própria a coluna não aparece.

### 8.3 Criação (`PurchaseInvoicesNfeReturnCreateService`)

`ValidateOriginAsync` passa a aceitar a origem de terceiro (§8.1). Para ela, além das conferências de hoje
(motivo, quantidade > 0 e dentro do saldo):

1. Natureza: `Branch.ThirdPartyPurchaseReturnUsageCode` preenchida, existente, ativa e de Saída — senão
   "Configure a natureza de devolução de compra de terceiro na filial {filial}.";
2. `nItem`: linha de origem com `NfeItemNumber` usa o dela (um `ItemNumber` enviado é ignorado); sem ele, exige o
   `ItemNumber` (1–990, único entre as linhas do documento de origem, contando os que já existem) e o grava na
   linha de origem (D6) — senão "Item X: informe o número do item na NF-e do fornecedor.";
3. IPI/ST (D7): com `XmlData`, o `det` do `nItem` com `vIPI > 0` ou `vICMSST > 0` → "Item X: a nota do
   fornecedor tem IPI/ICMS-ST neste item, que o Siagro ainda não devolve.";
4. Produto: `ItemCode` da linha de origem precisa existir no cadastro de produtos — senão "Item X: o produto não
   está cadastrado; ajuste o produto na entrada antes de devolver." (a importação copia o `cProd` do fornecedor).

A devolução nasce igual à de hoje (própria, Devolução, `IsNfeReturn`, `PurchaseInvoiceOriginKey`, pesos
proporcionais, transporte e volume copiados, motivo nas observações), com **todas as linhas na natureza padrão**.

### 8.4 Saldo

Sem mudança: `PurchaseInvoiceNfeReturnBalance` já soma as devoluções `IsNfeReturn` não canceladas por linha de
origem, sem olhar o tipo de emissão da origem.

## 9. Cálculo e conferência

`PurchaseInvoicesTaxApplyService` já calcula a devolução de compra pela natureza da linha, com as UFs da operação
original (fornecedor → filial) e o IBS/CBS pela data da entrada — vale igual para origem de terceiro. Muda só a
conferência: `NfeReturnConference.Ensure` roda **quando a linha de origem tem fotografia** (`CstIcms`
preenchido). Linha de origem sem fotografia (digitada) → sem conferência (D2). A conferência compara os mesmos
campos de hoje (CST e alíquota do ICMS, redução, diferimento, cBenef, CST/classificação/alíquotas/reduções do
IBS/CBS); PIS/COFINS continuam fora.

## 10. Emissão

- `NfeReadinessValidator.LoadPurchaseReturnOriginAsync`: origem **de terceiro** exige `ChaveNFe` de 44 dígitos e
  Confirmada (não olha `NfeStatus`, que é sempre `None`); o número de cada item é o `NfeItemNumber` da linha de
  origem, **sem** o recurso "documento de uma linha só = item 1" (`NfeItemNumbering.OriginNumber`), que continua
  só para a origem própria.
- `NfeIssueInputAssembler`: sem mudança — `DFeReferenciado` com a chave do fornecedor e o `nItem`, sem `NFref`,
  `tPag 90`, sem `cobr`; destinatário = fornecedor, o que satisfaz a VC02-50 (rejeição 1194).
- `infCpl`: a referência "Devolução da NF-e nº {nNF}, série {serie}, de {data}, chave {chave}." usa número e série
  da entrada de terceiro.
- Emissão, consulta, conclusão, DANFE e XML: o pipeline atual, sem mudança.

## 11. Telas (frontend)

- **Filiais** (`view/branchs/fragments/Form.fragment.xml`): campo "Natureza de devolução de compra de terceiro"
  (value help de naturezas de Saída ativas, código + nome), visível com STANDALONE e "Emite NF-e" marcado.
- **Documento de Entrada — inclusão:** `NfeItemNumber` no rascunho e nas linhas (§7).
- **Documento de Entrada — detalhe:** o "Devolver" aparece também na entrada de terceiro (§8.1), por um helper
  testado em QUnit; o diálogo ganha a coluna "Item na NF" (§8.2) e o payload envia `ItemNumbers` sempre.

## 12. Migration

`AddThirdPartyPurchaseReturn`: a coluna `BRANCHS.ThirdPartyPurchaseReturnUsageCode`. Aplicada só no
`CEAGUI_SIAGRO_DEV` (com `ASPNETCORE_ENVIRONMENT=Ceagui-Development`).

## 13. Testes e verificação

TDD com RED visto antes do GREEN.

**`SiagroB1.Application.Tests`:**
- leitor: `det` com ICMS00, ICMS20, ICMS51 (diferimento), ICMS40, CSOSN 101, IPI e ST; IBS/CBS; `nfeProc` e `NFe`;
- importação: rascunho com `NfeItemNumber` na ordem do XML;
- gravação de terceiro com `XmlData`: linha com `nItem` recebe a fotografia do fornecedor; linha sem `nItem` fica
  vazia; `nItem` inexistente recusa; documento sem `XmlData` não muda; entrada própria não passa por aqui;
- "Devolver" de terceiro: cada recusa do §8.3 (natureza ausente, inativa, de Entrada; sem `nItem` e sem
  `ItemNumber`; `ItemNumber` repetido ou fora de 1–990; IPI; ST; produto não cadastrado; origem não confirmada; sem
  chave); `ItemNumber` gravado na origem e reaproveitado na segunda devolução; total e parcial; natureza padrão em
  todas as linhas;
- conferência: linha importada com CST igual passa; CST diferente recusa nomeando item e campo; linha digitada
  passa sem conferência;
- emissão: XML assinado com `tpNF 1`, `finNFe 4`, CFOP 5202 (dentro) e 6202 (fora), `DFeReferenciado` com a chave
  e o `nItem` do fornecedor, destinatário = fornecedor, `tPag 90`; prontidão recusa origem de terceiro sem chave;
- toda a suíte de hoje verde sem mudar asserções.

**Frontend:** QUnit do helper do "Devolver" (própria × terceiro × devolução do cliente) e do payload com
`ItemNumbers`; `yarn ts-typecheck`, `yarn lint`, `ui5lint` sem passar de 919.

**Na tela (stack `ceagui`, Playwright, pelo caminho do usuário):** natureza padrão na filial; fornecedor de teste
pessoa jurídica (CNPJ e IE fictícios) e um XML fictício de NF-e dele, com chave válida (DV) contendo o CNPJ do
fornecedor; importar, ajustar o produto, confirmar, "Devolver" parcial, emitir em **homologação** e conferir
`DFeReferenciado`. Se a SEFAZ exigir que a nota referenciada exista, a verificação para no XSD e isso é dito.

## 14. Riscos

- **cBenef e CST do fornecedor × natureza padrão:** basta o fornecedor usar um cBenef ou CST diferente da natureza
  para a conferência recusar. É o comportamento pedido ("espelhar"), mas pode exigir naturezas de devolução por
  tipo de fornecedor — e a natureza é uma só por filial (D3).
- **Produto do fornecedor:** sem de-para, toda linha importada precisa ter o produto ajustado antes de devolver.
- **`nItem` digitado errado (D2):** só a SEFAZ (ou o fornecedor) percebe.
- **Nota referenciada inexistente em homologação:** o teste fim a fim pode parar no XSD (§13).

## 15. Próximas etapas (fora daqui)

IPI e ICMS-ST na devolução; regra para fornecedor do Simples; de-para de produto do fornecedor; tributação do
fornecedor no diálogo "Tributos do item"; natureza de devolução por fornecedor ou por linha, se uma por filial não
bastar.
