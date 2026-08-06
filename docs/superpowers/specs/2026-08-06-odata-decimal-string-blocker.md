# `Edm.Decimal` em string quebra a gravação — e a convenção do projeto é usar o tipo `Double` na tela

> Investigação de 06/08/2026, disparada pela verificação da Fase 1 do Documento de Entrada.
>
> **RESOLVIDO.** A primeira versão deste documento concluía que era um bloqueio pré-existente do
> backend, sem solução no escopo da feature. **Estava errado na implicação prática:** o projeto já
> tem uma convenção que contorna isso, e eu a violei. A correção é de uma linha por campo, no
> frontend.

## O sintoma

Gravar pela tela devolve **400**:

```
The input was not valid.

entity:
The entity field is required.
```

A mensagem não nomeia o campo culpado — ver "Por que o erro engana".

## A causa

O leitor OData deste backend **recusa `Edm.Decimal` em STRING**. Bisecção do corpo do POST:

| Corpo | Resultado |
|---|---|
| `{"CardCode":"F999999","Items":[{"ItemCode":"X","Quantity":1}]}` | **201 Created** |
| `{"CardCode":"F999999","Items":[{"ItemCode":"X","Quantity":"1"}]}` | **400** |

Vale para POST **e** PATCH, em todos os endpoints (reproduzido em `/odata/SalesInvoices`), e em
todas as variações de Content-Type testadas — com e sem `IEEE754Compatible`, com aspas e sem,
com e sem `odata.metadata`, e na ordem exata que o UI5 usa.

## A convenção que resolve: tipo `Double` no binding que GRAVA

`sap.ui.model.odata.type.Decimal` faz parse para **string**.
`sap.ui.model.odata.type.Double` faz parse para **número**.

O documento de saída já usa `Double` nos campos editáveis —
`salesInvoices/fragments/Items.fragment.xml`, colunas Quantidade e Valor Unitário:

```xml
value="{
  path: 'Quantity',
  type: 'sap.ui.model.odata.type.Double',
  formatOptions: { decimals: 0, decimalSeparator: ',', groupingEnabled: true, groupingSeparator: '.' }
}"
```

É por isso que aquela tela grava e a nova não gravava: **eu escolhi `Decimal`**, que é o tipo
"correto" para `Edm.Decimal` no papel, mas incompatível com este leitor.

**Regra prática:** campo de decimal que o usuário EDITA usa `Double` + `formatOptions.decimals`.
Campo de decimal só de EXIBIÇÃO pode continuar com `Decimal` + `constraints`, porque nunca grava.

## Por que o erro engana

Qualquer exceção do leitor OData faz o corpo vincular como `null`; o `[FromBody]` fica nulo e o
`ModelState` produz sempre a MESMA frase genérica — seja a causa um decimal em string, uma
propriedade inexistente ou outra coisa. **Não perca tempo lendo a mensagem: bisecte o payload.**

## Sobre a configuração do OData (`Program.cs:152-161`)

```csharp
builder.Services.AddControllers().AddOData(...)
.AddJsonOptions(options =>
{
    options.JsonSerializerOptions.NumberHandling = JsonNumberHandling.AllowReadingFromString;
    options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
});
```

`AllowReadingFromString` é exatamente a opção que aceitaria número em string — mas configura o
formatter do **System.Text.Json**, e as rotas `/odata` são lidas pelo formatter do **OData**: ali
essas linhas não têm efeito. Assimetria confirmada: o ESCRITOR OData honra `IEEE754Compatible`
(um GET com esse `Accept` devolve `"52020.000"`); só o leitor recusa.

**Nada disso foi alterado**, e não precisa ser: a convenção do `Double` resolve na tela. Mexer no
leitor continua sendo uma opção de longo prazo, com alcance sobre todos os endpoints — mas deixou
de ser bloqueio.
