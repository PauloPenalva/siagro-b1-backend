# GAC-1226 (rodada 2) — vínculo de várias NFs do SAP à mesma carga

Data: 2026-11-17 · Decidido pelo usuário em chat (16/11/2026, depois do teste manual da rodada 1)

Complementa `2026-11-16-gac-1226-sap-invoice-link-design.md`; o que não é citado aqui continua valendo.

## Por quê

Na rodada 1 a NF do SAP precisava ter quantidade **igual** ao saldo da carga. No teste manual isso
quase nunca casa: a Yokotobi emite a NF com o peso de balança e, quando preciso, uma segunda NF de
complemento. O usuário mudou a decisão para "peso de balança + complemento": **1 carga : N notas**.

## Regras

1. O usuário marca **uma ou mais** NFs do SAP. Nenhuma NF é dividida: cada uma entra inteira.
2. A **soma** das quantidades marcadas deve ser **igual ao saldo disponível da carga** (tolerância
   0,001). Senão: *"A soma das notas fiscais do SAP ({s}) é diferente do saldo da carga {código}
   ({a}). O vínculo exige que a soma seja igual ao saldo."*
3. Todas as NFs marcadas são do **mesmo cliente**, e o usuário escolhe **uma única liberação de
   entrega**, que vale para todas. Clientes diferentes: *"As notas fiscais selecionadas devem ser do
   mesmo cliente."* O cliente do contrato da liberação continua tendo de ser o da NF.
4. Cada NF gera **um documento de saída próprio** (um `SapDocEntry`, um número/série/chave por
   documento) — o modelo guarda uma NF por documento. Os efeitos de cada documento são os de hoje.
5. **Tudo ou nada:** se qualquer NF for recusada, nenhuma é vinculada e nada fica gravado. Os
   NÚMEROS de documento tomados por um lote desfeito não são reaproveitados (a sequência de numeração
   roda fora da transação, como sempre foi no faturamento manual), então um vínculo que falha pode
   deixar buracos na numeração.
6. A listagem deixa de filtrar por quantidade igual ao saldo: traz as NFs com quantidade **maior que
   zero, inclusive as maiores que o saldo** (ver a NF ajuda a achar uma quantidade de carga digitada
   errada; quem barra é a regra da soma no vínculo) (as demais regras — item, filial, modelo, não cancelada, autorizada no add-on,
   incoterm, ainda não vinculada — continuam). Carga sem saldo continua com lista vazia.
7. Vinculada cada NF, o resto do ciclo não muda: documento vinculado travado, estornar/excluir/
   cancelar por documento, sincronização por documento. Excluir um dos documentos devolve só a
   quantidade dele ao saldo; para vincular de novo, a soma das NFs escolhidas tem de bater com o
   saldo que voltou.

## Contrato

- Função `ShipmentLoadsGetLinkableSapInvoices(Key, DateFrom, DateTo)`: mesma forma; muda só o filtro
  de quantidade.
- Ação `ShipmentLoadsLinkSapInvoice`: o parâmetro `SapDocEntry` (Int32) vira `SapDocEntries`
  (coleção de Int32, ao menos um, sem repetição). Resposta: `{ documents: [{ key, invoiceNumber }] }`
  na ordem dos `SapDocEntries`. Backend e frontend mudam juntos.

## Telas

- **Vincular NF do SAP:** a tabela de notas vira `sap.ui.table.Table` com seleção múltipla, filtro e
  ordenação por coluna; rodapé com "Selecionado: {soma} kg de {saldo} kg". "Vincular" só habilita com
  soma igual ao saldo, um único cliente e uma liberação escolhida. A tabela de liberações também vira
  `sap.ui.table.Table` (seleção única), filtrada pelo cliente das NFs marcadas. Depois de vincular:
  uma NF → abre o documento criado; várias → fica na carga, atualiza e mostra "{n} notas fiscais do
  SAP vinculadas à carga."
- **Expedir:** a tabela de liberações vira `sap.ui.table.Table` (seleção única) com filtro e
  ordenação por coluna.
- **Layout por usuário:** as tabelas novas entram no mecanismo que já existe
  (`TableLayoutService.registerTables` / `registerTableLayouts`), com ids estáveis — ordem e largura
  das colunas ficam gravadas no perfil do usuário, como nas outras telas.

## Escolhas do autor

- Um documento por NF; vínculo atômico; lista com quantidade maior que zero, sem teto.
- Texto de lista vazia: "Nenhuma nota fiscal do SAP disponível para esta carga neste período."
- Marcar uma NF de outro cliente não é impedido na seleção; o botão fica desabilitado e o rodapé
  avisa "Selecione notas fiscais do mesmo cliente."
