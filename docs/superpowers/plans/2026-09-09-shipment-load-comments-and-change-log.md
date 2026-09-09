# Comentários e Log de Alterações na Montagem de Carga — Plano de Implementação

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Dar à Carga (`ShipmentLoad`) uma coleção de comentários com autor e data, e um log de alterações campo a campo de tudo que o usuário digita no cadastro.

**Architecture:** Duas tabelas novas (`SHIPMENT_LOADS_COMMENTS`, `SHIPMENT_LOADS_CHANGE_LOGS`) espelhando o que contratos e documentos de saída/entrada já têm. Escrita dos comentários por OData action, nunca por POST na coleção aninhada, para que mutação e log entrem no mesmo `SaveChanges`. O `ShipmentLoadsUpdateService` passa a emitir uma linha de log por campo alterado, mantendo intacta a linha narrativa que já grava em `SHIPMENT_LOAD_MOVEMENTS`.

**Tech Stack:** .NET 10, EF Core (SQL Server), ASP.NET Core OData v4, xUnit + EF InMemory, OpenUI5 + TypeScript.

**Spec:** `docs/superpowers/specs/2026-09-09-shipment-load-comments-and-change-log-design.md`

## Global Constraints

- **Nomenclatura**: identificadores de código, tabelas e colunas em **inglês**; texto que o usuário lê (rótulos, mensagens de negócio, títulos) em **pt-BR**. Comentários de código em pt-BR.
- **NUNCA commitar nem dar push.** Os commits deste projeto são manuais, feitos pelo usuário. A única operação de escrita no git permitida é `git add`, e ela deve ser feita **imediatamente após criar cada arquivo novo**, no sub-repo correto (`siagro-b1-backend/` ou `siagro-b1-frontend/`). Onde este plano diz "Stage", é `git add` — nunca `git commit`.
- **Dois repos, dois stagings.** Backend e frontend são repositórios git independentes. Nunca `git add` de um caminho do frontend estando no backend.
- **Migrations**: sempre com `ASPNETCORE_ENVIRONMENT` explícito. O profile `db-migration` aponta para produção. Ambiente de trabalho local: `Yokotobi-Development` (= profile `yktb` = localhost) (banco `IDX_SIAGRO_DEV`).
- **`yarn test` do frontend não passa neste projeto** (gate de cobertura de 50% contra ~2,4% reais). Os gates do frontend são `yarn lint` e `yarn ts-typecheck`.
- **Nenhum `--` dentro de comentário XML** nos fragmentos: `Fragment.load` devolve nulls em silêncio e nenhum gate acusa.
- A coleção de comentários chama **`CommentEntries`**, nunca `Comments` — `ShipmentLoad.Comments` já é o escalar "Observações" do cabeçalho e continua existindo.

---

### Task 1: Entidades, DbSets e migration

**Files:**
- Create: `SiagroB1.Domain/Entities/ShipmentLoadComment.cs`
- Create: `SiagroB1.Domain/Entities/ShipmentLoadChangeLog.cs`
- Create: `SiagroB1.Domain/Entities/ShipmentLoadChangeLogFields.cs`
- Modify: `SiagroB1.Domain/Entities/ShipmentLoad.cs` (duas nav properties novas, junto das existentes por volta da linha 175)
- Modify: `SiagroB1.Infra/Context/AppDbContext.cs:64-65` (dois `DbSet` novos)
- Create: `SiagroB1.Migrations/AppContext/<timestamp>_AddShipmentLoadCommentsAndChangeLogs.cs` (gerada)
- Test: `SiagroB1.Application.Tests/ShipmentLoads/ShipmentLoadModelTests.cs`

**Interfaces:**
- Consumes: nada.
- Produces: `ShipmentLoadComment` (`Key: Guid?`, `ShipmentLoadKey: Guid?`, `ShipmentLoad: ShipmentLoad?`, `CommentedAt: DateTime`, `CommentedBy: string?`, `CommentText: required string`); `ShipmentLoadChangeLog` (`Key: Guid?`, `ShipmentLoadKey: Guid?`, `ShipmentLoad: ShipmentLoad?`, `ChangedAt: DateTime`, `ChangedBy: string?`, `Field: required string`, `OldValue: string?`, `NewValue: string?`); `ShipmentLoadChangeLogFields` com as constantes `Comment`, `LoadDate`, `TruckCode`, `TruckDriver`, `Carrier`, `CardCode`, `Warehouse`, `Item`, `UnitOfMeasure`, `Branch`, `HasExcess`, `FreightPrice`, `Comments`, `Status`, `CancellationReason` e os helpers estáticos `DescribeDate(DateTime) : string`, `DescribeBoolean(bool) : string`, `DescribeFreightPrice(decimal?) : string?`, `DescribeStatus(ShipmentLoadStatus) : string`; `ShipmentLoad.CommentEntries : ICollection<ShipmentLoadComment>`; `ShipmentLoad.ChangeLogs : ICollection<ShipmentLoadChangeLog>`; `AppDbContext.ShipmentLoadsComments`, `AppDbContext.ShipmentLoadsChangeLogs`.

- [ ] **Step 1: Escrever o teste que falha**

Acrescentar ao fim de `SiagroB1.Application.Tests/ShipmentLoads/ShipmentLoadModelTests.cs`, dentro da classe:

```csharp
    [Fact]
    public void Comments_and_change_logs_are_mapped_to_their_own_tables()
    {
        using var context = TestDb.CreateContext();

        var comments = context.Model.FindEntityType(typeof(ShipmentLoadComment));
        var changeLogs = context.Model.FindEntityType(typeof(ShipmentLoadChangeLog));

        Assert.NotNull(comments);
        Assert.NotNull(changeLogs);
        Assert.Equal("SHIPMENT_LOADS_COMMENTS", comments!.GetTableName());
        Assert.Equal("SHIPMENT_LOADS_CHANGE_LOGS", changeLogs!.GetTableName());
    }

    [Fact]
    public void ShipmentLoad_exposes_both_collections_under_names_that_do_not_clash_with_the_scalar()
    {
        using var context = TestDb.CreateContext();

        var load = context.Model.FindEntityType(typeof(ShipmentLoad))!;
        var navigations = load.GetNavigations().Select(n => n.Name).ToArray();

        // CommentEntries, e nao Comments: o escalar Comments e a "Observacoes" do cabecalho.
        Assert.Contains(nameof(ShipmentLoad.CommentEntries), navigations);
        Assert.Contains(nameof(ShipmentLoad.ChangeLogs), navigations);
        Assert.NotNull(load.FindProperty(nameof(ShipmentLoad.Comments)));
    }
```

Se `TestDb.CreateContext()` não existir com esse nome, usar o helper que os demais testes do arquivo já usam para obter um `AppDbContext` — não inventar um novo.

- [ ] **Step 2: Rodar o teste e confirmar que falha**

```
dotnet test --filter FullyQualifiedName~ShipmentLoadModelTests
```

Esperado: FALHA de compilação — `ShipmentLoadComment` não existe.

- [ ] **Step 3: Criar `ShipmentLoadComment.cs`**

```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Um comentário da carga: anotação livre com data, hora e autor, editável a qualquer tempo —
/// inclusive em carga cancelada, porque comentário não altera peso, saldo nem valor.
///
/// Não confundir com <see cref="ShipmentLoad.Comments"/>, o campo escalar de "Observações" do
/// cabeçalho. É por causa desse escalar que a coleção se chama
/// <see cref="ShipmentLoad.CommentEntries"/>, e não <c>Comments</c>.
///
/// Toda inclusão, edição e exclusão gera linha em <see cref="ShipmentLoadChangeLog"/> com o
/// código <see cref="ShipmentLoadChangeLogFields.Comment"/>.
/// </summary>
[Table("SHIPMENT_LOADS_COMMENTS")]
[Index(nameof(ShipmentLoadKey))]
public class ShipmentLoadComment
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid? Key { get; set; }

    public Guid? ShipmentLoadKey { get; set; }
    public virtual ShipmentLoad? ShipmentLoad { get; set; }

    /// <summary>
    /// Data e hora da última escrita: nasce na inclusão e é SOBRESCRITA a cada edição. O texto
    /// anterior e o momento da versão anterior ficam no log de alterações.
    /// </summary>
    public DateTime CommentedAt { get; set; } = DateTime.Now;

    /// <summary>
    /// Autor da última escrita. Sobrescrito junto com <see cref="CommentedAt"/> quando um
    /// administrador edita o comentário de outra pessoa.
    /// </summary>
    [Column(TypeName = "VARCHAR(100)")]
    public string? CommentedBy { get; set; }

    /// <summary>
    /// 500 caracteres para casar com <see cref="ShipmentLoadChangeLog.NewValue"/>: assim nenhuma
    /// linha do log sai truncada.
    /// </summary>
    [Column(TypeName = "VARCHAR(500) NOT NULL")]
    public required string CommentText { get; set; }
}
```

O `[Index]` exige `using Microsoft.EntityFrameworkCore;` no topo — acrescentar.

- [ ] **Step 4: Criar `ShipmentLoadChangeLog.cs`**

```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Uma alteração pontual feita no cadastro da carga. Registro campo a campo — responde "quem
/// mudou o quê, quando, e o que estava lá antes".
/// </summary>
/// <remarks>
/// <b>Log e Movimentação não são a mesma coisa.</b> Este log registra o que o USUÁRIO DIGITOU:
/// os campos do formulário, o comentário e o cancelamento. <c>SHIPMENT_LOAD_MOVEMENTS</c>
/// registra o que aconteceu com o SALDO: vinculação de romaneio, faturamento, recusa, devolução.
/// Manter a fronteira é o que impede as duas grades de contarem a mesma história e divergirem.
/// </remarks>
[Table("SHIPMENT_LOADS_CHANGE_LOGS")]
[Index(nameof(ShipmentLoadKey))]
public class ShipmentLoadChangeLog
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid? Key { get; set; }

    public Guid? ShipmentLoadKey { get; set; }
    public virtual ShipmentLoad? ShipmentLoad { get; set; }

    public DateTime ChangedAt { get; set; } = DateTime.Now;

    [Column(TypeName = "VARCHAR(100)")]
    public string? ChangedBy { get; set; }

    /// <summary>
    /// Código do campo alterado (ver <see cref="ShipmentLoadChangeLogFields"/>), não o rótulo
    /// traduzido: a tela resolve o rótulo por formatter, para não travar o i18n.
    /// </summary>
    [Column(TypeName = "VARCHAR(50) NOT NULL")]
    public required string Field { get; set; }

    /// <summary>Valor anterior. Nulo quando a linha registra uma INCLUSÃO.</summary>
    [Column(TypeName = "VARCHAR(500)")]
    public string? OldValue { get; set; }

    /// <summary>Valor novo. Nulo quando a linha registra uma REMOÇÃO.</summary>
    [Column(TypeName = "VARCHAR(500)")]
    public string? NewValue { get; set; }
}
```

- [ ] **Step 5: Criar `ShipmentLoadChangeLogFields.cs`**

