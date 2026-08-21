# Diferença de entrega concentrada numa única alocação

**Data:** 14/08/2026
**Tela:** `/sales-contracts/reconciliation` — tabela "Entregas"
**Repos:** `siagro-b1-backend`, `siagro-b1-frontend`

## Problema

Quando uma entrega conferida com quebra é dividida entre N contratos, a quebra é **rateada
pró-rata** entre eles. Dividir 100 t em 60/40 com 2 t de quebra tira 1,2 t de um contrato e
0,8 t do outro. Ninguém decidiu esse rateio por contrato — ele é consequência mecânica de
como o saldo é derivado.

A causa está no **fator efetivo** `NetQuantity / Quantity`
(`SalesContractsRecalculateBalanceService.EffectiveFactor`), que é multiplicado **linha a
linha** dentro do `SUM` do ledger em `CalculateAllocatedAsync`. Como toda linha do mesmo
item leva o mesmo fator, a quebra se espalha na mesma proporção do volume. O comportamento
está inclusive escrito como regra no comentário da classe: *"Em item realocado, a quebra
distribui pró-rata entre os contratos que dividem o item."*

Consequência prática: o operador não consegue explicar o saldo de um contrato olhando a
tela. A tabela "Entregas" mostra `Volume` (o **faturado**) e o `AvaiableVolume` do contrato
já vem líquido do rateio — os dois não se reconciliam visualmente, e a fração de quebra que
coube a cada contrato não aparece em lugar nenhum.

## Decisões

| Decisão | Escolha | Motivo |
|---|---|---|
| Quem carrega a diferença | A linha do **faturamento original** (`Origin = Billing`) | Decisão do usuário. É única e estável por item: `SalesContractsAllocationCreateService` cria uma por item e a realocação só acrescenta pares −/+, nunca apaga a original. |
| Origem esvaziada pela realocação | A titularidade **acompanha o volume** para o destino, **gravada** no ledger | Decisão do usuário. Sem isso, na troca cruzada o contrato de origem terminaria consumindo volume negativo puro (saldo acima do contratado) e o destino ficaria com o volume cheio sem a quebra. |
| Como gravar a titularidade | Flag booleana na **linha** do ledger | Alternativas descartadas: linha de quebra materializada obrigaria a filtrar a nova origem no recálculo da liberação, no `availableAtSource` do guard e na devolução, além de duplicar dado que já vive no item; ponteiro no item identificaria o *contrato*, não a *linha*, deixando a coluna de diferença ambígua quando o mesmo contrato tem duas linhas do mesmo item. |
| Fórmula da diferença | `AssessedShortage` = `Quantity − NetQuantity` (**inclui** `QuantityLoss`) | É a única que preserva o total. Ver "Invariante" abaixo. **Não** é a coluna persistida `SalesInvoiceItem.DeliveryDifference`, que ignora a perda. |
| Escopo | Somente venda | A compra tem o seu próprio `CalculateAllocatedAsync`, mas não tem conferência de entrega (`DeliveredQuantity`/`QuantityLoss` não existem lá) — não há fator, logo não há rateio a corrigir. |
| Liberação de entrega | Inalterada | `ShippedQuantity` continua somando `Volume` nominal (quebra não devolve saldo à liberação). A flag não entra nesse caminho. |
| Exibição | `Quantidade` (faturada) + `Qtd. Efetiva` + `Difer. Entrega` | Decisão do usuário. `Quantidade` continua casando com `Preço NF` e `Diferença` ao lado; a diferença aparece preenchida só na linha dona. |

## Invariante: o total não muda

Esta é a propriedade que autoriza a mudança sem risco de deriva de saldo.

**Hoje**, somando o consumo de todos os contratos que dividem o item:

```
Σ (Volume_i × fator) = Quantity × NetQuantity/Quantity = NetQuantity
```

**Depois**, com a quebra concentrada:

```
Σ Volume_i − AssessedShortage = Quantity − (Quantity − NetQuantity) = NetQuantity
```

Idênticos. **Muda apenas a repartição entre contratos, nunca o total consumido.** É por isso
que a fórmula tem que incluir o `QuantityLoss`: usar `DeliveredQuantity − Quantity` (a
fórmula da coluna `DeliveryDifference`) quebraria a igualdade e faria os saldos derivarem em
exatamente `QuantityLoss` por item conferido.

Há um teste dedicado a esta igualdade — é a rede de segurança da feature.

## Regra nova

