# Contrato de compra a fixar (PAF) e mecanismo de fixação de preço

**Data:** 2026-07-20
**Escopo:** contratos `PurchaseContract` do tipo `ContractType.ToBeDetermined` e o ciclo de vida das fixações de preço.

## Problema

Hoje todo contrato de compra nasce `ContractType.Fixed`: o preço é determinado na negociação e
`PurchaseContractsCreateService` cria automaticamente uma fixação única cobrindo o volume total.

O negócio precisa do tipo `ToBeDetermined` (PAF — preço a fixar), em dois cenários:

1. O produtor entrega a mercadoria mas prefere aguardar alta de preço para fixar.
2. Excedente de embarque: contratou-se 120.000 kg, os caminhões carregaram 130.000 kg. Os 120.000 kg
   baixam do contrato existente; os 10.000 kg excedentes precisam de um contrato PAF para pousar.

O enum `ContractType` já tem os dois valores e o frontend já oferece a opção "PAF", mas não existe
mecanismo de fixação: as fixações são somente-leitura na UI e os status não têm efeito no cálculo.

## Fora de escopo

O **roteamento automático do excedente** (cenário 2 acima: detectar sobra na baixa do romaneio e alocá-la
a um contrato PAF) fica para uma spec separada. Ele depende de o PAF existir primeiro. Esta spec entrega
a capacidade de ter contratos PAF com fixação funcionando; a automação do excedente vem depois.

Também fora de escopo: qualquer integração com SAP B1 (nenhum documento é gerado no SAP a partir de
uma fixação).

## Situação atual (levantada no código)

**Backend**
- `ContractType { Fixed = 0, ToBeDetermined = 1 }` — `SiagroB1.Domain/Enums/ContractType.cs`
- `PriceFixationStatus { InApproval = 0, Confirmed = 1, Canceled = 2 }`
- `PurchaseContractPriceFixation` (`PURCHASE_CONTRACTS_PRICE_FIXATIONS`): classe solta, **não herda**
  `BaseEntity`; tem `Guid? Key` próprio e nenhum campo de auditoria.
- CRUD completo: `...PriceFixationsCreate/Update/Delete/GetService` + `PurchaseContractsPriceFixationsController`.
- Computados em `PurchaseContract.cs`: `FixedVolume` (139), `AvailableVolumeToPricing` (148), `TotalPrice` (151).
- `PurchaseContractsCreateService.cs:56` — cria fixação automática só quando `Type == Fixed`.
- `PurchaseContractsUpdateService.cs:68` — `price.FixationPrice = entity.StandardPrice`, incondicional.
- `PurchaseContractTax.cs:22` — `TotalTax` é **calculado em runtime**, não persistido:
  `(PurchaseContract.TotalPrice / 100) * Tax.Rate`.

**Portões de aprovação existentes** — `ContractStatus.Approved` é o portão da *movimentação física*:
- `ShipmentReleasesApprovationService.cs:29` — liberação não ativa sem contrato aprovado
- `PurchaseContractsGetShipmentReleasesAvailableService.cs:23`, `PurchaseContractsGetService.cs:56`
- `PurchaseContractsCloseService.cs:13`, `CancelService.cs:16`, `ReopenService.cs:16`

**Frontend**
- Opção "PAF" já existe em `PurchaseContractForm.fragment.xml:109` e no filterbar.
- `PurchaseContractPriceFixations.fragment.xml` — tabela read-only, botões Incluir/Remover comentados (25-38).
- Bugs latentes: `PurchaseContractsBaseController.ts:164` cria fixação com `Status: "Pending"` e
  `formatter.ts:143` mapeia `"Pending"` — status que **não existe** no enum do backend.

## Decisões de arquitetura

### Dois eixos de aprovação, não um

A aprovação do contrato é o portão da **movimentação física** ("essa mercadoria pode andar?"). A aprovação
da fixação é o portão do **compromisso financeiro** ("a que preço nós devemos?").

