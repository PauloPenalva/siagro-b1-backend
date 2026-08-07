# Referência ao contrato de compra na linha do Documento de Entrada — Plano de Implementação

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Dar à linha do Documento de Entrada tipo `Normal` um campo que referencia o contrato de compra, para conciliação fiscal — sem nenhum efeito em saldo, valor ou allocation.

**Architecture:** FK nullable `PurchaseInvoiceItem.PurchaseContractKey` espelhando `SalesInvoiceItem.SalesContractKey`. Value help bindando o entity set `/PurchaseContracts` com `$filter` estático de status e fornecedor/produto como filtros de runtime — **sem endpoint novo**, seguindo o diálogo da NF de origem. Regra de validação compartilhada em `PurchaseInvoiceLineGuard`, chamada pelos quatro serviços que gravam.

**Tech Stack:** .NET 10, EF Core (SQL Server), OData v4 (Microsoft.AspNetCore.OData), xUnit + EF InMemory, OpenUI5 1.141 + TypeScript.

**Spec:** `docs/superpowers/specs/2026-08-07-purchase-invoice-contract-reference-design.md`

## Global Constraints

- **NUNCA commitar ou dar push.** O CLAUDE.md do projeto é explícito e sobrepõe o fluxo padrão desta skill: os commits são feitos manualmente pelo usuário. Onde o template desta skill pediria `git commit`, este plano pede **apenas `git add`**. Todo arquivo NOVO deve ser `git add`-ado assim que criado, no sub-repo a que pertence.
- **Dois repositórios git distintos.** `siagro-b1-backend/` e `siagro-b1-frontend/` são repos independentes. Uma mudança que cruza os dois nunca é um commit só. Sempre use `git -C "<caminho do repo>"` ou confirme em qual você está.
- **Identificadores em inglês, texto de usuário em pt-BR.** Classes, propriedades e colunas em inglês (`PurchaseContractKey`, `PURCHASE_CONTRACT_KEY`); labels, títulos de menu e mensagens de erro de negócio em pt-BR.
- **Decimal editável no UI5 usa `sap.ui.model.odata.type.Double`, nunca `Decimal`.** `Decimal` faz parse para string e o backend devolve 400 sem nomear o campo. Não se aplica a esta feature (não há campo decimal novo), mas não regrida os existentes ao mexer no fragmento.
- **`showValueHelp` liga em `{ui>/editable}`, nunca em `"true"`** — o fragmento `Items` é compartilhado com o Detail read-only.
- Backend roda em `SiagroB1.Web` (`localhost:50000`) + `SiagroB1.Gateway` (`localhost:5246`), profile `yktb`. Frontend: `yarn start:dev` (`localhost:8080`). Login `admin` / `1234`.
- **`dotnet build SiagroB1.sln` falha com erro de lock se Web/Gateway/Reports estiverem rodando.** Pare os serviços antes de compilar, ou compile só o projeto de testes.
- `yarn test` do frontend **não** é gate: o limiar de cobertura de 50% reprova contra ~2,4% reais, independentemente da mudança. Os gates do frontend são `yarn ts-typecheck` e `yarn lint`.

---

## Estrutura de arquivos

**Backend (`siagro-b1-backend/`)**

| Arquivo | Responsabilidade |
|---|---|
| `SiagroB1.Domain/Entities/PurchaseInvoiceItem.cs` | *modificar* — FK + navegação |
| `SiagroB1.Infra/Context/AppDbContext.cs` | *modificar* — FK `Restrict` |
| `SiagroB1.Migrations/AppContext/<timestamp>_AddPurchaseInvoiceItemContract.cs` | *criar* (scaffold) |
| `SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoiceLineGuard.cs` | *modificar* — regra compartilhada de validação |
| `SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoicesCreateService.cs` | *modificar* — guard no deep-insert |
| `SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoicesItemsCreateService.cs` | *modificar* — guard no POST de linha |
| `SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoicesItemsUpdateService.cs` | *modificar* — guard no PATCH de linha |
| `SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoicesUpdateService.cs` | *modificar* — guard na troca de emitente + `SyncItems` |
| `SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoicesGetService.cs` | *modificar* — `Include` da navegação |
| `SiagroB1.Application.Tests/PurchaseInvoices/PurchaseInvoiceContractLinkTests.cs` | *criar* — guard + ausência de efeito em saldo |

Nenhum arquivo novo em `SiagroB1.Web/`: o value help binda o entity set `PurchaseContracts`, que já
está exposto no EDM. Não há DTO, serviço de consulta, rota nem registro de DI a criar.

**Frontend (`siagro-b1-frontend/`)**

| Arquivo | Responsabilidade |
|---|---|
| `webapp/dialogs/fragments/PurchaseInvoiceContractsSelectDialog.fragment.xml` | *criar* — diálogo do value help |
| `webapp/controller/purchaseInvoices/BaseController.ts` | *modificar* — `openContractValueHelp` |
| `webapp/view/purchaseInvoices/fragments/Items.fragment.xml` | *modificar* — coluna "Contrato" |
| `webapp/controller/purchaseInvoices/Add.controller.ts` | *modificar* — payload do `create()` + aviso ao salvar |
| `webapp/controller/purchaseInvoices/Edit.controller.ts` | *modificar* — `$expand`, payload, aviso |
| `webapp/controller/purchaseInvoices/Detail.controller.ts` | *modificar* — `$expand` |

---

## Task 1: Modelo, migration e leitura

**Files:**
- Modify: `SiagroB1.Domain/Entities/PurchaseInvoiceItem.cs`
- Modify: `SiagroB1.Infra/Context/AppDbContext.cs` (bloco de `PurchaseInvoiceItem`, ~linhas 143-155)
- Modify: `SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoicesGetService.cs`
- Create: `SiagroB1.Migrations/AppContext/<timestamp>_AddPurchaseInvoiceItemContract.cs` (scaffold)
- Test: `SiagroB1.Application.Tests/PurchaseInvoices/PurchaseInvoiceContractLinkTests.cs`

