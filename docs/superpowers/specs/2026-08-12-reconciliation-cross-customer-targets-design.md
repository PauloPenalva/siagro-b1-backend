# Conciliação de entrega para contrato de outro cliente

**Data:** 12/08/2026
**Tela:** `/sales-contracts/reconciliation` — diálogo "Contratos de Destino"
**Repos:** `siagro-b1-backend`, `siagro-b1-frontend`

## Problema

O diálogo de destinos da conciliação de entregas só oferece contratos do **mesmo cliente**
da nota. Quando a conciliação com o relatório de entrega do cliente revela que a nota
pertence a um contrato de **outro** cliente, não há saída pela tela: a lista não mostra o
contrato, e mesmo que mostrasse a action recusaria.

Além disso, ao abrir a lista para outros clientes, as colunas atuais (filial, contrato,
complemento, safra, preço, saldos) não identificam de quem é cada contrato.

Dois pontos travam isso hoje:

- `SalesContractsGetReconciliationTargetsService` — predicado `c.CardCode == cardCode`.
- `SalesContractsReallocationCreateService` — guard
  `"O contrato de destino pertence a outro cliente."`.

## Decisões

| Decisão | Escolha | Motivo |
|---|---|---|
| Escopo da lista | Opt-in por checkbox + campo de busca | A lista já inclui contratos sem saldo; abrir para todos os clientes por padrão a deixaria longa e poria o destino errado a um clique. |
| Auditoria do cruzamento | Só remover o guard — sem flag nova, sem motivo obrigatório, sem `Origin` especial | Decisão do usuário: conciliar entre clientes é operação normal desta tela (ADMIN), não exceção. |
| Rede de proteção | Aviso nomeando o cliente no confirm da tela | Custo zero no backend e o operador vê o que está fazendo antes de gravar. |
| Origem da razão social/CNPJ | Campos desnormalizados da própria `SALES_CONTRACTS` | `CardName`/`CardTaxId` já existem na entidade; navegação para `BUSINESS_PARTNERS` zeraria a lista em modo SAPB1. |

## Backend — listagem de destinos

### `SalesContractReconciliationTargetDto`

Acrescentar:

```csharp
/// <summary>CNPJ/CPF do cliente do contrato, desnormalizado em SALES_CONTRACTS.</summary>
[JsonPropertyName("CardTaxId")]
public string? CardTaxId { get; set; }

/// <summary>Contrato de cliente diferente do cliente da NOTA.</summary>
[JsonPropertyName("IsOtherCustomer")]
public bool IsOtherCustomer { get; set; }
```

`CardName` já existe no DTO e já é preenchido — só não estava sendo exibido.

`IsOtherCustomer` é calculado no servidor (`c.CardCode != cardCode`) porque o frontend
**não tem** o `CardCode` da nota: com `autoExpandSelect: true`, nem
`SalesContract/CardCode` nem `SalesInvoiceItem/SalesInvoice/CardCode` entram no `$select`
(nenhum controle os binda), e buscá-los depois cairia em late property — que neste projeto
dá 404 sem rota declarada à mão.

### `SalesContractsGetReconciliationTargetsService`

Assinatura ganha `bool includeOtherCustomers = false`. O predicado de cliente passa a ser
condicional; **os demais filtros não mudam**:

```csharp
.Where(c => c.Key != sourceSalesContractKey
            && c.Status != ContractStatus.Finished
            && (includeOtherCustomers || c.CardCode == cardCode)
            && c.ItemCode == item.ItemCode
            && c.UnitOfMeasureCode == item.UnitOfMeasureCode)
```

`ItemCode` e `UnitOfMeasureCode` continuam filtrando nos dois modos: o guard da action
segue recusando produto/UM divergente, então listá-los só ofereceria destino inválido.

Projeção ganha `CardTaxId = c.CardTaxId` e `IsOtherCustomer = c.CardCode != cardCode`.

Ordenação: `OrderBy(c => c.CardName).ThenBy(c => c.Code)` quando incluir outros clientes,
para os contratos do mesmo cliente ficarem agrupados. Sem a flag, mantém `OrderBy(c => c.Code)`.

### `SalesContractsGetReconciliationTargetsController`

Rota e parâmetro novos:

```csharp
[HttpGet("odata/SalesContractsGetReconciliationTargets(SalesInvoiceItemKey={salesInvoiceItemKey},SourceSalesContractKey={sourceSalesContractKey},IncludeOtherCustomers={includeOtherCustomers})")]
```

⚠️ Rota OData é declarada à mão neste projeto — a sobrecarga com o parâmetro novo precisa
existir explicitamente, senão o UI5 recebe 404.

### `ODataConfigurations.cs`

```csharp
salesContractsGetReconciliationTargets.Parameter<bool>("IncludeOtherCustomers");
```

## Backend — guard da action

Em `SalesContractsReallocationCreateService.ExecuteAsync`, remover:

```csharp
if (target.CardCode != item.SalesInvoice.CardCode)
    throw new ApplicationException("O contrato de destino pertence a outro cliente.");
```

