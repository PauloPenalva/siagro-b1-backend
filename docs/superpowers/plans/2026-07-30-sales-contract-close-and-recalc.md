# Encerramento e recálculo de saldo no contrato de venda — plano de implementação

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Levar as ações *Recalcular Saldo*, *Encerrar* e *Reabrir* para a tela de Detalhe do contrato de venda e impedir, nos dois lados (venda e compra), que um contrato com saldo negativo seja encerrado.

**Architecture:** O backend de venda já tem `SalesContractsCloseService`, `SalesContractsReopenService` e `SalesContractsRecalculateBalanceService` com actions OData registradas — nenhuma action nova. As mudanças de servidor são só os dois guards de saldo; o resto é UI. O guard decide sobre o saldo **recalculado do ledger na hora** (`CalculateAllocatedAsync`), nunca sobre o `AllocatedVolume` persistido, que pode estar defasado e produziria bloqueio falso.

**Tech Stack:** .NET 10 / EF Core / OData (ASP.NET Core OData 8) no backend; OpenUI5 1.141 + TypeScript, OData v4, no frontend. Testes: xUnit + EF InMemory (`SiagroB1.Application.Tests`).

Spec: `docs/superpowers/specs/2026-07-30-sales-contract-close-and-recalc-design.md`

## Global Constraints

- **Nunca commitar.** Commits são manuais, feitos pelo usuário. Todo arquivo **novo** deve ser staged com `git add <path>` no sub-repo a que pertence; arquivos já rastreados não precisam de nada.
- Identificadores de código sempre em **inglês**; texto visível ao usuário sempre em **pt-BR**.
- Saldo negativo = `TotalVolume − AllocatedVolume < 0`. Saldo **positivo ou zero encerra normalmente** — encerrar é como se abre mão do volume não entregue.
- Arredondamento: venda em 3 casas, compra em 2 (`MidpointRounding.ToEven`), cada lado como já faz na sua entidade.
- O encerramento **não persiste** o valor recalculado — só muda o status.
- Nos bindings de `Status` em XML use sempre `targetType: 'any'`; sem isso o enum do modelo v4 chega formatado e a comparação falha só no browser.
- Rotas OData deferidas no `ServerRoutes` precisam do sufixo `(...)`, senão `invoke()` falha com "The binding must be deferred" — também só quebra no browser.

---

### Task 1: Guard de saldo negativo no encerramento de VENDA

**Files:**
- Create: `SiagroB1.Application.Tests/SalesContracts/SalesContractsCloseNegativeBalanceGuardTests.cs`
- Modify: `SiagroB1.Application/Services/SalesContracts/SalesContractsCloseService.cs`

**Interfaces:**
- Consome: `SalesContractsRecalculateBalanceService.CalculateAllocatedAsync(AppDbContext, Guid) → Task<decimal>` (já existe, estático, público).
- Produz: nada de novo para outras tasks — só o comportamento do `SalesContractsCloseService.ExecuteAsync`.

- [ ] **Step 1: Escrever os testes falhando**

