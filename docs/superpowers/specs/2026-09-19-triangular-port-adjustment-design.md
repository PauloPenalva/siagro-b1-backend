---
status: desenho aprovado 19/09/2026 (revisão: romaneio no lugar de NF) — planos escritos, implementação pendente
---

# Design — Troca nota: romaneios de quebra/sobra de descarga no contrato de compra CIF

## Contexto

Na **troca nota**, a Yokotobi compra soja de um fornecedor (cerealista, raramente produtor) com frete
**CIF fornecedor**. O fornecedor entrega direto no porto/cliente; ele emite a NF de venda contra a
Yokotobi, e a Yokotobi emite a NF de venda contra o contrato do cliente.

A operação **já é coberta** pelo sistema e não muda:

1. carga com frete 0;
2. Expedição pela liberação do contrato de compra do fornecedor
   (`ShippingTransactionsCreateService`): par Compra(8) + Saída(7) no local de entrega da
   liberação, com saldo de armazém líquido **zero**, e alocação do contrato de compra pelo peso de
   origem;
3. vínculo da Expedição à carga;
4. faturamento da carga (`ShipmentBillingCreateSalesInvoiceService`), que consome o contrato de venda.

A lacuna está na **liquidação da compra**. O fornecedor responde pelo resultado do porto (quebra de
peso **e** desconto de qualidade), acertado por devolução ou complemento. Hoje o resultado do porto —
digitado na Conferência de Entregas (`SalesInvoiceItem.DeliveredQuantity` / `QuantityLoss`) — só
ajusta o contrato de **venda**. O contrato de compra fica alocado pelo peso de origem, sem desconto.

## Decisões do usuário (19/09/2026)

1. O acerto com o fornecedor é um **romaneio** — nunca um lançamento solto no contrato. As NFs de
   devolução/complemento virão no futuro, **sempre em cima do romaneio**.
2. **Dois tipos novos**, em vez de reusar Devolução de Compra(9) / Complemento de Quantidade(10):
   - a 9 entra no saldo de armazém (`−9`) e deixaria negativo o armazém do par 8/7, onde o grão nunca
     esteve (mesmo erro que o GAC-1164 corrigiu);
   - a 10 é alocada com volume **0** (`PurchaseContractsAllocationCreateService.cs:100`), e mudar isso
     mexe em quem já usa o tipo.
3. **Repasse integral** do líquido reconhecido pelo cliente: diferença = `NetQuantity − Quantity`,
   sem recalcular desconto pela tabela de custos do contrato de compra.
4. Só vale para contrato de compra com **`FreightTerms = Cif`** (`PurchaseContract.cs:52`). Expedição
   de grão comprado dentro de armazém (FOB) não é afetada: lá a quebra no porto é da Yokotobi.
5. **Quebra reabre o saldo do contrato** de compra (o fornecedor fica devendo); sobra reduz. A
   **liberação não é reaberta**: ela registra o que foi embarcado; se o fornecedor for completar,
   cria-se uma liberação nova. Quem não for completar encerra o contrato pelo fluxo existente.
6. **Contrato de compra Encerrado recusa** o encerramento (e o estorno) da conferência.

## 1. Tipos de romaneio

`StorageTransactionType` ganha:

| Valor | Nome | Tela | Alocação no contrato de compra |
|---|---|---|---|
| 16 | `PurchaseDischargeLoss` | Quebra de descarga (compra) | volume **negativo** |
| 17 | `PurchaseDischargeGain` | Sobra de descarga (compra) | volume positivo |

O valor 15 fica reservado para o transbordo (GAC-1181, pausado).

Efeitos, por construção:

- **Saldo de armazém não muda** — `StorageTransactionsWarehouseBalanceService` soma uma lista fixa
  de tipos (`+8 +12 +14 −7 −9 −13`), e o mesmo vale para os demais filtros de tipo (todos por lista
  de inclusão; nenhum `NOT IN` / `!=` genérico no código).
- **Liberação não muda** — `ShipmentReleasesRecalculateShippedService` conta só `8 − 9`.
- **Contrato de compra muda pela alocação** — `AllocatedVolume` é a soma com sinal de
  `PURCHASE_CONTRACTS_ALLOCATIONS.Volume`, e `AvaiableVolume = Total − Allocated − WashedOut`. Não
  há campo novo no contrato.