```csharp
using System.Globalization;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Códigos gravados na coluna <c>Field</c> de <see cref="ShipmentLoadChangeLog"/>. São contrato
/// com a tela: o formatter do frontend traduz cada código para o rótulo em pt-BR. Não renomeie
/// sem migrar as linhas já gravadas.
/// </summary>
public static class ShipmentLoadChangeLogFields
{
    /// <summary>
    /// Comentário da carga (coleção <c>CommentEntries</c>). Mesma string usada pelos contratos e
    /// pelos documentos — é o que permite reusar <c>ContractCommentRules</c> sem tocar em nada.
    /// Singular de propósito: <see cref="Comments"/> é a OBSERVAÇÃO do cabeçalho.
    /// </summary>
    public const string Comment = "Comment";

    public const string LoadDate = "LoadDate";
    public const string TruckCode = "TruckCode";
    public const string TruckDriver = "TruckDriver";
    public const string Carrier = "Carrier";
    public const string CardCode = "CardCode";
    public const string Warehouse = "Warehouse";
    public const string Item = "Item";
    public const string UnitOfMeasure = "UnitOfMeasure";
    public const string Branch = "Branch";
    public const string HasExcess = "HasExcess";
    public const string FreightPrice = "FreightPrice";

    /// <summary>Observação escalar do cabeçalho. Plural, distinto de <see cref="Comment"/>.</summary>
    public const string Comments = "Comments";

    public const string Status = "Status";
    public const string CancellationReason = "CancellationReason";

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>
    /// Data como ela aparece no log. Formatar no servidor mantém "de" e "para" comparáveis
    /// mesmo que a máscara da tela mude depois — e a tela não teria como saber que aquela linha
    /// é uma data: <c>OldValue</c>/<c>NewValue</c> são texto livre.
    /// </summary>
    public static string DescribeDate(DateTime value) => value.ToString("dd/MM/yyyy", PtBr);

    public static string DescribeBoolean(bool value) => value ? "Sim" : "Não";

    /// <summary>
    /// Nulo devolve nulo: o log já usa nulo para "não havia valor", e "frete não informado" é
    /// diferente de "frete zero".
    /// </summary>
    public static string? DescribeFreightPrice(decimal? value) =>
        value?.ToString("N2", PtBr);

    public static string DescribeStatus(ShipmentLoadStatus status) => status switch
    {
        ShipmentLoadStatus.Planned => "Planejada",
        ShipmentLoadStatus.Open => "Carregada",
        ShipmentLoadStatus.PartiallyInvoiced => "Faturada Parcial",
        ShipmentLoadStatus.Invoiced => "Faturada",
        ShipmentLoadStatus.Cancelled => "Cancelada",
        _ => status.ToString(),
    };
}
```

Antes de gravar, abrir `SiagroB1.Domain/Enums/ShipmentLoadStatus.cs` e conferir os nomes dos membros do enum; se algum diferir do usado acima, corrigir o `switch` (e só ele). Os rótulos em pt-BR devem bater com os que `formatShipmentLoadStatus` já usa no frontend — em particular `Open` é **"Carregada"**, não "Aberta".

- [ ] **Step 6: Acrescentar as nav properties em `ShipmentLoad.cs`**

Logo depois de `public virtual ICollection<ShipmentLoadMovement> Movements { get; } = [];`:

```csharp
    /// <summary>
    /// Anotações da carga. Chama-se <c>CommentEntries</c>, e não <c>Comments</c>, porque
    /// <see cref="Comments"/> já é a "Observações" escalar do cabeçalho.
    /// </summary>
    public virtual ICollection<ShipmentLoadComment> CommentEntries { get; } = [];

    /// <summary>
    /// Log campo a campo do que o usuário digitou. Complementa <see cref="Movements"/>, que
    /// narra o que aconteceu com o saldo.
    /// </summary>
    public virtual ICollection<ShipmentLoadChangeLog> ChangeLogs { get; } = [];
```

- [ ] **Step 7: Acrescentar os `DbSet` em `AppDbContext.cs`**

Logo abaixo de `public DbSet<ShipmentLoadMovement> ShipmentLoadMovements { get; set; }`:

```csharp
    public DbSet<ShipmentLoadComment> ShipmentLoadsComments { get; set; }
    public DbSet<ShipmentLoadChangeLog> ShipmentLoadsChangeLogs { get; set; }
```

Nenhuma configuração de FK é necessária: o `OnModelCreating` já força `DeleteBehavior.NoAction` em todas as relações (`AppDbContext.cs:102-106`).

- [ ] **Step 8: Rodar o teste e confirmar que passa**

```
dotnet test --filter FullyQualifiedName~ShipmentLoadModelTests
```

Esperado: PASSA.

- [ ] **Step 9: Gerar a migration**

```powershell
dotnet build
$env:ASPNETCORE_ENVIRONMENT="Yokotobi-Development"; dotnet ef migrations add AddShipmentLoadCommentsAndChangeLogs --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web
```

- [ ] **Step 10: LER a migration gerada antes de qualquer coisa**

Abrir o arquivo gerado em `SiagroB1.Migrations/AppContext/`. Ele deve conter **exatamente dois `CreateTable`** e os dois `CreateIndex` de `ShipmentLoadKey` — nada mais. Qualquer `AlterColumn`, `DropColumn` ou `AddColumn` em outra tabela é drift do snapshot: apagar essas operações à mão antes de aplicar. Não aplicar ainda — a aplicação no banco é a Task 9.

- [ ] **Step 11: Stage**

```bash
git add SiagroB1.Domain/Entities/ShipmentLoadComment.cs \
        SiagroB1.Domain/Entities/ShipmentLoadChangeLog.cs \
        SiagroB1.Domain/Entities/ShipmentLoadChangeLogFields.cs \
        SiagroB1.Migrations/AppContext/
```

Sem `git commit`.

---

### Task 2: Serviços de escrita e leitura do log

**Files:**
- Create: `SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadsChangeLogService.cs`
- Create: `SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadsChangeLogsGetService.cs`
- Test: `SiagroB1.Application.Tests/ShipmentLoads/ShipmentLoadsChangeLogServiceTests.cs`

**Interfaces:**
- Consumes: `ShipmentLoadChangeLog`, `ShipmentLoadChangeLogFields` (Task 1).
- Produces: `ShipmentLoadsChangeLogService.Register(Guid shipmentLoadKey, string field, string? oldValue, string? newValue, string userName) : void`; `ShipmentLoadsChangeLogsGetService.QueryAll(Guid shipmentLoadKey) : IQueryable<ShipmentLoadChangeLog>`.

- [ ] **Step 1: Escrever o teste que falha**

Criar `SiagroB1.Application.Tests/ShipmentLoads/ShipmentLoadsChangeLogServiceTests.cs`:

```csharp
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.ShipmentLoads;

/// <summary>
/// Porta única de escrita do log da carga. Ela apenas ENFILEIRA: quem chama salva, para que o
/// log e a alteração que ele descreve entrem no mesmo SaveChanges.
/// </summary>
public class ShipmentLoadsChangeLogServiceTests
{
    private readonly IUnitOfWork _db = TestDb.CreateUnitOfWork();

    private ShipmentLoadsChangeLogService Service() => new(_db.Context);

    [Fact]
    public void Register_only_enqueues_and_does_not_save()
    {
        var loadKey = Guid.NewGuid();

        Service().Register(
            loadKey, ShipmentLoadChangeLogFields.TruckCode, "ABC1D23", "XYZ4E56", "joao");

        // Nada no banco ate o SaveChanges de quem chamou.
        Assert.Empty(_db.Context.ShipmentLoadsChangeLogs.ToList());

        _db.Context.SaveChanges();

        var log = Assert.Single(_db.Context.ShipmentLoadsChangeLogs.ToList());
        Assert.Equal(loadKey, log.ShipmentLoadKey);
        Assert.Equal(ShipmentLoadChangeLogFields.TruckCode, log.Field);
        Assert.Equal("ABC1D23", log.OldValue);
        Assert.Equal("XYZ4E56", log.NewValue);
        Assert.Equal("joao", log.ChangedBy);
    }

    [Fact]
    public void Register_truncates_values_at_the_column_width()
    {
        // As colunas sao VARCHAR(500): um texto longo nao pode derrubar a gravacao da alteracao
        // que o log so acompanha.
        Service().Register(
            Guid.NewGuid(),
            ShipmentLoadChangeLogFields.Comments,
            new string('a', 600),
            new string('b', 600),
            "joao");

        _db.Context.SaveChanges();

        var log = Assert.Single(_db.Context.ShipmentLoadsChangeLogs.ToList());
        Assert.Equal(500, log.OldValue!.Length);
        Assert.Equal(500, log.NewValue!.Length);
    }
}
```

- [ ] **Step 2: Rodar o teste e confirmar que falha**

```
dotnet test --filter FullyQualifiedName~ShipmentLoadsChangeLogServiceTests
```

Esperado: FALHA de compilação — `ShipmentLoadsChangeLogService` não existe.

- [ ] **Step 3: Criar `ShipmentLoadsChangeLogService.cs`**

```csharp
using SiagroB1.Domain.Entities;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.ShipmentLoads;

/// <summary>
/// Porta única de escrita do log de alterações da carga. Espelho de
/// <c>PurchaseContractsChangeLogService</c>.
///
/// Apenas enfileira a linha no contexto — quem chama decide quando salvar, para que o log e a
/// alteração que ele descreve entrem no mesmo <c>SaveChanges</c> (nunca sobra log de uma
/// alteração que falhou, nem alteração sem log).
/// </summary>
public class ShipmentLoadsChangeLogService(AppDbContext context)
{
    public void Register(
        Guid shipmentLoadKey,
        string field,
        string? oldValue,
        string? newValue,
        string userName)
    {
        context.ShipmentLoadsChangeLogs.Add(new ShipmentLoadChangeLog
        {
            ShipmentLoadKey = shipmentLoadKey,
            ChangedAt = DateTime.Now,
            ChangedBy = userName,
            Field = field,
            OldValue = Truncate(oldValue),
            NewValue = Truncate(newValue),
        });
    }

    /// <summary>
    /// As colunas de valor são VARCHAR(500): um texto longo não pode derrubar a gravação da
    /// alteração que o log só acompanha.
    /// </summary>
    private static string? Truncate(string? value) =>
        value is { Length: > 500 } ? value[..500] : value;
}
```

- [ ] **Step 4: Criar `ShipmentLoadsChangeLogsGetService.cs`**

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.ShipmentLoads;

/// <summary>
/// Leitura do log de alterações de uma carga, mais recente primeiro. Devolve
/// <see cref="IQueryable{T}"/> para o OData ainda poder aplicar $filter/$orderby/$top.
/// </summary>
public class ShipmentLoadsChangeLogsGetService(AppDbContext context)
{
    public IQueryable<ShipmentLoadChangeLog> QueryAll(Guid shipmentLoadKey) =>
        context.ShipmentLoadsChangeLogs
            .AsNoTracking()
            .Where(x => x.ShipmentLoadKey == shipmentLoadKey)
            .OrderByDescending(x => x.ChangedAt);
}
```

- [ ] **Step 5: Rodar o teste e confirmar que passa**

```
dotnet test --filter FullyQualifiedName~ShipmentLoadsChangeLogServiceTests
```

Esperado: PASSA (2 testes).

- [ ] **Step 6: Stage**

```bash
git add SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadsChangeLogService.cs \
        SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadsChangeLogsGetService.cs \
        SiagroB1.Application.Tests/ShipmentLoads/ShipmentLoadsChangeLogServiceTests.cs
