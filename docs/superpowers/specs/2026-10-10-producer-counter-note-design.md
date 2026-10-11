# Contra-nota gerada a partir da escrituração da NF-e do produtor — desenho

Data: 2026-10-10 · Escopo: Documento de Entrada (`PURCHASE_INVOICES`), filial com NF-e (STANDALONE + `Branch.IssuesNfe`)

## 1. Problema

Algumas UFs não exigem contra-nota na compra de produtor rural: basta escriturar a NF-e que o produtor
emitiu. São Paulo exige a contra-nota, mas a **devolução** dessa compra precisa referenciar a chave da
NF-e **do produtor** — referenciar a contra-nota dá rejeição de autorização (1194 / VC02-50, ver spec
`2026-10-05-nfe-purchase-invoice-issuance-design.md` §14).

Hoje a contra-nota é uma Entrada "Própria" isolada: não guarda a chave do produtor (o
`ReferencedAccessKey` foi removido em `db152cf`, spec de 05/10 §16) e a devolução dela referencia a
nossa própria chave. Se o usuário escriturar a nota do produtor como "De terceiro" **e** emitir a
contra-nota como "Própria", nada liga os dois documentos: o Funrural é retido duas vezes (inclusive no
relatório de Funrural retido) e os relatórios de entrada contam a compra em dobro.

## 2. Regra de negócio (decidida com o usuário)

- A NF-e do produtor é **sempre escriturada** (Entrada De terceiro, normalmente importada pelo XML), em
  qualquer UF.
- Quando a natureza permite, a **contra-nota é gerada a partir da escrituração**, com NFref para a chave
  do produtor.
- **A compra é a escrituração.** Estoque fiscal, título a pagar e tributação (apuração/livros) saem só
  dela, como em qualquer outro documento de entrada. A contra-nota é um documento só para a SEFAZ.
- A devolução nasce da escrituração e referencia a chave do produtor + `nItem` (caminho da devolução de
  terceiro já existente — spec `2026-10-05-nfe-third-party-purchase-return-design.md`).

## 3. Decisões

| # | Decisão |
|---|---|
| D1 | Dois documentos vinculados 1:1 (escrituração De terceiro + contra-nota Própria). Rejeitados: um documento com duas NF-e (toda a emissão/cancelamento/CC-e/DANFE assume um cabeçalho = uma NF-e) e a contra-nota como principal com a nota do produtor em tabela filha (contradiz "escriturar sempre"). |
| D2 | Relação **1:1**: uma escrituração gera no máximo uma contra-nota válida; uma contra-nota referencia uma única nota do produtor. |
| D3 | Só escrituração com `TaxDocumentKind = Nfe` (chave de 44 dígitos) gera contra-nota. **"Outro" nunca gera** (nota de produtor modelo 04 não existe mais). |
| D4 | A permissão mora na **natureza**: flag `USAGES.AllowsCounterNote` ("Permite contra-nota"), só em natureza de entrada. |
| D5 | A contra-nota usa **a mesma natureza** de cada linha da escrituração, e a fotografia fiscal da linha (CST, CFOP, bases, alíquotas, valores) é **copiada**, não recalculada — as duas notas têm os mesmos valores. |
| D6 | **Emissão Própria continua** no seletor "Emissão" do Documento de Entrada, sem mudança (será usada para outras situações, a discutir). A contra-nota avulsa criada por esse caminho segue o comportamento atual. |
| D7 | A contra-nota **não tem Funrural**: as colunas de Funrural da linha ficam zeradas; o trecho do Funrural no `infCpl` é montado a partir da escrituração. |
| D8 | Relatórios de entrada (`PurchaseInvoicesByPeriod`, `PurchaseInvoiceItems`) **excluem** a contra-nota. |
| D9 | A contra-nota **não é devolvível** ("Devolver" não aparece). |
| D10 | Escrituração com contra-nota válida (não cancelada) **não pode** ser cancelada, ter a confirmação estornada nem ser editada. |
| D11 | Contra-nota Pendente/Rejeitada pode ser excluída (regra atual do Delete); contra-nota cancelada (documento ou NF-e), inutilizada ou denegada conta como inexistente. Essas situações liberam a escrituração para gerar outra. |
| D12 | Colunas próprias para o vínculo, sem reaproveitar `PurchaseInvoiceOriginKey`/`PurchaseInvoiceItemOriginKey` (que significam remessa/devolução e alimentam o saldo devolvível). |
| D13 | Fases futuras (estoque fiscal, títulos, SPED) devem ignorar a contra-nota — identificada por `ProducerInvoiceKey` preenchido. |