**Interfaces:**
- Produces: `PurchaseInvoiceItem.PurchaseContractKey` (`Guid?`) e `PurchaseInvoiceItem.PurchaseContract` (`PurchaseContract?`) — usados por todas as tasks seguintes.

- [ ] **Step 1: Escrever o teste que falha**

Criar `SiagroB1.Application.Tests/PurchaseInvoices/PurchaseInvoiceContractLinkTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.PurchaseInvoices;

/// <summary>
/// Amarração da linha do documento de entrada ao contrato de compra.
///
/// É REFERÊNCIA, não efeito: nenhuma allocation é criada e nenhum saldo de contrato muda. O saldo
/// físico continua sendo movido só pelo romaneio.
/// </summary>
public class PurchaseInvoiceContractLinkTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private async Task<PurchaseContract> SeedContractAsync(
        string cardCode = "F0001",
        string itemCode = "SOJA",
        ContractStatus status = ContractStatus.Approved)
    {
        var contract = new PurchaseContract
        {
            Key = Guid.NewGuid(),
            Code = "CC-001",
            CardCode = cardCode,
            ItemCode = itemCode,
            UnitOfMeasureCode = "KG",
            HarvestSeasonCode = "2026",
            DeliveryLocationCode = "A1",
            Status = status,
            TotalVolume = 1000m,
        };

        _db.Context.PurchaseContracts.Add(contract);
        await _db.Context.SaveChangesAsync();
        _db.Context.ChangeTracker.Clear();

        return contract;
    }

    [Fact]
    public async Task Line_persists_and_reloads_the_contract_key()
    {
        var contract = await SeedContractAsync();

        var invoice = new PurchaseInvoice { Key = Guid.NewGuid(), CardCode = "F0001" };
        invoice.AddItem(new PurchaseInvoiceItem
        {
            Key = Guid.NewGuid(),
            ItemCode = "SOJA",
            Quantity = 10m,
            UnitPrice = 1m,
            PurchaseContractKey = contract.Key,
        });

        _db.Context.PurchaseInvoices.Add(invoice);
        await _db.Context.SaveChangesAsync();
        _db.Context.ChangeTracker.Clear();

        var reloaded = await _db.Context.PurchaseInvoicesItems
            .AsNoTracking().FirstAsync(x => x.PurchaseInvoiceKey == invoice.Key);

        Assert.Equal(contract.Key, reloaded.PurchaseContractKey);
    }

    [Fact]
    public async Task Line_without_a_contract_is_valid()
    {
        // NF de insumo, serviço ou frete não tem contrato — e a linha importada de XML nasce sem
        // vínculo, porque o XML não o carrega.
        var invoice = new PurchaseInvoice { Key = Guid.NewGuid(), CardCode = "F0001" };
        invoice.AddItem(new PurchaseInvoiceItem
        {
            Key = Guid.NewGuid(), ItemCode = "SOJA", Quantity = 10m, UnitPrice = 1m,
        });

        _db.Context.PurchaseInvoices.Add(invoice);
        await _db.Context.SaveChangesAsync();
        _db.Context.ChangeTracker.Clear();

        var reloaded = await _db.Context.PurchaseInvoicesItems
            .AsNoTracking().FirstAsync(x => x.PurchaseInvoiceKey == invoice.Key);

        Assert.Null(reloaded.PurchaseContractKey);
    }
}
```

- [ ] **Step 2: Rodar o teste para confirmar que falha**

```bash
cd siagro-b1-backend
dotnet test SiagroB1.Application.Tests/SiagroB1.Application.Tests.csproj \
  --filter "FullyQualifiedName~PurchaseInvoiceContractLinkTests"
```

Esperado: FALHA de compilação — `'PurchaseInvoiceItem' does not contain a definition for 'PurchaseContractKey'`.

- [ ] **Step 3: Adicionar a FK e a navegação na entidade**

Em `SiagroB1.Domain/Entities/PurchaseInvoiceItem.cs`, logo depois do bloco de `SalesInvoiceItemKey`:

```csharp
    /// <summary>
    /// Contrato de compra que esta linha referencia — a amarração que fecha a conciliação fiscal
    /// da compra, espelhando <c>SalesInvoiceItem.SalesContractKey</c> do lado da venda.
    ///
    /// É REFERÊNCIA e não efeito: não cria allocation e não move saldo. O saldo físico continua
    /// sendo movido só pelo romaneio.
    ///
    /// Nullable por três razões independentes: NF de insumo, serviço ou frete não tem contrato; a
    /// linha importada de XML nasce sem vínculo, porque o XML não o carrega; e amarrar depois de
    /// gravar é fluxo legítimo.
    /// </summary>
    public Guid? PurchaseContractKey { get; set; }
    public virtual PurchaseContract? PurchaseContract { get; set; }
```

- [ ] **Step 4: Configurar a FK no AppDbContext**

Em `SiagroB1.Infra/Context/AppDbContext.cs`, logo depois do bloco de `SalesInvoiceItem` do `PurchaseInvoiceItem`:

```csharp
        // FK OPCIONAL, como a de origem acima: entrada de insumo/serviço não tem contrato, e uma FK
        // obrigatória viraria INNER JOIN zerando a coleção inteira de itens.
        // Restrict porque apagar um contrato não pode apagar a linha fiscal que o referencia.
        modelBuilder.Entity<PurchaseInvoiceItem>()
            .HasOne(x => x.PurchaseContract)
            .WithMany()
            .HasForeignKey(x => x.PurchaseContractKey)
            .OnDelete(DeleteBehavior.Restrict);
```

- [ ] **Step 5: Rodar os testes para confirmar que passam**