```

---

### Task 3: Serviços de comentário

**Files:**
- Create: `SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadsCommentCreateService.cs`
- Create: `SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadsCommentUpdateService.cs`
- Create: `SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadsCommentDeleteService.cs`
- Create: `SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadsCommentsGetService.cs`
- Test: `SiagroB1.Application.Tests/ShipmentLoads/ShipmentLoadsCommentTests.cs`

**Interfaces:**
- Consumes: `ShipmentLoadsChangeLogService.Register(...)` (Task 2); `ContractCommentRules.NormalizeText(string?) : string` e `ContractCommentRules.EnsureCanModify(string? author, string userName, bool isAdmin) : void`, ambos já existentes em `SiagroB1.Application/Services/ContractCommentRules.cs`.
- Produces: `ShipmentLoadsCommentCreateService.ExecuteAsync(Guid shipmentLoadKey, string? commentText, string userName) : Task<ShipmentLoadComment>`; `ShipmentLoadsCommentUpdateService.ExecuteAsync(Guid commentKey, string? commentText, string userName, bool isAdmin) : Task<ShipmentLoadComment>`; `ShipmentLoadsCommentDeleteService.ExecuteAsync(Guid commentKey, string userName, bool isAdmin) : Task`; `ShipmentLoadsCommentsGetService.QueryAll(Guid shipmentLoadKey) : IQueryable<ShipmentLoadComment>`.

**`ContractCommentRules` é reusada sem nenhuma alteração.** O nome com "Contract" é histórico — ela já serve quatro entidades. Não criar uma cópia "ShipmentLoadCommentRules".

- [ ] **Step 1: Escrever o teste que falha**

Criar `SiagroB1.Application.Tests/ShipmentLoads/ShipmentLoadsCommentTests.cs`:

```csharp
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.ShipmentLoads;

/// <summary>
/// Comentários da carga: gravação, regra de autoria (autor ou admin), ausência DELIBERADA de
/// guarda de status, e as linhas que cada operação deixa no log de alterações.
/// </summary>
public class ShipmentLoadsCommentTests
{
    private readonly IUnitOfWork _db = TestDb.CreateUnitOfWork();

    private ShipmentLoadsChangeLogService ChangeLog() => new(_db.Context);

    private ShipmentLoadsCommentCreateService CreateService() => new(
        _db.Context, ChangeLog(), NullLogger<ShipmentLoadsCommentCreateService>.Instance);

    private ShipmentLoadsCommentUpdateService UpdateService() => new(
        _db.Context, ChangeLog(), NullLogger<ShipmentLoadsCommentUpdateService>.Instance);

    private ShipmentLoadsCommentDeleteService DeleteService() => new(
        _db.Context, ChangeLog(), NullLogger<ShipmentLoadsCommentDeleteService>.Instance);

    private async Task<ShipmentLoad> SeedLoadAsync(
        ShipmentLoadStatus status = ShipmentLoadStatus.Planned)
    {
        var load = new ShipmentLoad
        {
            Key = Guid.NewGuid(),
            Code = "CG000001",
            BranchCode = "01",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            TruckCode = "ABC1D23",
            WarehouseCode = "ARM01",
            Status = status,
        };

        _db.Context.ShipmentLoads.Add(load);
        await _db.Context.SaveChangesAsync();
        return load;
    }

    private List<ShipmentLoadChangeLog> LogsOf(Guid loadKey) =>
        _db.Context.ShipmentLoadsChangeLogs.Where(l => l.ShipmentLoadKey == loadKey).ToList();

    [Fact]
    public async Task Create_stamps_the_author_and_logs_an_inclusion()
    {
        var load = await SeedLoadAsync();

        var comment = await CreateService()
            .ExecuteAsync(load.Key, "  Motorista trocou na portaria.  ", "joao");

        Assert.Equal("Motorista trocou na portaria.", comment.CommentText);
        Assert.Equal("joao", comment.CommentedBy);

        var log = Assert.Single(LogsOf(load.Key));
        Assert.Equal(ShipmentLoadChangeLogFields.Comment, log.Field);
        Assert.Null(log.OldValue);
        Assert.Equal("Motorista trocou na portaria.", log.NewValue);
    }

    [Fact]
    public async Task Create_on_unknown_load_throws_and_writes_nothing()
    {
        await Assert.ThrowsAsync<NotFoundException>(
            () => CreateService().ExecuteAsync(Guid.NewGuid(), "texto", "joao"));

        Assert.Empty(_db.Context.ShipmentLoadsComments.ToList());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task Create_refuses_an_empty_text(string? text)
    {
        var load = await SeedLoadAsync();

        await Assert.ThrowsAsync<DefaultException>(
            () => CreateService().ExecuteAsync(load.Key, text, "joao"));
    }

    [Fact]
    public async Task Create_refuses_a_text_longer_than_the_column_instead_of_truncating()
    {
        var load = await SeedLoadAsync();

        // Recusa, e nao trunca: cortar em silencio esconderia parte do que o usuario escreveu.
        await Assert.ThrowsAsync<DefaultException>(
            () => CreateService().ExecuteAsync(load.Key, new string('a', 501), "joao"));
    }

    [Fact]
    public async Task Comment_is_allowed_on_a_cancelled_load()
    {
        // Sem guarda de status por decisao: comentar carga cancelada e o uso mais comum.
        var load = await SeedLoadAsync(ShipmentLoadStatus.Cancelled);

        var comment = await CreateService().ExecuteAsync(load.Key, "motivo do cancelamento", "joao");

        Assert.NotNull(comment.Key);
    }

    [Fact]
    public async Task Update_rewrites_the_stamp_and_logs_the_previous_text()
    {
        var load = await SeedLoadAsync();
        var comment = await CreateService().ExecuteAsync(load.Key, "antes", "joao");

        var updated = await UpdateService()
            .ExecuteAsync(comment.Key!.Value, "depois", "joao", isAdmin: false);

        Assert.Equal("depois", updated.CommentText);
        Assert.Equal("joao", updated.CommentedBy);

        var log = LogsOf(load.Key).Single(l => l.OldValue == "antes");
        Assert.Equal(ShipmentLoadChangeLogFields.Comment, log.Field);
        Assert.Equal("depois", log.NewValue);
    }

    [Fact]
    public async Task Delete_keeps_the_removed_text_in_the_log()
    {
        var load = await SeedLoadAsync();
        var comment = await CreateService().ExecuteAsync(load.Key, "some daqui", "joao");

        await DeleteService().ExecuteAsync(comment.Key!.Value, "joao", isAdmin: false);

        Assert.Empty(_db.Context.ShipmentLoadsComments.ToList());

        var log = LogsOf(load.Key).Single(l => l.NewValue == null);
        Assert.Equal("some daqui", log.OldValue);
    }

    [Fact]
    public async Task A_non_author_can_neither_edit_nor_delete()
    {
        var load = await SeedLoadAsync();
        var comment = await CreateService().ExecuteAsync(load.Key, "do joao", "joao");

        await Assert.ThrowsAsync<DefaultException>(
            () => UpdateService().ExecuteAsync(comment.Key!.Value, "x", "maria", isAdmin: false));

        await Assert.ThrowsAsync<DefaultException>(
            () => DeleteService().ExecuteAsync(comment.Key!.Value, "maria", isAdmin: false));
    }

    [Fact]
    public async Task An_admin_can_edit_and_delete_someone_elses_comment()
    {
        var load = await SeedLoadAsync();
        var comment = await CreateService().ExecuteAsync(load.Key, "do joao", "joao");

        var updated = await UpdateService()
            .ExecuteAsync(comment.Key!.Value, "editado pelo admin", "maria", isAdmin: true);

        // O carimbo passa a ser de quem editou.
        Assert.Equal("maria", updated.CommentedBy);

        await DeleteService().ExecuteAsync(comment.Key!.Value, "maria", isAdmin: true);
        Assert.Empty(_db.Context.ShipmentLoadsComments.ToList());
    }
}
```

Se a assinatura de `SeedLoadAsync` reclamar de propriedade `required` faltando em `ShipmentLoad`, acrescentar só o que o compilador pedir — não inventar campos.

- [ ] **Step 2: Rodar o teste e confirmar que falha**

```
dotnet test --filter FullyQualifiedName~ShipmentLoadsCommentTests
```

Esperado: FALHA de compilação — os quatro serviços não existem.

- [ ] **Step 3: Criar `ShipmentLoadsCommentCreateService.cs`**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.ShipmentLoads;

/// <summary>
/// Inclui um comentário na carga. Sem guarda de status: comentário é anotação e vale a qualquer
/// tempo — inclusive em carga cancelada, onde registrar o motivo é justamente o uso mais comum
/// (ver <see cref="ContractCommentRules"/>).
/// </summary>
public class ShipmentLoadsCommentCreateService(
    AppDbContext context,
    ShipmentLoadsChangeLogService changeLog,
    ILogger<ShipmentLoadsCommentCreateService> logger)
{
    public async Task<ShipmentLoadComment> ExecuteAsync(
        Guid shipmentLoadKey, string? commentText, string userName)
    {
        try
        {
            var text = ContractCommentRules.NormalizeText(commentText);

            if (!await context.ShipmentLoads.AnyAsync(x => x.Key == shipmentLoadKey))
                throw new NotFoundException("Carga não encontrada.");

            var comment = new ShipmentLoadComment
            {
                ShipmentLoadKey = shipmentLoadKey,
                CommentedAt = DateTime.Now,
                CommentedBy = userName,
                CommentText = text,
            };

            await context.AddAsync(comment);

            changeLog.Register(
                shipmentLoadKey,
                ShipmentLoadChangeLogFields.Comment,
                null,
                text,
                userName);

            await context.SaveChangesAsync();

            return comment;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, exception.Message);
            throw;
        }
    }
}
```

`ContractCommentRules` está em `SiagroB1.Application.Services`, o namespace pai — não precisa de `using` extra.

- [ ] **Step 4: Criar `ShipmentLoadsCommentUpdateService.cs`**

```csharp
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.ShipmentLoads;

/// <summary>
/// Altera o texto de um comentário da carga. A data/hora e o autor são REESCRITOS: a linha passa
/// a mostrar a última alteração, e a versão anterior sobrevive no log.
/// </summary>
public class ShipmentLoadsCommentUpdateService(
    AppDbContext context,
    ShipmentLoadsChangeLogService changeLog,
    ILogger<ShipmentLoadsCommentUpdateService> logger)
{
    public async Task<ShipmentLoadComment> ExecuteAsync(
        Guid commentKey, string? commentText, string userName, bool isAdmin)
    {
        try
        {
            var text = ContractCommentRules.NormalizeText(commentText);

            var comment = await context.ShipmentLoadsComments.FindAsync(commentKey)
                ?? throw new NotFoundException("Comentário não encontrado.");

            ContractCommentRules.EnsureCanModify(comment.CommentedBy, userName, isAdmin);

            var previousText = comment.CommentText;

            comment.CommentText = text;
            comment.CommentedAt = DateTime.Now;
            comment.CommentedBy = userName;

            if (comment.ShipmentLoadKey.HasValue)
                changeLog.Register(
                    comment.ShipmentLoadKey.Value,
                    ShipmentLoadChangeLogFields.Comment,
                    previousText,
                    text,
                    userName);

            await context.SaveChangesAsync();

            return comment;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, exception.Message);
            throw;
        }
    }
}
```

- [ ] **Step 5: Criar `ShipmentLoadsCommentDeleteService.cs`**

```csharp
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.ShipmentLoads;

/// <summary>
/// Exclui um comentário da carga. O texto excluído fica registrado no log — é o que permite
/// reconstituir o que foi apagado.
/// </summary>
public class ShipmentLoadsCommentDeleteService(
    AppDbContext context,
    ShipmentLoadsChangeLogService changeLog,
    ILogger<ShipmentLoadsCommentDeleteService> logger)
{
    public async Task ExecuteAsync(Guid commentKey, string userName, bool isAdmin)
    {
        try
        {
            var comment = await context.ShipmentLoadsComments.FindAsync(commentKey)
                ?? throw new NotFoundException("Comentário não encontrado.");

            ContractCommentRules.EnsureCanModify(comment.CommentedBy, userName, isAdmin);

            context.ShipmentLoadsComments.Remove(comment);

            if (comment.ShipmentLoadKey.HasValue)
                changeLog.Register(
                    comment.ShipmentLoadKey.Value,
                    ShipmentLoadChangeLogFields.Comment,
                    comment.CommentText,
                    null,
                    userName);

            await context.SaveChangesAsync();
        }
        catch (Exception exception)
        {
            logger.LogError(exception, exception.Message);
            throw;
        }
    }
}
```

- [ ] **Step 6: Criar `ShipmentLoadsCommentsGetService.cs`**

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.ShipmentLoads;

/// <summary>
/// Leitura dos comentários de uma carga, mais recente primeiro. Devolve
/// <see cref="IQueryable{T}"/> para o OData ainda poder aplicar $filter/$orderby/$top.
/// </summary>
public class ShipmentLoadsCommentsGetService(AppDbContext context)
{
    public IQueryable<ShipmentLoadComment> QueryAll(Guid shipmentLoadKey) =>
        context.ShipmentLoadsComments
            .AsNoTracking()
            .Where(x => x.ShipmentLoadKey == shipmentLoadKey)
            .OrderByDescending(x => x.CommentedAt);
}
```

- [ ] **Step 7: Rodar o teste e confirmar que passa**

```
dotnet test --filter FullyQualifiedName~ShipmentLoadsCommentTests
```

Esperado: PASSA (11 testes, contando as três variações do `[Theory]`).

- [ ] **Step 8: Stage**

```bash
git add SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadsComment*.cs \
        SiagroB1.Application.Tests/ShipmentLoads/ShipmentLoadsCommentTests.cs
```

---

### Task 4: Log campo a campo na edição da carga

**Files:**
- Modify: `SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadsUpdateService.cs`
- Test: `SiagroB1.Application.Tests/ShipmentLoads/ShipmentLoadsChangeLogTests.cs` (novo)
- Test: `SiagroB1.Application.Tests/ShipmentLoads/ShipmentLoadsUpdateServiceTests.cs` (o construtor do serviço muda)

**Interfaces:**
- Consumes: `ShipmentLoadsChangeLogService.Register(...)` (Task 2); `ShipmentLoadChangeLogFields` (Task 1).
- Produces: `ShipmentLoadsUpdateService(IUnitOfWork db, ShipmentLoadsMovementLogService movementLog, ShipmentLoadsChangeLogService changeLog)` — **o construtor ganha um terceiro parâmetro**; `ExecuteAsync(ShipmentLoad input, string userName) : Task<ShipmentLoad>` continua igual.

**Invariante a preservar:** a descrição da linha `ShipmentLoadMovementType.Updated` deve continuar **byte a byte idêntica** à atual — `"Dados da carga alterados: " + string.Join("; ", changes) + "."`, com cada item no formato `"{label}: '{before}' para '{after}'"`. Há teste de regressão para isso.

- [ ] **Step 1: Escrever o teste que falha**

Criar `SiagroB1.Application.Tests/ShipmentLoads/ShipmentLoadsChangeLogTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.ShipmentLoads;

/// <summary>
/// O log campo a campo da edição da carga, e a garantia de que a linha narrativa da
/// Movimentação continua exatamente como era.
/// </summary>
public class ShipmentLoadsChangeLogTests
{
    private readonly IUnitOfWork _db = TestDb.CreateUnitOfWork();

