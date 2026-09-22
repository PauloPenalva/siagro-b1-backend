# GAC-1181 Fase 2 — Transbordo em Armazém Próprio — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** permitir o transbordo em armazém próprio, onde a mercadoria passa por um **lote de natureza Transbordo** e as duas pernas físicas são pesadas na balança.

**Architecture:** o lote ganha um campo de natureza; a entrada (pesagem) credita o lote e o vínculo na carga credita o armazém pelo romaneio `TransshipmentReceipt (15)`; a saída (pesagem) debita o lote e o vínculo na carga **emite a liberação**, que autoriza a Expedição de Grãos a criar a perna de venda — e é ela que conclui o transbordo e libera o faturamento. A sobra fica no lote.

**Tech Stack:** .NET 10, EF Core (SQL Server), OData v4, xUnit + EF InMemory, OpenUI5 + TypeScript.

**Spec:** `docs/superpowers/specs/2026-09-21-gac-1181-load-transshipment-design.md`, seção **"Fase 2 — transbordo em armazém próprio"**. Leia-a inteira antes da Task 1: a tabela de sete passos com os dois papéis (armazém x escritório) é o contrato deste plano.

**Repos:** backend no worktree `C:\Projetos\SiagroB1\siagro-b1-backend-gac-1181`; frontend em `C:\Projetos\SiagroB1\siagro-b1-frontend`. Ambos na branch `feature/gac-1181-load-transshipment`.

## Global Constraints

- **Nomes em inglês, texto de tela em pt-BR.** Rótulo de enum vem de `webapp/model/formatter.ts`; o módulo não usa i18n.
- **Valor de enum persistido entra SEMPRE no fim.** Mudança de enum não gera migration e nada avisa.
- **`StorageAddressNature`**: `Regular = 0`, `Transshipment = 1`. `Regular` é o default, o que dispensa backfill.
- **Natureza Transbordo só em armazém com `WarehouseComplement.IsOwn == true`**, escolhida na criação e **imutável** depois.
- **A liberação do passo 5 não carrega lote** — com lote, a Expedição drenaria o lote uma segunda vez.
- **O volume da carga continua contando só `SalesShipment (7)`**; o `Shipment (1)` do lote é movimento físico.
- Recalcular só DEPOIS do `SaveChangesAsync` que gravou as FKs; serviço interno em transação alheia vai em `CommitMode.Deferred`; `UnitOfWork.CommitAsync` não é aninhável.
- Todo serviço novo é registrado à mão em `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs`.
- Parâmetro de action OData: quantidade é `Edm.Double`, data é `string yyyy-MM-dd` lida com `TryParseExact`, enum é `string`, tudo que pode faltar é `.Optional()`, e o guard `parameters == null` vem antes de qualquer `TryGetValue`. Rota de navigation property só responde declarada à mão, nas duas formas.
- Commits: `tipo(escopo): descrição em pt-BR, imperativo, minúscula, sem ponto final`, escopo `shipment` ou `storage`, `Refs: GAC-1181`, `DB: <migration>` quando houver migration, terminando com:
  `Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>`
  `Claude-Session: https://claude.ai/code/session_01D9azYj2FzqPz5xBpqDerHM`
- Nunca `git push`. **Nunca aplicar migration sem pedir ao usuário** (banco de dev compartilhado).
- Testes: `dotnet test SiagroB1.Application.Tests/SiagroB1.Application.Tests.csproj`. Baseline **2188 passando**. ⚠️ O Web local roda do mesmo worktree e trava os DLLs: pare o Web antes, ou builde com `-p:BaseOutputPath=<dir temporário>`.
- Frontend: `yarn ts-typecheck` e `yarn lint` são os gates; **`yarn test` não é gate** (trava de cobertura irreal).
- **Um teste que afirma `Null`, `igual` ou `não lança` sobre um valor que já nasceria assim não prova nada.** Monte o cenário com o dado que a regra deveria barrar e, em teste de regressão de armadilha, veja o vermelho antes.

---

### Task 1: A natureza do lote

**Files:**
- Create: `SiagroB1.Domain/Enums/StorageAddressNature.cs`
- Modify: `SiagroB1.Domain/Entities/StorageAddress.cs`
- Modify: `SiagroB1.Application/Services/StorageAddresses/StorageAddressesCreateService.cs`
- Modify: `SiagroB1.Application/Services/StorageAddresses/StorageAddressesUpdateService.cs`
- Create: migration `AddStorageAddressNature`
- Test: `SiagroB1.Application.Tests/StorageAddresses/StorageAddressNatureTests.cs`