Consumo de um contrato `C`:

```
Σ Volume das linhas de C  −  Σ AssessedShortage dos itens cuja linha dona está em C
```

**Dono da diferença**: exatamente uma linha por `SalesInvoiceItemKey`. Designação
idempotente e auto-corretiva, em três passos:

1. Se a linha dona atual existe **e** o contrato dela tem volume líquido `> 0` naquele item
   → mantém.
2. Senão → a linha mais antiga (`RowId`, identity int de `BaseEntity`) entre as que estão em
   contrato com líquido `> 0`.
3. Se nenhum contrato tem líquido `> 0` (item integralmente devolvido) → a linha mais antiga
   do item.

A regra entrega as duas decisões de uma vez: o padrão é a linha `Billing` (a mais antiga do
item), e a titularidade segue o volume quando a realocação zera a origem. **O estorno não
precisa de bookkeeping** — apagado o grupo, a mesma regra reelege a linha `Billing`.

## Backend

### Entidade

`SalesContractAllocation` ganha:

```csharp
/// <summary>
/// Marca a ÚNICA linha do item que carrega a diferença de entrega inteira.
/// </summary>
public bool OwnsDeliveryDifference { get; set; }
```

Os docs da classe e do `Volume`, que hoje afirmam o rateio pró-rata, passam a descrever a
concentração.

### `SalesContractsDeliveryDifferenceOwnerService` (novo)

Estático, no molde de `SalesContractsRecalculateBalanceService`:

```csharp
public static void EnsureOwner(IReadOnlyCollection<SalesContractAllocation> linesOfItem)
public static async Task EnsureOwnerAsync(AppDbContext context, ICollection<Guid> itemKeys)
```

A sobrecarga em memória recebe o conjunto **pós-mutação** das linhas do item (persistidas +
pendentes − excluídas). Linhas pendentes têm `RowId == 0`: ordenar por
`RowId == 0 ? int.MaxValue : RowId` para que contem como as mais novas, e não como as mais
antigas.

### `SalesContractsRecalculateBalanceService`

`EffectiveFactor` **sai**; entra:

```csharp
public static decimal EffectiveVolume(SalesContractAllocation a, SalesInvoiceItem item) =>
    a.Volume - (a.OwnsDeliveryDifference ? item.AssessedShortage : 0m);
```

A troca de nome é deliberada: força revisitar cada chamador em vez de compilar em silêncio
com a semântica antiga.

`CalculateAllocatedAsync` vira duas somas — nominal e quebra das linhas donas:

```csharp
var nominal = await context.SalesContractsAllocations
    .Where(a => a.SalesContractKey == key)
    .SumAsync(a => a.Volume);

var shortage = await context.SalesContractsAllocations
    .Where(a => a.SalesContractKey == key
             && a.OwnsDeliveryDifference
             && a.SalesInvoiceItem!.DeliveryStatus == SalesInvoiceDeliveryStatus.Closed)
    .SumAsync(a => a.SalesInvoiceItem!.Quantity
                 - (a.SalesInvoiceItem.DeliveredQuantity - a.SalesInvoiceItem.QuantityLoss));

return decimal.Round(nominal - shortage, 3, MidpointRounding.ToEven);
```

Mais uma sobrecarga `CalculateAllocatedAsync(context, contractKey, Guid excludedItemKey)`,
que aplica `a.SalesInvoiceItemKey != excludedItemKey` nas duas somas — ver "Leitura
obsoleta".

`RecalculateForItemsAsync` chama `EnsureOwnerAsync(itemKeys)` **antes** de recalcular.

### Chamadores sem risco

`SalesContractsAllocationCreateService`, `...CreateForReturnService` e
`...CreateForFiscalAdjustmentService` criam a **primeira** linha de um item novo
(faturamento / item de devolução / item de ajuste). Não existe linha persistida desse item
para reetiquetar, então basta chamar `EnsureOwner` sobre a lista pendente — nunca marcar a
flag à mão, para manter uma regra só — e trocar `Volume * EffectiveFactor(item)` por
`EffectiveVolume(a, item)` no `pendingSum`.

### Leitura obsoleta — os dois serviços de realocação

`SalesContractsReallocationCreateService` e `SalesContractsReallocationDeleteService` são os
únicos em que a titularidade migra de uma linha **já persistida** para outra. Ambos rodam em
`CommitMode.Deferred`, e `SumAsync` é agregação no servidor: ela lê o **banco**, não as
entidades rastreadas. Um `SUM` disparado antes do `SaveChanges` leria a flag antiga.

