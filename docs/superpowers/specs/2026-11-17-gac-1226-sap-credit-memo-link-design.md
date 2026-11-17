# GAC-1226 — NF-e de devolução de venda do SAP (A/R Credit Memo) vinculada na carga

Data: 17/11/2026. Cliente: Yokotobi. Só `Erp = SAPB1`.
Continua `2026-11-16-gac-1226-sap-invoice-link-design.md` (lá a devolução no SAP era não-objetivo).

## Intenção

O documento de saída já nasce da A/R Invoice do SAP (OINV) escolhida na carga. A devolução não: em SAPB1 o
"Registrar Recusa" cria o documento de devolução (`InvoiceType = Return`, Confirmado) sem número, série nem
chave. A Yokotobi emite a NF-e de devolução no SAP como A/R Credit Memo (ORIN) e precisa vê-la no Siagro, no
documento de devolução correspondente.

## Objetivos

- Na carga, o usuário vincula um Credit Memo do SAP a um documento de devolução já confirmado.
- O vínculo grava no documento: DocEntry e DocNum do Credit Memo, número, série e chave da NF-e, e CFOP e
  natureza do item.
- Dá para desvincular (erro de escolha) e o cancelamento do Credit Memo no SAP desvincula sozinho.

## Não-objetivos

- Criar ou alterar qualquer coisa no SAP ou no InvOne.
- Mudar o "Registrar Recusa": ele continua síncrono em SAPB1 e move saldo, armazém e refaturamento na hora.
- Devolução de documento sem carga, e qualquer mudança em STANDALONE.
- Um Credit Memo para várias devoluções, ou várias notas para uma devolução.

## Anti-objetivos

- O vínculo ou o desvínculo mover saldo da carga, `ReturnedQuantity` da origem, romaneio, liberação ou contrato.
- O cancelamento do Credit Memo no SAP cancelar a devolução (desfaria o físico).
- Alterar o comportamento do vínculo de venda (OINV) que já está em `main`.

## Decisões do usuário (17/11/2026)

1. O vínculo é feito na carga, não na tela de documento de saída.
2. Na Yokotobi a recusa é registrada primeiro; a nota sai no SAP dias depois.
3. A recusa vale na hora; o vínculo posterior só grava os dados fiscais.

## Abordagens

**Onde vincular**

- *Na carga (escolhida).* Toda nota vinculada ao SAP tem carga; a devolução de documento de carga já é feita
  pela carga (o documento de saída recusa o "Retornar" e manda para lá); a carga já mostra "Doc. SAP".
- *No documento de saída.* Não teria público: em SAPB1 não há devolução por lá, e o detalhe nem mostra os dados
  do SAP.

**Quando a recusa vale**

- *Na hora, nota depois (escolhida).* A operação não para; por alguns dias existe devolução sem nota, marcada
  "Aguardando NF do SAP". Mudança pequena: o vínculo só grava campos.
- *Pendente até a nota.* Nunca há devolução sem nota, mas a carga fica travada por dias e o motor da recusa em
  dois tempos (hoje só STANDALONE) teria de ser adaptado.

**Como guardar o DocEntry do Credit Memo**

- *Colunas próprias (escolhida).* `SapCreditMemoDocEntry` / `SapCreditMemoDocNum`. `SapDocEntry` segue
  significando só OINV; nenhuma consulta do fluxo de venda muda; migration só aditiva.
- *Discriminador `SapObjType` sobre `SapDocEntry`.* DocEntry de OINV e de ORIN são sequências independentes e
  colidem; cinco consultas do fluxo de venda teriam de ganhar o filtro, e esquecer uma falha no lado perigoso
  (o job leria a OINV de mesmo número e cancelaria a devolução).

## Regras

**Documento que aceita vínculo:** da carga informada, `InvoiceType = Return`, `InvoiceStatus = Confirmed`,
`IsNfeReturn = false`, sem Credit Memo vinculado.

**Credit Memo vinculável** (ORIN + RIN1, lido por SQL no banco do SAP):

- `CANCELED = 'N'`, `Model = '39'`, `Serial` preenchido;
- `BPLId` igual ao `BranchCode` do documento de devolução (mesma regra de texto exato da venda);
- `CardCode` igual ao do documento de devolução;
- exatamente uma linha, do mesmo item do documento, quantidade > 0;
- não vinculado a outro documento de saída ativo (nem Cancelado nem Devolvido);
- com add-on fiscal configurado: autorizado e não cancelado; sem informação no add-on = não aparece.

A listagem **mostra** notas de quantidade diferente (ajuda a achar erro) com `QuantityMatches = false`; o
vínculo **exige** quantidade igual à do documento de devolução (tolerância 0,001). `MatchesOrigin` é verdadeiro
quando a linha tem `BaseType = 13` e `BaseEntry` igual ao `SapDocEntry` do documento de origem; as casadas vêm
primeiro e a referência nunca é exigida. Máximo de 200 linhas.

