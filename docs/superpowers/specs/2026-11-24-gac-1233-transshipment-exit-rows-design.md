# GAC-1233 — Saídas de transbordo na aba Entregas do contrato de compra

## Por quê

A Yokotobi confrontou o saldo do armazém Eugênia (F006903) com a planilha e não achou o romaneio
00017669 (440 kg, carga CG002223) nas entregas do contrato 00004787. O saldo está certo: o romaneio
é uma saída pela liberação gerada na entrada de transbordo (`ReleaseOrigin.Transshipment`), que não
debita o contrato de novo — o volume já foi baixado pela Compra(8) na saída do fornecedor. Como a
aba Entregas lê só o razão `PURCHASE_CONTRACTS_ALLOCATIONS`, a saída some da tela, enquanto a saída
por liberação de devolução (GAC-1214) aparece como "Reentrega". A diferença gera dúvida a cada
conferência.

## Objetivo

Listar na aba Entregas, como linhas **informativas**, os romaneios que consomem liberações de
transbordo do contrato, para que romaneio e carga sejam rastreáveis sem mudar o saldo.

**Fora do escopo:** sobra de conferência (`WarehouseGain`), transferência de titularidade, a entrada
do transbordo (romaneio 15), o contrato de venda, qualquer mudança em saldo, razão ou migration.

**Não pode acontecer:** a soma da coluna Quantidade deixar de bater com o volume alocado do contrato.

## Abordagens

1. **Segunda consulta só de exibição, unida às linhas do razão (escolhida).** Não toca o razão, não
   tem migration, cabe num hotfix e não altera a consulta principal (sensível a plano de execução).
2. Gravar alocações de volume zero no razão. Exige backfill e mexe em todos os leitores do razão.
3. `UNION` dentro da consulta principal. Mesmo resultado da 1, mas arrisca o plano do
   `SELECT DISTINCT` com 19 joins, que já deu timeout em produção.

## Desenho (decisões do usuário)

- Coluna **Quantidade = 0**; o peso do romaneio vai no **Motivo**. (Escolha explícita do usuário.)
- Só transbordo.
- Hotfix sobre a 1.1.319: backend a partir de `116dfa35`, frontend a partir de `7eb7122`, branch
  `hotfix/gac-1233-transshipment-exit-rows` nos dois repos; frontend versão `1.1.319-hotfix.1`.

## Desenho (decisões minhas)

- **Linhas:** romaneios não cancelados dos tipos 7 (Saída), 12 (Devolução) e 13 (Perda de armazém)
  — os mesmos que `ShipmentReleasesRecalculateShippedService` conta no embarcado — cuja liberação
  tem `Origin = 3` e `PurchaseContractKey` do contrato, e que **não** têm alocação nesse contrato
  (proteção contra linha duplicada).
- **Tipo:** `Saída transbordo` (7), `Dev. transbordo` (12), `Perda transbordo` (13).
- **Origem:** `TransshipmentExit` para os três.
- **Motivo:** `Saída do transbordo 00017647 — 440 KG` (7); `Devolução ao transbordo …` (12);
  `Perda no transbordo …` (13). Sem o código do gerador: `Saída do transbordo — 440 KG`. Peso em
  pt-BR, até 3 decimais, sem zeros à direita.
- **Carga:** a do próprio romaneio (`ShipmentLoadKey`). **NF de venda:** a do romaneio
  (`SalesInvoiceKey`). **Colunas fiscais:** zeradas/nulas, como devolução e reentrega.
- **Frontend:** entrada `TransshipmentExit` em `ALLOCATION_ORIGINS` — legenda "Saída de transbordo",
  texto "Transbordo", faixa/estado `None`, ícone `sap-icon://shipping-status`; tooltip "Saída pela
  liberação de transbordo: o contrato já foi baixado pelo romaneio de compra."
- Toda tabela da consulta nova leva `WITH (NOLOCK)` (regra da aba, coberta por teste).