    private ShipmentLoadsUpdateService Service() => new(
        _db,
        new ShipmentLoadsMovementLogService(_db.Context),
        new ShipmentLoadsChangeLogService(_db.Context));

    private ShipmentLoad Load(ShipmentLoadStatus status = ShipmentLoadStatus.Planned)
    {
        var load = new ShipmentLoad
        {
            Key = Guid.NewGuid(),
            Code = "CG000001",
            BranchCode = "01",
            LoadDate = new DateTime(2026, 8, 28),
            ItemCode = "SOJA",
            ItemName = "SOJA EM GRAOS",
            UnitOfMeasureCode = "KG",
            TruckCode = "ABC1D23",
            TruckDriverName = "JOAO",
            WarehouseCode = "ARM01",
            Status = status,
            FreightPrice = 1_000m,
        };

        _db.Context.ShipmentLoads.Add(load);
        return load;
    }

    private static ShipmentLoad Input(
        Guid key,
        string? truckCode = "ABC1D23",
        string? driverName = "JOAO",
        decimal? freightPrice = 1_000m,
        bool hasExcess = false) => new()
    {
        Key = key,
        BranchCode = "01",
        LoadDate = new DateTime(2026, 8, 28),
        ItemCode = "SOJA",
        ItemName = "SOJA EM GRAOS",
        UnitOfMeasureCode = "KG",
        TruckCode = truckCode,
        TruckDriverName = driverName,
        WarehouseCode = "ARM01",
        FreightPrice = freightPrice,
        HasExcess = hasExcess,
    };

    private List<ShipmentLoadChangeLog> LogsOf(Guid loadKey) =>
        _db.Context.ShipmentLoadsChangeLogs.Where(l => l.ShipmentLoadKey == loadKey).ToList();

    [Fact]
    public async Task Each_changed_field_gets_its_own_log_row()
    {
        var load = Load();
        await _db.Context.SaveChangesAsync();

        await Service().ExecuteAsync(
            Input(load.Key, truckCode: "XYZ4E56", driverName: "PEDRO", freightPrice: 1_200m),
            "joao");

        var logs = LogsOf(load.Key);
        Assert.Equal(3, logs.Count);

        var truck = logs.Single(l => l.Field == ShipmentLoadChangeLogFields.TruckCode);
        Assert.Equal("ABC1D23", truck.OldValue);
        Assert.Equal("XYZ4E56", truck.NewValue);

        var driver = logs.Single(l => l.Field == ShipmentLoadChangeLogFields.TruckDriver);
        Assert.Equal("JOAO", driver.OldValue);
        Assert.Equal("PEDRO", driver.NewValue);

        var freight = logs.Single(l => l.Field == ShipmentLoadChangeLogFields.FreightPrice);
        Assert.Equal("1.000,00", freight.OldValue);
        Assert.Equal("1.200,00", freight.NewValue);

        Assert.All(logs, l => Assert.Equal("joao", l.ChangedBy));
    }

    [Fact]
    public async Task An_update_that_changes_nothing_writes_neither_log_nor_movement()
    {
        var load = Load();
        await _db.Context.SaveChangesAsync();

        await Service().ExecuteAsync(Input(load.Key), "joao");

        Assert.Empty(LogsOf(load.Key));
        Assert.Empty(_db.Context.ShipmentLoadMovements.Where(m => m.ShipmentLoadKey == load.Key).ToList());
    }

    [Fact]
    public async Task The_movement_narrative_is_unchanged()
    {
        // Regressao: a Movimentacao e a linha do tempo que a Logistica le. O log acrescenta
        // granularidade, nao substitui a narrativa.
        var load = Load();
        await _db.Context.SaveChangesAsync();

        await Service().ExecuteAsync(Input(load.Key, truckCode: "XYZ4E56"), "joao");

        var movement = _db.Context.ShipmentLoadMovements
            .Single(m => m.ShipmentLoadKey == load.Key);

        Assert.Equal(ShipmentLoadMovementType.Updated, movement.MovementType);
        Assert.Equal(
            "Dados da carga alterados: Veículo: 'ABC1D23' para 'XYZ4E56'.",
            movement.Description);
    }

    [Fact]
    public async Task A_boolean_and_a_date_are_written_as_pt_br_text()
    {
        var load = Load();
        await _db.Context.SaveChangesAsync();

        var input = Input(load.Key, hasExcess: true);
        input.LoadDate = new DateTime(2026, 8, 30);

        await Service().ExecuteAsync(input, "joao");

        var logs = LogsOf(load.Key);

        var excess = logs.Single(l => l.Field == ShipmentLoadChangeLogFields.HasExcess);
        Assert.Equal("Não", excess.OldValue);
        Assert.Equal("Sim", excess.NewValue);

        var date = logs.Single(l => l.Field == ShipmentLoadChangeLogFields.LoadDate);
        Assert.Equal("28/08/2026", date.OldValue);
        Assert.Equal("30/08/2026", date.NewValue);
    }
}
```

- [ ] **Step 2: Rodar o teste e confirmar que falha**

```
dotnet test --filter FullyQualifiedName~ShipmentLoadsChangeLogTests
```

Esperado: FALHA de compilação — o construtor de `ShipmentLoadsUpdateService` só aceita dois argumentos.

- [ ] **Step 3: Reescrever `DescribeChanges` e o construtor**

Em `ShipmentLoadsUpdateService.cs`, trocar a assinatura da classe:

```csharp
public class ShipmentLoadsUpdateService(
    IUnitOfWork db,
    ShipmentLoadsMovementLogService movementLog,
    ShipmentLoadsChangeLogService changeLog)
```

Substituir o `DescribeChanges` inteiro por:

```csharp
    /// <summary>
    /// Uma alteração de campo, na forma que serve às DUAS saídas: a linha do log (código, de,
    /// para) e a frase da narrativa da Movimentação (rótulo, de, para).
    /// </summary>
    private readonly record struct FieldChange(
        string Field, string Label, string? Old, string? New);