**Vincular** (uma transação): grava `SapCreditMemoDocEntry`, `SapCreditMemoDocNum`, `TaxDocumentNumber`,
`TaxDocumentSeries`, `ChaveNFe`, zera `WithoutTaxDocument`, e por item `Cfop` (quando o SAP informa),
`UsageCode` e `UsageName`. Passa pelo `SalesInvoiceTaxDocumentDuplicateGuard`. Registra no log de alterações
do documento. Pode sobrescrever número digitado antes.

**Desvincular:** limpa as duas colunas, número, série e chave; mantém CFOP e natureza; registra no log, com o
motivo quando vier do job.

**Travas:** `IsSapLinked` passa a valer para as duas colunas, então o documento de devolução vinculado tem
cabeçalho e itens travados como o de venda. Estornar a confirmação de uma devolução vinculada é recusado:
"Desvincule a NF de devolução do SAP antes de estornar." Cancelar é permitido; o índice filtrado libera o
Credit Memo.

**Sincronização (`sap-invoice-sync`):** segundo passo, depois do de venda, com a mesma trava e o mesmo carimbo.
Credit Memo com `CANCELED = 'Y'` ou cancelado no add-on → desvincula (usuário `sap-sync`). Ausente do SAP ou de
outra filial → só aviso. Chave faltando → preenche.

**"Aguardando NF do SAP"** é derivado na tela: modo SAP, `Return`, `Confirmed`, sem `SapCreditMemoDocEntry`,
sem `TaxDocumentNumber` e sem `WithoutTaxDocument`. Devoluções antigas sem número aparecem como pendentes.

## Tela

Na carga, a seção "Devoluções e Quebras da Recusa" ganha, acima da tabela de romaneios, a tabela "Documentos de
devolução" (os documentos `Return` não cancelados da carga): Documento, NF, Doc. SAP, Situação, Emissão, Qtde e
ações por linha — "Vincular NF de devolução" quando aguardando, "Desvincular NF de devolução" quando vinculada.
Só em modo SAP. A seção "Documentos de Saída" mostra o mesmo selo e o número do Credit Memo em "Doc. SAP".

Diálogo "Vincular NF de devolução": cabeçalho com o documento de devolução e a quantidade esperada; período
(padrão: da emissão da devolução até hoje); tabela de seleção única com NF, Série, Emissão, Doc. SAP, Cliente,
Qtde, Valor, CFOP, Natureza, "Ref. NF de origem" e aviso de quantidade diferente. "Vincular" só habilita com
linha de quantidade igual.

## API

- Function `ShipmentLoadsGetLinkableSapCreditMemos(Key, SalesInvoiceKey, DateFrom, DateTo)`.
- Action `ShipmentLoadsLinkSapCreditMemo` (`Key`, `SalesInvoiceKey`, `SapDocEntry`).
- Action `ShipmentLoadsUnlinkSapCreditMemo` (`Key`, `SalesInvoiceKey`).
- Fora de SAPB1 as três respondem 400, como as do vínculo de venda.

## Banco e deploy

Migration `AddSalesInvoiceSapCreditMemoLink` (AppDbContext): duas colunas `int NULL` e índice único filtrado
`[SapCreditMemoDocEntry] IS NOT NULL AND [InvoiceStatus] <> Cancelled AND <> Returned`. Ordem: migration em todo
banco de empresa → backend → frontend. Sem mudança de appsettings.

## A confirmar no SAP da Yokotobi

A consulta ao banco de produção não foi feita nesta sessão (acesso negado pelo ambiente). Ficam como hipótese:

- `Process.DocType` do Credit Memo no InvOne = 14 (ObjType de ORIN). É a constante `ArCreditMemoDocType`; com o
  valor errado nenhuma nota aparece na lista (falha fechada).
- ORIN traz `Model`, `Serial`, `SeriesStr` e `BPLId` como a OINV, e RIN1 traz `CFOPCode`, `Usage`, `BaseType`
  e `BaseEntry` (é o esquema padrão do SAP B1; os tipos foram copiados de OINV/INV1).

Pontos de negócio em aberto: um Credit Memo cobrindo mais de uma devolução (a regra de quantidade igual não
fecharia); destinos sem nota no SAP (quebra no trajeto, transbordo), em que o selo ficaria aceso — o botão
"Informar Nota Fiscal" está oculto desde o GAC-1226.

## Deixado ao implementador

Nomes internos de métodos, organização dos testes, textos exatos das mensagens de erro (pt-BR) e o detalhe
visual do diálogo, seguindo o padrão do "Vincular NF do SAP".