Padrão a aplicar nos dois:

```
allocated = CalculateAllocatedAsync(context, contractKey, excludedItemKey: itemKey)  // banco, sem o item tocado
          + Σ EffectiveVolume(linha, item) do conjunto pós-mutação do item           // memória
```

O item tocado sai da soma SQL e entra em memória com a titularidade já corrigida. O conjunto
é pequeno (as linhas de um item de nota), então não há custo relevante.

## Migration

Um único `Up()`, **nesta ordem**:

1. `ALTER TABLE SALES_CONTRACTS_ALLOCATIONS ADD OwnsDeliveryDifference BIT NOT NULL DEFAULT 0`
2. **Backfill**: por `SalesInvoiceItemKey`, marcar a linha de menor `RowId` entre as que
   estão em contrato com `SUM(Volume) > 0` naquele item; para os itens em que nenhum
   contrato tem líquido positivo, marcar a linha de menor `RowId` do item. É a regra dos 3
   passos aplicada em SQL.
3. **Só então** criar o índice único filtrado sobre `SalesInvoiceItemKey`
   `WHERE OwnsDeliveryDifference = 1`.

A ordem importa: criar o índice antes do backfill quebraria. E se o backfill marcar dois
donos para o mesmo item, a criação do índice **falha** — que é exatamente o sinal desejado,
em vez de a invariante ser violada silenciosamente em produção.

Aplicar com `dotnet ef database update` passando `ASPNETCORE_ENVIRONMENT` explicitamente.

**Pós-deploy:** rodar o recálculo geral, senão os `AllocatedVolume` persistidos permanecem
na repartição antiga. Já existe botão para isso: *Recalcular Saldos*, no painel "Contratos
com Saldo Negativo" da própria tela → `ExecuteAllAsync`.

## Frontend

`view/salesContracts/reconciliation/Main.view.xml` — `Quantidade` continua mostrando
`Volume`. Duas colunas novas depois de `Un.Med.`:

- **Qtd. Efetiva** — `Volume − (dona && Closed ? AssessedShortage : 0)`
- **Difer. Entrega** — `NetQuantity − Quantity` na linha dona; **vazia** nas demais

Ambas por formatter em `model/formatter.ts`, com `parts` sobre `Volume`,
`OwnsDeliveryDifference` e `Quantity`/`DeliveredQuantity`/`QuantityLoss`/`DeliveryStatus` do
`SalesInvoiceItem`. O `$expand` atual já traz o item inteiro.

Armadilhas deste projeto a respeitar:

- **todo** `part` precisa de `targetType: 'any'` — o enum `DeliveryStatus` estoura sem ele, e
  o booleano `OwnsDeliveryDifference` sem `targetType` renderiza "Sim" sempre;
- decimais chegam como **string**: converter com `Number()` dentro do formatter;
- `OwnsDeliveryDifference` é coluna mapeada, não `[NotMapped]` — entra sozinha no EDM, sem
  precisar de `AddProperty`.

## Testes

`SiagroB1.Application.Tests/SalesContracts/SalesContractsRecalculateBalanceServiceTests.cs`,
reaproveitando `SalesContractsAllocationTestSupport`:

- divisão 60/40 com quebra → diferença inteira no contrato da linha `Billing`, o outro
  consome o nominal;
- **total preservado**: `Σ consumo nos dois contratos == NetQuantity`;
- realocação que esvazia a origem → titularidade migra para a linha positiva do destino;
- estorno da realocação → titularidade volta para a linha `Billing`;
- item com entrega `Open` → consumo nominal, ninguém subtrai nada;
- item integralmente devolvido → regra 3, sem dono órfão e sem exceção.

## Verificação no navegador

Stack local (backend no profile `yktb`, `yarn start:dev` no frontend), em
`/sales-contracts/reconciliation`:

1. Achar ou montar uma nota conferida com quebra, dividida entre dois contratos.
2. `Difer. Entrega` aparece em **uma só linha**; `Qtd. Efetiva` = faturada nas demais.
3. A soma de `Qtd. Efetiva` das linhas de um contrato bate com o movimento no seu
   `Saldo Contrato`.
4. Realocar **todo** o volume para o outro contrato → a diferença migra junto.
5. Estornar → a diferença volta para a linha do faturamento.
6. *Recalcular Saldos* duas vezes: a segunda não pode alterar nada (idempotência).