    /// <summary>
    /// Descreve o que mudou ANTES de a entidade rastreada ser sobrescrita — depois da atribuição
    /// não haveria mais com o que comparar.
    /// </summary>
    /// <remarks>
    /// Uma única lista alimenta o log campo a campo e a descrição concatenada da Movimentação.
    /// Duas fontes divergiriam com o tempo.
    /// </remarks>
    private static List<FieldChange> DescribeChanges(ShipmentLoad load, ShipmentLoad input)
    {
        var changes = new List<FieldChange>();

        void Compare(string field, string label, string? before, string? after)
        {
            if ((before ?? string.Empty) != (after ?? string.Empty))
                changes.Add(new FieldChange(field, label, before, after));
        }

        Compare(ShipmentLoadChangeLogFields.TruckCode, "Veículo", load.TruckCode, input.TruckCode);
        Compare(ShipmentLoadChangeLogFields.TruckDriver, "Motorista",
            load.TruckDriverName, input.TruckDriverName);
        Compare(ShipmentLoadChangeLogFields.Carrier, "Transportadora",
            load.CarrierName, input.CarrierName);
        Compare(ShipmentLoadChangeLogFields.CardCode, "Cliente", load.CardName, input.CardName);
        Compare(ShipmentLoadChangeLogFields.Warehouse, "Armazém",
            load.WarehouseCode, input.WarehouseCode);
        Compare(ShipmentLoadChangeLogFields.Item, "Produto", load.ItemCode, input.ItemCode);
        Compare(ShipmentLoadChangeLogFields.UnitOfMeasure, "Unidade",
            load.UnitOfMeasureCode, input.UnitOfMeasureCode);
        Compare(ShipmentLoadChangeLogFields.Branch, "Filial", load.BranchCode, input.BranchCode);
        Compare(ShipmentLoadChangeLogFields.Comments, "Observações", load.Comments, input.Comments);

        if (load.LoadDate.Date != input.LoadDate.Date)
            changes.Add(new FieldChange(
                ShipmentLoadChangeLogFields.LoadDate, "Data",
                ShipmentLoadChangeLogFields.DescribeDate(load.LoadDate),
                ShipmentLoadChangeLogFields.DescribeDate(input.LoadDate)));

        if (load.HasExcess != input.HasExcess)
            changes.Add(new FieldChange(
                ShipmentLoadChangeLogFields.HasExcess, "Excesso",
                ShipmentLoadChangeLogFields.DescribeBoolean(load.HasExcess),
                ShipmentLoadChangeLogFields.DescribeBoolean(input.HasExcess)));

        if (load.FreightPrice != input.FreightPrice)
            changes.Add(new FieldChange(
                ShipmentLoadChangeLogFields.FreightPrice, "Valor do frete",
                ShipmentLoadChangeLogFields.DescribeFreightPrice(load.FreightPrice),
                ShipmentLoadChangeLogFields.DescribeFreightPrice(input.FreightPrice)));

        return changes;
    }

    /// <summary>
    /// A frase da narrativa, no formato que a Movimentação sempre teve. Preservada byte a byte:
    /// há teste de regressão sobre este texto.
    /// </summary>
    private static string DescribeNarrative(IEnumerable<FieldChange> changes) =>
        "Dados da carga alterados: "
        + string.Join("; ", changes.Select(c => $"{c.Label}: '{c.Old}' para '{c.New}'"))
        + ".";
```

Atenção: os rótulos e a ordem das comparações devem reproduzir o comportamento atual. A comparação de `TruckDriverCode`, `CarrierCardCode` e `UnitOfMeasureCode` **não** entra na narrativa hoje; ela entra agora, e a Task 4 aceita essa mudança de texto **apenas** para `UnitOfMeasure` — que é o único acrescentado à lista acima. `TruckDriverCode` e `CarrierCardCode` continuam fora, porque o nome desnormalizado já cobre o caso e acrescentá-los duplicaria a frase.

- [ ] **Step 4: Usar a lista nas duas saídas**

No corpo de `ExecuteAsync`, o bloco dentro da transação passa a ser:

```csharp
            if (changes.Count > 0)
            {
                movementLog.Register(
                    load.Key,
                    ShipmentLoadMovementType.Updated,
                    decimal.Zero,
                    load.AvailableQuantity,
                    DescribeNarrative(changes),
                    userName);

                foreach (var change in changes)
                    changeLog.Register(load.Key, change.Field, change.Old, change.New, userName);
            }
```

O resto de `ExecuteAsync` fica intacto — inclusive `BeginTransactionAsync`/`SaveChangesAsync`/`CommitAsync`, que é o que garante log, movimento e alteração no mesmo commit.

Acrescentar `using SiagroB1.Domain.Entities;` se ainda não estiver no topo (já está — `ShipmentLoad` vem de lá).

- [ ] **Step 5: Corrigir o construtor nos testes existentes**

Em `SiagroB1.Application.Tests/ShipmentLoads/ShipmentLoadsUpdateServiceTests.cs`, o helper `Service()` passa a ser:

```csharp
    private ShipmentLoadsUpdateService Service() => new(
        _db,
        new ShipmentLoadsMovementLogService(_db.Context),
        new ShipmentLoadsChangeLogService(_db.Context));
```

Procurar por outros pontos que instanciem `ShipmentLoadsUpdateService` e corrigir do mesmo jeito:

```
grep -rn "new ShipmentLoadsUpdateService" --include=*.cs .
```

- [ ] **Step 6: Rodar a suíte inteira**

```
dotnet test
```

Esperado: PASSA, sem regressão. Se `The_movement_narrative_is_unchanged` falhar, o texto da narrativa mudou — corrigir `DescribeNarrative`, não o teste.

- [ ] **Step 7: Stage**

```bash
git add SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadsUpdateService.cs \
        SiagroB1.Application.Tests/ShipmentLoads/ShipmentLoadsChangeLogTests.cs \
        SiagroB1.Application.Tests/ShipmentLoads/ShipmentLoadsUpdateServiceTests.cs
```

---

### Task 5: Log do cancelamento

**Files:**
- Modify: `SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadsCancelService.cs`
- Test: `SiagroB1.Application.Tests/ShipmentLoads/ShipmentLoadsCancelServiceTests.cs`

**Interfaces:**
- Consumes: `ShipmentLoadsChangeLogService.Register(...)` (Task 2); `ShipmentLoadChangeLogFields.Status`, `.CancellationReason`, `.DescribeStatus(...)` (Task 1).
- Produces: `ShipmentLoadsCancelService(IUnitOfWork db, ShipmentLoadsCompositionGuardService compositionGuard, ShipmentLoadsMovementLogService movementLog, ShipmentLoadsChangeLogService changeLog)` — **quarto parâmetro no construtor**; `ExecuteAsync(Guid key, string cancellationReason, string userName) : Task` inalterado.

- [ ] **Step 1: Escrever o teste que falha**

Acrescentar ao fim da classe em `ShipmentLoadsCancelServiceTests.cs`:

```csharp
    [Fact]
    public async Task Cancelling_logs_the_status_and_the_reason()
    {
        var load = Load();
        await _db.Context.SaveChangesAsync();

        await Service().ExecuteAsync(load.Key, "  cliente desistiu  ", "joao");

        var logs = _db.Context.ShipmentLoadsChangeLogs
            .Where(l => l.ShipmentLoadKey == load.Key).ToList();

        var status = logs.Single(l => l.Field == ShipmentLoadChangeLogFields.Status);
        // Prende o valor ANTERIOR: sem isso o teste passaria mesmo se a situacao fosse lida
        // depois de sobrescrita, gravando "Cancelada" nos dois lados.
        Assert.Equal("Carregada", status.OldValue);
        Assert.Equal("Cancelada", status.NewValue);

        var reason = logs.Single(l => l.Field == ShipmentLoadChangeLogFields.CancellationReason);
        Assert.Null(reason.OldValue);
        Assert.Equal("cliente desistiu", reason.NewValue);
    }
```

Reusar os helpers `Load()` e `Service()` que o arquivo já tem — ajustar `Service()` para o construtor de quatro argumentos.

- [ ] **Step 2: Rodar o teste e confirmar que falha**

```
dotnet test --filter FullyQualifiedName~ShipmentLoadsCancelServiceTests
```

Esperado: FALHA de compilação.

- [ ] **Step 3: Acrescentar o parâmetro e as duas linhas**

Construtor:

```csharp
public class ShipmentLoadsCancelService(
    IUnitOfWork db,
    ShipmentLoadsCompositionGuardService compositionGuard,
    ShipmentLoadsMovementLogService movementLog,
    ShipmentLoadsChangeLogService changeLog)
```

Dentro do `try`, capturar a situação anterior **antes** de sobrescrevê-la e registrar as duas linhas junto das atribuições existentes:

```csharp
            var previousStatus = load.Status;

            load.Status = ShipmentLoadStatus.Cancelled;
            load.InvoicedQuantity = decimal.Zero;
            load.CancellationReason = cancellationReason.Trim();
            // ... o resto das atribuicoes que ja existem

            changeLog.Register(
                load.Key,
                ShipmentLoadChangeLogFields.Status,
                ShipmentLoadChangeLogFields.DescribeStatus(previousStatus),
                ShipmentLoadChangeLogFields.DescribeStatus(ShipmentLoadStatus.Cancelled),
                userName);

            changeLog.Register(
                load.Key,
                ShipmentLoadChangeLogFields.CancellationReason,
                null,
                load.CancellationReason,
                userName);
```

As duas linhas ficam dentro da transação que já existe, antes do `SaveChangesAsync`.

- [ ] **Step 4: Rodar o teste e confirmar que passa**

```
dotnet test --filter FullyQualifiedName~ShipmentLoadsCancelServiceTests
```

Esperado: PASSA.

- [ ] **Step 5: Stage**

```bash
git add SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadsCancelService.cs \
        SiagroB1.Application.Tests/ShipmentLoads/ShipmentLoadsCancelServiceTests.cs
```

---

### Task 6: Exposição no OData — EDM, actions, controllers e DI

**Files:**
- Modify: `SiagroB1.Web/ODataConfig/ODataConfigurations.cs` (entity sets junto da linha 191; actions junto da linha 580)
- Create: `SiagroB1.Web/Actions/ShipmentLoads/ShipmentLoadsCommentCreateController.cs`
- Create: `SiagroB1.Web/Actions/ShipmentLoads/ShipmentLoadsCommentUpdateController.cs`
- Create: `SiagroB1.Web/Actions/ShipmentLoads/ShipmentLoadsCommentDeleteController.cs`
- Create: `SiagroB1.Web/Controllers/ShipmentLoadsCommentsController.cs`
- Create: `SiagroB1.Web/Controllers/ShipmentLoadsChangeLogsController.cs`
- Modify: `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs:382-395`
- Test: `SiagroB1.Application.Tests/ShipmentLoads/ShipmentLoadEdmModelTests.cs`

**Interfaces:**
- Consumes: os seis serviços das Tasks 2 e 3; `User.IsAdmin()` de `SiagroB1.Web.Extensions`.
- Produces: as actions OData `ShipmentLoadsCommentCreate` (`LoadKey: Guid`, `Text: string`), `ShipmentLoadsCommentUpdate` (`Key: Guid`, `Text: string`), `ShipmentLoadsCommentDelete` (`Key: Guid`); os entity sets `ShipmentLoadsComments` e `ShipmentLoadsChangeLogs`; as rotas `odata/ShipmentLoads({key})/CommentEntries` e `odata/ShipmentLoads({key})/ChangeLogs`.

- [ ] **Step 1: Escrever o teste que falha**

Acrescentar à classe em `ShipmentLoadEdmModelTests.cs`:

```csharp
    [Fact]
    public void Comments_and_change_logs_are_exposed_as_entity_sets()
    {
        var container = Model().EntityContainer;

        Assert.NotNull(container.FindEntitySet("ShipmentLoadsComments"));
        Assert.NotNull(container.FindEntitySet("ShipmentLoadsChangeLogs"));
    }