Criar `SiagroB1.Application.Tests/SalesContracts/SalesContractsCloseNegativeBalanceGuardTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.SalesContracts;

/// <summary>
/// Contrato faturado ALÉM do volume contratado não pode ser encerrado: encerrar esconde o
/// erro de distribuição (o contrato some das listas de alocação e do recálculo em lote) e
/// deixa o volume excedente órfão. O guard decide sobre o saldo RECALCULADO do ledger —
/// AllocatedVolume é persistido-derivado e pode estar defasado.
/// </summary>
public class SalesContractsCloseNegativeBalanceGuardTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private SalesContractsCloseService CloseService() =>
        new(_db.Context, new SalesContractsFixedVolumeService(_db.Context),
            TestNotificationOutbox.For(_db.Context));

    private static SalesContract NewContract(decimal totalVolume, decimal allocatedVolume) => new()
    {
        Key = Guid.NewGuid(),
        Code = "SC-NEG",
        CardCode = "C0001",
        ItemCode = "SOJA",
        UnitOfMeasureCode = "KG",
        HarvestSeasonCode = "24/25",
        TotalVolume = totalVolume,
        AllocatedVolume = allocatedVolume,
        Type = ContractType.Fixed,
        Status = ContractStatus.Approved,
    };

    /// <param name="ledgerVolume">
    /// Volume gravado no ledger. Quando difere de <paramref name="allocatedVolume"/>,
    /// reproduz o drift do agregado persistido.
    /// </param>
    private async Task<SalesContract> SeedAsync(
        decimal totalVolume, decimal allocatedVolume, decimal? ledgerVolume = null)
    {
        var contract = NewContract(totalVolume, allocatedVolume);
        var item = new SalesInvoiceItem
        {
            Key = Guid.NewGuid(),
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            Quantity = 1m,
            DeliveryStatus = SalesInvoiceDeliveryStatus.Open,
        };

        _db.Context.SalesContracts.Add(contract);
        _db.Context.SalesInvoicesItems.Add(item);
        _db.Context.SalesContractsAllocations.Add(new SalesContractAllocation
        {
            Key = Guid.NewGuid(),
            SalesContractKey = contract.Key,
            SalesInvoiceItemKey = item.Key!.Value,
            Volume = ledgerVolume ?? allocatedVolume,
            Origin = SalesContractAllocationOrigin.Billing,
        });
        await _db.Context.SaveChangesAsync();

        return contract;
    }

    private async Task<SalesContract> ReloadAsync(Guid key) =>
        await _db.Context.SalesContracts.AsNoTracking().SingleAsync(x => x.Key == key);

    [Fact]
    public async Task Close_NegativeBalance_Throws_AndKeepsApproved()
    {
        var sc = await SeedAsync(totalVolume: 1000m, allocatedVolume: 1200m);

        var ex = await Assert.ThrowsAsync<ApplicationException>(() =>
            CloseService().ExecuteAsync(sc.Key, "tester"));

        Assert.Contains("além do volume contratado", ex.Message);
        Assert.Equal(ContractStatus.Approved, (await ReloadAsync(sc.Key)).Status);
    }

    [Fact]
    public async Task Close_PositiveBalance_Succeeds()
    {
        var sc = await SeedAsync(totalVolume: 1000m, allocatedVolume: 600m);

        await CloseService().ExecuteAsync(sc.Key, "tester");

        Assert.Equal(ContractStatus.Finished, (await ReloadAsync(sc.Key)).Status);
    }

    [Fact]
    public async Task Close_ZeroBalance_Succeeds()
    {
        var sc = await SeedAsync(totalVolume: 1000m, allocatedVolume: 1000m);

        await CloseService().ExecuteAsync(sc.Key, "tester");

        Assert.Equal(ContractStatus.Finished, (await ReloadAsync(sc.Key)).Status);
    }

    /// <summary>
    /// O motivo de o guard recalcular: agregado persistido negativo por drift, ledger dizendo
    /// que o contrato está são. Ler o persistido bloquearia um contrato correto.
    /// </summary>
    [Fact]
    public async Task Close_PersistedBalanceNegativeButLedgerHealthy_Succeeds()
    {
        var sc = await SeedAsync(totalVolume: 1000m, allocatedVolume: 1200m, ledgerVolume: 800m);

        await CloseService().ExecuteAsync(sc.Key, "tester");

        Assert.Equal(ContractStatus.Finished, (await ReloadAsync(sc.Key)).Status);
    }
}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~SalesContractsCloseNegativeBalanceGuardTests"`
Expected: FAIL — `Close_NegativeBalance_Throws_AndKeepsApproved` não lança (os outros três já passam, porque hoje nada bloqueia).

- [ ] **Step 3: Implementar o guard**

Em `SalesContractsCloseService.ExecuteAsync`, entre o guard de fixação e a mudança de status:

```csharp
        if (contract.Type == ContractType.ToBeDetermined)
            await GuardPriceFixationAsync(contract);

        await GuardNegativeBalanceAsync(contract);

        contract.Status = ContractStatus.Finished;
```

E o método novo, no fim da classe:

```csharp
    /// <summary>
    /// Contrato faturado ALÉM do volume contratado não pode ser congelado: encerrado, ele sai
    /// das listas de alocação e do recálculo em lote, e o volume excedente fica órfão.
    /// Decide sobre o saldo RECALCULADO do ledger, não sobre <c>AllocatedVolume</c>: o agregado
    /// é persistido-derivado e pode estar defasado — usar o valor persistido barraria contratos
    /// corretos por drift. Não persiste o recálculo: encerrar não tem efeito colateral de saldo
    /// (para isso existe o botão Recalcular Saldo, ao lado na tela).
    /// </summary>
    private async Task GuardNegativeBalanceAsync(SalesContract contract)
    {
        var allocated = await SalesContractsRecalculateBalanceService
            .CalculateAllocatedAsync(context, contract.Key);
        var balance = decimal.Round(contract.TotalVolume - allocated, 3, MidpointRounding.ToEven);

        if (balance < 0)
            throw new ApplicationException(
                $"Contrato faturado além do volume contratado. Contratado: {contract.TotalVolume:N3}, " +
                $"alocado: {allocated:N3}, saldo: {balance:N3}. " +
                "Ajuste as alocações na tela de conciliação de saldos antes de encerrar.");
    }
```

