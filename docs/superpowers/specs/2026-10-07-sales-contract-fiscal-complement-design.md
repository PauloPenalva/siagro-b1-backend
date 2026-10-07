# Complemento Fiscal do Contrato de Venda — design

Data: 07/10/2026. Branch `feature/sales-contract-fiscal-complement` nos dois repos, criado de `main` (que já contém a
peça 1 "confirma e depois transmite" e a peça 2 "Expedir e Faturar na carga").

Peça **2b** do trabalho de venda com NF-e na CEAGUI:

| # | Peça | Situação |
|---|---|---|
| 1 | Documento de saída confirma e depois transmite a NF-e + "Tipo de documento" | mergeada |
| 2 | "Expedir" e "Faturar" dentro do detalhe da carga | mergeada |
| 2b | Complemento fiscal do contrato de venda + trava no faturamento | **este spec** |
| 3 | Saída de estoque próprio na expedição | ciclo próprio |

## 1. O problema

O faturamento não pergunta a natureza de operação. Cada linha recebe a natureza **padrão** da base
(`SalesInvoicesUsageGuardService.ResolveDefaultUsageAsync`), e a condição de pagamento vem só do cadastro do cliente
(`SalesInvoicesCreateService`, `PaymentConditionCode ??= customer?.PaymentConditionCode`). Na CEAGUI, isso traz três
problemas:

- **Natureza errada.** As vendas usam naturezas diferentes: com suspensão de PIS/COFINS ou com PIS/COFINS a 0,93%, por
  exemplo. Toda nota de carga sai com a padrão. Como o documento nasce Confirmado (peça 1), corrigir exige Estornar →
  Editar → Confirmar.
- **Condição de pagamento.** Clientes reais (C00001, C00002) não têm condição de pagamento no cadastro. O documento nasce
  Confirmado, mas a NF-e é recusada antes de ir à SEFAZ.
- **Instrução de faturamento.** O cliente manda uma instrução de faturamento por contrato: texto para as informações
  adicionais e número do pedido de compra dele. Hoje o operador redigita isso a cada carga, e o número do pedido nem
  tem campo na NF-e (`xPed`/`nItemPed`).

O usuário do faturamento pode não ter conhecimento fiscal. A natureza e a condição precisam ser decididas por quem
tem, uma vez por contrato.

**Sucesso:** na filial que emite NF-e pelo Siagro, cada contrato de venda tem um **complemento fiscal** mantido por
quem tem permissão. O faturamento aplica esse complemento sem deixar o operador trocar a natureza nem a condição, e é
recusado quando o contrato não tem complemento.

## 2. Decisões (do usuário, em 07/10)

| # | Decisão | Motivo |
|---|---|---|
| D1 | Os dados ficam num **complemento fiscal** à parte, e não nas colunas do contrato. | Não misturar o fiscal com o comercial; permitir controle de acesso próprio. |
| D2 | Campos: **natureza de operação**, **condição de pagamento**, **informações adicionais**, **pedido do cliente** + **item do pedido**. | É o que a instrução de faturamento traz. |
| D3 | **Permissão própria para editar.** Quem não a tem **vê** o complemento, só para leitura (opção A). | O comercial enxerga o que vai sair na nota; só o fiscal muda. |
| D4 | **Contrato sem complemento com natureza e condição não pode ser faturado.** | Quem fatura pode não ter conhecimento fiscal. |
| D5 | Tudo isso só no **ambiente STANDALONE, na filial com "Emite NF-e"** (`TaxCalculationGate`). | Na Yokotobi (SAPB1) a natureza vem do SAP; a MH Agro não emite NF-e. |
| D6 | **No diálogo de faturamento, natureza e condição são só leitura.** As informações adicionais do operador continuam editáveis. | Consequência de D4: o operador não troca o que o fiscal decidiu. (Recomendação aceita na conversa; o usuário não se opôs.) |

## 3. Modelo

### 3.1 Tabela `SALES_CONTRACT_FISCAL_COMPLEMENTS` (AppDbContext)

Entidade `SalesContractFiscalComplement`, uma linha por contrato:

| Coluna | Tipo | Observação |
|---|---|---|
| `SalesContractKey` | `UNIQUEIDENTIFIER` PK | FK para `SALES_CONTRACTS` com **ON DELETE CASCADE**. Excluir o contrato não pode quebrar por causa da tabela filha (memória "tabela filha nova quebra o delete do pai"). |
| `UsageCode` | `INT NULL` | Natureza de **saída**, ativa. **Sem FK**, porque no SAPB1 a natureza vem do `OUSG` (mesmo motivo do `ITEM_COMPLEMENTS`). |
| `PaymentConditionCode` | `INT NULL` | Sem FK. A existência é validada no serviço. |
| `AdditionalInfo` | `VARCHAR(2000) NULL` | Vai para `infCpl`. |
| `CustomerOrderNumber` | `VARCHAR(15) NULL` | `xPed`. |
| `CustomerOrderItem` | `VARCHAR(6) NULL` | `nItemPed`: só dígitos, de 1 a 6. |
| `UpdatedAt` | `DATETIME2` | |
| `UpdatedBy` | `VARCHAR(100)` | |

