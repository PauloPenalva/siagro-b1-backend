# Tipo de contrato editável somente na criação (compra e venda)

Data: 2026-07-30

## Problema

O campo **Tipo Contrato** (`Type`: `Fixed`/FIX ou `ToBeDetermined`/PAF) fica habilitado em todas as
telas de contrato, de compra e de venda. O `Select` está com `editable="true"` fixo no fragmento
compartilhado, enquanto todos os outros campos do formulário usam `{ui>/editable}`.

Como o mesmo fragmento é renderizado por quatro telas de cada lado (Add, Edit, Detail e
approval/Detail), o campo continua editável na tela de Edição. Trocar FIX por PAF depois da
criação desalinha o contrato da sua fixação de preço, que nasceu com regras diferentes para cada
tipo. Não há nenhuma trava no backend: um PATCH direto no contrato altera `Type` livremente.

## Regra

O tipo do contrato é definido na criação e não muda mais. Vale para contrato de compra e de venda.

## Frontend

Novo flag `ui>/typeEditable` no modelo `ui` (JSONModel global do Component).

O `Select` de `Type` passa de `editable="true"` para `editable="{ui>/typeEditable}"` em:

- `webapp/view/purchaseContracts/fragments/PurchaseContractForm.fragment.xml`
- `webapp/view/salesContracts/fragments/SalesContractForm.fragment.xml`

O flag é escrito ao lado de cada `setProperty("/editable", …)` já existente, nas oito telas que
consomem esses fragmentos:

| Controller | `/editable` | `/typeEditable` |
|---|---|---|
| `purchaseContracts/Add` | true | **true** |
| `purchaseContracts/Edit` | true | **false** |
| `purchaseContracts/Detail` | false | **false** |
| `purchaseContracts/approval/Detail` | false | **false** |
| `salesContracts/Add` | true | **true** |
| `salesContracts/Edit` | true | **false** |
| `salesContracts/Detail` | false | **false** |
| `salesContracts/approval/Detail` | false | **false** |

Não dá para reaproveitar `{ui>/editable}`: ele é `true` na Edição, que é exatamente onde o campo
precisa travar.

O modelo `ui` é global, então o valor sobrevive à navegação entre telas. Como as oito escrevem o
flag explicitamente no `patternMatched`, não há vazamento. E o sentido da falha é seguro: um
binding sem valor resolve `editable` como falso (readonly), diferente de `visible`, que vira
`true` quando o binding é `undefined`.

Fora de escopo: os filtros das telas de listagem (`PurchaseContractFilterbar`,
`SalesContractFilterbar`) e as colunas/labels "Tipo de Contrato" das tabelas e telas de aprovação.
Nenhum deles é o campo do formulário.

## Backend

Guarda de imutabilidade em:

- `SiagroB1.Application/Services/PurchaseContracts/PurchaseContractsUpdateService.cs`
- `SiagroB1.Application/Services/SalesContracts/SalesContractsUpdateService.cs`

Inserida **antes** do `context.Entry(existingEntity).CurrentValues.SetValues(entity)`:

```csharp
var entry = context.Entry(existingEntity);
var originalType = (ContractType)entry.OriginalValues[nameof(PurchaseContract.Type)]!;
if (entity.Type != originalType)
    throw new ApplicationException("O tipo do contrato não pode ser alterado após a criação.");
```

O ponto crítico é usar `OriginalValues`. No PATCH o controller aplica `patch.Patch(t)` sobre a
entidade já rastreada e o service recarrega a mesma instância pelo `key` — `existingEntity` e
`entity` são o mesmo objeto, então comparar um com o outro nunca acusaria diferença nenhuma.
`OriginalValues` é a única fonte do valor que está gravado no banco.

Funciona nos dois verbos expostos pelo controller:

- **PATCH** sem `Type` no payload: o valor da instância continua sendo o do banco, igual ao
  `OriginalValues` — sem falso positivo.
- **PUT**: o corpo completo carrega `Type`, comparado contra o `OriginalValues`.

A mensagem de erro é de negócio e vai em pt-BR, conforme a regra de nomenclatura do projeto, ainda
que as mensagens vizinhas nesses dois arquivos estejam em inglês.

Os dois services já rejeitam qualquer edição de contrato fora do status `Draft`, então a guarda só
tem efeito prático sobre contratos ainda em rascunho.

## Verificação

- `dotnet build SiagroB1.sln`
- `yarn ts-typecheck` e `yarn lint` no frontend
- No browser (ambiente Yokotobi), nas quatro telas de compra e nas quatro de venda: o campo aparece
  habilitado só na criação e, desabilitado, continua **exibindo** FIX ou PAF em vez de ficar vazio.