**Interfaces:**
- Produces: `StorageAddressNature` (`Regular = 0`, `Transshipment = 1`); `StorageAddress.Nature`.

- [ ] **Step 1: Escrever os testes que falham**

```csharp
[Fact] public async Task Create_RefusesTransshipmentNatureOnThirdPartyWarehouse()
[Fact] public async Task Create_AcceptsTransshipmentNatureOnOwnWarehouse()
[Fact] public async Task Update_RefusesChangingTheNature()
[Fact] public void Nature_DefaultsToRegular()
```

O primeiro monta `WarehouseComplement { WarehouseCode = "F010966", IsOwn = false }` **gravado no contexto** (não a ausência da linha, que é o caso comum e passa por outro ramo) e espera `ApplicationException`. O segundo monta `IsOwn = true` e espera sucesso. O terceiro grava um lote `Transshipment` e tenta atualizar para `Regular`, esperando recusa.

- [ ] **Step 2: Rodar e ver falhar** — `StorageAddressNature` não existe.

- [ ] **Step 3: Criar o enum**

```csharp
namespace SiagroB1.Domain.Enums;

/// <summary>
/// Para que o lote SERVE (GAC-1181 fase 2) — eixo independente de
/// <see cref="StorageOwnershipType"/> (de quem é a mercadoria) e de
/// <see cref="StorageAddressStatus"/> (ciclo de vida).
/// </summary>
public enum StorageAddressNature
{
    /// <summary>Lote comum de armazenagem.</summary>
    Regular = 0,

    /// <summary>
    /// Lote de passagem: recebe a mercadoria descarregada num transbordo e a devolve ao
    /// caminhão. Só existe em armazém próprio, fica fora da Expedição de Grãos comum e fora
    /// da cobrança de armazenagem e da quebra técnica.
    /// </summary>
    Transshipment = 1,
}
```

- [ ] **Step 4: Campo na entidade**, junto de `OwnershipType`:

```csharp
    /// <summary>
    /// Natureza do lote (GAC-1181 fase 2). Escolhida na criação e imutável: trocar a natureza
    /// de um lote com saldo mudaria o significado de movimentos já gravados.
    /// </summary>
    public StorageAddressNature Nature { get; set; } = StorageAddressNature.Regular;
```

- [ ] **Step 5: Travas de criação e de edição**

Em `StorageAddressesCreateService.ExecuteAsync`, ANTES do `AddAsync` (o serviço não abre transação; a validação vem antes da única escrita), recusar `Nature == Transshipment` quando o complemento do armazém não tiver `IsOwn == true`, com mensagem pt-BR nomeando o armazém. Injetar `IWarehouseComplementService`.

Em `StorageAddressesUpdateService`, recusar mudança de `Nature` comparando com o valor persistido — ⚠️ leia o original por `OriginalValues`, nunca pela entidade rastreada, que já vem com o valor novo no fluxo do OData.

- [ ] **Step 6: Rodar os testes da task; depois a suíte inteira.**

- [ ] **Step 7: Gerar a migration** (não aplicar):

```bash
dotnet ef migrations add AddStorageAddressNature \
  --project SiagroB1.Migrations --startup-project SiagroB1.Web --context AppDbContext
```

Conferir que sai `AddColumn<int>("Nature", "STORAGE_ADDRESSES", defaultValue: 0)`.

- [ ] **Step 8: Commit** (`feat(storage): dar natureza ao lote de armazenagem`, com `DB: AddStorageAddressNature`).

---

### Task 2: A pesagem só aceita lote de transbordo para quem é transbordo

**Files:**
- Modify: `SiagroB1.Application/Services/WeighingTickets/WeighingTicketsCompletedService.cs`
- Test: `SiagroB1.Application.Tests/WeighingTickets/WeighingTicketTransshipmentLotTests.cs`

**Interfaces:**
- Consumes: `StorageAddress.Nature` (Task 1).

O ticket de pesagem **já escolhe o lote** (`WeighingTicket.StorageAddressCode`), e é dele que nasce o `Receipt (0)` na entrada e o `Shipment (1)` na saída. Nada muda nesse caminho: a pesagem continua livre para usar lote de qualquer natureza. **Esta task só acrescenta os testes que provam que o romaneio gerado carrega o lote e a natureza esperada**, porque as Tasks 3 e 5 vão decidir com base nisso.

- [ ] **Step 1: Escrever os testes**