    [Theory]
    [InlineData("ShipmentLoadsCommentCreate")]
    [InlineData("ShipmentLoadsCommentUpdate")]
    [InlineData("ShipmentLoadsCommentDelete")]
    public void Comment_actions_are_declared(string actionName)
    {
        Assert.Single(Model().SchemaElements.OfType<IEdmAction>().Where(a => a.Name == actionName));
    }

    [Fact]
    public void The_create_action_takes_the_load_key_and_the_text()
    {
        var action = Model().SchemaElements.OfType<IEdmAction>()
            .Single(a => a.Name == "ShipmentLoadsCommentCreate");

        var parameters = action.Parameters.Select(p => p.Name).ToArray();

        // LoadKey, e nao Key: Key e a chave do COMENTARIO nas outras duas.
        Assert.Contains("LoadKey", parameters);
        Assert.Contains("Text", parameters);
    }
```

Seguir o formato do `[Theory]` que o arquivo já usa para as outras actions (por volta da linha 48) — se ele usar um helper próprio para procurar a action, reusar esse helper em vez do LINQ acima.

- [ ] **Step 2: Rodar o teste e confirmar que falha**

```
dotnet test --filter FullyQualifiedName~ShipmentLoadEdmModelTests
```

Esperado: FALHA — entity sets e actions não declarados.

- [ ] **Step 3: Declarar entity sets e actions no EDM**

Em `ODataConfigurations.cs`, logo após `modelBuilder.EntitySet<ShipmentLoadMovement>("ShipmentLoadMovements");`:

```csharp
        modelBuilder.EntitySet<ShipmentLoadComment>("ShipmentLoadsComments");
        modelBuilder.EntitySet<ShipmentLoadChangeLog>("ShipmentLoadsChangeLogs");
```

E junto das demais actions de carga (perto de `shipmentLoadsCancel`):

```csharp
        var shipmentLoadsCommentCreate = modelBuilder.Action("ShipmentLoadsCommentCreate");
        shipmentLoadsCommentCreate.Parameter<Guid>("LoadKey");
        shipmentLoadsCommentCreate.Parameter<string>("Text");
        shipmentLoadsCommentCreate.Returns<IActionResult>();

        var shipmentLoadsCommentUpdate = modelBuilder.Action("ShipmentLoadsCommentUpdate");
        shipmentLoadsCommentUpdate.Parameter<Guid>("Key");
        shipmentLoadsCommentUpdate.Parameter<string>("Text");
        shipmentLoadsCommentUpdate.Returns<IActionResult>();

        var shipmentLoadsCommentDelete = modelBuilder.Action("ShipmentLoadsCommentDelete");
        shipmentLoadsCommentDelete.Parameter<Guid>("Key");
        shipmentLoadsCommentDelete.Returns<IActionResult>();
```

- [ ] **Step 4: Criar `ShipmentLoadsCommentCreateController.cs`**

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.ShipmentLoads;

public class ShipmentLoadsCommentCreateController(
    ShipmentLoadsCommentCreateService service) : ODataController
{
    /// <summary><c>LoadKey</c> é a chave da CARGA — as outras duas actions recebem a do comentário.</summary>
    [HttpPost("odata/ShipmentLoadsCommentCreate")]
    public async Task<IActionResult> Post(ODataActionParameters parameters)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            // parameters chega NULO quando nenhum parametro do EDM e enviado.
            if (parameters is null || !parameters.TryGetValue("LoadKey", out var keyObj))
                return BadRequest("Chave da carga não informada.");

            parameters.TryGetValue("Text", out var textObj);

            await service.ExecuteAsync(
                (Guid) keyObj, textObj as string, User.Identity?.Name ?? "Unknown");

            return Ok();
        }
        catch (Exception e)
        {
            if (e is NotFoundException or KeyNotFoundException)
                return NotFound(e.Message);

            if (e is DefaultException or BusinessException or ApplicationException)
                return BadRequest(e.Message);

            return StatusCode(500, e.Message);
        }
    }
}
```

- [ ] **Step 5: Criar `ShipmentLoadsCommentUpdateController.cs`**

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Web.Extensions;

namespace SiagroB1.Web.Actions.ShipmentLoads;

public class ShipmentLoadsCommentUpdateController(
    ShipmentLoadsCommentUpdateService service) : ODataController
{
    /// <summary>
    /// <c>Key</c> é a chave do COMENTÁRIO. Só o autor altera o próprio comentário; admin altera
    /// qualquer um — a permissão é decidida no servidor, a tela só evita a viagem inútil.
    /// </summary>
    [HttpPost("odata/ShipmentLoadsCommentUpdate")]
    public async Task<IActionResult> Post(ODataActionParameters parameters)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            if (parameters is null || !parameters.TryGetValue("Key", out var keyObj))
                return BadRequest("Chave do comentário não informada.");

            parameters.TryGetValue("Text", out var textObj);

            await service.ExecuteAsync(
                (Guid) keyObj, textObj as string, User.Identity?.Name ?? "Unknown", User.IsAdmin());

            return Ok();
        }
        catch (Exception e)
        {
            if (e is NotFoundException or KeyNotFoundException)
                return NotFound(e.Message);

            if (e is DefaultException or BusinessException or ApplicationException)
                return BadRequest(e.Message);

            return StatusCode(500, e.Message);
        }
    }
}
```

- [ ] **Step 6: Criar `ShipmentLoadsCommentDeleteController.cs`**

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Web.Extensions;

namespace SiagroB1.Web.Actions.ShipmentLoads;

public class ShipmentLoadsCommentDeleteController(
    ShipmentLoadsCommentDeleteService service) : ODataController
{
    /// <summary><c>Key</c> é a chave do COMENTÁRIO, não a da carga.</summary>
    [HttpPost("odata/ShipmentLoadsCommentDelete")]
    public async Task<IActionResult> Post(ODataActionParameters parameters)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            if (parameters is null || !parameters.TryGetValue("Key", out var keyObj))
                return BadRequest("Chave do comentário não informada.");

            await service.ExecuteAsync(
                (Guid) keyObj, User.Identity?.Name ?? "Unknown", User.IsAdmin());

            return Ok();
        }
        catch (Exception e)
        {
            if (e is NotFoundException or KeyNotFoundException)
                return NotFound(e.Message);

            if (e is DefaultException or BusinessException or ApplicationException)
                return BadRequest(e.Message);

            return StatusCode(500, e.Message);
        }
    }
}
```

- [ ] **Step 7: Criar os dois controllers de leitura**

`SiagroB1.Web/Controllers/ShipmentLoadsCommentsController.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Domain.Entities;

namespace SiagroB1.Web.Controllers;

/// <summary>
/// Comentários da carga: somente leitura aqui. A escrita passa pelas actions
/// ShipmentLoadsComment{Create,Update,Delete}, que carimbam o autor e gravam o log de alterações
/// na mesma transação.
/// </summary>
public class ShipmentLoadsCommentsController(
    ShipmentLoadsCommentsGetService getService)
    : ODataController
{
    [HttpGet("odata/ShipmentLoads({key:guid})/CommentEntries")]
    [HttpGet("odata/ShipmentLoads/{key:guid}/CommentEntries")]
    [EnableQuery]
    public ActionResult<IEnumerable<ShipmentLoadComment>> Get([FromRoute] Guid key)
    {
        return Ok(getService.QueryAll(key));
    }
}
```

`SiagroB1.Web/Controllers/ShipmentLoadsChangeLogsController.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Domain.Entities;

namespace SiagroB1.Web.Controllers;

/// <summary>
/// Log de alterações da carga: somente leitura. Quem escreve é
/// <c>ShipmentLoadsChangeLogService</c>, sempre no mesmo SaveChanges da alteração que descreve.
/// </summary>
public class ShipmentLoadsChangeLogsController(
    ShipmentLoadsChangeLogsGetService getService)
    : ODataController
{
    [HttpGet("odata/ShipmentLoads({key:guid})/ChangeLogs")]
    [HttpGet("odata/ShipmentLoads/{key:guid}/ChangeLogs")]
    [EnableQuery]
    public ActionResult<IEnumerable<ShipmentLoadChangeLog>> Get([FromRoute] Guid key)
    {
        return Ok(getService.QueryAll(key));
    }
}
```

As duas formas de rota (parêntese e barra) são obrigatórias: navegação não declarada à mão devolve 404 neste projeto.

- [ ] **Step 8: Registrar os seis serviços no DI**

Em `ServiceCollectionExtensions.cs`, junto do bloco de `ShipmentLoads`:

```csharp
        services.AddScoped<ShipmentLoadsChangeLogService>();
        services.AddScoped<ShipmentLoadsChangeLogsGetService>();
        services.AddScoped<ShipmentLoadsCommentCreateService>();
        services.AddScoped<ShipmentLoadsCommentUpdateService>();
        services.AddScoped<ShipmentLoadsCommentDeleteService>();
        services.AddScoped<ShipmentLoadsCommentsGetService>();
```

- [ ] **Step 9: Rodar a suíte inteira**

```
dotnet test
```

Esperado: PASSA, incluindo os testes novos de EDM.

- [ ] **Step 10: Stage**

```bash
git add SiagroB1.Web/Actions/ShipmentLoads/ShipmentLoadsComment*.cs \
        SiagroB1.Web/Controllers/ShipmentLoadsCommentsController.cs \
        SiagroB1.Web/Controllers/ShipmentLoadsChangeLogsController.cs \
        SiagroB1.Web/ODataConfig/ODataConfigurations.cs \
        SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs \
        SiagroB1.Application.Tests/ShipmentLoads/ShipmentLoadEdmModelTests.cs
```

---

### Task 7: Fragmentos e seções na tela de detalhe

**Repo: `siagro-b1-frontend`.** Todos os caminhos desta task e da Task 8 são relativos a `C:\Projetos\SiagroB1\siagro-b1-frontend`.

**Files:**
- Create: `webapp/view/shipmentLoads/fragments/ShipmentLoadComments.fragment.xml`
- Create: `webapp/view/shipmentLoads/fragments/ShipmentLoadCommentDialog.fragment.xml`
- Create: `webapp/view/shipmentLoads/fragments/ShipmentLoadChangeLogs.fragment.xml`
- Modify: `webapp/view/shipmentLoads/Detail.view.xml` (duas seções novas, depois da seção "Movimentação")
- Modify: `webapp/model/formatter.ts` (um formatter novo)

**Interfaces:**
- Consumes: as coleções `CommentEntries` e `ChangeLogs` do EDM (Task 6).
- Produces: os ids de controle `shipmentLoadCommentsTable`, `shipmentLoadChangeLogsTable` e `shipmentLoadCommentDialog`; o formatter `formatShipmentLoadChangeLogField(value: string) : string`; os handlers que a Task 8 implementa: `.onAddComment`, `.onEditComment`, `.onRemoveComment`, `.onConfirmComment`, `.onCloseCommentDialog`.