```bash
dotnet test SiagroB1.Application.Tests/SiagroB1.Application.Tests.csproj \
  --filter "FullyQualifiedName~PurchaseInvoiceContractLinkTests"
```

Esperado: 2 testes PASSAM.

- [ ] **Step 6: Incluir a navegação na leitura**

Em `SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoicesGetService.cs`, adicionar o `Include` nos **dois** métodos (`QueryAll()` e `GetByIdAsync`). O `ThenInclude` do `SalesInvoiceItem` já existe; o novo `Include` parte de `Items` de novo:

```csharp
            .Include(x => x.Items)
                .ThenInclude(i => i.PurchaseContract)
```

Isto não é opcional para a tela: sem o `Include` no servidor, o `$expand` do cliente não adianta — o OData serializa o grafo que o EF materializou, então a navegação volta null e a coluna fica vazia mesmo com a chave gravada. É a mesma armadilha que o comentário do `SalesInvoice` naquele arquivo já documenta.

- [ ] **Step 7: Gerar a migration**

```bash
cd siagro-b1-backend
dotnet ef migrations add AddPurchaseInvoiceItemContract \
  --project SiagroB1.Migrations --startup-project SiagroB1.Web --context AppDbContext
```

⚠️ Se precisar desfazer, **não** use `dotnet ef migrations remove --no-build` — ele remove a migration ERRADA. Use `dotnet ef migrations remove` sem a flag.

Conferir o arquivo gerado: deve conter só `AddColumn<Guid>` nullable em `PURCHASE_INVOICES_ITEMS`, o índice, e a FK para `PURCHASE_CONTRACTS` com `ReferentialAction.Restrict`. Se vier qualquer outra alteração, é drift de snapshot — pare e investigue antes de aplicar.

- [ ] **Step 8: Aplicar a migration no banco local**

```bash
ASPNETCORE_ENVIRONMENT=Yokotobi dotnet ef database update \
  --project SiagroB1.Migrations --startup-project SiagroB1.Web --context AppDbContext
```

⚠️ **Passe o `ASPNETCORE_ENVIRONMENT` explicitamente.** O alvo do fallback muda conforme a configuração; leia a connection string que a ferramenta reporta e confirme que é `localhost`/dev antes de deixar rodar.

- [ ] **Step 9: Stage dos arquivos novos**

```bash
git -C siagro-b1-backend add \
  SiagroB1.Application.Tests/PurchaseInvoices/PurchaseInvoiceContractLinkTests.cs \
  SiagroB1.Migrations/AppContext/
```

Não commitar.

---

## Task 2: Guard compartilhado nos quatro caminhos de gravação

**Files:**
- Modify: `SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoiceLineGuard.cs`
- Modify: `SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoicesCreateService.cs`
- Modify: `SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoicesItemsCreateService.cs`
- Modify: `SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoicesItemsUpdateService.cs`
- Modify: `SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoicesUpdateService.cs`
- Test: `SiagroB1.Application.Tests/PurchaseInvoices/PurchaseInvoiceContractLinkTests.cs`

**Interfaces:**
- Consumes: `PurchaseInvoiceItem.PurchaseContractKey` (Task 1).
- Produces: `PurchaseInvoiceLineGuard.EnsureContractIsCompatibleAsync(IUnitOfWork db, Guid? purchaseContractKey, string? itemCode, string cardCode)` → `Task`. Lança `DefaultException` (pt-BR) quando incompatível; retorna silenciosamente quando `purchaseContractKey` é null.

- [ ] **Step 1: Escrever os testes que falham**

Acrescentar a `PurchaseInvoiceContractLinkTests.cs` (dentro da mesma classe):

```csharp
    private static PurchaseInvoice NewInvoice(Guid? contractKey, string cardCode = "F0001") 
    {
        var invoice = new PurchaseInvoice { Key = Guid.NewGuid(), CardCode = cardCode };
        invoice.AddItem(new PurchaseInvoiceItem
        {
            ItemCode = "SOJA",
            ItemName = "SOJA",
            Quantity = 10m,
            UnitPrice = 1m,
            PurchaseContractKey = contractKey,
        });
        return invoice;
    }

    private PurchaseInvoicesCreateService CreateService() =>
        new(_db,
            new FakeBusinessPartnerService(
                names: new Dictionary<string, string> { ["F0001"] = "PRODUTOR TESTE" }),
            new FakeItemService(
                names: new Dictionary<string, string> { ["SOJA"] = "SOJA EM GRAOS" }));

    [Fact]
    public async Task Contract_of_another_supplier_is_refused()
    {
        var contract = await SeedContractAsync(cardCode: "F0002");

        await Assert.ThrowsAsync<DefaultException>(
            () => CreateService().ExecuteAsync(NewInvoice(contract.Key), "tester"));
    }

    [Fact]
    public async Task Contract_of_another_product_is_refused()
    {
        var contract = await SeedContractAsync(itemCode: "MILHO");

        await Assert.ThrowsAsync<DefaultException>(
            () => CreateService().ExecuteAsync(NewInvoice(contract.Key), "tester"));
    }

    [Fact]
    public async Task Contract_in_draft_is_refused()
    {
        // Só Approved e Finished podem lastrear uma NF.
        var contract = await SeedContractAsync(status: ContractStatus.Draft);

        await Assert.ThrowsAsync<DefaultException>(
            () => CreateService().ExecuteAsync(NewInvoice(contract.Key), "tester"));
    }

    [Fact]
    public async Task Finished_contract_is_accepted()
    {
        // A NF chega com frequência DEPOIS do contrato encerrado — é o caso que a conciliação
        // precisa cobrir.
        var contract = await SeedContractAsync(status: ContractStatus.Finished);

        await CreateService().ExecuteAsync(NewInvoice(contract.Key), "tester");

        var line = await _db.Context.PurchaseInvoicesItems.AsNoTracking().FirstAsync();
        Assert.Equal(contract.Key, line.PurchaseContractKey);
    }

    [Fact]
    public async Task Unknown_contract_is_refused()
    {
        await Assert.ThrowsAsync<DefaultException>(
            () => CreateService().ExecuteAsync(NewInvoice(Guid.NewGuid()), "tester"));
    }

    [Fact]
    public async Task Binding_a_contract_does_not_change_its_balance()
    {
        // O ponto da feature: é REFERÊNCIA, não efeito.
        var contract = await SeedContractAsync();
        var volumeBefore = contract.TotalVolume;

        await CreateService().ExecuteAsync(NewInvoice(contract.Key), "tester");

        var reloaded = await _db.Context.PurchaseContracts
            .AsNoTracking().FirstAsync(x => x.Key == contract.Key);

        Assert.Equal(volumeBefore, reloaded.TotalVolume);
        Assert.Empty(await _db.Context.PurchaseContractsAllocations.ToListAsync());
    }
```

