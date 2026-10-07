# Expedir e Faturar dentro da Carga — design

Data: 06/10/2026. Branch `feature/shipment-load-ship-and-bill` nos dois repos, criado de `main` (que já contém a
peça 1, `2026-10-06-sales-invoice-confirm-then-transmit-design.md`).

Peça **2 de 3** do trabalho decomposto em 06/10:

| # | Peça | Situação |
|---|---|---|
| 1 | Documento de saída confirma e depois transmite a NF-e + "Tipo de documento" | mergeada em `main` |
| 2 | "Expedir" e "Faturar" dentro do detalhe da carga | **este spec** |
| 3 | Saída de estoque próprio na expedição (sem perna de compra) | ciclo próprio |

## 1. O problema

A Montagem de Carga foi desenhada para a Yokotobi: muitos usuários, papéis separados (a Logística planeja, a balança
pesa, o escritório fatura). Um caminhão passa hoje por quatro telas:

1. **Carga** (`/shipment-loads`): a carga planejada.
2. **Expedição de Grãos**, em três passos:
   - saldo por produto;
   - escolha da liberação de compra;
   - formulário com filial, data, armazém, motorista, placa e peso bruto digitado.

   O romaneio nasce confirmado, com o par `Purchase(8)`/`SalesShipment(7)`, mas **sem carga**.
3. **Vincular Romaneios**: página separada, aberta pela lista de cargas.
4. **Faturamento da Expedição** (`/shipment-billing`): diálogo com a liberação de venda, a quantidade, o frete, o
   endereço e o tipo de documento.

A CEAGUI tem poucos usuários: a mesma pessoa faz tudo. O detalhe da carga não tem nem "Expedir" nem "Faturar".

**Sucesso:** a partir do detalhe da carga, uma pessoa expede, com o romaneio já nascendo vinculado, e fatura, com o
documento de saída nascendo confirmado e do tipo NF-e ou Outro (peça 1), sem sair da tela.

## 2. Decisões (do usuário, em 06/10)

| # | Decisão | Motivo |
|---|---|---|
| D1 | A carga continua visível e pode ter N romaneios e N documentos; as ações ficam **dentro do detalhe da carga**. | Às vezes um caminhão leva mais de uma origem ou é faturado em mais de um documento. |
| D2 | As ações valem **para todos os clientes**, sem configuração nova. As telas antigas continuam existindo. | Menos interruptores; a Yokotobi ganha o atalho sem perder nada. |
| D3 | **Peso digitado à mão**, como no formulário atual da Expedição. | É como a CEAGUI trabalha. |
| D4 | **Abordagem 1:** uma ação nova no servidor, "Expedir na carga", **numa transação só** (expedição + vínculo). | Nada fica pela metade: ou o romaneio nasce vinculado, ou nada é gravado. |
| D5 | **Faturar** reaproveita o diálogo do `/shipment-billing`, com a lógica **extraída para um helper compartilhado**. | Uma regra só para as duas telas, sem cópia de código. |
| D6 | **Fora desta peça:** análise de qualidade na expedição e origem "estoque próprio" (peça 3). | Escopo. |

## 3. Servidor — "Expedir na carga"

### 3.1 Ação

Action OData `ShipmentLoadsShip` (controller em `SiagroB1.Web/Actions/ShipmentLoads/`, padrão das demais actions de
carga). O serviço é `ShipmentLoadsShipService` (`SiagroB1.Application/Services/ShipmentLoads/`), registrado no bloco
de cargas do `ServiceCollectionExtensions`.

Parâmetros:

| Parâmetro | Tipo | Obrigatório | Observação |
|---|---|---|---|
| `Key` | Guid | sim | a carga |
| `ShipmentReleaseKey` | Guid | sim | liberação de compra (embarque) |
| `PurchaseContractKey` | Guid? | não | nulo só nas origens sem perna de compra (`ReleaseOriginRules.ShipsWithoutPurchaseLeg`); a expedição atual já faz essa recusa |
| `WarehouseCode` | string | sim | sugerido da carga, editável |
| `TruckDriverCode` | string | sim | sugerido da carga, editável |
| `TransactionDate` | DateTimeOffset | sim | |
| `GrossWeight` | double | sim | > 0 |
| `Comments` | string? | não | |