Campos do romaneio gerado: `TransactionType` 16/17, `TransactionStatus = Confirmed`,
`TransactionOrigin = PurchaseDischarge` (novo `TransactionCode = 14`, dono único — as telas de
Romaneios recusam cancelar/estornar), `CardCode` = fornecedor, `BranchCode`/`ItemCode`/`UoM`/
`WarehouseCode`/`TruckCode` da Compra(8) de origem, `NetWeight = GrossWeight = |ajuste|`,
`TransactionDate` = data do encerramento da conferência.

Vínculos em **duas colunas novas**, FK `NoAction` e sem navegação:
`DischargeSalesInvoiceItemKey` (o item conferido) e `DischargeShippingTransactionKey` (a Expedição
acertada). O romaneio **nunca** recebe `ShipmentLoadKey`, `ShipmentReleaseKey` nem `SalesInvoiceKey`:
mais de 20 serviços de carga consultam por `ShipmentLoadKey` sem filtrar tipo, e a liberação/nota
passariam a contá-lo. Carga e liberação são alcançadas pelos vínculos.

O extrato de armazém (`SiagroB1.Reports`, `StorageStatementReportService`) exclui 16/17.

## 2. Cálculo

Para um item conferido e **encerrado**:

1. `diff = NetQuantity − Quantity`, com `NetQuantity = DeliveredQuantity − QuantityLoss`
   (`SalesInvoicesItemsUpdateService.cs:81`). Negativo ⇒ quebra (16); positivo ⇒ sobra (17); zero ⇒
   nada.
2. Expedições da carga: nota → `SalesInvoice.ShipmentLoadKey` → Saídas(7) com esse
   `ShipmentLoadKey` → `SHIPPING_TRANSACTIONS` (`SalesStorageTransactionKey`) → Compra(8)
   (`PurchaseStorageTransactionKey`) → `PURCHASE_CONTRACTS_ALLOCATIONS` → contrato de compra.
3. Participação de cada Expedição = `NetWeight(7) / Σ NetWeight(7 da carga)` — sobre **todas** as
   Saídas da carga, CIF ou não.
4. Para cada Expedição cujo contrato é **CIF**: volume = `|diff| × participação`, arredondado a 3
   casas; o resto do arredondamento vai para a maior participação. Um romaneio por Expedição.
5. Nenhuma Expedição CIF ⇒ nenhum romaneio, sem erro.

A participação cobre carga com várias notas/itens: cada item distribui a própria diferença pela mesma
proporção física da carga.

## 3. Ciclo de vida

Serviço novo `PurchaseDischargeTransactionsSyncService`, chamado no gancho de
`SalesInvoicesItemsUpdateService` (`:80-92`, quando `deliveryChanged`) em dois passos:
`PrepareAsync` (lê e roda as travas, sem gravar — antes de tocar no item) e `ApplyAsync` (depois dos
recálculos da venda, na mesma transação). Só itens de nota **Normal** faturada por carga geram 16/17.

`SalesInvoicesReverseConfirmService` **não** precisa de gancho: ele só zera a conferência de itens de
nota de RETORNO, que nunca geram 16/17. O item de nota Normal continua encerrado após o estorno da
confirmação, e o acerto da compra continua valendo.

O `ApplyAsync`:

1. **cancela** os romaneios 16/17 ativos do item e remove as alocações deles;
2. se o item está `Closed`, gera os novos pela regra da seção 2 e os aloca;
3. recalcula o saldo dos contratos afetados (antigos ∪ novos).

Reencerrar com outro peso = cancelar e gerar de novo. Não há edição no lugar. Romaneios cancelados
ficam no histórico.

### Alocação

A alocação dos tipos 16/17 é um método próprio, `PurchaseContractsAllocationCreateService.ExecuteDischargeAsync`
(recebe volume positivo e grava −/+ conforme o tipo), com as travas ajustadas ao tipo:

- a trava "volume maior que o saldo do contrato" **não** se aplica à quebra (ela devolve saldo);
- a sobra respeita o saldo do contrato — sobra acima do saldo recusa a conferência com mensagem de
  negócio;