```csharp
[Fact] public async Task CompletedReceiptTicket_CreatesReceiptCarryingTheLot()
[Fact] public async Task CompletedShipmentTicket_CreatesShipmentCarryingTheLot()
```

Cada um monta um lote `Nature = Transshipment` em armazém próprio, completa o ticket e assere `TransactionType` (`Receipt`/`Shipment`), `StorageAddressCode` igual ao do lote e `TransactionStatus` confirmado.

- [ ] **Step 2: Rodar.** Se passarem sem mudança de produção, ótimo — são testes de caracterização e o commit é só deles. Se falharem, **pare e relate**: significa que a pesagem não preserva o lote, e o desenho da fase 2 depende disso.

- [ ] **Step 3: Commit** (`test(storage): provar que a pesagem preserva o lote no romaneio`).

---

### Task 3: Entrada do transbordo — só lote de transbordo, e o crédito do armazém volta

**Files:**
- Modify: `SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadsTransshipmentRegisterEntryService.cs`
- Modify: `SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadTransshipmentRules.cs`
- Test: `SiagroB1.Application.Tests/ShipmentLoads/ShipmentLoadsTransshipmentOwnWarehouseEntryTests.cs`

**Interfaces:**
- Produces: `ShipmentLoadTransshipmentRules.EnsureReceiptIsFromTransshipmentLotAsync(AppDbContext, StorageTransaction receipt)`.

O ramo `isOwn` de hoje aceita **qualquer** `Receipt` confirmado do armazém e não credita nada. Passa a exigir que o `Receipt` esteja num lote `Nature = Transshipment` e, além de vincular, cria o `TransshipmentReceipt (15)` creditando o **armazém** — o crédito simétrico do débito que a Expedição fará no passo 6.

- [ ] **Step 1: Escrever os testes que falham**

```csharp
[Fact] public async Task RegisterEntry_OwnWarehouse_RefusesReceiptFromRegularLot()
[Fact] public async Task RegisterEntry_OwnWarehouse_LinksReceiptFromTransshipmentLot()
[Fact] public async Task RegisterEntry_OwnWarehouse_CreatesTheWarehouseCreditReceipt()
[Fact] public async Task RegisterEntry_OwnWarehouse_TheCreditCarriesNeitherReleaseKeyNorLoadKey()
[Fact] public async Task RegisterEntry_OwnWarehouse_DoesNotEmitRelease()
```

O terceiro assere que existe um `TransshipmentReceipt (15)` confirmado, no armazém do transbordo, com `GrossWeight` igual ao do `Receipt` vinculado, **sem `StorageAddressCode`** (o crédito é do armazém, não do lote — com lote ele creditaria o lote duas vezes). O quarto é o teste de armadilha das duas chaves proibidas, no molde do que já existe para o armazém de terceiro: semeie a origem com `ShipmentReleaseKey` REAL antes de asserir que o 15 não a herdou.

- [ ] **Step 2: Rodar e ver falhar.**

- [ ] **Step 3: Regra nova em `ShipmentLoadTransshipmentRules`** — carrega o lote do `Receipt` e recusa quando `Nature != Transshipment`, com mensagem pt-BR dizendo que a entrada do transbordo tem de ser pesada num lote de transbordo.

- [ ] **Step 4: Ramo próprio do `RegisterEntryService`** — depois de validar o `Receipt` (confirmado, mesmo armazém/produto/filial/unidade, sem `ShipmentLoadKey` e sem `ShipmentLoadTransshipmentKey`) e a natureza do lote: grava `ShipmentLoadTransshipmentKey` no `Receipt`, cria e confirma o 15 em `CommitMode.Deferred` no molde do ramo de terceiro (mesma origem `TransactionCode.ShipmentLoad`, `CardCode` do primeiro romaneio de saída da origem), grava `EntryQuantity` = `GrossWeight` do `Receipt`, `SaveChangesAsync`, recalcula e registra `TransshipmentEntered` no log de movimentação.

- [ ] **Step 5: Rodar os testes da task e a suíte inteira. Commit.**

---

### Task 4: Vincular a saída do lote e emitir a liberação

**Files:**
- Create: `SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadsTransshipmentAttachLotExitService.cs`
- Modify: `SiagroB1.Domain/Entities/ShipmentLoadTransshipment.cs`
- Modify: `SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadTransshipmentRules.cs`
- Modify: `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs`
- Create: migration `AddTransshipmentLotExit`
- Test: `SiagroB1.Application.Tests/ShipmentLoads/ShipmentLoadsTransshipmentAttachLotExitServiceTests.cs`