Retorno: a carga atualizada, com a chave do romaneio de saída criado (DTO pequeno, por exemplo
`{ ShipmentLoadKey, StorageTransactionKey, StorageTransactionCode }`).

### 3.2 O que o serviço faz

1. Lê a carga. Recusa, **antes de qualquer escrita**, com
   "A carga {Code} não aceita expedição nesta situação." se:
   - ela não for do tipo `Normal`;
   - ou a situação não for `Planned`, `Open` ou `InTransshipment` (as mesmas que o vínculo aceita hoje).
2. Monta o `StorageTransaction` de compra como o formulário da Expedição monta hoje:
   - **da carga:** `TruckCode`, `ItemCode`, `BranchCode`, `UnitOfMeasureCode`;
   - **da liberação:** o parceiro (`CardCode`/`CardName`);
   - **dos parâmetros:** os demais campos;
   - **numeração:** `DocNumberKey` da numeração padrão de romaneio.
3. Abre **uma** transação e, dentro dela:
   1. chama `ShippingTransactionsCreateService` em modo `Deferred` (cria o par, baixa o contrato de compra e a
      liberação, como hoje);
   2. chama `ShipmentLoadsAttachTransactionsService` em modo `Deferred` com a chave da saída criada (grava
      `ShipmentLoadKey`, recalcula o total e o saldo da carga, registra o movimento);
   3. confirma a transação.
4. Depois do commit, faz o pós-processamento que a expedição já faz hoje depois do commit
   (`recalcShipped.RecalculateAsync(ShipmentReleaseKey)`).
5. Qualquer falha desfaz tudo: nem romaneio, nem alocação, nem vínculo.

### 3.3 Mudança nos serviços existentes

`ShippingTransactionsCreateService.ExecuteAsync` e `ShipmentLoadsAttachTransactionsService.ExecuteAsync` ganham um
parâmetro opcional `CommitMode commitMode = CommitMode.Auto`. No modo `Deferred`:

- não abrem, não confirmam nem desfazem transação (quem chama é o dono dela);
- continuam chamando `SaveChangesAsync` onde hoje chamam, porque o recálculo da carga lê do banco
  (memória: `ShipmentLoadsRecalculateTotalService` precisa das FKs gravadas);
- o pós-commit da expedição (`recalcShipped`) **não roda** no `Deferred`, e quem chama faz isso depois do seu commit.

No modo `Auto`, que é o das telas atuais, nada muda.

## 4. Frontend — detalhe da carga

### 4.1 Expedir

Botão **"Expedir"** no cabeçalho de `shipmentLoads/Detail.view.xml`. Ele só aparece para carga `Normal` em
`Planned`/`Open`/`InTransshipment`, e o servidor recusa igual.

Diálogo (fragmento novo `shipmentLoads/fragments/ShipDialog.fragment.xml`):

- **Tabela de liberações de compra** com saldo para o produto e o armazém da carga, pela mesma function que a
  Expedição usa: `ShipmentReleasesGetPurchaseContracts(ItemCode, WarehouseCode)`. Colunas: contrato, fornecedor,
  origem e saldo.
- **Armazém:** value help, padrão da carga. Ao trocar, recarrega as liberações.
- **Motorista:** value help, padrão da carga.
- **Data:** padrão hoje.
- **Peso bruto:** obrigatório, > 0.
- **Observação.**
- **Rodapé:** Cancelar / Expedir. Trava contra duplo clique: a flag de "em andamento" é setada **antes** do primeiro
  `await`, como o faturamento faz.

Ao salvar, chama `ShipmentLoadsShip`. Com sucesso:

- fecha o diálogo e mostra uma toast "Romaneio {Code} expedido na carga.";
- atualiza a carga (cabeçalho, romaneios, saldo).