- [ ] **Step 1: Criar `ShipmentLoadComments.fragment.xml`**

```xml
<core:FragmentDefinition
    xmlns="sap.m"
    xmlns:t="sap.ui.table"
    xmlns:core="sap.ui.core"
>
  <!--
    Comentarios da carga: anotacoes com data, hora e autor. Editaveis a qualquer tempo,
    inclusive em carga cancelada, porque comentario nao altera peso, saldo nem valor. Por isso
    os botoes nao olham o Status.

    A tabela e somente leitura: o texto e digitado no dialogo e gravado por action.

    `$$ownRequest` e obrigatorio: sem ele o binding vira $expand no GET da carga, e
    ShipmentLoadsGetService nao inclui CommentEntries - a tabela viria vazia.

    O `sorter` tambem e obrigatorio, por menos obvio: o OrderByDescending do service SO vale na
    consulta sem paginacao. A sap.ui.table pede $skip/$top, e ai o [EnableQuery] reordena por
    chave para estabilizar a paginacao.
  -->
  <t:Table
    id="shipmentLoadCommentsTable"
    class="sapUiSizeCondensed"
    alternateRowColors="true"
    enableBusyIndicator="true"
    enableSelectAll="false"
    selectionBehavior="Row"
    selectionMode="Single"
    busyIndicatorDelay="0"
    visibleRowCount="6"
    rows="{
      path: 'CommentEntries',
      parameters: { '$$ownRequest': true },
      sorter: { path: 'CommentedAt', descending: true }
    }"
    noData="Nenhum comentário registrado nesta carga."
    >
    <t:extension>
      <OverflowToolbar>
        <content>
          <Title text="Comentários" />
          <ToolbarSpacer />
          <Button text="Incluir" icon="sap-icon://add" type="Transparent" press=".onAddComment" />
          <Button text="Editar" icon="sap-icon://edit" type="Transparent" press=".onEditComment" />
          <Button text="Remover" icon="sap-icon://delete" type="Transparent" press=".onRemoveComment" />
        </content>
      </OverflowToolbar>
    </t:extension>
    <t:columns>
      <!--
        Data/hora e autor da ULTIMA escrita: editar reescreve as duas colunas. A versao anterior
        fica no Log de Alteracoes.

        `targetType: 'any'` porque o datetime2 chega com 7 casas de fracao de segundo, que o
        DateTimeOffset do UI5 recusa.
      -->
      <t:Column label="Data/Hora" width="11rem">
        <t:template>
          <Text wrapping="false" text="{
            path: 'CommentedAt',
            targetType: 'any',
            formatter: '.formatter.formatDateTime'
          }" />
        </t:template>
      </t:Column>
      <t:Column label="Usuário" width="10rem">
        <t:template>
          <Text wrapping="false" text="{CommentedBy}" />
        </t:template>
      </t:Column>
      <!--
        `wrapping="false"`: sem isso um comentario longo quebra a linha e estoura a altura da
        sap.ui.table. O texto completo continua no tooltip, e a edicao abre no dialogo.
      -->
      <t:Column label="Comentário">
        <t:template>
          <Text text="{CommentText}" tooltip="{CommentText}" wrapping="false" />
        </t:template>
      </t:Column>
    </t:columns>
  </t:Table>
</core:FragmentDefinition>
```

- [ ] **Step 2: Criar `ShipmentLoadCommentDialog.fragment.xml`**

```xml
<core:FragmentDefinition
    xmlns="sap.m"
    xmlns:f="sap.ui.layout.form"
    xmlns:core="sap.ui.core"
>
<!--
  Inclusao e edicao de comentario. O campo escreve num BUFFER JSON (`viewModel>/commentDialog/...`),
  nunca no contexto OData: two-way binding num Detail deixa PATCH pendente no update group diferido
  e derruba o batch inteiro. Ao confirmar, o texto vai por action.
-->
<Dialog id="shipmentLoadCommentDialog" title="{viewModel>/commentDialog/title}">
  <content>
    <VBox class="sapUiSmallMargin" width="520px">
      <f:Form editable="true">
        <f:layout>
          <f:ColumnLayout columnsM="1" columnsL="1" columnsXL="1"/>
        </f:layout>
        <f:formContainers>
          <f:FormContainer>
            <f:formElements>
              <f:FormElement label="Comentário">
                <f:fields>
                  <TextArea
                    value="{viewModel>/commentDialog/text}"
                    rows="6"
                    width="100%"
                    growing="true"
                    maxLength="500"
                    placeholder="Anotação sobre a carga."
                  />
                </f:fields>
              </f:FormElement>
            </f:formElements>
          </f:FormContainer>
        </f:formContainers>
      </f:Form>
    </VBox>
  </content>
  <footer>
    <OverflowToolbar>
      <ToolbarSpacer />
      <Button text="Salvar" type="Emphasized" press=".onConfirmComment"/>
      <Button text="Fechar" press=".onCloseCommentDialog" />
    </OverflowToolbar>
  </footer>
</Dialog>
</core:FragmentDefinition>
```

- [ ] **Step 3: Criar `ShipmentLoadChangeLogs.fragment.xml`**

```xml
<core:FragmentDefinition
    xmlns="sap.m"
    xmlns:t="sap.ui.table"
    xmlns:core="sap.ui.core"
>
  <!--
    O que o usuario DIGITOU na carga: campos do cadastro, comentario e cancelamento. Nao confundir
    com a Movimentacao, que narra o que aconteceu com o SALDO (romaneio, faturamento, recusa,
    devolucao). Manter a fronteira e o que impede as duas grades de divergirem.

    Somente leitura: as linhas nascem nos services que fazem a alteracao.
  -->
  <t:Table
    id="shipmentLoadChangeLogsTable"
    class="sapUiSizeCondensed"
    alternateRowColors="true"
    enableBusyIndicator="true"
    enableSelectAll="false"
    selectionMode="None"
    busyIndicatorDelay="0"
    visibleRowCount="8"
    rows="{
      path: 'ChangeLogs',
      parameters: { '$$ownRequest': true },
      sorter: { path: 'ChangedAt', descending: true }
    }"
    noData="Nenhuma alteração registrada nesta carga."
    >
    <t:extension>
      <OverflowToolbar>
        <content>
          <Title text="Log de Alterações" />
          <ToolbarSpacer />
        </content>
      </OverflowToolbar>
    </t:extension>
    <t:columns>
      <t:Column label="Data/Hora" width="11rem">
        <t:template>
          <Text wrapping="false" text="{
            path: 'ChangedAt',
            targetType: 'any',
            formatter: '.formatter.formatDateTime'
          }" />
        </t:template>
      </t:Column>
      <t:Column label="Usuário" width="10rem">
        <t:template>
          <Text wrapping="false" text="{ChangedBy}" />
        </t:template>
      </t:Column>
      <t:Column label="Campo" width="14rem">
        <t:template>
          <Text wrapping="false" text="{
            path: 'Field',
            formatter: '.formatter.formatShipmentLoadChangeLogField'
          }" />
        </t:template>
      </t:Column>
      <t:Column label="De" width="24rem">
        <t:template>
          <Text text="{OldValue}" tooltip="{OldValue}" wrapping="false" />
        </t:template>
      </t:Column>
      <t:Column label="Para" width="24rem">
        <t:template>
          <Text text="{NewValue}" tooltip="{NewValue}" wrapping="false" />
        </t:template>
      </t:Column>
    </t:columns>
  </t:Table>
</core:FragmentDefinition>
```

- [ ] **Step 4: Acrescentar o formatter**

Em `webapp/model/formatter.ts`, logo depois de `formatShipmentLoadMovementType`:

```typescript
  /**
   * Rótulo do campo no log de alterações da carga. O backend grava o código
   * (ShipmentLoadChangeLogFields), não o texto - a tradução é aqui para não travar o i18n.
   * Código desconhecido cai para ele mesmo: linha antiga nunca fica em branco.
   */
  formatShipmentLoadChangeLogField: (value: string) => {
    const m = new Map<string, string>();
    // Singular: e a colecao de comentarios (CommentEntries). Nao confundir com "Comments"
    // abaixo, que e a observacao do cabecalho.
    m.set("Comment", "Comentário");
    m.set("Comments", "Observações");
    m.set("LoadDate", "Data");
    m.set("TruckCode", "Veículo");
    m.set("TruckDriver", "Motorista");
    m.set("Carrier", "Transportadora");
    m.set("CardCode", "Cliente");
    m.set("Warehouse", "Armazém");
    m.set("Item", "Produto");
    m.set("UnitOfMeasure", "Unidade");
    m.set("Branch", "Filial");
    m.set("HasExcess", "Excesso");
    m.set("FreightPrice", "Valor do frete");
    m.set("Status", "Situação");
    m.set("CancellationReason", "Motivo do cancelamento");

    return m.get(value) ?? value;
  },
```

- [ ] **Step 5: Acrescentar as duas seções em `Detail.view.xml`**

Depois da `<uxap:ObjectPageSection ... title="Movimentação">` que já existe e antes de `</uxap:sections>`:

```xml
			<uxap:ObjectPageSection titleUppercase="false" title="Comentários">
				<uxap:subSections>
					<uxap:ObjectPageSubSection>
						<uxap:blocks>
							<core:Fragment fragmentName="siagrob1.view.shipmentLoads.fragments.ShipmentLoadComments" type="XML"/>
						</uxap:blocks>
					</uxap:ObjectPageSubSection>
				</uxap:subSections>
			</uxap:ObjectPageSection>

			<uxap:ObjectPageSection titleUppercase="false" title="Log de Alterações">
				<uxap:subSections>
					<uxap:ObjectPageSubSection>
						<uxap:blocks>
							<core:Fragment fragmentName="siagrob1.view.shipmentLoads.fragments.ShipmentLoadChangeLogs" type="XML"/>
						</uxap:blocks>
					</uxap:ObjectPageSubSection>
				</uxap:subSections>
			</uxap:ObjectPageSection>
```

O namespace `core` já está declarado no topo da view.

- [ ] **Step 6: Rodar os gates do frontend**

```
yarn lint
yarn ts-typecheck
```

Esperado: sem erro novo. **Não rodar `yarn test`** — o gate de cobertura de 50% deste projeto nunca passa e não é sinal de nada.

- [ ] **Step 7: Stage**

```bash
git add webapp/view/shipmentLoads/fragments/ShipmentLoadComments.fragment.xml \
        webapp/view/shipmentLoads/fragments/ShipmentLoadCommentDialog.fragment.xml \
        webapp/view/shipmentLoads/fragments/ShipmentLoadChangeLogs.fragment.xml \
        webapp/view/shipmentLoads/Detail.view.xml \
        webapp/model/formatter.ts
```

---

### Task 8: Handlers de comentário no controller da carga

**Repo: `siagro-b1-frontend`.**

**Files:**
- Modify: `webapp/controller/shipmentLoads/Detail.controller.ts`