- [ ] **Step 4: Rodar e ver passar**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~SalesContractsCloseNegativeBalanceGuardTests"`
Expected: PASS, 4 testes.

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~SalesContractsCloseFixationGuardTests"`
Expected: PASS — os contratos daquele arquivo têm `AllocatedVolume` 0 e `TotalVolume` positivo, logo saldo positivo.

- [ ] **Step 5: Stage do arquivo novo**

```bash
git add SiagroB1.Application.Tests/SalesContracts/SalesContractsCloseNegativeBalanceGuardTests.cs
```

---

### Task 2: Guard de saldo negativo no encerramento de COMPRA

**Files:**
- Modify: `SiagroB1.Application/Services/PurchaseContracts/PurchaseContractsRecalculateBalanceService.cs`
- Modify: `SiagroB1.Application/Services/PurchaseContracts/PurchaseContractsCloseService.cs`
- Modify: `SiagroB1.Application.Tests/PurchaseContracts/PurchaseContractsCloseReopenServiceTests.cs`

**Interfaces:**
- Produz: `PurchaseContractsRecalculateBalanceService.CalculateAllocatedAsync(AppDbContext context, Guid purchaseContractKey) → Task<decimal>` — público e estático, espelhando o do lado de venda.

- [ ] **Step 1: Escrever os testes falhando**

Acrescentar ao fim de `PurchaseContractsCloseReopenServiceTests` (a classe já tem `NewContract`, `SeedAsync`, `ReloadAsync` e `CloseService`):

```csharp
    /// <param name="ledgerVolume">
    /// Volume no ledger; quando difere de <paramref name="allocatedVolume"/>, reproduz o drift
    /// do agregado persistido.
    /// </param>
    private async Task<PurchaseContract> SeedWithAllocationAsync(
        decimal totalVolume, decimal allocatedVolume, decimal? ledgerVolume = null)
    {
        var contract = NewContract(ContractStatus.Approved);
        contract.TotalVolume = totalVolume;
        contract.AllocatedVolume = allocatedVolume;

        _db.Context.PurchaseContracts.Add(contract);
        _db.Context.PurchaseContractsAllocations.Add(new PurchaseContractAllocation
        {
            Key = Guid.NewGuid(),
            PurchaseContractKey = contract.Key,
            // FK obrigatória da entidade; o provider InMemory não a valida, mas
            // preenchemos para não depender disso.
            StorageTransactionKey = Guid.NewGuid(),
            Volume = ledgerVolume ?? allocatedVolume,
        });
        await _db.Context.SaveChangesAsync();

        return contract;
    }

    [Fact]
    public async Task Close_NegativeBalance_Throws_AndKeepsApproved()
    {
        var pc = await SeedWithAllocationAsync(totalVolume: 1000m, allocatedVolume: 1200m);

        var ex = await Assert.ThrowsAsync<ApplicationException>(() =>
            CloseService().ExecuteAsync(pc.Key, "tester"));

        Assert.Contains("além do volume contratado", ex.Message);
        Assert.Equal(ContractStatus.Approved, (await ReloadAsync(pc.Key)).Status);
    }

    [Fact]
    public async Task Close_PositiveBalance_Succeeds()
    {
        var pc = await SeedWithAllocationAsync(totalVolume: 1000m, allocatedVolume: 600m);

        await CloseService().ExecuteAsync(pc.Key, "tester");

        Assert.Equal(ContractStatus.Finished, (await ReloadAsync(pc.Key)).Status);
    }

    [Fact]
    public async Task Close_ZeroBalance_Succeeds()
    {
        var pc = await SeedWithAllocationAsync(totalVolume: 1000m, allocatedVolume: 1000m);

        await CloseService().ExecuteAsync(pc.Key, "tester");

        Assert.Equal(ContractStatus.Finished, (await ReloadAsync(pc.Key)).Status);
    }

    /// <summary>
    /// Agregado persistido negativo por drift, ledger são: não pode barrar.
    /// </summary>
    [Fact]
    public async Task Close_PersistedBalanceNegativeButLedgerHealthy_Succeeds()
    {
        var pc = await SeedWithAllocationAsync(
            totalVolume: 1000m, allocatedVolume: 1200m, ledgerVolume: 800m);

        await CloseService().ExecuteAsync(pc.Key, "tester");

        Assert.Equal(ContractStatus.Finished, (await ReloadAsync(pc.Key)).Status);
    }
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~PurchaseContractsCloseReopenServiceTests"`
Expected: FAIL em `Close_NegativeBalance_Throws_AndKeepsApproved`; os demais passam.