Permanecem intactos: contrato de origem ≠ destino, `Status != Finished` nas duas pontas,
`ItemCode`, `UnitOfMeasureCode`, volume > 0, saldo alocado na origem, e o caminho de
`allowNegativeBalance` (flag + motivo + `Origin = Reconciliation`).

Nada muda no cálculo: a diferença de preço já é apurada contra `target.Price`,
independentemente de quem é o cliente do destino.

## Frontend — diálogo `ReconciliationDialog.fragment.xml`

### Toolbar da tabela

```xml
<t:extension>
  <OverflowToolbar>
    <Title text="Contratos de Destino (inclusive sem saldo)" />
    <ToolbarSpacer />
    <CheckBox id="includeOtherCustomers"
              text="Incluir contratos de outros clientes"
              select=".onToggleIncludeOtherCustomers" />
    <SearchField id="targetsSearch" width="18rem"
                 placeholder="Contrato, cliente ou CNPJ"
                 liveChange=".onSearchTargets" />
  </OverflowToolbar>
</t:extension>
```

### Colunas novas

Depois de "Contrato", antes de "Complemento":

- **Cliente** — `{targetsModel>CardName}`, largura ~16rem.
- **CNPJ** — `{targetsModel>CardTaxId}`, largura ~11rem.

## Frontend — `reconciliation/Main.controller.ts`

### Carga da lista

`loadTargets(salesInvoiceItemKey, sourceContractKey, includeOtherCustomers)` passa o
parâmetro novo. O resultado é guardado inteiro em memória (`allTargets`) e o
`targetsModel` recebe o resultado do filtro de busca corrente. Trocar o checkbox limpa a
busca e recarrega do servidor.

`onToggleIncludeOtherCustomers` relê as chaves do `viewModel` (não do contexto da tabela
principal, que pode ter mudado) e chama `loadTargets`.

### Busca client-side

`onSearchTargets` filtra `allTargets` por `Code`, `Complement`, `CardName` e `CardTaxId`,
case-insensitive, comparando `String(valor ?? "")`. Busca vazia devolve a lista completa.

O CNPJ casa também **sem máscara**: além da comparação textual, o termo e o `CardTaxId`
são reduzidos a dígitos e comparados. O campo está gravado formatado em parte da base e
só com dígitos em outra, e quem digita ora copia da coluna (formatada) ora do cadastro.

⚠️ O `ReconciliationVolume` digitado vive no `targetsModel`. Como o filtro reescreve o
model, o filtro opera sobre os **mesmos objetos** de `allTargets` (mesma referência), não
sobre cópias — assim o volume digitado sobrevive a uma busca. Isso também mantém válida a
leitura por `getContextByIndex` no confirm.

### Interface `ReconciliationTarget`

Acrescentar `CardCode`, `CardName`, `CardTaxId`, `Complement`.

### Aviso no confirm

Em `onConfirmReconciliation`, os avisos de cliente diferente e de saldo negativo são
montados como uma lista e concatenados numa frase só, para o código do contrato não
aparecer duas vezes quando os dois casos ocorrem:

> O contrato de destino 00000447 pertence ao cliente ABATEDOURO DE AVES IDEAL LTDA,
> diferente do cliente da nota e ficará com saldo NEGATIVO de -98.461,000. Confirma a
> conciliação?

O cliente de origem não é repetido no texto: ele já está no cabeçalho do diálogo.

Compõe com o aviso de saldo negativo já existente: se os dois casos ocorrerem, a pergunta
menciona ambos.

## Testes

`SiagroB1.Application.Tests/SalesContracts/SalesContractsReconciliationQueriesTests.cs`:

1. `Targets_IncludeOtherCustomers_ReturnsContractsOfOtherCustomers` — com a flag `true`,
   contrato de outro `CardCode` (mesmo produto/UM) aparece.
2. `Targets_IncludeOtherCustomers_StillExcludesMismatchedItemAndUom` — com a flag `true`,
   contrato de outro cliente com produto ou UM divergente **não** aparece.
3. O teste existente `Targets_ExcludeFinishedAndMismatchedContracts` cobre a flag `false`;
   estender a asserção para provar que o contrato de outro cliente continua fora.

`SiagroB1.Application.Tests/SalesContracts/SalesContractsReallocationCreateServiceTests.cs`:

4. Destino de outro cliente (mesmo `ItemCode`/`UnitOfMeasureCode`) grava o par −/+ sem
   lançar exceção. Se houver hoje um teste afirmando o erro
   `"O contrato de destino pertence a outro cliente."`, ele é removido junto com o guard.

## Fora de escopo

- Espelhar a mudança no lado de **compra** (`purchaseContracts`).
- Fallback de `CardTaxId` para `BUSINESS_PARTNERS` em contratos legados anteriores a
  19/07/2026 (migration `AddSalesContractCardFNameAndTaxId`): a célula fica vazia. Fazer
  o fallback daria resultado diferente entre STANDALONE e SAPB1.
- Qualquer marcação de auditoria específica para o cruzamento de clientes
  (`Origin`, motivo obrigatório, flag na action).
