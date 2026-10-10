# Controle de royalties Bayer/Monsanto (baixas no ITS) — desenho

Data: 2026-10-10 · Repos: `siagro-b1-backend` + `siagro-b1-frontend` · Branch: `feature/royalty-its-write-off`

## 1. Contexto e objetivo

A soja recebida no armazém precisa ser lançada no **ITS**, o sistema da Bayer/Monsanto, onde a
carga consome crédito de tecnologia do produtor (Declarada/Testada) ou é apenas informada
(Convencional/Armazém Participante). Hoje a baixa no ITS é feita à mão e o SiagroB1 não sabe o que
já foi baixado. Não há nada de royalty no código: o único parente é `TechnologyType`
(`None/Intacta/Conventional`) no contrato de compra, que não é usado aqui.

Objetivo, começando pelo módulo de **Armazenagem**:

1. Apurar, na entrada, a **situação de royalty**, a **origem** da carga e o **código do teste**.
2. Listar o que falta baixar no ITS (tela + PDF), para o operador fazer a baixa no site.
3. Registrar no SiagroB1 a baixa feita no ITS (Data, ID do ITS, CPF/CNPJ, Nome) e abater o saldo.

Decisões do usuário (brainstorming 10/10/2026):

- **Situações**: Declarada, Testada, Convencional e **Armazém Participante** (carga vinda de outro
  armazém participante do programa). Declarada e Testada consomem crédito no ITS; Convencional e
  Armazém Participante são só informados — mas **as quatro são lançadas no ITS** e têm o mesmo
  controle de baixa no SiagroB1.
- **Uma baixa cobre vários romaneios e pode ser parcial**: relação N:N com peso por linha.
- **Base = peso líquido** do romaneio (após descontos de umidade, impureza e outros).
- **Onde se apura**: no Ticket de Pesagem (classificação); copia para o romaneio ao concluir o
  ticket; editável no romaneio.
- **Código do teste**: campo próprio, obrigatório na situação Testada. Não há campo de resultado
  positivo/negativo.
- **Origem da carga**: obrigatória em todas as situações; value help de parceiros de negócio, no
  mesmo molde do local de entrega (`DeliveryLocationCode/Name`).
- **Escopo por produto**: indicador no **complemento do produto** (`ITEM_COMPLEMENTS`).
- **Romaneio de Compra (carregamento em terceiro) também entra no controle** (decisão de
  10/10/2026, depois do primeiro desenho). Majoritariamente carregamento em armazém participante,
  às vezes em fazenda. A **origem da compra é o armazém do carregamento**, já gravado no romaneio
  (`WarehouseCode`/`WarehouseName`) — copiada pelo backend, somente leitura na tela. Situação
  obrigatória; padrão **Armazém Participante** quando o armazém está marcado como participante
  (`WAREHOUSE_COMPLEMENTS.IsParticipant`). Informada na Expedição de Grãos, no "Expedir" da
  Montagem de Carga, na Entrada em armazenagem própria e no romaneio manual de Compra.
- **Entrada em armazenagem própria conta uma vez só, pela Compra**: o Recebimento clonado dela fica
  fora da lista. Transferência de titularidade e Conferência de saldo de armazém (movimentos de
  papel) ficam fora.

Fora do escopo: integração automática com o ITS, valor em R$/cobrança do produtor, saldo de crédito
do produtor, royalties nos contratos de compra.

Entra junto (pedido do usuário): correção do bug da chave `"Conventinal"` no Select de "Tipo
Tecnologia" do contrato de compra, que não bate com `TechnologyType.Conventional` — escolher
"Convencional" falhava ao gravar. Ver seção 5.

## 2. Modelo de dados

### 2.1 Enum `RoyaltySituation` (Domain/Enums)

```csharp
public enum RoyaltySituation
{
    Declared = 0,             // Declarada
    Tested = 1,               // Testada
    Conventional = 2,         // Convencional
    ParticipantWarehouse = 3  // Armazém Participante
}
```

Gravado como INT (padrão do projeto, sem `HasConversion`); no OData trafega como nome do membro.

### 2.2 Complemento do produto — `ITEM_COMPLEMENTS`

| Propriedade | Coluna | Tipo | Tela |
|---|---|---|---|
| `ControlsRoyalties` | `ControlsRoyalties` | `bit NOT NULL DEFAULT 0` | "Controla royalties (ITS)" |

Produto sem complemento = não controla.

### 2.3 Ticket de Pesagem e Romaneio — `WEIGHING_TICKETS` e `STORAGE_TRANSACTIONS`

Mesmos quatro campos nas duas tabelas:

| Propriedade | Tipo | Regra |
|---|---|---|
| `RoyaltySituation` | `RoyaltySituation?` | obrigatório se o produto controla royalties |
| `RoyaltyTestCode` | `VARCHAR(50)` nulo | obrigatório se situação = Testada; limpo nas demais |
| `RoyaltyOriginCode` | `VARCHAR(10)` nulo | obrigatório se o produto controla royalties |
| `RoyaltyOriginName` | `VARCHAR(200)` nulo | preenchido junto com o código |