Num contrato `Fixed` os dois colapsam num único momento, e por isso o desenho atual nunca doeu. Num PAF eles
se descolam: volume é comprometido na assinatura, preço é comprometido depois, N vezes, por outra alçada.

**Decisão:** manter a aprovação do contrato intacta e adicionar a aprovação de fixação como fluxo
independente e ortogonal.

**Alternativa rejeitada:** mover a aprovação do contrato para a fixação. Um contrato PAF precisa aceitar
liberação de embarque com **zero fixações** — se a aprovação morasse só na fixação, um PAF nunca ativaria
uma liberação, inviabilizando o cenário do excedente que motivou a feature. Além disso quebraria
Close/Cancel/Reopen e o `FinishedContractMutationGuardInterceptor`.

**Ajuste operacional (revisado em 20/07/2026, durante a implementação):** o contrato `ToBeDetermined` passa
pelo fluxo normal `Draft → InApproval → Approved`, igual ao `Fixed`.

A versão anterior desta spec previa que o PAF nascesse `Approved`, para evitar um passo de aprovação sem
preço a aprovar. Descartado: `PurchaseContractsUpdateService.cs:25-28` só permite editar contrato em `Draft`.
Um PAF nascendo `Approved` seria **permanentemente ineditável** — qualquer erro de data, local, frete ou
comentário exigiria cancelar e refazer o contrato, perdendo código e liberações vinculadas. O passo de
aprovação é barato; perder a editabilidade não é.

Consequência colateral positiva: como fixação exige contrato `Approved` e contrato `Approved` não é
editável, é **impossível** editar um contrato que já tenha fixações. O risco de o `UpdateService`
atropelar fixações da diretoria deixa de existir por construção.

### `TotalVolume` é imutável

**Decisão:** o fechamento de um PAF **não** ajusta `TotalVolume` para o volume fixado.

`TotalVolume` é o volume negociado — registro contratual. Sobrescrevê-lo destruiria a distinção entre
"contratei 120 e entregaram 95" e "contratei 95", e reescreveria retroativamente os quatro computados que
dependem dele (`PurchaseContract.cs:136,148,178,208`) — inclusive podendo tornar `AvaiableVolume` negativo,
que é persistido e protegido por `RowVersion`.

O objetivo por trás da ideia já está atendido: `TotalPrice` soma **apenas** `FixationPrice × FixationVolume`
das fixações e nunca olhou para `TotalVolume`, logo a base tributária já reflete só o que foi fixado.

### `BaseEntity` permanece inalterada

Avaliado e rejeitado: trocar a geração de `Key` para `Guid.NewGuid()`.

Documentação do EF Core (`modeling/generated-properties.md`): *"on SQL Server, when a GUID property is
configured as a primary key, the provider automatically performs value generation client-side, using an
algorithm to generate optimal sequential GUID values."* Confirmado no banco: não há `defaultValueSql` nem
`NEWSEQUENTIALID` em nenhuma migration. As chaves já são geradas no cliente e já são sequenciais.

Trocar por `Guid.NewGuid()` (v4, aleatório) fragmentaria a PK de 13 entidades sem ganho — o valor já está
disponível logo após o `Add()`. Remover a annotation sozinha nem teria efeito: EF reaplica
`ValueGeneratedOnAdd` por convenção para PK Guid; só `.ValueGeneratedNever()` desligaria.

## Modelo de domínio

### `PurchaseContractPriceFixation` passa a herdar `BaseEntity`

Ganha de graça `CreatedAt/By`, `UpdatedAt/By`, `ApprovedAt/By`, `CanceledAt/By`. Campo novo apenas:

```
[Column(TypeName = "VARCHAR(500)")]
public string? ApprovalComments { get; set; }
```

Impacto: `Key` deixa de ser `Guid?` e vira `Guid`; entra `RowId`. Requer migration e verificação de que o
frontend não depende da nulidade da chave.

### `PriceFixationStatus` ganha `Rejected`