- duas linhas no mesmo contrato na mesma operação exigem `SaveChangesAsync` por linha (o recálculo
  por SUM não enxerga alocação rastreada).

### Travas (antes de qualquer gravação)

| Situação | Resultado |
|---|---|
| Contrato de compra CIF afetado está `Finished` (encerrar, reencerrar ou estornar) | recusa: "O contrato de compra {Code} está encerrado; reabra-o para conferir esta entrega." |
| Sobra maior que o saldo do contrato de compra | recusa: "A sobra de descarga ({volume}) excede o saldo do contrato de compra {Code}." |
| Excluir item/nota, **cancelar a nota** ou **estornar a Expedição** com romaneio 16/17 ativo | recusa: "…tem quebra/sobra de descarga lançada no contrato de compra. Reabra a conferência de entregas antes." (FK NoAction daria 547 → 500 mudo, e o acerto ficaria órfão) |

As recusas saem como `BadRequestODataResult` (mensagem de negócio visível no UI5, não
"Communication error").

Os romaneios 16/17 **não** são estornáveis nem editáveis por tela própria: o único dono deles é a
conferência.

## 4. Telas

### Entregas do contrato de compra (Detalhe)

- `PurchaseContractsGetAllocationsByContractService`: o `CASE` de sinal do volume passa a incluir
  16 (negativo) e 17 (positivo), e `Origin` ganha `DischargeLoss` / `DischargeGain`, com nota de
  venda e carga.
- `ALLOCATION_ORIGINS` (`helpers/AllocationOriginHelpers.ts`) ganha "Quebra de descarga" e "Sobra de
  descarga" (faixa, coluna Origem, legenda); nota e carga no tooltip.

### Tela nova "Ajustes de Descarga" (grupo Compras)

- Lista dos romaneios 16/17: tipo, fornecedor, contrato de compra, carga, nota de venda, data,
  volume, situação (ativo/cancelado).
- FilterBar: fornecedor, contrato, carga, período, tipo; cancelados ocultos por padrão.
- Somente leitura nesta entrega (a NF virá depois, em cima do romaneio).
- Migration de menu: `MENU_ITEMS` (Key = rota) + `ROLE_MENUS` para ADMIN.

### Conferência de Entregas

Nenhuma mudança de layout; garantir que as recusas da seção 3 apareçam como mensagem de negócio.

### Rótulos

Os nomes dos tipos 16/17 entram onde o tipo de romaneio é traduzido (formatters/i18n do frontend e
qualquer rótulo do backend usado em relatório/log).

## 5. Fora de escopo

- NF de devolução/complemento do fornecedor (futuro, em cima dos romaneios 16/17).
- Descontar a quebra no pagamento ao fornecedor (depende do Netting, Fase 3 do financeiro).
- Natureza fiscal / CFOP de venda à ordem (Fase 2 do roadmap fiscal).
- Capturar número/chave da NF do fornecedor no formulário da Expedição.
- Dispensar a transportadora obrigatória no faturamento da carga.

## 6. Testes

xUnit + EF InMemory (`SiagroB1.Application.Tests`):

- rateio com duas Expedições CIF e uma FOB na mesma carga (soma exata, resto do arredondamento);
- carga sem Expedição CIF não gera romaneio;
- quebra gera 16, aloca negativo e reabre `AvaiableVolume`; sobra gera 17 e reduz;
- saldo de armazém e embarcado da liberação **inalterados** após 16/17;
- reencerrar com outro peso cancela e regera;
- estorno da conferência e estorno da confirmação cancelam os romaneios e as alocações;
- contrato Finished recusa encerrar e estornar;
- sobra acima do saldo do contrato recusa;
- duas Expedições no mesmo contrato somam certo;
- a lista "Ajustes de Descarga" filtra e oculta cancelados.

Verificação no navegador (profile `yktb` + `yarn start:dev`): carga com frete 0 → Expedição por
liberação de contrato de compra CIF → faturar → encerrar conferência com quebra → contrato de compra
mostra "Quebra de descarga" nas Entregas e o saldo reaberto → armazém e liberação inalterados →
romaneio aparece em "Ajustes de Descarga" → estornar a conferência → romaneio cancelado e saldo
volta → com o contrato encerrado, a conferência é recusada com mensagem de negócio.
