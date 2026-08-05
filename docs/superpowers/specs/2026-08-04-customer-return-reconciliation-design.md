# Devolução de Cliente — conciliação da quebra e importação do XML

## Context

No fim do contrato o cliente pede à Yokotobi complementos de quantidade e valor — isso já está
resolvido pelo documento de saída avulso (natureza com efeito `Consume`/`Add`). O que **não**
tem lugar hoje é o outro lado: quando o destino pesa menos, **o cliente emite uma ou mais NF-e
de devolução contra a Yokotobi**, e não há onde registrá-las.

O princípio que o usuário deu governa o desenho: **o movimento fiscal tem que bater com o
movimento físico**. O movimento físico já está registrado — a Conferência de Entrega grava
`DeliveredQuantity`/`QuantityLoss`, e o fator efetivo de
`SalesContractsRecalculateBalanceService` já devolveu o volume ao contrato. A NF de devolução é
o documento **fiscal do mesmo fato**.

Resultado esperado: registrar a devolução do cliente, amarrar cada linha à NF de saída de
origem, e mostrar se a quantidade devolvida bate com a quebra apurada — sem tocar no saldo.

## A decisão central: NÃO mexe no saldo do contrato

Existem hoje três mecanismos que devolvem saldo, e usar o errado credita o contrato duas vezes:

| Mecanismo | Quando | Efeito |
|---|---|---|
| Conferência de Entrega | quebra apurada no destino | fator efetivo devolve o volume — **é o que já roda hoje** |
| `SalesInvoiceType.Return` | mercadoria VOLTA fisicamente | reverte romaneios para `Returned`, zera `InvoiceQty`, ledger negativo |
| Documento avulso `Restore` | ajuste fiscal sem romaneio | linha `FiscalAdjustment` no ledger |

A devolução do cliente **não é nenhum dos três**. Reaproveitar `InvoiceType.Return`
(`SalesInvoicesReturnService`) corromperia os romaneios — aquele fluxo pressupõe retorno físico,
e aqui não voltou mercadoria nenhuma. Por isso: **entidade própria, zero efeito em saldo,
zero linha de ledger.** O papel é controle e conciliação; a escrituração fiscal continua no SAP.

## Modelo de dados

### `CUSTOMER_RETURNS` (nova)

Cabeçalho da NF-e emitida pelo CLIENTE. Documento de entrada sob a ótica da Yokotobi.

| Coluna | Tipo | Observação |
|---|---|---|
| `Key` | `UNIQUEIDENTIFIER` PK | segue `BaseEntity` |
| `CardCode` / `CardName` | `VARCHAR(15)` / `VARCHAR(200)` | emitente (o cliente). Sem FK — dual-mode |
| `DocumentNumber` | `VARCHAR(9)` | número da NF do cliente |
| `DocumentSeries` | `VARCHAR(3)` | |
| `AccessKey` | `VARCHAR(44)` | chave da NF-e |
| `IssueDate` | `DATETIME2` | |
| `TotalValue` | `DECIMAL(18,2)` | do XML |
| `TaxPayerComments` | `VARCHAR(MAX)` | `infAdic/infCpl` — é AQUI que o cliente escreve número/série/chave/quantidade das origens. Exibido cru para o operador amarrar |
| `Status` | `INT` | `Registered` / `Cancelled` |
| `XmlFileName` | `VARCHAR(200)` | |
| `XmlData` | `VARBINARY(MAX)` | o XML original, como `SalesContractAttachment.FileData` já faz |

Índice único em `AccessKey` filtrado por `Status <> Cancelled` — a mesma trava de duplicidade já
usada em `SALES_INVOICES` para a NF-e.

### `CUSTOMER_RETURNS_ITEMS` (nova)

| Coluna | Tipo | Observação |
|---|---|---|
| `Key` | `UNIQUEIDENTIFIER` PK | |
| `CustomerReturnKey` | FK → `CUSTOMER_RETURNS` | |
| `ItemCode` / `ItemName` | | do XML (código do cliente pode divergir; é informativo) |
| `Quantity` | `DECIMAL(18,3)` | quantidade devolvida |
| `UnitPrice` | `DECIMAL(18,8)` | |
| `SalesInvoiceItemKey` | `UNIQUEIDENTIFIER NULL` | **a amarração**, feita à mão. FK para `SALES_INVOICES_ITEMS` |

A linha é a unidade da amarração porque os dois padrões de cliente cabem nela: quem emite uma
nota com várias linhas (cada uma de uma NF de origem) e quem emite uma nota por NF de origem
(caso de uma linha só).