```
InApproval = 0, Confirmed = 1, Canceled = 2, Rejected = 3
```

Sem `Rejected`, "a diretoria recusou" e "foi estornada após confirmação" ficam indistinguíveis. O
`ContractStatus` já separa os dois casos, então é coerente.

### Semântica dos status

| Status | reserva volume | entra no preço/imposto |
|---|---|---|
| `InApproval` | sim | **não** |
| `Confirmed` | sim | sim |
| `Canceled` | não | não |
| `Rejected` | não | não |

> **`Canceled` ficou sem uso** após a revisão do estorno (ver abaixo): nenhum fluxo
> produz esse status hoje. Mantido no enum para não invalidar dados históricos.

Volume reserva já em `InApproval` para que duas pessoas não fixem a mesma tonelagem enquanto a diretoria
decide. Preço só conta em `Confirmed`, para não contaminar a base tributária com valor não autorizado.

### Mudanças em `PurchaseContract`

- **`TotalPrice` (linha 151) — muda:** passa a filtrar **apenas** `Confirmed`. Hoje inclui `InApproval`, e é
  isso que vaza valor não aprovado para o imposto.
- **`FixedVolume` — deixa de ser `[NotMapped]`:** vira coluna persistida `DECIMAL(18,3)`, recalculada em toda
  operação de fixação, protegida pelo `RowVersion` já existente. Mesmo padrão de `AllocatedVolume`. Conta
  `InApproval` + `Confirmed`. Funciona sob `$select` do OData e não depende de `Include`.
- **`AvailableVolumeToPricing` (linha 148)** — inalterado (`TotalVolume - FixedVolume`).
- **`TotalVolume`** — inalterado.

### Regra de volume

Σ `FixationVolume` das fixações `InApproval` + `Confirmed` ≤ `TotalVolume`. Um contrato pode ter N fixações
parciais. A guarda vive no servidor; o cliente valida antes só por conveniência.

## Serviços

Convenção do projeto: uma classe por operação em `Application/Services/PurchaseContracts/`, registrada à mão
em `AddApplicationServices()` (`SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs`).

| Serviço | Situação | Comportamento |
|---|---|---|
| `...PriceFixationsCreateService` | existe | Guarda de saldo; `Status = InApproval`; `CreatedBy`; recalcula `FixedVolume`; **bloqueia** fixação manual em contrato `Fixed` |
| `...PriceFixationsApprovalService` | **novo** | `InApproval → Confirmed`; grava `ApprovedBy/At` e `ApprovalComments` |
| `...PriceFixationsRejectService` | **novo** | `InApproval → Rejected`; devolve o volume reservado (recalcula `FixedVolume`) |
| `...PriceFixationsCancelService` | **novo** | `Confirmed → Canceled` (estorno); grava `CanceledBy/At`; recalcula `FixedVolume` |
| `...PriceFixationsUpdateService` | existe | Só permite editar se `InApproval`; recalcula `FixedVolume` |
| `...PriceFixationsDeleteService` | existe | Só permite apagar se `InApproval`; recalcula `FixedVolume` |
| `...PriceFixationsGetService` | existe | Acrescenta `QueryPending()` para a caixa de entrada |

Fixação `Confirmed` é **imutável enquanto confirmada**: para corrigi-la, estorna-se primeiro.

**Semântica do estorno (revisada em 20/07/2026, após teste manual):** o estorno desfaz a **aprovação** e
devolve a fixação para `InApproval`, não para `Canceled`. A versão anterior a matava como `Canceled` e
devolvia o volume ao saldo.

Consequências, todas intencionais:
- A fixação volta para a fila da diretoria e pode ser **reaprovada, editada ou excluída**.
- O **volume continua reservado** — `InApproval` reserva igual a `Confirmed`. Estornar **não** altera
  `FixedVolume` nem `AvailableVolumeToPricing`; só `TotalPrice` cai, porque conta apenas `Confirmed`.
- Para devolver volume ao saldo há dois caminhos: **excluir** a fixação (permitido em `InApproval`) ou a
  diretoria **rejeitá-la**.