**Interfaces:**
- Produces: `ShipmentLoadTransshipment.LotExitStorageTransactionKey` (`Guid?`) e nav property; `ShipmentLoadsTransshipmentAttachLotExitService.ExecuteAsync(Guid transshipmentKey, Guid lotExitStorageTransactionKey, string userName)`.

É o passo 5 do spec: o escritório vincula à carga o `Shipment (1)` que o armazém pesou, e **esse vínculo emite a liberação** pela quantidade real carregada.

- [ ] **Step 1: Escrever os testes que falham**

```csharp
[Fact] public async Task AttachLotExit_RefusesShipmentFromAnotherLot()
[Fact] public async Task AttachLotExit_RefusesWhenTheEntryWasNotRegistered()
[Fact] public async Task AttachLotExit_RefusesWhenAnExitIsAlreadyAttached()
[Fact] public async Task AttachLotExit_EmitsOneReleasePerPurchaseContract()
[Fact] public async Task AttachLotExit_ReleaseCarriesTheRealLoadedQuantityAndNoLot()
[Fact] public async Task AttachLotExit_ReleaseDoesNotConsumeThePurchaseContract()
[Fact] public async Task AttachLotExit_LeavesTheLoadInTransshipmentUntilTheSalesShipmentIsAttached()
[Fact] public async Task AttachLotExit_LeftoverStaysInTheLot()
```

O penúltimo prova o ponto mais sutil do desenho: vincular a saída do lote **não** conclui o transbordo nem torna a carga faturável — isso só acontece quando a Expedição (`SalesShipment (7)`) é vinculada. O último assere o saldo do lote depois da operação: entraram 50.000, saíram 49.000, sobram 1.000.

- [ ] **Step 2: Rodar e ver falhar.**

- [ ] **Step 3: Campo novo na entidade + migration** — `LotExitStorageTransactionKey`, FK `NoAction`, índice, no molde de `EntryStorageTransactionKey`.

- [ ] **Step 4: Escrever o serviço.** Ordem: carrega transbordo e carga → `EnsureLoadAcceptsTransshipment` → exige entrada registrada → exige que ainda não haja saída vinculada → carrega o `Shipment (1)`, exigindo `TransactionStatus` confirmado, `StorageAddressCode` **igual ao lote da entrada**, e nenhum vínculo anterior → em transação: grava `ShipmentLoadTransshipmentKey` no `Shipment (1)` e a FK no transbordo, `SaveChangesAsync`, emite as liberações por `ShipmentReleasesFromReturnService.BuildAsync(..., ReleaseOrigin.Transshipment)` com `GeneratedByStorageTransactionKey` apontando o `Shipment (1)` e **sem `StorageAddressCode`**, `SaveChangesAsync`, recalcula, registra o movimento, commit.

- [ ] **Step 5: Registrar no DI. Rodar os testes e a suíte. Commit** (com `DB: AddTransshipmentLotExit`).

---

### Task 5: Vincular a Expedição fecha o transbordo

**Files:**
- Modify: `SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadsAttachTransactionsService.cs`
- Test: `SiagroB1.Application.Tests/ShipmentLoads/ShipmentLoadsAttachOwnWarehouseExitTests.cs`

A validação de papel de hoje exige que o armazém do `SalesShipment (7)` seja o do transbordo — o que já vale para o armazém próprio. **Esta task prova que o passo 7 funciona sem mudança de produção** e acrescenta a única regra que falta: recusar vincular a Expedição antes de a saída do lote ter sido vinculada, porque a liberação que a originou nasce ali.

- [ ] **Step 1: Escrever os testes**

```csharp
[Fact] public async Task Attach_OwnWarehouseExit_ClosesTheTransshipmentAndMakesTheLoadBillable()
[Fact] public async Task Attach_OwnWarehouseExit_RefusedBeforeTheLotExitIsAttached()
```

- [ ] **Step 2: Rodar.** O primeiro deve passar sem mudança; se falhar, o desenho tem furo e você deve **parar e relatar**. O segundo falha e pede a regra nova.

- [ ] **Step 3: Implementar a recusa** em `ValidateTransshipmentRoleAsync`, com mensagem pt-BR mandando vincular antes a saída do lote.

- [ ] **Step 4: Rodar tudo. Commit.**

---

### Task 6: Estorno cobrindo o estado novo

**Files:**
- Modify: `SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadsTransshipmentReverseService.cs`
- Test: `SiagroB1.Application.Tests/ShipmentLoads/ShipmentLoadsTransshipmentReverseOwnWarehouseTests.cs`