Os campos são anuláveis no banco: o complemento pode ser salvo incompleto. A trava D4 é aplicada no **faturamento**,
não na gravação.

Ausência de linha = "sem complemento".

⚠️ Já existe um `SalesContract.Complement` (`VARCHAR(100)`, um rótulo comercial), lido como
`SalesInvoice.SalesContractComplement`. Nomes de código e de tela precisam evitar a confusão: aqui é sempre
**FiscalComplement** / "Complemento Fiscal".

### 3.2 Linha do documento de saída

`SALES_INVOICES_ITEMS` ganha:

- `CustomerOrderNumber` `VARCHAR(15) NULL`;
- `CustomerOrderItem` `VARCHAR(6) NULL`.

Os dois são copiados do complemento quando a linha é criada e entram na trava da NF-e (`SalesInvoiceNfeLock.ItemFiscalFields`).

### 3.3 Permissão

Constante `PermissionCodes.SalesContractFiscalEdit = "SALES_CONTRACT_FISCAL_EDIT"`, em `IUserPermissions.cs`.

Migration do CommonContext, no molde de `SeedWeighingManualEntryPermission`:

- SQL idempotente que insere em `PERMISSIONS` (`'SALES_CONTRACT_FISCAL_EDIT'`,
  `'Editar o complemento fiscal do contrato de venda'`);
- uma linha em `ROLE_PERMISSIONS` para `'ADMIN'`;
- no `Down`, remove as duas.

## 4. Servidor

### 4.1 Leitura e gravação do complemento

Os dois endpoints seguem o molde de `ItemsGetComplement`/`ItemsSetComplement`, com serviço próprio
`SalesContractFiscalComplementService` (`GetAsync`, `SetAsync`):

- **Function `SalesContractsGetFiscalComplement(Key=…)`.** Devolve o DTO, com o nome da natureza e o da condição para
  exibição, ou `null` sem linha. Liberada para quem acessa o contrato.
- **Action `SalesContractsSetFiscalComplement`.** Parâmetros:
  - `Key` (Guid);
  - `UsageCode` (int, opcional);
  - `PaymentConditionCode` (int, opcional);
  - `AdditionalInfo`, `CustomerOrderNumber`, `CustomerOrderItem` (string, opcionais).

  O gravar verifica, nesta ordem:
  1. `HasAsync(user, SALES_CONTRACT_FISCAL_EDIT)`, senão "Você não tem permissão para alterar o complemento fiscal do
     contrato.";
  2. o contrato existe;
  3. a natureza existe, é de saída e está ativa, senão "A natureza de operação {código} não é uma natureza de saída
     ativa.";
  4. a condição existe;
  5. `CustomerOrderNumber` tem até 15 caracteres;
  6. `CustomerOrderItem` tem só dígitos, de 1 a 6.

  Faz upsert e grava `UpdatedAt`/`UpdatedBy`. Limpar todos os campos mantém a linha vazia, o que equivale a "sem
  complemento".

### 4.2 Aplicação no documento de saída (só com o gate ativo)

Um serviço novo, `SalesInvoicesFiscalComplementApplier.ApplyAsync(invoice)`. Ele é chamado em
`SalesInvoicesCreateService` **antes** do `usageGuard.ValidateAsync` e antes da linha que copia a condição do cliente,
e também na inclusão e alteração de linha (`SalesInvoicesItemsCreateService`/`UpdateService`) de documento Pendente.

Só age quando:

- `TaxCalculationGate.IsActiveAsync(invoice.BranchCode)` é verdadeiro;
- o documento é `InvoiceType == Normal` e não é `IsNfeReturn`;
- a linha tem `SalesContractKey`.

Linhas sem contrato (avulso de propósito geral) seguem como hoje.

Para cada contrato distinto das linhas:

1. **Lê o complemento.** Sem complemento, ou sem `UsageCode` ou `PaymentConditionCode`, recusa **antes de qualquer
   gravação** com:
   "O contrato {Código} não tem complemento fiscal com natureza de operação e condição de pagamento. Peça ao fiscal
   para completá-lo."
2. **Em cada linha do contrato:**
   - `item.UsageCode = complemento.UsageCode` (**sobrescreve** o que veio no corpo, conforme D6);
   - `item.CustomerOrderNumber` e `item.CustomerOrderItem` vêm do complemento.
3. **No documento:**
   - `invoice.PaymentConditionCode = complemento.PaymentConditionCode`, sobrescrevendo o que veio no corpo. Se os
     contratos do documento tiverem condições diferentes, recusa com "Os contratos deste documento têm condições de
     pagamento diferentes no complemento fiscal.";
   - **informações adicionais:** o `AdditionalInfo` de cada contrato é juntado **antes** do texto do operador em
     `TaxPayerComments`, sem repetir o mesmo texto, separado por `" | "`, o mesmo separador do `infCpl`.

     Para não duplicar quando a regra é reaplicada (alteração de linha), o texto do contrato só é acrescentado se
     `TaxPayerComments` ainda não o contiver. Não há coluna nova para isso.