- [ ] **Step 3: Extrair o cálculo para um estático reutilizável**

Em `PurchaseContractsRecalculateBalanceService`, criar o método público e fazer o privado usá-lo:

```csharp
    /// <summary>
    /// Σ Volume assinado do ledger de alocações — fonte única do consumo do contrato,
    /// idêntica ao runtime/backfill. Estática para o guard de encerramento reusar sem
    /// instanciar o serviço (espelha SalesContractsRecalculateBalanceService).
    /// </summary>
    public static async Task<decimal> CalculateAllocatedAsync(
        AppDbContext context, Guid purchaseContractKey) =>
        await context.PurchaseContractsAllocations
            .Where(a => a.PurchaseContractKey == purchaseContractKey)
            .SumAsync(a => a.Volume);
```

E em `RecalculateAsync`, trocar o bloco inline pelo novo método:

```csharp
        // Σ com sinal — igual ao runtime/backfill.
        var newAllocated = await CalculateAllocatedAsync(context, contract.Key);
```

- [ ] **Step 4: Implementar o guard**

Em `PurchaseContractsCloseService.ExecuteAsync`, no mesmo ponto do lado de venda (depois do guard de fixação, antes de mudar o status), chamar `await GuardNegativeBalanceAsync(contract);` e acrescentar:

```csharp
    /// <summary>
    /// Espelha SalesContractsCloseService: contrato consumido ALÉM do volume contratado não
    /// pode ser congelado. Decide sobre o saldo RECALCULADO do ledger — AllocatedVolume é
    /// persistido-derivado e pode estar defasado. Não persiste o recálculo.
    /// </summary>
    private async Task GuardNegativeBalanceAsync(PurchaseContract contract)
    {
        var allocated = await PurchaseContractsRecalculateBalanceService
            .CalculateAllocatedAsync(context, contract.Key);
        var balance = decimal.Round(contract.TotalVolume - allocated, 2, MidpointRounding.ToEven);

        if (balance < 0)
            throw new ApplicationException(
                $"Contrato faturado além do volume contratado. Contratado: {contract.TotalVolume:N2}, " +
                $"alocado: {allocated:N2}, saldo: {balance:N2}. " +
                "Ajuste as alocações antes de encerrar.");
    }
```

Se o parâmetro do `AppDbContext` no construtor primário tiver outro nome, usar o nome real.

- [ ] **Step 5: Rodar tudo**

Run: `dotnet test SiagroB1.Application.Tests`
Expected: PASS, suíte inteira (era 665 + 8 antes desta feature).

Run: `dotnet build SiagroB1.sln`
Expected: `Compilação com êxito`, 0 erros. Parar o `SiagroB1.Web` antes — com ele rodando a DLL não é atualizada.

---

### Task 3: Rotas e tipo no frontend

**Files:**
- Create: `webapp/types/SalesContractRecalcResult.ts`
- Modify: `webapp/model/ServerRoutes.ts`

**Interfaces:**
- Produz: `SalesContractRecalcResult` (type), `api.salesContractsClose`, `api.salesContractsReopen`. `api.salesContractsRecalculateBalance` já existe e não muda.

- [ ] **Step 1: Criar o type**

`webapp/types/SalesContractRecalcResult.ts`:

```ts
export type SalesContractRecalcResult = {
  Key: string;
  Code?: string;
  PreviousAllocatedVolume: number;
  NewAllocatedVolume: number;
  PreviousAvaiableVolume: number;
  NewAvaiableVolume: number;
  Changed: boolean;
}
```

- [ ] **Step 2: Acrescentar as duas rotas**

Em `webapp/model/ServerRoutes.ts`, junto das demais ações REST de venda (perto de `salesContractsWithdrawApproval`):