- [ ] **Step 1: Escrever os testes que falham**

```csharp
[Fact] public async Task Reverse_OwnWarehouse_CancelsTheWarehouseCreditAndTheRelease()
[Fact] public async Task Reverse_OwnWarehouse_UnlinksBothWeighingRomaneiosWithoutCancellingThem()
[Fact] public async Task Reverse_OwnWarehouse_KeepsTheLeftoverAndTheLotNature()
[Fact] public async Task Reverse_OwnWarehouse_RefusesWhenTheReleaseWasAlreadyShipped()
```

O segundo é a regra que o usuário definiu: `Receipt (0)` e `Shipment (1)` são movimento físico pesado na balança e **não são cancelados** — só perdem o vínculo. O terceiro assere que o lote continua com `Nature = Transshipment` e com o saldo residual.

- [ ] **Step 2: Rodar e ver falhar.**

- [ ] **Step 3: Implementar** o ramo de armazém próprio: cancela o `15` e as liberações sem consumo, zera `ShipmentLoadTransshipmentKey` dos dois romaneios de pesagem, remove a linha do transbordo, recalcula e registra `TransshipmentReversed`.

- [ ] **Step 4: Rodar tudo. Commit.**

---

### Task 7: Isolar o lote de transbordo

**Files:**
- Modify: `SiagroB1.Application/Services/StorageAddresses/StorageAddressesListOpenedByItemService.cs`
- Modify: `SiagroB1.Application/Services/StorageAddresses/StorageAddressesStorageChargeCalculatorService.cs`
- Modify: `SiagroB1.Application/Services/StorageAddresses/StorageAddressesTechnicalLossCalculatorService.cs`
- Test: `SiagroB1.Application.Tests/StorageAddresses/TransshipmentLotIsolationTests.cs`

- [ ] **Step 1: Escrever os testes que falham**

```csharp
[Fact] public async Task ListOpenedByItem_HidesTransshipmentLots()
[Fact] public async Task ListOpenedByItem_StillListsRegularLots()
[Fact] public async Task StorageCharge_SkipsTransshipmentLots()
[Fact] public async Task TechnicalLoss_SkipsTransshipmentLots()
[Fact] public async Task BalanceReaders_StillSeeTransshipmentLots()
```

O último é tão importante quanto os outros: o lote tem saldo real, e some-lo dos leitores de saldo criaria divergência entre o físico e o relatado. Ele assere que `StorageAddressesGetBalanceService` continua devolvendo o saldo do lote de transbordo.

- [ ] **Step 2: Rodar e ver falhar.**

- [ ] **Step 3: Implementar os três filtros**, cada um com comentário em pt-BR dizendo **por quê** (mercadoria em trânsito; se aparecesse na Expedição comum, alguém embarcaria por fora e a carga ficaria esperando uma saída que já aconteceu).

- [ ] **Step 4: Rodar tudo. Commit.**

---

### Task 8: Camada OData e remoção do guard da fase 1

**Files:**
- Modify: `SiagroB1.Web/ODataConfig/ODataConfigurations.cs`
- Create: `SiagroB1.Web/Actions/ShipmentLoads/ShipmentLoadsTransshipmentAttachLotExitController.cs`
- Modify: `SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadTransshipmentRules.cs`
- Modify: `SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadsTransshipmentStartService.cs`
- Modify: `SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadsRefuseService.cs`
- Test: `SiagroB1.Application.Tests/ShipmentLoads/ShipmentLoadTransshipmentPhase2EdmTests.cs`

- [ ] **Step 1: Escrever o teste de EDM que falha** — monta o EDM real e assere a action `ShipmentLoadsTransshipmentAttachLotExit` com `Key` e `LotExitStorageTransactionKey` como `Edm.Guid` obrigatórios, e `StorageAddress` expondo `Nature`.

- [ ] **Step 2: Rodar e ver falhar.**

- [ ] **Step 3: Declarar a action e o controller**, no molde de `ShipmentLoadsTransshipmentReverseController`.

- [ ] **Step 4: Remover `EnsureWarehouseAcceptsTransshipmentAsync`** e as duas chamadas (Start e o ramo Transbordo da recusa), junto com os testes que provavam a recusa — a fase 2 os substitui. Deixe no XML-doc do arquivo de regras uma linha dizendo que a trava existiu na fase 1 e por que saiu.

- [ ] **Step 5: Rodar tudo. Commit.**

---

### Task 9: Frontend — natureza no cadastro de lotes