Acrescentar os `using` que faltarem no topo: `SiagroB1.Domain.Exceptions;`.

- [ ] **Step 2: Rodar os testes para confirmar que falham**

```bash
dotnet test SiagroB1.Application.Tests/SiagroB1.Application.Tests.csproj \
  --filter "FullyQualifiedName~PurchaseInvoiceContractLinkTests"
```

Esperado: os 5 testes de recusa FALHAM (nenhuma exceção é lançada); `Finished_contract_is_accepted` e `Binding_a_contract_does_not_change_its_balance` já passam.

- [ ] **Step 3: Escrever a regra no guard compartilhado**

Em `SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoiceLineGuard.cs`, acrescentar:

```csharp
    /// <summary>
    /// Valida a amarração da linha com o contrato de compra.
    ///
    /// Mora aqui, e não em um serviço, porque são QUATRO os caminhos que gravam linha — deep-insert
    /// do documento, POST de linha, PATCH de linha e troca de emitente no cabeçalho — e a grade da
    /// tela alcança cada um por uma ação diferente. Deixar a regra em um só deles é o erro
    /// previsível: já aconteceu com a re-resolução da descrição do produto, que ficou só no
    /// SyncItems e não valia para a troca de produto pela grade do Edit.
    ///
    /// Linha SEM contrato passa: o campo é opcional por decisão de projeto.
    /// </summary>
    public static async Task EnsureContractIsCompatibleAsync(
        IUnitOfWork db, Guid? purchaseContractKey, string? itemCode, string cardCode)
    {
        if (purchaseContractKey is null)
            return;

        var contract = await db.Context.PurchaseContracts
            .AsNoTracking()
            .Where(x => x.Key == purchaseContractKey)
            .Select(x => new { x.Code, x.CardCode, x.ItemCode, x.Status })
            .FirstOrDefaultAsync();

        if (contract is null)
            throw new DefaultException("Contrato de compra informado na linha não foi encontrado.");

        // Encerrado ENTRA de propósito: a NF chega com frequência depois de o contrato fechar, e
        // recusá-la deixaria essa nota sem como ser conciliada.
        if (contract.Status != ContractStatus.Approved && contract.Status != ContractStatus.Finished)
            throw new DefaultException(
                $"O contrato {contract.Code} não está aprovado nem encerrado e não pode " +
                "ser amarrado a um documento de entrada.");

        if (contract.CardCode != cardCode)
            throw new DefaultException(
                $"O contrato {contract.Code} é de outro fornecedor e não pode ser amarrado " +
                "a este documento.");

        if (contract.ItemCode != itemCode)
            throw new DefaultException(
                $"O contrato {contract.Code} é de outro produto e não pode ser amarrado " +
                "a esta linha.");
    }
```

- [ ] **Step 4: Chamar o guard no deep-insert**

Em `PurchaseInvoicesCreateService.ExecuteAsync`, dentro do `foreach` que já resolve `ItemName`:

```csharp
        foreach (var item in invoice.Items)
        {
            item.ItemName = await PurchaseInvoiceLineGuard.ResolveItemNameAsync(
                itemService, item.ItemCode, item.ItemName);

            await PurchaseInvoiceLineGuard.EnsureContractIsCompatibleAsync(
                db, item.PurchaseContractKey, item.ItemCode, invoice.CardCode);
        }
```

- [ ] **Step 5: Chamar o guard no POST de linha**

Em `PurchaseInvoicesItemsCreateService.ExecuteAsync`, depois do `EnsureParentIsPendingAsync` e antes do `AddAsync`. O `CardCode` vem do documento pai, que o PATCH parcial não traz:

```csharp
        var cardCode = await db.Context.PurchaseInvoices
            .Where(x => x.Key == item.PurchaseInvoiceKey)
            .Select(x => x.CardCode)
            .FirstAsync();

        await PurchaseInvoiceLineGuard.EnsureContractIsCompatibleAsync(
            db, item.PurchaseContractKey, item.ItemCode, cardCode);
```

- [ ] **Step 6: Chamar o guard no PATCH de linha**

Em `PurchaseInvoicesItemsUpdateService.ExecuteAsync`, depois do `EnsureParentIsPendingAsync` e antes das atribuições. Note que o `ItemCode` usado é o **entrante** (`entity.ItemCode`), porque trocar produto e contrato na mesma gravação precisa validar o par final:

```csharp
        var cardCode = await db.Context.PurchaseInvoices
            .Where(x => x.Key == existing.PurchaseInvoiceKey)
            .Select(x => x.CardCode)
            .FirstAsync();

        await PurchaseInvoiceLineGuard.EnsureContractIsCompatibleAsync(
            db, entity.PurchaseContractKey, entity.ItemCode, cardCode);
```

E acrescentar a cópia do campo junto das outras atribuições:

```csharp
        existing.PurchaseContractKey = entity.PurchaseContractKey;
```

- [ ] **Step 7: Chamar o guard na troca de emitente e no SyncItems**