Padrão da origem ao criar o ticket: o próprio parceiro do ticket (`CardCode/CardName`), editável.

Os campos acompanham o romaneio em **todos os caminhos que o constroem a partir de outro**:
conclusão do ticket (`WeighingTicketsCompletedService`), cópia (`StorageTransactionCopyFactory`),
entrada em armazenagem própria (`StorageEntryReceiptFactory`), estorno
(`StorageTransactionsReverseService`) e troca de liberação
(`ShippingTransactionsChangeReleaseService`).

### 2.4 Baixa ITS — `ROYALTY_WRITE_OFFS`

| Propriedade | Tipo | Observação |
|---|---|---|
| `DocEntry` | int identity | chave |
| `WriteOffDate` | `DATE NOT NULL` | Data da baixa; padrão = hoje (data local) |
| `ItsId` | `VARCHAR(50) NOT NULL` | ID do ITS; único entre baixas **ativas** (índice filtrado) |
| `TaxId` | `VARCHAR(18) NOT NULL` | CPF/CNPJ usado na baixa do crédito, validado como CPF ou CNPJ |
| `Name` | `VARCHAR(200) NOT NULL` | Nome |
| `Situation` | `RoyaltySituation NOT NULL` | todas as linhas têm a mesma situação |
| `Status` | `RoyaltyWriteOffStatus` (`Active = 0, Cancelled = 1`) | |
| `Comments` | `VARCHAR(500)` nulo | |
| auditoria | padrão do projeto | criado/alterado/cancelado por e em |

`TaxId` e `Name` são digitados, não derivados do romaneio: o crédito pode ser de outro produtor.

### 2.5 Linhas da baixa — `ROYALTY_WRITE_OFF_LINES`

| Propriedade | Tipo | Observação |
|---|---|---|
| `DocEntry` + `LineId` | chave composta | padrão de linhas do projeto |
| `StorageTransactionKey` | int, FK `STORAGE_TRANSACTIONS` (Restrict) | romaneio baixado |
| `Weight` | `DECIMAL(18,2) NOT NULL` | kg; > 0 |

Índice em `StorageTransactionKey` incluindo `Weight` (o saldo é calculado por SUM).

### 2.6 Saldo a baixar

`Saldo = NetWeight − SUM(Weight das linhas de baixas Active)` do romaneio. Calculado, não gravado.
Se o volume pedir, migra-se para o padrão derivado-e-persistido depois.

**Romaneios elegíveis** ao controle: `TransactionStatus` em `Confirmed` ou `Invoiced`, produto com
`ControlsRoyalties`, `RoyaltySituation` preenchida (recebimentos/compras anteriores ao recurso ficam
fora) e:

- `TransactionType = Receipt`, **exceto** o Recebimento de uma Entrada em armazenagem própria
  (`STORAGE_ENTRY_TRANSACTIONS.ReceiptStorageTransactionKey`) — a carga conta pela Compra;
- `TransactionType = Purchase`, **exceto** a da Conferência de saldo de armazém
  (`TransactionOrigin = WarehouseReconciliation`) e a compra original de uma Troca de liberação
  (`SHIPPING_RELEASE_CHANGES.OriginalPurchaseStorageTransactionKey`), que é espelhada por um estorno
  e substituída pela compra nova. A compra da Transferência de titularidade nasce sem situação e
  fica fora pela regra geral.

Ficam de fora cancelados, estornados (`Returned`) e recebimentos de transbordo
(`TransshipmentReceipt`). Uma baixa pode misturar Compras e Recebimentos da mesma situação.

## 3. Regras de negócio

1. **Ticket**: com produto que controla royalties, concluir exige situação + origem, e código do
   teste quando Testada.
2. **Romaneio**: confirmar um Recebimento com produto que controla royalties exige o mesmo.
   Confirmar uma **Compra** também (exceto a da Conferência de saldo de armazém): antes de validar,
   o backend copia o armazém do carregamento para a origem e, sem situação informada, assume
   Armazém Participante quando o armazém é participante.
2a. **Troca de liberação**: recusada se a compra original tiver baixa ITS ativa (cancele a baixa
   antes); a compra nova herda situação, origem e código do teste.
3. **Baixa — criação**:
   - pelo menos uma linha; peso > 0 em todas;
   - todos os romaneios elegíveis e com a **mesma situação** da baixa;
   - por romaneio, a soma das linhas ativas (incluindo a nova) ≤ `NetWeight`;
   - `ItsId` não usado em outra baixa ativa;
   - validação e gravação na mesma transação (`BeginTransaction` explícito).
4. **Baixa — cancelamento**: muda para `Cancelled`, devolve o saldo. Não há edição de baixa:
   errou, cancela e refaz.