**Files:**
- Modify: `webapp/view/storageAddresses/*` (formulário de criação e edição)
- Modify: `webapp/controller/storageAddresses/*`
- Modify: `webapp/model/formatter.ts`

- [ ] **Step 1:** `Select` "Natureza" no formulário, com "Comum" e "Transbordo", **desabilitado na edição** (a natureza é imutável) e só com a opção Transbordo quando o armazém escolhido tiver complemento `IsOwn` — leia o complemento com `WarehousesGetComplement`, como a tela de complemento de armazém já faz.
- [ ] **Step 2:** coluna "Natureza" na lista de lotes, com `formatStorageAddressNature`.
- [ ] **Step 3:** `yarn ts-typecheck` e `yarn lint`. Commit no repositório do frontend.

⚠️ Se o campo for exibido por `getProperty` em algum controller, garanta o `$select` — campo que a tela não mostra chega `undefined` em silêncio.

---

### Task 10: Frontend — vincular a saída do lote na carga

**Files:**
- Modify: `webapp/view/shipmentLoads/fragments/ShipmentLoadTransshipments.fragment.xml`
- Modify: `webapp/controller/shipmentLoads/BaseController.ts`
- Modify: `webapp/model/formatter.ts`

- [ ] **Step 1:** botão **"Vincular Saída do Lote"** na toolbar da seção Transbordos, visível só quando o transbordo tem entrada registrada e ainda não tem saída de lote.
- [ ] **Step 2:** diálogo listando os `Shipment (1)` confirmados do **lote do transbordo**, ainda sem vínculo — `$filter` como string crua, com aspas escapadas, e todos os campos lidos pelo controller no `$select`.
- [ ] **Step 3:** chamar a action, fechar o diálogo só depois do resolve, e dar `refresh` no contexto do elemento e nas tabelas.
- [ ] **Step 4:** situação **"Aguardando expedição"** em `formatTransshipmentStatus`, entre "Aguardando saída" e "Concluído".
- [ ] **Step 5:** gates e commit.

---

### Task 11: Verificação no navegador

Roteiro do spec, ponta a ponta, com os dois papéis. **As duas pesagens são do usuário** — ele opera a balança; o restante é conduzido por quem executa o plano.

- [ ] **Step 1: Gates** — `dotnet test` inteiro verde; `yarn ts-typecheck` e `yarn lint` limpos.
- [ ] **Step 2: Aplicar as migrations** (`AddStorageAddressNature`, `AddTransshipmentLotExit`) **pedindo ao usuário antes** — o banco de dev é compartilhado. Sempre com `ASPNETCORE_ENVIRONMENT=Yokotobi-Development`: sem o ambiente explícito o `dotnet ef` cai no `appsettings.json`, que aponta para o servidor `192.168.1.144`.
- [ ] **Step 3: Subir a stack** (Web e Gateway no profile `yktb`, `yarn start:dev`), combinando as portas com a outra sessão. **Buildar uma vez com tudo parado** antes de subir: dois `dotnet run` simultâneos colidem no mesmo DLL.
- [ ] **Step 4: Percorrer os sete passos**, conferindo a cada um:
  1. criar o lote de transbordo — e confirmar que a opção não aparece para armazém de terceiro;
  2. **(usuário)** pesagem de entrada no lote → conferir saldo do LOTE subindo e do ARMAZÉM parado;
  3. Registrar Entrada na carga → conferir o romaneio 15 criado e o saldo do ARMAZÉM subindo;
  4. **(usuário)** pesagem de saída com peso menor → conferir o lote debitado e a sobra;
  5. Vincular Saída do Lote → conferir a liberação emitida pela quantidade REAL, sem lote, e a carga ainda **não** faturável;
  6. Expedição de Grãos a partir dessa liberação → conferir a perna de venda e o armazém debitado;
  7. Vincular Romaneios no papel do transbordo → conferir transbordo **Concluído**, carga faturável e os quatro números.
- [ ] **Step 5: Travas e estorno** — lote de transbordo ausente da Expedição de Grãos comum; estornar um transbordo e conferir que os dois romaneios de pesagem continuam confirmados, que o 15 e a liberação foram cancelados e que a sobra e a natureza do lote permanecem.
- [ ] **Step 6: Regressão do armazém de terceiro** — o fluxo da fase 1, inteiro, continua funcionando.
- [ ] **Step 7: Registrar no spec** a seção "Verificação executada", com o que foi exercido, os achados e os dados deixados no banco. Commit.