O faturamento da carga (`ShipmentBillingCreateSalesInvoiceService`) e o `/shipment-billing` passam pelo
`SalesInvoicesCreateService`, então herdam a regra. A recusa acontece antes da transação do faturamento gravar.

### 4.3 NF-e

- `NfeItem` (`SiagroB1.Fiscal/Nfe/NfeIssueInput.cs`) ganha `OrderNumber` e `OrderItem`.
- O assembler (`NfeIssueInputAssembler`) copia esses valores da linha.
- O `NfeXmlBuilder.BuildItem` escreve `prod.xPed` (até 15) e `prod.nItemPed` quando vierem preenchidos.
- O `infCpl` não muda de regra: o texto do complemento já chega por `TaxPayerComments`.

## 5. Telas

### 5.1 Detalhe do contrato de venda

Seção **"Complemento Fiscal"** no `salesContracts/Detail.view.xml`, depois de "Dados do Contrato". Fragmento novo
`SalesContractFiscalComplement.fragment.xml`.

- **Visibilidade:** só no STANDALONE (`ui>/standalone`).
- **Leitura:** um `Form` com natureza (código + nome), condição de pagamento (nome), pedido e item do pedido, e
  informações adicionais. Sem complemento, mostra o aviso "Este contrato ainda não tem complemento fiscal." e um
  `MessageStrip` informando que a filial que emite NF-e não fatura sem ele.
- **Botão "Editar":** visível só com `SessionService.hasPermission("SALES_CONTRACT_FISCAL_EDIT")`. Abre um diálogo
  com:
  - natureza: value help das naturezas de **saída** ativas;
  - condição de pagamento: value help;
  - informações adicionais: `TextArea` de 2000;
  - pedido (15) e item (6, só dígitos).

  Salvar chama a action, e a seção é relida. Os campos de value help precisam de um "Limpar" (memória:
  `valueHelpOnly` não esvazia).

### 5.2 Diálogo de faturamento (helper compartilhado, carga e `/shipment-billing`)

Só com `TaxLocked` (filial que emite NF-e). Ao escolher a liberação de venda, o diálogo lê o complemento do contrato
dela (`SalesContractsGetFiscalComplement`) e mostra, **só para leitura**:

- natureza;
- condição de pagamento;
- pedido;
- "Informações do contrato" (o `AdditionalInfo`).

O campo "Inf.Ad.Nota Fiscal" do operador continua editável e é enviado como hoje. Sem complemento completo, mostra
um `MessageStrip` de erro com o texto da recusa do servidor. A recusa de verdade é a do servidor.

Fora da filial que emite NF-e, o diálogo não muda.

### 5.3 Documento de saída avulso

Nada novo na tela. A natureza e a condição das linhas com contrato aparecem já resolvidas depois de salvar. Um
comentário no fragmento registra que o servidor sobrescreve esses valores (D6).

## 6. Fora de escopo

- Destinatário diferente do comprador (venda à ordem), local de entrega na nota, peso de origem ou destino.
- Complemento fiscal no contrato de **compra**.
- Bloquear a **confirmação** de documento antigo sem complemento: a trava vale na criação e na alteração.
- Histórico de alterações do complemento além de `UpdatedAt`/`UpdatedBy`.

## 7. Testes e verificação

Testes (xUnit + InMemory):

- **Gravação do complemento:**
  - sem permissão, recusa;
  - natureza de entrada, recusa; natureza inativa, recusa;
  - condição inexistente, recusa;
  - `nItemPed` com letra ou com 7 dígitos, recusa;
  - upsert grava `UpdatedBy`;
  - `IsAdmin` passa sem a permissão (bypass atual).
- **Aplicação:**
  - gate ativo e contrato sem complemento: recusa sem gravar nada;
  - gate ativo com complemento: a linha recebe a natureza (sobrescrevendo a do corpo) e o pedido; o documento recebe a
    condição (sobrescrevendo a do cliente) e o texto à frente das observações do operador, sem duplicar ao reaplicar;
  - dois contratos com condições diferentes: recusa;
  - gate inativo (SAPB1, MH Agro): nada muda;
  - devolução: nada muda;
  - linha sem contrato: nada muda.
- **Faturamento da carga sem complemento:** recusa sem documento.
- **NF-e:** `xPed`/`nItemPed` aparecem no XML assinado quando preenchidos e ficam ausentes quando vazios.
- **Migrations:** a de App cria as tabelas e colunas; a de Common faz o seed idempotente.

Verificação no navegador (`ceagui`, homologação, dados fictícios):

1. Usuário sem a permissão vê o complemento e não vê "Editar". O admin edita.
2. Faturar pela carga um contrato sem complemento: o diálogo avisa, e o servidor recusa.
3. Completar o complemento, com a natureza 3 e uma condição, e faturar: o diálogo mostra os dados só para leitura, e a
   NF-e autorizada sai com a natureza 3, a condição, o texto do contrato e o `xPed`.
4. Yokotobi (perfil `yktb`, se o ambiente permitir): a seção não aparece, e o faturamento segue igual.