- `ApprovedBy`/`ApprovedAt`/`ApprovalComments` são limpos no estorno — senão a fila exibiria uma fixação
  "pendente" já assinada. `CanceledBy`/`CanceledAt` registram quem estornou.

**Interação com contrato encerrado:** toda operação de fixação (criar, aprovar, rejeitar, cancelar) exige o
contrato em `ContractStatus.Approved`. Num contrato `Finished` o `FinishedContractMutationGuardInterceptor`
já bloquearia a gravação, mas com uma exceção genérica de interceptor — os serviços devem validar antes e
lançar mensagem de negócio clara. Para corrigir uma fixação de contrato encerrado, reabre-se o contrato via
`PurchaseContractsReopenService`.

O recálculo de `FixedVolume` deve ser um **método único** compartilhado pelos serviços, não replicado —
mesma disciplina adotada para `AvaiableVolumeToAllocate`.

### Correções obrigatórias em serviços existentes

- **`PurchaseContractsUpdateService`** — **nenhuma mudança necessária.** A spec original previa condicionar
  `price.FixationPrice = entity.StandardPrice` a `Type == Fixed`; verificado na implementação que a chamada
  a `UpdatePriceFixation` **já** está dentro de `if (existingEntity.Type == ContractType.Fixed)` (linha 34).
- **`PurchaseContractsCreateService`** — `ToBeDetermined` recebe `StandardPrice = 0`. O `Status = Draft` já
  é atribuído para todos os tipos, e a ramificação da fixação automática (linha 56) já está correta.
- **`PurchaseContractsCloseService.cs:13`** — nova guarda para `ToBeDetermined`, com duas condições:
  1. Σ `FixationVolume` das fixações **`Confirmed`** ≥ Σ `ShipmentRelease.ShippedQuantity`.
  2. Nenhuma fixação em `InApproval`.

  Note que a condição (1) usa o volume **confirmado**, não `FixedVolume` — que inclui `InApproval`. Fechar um
  contrato apoiado em fixação ainda não aprovada significaria encerrá-lo sem o preço de fato definido. A
  condição (2) impede deixar a diretoria com pendência órfã num contrato morto.

  **O volume entregue é Σ `ShippedQuantity`, e explicitamente NÃO `PurchaseContract.TotalShipmentReleases`.**
  Aquele computado (`PurchaseContract.cs:170`) soma `ConsumedQuantity`, que numa liberação não cancelada vale
  `ReleasedQuantity` (`ShipmentRelease.cs:74-77`) — ou seja, o volume **liberado**, não o romaneado. Uma
  liberação ativa de 60.000 kg com apenas 10.000 kg romaneados contaria 60.000 e bloquearia o fechamento por
  mercadoria que ainda não chegou ao armazém.

  Saldo contratado que nunca foi entregue não bloqueia o fechamento.

## API

OData actions em `Web/Actions/PurchaseContracts/`, no molde dos controllers de contrato já existentes
(`PurchaseContractsApprovalController`, `...RejectController`, `...CancelController`):

- `PurchaseContractsPriceFixationApproval`
- `PurchaseContractsPriceFixationReject`
- `PurchaseContractsPriceFixationCancel`

O CRUD continua em `PurchaseContractsPriceFixationsController`. Autorização de aprovação segue o sistema de
permissões existente, no mesmo molde da aprovação de contrato.

## Frontend

- **`PurchaseContractForm.fragment.xml`** — com `Type = ToBeDetermined`, `StandardPrice` (linha 251) fica
  desabilitado e zerado.
- **Diálogo de fixação** — novo fragmento em `webapp/dialogs/fragments/`, aberto por um botão "Fixar Preço"
  visível apenas em contrato PAF aprovado. Campos: data, volume, preço, frete. Valida contra
  `AvailableVolumeToPricing`.