Em `PurchaseInvoicesUpdateService`, `SyncItems` vira o lugar onde a linha nova/atualizada é validada, e o cabeçalho revalida as linhas **já gravadas** — que é o único ponto onde dá para perceber que a troca de emitente as deixou órfãs.

No `SyncItemsAsync`, no ramo da linha NOVA, acrescentar antes do `Add`:

```csharp
                await PurchaseInvoiceLineGuard.EnsureContractIsCompatibleAsync(
                    db, line.PurchaseContractKey, line.ItemCode, existing.CardCode);
```

e no objeto criado, a propriedade:

```csharp
                    PurchaseContractKey = line.PurchaseContractKey,
```

No ramo da linha EXISTENTE, antes das atribuições:

```csharp
            await PurchaseInvoiceLineGuard.EnsureContractIsCompatibleAsync(
                db, line.PurchaseContractKey, line.ItemCode, existing.CardCode);
```

e junto das atribuições:

```csharp
            current.PurchaseContractKey = line.PurchaseContractKey;
```

Em `ExecuteAsync`, **depois** de `existing.CardCode = entity.CardCode;` e **antes** de `SyncItemsAsync`, revalidar as linhas já gravadas quando o emitente mudou:

```csharp
        // Trocar o emitente com linha já amarrada deixaria o contrato de um fornecedor pendurado no
        // documento de outro. Barra no salvar em vez de limpar em silêncio: apagar o vínculo sem
        // avisar destrói trabalho do operador sem deixar rastro. Só as linhas JÁ gravadas precisam
        // disso — as entrantes passam pelo guard dentro do SyncItems.
        if (issuerChanged)
        {
            foreach (var line in existing.Items.Where(l => l.PurchaseContractKey is not null))
                await PurchaseInvoiceLineGuard.EnsureContractIsCompatibleAsync(
                    db, line.PurchaseContractKey, line.ItemCode, entity.CardCode);
        }
```

- [ ] **Step 8: Rodar toda a suíte**

```bash
dotnet test SiagroB1.Application.Tests/SiagroB1.Application.Tests.csproj
```

Esperado: tudo PASSA, incluindo os 953 testes que já existiam.

- [ ] **Step 9: Acrescentar o teste da troca de emitente**

Em `PurchaseInvoiceContractLinkTests.cs`:

```csharp
    [Fact]
    public async Task Changing_the_issuer_with_a_bound_contract_is_refused()
    {
        var contract = await SeedContractAsync();

        var invoice = NewInvoice(contract.Key);
        await CreateService().ExecuteAsync(invoice, "tester");
        _db.Context.ChangeTracker.Clear();

        var incoming = new PurchaseInvoice { CardCode = "F0002" };
        incoming.AddItem(new PurchaseInvoiceItem
        {
            Key = invoice.Items.First().Key,
            ItemCode = "SOJA",
            Quantity = 10m,
            UnitPrice = 1m,
            PurchaseContractKey = contract.Key,
        });

        var updateService = new PurchaseInvoicesUpdateService(
            _db,
            new FakeBusinessPartnerService(names: new Dictionary<string, string>
            {
                ["F0001"] = "PRODUTOR TESTE",
                ["F0002"] = "OUTRO PARCEIRO",
            }),
            new FakeItemService(
                names: new Dictionary<string, string> { ["SOJA"] = "SOJA EM GRAOS" }));

        await Assert.ThrowsAsync<DefaultException>(
            () => updateService.ExecuteAsync(invoice.Key, incoming, "tester"));
    }
```

- [ ] **Step 10: Rodar e confirmar**

```bash
dotnet test SiagroB1.Application.Tests/SiagroB1.Application.Tests.csproj \
  --filter "FullyQualifiedName~PurchaseInvoiceContractLinkTests"
```

Esperado: todos PASSAM.

- [ ] **Step 11: Stage**

Nada de arquivo novo nesta task além do que a Task 1 já staged. Confirme com `git -C siagro-b1-backend status --short` que não há linha `??`.

---


## Task 3: Value help no frontend — fragmento e handler

**Files:**
- Create: `webapp/dialogs/fragments/PurchaseInvoiceContractsSelectDialog.fragment.xml`
- Modify: `webapp/controller/purchaseInvoices/BaseController.ts`

**Interfaces:**
- Consumes: o entity set `PurchaseContracts`, já exposto no EDM, com as propriedades `Key`, `Code`, `Complement`, `TotalVolume`, `AvaiableVolume`, `UnitOfMeasureCode`, `DeliveryStartDate`, `DeliveryEndDate`, `CardCode`, `ItemCode`, `Status`.
- Produces: handler `openContractValueHelp(ev: Input$ValueHelpRequestEvent)` no `purchaseInvoices/BaseController`, herdado por Add, Edit e Detail. A Task 4 o referencia por `.openContractValueHelp` no XML (a coluna).

**Por que não há backend nesta task:** o padrão da casa para value help é bindar o **entity set** com as condições fixas num `$filter` estático e as variáveis como `Filter` de runtime. O diálogo da NF de origem faz exatamente isso — binda `/SalesInvoicesItems`, não a função `PurchaseInvoicesOriginItems`, que existe no backend e ficou órfã. Aqui vale o mesmo, e nenhum endpoint precisa ser criado.

- [ ] **Step 1: Criar o fragmento do diálogo**

Criar `webapp/dialogs/fragments/PurchaseInvoiceContractsSelectDialog.fragment.xml`:

