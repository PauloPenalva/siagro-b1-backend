# GAC-1226 — Vínculo de NF do SAP com divergência e registro de divergências

Data: 24/11/2026. Cliente: Yokotobi. Só `Erp = SAPB1`.
Continua `2026-11-16-gac-1226-sap-invoice-link-design.md`, `2026-11-17-gac-1226-multi-invoice-link-design.md` e
`2026-11-17-gac-1226-sap-credit-memo-link-design.md`. Onde este documento contradiz os anteriores, vale este.

Siglas do usuário: QE = quantidade efetiva (o que movimentou); QNF = quantidade coberta por NF; VE = valor efetivo
(pelo contrato atendido); VNF = valor das NFs; NFCD = NF de complemento ou de devolução; TP = transportadora;
LE = liberação de entrega.

## Intenção

Na Yokotobi a NF não comanda a operação: ela cobre, depois, o que já aconteceu. O vínculo de hoje exige que a soma
das NFs seja igual ao saldo da carga e por isso barra exatamente os casos que a operação produz todo dia. O
vínculo passa a **aceitar a divergência, avisar e registrá-la**; o Fiscal cobre cada divergência depois com NFCD.

## Escopo: seis frentes, esta spec cobre a primeira

1. **Vínculo permissivo + registro de divergência (esta spec).**
2. Reescrita do registro pela Conferência de Entrega e pela Conciliação de Entregas de Venda.
3. Visão QE × QNF × VE × VNF nas cargas e nos contratos.
4. Recusa "segue para novo destino" pedindo o cliente.
5. Tela de acompanhamento das NFs do SAP ainda não consumidas.
6. Conciliação fiscal N:N das divergências com NFCD.

Cada frente tem spec própria. Esta entrega funciona sozinha: vincula, registra e mostra a divergência na carga.

## Objetivos

- Vincular NFs cuja quantidade ou valor diferem do esperado, com alerta e confirmação, sem barrar.
- A carga fica Faturada ao vincular, mesmo com QNF ≠ QE.
- Um registro de divergência por NF, com QE, QNF, VE, VNF e as diferenças.
- A lista de NFs traz só NF-e normal (finNFe = 1), da transportadora, da placa e do cliente da carga.
- Desvincular as NFs da carga numa ação só, enquanto a carga estiver Faturada.
- Remover o vínculo 1:1 da NF de devolução (ORIN) na carga.

## Não-objetivos

- Frentes 2 a 6. Em especial: nada de NFCD nesta entrega, e a Conferência/Conciliação ainda não tocam o registro.
- Criar ou alterar qualquer coisa no SAP ou no InvOne.
- Qualquer mudança em STANDALONE.

## Anti-objetivos

- O contrato ser baixado por outra coisa que não a quantidade da NF.
- A carga voltar a oferecer para faturamento o volume que a divergência já explica.
- Uma NF de complemento (finNFe = 2) aparecer como NF normal na lista. Hoje ela aparece: é A/R Invoice como as
  outras.

## Decisões do usuário

- 24/11: NFCD é coberta só pela conciliação fiscal; o vínculo 1:1 da devolução na carga é removido.
- 24/11: a carga fecha por um **termo de divergência no saldo** (abordagem A, abaixo).
- Requisitos da Yokotobi (24/11): filtros por TP, placa e cliente da carga; LE do cliente da carga por padrão, com
  chave para ver as de outros clientes; alerta sem bloqueio; desvincular só até Faturada.

## Achados na base de produção (leitura em 24/11/2026)

| Pergunta | Resposta |
|---|---|
| Onde está a placa da NF? | `INV12.Vehicle` (nvarchar 10), no formato `AAA9X99`, sem hífen. Preenchida em 4.604 de 8.393 NFs modelo 39 dos últimos 120 dias — as mesmas que têm `Carrier` (4.586). |
| Onde está o finNFe? | Só no XML da NF-e. `OINV.U_finNfe` e `ORIN.U_finNfe` valem `0` em todas as linhas e `DocSubType` é sempre `--`: não servem. |
| Onde está o XML? | `DBInvOne.Process.XmlEnvAssinado` (varbinary), comprimido em **deflate sem cabeçalho**; ~4 KB por nota. |
| Complemento é que documento? | A/R Invoice (OINV, `Process.DocType = 13`) com `finNFe = 2`: 13 em 600 amostradas. |
| Devolução é que documento? | A/R Credit Memo (ORIN, `Process.DocType = 14`), `finNFe = 4` em 600 de 600. A hipótese do tipo 14 está **confirmada**. |
| Quantas linhas tem a NF? | 1 linha em 6.838 de 8.393 (81%). A regra "exatamente uma linha" continua. |