**Interfaces:**
- Consumes: as actions da Task 6; os ids de controle da Task 7; os modelos globais `viewModel` e `sessionModel` (já declarados em `webapp/manifest.json`); `DialogHelper.createDialog(controller, fragmentName)` e `DialogHelper.confirmDialog(text)`.
- Produces: os handlers públicos `onAddComment`, `onEditComment`, `onRemoveComment`, `onConfirmComment`, `onCloseCommentDialog`.

**Convenção local:** os controllers de `shipmentLoads/` chamam actions por **string literal inline** (`bindContext("/ShipmentLoadsCancel(...)")`), não por `this.api` — `ServerRoutes.ts` não tem nenhuma entrada de carga. Seguir a convenção local; **não** acrescentar rotas ao `ServerRoutes.ts` (a spec previa isso, mas a convenção do módulo prevalece).

- [ ] **Step 1: Acrescentar o campo e os helpers privados**

Ao fim da classe `Detail`, antes do `refreshAll` existente:

```typescript
  /* ------------------------------------------------------------------ */
  /* Comentários                                                         */
  /* ------------------------------------------------------------------ */

  private _commentDialog: Dialog;

  private selectedCommentContext(): Context | null {
    const table = this.byId("shipmentLoadCommentsTable") as Table;
    const selected = table.getSelectedIndex();

    if (selected < 0) {
      MessageBox.alert("Selecione um comentário.");
      return null;
    }

    return table.getContextByIndex(selected) as Context;
  }

  /**
   * Autor ou admin. A permissão é decidida no SERVIDOR; isto só evita a viagem inútil.
   */
  private canModifyComment(context: Context): boolean {
    const sessionModel = this.getModel("sessionModel") as JSONModel;

    if (sessionModel?.getProperty("/isAdmin") === true) {
      return true;
    }

    const userName = (sessionModel?.getProperty("/userName") as string) ?? "";
    const author = (context.getProperty("CommentedBy") as string) ?? "";

    return userName !== "" && author.toLowerCase() === userName.toLowerCase();
  }

  /**
   * O diálogo trabalha sobre um buffer JSON, nunca sobre o contexto OData: two-way binding num
   * Detail deixaria um PATCH pendente no update group diferido e derrubaria o batch inteiro.
   * `key` nulo significa inclusão.
   */
  private prepareCommentDialog(title: string, text: string, key: string): void {
    (this.getModel("viewModel") as JSONModel).setProperty("/commentDialog", {
      title,
      text: text ?? "",
      key,
    });
  }

  private async openCommentDialog(): Promise<void> {
    this._commentDialog ??= await DialogHelper.createDialog(
      this,
      "siagrob1.view.shipmentLoads.fragments.ShipmentLoadCommentDialog"
    );

    this._commentDialog.open();
  }

  /**
   * Recarrega a tabela de comentários (cache próprio, por `$$ownRequest`) e o log de alterações:
   * toda mutação de comentário grava linha no log.
   */
  private refreshCommentsList(): void {
    ["shipmentLoadCommentsTable", "shipmentLoadChangeLogsTable"].forEach(id => {
      const binding = (this.byId(id) as Table)?.getBinding("rows") as ODataListBinding;
      binding?.refresh();
    });
  }
```

- [ ] **Step 2: Acrescentar os handlers públicos**

Logo depois dos helpers:

```typescript
  async onAddComment(): Promise<void> {
    if (!this.getView().getBindingContext()) {
      MessageBox.alert("Carga não carregada.");
      return;
    }

    this.prepareCommentDialog("Novo Comentário", "", null);
    await this.openCommentDialog();
  }

  async onEditComment(): Promise<void> {
    const context = this.selectedCommentContext();

    if (!context) return;

    if (!this.canModifyComment(context)) {
      MessageBox.alert("Somente o autor do comentário pode alterá-lo.");
      return;
    }

    this.prepareCommentDialog(
      "Editar Comentário",
      context.getProperty("CommentText") as string,
      context.getProperty("Key") as string
    );

    await this.openCommentDialog();
  }

  onCloseCommentDialog(): void {
    this._commentDialog?.close();
  }

  async onConfirmComment(): Promise<void> {
    const viewModel = this.getModel("viewModel") as JSONModel;
    const text = ((viewModel.getProperty("/commentDialog/text") as string) ?? "").trim();

    if (text === "") {
      MessageBox.alert("Informe o texto do comentário.");
      return;
    }

    const commentKey = viewModel.getProperty("/commentDialog/key") as string;

    this.onCloseCommentDialog();
    this.setBusy(true);

    try {
      const model = this.getModel() as ODataModel;

      if (commentKey) {
        const action = model.bindContext("/ShipmentLoadsCommentUpdate(...)");
        action.setParameter("Key", commentKey);
        action.setParameter("Text", text);
        await action.invoke();
        MessageToast.show("Comentário alterado.");
      } else {
        const action = model.bindContext("/ShipmentLoadsCommentCreate(...)");
        action.setParameter("LoadKey", this._loadKey);
        action.setParameter("Text", text);
        await action.invoke();
        MessageToast.show("Comentário incluído.");
      }

      this.refreshCommentsList();
    } catch (e) {
      MessageBox.error((e as Error).message || "Erro ao gravar o comentário.");
    } finally {
      this.setBusy(false);
    }
  }

  async onRemoveComment(): Promise<void> {
    const context = this.selectedCommentContext();

    if (!context) return;

    if (!this.canModifyComment(context)) {
      MessageBox.alert("Somente o autor do comentário pode excluí-lo.");
      return;
    }

    if (!await DialogHelper.confirmDialog("Excluir o comentário selecionado ?")) return;

    this.setBusy(true);

    try {
      const action = (this.getModel() as ODataModel)
        .bindContext("/ShipmentLoadsCommentDelete(...)");
      action.setParameter("Key", context.getProperty("Key") as string);
      await action.invoke();

      MessageToast.show("Comentário excluído.");
      this.refreshCommentsList();
    } catch (e) {
      MessageBox.error((e as Error).message || "Erro ao excluir o comentário.");
    } finally {
      this.setBusy(false);
    }
  }
```

`this._loadKey` já existe na classe e é preenchido em `detailRouteMatched`. Os imports de `Dialog`, `MessageBox`, `MessageToast`, `JSONModel`, `Context`, `ODataListBinding`, `ODataModel`, `Table` e `DialogHelper` já estão todos no topo do arquivo — nenhum import novo é necessário.

- [ ] **Step 3: Incluir as duas tabelas novas no `refreshAll`**

No array de ids dentro de `refreshAll`, acrescentar as duas:

```typescript
    [
      "loadTransactionsTable",
      "loadInvoicesTable",
      "loadMovementsTable",
      "loadRefusalReturnsTable",
      "shipmentLoadCommentsTable",
      "shipmentLoadChangeLogsTable",
    ].forEach(id => {
```

Isso é o que faz o cancelamento (que grava duas linhas de log) aparecer na tela sem F5.

- [ ] **Step 4: Rodar os gates do frontend**

```
yarn lint
yarn ts-typecheck
```

Esperado: sem erro novo.

- [ ] **Step 5: Stage**

```bash
git add webapp/controller/shipmentLoads/Detail.controller.ts
```

---

### Task 9: Aplicar a migration e verificar pelo caminho do usuário

**Files:** nenhum arquivo de código. Esta task é a verificação — sem ela nada aqui está pronto.

**Interfaces:**
- Consumes: tudo das Tasks 1 a 8.
- Produces: a confirmação de que a feature funciona no navegador.

- [ ] **Step 1: Conferir o alvo da migration ANTES de escrever no banco**

```powershell
dotnet build
$env:ASPNETCORE_ENVIRONMENT="Yokotobi-Development"; dotnet ef migrations list --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web
```

A `AddShipmentLoadCommentsAndChangeLogs` deve aparecer como `(Pending)`. Se ela **não** aparecer na lista, o assembly está velho — rodar `dotnet build` de novo. Nunca rodar sem `ASPNETCORE_ENVIRONMENT` explícito: sem ele o alvo cai no primeiro launch profile, que é outro banco.

- [ ] **Step 2: Aplicar**

```powershell
$env:ASPNETCORE_ENVIRONMENT="Yokotobi-Development"; dotnet ef database update --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web
```

- [ ] **Step 3: Subir a stack**

Backend, dois processos, ambos com o profile `yktb`:

```
dotnet run --project SiagroB1.Web --launch-profile yktb
dotnet run --project SiagroB1.Gateway --launch-profile yktb
```

Frontend, no outro repo:

```
yarn start:dev
```

Login: `admin` / `1234`.

- [ ] **Step 4: Verificar pelo caminho do usuário**

Navegar pelo **menu**, não por URL direta. Montagem de Carga → abrir uma carga → conferir, na ordem:

1. As seções "Comentários" e "Log de Alterações" aparecem no fim da página.
2. Incluir um comentário: a linha aparece com o usuário `admin` e a data/hora de agora, e o Log ganha uma linha "Comentário" com "Para" preenchido e "De" vazio.
3. Editar o comentário: o texto e o carimbo mudam na linha, e o Log ganha uma linha com o texto antigo em "De".
4. Excluir: a linha some e o Log guarda o texto em "De" com "Para" vazio.
5. Editar a carga (tela de edição) mudando dois campos: o Log ganha **duas** linhas, uma por campo, com os rótulos em pt-BR — e a Movimentação ganha **uma** linha "Dados Alterados" com a frase de sempre.
6. Cancelar uma carga de teste: o Log ganha "Situação" e "Motivo do cancelamento".
7. Comentar numa carga **cancelada**: tem de funcionar. É a ausência deliberada de guarda de status.

Verificar o console do navegador a cada passo. Dois erros do ambiente Yokotobi **não** são desta feature e devem ser ignorados: value help do SAP devolvendo 500, e a tela de Liberações estourando 30s.

- [ ] **Step 5: Derrubar a stack**

Encerrar os três processos ao terminar.

- [ ] **Step 6: Conferir o staging dos dois repos**

```bash
git -C "C:/Projetos/SiagroB1/siagro-b1-backend" status --short
git -C "C:/Projetos/SiagroB1/siagro-b1-frontend" status --short
```

Nenhum arquivo novo pode aparecer como `??`. **Nada de `git commit`** — os commits são do usuário.

---

## Notas de auto-revisão

Divergências deliberadas entre este plano e a spec, resolvidas a favor da convenção do código:

- **`ServerRoutes.ts` não é tocado.** A spec previa três rotas novas lá; o módulo `shipmentLoads` chama actions por string literal inline e não tem nenhuma entrada naquele arquivo. Seguir o módulo.
- **`UnitOfMeasure` entra na comparação; `TruckDriverCode` e `CarrierCardCode` não.** A spec falava dos três; incluir os códigos de motorista e transportadora duplicaria na frase da narrativa o que o nome desnormalizado já diz. O código da unidade não tem nome desnormalizado equivalente, então entra.
- **`ShipmentLoadChangeLogFields` é uma classe nova, não uma extensão de `ContractChangeLogFields`.** Só a constante `Comment` é compartilhada por valor (`"Comment"`), o que basta para o reuso de `ContractCommentRules`. Os rótulos da carga não teriam sentido no formatter dos contratos.