```ts
  salesContractsClose: '/odata/SalesContractsClose',
  salesContractsReopen: '/odata/SalesContractsReopen',
```

Não criar entrada nova para o recálculo: `salesContractsRecalculateBalance: '/SalesContractsRecalculateBalance(...)'` já existe e é a que o handler vai usar via `bindContext`.

- [ ] **Step 3: Stage do arquivo novo**

```bash
git add webapp/types/SalesContractRecalcResult.ts
```

---

### Task 4: Handlers no Detail do contrato de venda

**Files:**
- Modify: `webapp/controller/salesContracts/Detail.controller.ts`

**Interfaces:**
- Consome: `SalesContractRecalcResult` (Task 3), `api.salesContractsClose` / `api.salesContractsReopen` (Task 3), `api.salesContractsRecalculateBalance` (já existente).
- Produz: `onRecalculateBalance()`, `onCloseContract()`, `onReopenContract()` — os nomes que a view da Task 5 referencia.

- [ ] **Step 1: Acrescentar os imports que faltam**

No topo de `Detail.controller.ts` (já importa `Context`, `JSONModel`, `ODataModel`, `RequestModel`):

```ts
import MessageBox from "sap/m/MessageBox";
import MessageToast from "sap/m/MessageToast";
import { confirmDialog } from "siagrob1/helpers/DialogHelpers";
import { SalesContractRecalcResult } from "siagrob1/types/SalesContractRecalcResult";
```

- [ ] **Step 2: Implementar os três handlers**

Antes do fechamento da classe (depois de `applyPostApprovalEditable`):

```ts
  /**
   * AllocatedVolume é persistido-derivado e pode dessincronizar do ledger de alocações.
   * Este botão reconcilia UM contrato a partir do ledger — é o mesmo cálculo do recálculo
   * em lote da tela de conciliação.
   *
   * O Saldo do header está ligado direto em AvaiableVolume da entidade, então o refresh do
   * contexto já repinta o número: não há viewModel para atualizar (diferente da tela de
   * compra, que lê os totais de um endpoint separado).
   */
  async onRecalculateBalance() {
    const oContext = this.getView().getBindingContext() as Context;
    if (!oContext) {
      return;
    }

    if (!await confirmDialog("Recalcular o saldo do contrato a partir das alocações ?")) {
      return;
    }

    const action = (this.getModel() as ODataModel)
      .bindContext(this.api.salesContractsRecalculateBalance);
    action.setParameter("Key", oContext.getProperty("Key") as string);

    this.setBusy(true);
    try {
      await action.invoke();
      const result = action.getBoundContext().getObject() as SalesContractRecalcResult;

      const fmt = (v: number) =>
        Number(v ?? 0).toLocaleString("pt-BR", { minimumFractionDigits: 3, maximumFractionDigits: 3 });

      if (result.Changed) {
        MessageBox.information(
          `Saldo recalculado.\n\n` +
          `Alocado: ${fmt(result.PreviousAllocatedVolume)} → ${fmt(result.NewAllocatedVolume)}\n` +
          `Disponível: ${fmt(result.PreviousAvaiableVolume)} → ${fmt(result.NewAvaiableVolume)}`
        );
      } else {
        MessageToast.show("Saldo já estava correto.");
      }

      oContext.refresh();
    } finally {
      this.setBusy(false);
    }
  }

  async onCloseContract() {
    const oContext = this.getView().getBindingContext() as Context;
    if (!oContext) {
      return;
    }

    if (!await confirmDialog("Encerrar o contrato ? Após encerrado não será possível movimentá-lo.")) {
      return;
    }

    const key = oContext.getProperty("Key") as string;

    this.setBusy(true);

    void jQuery.ajax({
      url: `${this.api.salesContractsClose}`,
      method: 'POST',
      data: JSON.stringify({ Key: key }),
      contentType: 'application/json',
      success: () => {
        oContext.refresh();
      },
      error: err => {
        this.setBusy(false);
        const message = (err.responseJSON as { error?: { message?: string } })?.error?.message;
        MessageBox.error(message ?? "Erro ao encerrar o contrato.");
      },
    })
    .done(() => this.setBusy(false));
  }

  async onReopenContract() {
    const oContext = this.getView().getBindingContext() as Context;
    if (!oContext) {
      return;
    }

    if (!await confirmDialog("Reabrir o contrato ? Ele voltará a aceitar movimentação.")) {
      return;
    }

    const key = oContext.getProperty("Key") as string;

    this.setBusy(true);

    void jQuery.ajax({
      url: `${this.api.salesContractsReopen}`,
      method: 'POST',
      data: JSON.stringify({ Key: key }),
      contentType: 'application/json',
      success: () => {
        oContext.refresh();
      },
      error: err => {
        this.setBusy(false);
        const message = (err.responseJSON as { error?: { message?: string } })?.error?.message;
        MessageBox.error(message ?? "Erro ao reabrir o contrato.");
      },
    })
    .done(() => this.setBusy(false));
  }
```