## Abordagens — como a carga fecha com QNF ≠ QE

- **A. Termo de divergência no saldo (escolhida).** O documento de saída espelha a quantidade da NF e o contrato
  é baixado por ela. A carga ganha um sexto termo persistido-derivado, a soma das diferenças de quantidade dos
  registros vivos; o saldo zera e a situação sai Faturada pelo cálculo que já existe.
- **B. Documento com a quantidade efetiva.** Não mexe na carga, mas baixa o contrato pela QE — o contrário do
  requisito.
- **C. Marca "faturamento encerrado".** Força a situação, mas o saldo continua mostrando sobra e as travas
  deixariam faturar de novo.

## Regras

### Lista de NFs vinculáveis

Mantém do que existe: `CANCELED = 'N'`, `Model = '39'`, `Serial` preenchido, `BPLId` = filial da carga (texto
exato), `Incoterms` em 0, 1, 2 ou 9, exatamente uma linha, do item da carga, quantidade > 0, não vinculada a outro
documento ativo, autorizada e não cancelada no add-on, período, máximo de 200.

Passa a filtrar também, no SQL do SAP:

- `OINV.CardCode` = `ShipmentLoad.CardCode`;
- `INV12.Carrier` = `ShipmentLoad.CarrierCardCode` (deixa de ser só aviso; `CarrierMismatch` sai do DTO);
- `INV12.Vehicle` = `ShipmentLoad.TruckCode`, comparadas em maiúsculas e sem hífen nem espaço.

E, depois do SAP, **finNFe = 1**, lido do XML do add-on. O `ISapFiscalDocumentSource` passa a devolver a
finalidade junto com a chave e a situação; o InvOne descomprime o `XmlEnvAssinado` (deflate cru) e lê a tag
`finNFe`. Nota sem XML legível ou sem a tag **não aparece** (falha fechada). Sem add-on configurado, o filtro de
finalidade não se aplica.

Carga sem cliente, sem transportadora ou sem placa: a lista vem vazia e a função responde com erro de negócio que
diz o que falta ("Informe o cliente da carga para vincular a NF do SAP." e equivalentes). Meu critério: corrigir a
carga é o caminho que o requisito descreve.

`ShipmentLoad.CardCode` deixa de ser "meramente informativo". O comentário da entidade proíbe ligar regra a ele
"por decisão do cliente"; a decisão mudou em 24/11 e o comentário é reescrito. Ele continua sem efeito em saldo,
romaneio e documento — o documento nasce com o cliente da NF, que por este filtro é o mesmo.

### Liberações de entrega

Por padrão, só as do **cliente da carga** (cliente do contrato = `ShipmentLoad.CardCode`). Uma chave "Mostrar
liberações de outros clientes", desligada ao abrir, traz as demais, marcadas com o aviso que já existe. Isto
substitui o comportamento de 18/11 (todas, as do cliente da NF primeiro). O backend continua aceitando liberação
de qualquer cliente.

### Vincular

Some a regra "soma das NFs = saldo da carga". No lugar:

- **QE** = saldo a faturar da carga no momento do vínculo.
- **QNF** = soma das quantidades das NFs escolhidas.
- **VE** = QE × preço do contrato da liberação escolhida, na unidade do documento.
- **VNF** = soma do total da linha (`INV1.LineTotal`) das NFs.
- Tolerâncias: 0,001 na quantidade; R$ 0,01 no valor.

| Tipo (`SapLinkType`) | Condição | Alerta |
|---|---|---|
| 0 Normal | QE = QNF e VE = VNF | nenhum |
| 1 Valor divergente | QE = QNF e VE ≠ VNF | "Valor(es) da(s) NF(s) diferente(s) do contrato, deseja finalizar o faturamento?" |
| 2 Quantidade e valor divergentes | QE ≠ QNF | "Valor(es) e qtde(s) da(s) NF(s) diferente(s) do contrato, deseja finalizar o faturamento?" |

QE ≠ QNF com VE = VNF não está no requisito; entra no tipo 2 (meu critério).

O servidor recalcula tudo. A action recebe `ConfirmDivergence`; tipo 1 ou 2 sem a confirmação é recusado com o
texto do alerta, e nada é gravado. A tela calcula o mesmo tipo para mostrar o alerta antes de enviar.

Com várias NFs, a QE é rateada entre elas na proporção da quantidade de cada uma (3 casas; a última leva o
resto) e a VE de cada NF é a QE dela × o preço do contrato. O tipo é do vínculo inteiro e fica gravado em cada
documento (`SalesInvoice.SapLinkType`).