## 4. Modelo de dados

Uma migration:

- `USAGES.AllowsCounterNote` — `bit NOT NULL DEFAULT 0`.
- `PURCHASE_INVOICES.ProducerInvoiceKey` — FK anulável para `PURCHASE_INVOICES` (a escrituração),
  `NoAction`, com índice **não único**. A 1:1 é regra do serviço de geração (§7, "contra-nota válida"):
  ela depende de `NfeStatus`, que um índice filtrado não expressa bem.
- `PURCHASE_INVOICE_ITEMS.ProducerInvoiceItemKey` — FK anulável para a linha da escrituração, `NoAction`.

Domínio: `Usage.AllowsCounterNote`; `PurchaseInvoice.ProducerInvoiceKey` + navegação `ProducerInvoice`;
`PurchaseInvoiceItem.ProducerInvoiceItemKey`. Propriedade derivada `IsCounterNote => ProducerInvoiceKey != null`
(não mapeada; se exposta no EDM, readicionar com `AddProperty`).

Validação da natureza (`UsageService`): `AllowsCounterNote = true` só com `Direction = Incoming`.

## 5. Gerar contra-nota

Action OData `PurchaseInvoicesCreateCounterNote(Key)` → devolve a contra-nota criada.

**Pré-condições** (cada uma com mensagem pt-BR própria):

1. `TaxCalculationGate` ativo para a filial.
2. Escrituração: `IssuerType = ThirdParty`, `InvoiceType = Normal`, `TaxDocumentKind = Nfe`, `ChaveNFe`
   com 44 dígitos, `InvoiceStatus = Confirmed` (em Produção a chave já foi conferida na SEFAZ no Confirmar).
3. Toda linha com natureza `AllowsCounterNote = true`.
4. Nenhuma contra-nota **válida** (§7) apontando para ela.

**Resultado**: Entrada `IssuerType = Own`, `InvoiceType = Normal`, `TaxDocumentKind = Nfe`, Pendente, com:

- fornecedor, filial, condição de pagamento, transporte, pesos e volume copiados do cabeçalho;
- `ProducerInvoiceKey` = escrituração;
- uma linha por linha da escrituração: produto, UM, quantidade, preço, contrato, encargos (frete, seguro,
  desconto, outras), natureza e fotografia fiscal copiados; `ProducerInvoiceItemKey` = linha de origem;
  Funrural zerado.

A criação **não** passa pelo recálculo do motor (D5). Se o serviço de criação comum for reaproveitado,
ele ganha um caminho que pula `taxApply` e o cálculo de Funrural para a contra-nota.

## 6. Emissão

- `NfeIssueInputAssembler`, entrada própria Normal: quando `ProducerInvoiceKey` está preenchido,
  `referencedKeys = [escrituração.ChaveNFe]` → `ide/NFref/refNFe`. `finNFe` continua 1. O
  `NfeXmlBuilder` já escreve o NFref quando há chave e nenhum item tem `Reference`.
- `infCpl`: o trecho do Funrural usa a escrituração (`PurchaseInvoiceFunrural.Text(escrituração)`).
- `NfeReadinessValidator`: além do atual, a escrituração precisa estar Confirmada, não cancelada e com
  chave de 44 dígitos.