A mensagem do guard de saldo negativo chega ao usuário exatamente por esse `MessageBox.error` — o `ApplicationException` vira 400 com `error.message`.

- [ ] **Step 3: Typecheck e lint**

Run: `cd ../siagro-b1-frontend && yarn ts-typecheck`
Expected: `Done` sem erros.

Run: `yarn lint`
Expected: `Done` sem erros.

---

### Task 5: Botões na view do Detail

**Files:**
- Modify: `webapp/view/salesContracts/Detail.view.xml:29-34`

**Interfaces:**
- Consome: `onRecalculateBalance`, `onCloseContract`, `onReopenContract` (Task 4).

- [ ] **Step 1: Acrescentar os três botões**

No `<uxap:actions>`, depois do botão "Enviar para Aprovação":

```xml
          <Button visible="{= ${path: 'Status', targetType: 'any'}==='Approved' &amp;&amp; !${ui&gt;/readonly} }" text="Recalcular Saldo" type="Transparent" press=".onRecalculateBalance"/>
          <Button visible="{= ${path: 'Status', targetType: 'any'}==='Approved' &amp;&amp; !${ui&gt;/readonly} }" text="Encerrar" type="Transparent" press=".onCloseContract"/>
          <Button visible="{= ${path: 'Status', targetType: 'any'}==='Finished' &amp;&amp; !${ui&gt;/readonly} }" text="Reabrir" type="Transparent" press=".onReopenContract"/>
```

Copiar a forma exata de `webapp/view/purchaseContracts/Detail.view.xml:34-36` — inclusive o `&amp;&amp;` e a referência ao modelo `ui`. (O `&gt;` acima é escape deste documento; no XML é `${ui>/readonly}`.)

- [ ] **Step 2: Gates do frontend**

Run: `yarn ts-typecheck && yarn lint`
Expected: sem erros.

Run: `yarn ui5lint`
Expected: falha global pré-existente (633 erros no repo). Conferir com
`yarn ui5lint 2>&1 | grep -A6 "salesContracts.Detail.view.xml"` que **nenhum achado novo**
apareceu no arquivo tocado.

---

### Task 6: Verificação no browser

**Files:** nenhum.

- [ ] **Step 1: Subir a stack**

Backend, do `siagro-b1-backend/`, em dois terminais:

```bash
dotnet run --project SiagroB1.Web --launch-profile yktb
dotnet run --project SiagroB1.Gateway --launch-profile yktb
```

Frontend, do `siagro-b1-frontend/`: `yarn start:dev`. Login `admin` / `1234`.

- [ ] **Step 2: Conferir o caminho do usuário**

1. Abrir `/sales-contracts`, escolher um contrato **Approved** e entrar no Detalhe.
2. Header deve mostrar *Recalcular Saldo* e *Encerrar* (e **não** *Reabrir*).
3. Clicar em *Recalcular Saldo* → confirmar → ou `MessageBox` com antes → depois, ou toast "Saldo já estava correto."; o Saldo do header deve refletir o resultado.
4. Localizar um contrato de saldo negativo em `/sales-contracts/reconciliation`, abrir seu Detalhe e clicar em *Encerrar* → mensagem "Contrato faturado além do volume contratado…" e o status **continua** `Aprovado`.
5. Num contrato com saldo positivo, *Encerrar* → status vira `Encerrado` e o botão *Reabrir* aparece no lugar dos outros dois.
6. *Reabrir* → volta para `Aprovado`.

⚠️ Os passos 5 e 6 **gravam no banco do Yokotobi** (dado real). Se não houver contrato de teste descartável, parar no passo 4 e registrar que o encerramento efetivo não foi exercitado no browser.

- [ ] **Step 3: Derrubar a stack**

Encerrar os três processos e conferir que as portas 50000, 5246 e 8080 ficaram livres.