QNF **maior** que a QE também é aceita. O guard de saldo físico da carga, que hoje recusa o excesso, não se aplica
ao caminho do vínculo: a divergência explica a diferença.

Tudo o mais continua como hoje: um documento de saída Confirmado por NF, com quantidade, preço, cliente, frete,
CFOP e natureza da NF; alocação no contrato, saldo da liberação e títulos pela quantidade da NF; tudo ou nada numa
transação.

### Registro de divergência

Entidade `SalesInvoiceDivergence`, tabela `SALES_INVOICE_DIVERGENCES`, um registro por documento de saída
vinculado nos tipos 1 e 2 (o tipo 0 não gera registro):

`Key`, `SalesInvoiceKey`, `ShipmentLoadKey`, `LinkType`, `EffectiveQuantity`, `InvoiceQuantity`,
`QuantityDifference` (QE − QNF, com sinal), `EffectiveValue`, `InvoiceValue`, `ValueDifference` (VE − VNF),
`Status` (`Open`, `Voided`), `CreatedAt`, `CreatedBy`, `VoidedAt`, `VoidedBy`, `VoidReason`.

`Voided` quando o documento é cancelado, desvinculado ou totalmente devolvido. As frentes 2 e 6 acrescentam a
origem do registro e a baixa por NFCD; não antecipar colunas para elas.

### Saldo e situação da carga

`ShipmentLoad.InvoiceDivergenceQuantity` (DECIMAL(18,3), padrão 0), persistido-derivado: soma de
`QuantityDifference` dos registros `Open` da carga. Escritor único: `ShipmentLoadsRecalculateInvoicedService`.
Entra como sexto termo em `CalculateAvailableQuantity` e em `ResolveStatus`:

`saldo = total − faturado − devolvido a armazém − transbordado − quebra − divergência`

Exemplo: 30.000 expedidos, NF de 29.800 → faturado 29.800, divergência 200, saldo 0, Faturada.
Com NF de 30.150 → faturado 30.150, divergência −150, saldo 0, Faturada.

### Desvincular

Action nova na carga. Permitida com a carga `Invoiced` ou `PartiallyInvoiced`; de `Discharged` em diante é
recusada: "Desfaça a descarga e a conferência antes de desvincular as notas fiscais." Cancela, numa transação,
todos os documentos de venda vivos da carga vinculados ao SAP, pelo mesmo caminho do cancelamento de documento
que já existe (alocação, liberação, títulos e saldo voltam), e marca os registros como `Voided`. Documento com
devolução ou recusa registrada bloqueia: "Estorne a recusa antes de desvincular."

### Cancelamento no SAP

Sem mudança de regra: a sincronização cancela o documento da NF cancelada. Acrescenta-se marcar o registro de
divergência como `Voided`; o recálculo reabre o faturamento da carga sozinho.

### Recusa

Documento totalmente devolvido pela recusa: o registro vira `Voided` e o termo sai do saldo, então a carga volta a
oferecer a quantidade efetiva inteira no destino "segue viagem". Devolução parcial mantém o registro.

## Remoção do vínculo da NF de devolução na carga

Sai: `SapCreditMemoDocEntry`/`SapCreditMemoDocNum` e o índice (migration `RemoveSalesInvoiceSapCreditMemoLink`),
`SapLinkableCreditMemosService`, os serviços e controllers de vincular/desvincular, as três rotas do EDM, o
segundo passo da sincronização, a trava do estorno, a tabela "Documentos de devolução" e o selo "Aguardando NF do
SAP" na carga, e o diálogo. `IsSapLinked` volta a olhar só `SapDocEntry`.

Fica, porque a frente 6 reaproveita: entidades `SapCreditMemo`/`SapCreditMemoLine`, a leitura do tipo 14 no
InvOne, `SapTaxDocumentFormat`, `ApplySapFiscalFields`.

A migration de criação já está em `main`; nenhum cliente a recebeu. Na Yokotobi as duas rodam em sequência.

## Tela (carga)

Diálogo "Vincular NF do SAP":

- some a coluna e o aviso de transportadora diferente;
- o rodapé mostra QE, QNF, VE e VNF e as duas diferenças, e deixa de bloquear por soma diferente;
- a chave "Mostrar liberações de outros clientes" acima da tabela de liberações;
- "Vincular" habilita com NF marcada e liberação escolhida; com divergência, abre a confirmação com o texto do
  tipo.