- **`PurchaseContractPriceFixations.fragment.xml`** — descomenta a toolbar; "Incluir/Remover" viram
  "Fixar Preço" e "Cancelar Fixação". As colunas passam a **read-only** (`Text` no lugar de `Input`/`DatePicker`),
  refletindo a imutabilidade. Acrescenta coluna de aprovador.
- **Caixa de entrada da diretoria** — novo módulo `view/purchaseContracts/priceFixationApproval/`
  (`Main.view.xml` com a fila de todas as fixações `InApproval` de todos os contratos + `Detail.view.xml`),
  espelhando `view/purchaseContracts/approval/`. Nova rota no `manifest.json`, endpoints em `ServerRoutes.ts`.
- **Limpeza dos bugs do status fantasma:** remover `"Pending"` de `formatter.ts:143` e corrigir
  `onAddPriceFixation` (`PurchaseContractsBaseController.ts:164`), que hoje cria com um status inexistente.
- **`formatter.ts`** — acrescentar `"Rejected"` → "Rejeitado".

## Relatório

`PriceFixationReportService` em `SiagroB1.Reports` + template `.frx`, no molde de
`PrePurchaseContractReportService`. Gera o espelho da fixação confirmada para envio ao produtor.

**Restrição:** os dados do parceiro **não podem** vir de `BUSINESS_PARTNERS` — em modo `SAPB1` essa tabela
fica vazia e o relatório sairia em branco. Usar `IPartnerSource`.

## Efeitos a jusante

Uma fixação confirmada:
1. Entra em `TotalPrice`, que recompõe automaticamente `PurchaseContractTax.TotalTax`. **Não há imposto
   persistido** — não existe serviço de recálculo a escrever.
2. Habilita a geração do espelho de fixação.

Nenhum documento é gerado no SAP B1.

## Testes

Em `SiagroB1.Application.Tests` (xUnit + EF InMemory):

- Guarda de saldo: aceita Σ = `TotalVolume`; rejeita Σ > `TotalVolume`; aceita N fixações parciais.
- Transições de status: válidas (`InApproval → Confirmed`, `InApproval → Rejected`, `Confirmed → Canceled`)
  e inválidas (editar/apagar `Confirmed`, aprovar `Canceled`, cancelar `InApproval`).
- Estorno devolve volume: após `Cancel`, `AvailableVolumeToPricing` volta ao valor anterior.
- `TotalPrice` ignora `InApproval` e conta `Confirmed`.
- `TotalTax` correto **com e sem** `Include` aninhado — o computado retorna 0 silenciosamente se
  `PurchaseContract`/`Tax` não estiverem carregados.
- `FixedVolume` persistido converge com a soma das fixações após cada operação.
- Guarda de fechamento: PAF não fecha com volume `Confirmed` < Σ `ShippedQuantity`; não fecha com fixação
  em `InApproval`; **não** fecha quando o volume entregue está coberto apenas por fixação `InApproval`;
  fecha com saldo contratado não entregue; **fecha** quando há volume liberado mas ainda não romaneado
  (regressão contra o uso equivocado de `TotalShipmentReleases`).
- Operação de fixação em contrato `Finished` é rejeitada com mensagem de negócio, não com erro de interceptor.
- `PurchaseContractsUpdateService` não sobrescreve fixações num contrato `ToBeDetermined`.
- Contrato `ToBeDetermined` nasce `Approved`; contrato `Fixed` continua nascendo `Draft`.

## Migrations

Duas mudanças de schema:
1. `PURCHASE_CONTRACTS_PRICE_FIXATIONS` — colunas de auditoria do `BaseEntity` + `RowId` + `ApprovalComments`.
2. `PURCHASE_CONTRACTS` — coluna `FixedVolume DECIMAL(18,3)`.

Backfill de `FixedVolume` para contratos existentes a partir da soma das fixações.

> Aplicar com `dotnet ef database update` passando `ASPNETCORE_ENVIRONMENT` explicitamente. O perfil
> `db-migration` faz fallback para um alvo que já apontou para produção — ler a connection string antes.