- Corrigir o comentário desatualizado de `NfeIssueInput.ReferencedKeys`.
- Cancelamento, CC-e, DANFE, rejeição/reemissão: fluxo atual, sem mudança.

## 7. Edição, exclusão e travas

**Contra-nota (Pendente/Rejeitada)**: travados produto, UM, quantidade, preço, encargos, natureza e
tributos, além de incluir/excluir linha. Editáveis: transporte, pesos, volume e observações. A trava vale
no servidor (Update do cabeçalho, `PurchaseInvoicesItemsUpdateService`, Items Create/Delete) e na tela.

**Escrituração com contra-nota válida** (definição logo abaixo): recusados Update (cabeçalho e linhas), Cancel e ReverseConfirm.

**Exclusão da contra-nota**: sem mudança — o Delete atual já aceita documento Pendente com NF-e não
emitida ou Rejeitada (`PurchaseInvoiceNfeLock.EnsureDeletable` só barra Processing/Authorized/Denied/
Cancelled/Voided) e remove os XMLs da rejeição. O Delete ganha apenas a limpeza natural do vínculo (a
contra-nota some, a escrituração fica livre).

**Contra-nota válida** (a que trava a escrituração e impede gerar outra): `InvoiceStatus <> Cancelled` e
`NfeStatus` fora de {Cancelled, Voided, Denied}. Por isso o índice do §4 não garante sozinho a 1:1 — a
regra fica no serviço de geração, e o índice de `ProducerInvoiceKey` é não único.

## 8. Contagem única

- Funrural: zerado na contra-nota (D7) → relatório de Funrural retido já não a conta.
- `PurchaseInvoicesByPeriodReportService` e `PurchaseInvoiceItemsReportService`: filtro
  `ProducerInvoiceKey == null`.
- `PurchaseInvoicesNfeReturnableItems` / criação de devolução: recusa contra-nota.

## 9. Frontend

- **Natureza** (tela de naturezas de operação): checkbox "Permite contra-nota", visível/habilitado só
  para direção Entrada.
- **Detail da escrituração**: botão **"Gerar contra-nota"** (visível com as pré-condições do §5, calculadas
  como `canReturnThirdPartyPurchase` é hoje, e sem contra-nota válida); ao concluir, navega para a
  contra-nota. Link **"Contra-nota: nº X (status)"** quando existir.
- **Detail da contra-nota**: link **"Escrituração: NF nº Y"**; sem "Devolver"; campos travados conforme §7.
- **Lista**: coluna nova **"Contra-nota de"** com o número da escrituração.
- Textos em pt-BR via i18n.

## 10. Fora da filial com NF-e

Modo SAPB1 ou filial sem `IssuesNfe`: o botão nunca aparece e a flag da natureza não tem efeito.

## 11. Testes

- Application.Tests: geração (cópia da fotografia fiscal, Funrural zerado, vínculos) e cada recusa do §5;
  travas do §7; unicidade 1:1; exclusão nos relatórios; recusa de devolução da contra-nota; validação da
  flag na natureza.
- Fiscal: `NFref/refNFe` com a chave do produtor no XML da contra-nota; `infCpl` com o Funrural da origem.
- **E2E pela tela, homologação, `CEAGUI_SIAGRO_DEV`**: marcar a natureza → importar o XML do fornecedor
  fictício → confirmar → "Gerar contra-nota" → informar pesos → emitir → devolver pela escrituração.
  Risco: a chave do produtor é fictícia; se a SEFAZ conferir a nota referenciada, a rejeição é do dado,
  não do código — registrar e não emitir com dados reais sem pedido explícito.

## 12. Fora de escopo

- Uma contra-nota cobrindo várias notas do produtor (NFref 1:N).
- Mudanças na Emissão Própria avulsa (D6) — tema da próxima conversa.
- Estoque fiscal, títulos e SPED (só a regra D13 fica registrada).