```xml
<core:FragmentDefinition
    xmlns="sap.m"
    xmlns:core="sap.ui.core"
>

<!--
  Binda o ENTITY SET, não uma função: é o padrão da casa para value help, o mesmo que o diálogo da
  NF de origem usa. As condições FIXAS ficam neste $filter estático; fornecedor e produto entram
  como Filter de runtime pelo controller, porque mudam a cada linha.

  ENCERRADO entra de propósito: a NF chega com frequência depois de o contrato fechar, e recusá-la
  deixaria essa nota sem como ser conciliada.

  O $filter de status é STRING ESTÁTICA e não sap.ui.model.Filter: filtro de enum montado como
  objeto estoura "Unsupported type" no UI5.
-->
<TableSelectDialog
  class=" sapUiSizeCompact"
  growing="true"
  growingThreshold="20"
  items="{
    path: '/PurchaseContracts',
    parameters: {
      $filter: 'Status eq \'Approved\' or Status eq \'Finished\''
    },
    sorter: {
      path: 'Code',
      descending: false
    }
  }"
  rememberSelections="true"
  title="Contratos de Compra"
>
<columns>
  <Column>
    <header>
      <Text text="Contrato" />
    </header>
  </Column>
  <Column>
    <header>
      <Text text="Entrega" />
    </header>
  </Column>
  <Column hAlign="End">
    <header>
      <Text text="Volume Total" />
    </header>
  </Column>
  <Column hAlign="End">
    <header>
      <Text text="Saldo" />
    </header>
  </Column>
</columns>
<ColumnListItem>
  <cells>
    <ObjectIdentifier title="{Code}" text="{Complement}" />
    <Text text="{
      parts: [
        { path: 'DeliveryStartDate', type: 'sap.ui.model.odata.type.DateTimeOffset' },
        { path: 'DeliveryEndDate', type: 'sap.ui.model.odata.type.DateTimeOffset' }
      ],
      formatter: '.formatDeliveryWindow'
    }" />
    <ObjectNumber
      number="{
        path: 'TotalVolume',
        type: 'sap.ui.model.type.Float',
        formatOptions: {
          decimals: 3,
          decimalSeparator: ',',
          groupingEnabled: true,
          groupingSeparator: '.'
        }
      }"
      unit="{UnitOfMeasureCode}"
      state="None"
    />
    <ObjectNumber
      number="{
        path: 'AvaiableVolume',
        type: 'sap.ui.model.type.Float',
        formatOptions: {
          decimals: 3,
          decimalSeparator: ',',
          groupingEnabled: true,
          groupingSeparator: '.'
        }
      }"
      unit="{UnitOfMeasureCode}"
      state="None"
    />
  </cells>
</ColumnListItem>
</TableSelectDialog>
</core:FragmentDefinition>
```

Se o `formatter` de janela de entrega der trabalho, simplifique para duas colunas separadas com `<Text text="{path: 'DeliveryStartDate', type: 'sap.ui.model.odata.type.DateTimeOffset', formatOptions: {style: 'short'}}" />` — o valor da coluna é informativo, não é requisito.

- [ ] **Step 2: Stage do arquivo novo (imediatamente)**

```bash
git -C siagro-b1-frontend add webapp/dialogs/fragments/PurchaseInvoiceContractsSelectDialog.fragment.xml
```

- [ ] **Step 3: Escrever o handler**

Em `webapp/controller/purchaseInvoices/BaseController.ts`, ao lado de `openOriginItemValueHelp`:

```ts
  /**
   * Value help do CONTRATO DE COMPRA da linha — só faz sentido no documento tipo Normal.
   *
   * Não usa o `applyValueHelp` genérico nem `descriptionProperty`: aquele mecanismo copia uma
   * DESCRIÇÃO, e aqui o que precisa ser gravado é a CHAVE do contrato. Segue o mesmo desenho do
   * value help da NF de origem — `setValue` no que a tela mostra, `setProperty` no que o banco
   * guarda.
   *
   * Fornecedor e produto entram como filtro porque o contrato é por par (fornecedor, produto):
   * sem eles o diálogo ofereceria contrato de outro produto, que o servidor recusa na gravação.
   */
  async openContractValueHelp(ev: Input$ValueHelpRequestEvent) {
    const oInput = ev.getSource();
    const oTarget = oInput.getBindingContext() as Context;
    const oInvoice = this.getView().getBindingContext() as Context;

    const cardCode = oInvoice?.getProperty("CardCode") as string;
    const itemCode = oTarget?.getProperty("ItemCode") as string;

    if (!cardCode) {
      MessageBox.warning("Informe o emitente antes de amarrar o contrato.");
      return;
    }

    if (!itemCode) {
      MessageBox.warning("Informe o produto da linha antes de amarrar o contrato.");
      return;
    }

    const oSelected = await DialogHelper.openTableSelectDialog(
      this,
      "PurchaseInvoiceContractsSelectDialog",
      ["Code", "Complement"],
      [
        new Filter("CardCode", FilterOperator.EQ, cardCode),
        new Filter("ItemCode", FilterOperator.EQ, itemCode),
      ]);

    // Cancelar resolve undefined: não mexer no que já estava amarrado.
    if (!oSelected) {
      return;
    }

    oInput.setValue(oSelected.getProperty("Code") as string);
    await oTarget.setProperty("PurchaseContractKey", oSelected.getProperty("Key"));
  }
```

`CardCode` e `ItemCode` vão como `Filter` normalmente porque são string. O status **não** vai por aqui — ele já está no `$filter` estático do fragmento, pelo motivo documentado lá.

- [ ] **Step 4: Rodar os gates do frontend**

```bash
cd siagro-b1-frontend
yarn ts-typecheck
yarn lint
```

Esperado: ambos limpos.

---

## Task 4: Coluna na grade, expand e aviso ao salvar

**Files:**
- Modify: `webapp/view/purchaseInvoices/fragments/Items.fragment.xml`
- Modify: `webapp/controller/purchaseInvoices/Add.controller.ts`
- Modify: `webapp/controller/purchaseInvoices/Edit.controller.ts`
- Modify: `webapp/controller/purchaseInvoices/Detail.controller.ts`