Detalhe da carga: botão "Desvincular NFs do SAP" (visível em Faturada / Parcialmente faturada, modo SAP); no
cabeçalho, "Divergência de NF" quando diferente de zero; na seção "Documentos de Saída", tabela "Divergências"
com NF, tipo, QE, QNF, diferença, VE, VNF, diferença e situação.

## API

- Function `ShipmentLoadsGetLinkableSapInvoices`: mesmos parâmetros; some `CarrierMismatch`.
- Action `ShipmentLoadsLinkSapInvoice`: ganha `ConfirmDivergence` (Boolean, opcional, padrão falso).
- Action nova `ShipmentLoadsUnlinkSapInvoices` (`Key`).
- Entity set `SalesInvoiceDivergences`, só leitura, e navegação `ShipmentLoad.InvoiceDivergences`.

## Banco e deploy

Migrations (AppDbContext): `RemoveSalesInvoiceSapCreditMemoLink`; `AddSalesInvoiceDivergences` (tabela,
`SalesInvoice.SapLinkType`, `ShipmentLoad.InvoiceDivergenceQuantity`). Ordem: migrations em todo banco de empresa
→ backend → frontend. Sem mudança de appsettings.

## Deixado ao implementador

Nomes internos, organização dos testes, textos de erro não citados aqui, e o detalhe visual do rodapé e da tabela
de divergências, no padrão do que a carga já tem.

## Riscos

- Ler o XML custa ~4 KB por nota candidata; com os filtros de cliente, TP e placa o conjunto é pequeno. Não ler o
  XML fora da listagem e da revalidação do vínculo.
- 45% das NFs modelo 39 não têm placa nem transportadora no SAP. Pelo recorte parecem ser as que não são de carga
  de grão; se uma NF de carga vier sem placa, ela não aparece e o usuário não tem como saber por quê.

## Ajustes da revisão de 24/11 (valem sobre o texto acima)

Um revisor leu a spec contra o código. O que mudou:

**Unidade e moeda.** O preço do contrato é por KG e o vínculo copia quantidade e preço da NF sem conversão; a
regra "soma = saldo" era o único guard de unidade. Na produção, das 4.586 NFs de carga de uma linha dos últimos
120 dias, 4.585 estão em KG e uma em TON, todas em R$, e em todas `LineTotal` = quantidade × preço. Regra: a
lista só traz NF com `INV1.unitMsr = 'KG'` e `OINV.DocCur = 'R$'`. Sem conversão.

**Placa.** Só 1 dessas 4.586 NFs está sem placa. O risco listado acima não se confirma para NF de carga.

**Preço para a VE.** O mesmo que a alocação já usa (`ConfirmedUnitPriceAsync`: preço do contrato fixo ou média
das fixações confirmadas), devolvido pela função das liberações para a tela classificar igual ao servidor.

**Registro: a contagem segue o documento, não o status.** `Voided` só no cancelamento, na exclusão e no
desvincular — gravado dentro do próprio cancelamento de documento, antes do recálculo da carga, para valer também
no cancelamento manual e na sincronização. Documento Pendente excluído leva o registro junto. O termo da carga
soma os registros `Open` cujo documento está vivo e **não totalmente devolvido**; assim, estornar uma devolução
faz o termo voltar sozinho, sem religar status.

**Desvincular.** Só com a carga `Invoiced` ou `PartiallyInvoiced`; qualquer outra situação é recusada. Exige uma
entrada diferida no cancelamento de documento (a pública abre e comita a própria transação). As travas do
cancelamento — título baixado, mês fiscal fechado, quebra de descarga ativa, complemento de ICMS — continuam
valendo e derrubam o desvincular inteiro com a mensagem delas.

**Concorrência.** A QE é relida dentro da transação do vínculo. Do guard de faturamento sai só o teste de excesso;
as travas de situação e de transportadora ficam.

**Um vínculo por ciclo.** Como a QE é o saldo inteiro, o primeiro vínculo sempre fecha a carga. NF normal que
chegue depois exige desvincular e vincular de novo. Complemento não é mais NF de vínculo: é NFCD.

**Finalidade.** Método próprio na fonte fiscal, usado só na listagem e na revalidação; a sincronização de minuto
em minuto não lê XML.

**Sincronização.** Comportamento atual mantido, inclusive excluir o documento Pendente e reabrir cargas
Descarregadas ou Concluídas quando a NF é cancelada no SAP.

### Em aberto com o usuário

1. Recusa total para armazém, transbordo ou quebra de um documento com divergência: o efeito físico usa a
   quantidade efetiva ou a da NF?
2. Contrato sem preço confirmado (a fixar) ou em dólar: como classificar o valor?