## Regras

### Quebra apurada — o número contra o qual se confere

Para o item de saída de origem, com `DeliveryStatus == Closed`:

```
QuebraApurada = Quantity − (DeliveredQuantity − QuantityLoss)
```

É exatamente o volume que o fator efetivo devolveu ao contrato (o consumo virou `NetQuantity`),
então é o que o fiscal precisa espelhar. Item com entrega ainda aberta não tem quebra apurada.

### Conferência

Por linha amarrada: `Diferenca = Quantity (devolvida) − QuebraApurada`. Diferente de zero, a tela
**avisa e deixa gravar** — arredondamento e devolução parcial são legítimos, e quem decide é o
usuário. Nada de bloquear.

Só é oferecido como origem o item de saída que: é do MESMO cliente do cabeçalho, está em
documento `Confirmed` (não cancelado), e tem `DeliveryStatus == Closed` com `QuebraApurada > 0`.

## Importação do XML

`CustomerReturnsImportXmlService`: recebe o arquivo, desserializa com **`Zeus.Net.NFe.NFCe`**
(já referenciado em `SiagroB1.Infra`, não escrever parser à mão) e monta cabeçalho + linhas.

Preenche: emitente (CNPJ → resolve `CardCode` por `IBusinessPartnerService`), número, série,
chave, data, valor total, `infCpl` e uma linha por `det` do XML.

**Não tenta adivinhar a amarração.** O layout da NF-e põe as referências em `ide/NFref`, que é do
cabeçalho — o XML não diz qual linha é de qual origem. Os clientes escrevem isso em texto livre
no `infCpl`, que fica visível na tela para o operador amarrar, como ele já faz hoje. Casamento
automático por quantidade fica de fora: erraria em silêncio.

Guarda o XML original em `XmlData` — é a prova documental, e permite reprocessar se a leitura
mudar.

## Serviços e API

Uma classe por operação em `Services/CustomerReturns/`, registradas à mão em
`AddApplicationServices()` (`SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs`):
`CustomerReturnsGetService`, `CreateService`, `UpdateService`, `CancelService`,
`ImportXmlService`, e `CustomerReturnsGetOriginItemsService` (as origens elegíveis do cliente).

Controller OData `CustomerReturnsController` + action `CustomerReturnsImportXml` (upload).

## Frontend

- Tela **Devoluções de Clientes** em Vendas: lista, detalhe/edição, cancelamento.
- Botão **Importar XML** no topo da inclusão — o caminho normal de entrada.
- `infCpl` exibido num TextArea somente-leitura, ao lado da grade: é a cola do operador.
- Grade de linhas com value help **NF de Origem** (`CustomerReturnsOriginItemsSelectDialog`),
  listando documento, item, quantidade faturada e **Quebra Apurada**.
- Colunas **Quebra Apurada** e **Diferença** por linha, com `ObjectStatus` verde/amarelo — mesmo
  padrão da coluna Saldo do diálogo de contratos.
- Migration de menu no `CommonContext` (`MENU_ITEMS` com Key = rota + `ROLE_MENUS` p/ ADMIN).

## Testes (`SiagroB1.Application.Tests`)

- `QuebraApurada` = faturado − líquido, e é 0 em item com entrega aberta.
- Diferença apontada quando a devolvida não bate; gravação NÃO é bloqueada.
- Origens elegíveis excluem: outro cliente, documento cancelado, entrega aberta, quebra zero.
- Importação de XML real: cabeçalho e linhas preenchidos, `infCpl` preservado, XML guardado.
- Chave de NF-e duplicada é recusada; cancelada libera a chave.
- **Nenhuma linha de `SALES_CONTRACTS_ALLOCATIONS` é criada** e o `AllocatedVolume` do contrato
  não muda ao registrar, alterar ou cancelar a devolução — é a invariante que impede a
  contagem dupla.

## Verificação

Pelo caminho do usuário, no navegador: chegar à tela pelo menu, importar um XML real de
devolução de cliente, amarrar as linhas às NFs de origem, ver a Quebra Apurada e a Diferença, e
confirmar que o saldo do contrato **não mudou** antes e depois. Depois cancelar e ver que nada
no contrato se moveu.

## O que NÃO entra

- Escrituração fiscal da entrada (livro, SPED, apuração) — é o sub-projeto 2 do roadmap.
- Qualquer efeito em saldo, ledger ou romaneio.
- Emissão de NF-e (esta é a nota do CLIENTE; a Yokotobi só recebe).
- Casamento automático linha → NF de origem.