**Interfaces:**
- Consumes: `.openContractValueHelp` (Task 3), `PurchaseContractKey` e a navegação `PurchaseContract` (Task 1).

- [ ] **Step 1: Acrescentar a coluna na grade**

Em `webapp/view/purchaseInvoices/fragments/Items.fragment.xml`, **depois** da coluna "Total" e **antes** do bloco de colunas da devolução:

```xml
      <!-- A amarração fiscal, só relevante no tipo Normal: o campo mostra o CÓDIGO do contrato e o
           value help grava a CHAVE. OneWay é obrigatório porque o campo exibe uma NAVEGAÇÃO — em
           linha ainda não amarrada `PurchaseContract` é null, e no modo TwoWay o setValue tentaria
           escrever dentro do null. Mesmo desenho da coluna "NF de Origem" abaixo. -->
      <t:Column label="Contrato" width="12rem"
                visible="{= ${path: 'InvoiceType', targetType: 'any'} === 'Normal' }">
        <t:template>
          <Input
            editable="{ui>/editable}"
            value="{path: 'PurchaseContract/Code', mode: 'OneWay'}"
            showValueHelp="{ui>/editable}"
            valueHelpOnly="true"
            valueHelpRequest=".openContractValueHelp" />
        </t:template>
      </t:Column>
```

`InvoiceType` é do CABEÇALHO e a linha o alcança pelo contexto do documento, igual às colunas de devolução que já usam esse mesmo `visible`.

- [ ] **Step 2: Incluir a propriedade no payload inicial do Add**

Em `webapp/controller/purchaseInvoices/Add.controller.ts`, acrescentar ao `interface InvoiceItemPayload`:

```ts
  /** Nulo até o operador amarrar. (strictNullChecks off: `string` já admite null aqui.) */
  PurchaseContractKey: string;
```

e no `.map` de `createDraft` e no `oBinding.create` de `onAddItem`:

```ts
      PurchaseContractKey: null,
```

Toda propriedade que a tela edita precisa existir no `create()` inicial, nem que seja como null — sem isso a primeira escolha abre *"Must not change a property before it has been read"*.

- [ ] **Step 3: Mesma coisa no Edit**

Em `webapp/controller/purchaseInvoices/Edit.controller.ts`, no `oBinding.create` de `onAddItem`, acrescentar `PurchaseContractKey: null,`.

E ampliar o `$expand` do `bindElement`:

```ts
    this.bindElement(`/PurchaseInvoices(${id})`, {
      $expand:
        "Items($select=Key,ItemCode,ItemName,UnitOfMeasureCode,Quantity,UnitPrice,Total," +
        "AssessedShortage,Difference,SalesInvoiceItemKey,PurchaseContractKey" +
        ";$expand=SalesInvoiceItem($expand=SalesInvoice),PurchaseContract)",
    });
```

- [ ] **Step 4: Mesmo `$expand` no Detail**

Em `webapp/controller/purchaseInvoices/Detail.controller.ts`, aplicar exatamente a mesma string de `$expand` do passo anterior.

Sem isto, o UI5 buscaria `Items({key})/PurchaseContract` sozinho — rota que o backend não expõe — e daria 404 com a coluna em branco.

- [ ] **Step 5: Aviso ao salvar no Add**

Em `Add.controller.ts`, `onSave`, ao lado do aviso de devolução que já existe, acrescentar o do contrato para o tipo Normal:

```ts
    if (oContext.getProperty("InvoiceType") === "Normal") {
      const unlinked = (oBinding?.getAllCurrentContexts() ?? [])
        .filter(ctx => !ctx.getProperty("PurchaseContractKey"));

      if (unlinked.length > 0 && !await confirmDialog(
        `${unlinked.length} item(ns) sem contrato amarrado. Salvar assim mesmo ?`,
        "Documento sem contrato")) {
        return;
      }
    }
```

- [ ] **Step 6: Aviso ao salvar no Edit**

`Edit.controller.ts` não tem o aviso equivalente hoje. Acrescentar em `onSave`, antes do `submitBatch`, o mesmo bloco do passo anterior — obtendo o binding da tabela:

```ts
    const oTable = this.byId("tablePurchaseInvoiceItems");
    const oBinding = oTable?.getBinding("rows") as ODataListBinding;

    if (oContext.getProperty("InvoiceType") === "Normal") {
      const unlinked = (oBinding?.getAllCurrentContexts() ?? [])
        .filter(ctx => !ctx.getProperty("PurchaseContractKey"));

      if (unlinked.length > 0 && !await confirmDialog(
        `${unlinked.length} item(ns) sem contrato amarrado. Salvar assim mesmo ?`,
        "Documento sem contrato")) {
        return;
      }
    }
```

Acrescentar os imports que faltarem: `import Table from "sap/ui/table/Table";` já existe; `import { confirmDialog } from "siagrob1/helpers/DialogHelpers";` precisa ser adicionado.

- [ ] **Step 7: Rodar os gates**

```bash
cd siagro-b1-frontend
yarn ts-typecheck
yarn lint
```

Esperado: ambos limpos.

---

## Task 5: Verificação no navegador

**Files:** nenhum — é conferência.

Esta task **não é opcional**. Neste projeto, build verde + testes verdes + lint limpo já conviveram com Detail nascendo em branco, coluna vazia por `Include` faltando e descrição gravando vazia. Todos os bugs recentes desta feature só apareceram no navegador, e um deles só conferindo o **banco** depois de salvar.

- [ ] **Step 1: Subir a stack**

```bash
# backend (parar antes qualquer instância antiga, senão o build falha com lock)
dotnet run --project SiagroB1.Web --launch-profile yktb
dotnet run --project SiagroB1.Gateway --launch-profile yktb
# frontend
cd siagro-b1-frontend && yarn start:dev
```

Login `admin` / `1234`, filial Yokotobi - Pilar.

- [ ] **Step 2: Add — amarrar contrato**

