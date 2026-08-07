# Referência ao contrato de compra na linha do Documento de Entrada

Data: 07/08/2026
Antecede: `2026-08-06-purchase-invoice-design.md` (Fase 1, encerrada e verificada)

## O problema

O Documento de Entrada tipo `Normal` — de terceiro ou de emissão própria — não tem como dizer a
qual contrato de compra cada linha se refere. Do lado da venda esse vínculo já existe há tempo
(`SalesInvoiceItem.SalesContractKey`); do lado da compra, não. Enquanto isso não existir, a
conciliação fiscal de compra não fecha: há a NF e há o contrato, e nada os liga.

A spec da Fase 1 previa essa amarração para a Fase 3, junto com o efeito de negócio. Este trabalho
antecipa **apenas a referência**, sem nenhum efeito — que é o que a conciliação fiscal precisa
agora.

## Decisão central: referência, não efeito

A linha ganha o contrato como **dado de conciliação**. Nada mais:

- Nenhuma linha de `PURCHASE_CONTRACTS_ALLOCATIONS` é criada.
- Nenhum saldo de contrato é recalculado.
- Nenhum valor de contrato é alterado.

Isso não é uma simplificação de conveniência: é a mesma decisão já registrada na Fase 1, onde
`ContractBalanceEffect` fica `None` em todos os cenários do dia a dia porque **o saldo físico
continua sendo movido só pelo romaneio**. O mecanismo de mover saldo existe e permanece desligado.

Consequência a testar explicitamente: criar, alterar e cancelar um documento com contrato amarrado
não pode mexer no saldo de contrato nenhum.

## Modelo

`PurchaseInvoiceItem` ganha, espelhando `SalesInvoiceItem`:

```csharp
public Guid? PurchaseContractKey { get; set; }
public virtual PurchaseContract? PurchaseContract { get; set; }
```

Nullable por três razões independentes: a NF de insumo, serviço ou frete não tem contrato; a linha
importada de XML nasce sem vínculo, porque o XML não o carrega; e amarrar depois de gravar é fluxo
legítimo.

Migration: coluna nullable + FK `Restrict`, o mesmo padrão que `SalesInvoiceItemKey` já usa nesta
tabela. `Restrict` porque apagar um contrato não pode apagar a linha fiscal que o referencia.

## Quais contratos são oferecidos

Do **fornecedor do documento** (`CardCode`) e do **produto da linha** (`ItemCode`), em status
`Approved` ou `Finished`.

`Finished` entra de propósito. A NF chega com frequência depois do contrato ter sido todo liberado
ou encerrado, e excluí-lo deixaria essa NF sem como ser amarrada — exatamente o caso que a
conciliação precisa cobrir. Ficam de fora `Draft`, `InApproval`, `Rejected` e `Canceled`, que não
podem lastrear uma NF.

### Por que não reusar `PurchaseContractsGetAvaiablesList`

Ela já filtra `CardCode + ItemCode + (Approved | Finished)`, que é exatamente o recorte acima — mas
em seguida descarta todo contrato com `AvaiableVolume <= 0`. Esse corte joga fora justamente os
contratos encerrados e os já totalmente liberados, que são o motivo de `Finished` estar na lista.

Reusá-la reintroduziria em silêncio o recorte estreito. Alterá-la quebraria a tela de alocação, que
depende do corte por volume. Portanto, não reusar — mas também **não criar função nova**.

### Endpoint novo é desnecessário: bindar o entity set

O padrão da casa para value help é bindar o **entity set** e pôr as condições fixas num `$filter`
estático do fragmento, deixando as variáveis como `Filter` de runtime vindas do controller. É
exatamente o que o diálogo da NF de origem faz: ele binda `/SalesInvoicesItems` com um `$filter`
estático de status e entrega o cliente como filtro na abertura — a função
`PurchaseInvoicesOriginItems`, que existe no backend, ficou órfã e não é usada por tela nenhuma.

Aqui vale o mesmo: `/PurchaseContracts` com

```
$filter: Status eq 'Approved' or Status eq 'Finished'
```

no fragmento, e `CardCode` + `ItemCode` como `Filter` na abertura. Zero backend novo para o value
help.

O `$filter` de status precisa ser **string estática** e não `sap.ui.model.Filter`: filtro de enum
montado como objeto estoura *"Unsupported type"* no UI5. `CardCode` e `ItemCode` são string e vão
como `Filter` normalmente.

Quem garante a regra de verdade é o guard do servidor, abaixo — o `$filter` do diálogo é
conveniência de tela, não autoridade.

## Guarda no servidor

Ao gravar uma linha com `PurchaseContractKey` preenchida, validar que o contrato:

1. existe;
2. está em `Approved` ou `Finished`;
3. tem `CardCode` igual ao do documento;
4. tem `ItemCode` igual ao da linha.

Erro de negócio em pt-BR quando qualquer uma falhar. O filtro do value help já evita isso na tela,
mas a validação é do servidor porque a tela não é o único caminho até o dado.

**Trocar o emitente com contratos amarrados barra no salvar**, com mensagem explicando qual linha
ficou inconsistente. Não limpar em silêncio: apagar o vínculo sem avisar destrói trabalho do
operador sem deixar rastro.

### Os TRÊS caminhos de gravação

A validação precisa existir nos três, e isso não é redundância — cada um é alcançado por uma ação
diferente da tela:

- **Deep-insert do Add** — `PurchaseInvoicesCreateService`, que recebe o documento com as linhas.
- **POST de linha nova no Edit** — `PurchaseInvoicesItemsCreateService`.
- **PATCH de linha do Edit** — `PurchaseInvoicesItemsUpdateService`. A grade do Edit patcheia a
  LINHA (`PurchaseInvoicesItems({key})`), **não** o cabeçalho, então o `SyncItems` do
  `PurchaseInvoicesUpdateService` nunca vê essa alteração em particular.

A **troca do emitente** no cabeçalho está coberta pelo `SyncItems` do `PurchaseInvoicesUpdateService`
sem código extra, e entender por quê importa: `PurchaseInvoicesController.Patch` carrega o documento
por `GetByIdAsync` — que faz `Include(Items)` — e aplica o `Delta` em cima. O `entity` que chega ao
serviço traz portanto **todas** as linhas, e o ramo de "linha existente" do `SyncItems` revalida cada
uma contra o emitente novo.

> Correção de rota, registrada porque a premissa errada quase virou código morto: a primeira versão
> deste spec afirmava que as linhas já gravadas "não são tocadas pelo PATCH do cabeçalho" e mandava
> um laço de revalidação próprio no `ExecuteAsync`. A revisão da Task 2 mostrou que a afirmação é
> falsa — o laço validava exatamente o que o `SyncItems` já validava, e o teste que deveria prová-lo
> passava mesmo com o laço removido. O laço foi retirado.

A regra compartilhada mora em `PurchaseInvoiceLineGuard`, ao lado de `EnsureParentIsPendingAsync` e
`ResolveItemNameAsync`, que existem exatamente por este motivo: a grade grava por POST/PATCH/DELETE
direto na linha, sem passar pelo serviço do cabeçalho.

O terceiro caminho foi descoberto na verificação de 07/08/2026, quando a re-resolução da descrição do
produto foi implementada só no `SyncItems` e o bug continuou de pé até ser flagrado conferindo o
banco depois de salvar. Tratar só um dos caminhos é o erro previsível aqui — e, como a correção de
rota acima mostra, inventar um caminho que não existe é o erro simétrico.

## Tela

Coluna **"Contrato"** em `view/purchaseInvoices/fragments/Items.fragment.xml`, visível apenas quando
`InvoiceType == Normal` — mesmo mecanismo `visible=` que as colunas de devolução já usam. Vale para
`ThirdParty` e `OwnIssue` indistintamente: quem decide é o tipo, não a emissão.

O campo grava a **chave**, não o código, então **não** usa `descriptionProperty` — aquele mecanismo
copia uma descrição, e aqui o que importa é a FK. Segue o padrão já estabelecido por
`openOriginItemValueHelp`:

- binding de exibição em `OneWay` sobre a navegação (`PurchaseContract/Code`), porque em linha ainda
  não amarrada a navegação é null e o modo TwoWay tentaria escrever dentro dela;
- `setValue(Code)` no input e `setProperty("PurchaseContractKey", ...)` na linha;
- `showValueHelp="{ui>/editable}"`, não `"true"`: o fragmento é compartilhado com o Detail.

`PurchaseContractKey` precisa entrar no `create()` inicial do Add e do Edit, nem que seja como
`null`, senão a primeira escolha abre *"Must not change a property before it has been read"*.

O `$expand`/`$select` da Edit e da Detail precisa incluir `PurchaseContract`, e o
`PurchaseInvoicesGetService` precisa do `Include` correspondente. Sem o `Include` no servidor o
`$expand` do cliente não adianta: a navegação volta null e a coluna fica vazia mesmo com a chave
gravada.

Ao salvar, em Add e Edit, aviso — não bloqueio — quando houver linha sem contrato: *"N item(ns) sem
contrato amarrado. Salvar assim mesmo?"*. Espelha a amarração da devolução, pelo mesmo motivo:
amarrar depois é caminho legítimo, e a conciliação só fica incompleta enquanto isso.

Obrigatoriedade real fica para a Fase 2, quando `UsageEffect.RequiresContract` souber, por natureza
de operação, quais linhas exigem contrato. Hoje não há como distinguir a linha de mercadoria da
linha de frete sem essa camada, e tornar obrigatório travaria a NF de insumo e serviço.

## Testes

- Guard rejeita contrato de outro fornecedor, de outro produto, e em status `Draft`/`Canceled`.
- Linha grava e relê a chave — nos dois caminhos que gravam linha (deep-insert e PATCH de linha).
- Trocar o emitente do documento com linha amarrada é recusado, e a mensagem diz qual linha.
- Documento sem contrato continua gravando (o campo é opcional).
- **Nenhum saldo de contrato muda** ao criar, alterar ou cancelar documento com contrato amarrado.
- Contrato `Finished` é aceito pelo guard — o caso que `PurchaseContractsGetAvaiablesList`
  descartaria e que a conciliação precisa cobrir.

## Fora de escopo

- **Coluna de divergência** entre faturado e contratado — Fase 3, como a spec da Fase 1 já previa.
- Qualquer efeito em saldo, valor de contrato ou ledger — Fase 3.
- Natureza de operação por linha e `RequiresContract` — Fase 2.
- Amarração automática pelo XML: o XML da NF-e não carrega o contrato, então a amarração é manual
  por limitação do layout, igual à da NF de origem na devolução.