Com erro, mostra a mensagem do servidor e deixa o diálogo aberto.

### 4.2 Faturar

Botão **"Faturar"** no cabeçalho: carga `Normal` com saldo a faturar (`AvailableQuantity > 0`).

- A lógica do diálogo de faturamento sai de `shipmentBilling/Main.controller.ts` para um helper compartilhado
  (por exemplo `webapp/helpers/ShipmentBillingDialog.ts`):
  - abrir com uma carga;
  - carregar filiais e liberações de venda (`SalesShipmentReleasesGetAvailable`);
  - "Tipo de documento" e `TaxLocked`;
  - salvar com `ShipmentBillingCreateSalesInvoice`;
  - trava de duplo clique;
  - fechar.
- O fragmento `Billing.fragment.xml` continua sendo o mesmo.
- O helper recebe o controller dono, para os value helps herdados de `CommonController` e o `addDependent`, e uma
  callback "depois de faturar". O `/shipment-billing` passa a usá-lo **sem mudar o comportamento**.
- No detalhe da carga, depois de faturar, atualiza a carga e a tabela "Documentos de Saída". O documento nasce
  Confirmado. Na filial que emite NF-e, o "Transmitir NF-e" fica no documento (peça 1).

## 5. Erros

| Situação | Comportamento |
|---|---|
| Carga de Remoção, Cancelada, Faturada, Concluída ou Recusada | Recusa antes de escrever: "A carga {Code} não aceita expedição nesta situação." |
| Sem liberação, peso ≤ 0, contrato faltando, liberação pausada/encerrada | As recusas que a expedição já faz (mensagens atuais). |
| Falha no vínculo depois da expedição | A transação inteira é desfeita. |
| Placa/produto/filial divergentes | Impossível: vêm da carga. |
| Duplo clique | Trava no diálogo. |
| Concorrência (outra pessoa mexe na carga) | `RowVersion` da carga e do romaneio derrubam o segundo `SaveChanges`; a transação é desfeita. |

## 6. Testes e verificação

Testes (`SiagroB1.Application.Tests`, xUnit + EF InMemory):

- `ShipmentLoadsShipService`:
  - **caso feliz:** o romaneio `SalesShipment` nasce com `ShipmentLoadKey` da carga; o total da carga sobe; o
    contrato de compra tem a alocação;
  - **carga Cancelada ou de Remoção:** recusa, e nenhum romaneio ou alocação é gravado;
  - **falha no vínculo:** nenhum romaneio fica gravado. Exemplo: carga muda de situação entre a leitura e o vínculo;
    no teste, um serviço de vínculo falso que lança.
- `ShippingTransactionsCreateService` e `ShipmentLoadsAttachTransactionsService` no modo `Deferred`: não chamam
  `BeginTransaction`/`Commit`. Os testes atuais seguem verdes no modo `Auto`.
- Frontend: `ts-typecheck`, `lint`. A validação pura do diálogo de expedição (peso > 0, liberação escolhida) ganha
  teste QUnit se ficar num helper.

Verificação no navegador (`ceagui` / `CEAGUI_SIAGRO_DEV`, NF-e em homologação, **só dado fictício**). O banco não tem
contratos, então a verificação cria:

- contrato de compra aprovado + liberação de embarque;
- contrato de venda aprovado + liberação de entrega;
- carga planejada.

Roteiro:

1. **Expedir** pela carga: o romaneio aparece na carga, e a carga passa a Carregada.
2. **Faturar** pela carga com tipo NF-e: o documento nasce Confirmado, e as liberações de compra e de venda baixam.
3. **Transmitir** a NF-e no documento.
4. **Expedir em carga cancelada:** recusa.
5. **`/shipment-billing`:** continua funcionando igual.

## 7. Fora de escopo

- Origem "estoque próprio" (peça 3).
- Análise de qualidade na expedição.
- Ticket de balança na expedição.
- Esconder as telas antigas.