5. **Romaneio com baixa ativa** não pode ser estornado, cancelado nem ter situação, origem, código
   do teste ou produto alterados.

Mensagens (pt-BR):

- "Informe a situação de royalty e a origem da carga."
- "Informe o código do teste para a situação Testada."
- "O peso baixado excede o saldo do romaneio {Code} (saldo {saldo} kg)."
- "Selecione romaneios de uma mesma situação."
- "ID do ITS {ItsId} já utilizado na baixa {DocEntry}."
- "Romaneio possui baixa ITS ativa ({ItsId}); cancele a baixa antes."
- "CPF/CNPJ inválido."

## 4. Backend

- `Application/Services/RoyaltyWriteOffs/`: `RoyaltyWriteOffsCreateService`,
  `RoyaltyWriteOffsCancelService`, `RoyaltyPendingQueryService` (pendentes com saldo, filtros e
  totais).
- Validação da situação nos serviços de conclusão do ticket e de confirmação do romaneio; trava de
  baixa ativa nos serviços de estorno, cancelamento e update do romaneio.
- OData: entidade `RoyaltyWriteOffs` (com `Lines`); action `Cancel`; função/endpoint de pendentes.
  Navegações e late properties declaradas à mão quando necessário.
- Migrations (contexto da empresa): colunas novas em `ITEM_COMPLEMENTS`, `WEIGHING_TICKETS`,
  `STORAGE_TRANSACTIONS`; tabelas novas; índice filtrado do `ItsId` (com `QUOTED_IDENTIFIER`).
- Migration de menu (`MENU_ITEMS` + `ROLE_MENUS`): "Royalties ITS" sob Armazenagem.
- Relatório: `.frx` novo em `SiagroB1.Reports` — pendentes agrupados por situação e parceiro, com
  subtotais de peso líquido, baixado e saldo; mesmos filtros da tela.

## 5. Frontend

- **Complemento do produto**: checkbox "Controla royalties (ITS)".
- **Ticket de Pesagem** (form/classificação): Select "Situação Royalty", value help "Origem"
  (parceiros, molde do local de entrega), "Código do Teste" (habilitado só em Testada). Visíveis só
  quando o produto controla royalties.
- **Romaneio** (Edit/Detail): os mesmos campos, travados se houver baixa ativa; seção "Baixas ITS"
  no Detail (peso líquido, baixado, saldo e lista: Data, ID do ITS, CPF/CNPJ, Nome, Peso).
- **Romaneio de Compra** (Expedição de Grãos, "Expedir" da Montagem de Carga, Entrada em
  armazenagem própria, romaneio manual): Select "Situação Royalty" e "Código do Teste"; sem campo
  de origem (é o armazém do carregamento, já na tela). Vazio = Armazém Participante quando o
  armazém é participante.
- **Tela "Royalties ITS"**, duas abas (Pendentes e PDF ganham a coluna **Tipo**: Recebimento/Compra):
  - **Pendentes**: filtros período, parceiro, origem, produto, situação, lote; colunas Romaneio,
    Data, Parceiro, Origem, Produto, Situação, Código do Teste, Peso Líquido, Baixado, Saldo;
    totais por situação; ações **Exportar PDF** e **Baixar selecionados**.
  - **Baixas**: filtros data, ID do ITS, CPF/CNPJ, situação; detalhe com linhas e ação **Cancelar**.
- **Diálogo "Registrar baixa ITS"**: cabeçalho (Data, ID do ITS, CPF/CNPJ, Nome, Observações);
  linhas pré-preenchidas com peso = saldo, editáveis para baixa parcial; total no rodapé. Seleção com
  situações diferentes é recusada antes de abrir.
- **Contrato de compra — correção**: em `PurchaseContractForm.fragment.xml`, a chave do item
  "Convencional" passa de `Conventinal` para `Conventional`. O enum é gravado como INT, então não
  há dado a sanear; commit próprio (`fix(purchase-contract)`), separado do recurso. Verificação:
  criar/editar um contrato com "Convencional", gravar e reabrir mostrando o valor.

## 6. Testes e verificação

`SiagroB1.Application.Tests`:

- conclusão do ticket e confirmação do romaneio exigem situação/origem (e código em Testada) só
  para produto que controla royalties;
- cópia dos campos ticket → romaneio e nos caminhos de cópia, entrada própria, estorno e troca de
  liberação;
- baixa com várias linhas; baixa parcial em duas baixas; recusa acima do saldo; recusa com situação
  misturada; recusa de romaneio não elegível; `ItsId` duplicado entre ativas;
- cancelamento devolve o saldo e libera o `ItsId`;
- estorno/cancelamento/alteração do romaneio bloqueados com baixa ativa.

Verificação no navegador pelo caminho do usuário: complemento do produto → ticket (situação,
origem, teste) → romaneio confirmado → Pendentes → baixa parcial → saldo → PDF → cancelamento.