Ir pela lista → **Incluir** (a rota por hash direto não monta o rascunho). Escolher emitente, escolher produto na linha, então abrir o value help de Contrato.

Conferir: o diálogo abre; oferece só contratos daquele fornecedor e daquele produto; o campo passa a mostrar o código do contrato.

- [ ] **Step 3: Add — conferir a chave no BANCO depois de salvar**

Salvar e consultar:

```bash
sqlcmd -S localhost -d IDX_SIAGRO_DEV -E -W -s"|" -Q \
  "SET NOCOUNT ON; SELECT TOP 3 i.ItemCode, i.ItemName, i.PurchaseContractKey \
   FROM PURCHASE_INVOICES_ITEMS i \
   JOIN PURCHASE_INVOICES p ON p.[Key]=i.PurchaseInvoiceKey ORDER BY p.CreatedAt DESC"
```

Esperado: `PurchaseContractKey` preenchida. **Vazia significa que o value help não gravou** — a mesma classe de bug da descrição, e a razão de este passo existir.

- [ ] **Step 4: Edit — a coluna carrega e a troca grava**

Abrir o documento em Editar. Conferir que a coluna Contrato **já vem preenchida** (se vier vazia, é `Include`/`$expand` faltando). Trocar por outro contrato, salvar, e reconsultar o banco.

- [ ] **Step 5: Detail — sem botão de value help**

Abrir em Visualizar. O fragmento é o mesmo: o botão do diálogo **não** pode aparecer, e a coluna deve mostrar o código do contrato.

- [ ] **Step 6: Tipo Devolução — coluna escondida**

Criar (ou abrir) um documento tipo Devolução e conferir que a coluna Contrato **não** aparece, e que as colunas NF de Origem / Quebra Apurada / Diferença continuam funcionando.

- [ ] **Step 7: Guard — troca de emitente**

Com um documento que tem contrato amarrado, trocar o emitente e salvar. Esperado: erro de negócio em pt-BR nomeando a linha, e o documento **não** gravado.

- [ ] **Step 8: Ausência de efeito — o saldo do contrato não muda**

Antes e depois de amarrar, conferir:

```bash
sqlcmd -S localhost -d IDX_SIAGRO_DEV -E -W -s"|" -Q \
  "SET NOCOUNT ON; SELECT Code, TotalVolume, AvaiableVolume FROM PURCHASE_CONTRACTS WHERE Code='<código>'"
```

Esperado: idênticos. É o invariante central da feature.

- [ ] **Step 9: Conferir que nada ficou por stagear**

```bash
git -C siagro-b1-backend status --short
git -C siagro-b1-frontend status --short
```

Nenhuma linha começando com `??`. **Não commitar.**

---

## Auto-revisão deste plano

**Cobertura do spec:**

| Requisito do spec | Task |
|---|---|
| FK nullable + navegação espelhando a venda | 1 |
| Migration com FK `Restrict` | 1 |
| Sem efeito em saldo/valor/allocation (com teste) | 1, 2 (step 1), 5 (step 8) |
| Recorte Approved + Finished, fornecedor + produto | 3 (`$filter` + filtros de runtime), 2 (guard, autoridade) |
| Não reusar `GetAvaiablesPurchaseContracts`, e por quê | 3 |
| Endpoint novo é desnecessário: bindar o entity set | 3 |
| Guard nos quatro caminhos de gravação | 2 |
| Troca de emitente barra no salvar | 2 (steps 7, 9), 5 (step 7) |
| Regra morando em `PurchaseInvoiceLineGuard` | 2 |
| Coluna visível só em `InvoiceType == Normal` | 4 |
| Grava a chave, não usa `descriptionProperty`, binding OneWay | 3, 4 |
| `showValueHelp="{ui>/editable}"` | 4 |
| `PurchaseContractKey` no `create()` inicial | 4 |
| `$expand` + `Include` da navegação | 1 (step 6), 4 (steps 3, 4) |
| Aviso não-bloqueante ao salvar | 4 (steps 5, 6) |
| Obrigatoriedade fica para a Fase 2 | fora de escopo, registrado |

**Consistência de tipos:** `EnsureContractIsCompatibleAsync(IUnitOfWork, Guid?, string?, string)` é declarada na Task 2 e chamada com essa assinatura nos quatro serviços. As propriedades usadas no fragmento da Task 3 (`Code`, `Complement`, `TotalVolume`, `AvaiableVolume`, `UnitOfMeasureCode`, `DeliveryStartDate`, `DeliveryEndDate`) e nos filtros do handler (`CardCode`, `ItemCode`, `Status`) são todas propriedades reais de `PurchaseContract`, entity set já exposto no EDM. `.openContractValueHelp` é declarado na Task 3 e referenciado com esse nome exato no XML da Task 4.

**Simplificação encontrada durante a escrita deste plano:** a versão inicial criava um endpoint OData novo (DTO + serviço + rota + DI + suíte de testes própria) para alimentar o value help. Ao conferir o diálogo da NF de origem descobriu-se que ele **não** usa a função `PurchaseInvoicesOriginItems` que existe no backend — ele binda o entity set direto, com `$filter` estático. A função ficou órfã. Seguindo o padrão real da casa, o value help desta feature não precisa de backend nenhum, e uma task inteira saiu do plano. A justificativa de **não** reusar `PurchaseContractsGetAvaiablesList` continua valendo: o corte por `AvaiableVolume > 0` dela descartaria justamente os contratos encerrados.

**Sintaxe do filtro de enum, verificada:** `Status eq 'Approved'` é exatamente o que `webapp/controller/purchaseContracts/approval/Main.controller.ts:33-42` já usa contra este mesmo entity set, em produção. Sem namespace qualificado, sem surpresa. O que **não** funciona é montar isso como `sap.ui.model.Filter` — daí o `$filter` ser string estática no fragmento.

**Nenhum risco aberto restante.**
