# Minutas de contrato — Fase 1 (backend: modelos, signatários, minutas e PDF)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** o usuário cadastra modelos de contrato com placeholders e signatários (da empresa e do parceiro), cria minutas de contratos de compra/venda a partir de um modelo, edita o texto enquanto rascunho e baixa o PDF gerado por Chromium. Nenhum provedor de assinatura ainda — é a Fase 2 (`2026-09-21-contract-drafts-phase2-esignature.md`, a escrever depois desta).

**Architecture:** quatro tabelas novas no `AppDbContext` (`CONTRACT_TEMPLATES`, `COMPANY_SIGNATORIES`, `BUSINESS_PARTNER_SIGNATORIES`, `CONTRACT_DRAFTS`) mais `CONTRACT_DRAFT_SIGNERS` (só criada; preenchida na Fase 2). Renderização = função pura (`{{placeholder}}` → valor formatado) sobre um dicionário resolvido por `ContractDraftPlaceholderResolver`, que passa por `IBusinessPartnerService` para funcionar nos dois modos ERP. A minuta guarda o HTML renderizado e o JSON dos valores como snapshot. PDF por `IHtmlToPdfRenderer` (PuppeteerSharp) no processo do `SiagroB1.Web`. Um serviço por operação, actions/functions OData um por arquivo, registro manual no DI.

**Tech Stack:** .NET 10, EF Core 10 (SQL Server), ASP.NET Core OData 9, PuppeteerSharp, xUnit + EF InMemory.

**Spec:** `docs/superpowers/specs/2026-09-21-contract-drafts-esignature-design.md` (leia antes da Task 1; o plano argumenta a partir dela).

## Global Constraints

- **Identificadores de código em inglês; texto que o usuário lê em pt-BR.** Classes, enums, tabelas, colunas, serviços: inglês. Mensagens de negócio, rótulos, comentários explicativos: pt-BR. **Exceção deliberada da spec:** os nomes dos placeholders (`{{fornecedor_cnpj}}`) são pt-BR porque são texto digitado pelo usuário.
- **Todo arquivo novo é staged imediatamente** com `git add <caminho>`.
- **Nunca `git push`.** Commit por task, no branch `feature/contract-drafts-esignature`.
- **Mensagem de commit:** `tipo(escopo): descrição em pt-BR, imperativo, minúscula, sem ponto final`. Scope deste trabalho: `platform` (subsistema transversal a compra e venda). **Rodapé `DB: <NomeDaMigration>` obrigatório em todo commit que contenha migration.** Rodapé `Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>`.
- **Valor novo de enum entra sempre no fim da numeração.**
- **Ambiente explícito em todo comando de banco:** `$env:ASPNETCORE_ENVIRONMENT = 'Yokotobi-Development'` antes de `dotnet ef database update`. Scaffold: `dotnet ef migrations add <Nome> --project SiagroB1.Migrations --startup-project SiagroB1.Web --context AppDbContext` (ou `--context CommonDbContext` para menus).
- **Controller de action OData:** `ODataActionParameters` chega **nulo** quando nenhum parâmetro é enviado; parâmetro string é anulável (`TryGetValue` devolve `true` com `null`). Enums e datas viajam como string.
- **Escada de exceções nos controllers:** `NotFoundException`/`KeyNotFoundException` → 404; `DefaultException`/`BusinessException`/`ApplicationException` → 400; resto → 500.
- **Serviços "enqueue-only"** (`*ChangeLogService.Register`, `*SetSignatureStatusService`) são chamados **antes** do `SaveChangesAsync` do serviço da operação.
- **Sem `IOptions<T>`:** configuração lida inline com `IConfiguration["Signature:..."]`, como o resto do solution.
- **Teste que semeia com um `AppDbContext` e exercita o serviço com outro** apontando para o mesmo nome de banco InMemory quando o serviço depende de `Include` — reusar o mesmo contexto esconde `Include` faltando.
- Build: `dotnet build SiagroB1.sln`. Testes: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~ContractDrafts"` (ou o filtro da task).

---

## Ordem e dependências

1 → 2 → 3 → 4 → 5 → 6 → 7 → 8. A Task 2 (renderer puro) e a 5 (PDF) não dependem do banco e podem ser feitas em paralelo com 1/3/4 por outro executor; a 6 depende de 1, 2, 3 e 5; a 7 depende de 4 e 6; a 8 é independente (só migration de menu).

---

### Task 1: Modelo — enums, entidades, DbSets, mapeamento e migration

**Files:**
- Create: `SiagroB1.Domain/Enums/ContractTemplateScope.cs`, `SiagroB1.Domain/Enums/SignatoryRole.cs`, `SiagroB1.Domain/Enums/ContractDraftType.cs`, `SiagroB1.Domain/Enums/ContractDraftStatus.cs`, `SiagroB1.Domain/Enums/SignerSide.cs`, `SiagroB1.Domain/Enums/SignerStatus.cs`
- Create: `SiagroB1.Domain/Entities/ContractTemplate.cs`, `CompanySignatory.cs`, `BusinessPartnerSignatory.cs`, `ContractDraft.cs`, `ContractDraftSigner.cs`
- Modify: `SiagroB1.Domain/Entities/PurchaseContract.cs` e `SalesContract.cs` (coleção `Drafts`)
- Modify: `SiagroB1.Domain/Entities/ContractChangeLogFields.cs` (constante `Draft`)
- Modify: `SiagroB1.Infra/Context/AppDbContext.cs` (DbSets + bloco fluente)
- Modify: `SiagroB1.Infra/Interceptors/FinishedContractMutationGuardInterceptor.cs` (comentário no `switch`)
- Create: `SiagroB1.Migrations/AppContext/<timestamp>_AddContractDraftsAndTemplates.cs` (scaffold)
- Test: `SiagroB1.Application.Tests/ContractDrafts/ContractDraftModelTests.cs`

**Interfaces:**
- Produces: as entidades e enums abaixo, com estes nomes exatos; `AppDbContext.ContractTemplates`, `.CompanySignatories`, `.BusinessPartnerSignatories`, `.ContractDrafts`, `.ContractDraftSigners`; `ContractChangeLogFields.Draft` e `DescribeDraft(int, string)`.

- [ ] **Step 1: Enums**

```csharp
// SiagroB1.Domain/Enums/ContractTemplateScope.cs
namespace SiagroB1.Domain.Enums;

/// <summary>Em qual tipo de contrato o modelo pode ser usado.</summary>
public enum ContractTemplateScope { Purchase = 0, Sales = 1, Both = 2 }
```

```csharp
// SiagroB1.Domain/Enums/SignatoryRole.cs
namespace SiagroB1.Domain.Enums;

/// <summary>
/// Papel do signatário. Espelha os 13 atos do D4Sign para nada se perder na migração da Tagui,
/// mas os valores são nossos — o código <c>act</c> do provedor é mapeado dentro do provider.
/// </summary>
public enum SignatoryRole
{
    Sign = 1,
    Approve = 2,
    Acknowledge = 3,
    SignAsParty = 4,
    SignAsWitness = 5,
    SignAsIntervening = 6,
    AcknowledgeReceipt = 7,
    SignAsIssuerEndorserGuarantor = 8,
    SignAsIssuerEndorserGuarantorSurety = 9,
    SignAsSurety = 10,
    SignAsPartyAndSurety = 11,
    SignAsJointDebtor = 12,
    SignAsPartyAndJointDebtor = 13,
}
```

```csharp
// SiagroB1.Domain/Enums/ContractDraftType.cs
namespace SiagroB1.Domain.Enums;

/// <summary>Contrato, aditivo ou distrato. Cessão de crédito (Tagui) não existe no SiagroB1.</summary>
public enum ContractDraftType { Contract = 0, Amendment = 1, Termination = 2 }
```

```csharp
// SiagroB1.Domain/Enums/ContractDraftStatus.cs
namespace SiagroB1.Domain.Enums;

/// <summary>
/// Ciclo da minuta. Só <see cref="Draft"/> aceita edição/exclusão. Os demais valores são
/// escritos pela Fase 2 (envio, webhook, reconciliação) — existem já para a coluna nascer completa.
/// </summary>
public enum ContractDraftStatus
{
    Draft = 0,
    AwaitingSignature = 1,
    PartiallySigned = 2,
    Signed = 3,
    Canceled = 4,
}
```

```csharp
// SiagroB1.Domain/Enums/SignerSide.cs
namespace SiagroB1.Domain.Enums;

public enum SignerSide { Company = 0, Partner = 1 }
```

```csharp
// SiagroB1.Domain/Enums/SignerStatus.cs
namespace SiagroB1.Domain.Enums;

public enum SignerStatus { Pending = 0, Signed = 1, EmailFailed = 2 }
```

- [ ] **Step 2: Entidades**

```csharp
// SiagroB1.Domain/Entities/ContractTemplate.cs
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Shared.Base;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Modelo de contrato: um único texto HTML com <c>{{placeholders}}</c>, editado pelo usuário na
/// tela. Não há cláusulas em tabela separada — a Tagui tentou e removeu no release 7.8.
/// Sem BranchCode: é documentação da empresa, não documento de filial.
/// </summary>
[Table("CONTRACT_TEMPLATES")]
[Index(nameof(Name), IsUnique = true)]
public class ContractTemplate : BaseEntity
{
    [Column(TypeName = "VARCHAR(100) NOT NULL")]
    public required string Name { get; set; }

    /// <summary>Título do documento; vira o nome do arquivo no provedor de assinatura.</summary>
    [Column(TypeName = "VARCHAR(200) NOT NULL")]
    public required string Title { get; set; }

    public ContractTemplateScope ContractType { get; set; } = ContractTemplateScope.Both;

    [Column(TypeName = "VARCHAR(MAX) NOT NULL")]
    public required string BodyHtml { get; set; }

    /// <summary>Inativo não aparece para criar minuta; minutas antigas continuam apontando para ele.</summary>
    public bool Active { get; set; } = true;
}
```

```csharp
// SiagroB1.Domain/Entities/CompanySignatory.cs
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Shared.Base;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Quem assina pela empresa. <see cref="BranchCode"/> nulo = assina por todas as filiais.
/// E-mail único: é a chave pela qual o webhook do provedor identifica quem assinou.
/// </summary>
[Table("COMPANY_SIGNATORIES")]
[Index(nameof(Email), IsUnique = true)]
public class CompanySignatory : BaseEntity
{
    [Column(TypeName = "VARCHAR(14)")]
    public string? BranchCode { get; set; }
    public virtual Branch? Branch { get; set; }

    [Column(TypeName = "VARCHAR(100) NOT NULL")]
    public required string Name { get; set; }

    /// <summary>CPF, só dígitos.</summary>
    [Column(TypeName = "VARCHAR(14) NOT NULL")]
    public required string TaxId { get; set; }

    [Column(TypeName = "VARCHAR(200) NOT NULL")]
    public required string Email { get; set; }

    public SignatoryRole Role { get; set; } = SignatoryRole.SignAsParty;

    /// <summary>Ordem no bloco de assinaturas e no envio ao provedor.</summary>
    public int Order { get; set; }

    public bool Active { get; set; } = true;
}
```

```csharp
// SiagroB1.Domain/Entities/BusinessPartnerSignatory.cs
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Shared.Base;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Quem assina pelo parceiro. <see cref="CardCode"/> SEM FK e sem navegação: em modo SAPB1 a
/// tabela local BUSINESS_PARTNERS está vazia (mesma razão de <see cref="SalesContract.CardFName"/>).
/// A mesma pessoa pode assinar por dois parceiros, mas não duas vezes pelo mesmo.
/// </summary>
[Table("BUSINESS_PARTNER_SIGNATORIES")]
[Index(nameof(CardCode))]
[Index(nameof(CardCode), nameof(Email), IsUnique = true)]
public class BusinessPartnerSignatory : BaseEntity
{
    [Column(TypeName = "VARCHAR(15) NOT NULL")]
    public required string CardCode { get; set; }

    [Column(TypeName = "VARCHAR(100) NOT NULL")]
    public required string Name { get; set; }

    [Column(TypeName = "VARCHAR(14) NOT NULL")]
    public required string TaxId { get; set; }

    [Column(TypeName = "VARCHAR(200) NOT NULL")]
    public required string Email { get; set; }

    public SignatoryRole Role { get; set; } = SignatoryRole.SignAsParty;

    public int Order { get; set; }

    public bool Active { get; set; } = true;
}
```

```csharp
// SiagroB1.Domain/Entities/ContractDraft.cs
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Shared.Base;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Minuta: fotografia do contrato no momento da criação (<see cref="BodyHtml"/> renderizado e
/// <see cref="PlaceholdersJson"/> com os valores que entraram). Se o contrato mudar depois, a
/// minuta não muda — o usuário cria outra. Uma tabela para compra e venda: exatamente uma das
/// duas FKs é preenchida (check constraint <c>CK_CONTRACT_DRAFTS_ONE_CONTRACT</c>).
/// </summary>
[Table("CONTRACT_DRAFTS")]
[Index(nameof(PurchaseContractKey))]
[Index(nameof(SalesContractKey))]
public class ContractDraft : DocumentEntity
{
    public Guid? PurchaseContractKey { get; set; }
    public virtual PurchaseContract? PurchaseContract { get; set; }

    public Guid? SalesContractKey { get; set; }
    public virtual SalesContract? SalesContract { get; set; }

    /// <summary>Snapshot do código do contrato, para listar sem join.</summary>
    [Column(TypeName = "VARCHAR(50) NOT NULL")]
    public required string ContractCode { get; set; }

    /// <summary>1, 2, 3… por contrato. A tela exibe "Minuta {Sequence}".</summary>
    public int Sequence { get; set; }

    public Guid TemplateKey { get; set; }
    public virtual ContractTemplate? Template { get; set; }

    public ContractDraftType DraftType { get; set; } = ContractDraftType.Contract;

    [Column(TypeName = "VARCHAR(200) NOT NULL")]
    public required string Description { get; set; }

    [Column(TypeName = "VARCHAR(MAX) NOT NULL")]
    public required string BodyHtml { get; set; }

    [Column(TypeName = "VARCHAR(MAX) NOT NULL")]
    public required string PlaceholdersJson { get; set; }

    public ContractDraftStatus Status { get; set; } = ContractDraftStatus.Draft;

    // ---- preenchidos pela Fase 2 (assinatura eletrônica) ----

    [Column(TypeName = "VARCHAR(20)")]
    public string? Provider { get; set; }

    /// <summary>uuid do documento no provedor; único porque o webhook procura por ele.</summary>
    [Column(TypeName = "VARCHAR(100)")]
    public string? ExternalDocumentId { get; set; }

    public DateTime? SentAt { get; set; }
    public DateTime? SignedAt { get; set; }

    [Column(TypeName = "VARCHAR(1000)")]
    public string? LastError { get; set; }

    /// <summary>Anexo do contrato onde o PDF assinado foi guardado.</summary>
    public Guid? SignedAttachmentKey { get; set; }

    public virtual ICollection<ContractDraftSigner> Signers { get; set; } = [];

    [NotMapped]
    public bool IsPurchase => PurchaseContractKey.HasValue;
}
```

```csharp
// SiagroB1.Domain/Entities/ContractDraftSigner.cs
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Snapshot de um signatário no momento do envio (Fase 2). POCO como os anexos: não é entidade
/// de negócio com auditoria própria. Criada já na Fase 1 para a migration nascer completa.
/// </summary>
[Table("CONTRACT_DRAFT_SIGNERS")]
[Index(nameof(DraftKey))]
public class ContractDraftSigner
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid Key { get; set; }

    public Guid DraftKey { get; set; }
    public virtual ContractDraft? Draft { get; set; }

    public SignerSide Side { get; set; }

    [Column(TypeName = "VARCHAR(100) NOT NULL")]
    public required string Name { get; set; }

    [Column(TypeName = "VARCHAR(14) NOT NULL")]
    public required string TaxId { get; set; }

    [Column(TypeName = "VARCHAR(200) NOT NULL")]
    public required string Email { get; set; }

    public SignatoryRole Role { get; set; }

    public int Order { get; set; }

    public SignerStatus Status { get; set; } = SignerStatus.Pending;

    public DateTime? SignedAt { get; set; }

    [Column(TypeName = "VARCHAR(500)")]
    public string? LastMessage { get; set; }
}
```

- [ ] **Step 3: Navegações nos contratos e código do change log**

Em `PurchaseContract.cs`, ao lado de `public ICollection<PurchaseContractWashout> Washouts { get; set; } = [];`:

```csharp
    /// <summary>Minutas do contrato (documentação, como anexos — fora do guard de contrato encerrado).</summary>
    public ICollection<ContractDraft> Drafts { get; set; } = [];
```

Em `SalesContract.cs`, ao lado de `Attachments`:

```csharp
    public ICollection<ContractDraft> Drafts { get; set; } = [];
```

Em `ContractChangeLogFields.cs`, após `WithoutTaxDocument`:

```csharp
    /// <summary>Ciclo da minuta: "Minuta N criada / enviada para assinatura / assinada / cancelada".</summary>
    public const string Draft = "Draft";

    /// <summary>Como a minuta aparece no log. Sequência sempre, porque um contrato tem várias.</summary>
    public static string DescribeDraft(int sequence, string what) => $"Minuta {sequence} {what}";
```

- [ ] **Step 4: DbSets e mapeamento fluente**

Em `AppDbContext.cs`, ao lado dos DbSets de anexos:

```csharp
    public DbSet<ContractTemplate> ContractTemplates { get; set; }
    public DbSet<CompanySignatory> CompanySignatories { get; set; }
    public DbSet<BusinessPartnerSignatory> BusinessPartnerSignatories { get; set; }
    public DbSet<ContractDraft> ContractDrafts { get; set; }
    public DbSet<ContractDraftSigner> ContractDraftSigners { get; set; }
```

No `OnModelCreating`, depois do bloco de washout (linha ~340):

```csharp
        // Minutas (spec 2026-09-21). Duas FKs anuláveis para o contrato de compra e de venda,
        // exatamente uma preenchida: uma tabela só evita duplicar cada serviço como a Tagui fez.
        // NoAction nas duas — o contrato não some debaixo da minuta (anexo assinado aponta pra ele).
        modelBuilder.Entity<ContractDraft>()
            .HasOne(x => x.PurchaseContract).WithMany(x => x.Drafts)
            .HasForeignKey(x => x.PurchaseContractKey).OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<ContractDraft>()
            .HasOne(x => x.SalesContract).WithMany(x => x.Drafts)
            .HasForeignKey(x => x.SalesContractKey).OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<ContractDraft>()
            .HasOne(x => x.Template).WithMany()
            .HasForeignKey(x => x.TemplateKey).OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<ContractDraft>()
            .ToTable(t => t.HasCheckConstraint(
                "CK_CONTRACT_DRAFTS_ONE_CONTRACT",
                "([PurchaseContractKey] IS NULL) <> ([SalesContractKey] IS NULL)"));

        // O webhook do provedor procura a minuta pelo id externo: único enquanto preenchido.
        modelBuilder.Entity<ContractDraft>()
            .HasIndex(x => x.ExternalDocumentId)
            .IsUnique()
            .HasFilter("[ExternalDocumentId] IS NOT NULL");

        // Sequência é por contrato; o índice cobre os dois lados porque a outra FK é nula.
        modelBuilder.Entity<ContractDraft>()
            .HasIndex(x => new { x.PurchaseContractKey, x.SalesContractKey, x.Sequence })
            .IsUnique();

        modelBuilder.Entity<ContractDraftSigner>()
            .HasOne(x => x.Draft).WithMany(x => x.Signers)
            .HasForeignKey(x => x.DraftKey).OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<CompanySignatory>()
            .HasOne(x => x.Branch).WithMany()
            .HasForeignKey(x => x.BranchCode).OnDelete(DeleteBehavior.NoAction);
```

- [ ] **Step 5: Guard de contrato encerrado — decisão explícita**

Em `FinishedContractMutationGuardInterceptor.cs`, dentro do `switch` de `CollectContractKeys`, junto dos comentários que excluem anexos e comentários, adicione um comentário (sem `case`):

```csharp
            // ContractDraft / ContractDraftSigner ficam FORA de propósito: minuta é documentação,
            // como anexo — um contrato encerrado pode ter aditivo ou distrato (spec 2026-09-21).
```

- [ ] **Step 6: Teste do modelo**

```csharp
// SiagroB1.Application.Tests/ContractDrafts/ContractDraftModelTests.cs
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.ContractDrafts;

/// <summary>O modelo EF das minutas: FKs sem cascata, índice único filtrado e check constraint.</summary>
public class ContractDraftModelTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    [Fact]
    public void Draft_foreign_keys_do_not_cascade()
    {
        var entity = _db.Context.Model.FindEntityType(typeof(ContractDraft))!;

        foreach (var fk in entity.GetForeignKeys())
            Assert.Equal(DeleteBehavior.NoAction, fk.DeleteBehavior);
    }

    [Fact]
    public void External_document_id_is_unique_while_present()
    {
        var index = _db.Context.Model.FindEntityType(typeof(ContractDraft))!
            .GetIndexes().Single(i => i.Properties.Count == 1
                && i.Properties[0].Name == nameof(ContractDraft.ExternalDocumentId));

        Assert.True(index.IsUnique);
        Assert.Equal("[ExternalDocumentId] IS NOT NULL", index.GetFilter());
    }

    [Fact]
    public void Exactly_one_contract_is_enforced_by_check_constraint()
    {
        var entity = _db.Context.Model.FindEntityType(typeof(ContractDraft))!;

        Assert.Contains(entity.GetCheckConstraints(), c => c.Name == "CK_CONTRACT_DRAFTS_ONE_CONTRACT");
    }

    [Fact]
    public void Signatory_emails_are_unique()
    {
        var company = _db.Context.Model.FindEntityType(typeof(CompanySignatory))!;
        var partner = _db.Context.Model.FindEntityType(typeof(BusinessPartnerSignatory))!;

        Assert.Contains(company.GetIndexes(), i => i.IsUnique && i.Properties.Count == 1 && i.Properties[0].Name == "Email");
        Assert.Contains(partner.GetIndexes(), i => i.IsUnique
            && i.Properties.Select(p => p.Name).SequenceEqual(["CardCode", "Email"]));
    }
}
```

- [ ] **Step 7: Rodar os testes**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~ContractDraftModelTests"`
Expected: PASS (4 testes). Antes dos Steps 1–5 o projeto nem compila — é o "vermelho" desta task.

- [ ] **Step 8: Migration**

```powershell
dotnet ef migrations add AddContractDraftsAndTemplates --project SiagroB1.Migrations --startup-project SiagroB1.Web --context AppDbContext
```

Abra o arquivo gerado e confira: 5 `CreateTable`, check constraint `CK_CONTRACT_DRAFTS_ONE_CONTRACT`, índice filtrado em `ExternalDocumentId`, nenhum `onDelete: Cascade`. Adicione o comentário `/// Minutas de contrato, modelos e signatários (spec 2026-09-21, fase 1).` acima da classe. Aplique em homologação:

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Yokotobi-Development'
dotnet ef database update --project SiagroB1.Migrations --startup-project SiagroB1.Web --context AppDbContext
```

- [ ] **Step 9: Build completo e suíte inteira**

Run: `dotnet build SiagroB1.sln` e `dotnet test SiagroB1.Application.Tests`
Expected: 0 erros; todos os testes existentes continuam verdes (`AppDbContextModelTests` inclusive).

- [ ] **Step 10: Commit**

```bash
git add SiagroB1.Domain SiagroB1.Infra SiagroB1.Migrations SiagroB1.Application.Tests/ContractDrafts
git commit -m "feat(platform): criar modelo de minutas, modelos de contrato e signatários

Primeira fase das minutas (spec 2026-09-21). Uma tabela de minutas para compra
e venda com duas FKs anuláveis e check constraint, em vez de duplicar serviços.
Signatários da empresa e do parceiro em tabelas próprias, e-mail único porque
é a chave de correlação do webhook na fase 2.

DB: AddContractDraftsAndTemplates

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 2: Renderer puro e catálogo de placeholders

**Files:**
- Create: `SiagroB1.Application/Services/ContractDrafts/ContractDraftPlaceholderCatalog.cs`
- Create: `SiagroB1.Application/Services/ContractDrafts/ContractDraftTemplateRenderer.cs`
- Create: `SiagroB1.Domain/Dtos/ContractDraftPlaceholderDto.cs`
- Test: `SiagroB1.Application.Tests/ContractDrafts/ContractDraftTemplateRendererTests.cs`

**Interfaces:**
- Produces: `ContractDraftPlaceholderCatalog.For(ContractTemplateScope scope) : IReadOnlyList<ContractDraftPlaceholderDto>`; `ContractDraftPlaceholderCatalog.NamesFor(scope) : IReadOnlySet<string>`; `ContractDraftTemplateRenderer.Render(string html, IReadOnlyDictionary<string,string> values) : string`; `ContractDraftTemplateRenderer.FindUnknown(string html, IReadOnlySet<string> known) : IReadOnlyList<string>`; `ContractDraftTemplateRenderer.UnknownPlaceholdersMessage(IEnumerable<string>)`.

- [ ] **Step 1: Teste do renderer (falha: tipos não existem)**

```csharp
// SiagroB1.Application.Tests/ContractDrafts/ContractDraftTemplateRendererTests.cs
using SiagroB1.Application.Services.ContractDrafts;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Tests.ContractDrafts;

/// <summary>Renderização é função pura: HTML + dicionário → HTML. Sem banco.</summary>
public class ContractDraftTemplateRendererTests
{
    private static readonly Dictionary<string, string> Values = new()
    {
        ["numero"] = "PC-000123",
        ["fornecedor_cnpj"] = "12.345.678/0001-90",
    };

    [Fact]
    public void Replaces_placeholders_and_tolerates_inner_spaces()
    {
        var html = "<p>Contrato {{numero}} — CNPJ {{ fornecedor_cnpj }}</p>";

        var result = ContractDraftTemplateRenderer.Render(html, Values);

        Assert.Equal("<p>Contrato PC-000123 — CNPJ 12.345.678/0001-90</p>", result);
    }

    [Fact]
    public void Html_without_placeholders_passes_through_untouched()
    {
        const string html = "<table><tr><td>fixo</td></tr></table>";

        Assert.Equal(html, ContractDraftTemplateRenderer.Render(html, Values));
    }

    [Fact]
    public void Unknown_placeholders_fail_listing_every_name_once()
    {
        var html = "{{numero}} {{xpto}} {{outro}} {{xpto}}";

        var ex = Assert.Throws<BusinessException>(() => ContractDraftTemplateRenderer.Render(html, Values));

        Assert.Contains("xpto", ex.Message);
        Assert.Contains("outro", ex.Message);
        Assert.Equal(1, ex.Message.Split("xpto").Length - 1);
    }

    [Fact]
    public void Find_unknown_uses_the_catalog_names()
    {
        var known = ContractDraftPlaceholderCatalog.NamesFor(ContractTemplateScope.Purchase);

        var unknown = ContractDraftTemplateRenderer.FindUnknown(
            "{{numero}} {{cliente_cnpj}} {{fornecedor_cnpj}}", known);

        Assert.Equal(["cliente_cnpj"], unknown);
    }

    [Fact]
    public void Catalog_scopes_are_consistent()
    {
        var purchase = ContractDraftPlaceholderCatalog.NamesFor(ContractTemplateScope.Purchase);
        var sales = ContractDraftPlaceholderCatalog.NamesFor(ContractTemplateScope.Sales);
        var both = ContractDraftPlaceholderCatalog.NamesFor(ContractTemplateScope.Both);

        Assert.Contains("fornecedor_cnpj", purchase);
        Assert.DoesNotContain("fornecedor_cnpj", sales);
        Assert.Contains("cliente_cnpj", sales);
        Assert.Contains("corretor_nome", purchase);
        Assert.DoesNotContain("corretor_nome", sales);
        // "Both" só aceita o que existe nos dois: modelo compartilhado não pode citar lado que não há.
        Assert.True(both.IsSubsetOf(purchase) && both.IsSubsetOf(sales));
        Assert.Contains("assinaturas_parceiro", both);
    }
}
```

- [ ] **Step 2: Rodar — deve falhar por compilação**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~ContractDraftTemplateRendererTests"`
Expected: erro de compilação (`ContractDraftTemplateRenderer` não existe).

- [ ] **Step 3: DTO e catálogo**

```csharp
// SiagroB1.Domain/Dtos/ContractDraftPlaceholderDto.cs
namespace SiagroB1.Domain.Dtos;

/// <summary>Um placeholder disponível no editor de modelos: nome (como se digita) e descrição pt-BR.</summary>
public record ContractDraftPlaceholderDto(string Name, string Description);
```

```csharp
// SiagroB1.Application/Services/ContractDrafts/ContractDraftPlaceholderCatalog.cs
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Services.ContractDrafts;

/// <summary>
/// Catálogo dos placeholders que um modelo pode usar. Os nomes são pt-BR de propósito: é texto
/// que o usuário digita no editor, e coincidem com os da Tagui para a migração dos modelos ser um
/// replace de <c>$x</c> por <c>{{x}}</c>. Quem preenche os valores é
/// <see cref="ContractDraftPlaceholderResolver"/>; os dois têm de andar juntos — o teste
/// <c>Resolver_covers_every_catalog_name</c> cobra isso.
/// </summary>
public static class ContractDraftPlaceholderCatalog
{
    private static readonly ContractDraftPlaceholderDto[] Common =
    [
        new("numero", "Número do contrato"),
        new("complemento", "Complemento do contrato"),
        new("emissao", "Data de emissão (dd/mm/aaaa)"),
        new("emissao_extenso", "Data de emissão por extenso"),
        new("data_inicio_entrega", "Início da entrega"),
        new("data_termino_entrega", "Término da entrega"),
        new("representante_nome", "Nome do representante"),
        new("local_entrega", "Local(is) de entrega"),
        new("produto_descricao", "Descrição do produto"),
        new("safra_descricao", "Safra"),
        new("quantidade", "Quantidade contratada"),
        new("unidade_medida", "Unidade de medida"),
        new("preco", "Preço unitário"),
        new("moeda", "Moeda (R$ / US$)"),
        new("valor_total", "Valor total"),
        new("valor_total_extenso", "Valor total por extenso"),
        new("tipo_frete", "Tipo de frete (CIF / FOB / Terceiro / Nenhum)"),
        new("condicao_pagamento", "Condição de pagamento"),
        new("data_pagamento", "Data de pagamento"),
        new("empresa_razao_social", "Razão social da empresa"),
        new("empresa_cnpj", "CNPJ da filial"),
        new("filial_nome", "Nome da filial"),
        new("assinaturas_empresa", "Bloco de assinaturas da empresa"),
        new("assinaturas_parceiro", "Bloco de assinaturas do parceiro"),
    ];

    private static readonly ContractDraftPlaceholderDto[] PurchaseOnly =
    [
        new("corretor_nome", "Nome do corretor"),
        new("corretor_comissao", "Comissão do corretor"),
        new("fornecedor_razao_social", "Razão social do fornecedor"),
        new("fornecedor_nome_fantasia", "Nome fantasia do fornecedor"),
        new("fornecedor_cnpj", "CNPJ/CPF do fornecedor"),
        new("fornecedor_endereco", "Endereço do fornecedor"),
        new("fornecedor_bairro", "Bairro do fornecedor"),
        new("fornecedor_cep", "CEP do fornecedor"),
        new("fornecedor_cidade", "Cidade do fornecedor"),
        new("fornecedor_uf", "UF do fornecedor"),
    ];

    private static readonly ContractDraftPlaceholderDto[] SalesOnly =
    [
        new("cliente_razao_social", "Razão social do cliente"),
        new("cliente_nome_fantasia", "Nome fantasia do cliente"),
        new("cliente_cnpj", "CNPJ/CPF do cliente"),
        new("cliente_endereco", "Endereço do cliente"),
        new("cliente_bairro", "Bairro do cliente"),
        new("cliente_cep", "CEP do cliente"),
        new("cliente_cidade", "Cidade do cliente"),
        new("cliente_uf", "UF do cliente"),
    ];

    public static IReadOnlyList<ContractDraftPlaceholderDto> For(ContractTemplateScope scope) => scope switch
    {
        ContractTemplateScope.Purchase => [.. Common, .. PurchaseOnly],
        ContractTemplateScope.Sales => [.. Common, .. SalesOnly],
        _ => Common,
    };

    public static IReadOnlySet<string> NamesFor(ContractTemplateScope scope) =>
        For(scope).Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
}
```

- [ ] **Step 4: Renderer**

```csharp
// SiagroB1.Application/Services/ContractDrafts/ContractDraftTemplateRenderer.cs
using System.Text.RegularExpressions;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Services.ContractDrafts;

/// <summary>
/// Substitui <c>{{nome}}</c> pelo valor do dicionário. Função pura, sem banco, testável sozinha.
/// Placeholder desconhecido é erro de negócio com a lista completa: contrato com <c>{{x}}</c> no
/// meio não pode sair silenciosamente. A mesma checagem roda ao salvar o modelo, para o erro
/// aparecer na edição e não na hora de gerar a minuta.
/// </summary>
public static partial class ContractDraftTemplateRenderer
{
    [GeneratedRegex(@"\{\{\s*([a-z0-9_]+)\s*\}\}", RegexOptions.CultureInvariant)]
    private static partial Regex Placeholder();

    public static string Render(string html, IReadOnlyDictionary<string, string> values)
    {
        var unknown = FindUnknown(html, values.Keys.ToHashSet(StringComparer.Ordinal));
        if (unknown.Count > 0)
            throw new BusinessException(UnknownPlaceholdersMessage(unknown));

        return Placeholder().Replace(html, m => values[m.Groups[1].Value]);
    }

    /// <summary>Nomes usados no HTML que não estão em <paramref name="known"/>, sem repetição, na ordem em que aparecem.</summary>
    public static IReadOnlyList<string> FindUnknown(string html, IReadOnlySet<string> known)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var unknown = new List<string>();

        foreach (Match m in Placeholder().Matches(html))
        {
            var name = m.Groups[1].Value;
            if (!known.Contains(name) && seen.Add(name))
                unknown.Add(name);
        }

        return unknown;
    }

    public static string UnknownPlaceholdersMessage(IEnumerable<string> names) =>
        $"Campos não reconhecidos no modelo: {string.Join(", ", names)}. " +
        "Use apenas os campos da lista de placeholders disponíveis.";
}
```

- [ ] **Step 5: Rodar os testes**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~ContractDraftTemplateRendererTests"`
Expected: PASS (5 testes).

- [ ] **Step 6: Commit**

```bash
git add SiagroB1.Application/Services/ContractDrafts SiagroB1.Domain/Dtos/ContractDraftPlaceholderDto.cs SiagroB1.Application.Tests/ContractDrafts/ContractDraftTemplateRendererTests.cs
git commit -m "feat(platform): renderizar modelo de contrato por placeholders

Função pura sobre um dicionário já formatado: o snapshot da minuta vira
reproduzível sem banco. Placeholder desconhecido é erro de negócio com a lista,
e não texto {{x}} escapando para um contrato assinado.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 3: Resolver de placeholders (compra e venda, dois modos ERP) e extenso

**Files:**
- Create: `SiagroB1.Application/Services/ContractDrafts/ContractDraftNumberSpeller.cs`
- Create: `SiagroB1.Application/Services/ContractDrafts/ContractDraftPlaceholderResolver.cs`
- Test: `SiagroB1.Application.Tests/ContractDrafts/ContractDraftNumberSpellerTests.cs`
- Test: `SiagroB1.Application.Tests/ContractDrafts/ContractDraftPlaceholderResolverTests.cs`

**Interfaces:**
- Consumes: `IBusinessPartnerService.GetByIdAsync(string)` → `BusinessPartnerModel { CardName, CardFName, TaxId, Addresses: AddressModel[] { AddressName, AdresType, Street, Block, ZipCode, City, State } }`; `AppDbContext.CompanySignatories`, `.BusinessPartnerSignatories`, `.Branchs`, `.HarvestSeasons`; `ContractDraftPlaceholderCatalog.NamesFor`.
- Produces: `ContractDraftPlaceholderResolver.ResolveAsync(PurchaseContract, CancellationToken) : Task<Dictionary<string,string>>` e `ResolveAsync(SalesContract, CancellationToken)`; `ContractDraftNumberSpeller.Currency(decimal)`, `.Date(DateTime)`.

- [ ] **Step 1: Teste do extenso**

```csharp
// SiagroB1.Application.Tests/ContractDrafts/ContractDraftNumberSpellerTests.cs
using SiagroB1.Application.Services.ContractDrafts;

namespace SiagroB1.Application.Tests.ContractDrafts;

public class ContractDraftNumberSpellerTests
{
    [Theory]
    [InlineData(0, "zero real")]
    [InlineData(1, "um real")]
    [InlineData(1.5, "um real e cinquenta centavos")]
    [InlineData(21, "vinte e um reais")]
    [InlineData(100, "cem reais")]
    [InlineData(115, "cento e quinze reais")]
    [InlineData(1000, "mil reais")]
    [InlineData(2500.07, "dois mil e quinhentos reais e sete centavos")]
    [InlineData(1000000, "um milhão de reais")]
    [InlineData(3450000.10, "três milhões, quatrocentos e cinquenta mil reais e dez centavos")]
    public void Spells_brazilian_currency(decimal value, string expected)
    {
        Assert.Equal(expected, ContractDraftNumberSpeller.Currency(value));
    }

    [Fact]
    public void Spells_date_in_portuguese()
    {
        Assert.Equal("21 de setembro de 2026", ContractDraftNumberSpeller.Date(new DateTime(2026, 9, 21)));
    }
}
```

- [ ] **Step 2: Extenso**

```csharp
// SiagroB1.Application/Services/ContractDrafts/ContractDraftNumberSpeller.cs
using System.Globalization;

namespace SiagroB1.Application.Services.ContractDrafts;

/// <summary>Valor e data por extenso em pt-BR para os placeholders <c>*_extenso</c>.</summary>
public static class ContractDraftNumberSpeller
{
    private static readonly string[] Units =
        ["zero", "um", "dois", "três", "quatro", "cinco", "seis", "sete", "oito", "nove", "dez",
         "onze", "doze", "treze", "quatorze", "quinze", "dezesseis", "dezessete", "dezoito", "dezenove"];
    private static readonly string[] Tens =
        ["", "", "vinte", "trinta", "quarenta", "cinquenta", "sessenta", "setenta", "oitenta", "noventa"];
    private static readonly string[] Hundreds =
        ["", "cento", "duzentos", "trezentos", "quatrocentos", "quinhentos", "seiscentos",
         "setecentos", "oitocentos", "novecentos"];

    public static string Currency(decimal value)
    {
        value = decimal.Round(value, 2, MidpointRounding.ToEven);
        var reais = (long)decimal.Truncate(value);
        var centavos = (int)((value - reais) * 100);

        var text = reais switch
        {
            0 => "zero real",
            1 => "um real",
            _ => Integer(reais) + (reais >= 1_000_000 && reais % 1_000_000 == 0 ? " de reais" : " reais"),
        };

        if (centavos > 0)
            text += $" e {Integer(centavos)} {(centavos == 1 ? "centavo" : "centavos")}";

        return text;
    }

    public static string Date(DateTime date) =>
        $"{date.Day} de {date.ToString("MMMM", CultureInfo.GetCultureInfo("pt-BR"))} de {date.Year}";

    private static string Integer(long n)
    {
        if (n < 20) return Units[n];
        if (n < 100) return Tens[n / 10] + (n % 10 > 0 ? " e " + Units[n % 10] : "");
        if (n == 100) return "cem";
        if (n < 1000) return Hundreds[n / 100] + (n % 100 > 0 ? " e " + Integer(n % 100) : "");

        var parts = new List<string>();
        Group(ref n, 1_000_000_000, "bilhão", "bilhões", parts);
        Group(ref n, 1_000_000, "milhão", "milhões", parts);

        if (n >= 1000)
        {
            var thousands = n / 1000;
            parts.Add(thousands == 1 ? "mil" : Integer(thousands) + " mil");
            n %= 1000;
        }

        if (n > 0)
        {
            // "e" antes do último grupo quando ele é < 100 ou múltiplo de 100 (regra do português).
            var connector = n < 100 || n % 100 == 0 ? " e " : ", ";
            return string.Join(", ", parts) + connector + Integer(n);
        }

        return string.Join(", ", parts);
    }

    private static void Group(ref long n, long scale, string singular, string plural, List<string> parts)
    {
        if (n < scale) return;
        var count = n / scale;
        parts.Add(count == 1 ? $"um {singular}" : $"{Integer(count)} {plural}");
        n %= scale;
    }
}
```

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~ContractDraftNumberSpellerTests"` → PASS (11 testes). Se algum caso da tabela falhar por vírgula/"e", ajuste o conector em `Integer` até a tabela passar — a tabela é a regra.

- [ ] **Step 3: Teste do resolver (falha: tipo não existe)**

```csharp
// SiagroB1.Application.Tests/ContractDrafts/ContractDraftPlaceholderResolverTests.cs
using SiagroB1.Application.Services.ContractDrafts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.ContractDrafts;

/// <summary>
/// O resolver é a única ponte entre o contrato e o texto. Parceiro vem SEMPRE por
/// IBusinessPartnerService (em SAPB1 a tabela local está vazia); empresa vem de BRANCHS.
/// </summary>
public class ContractDraftPlaceholderResolverTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private ContractDraftPlaceholderResolver Resolver() => new(
        _db.Context,
        new FakeBusinessPartnerService(
            names: new() { ["F0001"] = "FAZENDA BOA VISTA LTDA", ["C0001"] = "TRADING SUL SA" },
            taxIds: new() { ["F0001"] = "12345678000190" },
            addresses: new()
            {
                ["F0001"] =
                [
                    new AddressModel { CardCode = "F0001", AddressName = "FATURAMENTO", AdresType = "B",
                        Street = "Rod. BR-163, km 12", Block = "Zona Rural", ZipCode = "78700000",
                        City = "Rondonópolis", State = "MT" },
                ],
            }));

    private async Task SeedMasterDataAsync()
    {
        _db.Context.Branchs.Add(new Branch { Code = "01", BranchName = "Matriz", ShortName = "MTZ", TaxId = "98765432000110" });
        _db.Context.HarvestSeasons.Add(new HarvestSeason { Code = "24/25", Name = "Safra 2024/2025" });
        _db.Context.CompanySignatories.Add(new CompanySignatory
            { Name = "Ana Diretora", TaxId = "11111111111", Email = "ana@empresa.com", Role = SignatoryRole.SignAsParty, Order = 1 });
        _db.Context.CompanySignatories.Add(new CompanySignatory
            { Name = "Inativo", TaxId = "22222222222", Email = "x@empresa.com", Active = false });
        _db.Context.CompanySignatories.Add(new CompanySignatory
            { BranchCode = "02", Name = "Outra filial", TaxId = "33333333333", Email = "f2@empresa.com" });
        _db.Context.BusinessPartnerSignatories.Add(new BusinessPartnerSignatory
            { CardCode = "F0001", Name = "João Produtor", TaxId = "44444444444", Email = "joao@fazenda.com", Role = SignatoryRole.SignAsParty });
        await _db.Context.SaveChangesAsync();
    }

    private static PurchaseContract Purchase() => new()
    {
        Key = Guid.NewGuid(), Code = "PC-000123", Complement = "Lote A", BranchCode = "01",
        CreationDate = new DateTime(2026, 9, 21), CardCode = "F0001", ItemCode = "SOJA", ItemName = "SOJA EM GRÃOS",
        UnitOfMeasureCode = "KG", HarvestSeasonCode = "24/25", TotalVolume = 1_500_000m, StandardPrice = 2.35m,
        StandardCurrency = CurrencyType.Brl, DeliveryStartDate = new DateTime(2026, 10, 1),
        DeliveryEndDate = new DateTime(2026, 11, 30), FreightTerms = FreightTerms.Cif,
        DeliveryLocationCode = "01", DeliveryLocationName = "ARMAZÉM MATRIZ", AgentName = "Carlos Rep.",
        PaymentTerms = "30 dias após entrega", StandardCashFlowDate = new DateTime(2026, 12, 30),
        Type = ContractType.Fixed, Status = ContractStatus.Approved,
        Brokers = [new PurchaseContractBroker { CardCode = "B01", CardName = "Corretora XY", Commission = 0.5m, ComissionUmCode = "%" }],
    };

    [Fact]
    public async Task Resolves_every_purchase_placeholder_with_formatted_values()
    {
        await SeedMasterDataAsync();

        var values = await Resolver().ResolveAsync(Purchase(), CancellationToken.None);

        Assert.Equal("PC-000123", values["numero"]);
        Assert.Equal("21/09/2026", values["emissao"]);
        Assert.Equal("21 de setembro de 2026", values["emissao_extenso"]);
        Assert.Equal("FAZENDA BOA VISTA LTDA", values["fornecedor_razao_social"]);
        Assert.Equal("12.345.678/0001-90", values["fornecedor_cnpj"]);
        Assert.Equal("Rod. BR-163, km 12", values["fornecedor_endereco"]);
        Assert.Equal("Rondonópolis", values["fornecedor_cidade"]);
        Assert.Equal("MT", values["fornecedor_uf"]);
        Assert.Equal("1.500.000,000", values["quantidade"]);
        Assert.Equal("2,35", values["preco"]);
        Assert.Equal("R$", values["moeda"]);
        Assert.Equal("3.525.000,00", values["valor_total"]);
        Assert.Equal("três milhões, quinhentos e vinte e cinco mil reais", values["valor_total_extenso"]);
        Assert.Equal("CIF", values["tipo_frete"]);
        Assert.Equal("Corretora XY", values["corretor_nome"]);
        Assert.Equal("Safra 2024/2025", values["safra_descricao"]);
        Assert.Equal("98.765.432/0001-10", values["empresa_cnpj"]);
        Assert.Equal("Matriz", values["filial_nome"]);
    }

    [Fact]
    public async Task Resolver_covers_every_catalog_name()
    {
        await SeedMasterDataAsync();

        var values = await Resolver().ResolveAsync(Purchase(), CancellationToken.None);

        foreach (var name in ContractDraftPlaceholderCatalog.NamesFor(ContractTemplateScope.Purchase))
            Assert.True(values.ContainsKey(name), $"placeholder sem valor: {name}");
    }

    [Fact]
    public async Task Signature_blocks_use_only_active_signatories_of_the_branch_or_global()
    {
        await SeedMasterDataAsync();

        var values = await Resolver().ResolveAsync(Purchase(), CancellationToken.None);

        Assert.Contains("Ana Diretora", values["assinaturas_empresa"]);
        Assert.DoesNotContain("Inativo", values["assinaturas_empresa"]);
        Assert.DoesNotContain("Outra filial", values["assinaturas_empresa"]);
        Assert.Contains("João Produtor", values["assinaturas_parceiro"]);
        Assert.Contains("444.444.444-44", values["assinaturas_parceiro"]);
    }

    [Fact]
    public async Task Unknown_partner_yields_empty_strings_not_exceptions()
    {
        await SeedMasterDataAsync();
        var contract = Purchase();
        contract.CardCode = "NAO-EXISTE";

        var values = await Resolver().ResolveAsync(contract, CancellationToken.None);

        Assert.Equal("", values["fornecedor_razao_social"]);
        Assert.Equal("", values["fornecedor_cnpj"]);
    }

    [Fact]
    public async Task Sales_uses_client_names_and_joins_delivery_locations()
    {
        await SeedMasterDataAsync();
        var contract = new SalesContract
        {
            Key = Guid.NewGuid(), Code = "SC-000045", BranchCode = "01", CreationDate = new DateTime(2026, 9, 21),
            CardCode = "C0001", ItemCode = "SOJA", ItemName = "SOJA EM GRÃOS", UnitOfMeasureCode = "KG",
            HarvestSeasonCode = "24/25", Volume = 500_000m, Price = 2.50m, StandardCurrency = CurrencyType.Usd,
            DeliveryStartDate = new DateTime(2026, 10, 1), DeliveryEndDate = new DateTime(2026, 10, 31),
            FreightTerms = FreightTerms.Fob, Type = ContractType.Fixed, Status = ContractStatus.Approved,
            DeliveryLocations =
            [
                new SalesContractDeliveryLocation { CardCode = "T1", CardName = "TERMINAL SANTOS" },
                new SalesContractDeliveryLocation { CardCode = "T2", CardName = "TERMINAL PARANAGUÁ" },
            ],
        };

        var values = await Resolver().ResolveAsync(contract, CancellationToken.None);

        Assert.Equal("TRADING SUL SA", values["cliente_razao_social"]);
        Assert.Equal("TERMINAL SANTOS; TERMINAL PARANAGUÁ", values["local_entrega"]);
        Assert.Equal("US$", values["moeda"]);
        Assert.Equal("1.250.000,00", values["valor_total"]);
        Assert.False(values.ContainsKey("fornecedor_cnpj"));

        foreach (var name in ContractDraftPlaceholderCatalog.NamesFor(ContractTemplateScope.Sales))
            Assert.True(values.ContainsKey(name), $"placeholder sem valor: {name}");
    }
}
```

Confira os nomes reais das propriedades `SalesContract.Volume`, `.Price`, `.StandardCurrency`, `.CreationDate` e `AppDbContext.Branchs`/`.HarvestSeasons` antes de compilar (`grep -n "DbSet<Branch>\|DbSet<HarvestSeason>" SiagroB1.Infra/Context/AppDbContext.cs`); ajuste o teste ao nome existente, nunca o contrário.

- [ ] **Step 4: Resolver**

```csharp
// SiagroB1.Application/Services/ContractDrafts/ContractDraftPlaceholderResolver.cs
using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Domain.Models;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.ContractDrafts;

/// <summary>
/// Monta o dicionário de placeholders a partir do contrato. Todos os valores saem já formatados
/// em pt-BR; ausência vira string vazia, nunca exceção — o modelo é do usuário e ele decide o
/// que citar. Parceiro SEMPRE por <see cref="IBusinessPartnerService"/>: em modo SAPB1 a tabela
/// local BUSINESS_PARTNERS está vazia e a navegação zeraria tudo.
/// </summary>
public class ContractDraftPlaceholderResolver(AppDbContext context, IBusinessPartnerService partners)
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public async Task<Dictionary<string, string>> ResolveAsync(PurchaseContract c, CancellationToken ct)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var partner = await partners.GetByIdAsync(c.CardCode);
        var total = decimal.Round(c.TotalVolume * c.StandardPrice, 2, MidpointRounding.ToEven);

        await AddCommonAsync(values, c.BranchCode, c.Code, c.Complement, c.CreationDate, c.DeliveryStartDate,
            c.DeliveryEndDate, c.AgentName, c.DeliveryLocationName, c.ItemName, c.HarvestSeasonCode,
            c.TotalVolume, c.UnitOfMeasureCode, c.StandardPrice, c.StandardCurrency, total, c.FreightTerms,
            c.PaymentTerms, c.StandardCashFlowDate, c.CardCode, ct);

        AddPartner(values, "fornecedor", partner);

        var broker = c.Brokers.FirstOrDefault();
        values["corretor_nome"] = broker?.CardName ?? "";
        values["corretor_comissao"] = broker is null ? "" : Number(broker.Commission, 2) + (broker.ComissionUmCode ?? "");

        return values;
    }

    public async Task<Dictionary<string, string>> ResolveAsync(SalesContract c, CancellationToken ct)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var partner = await partners.GetByIdAsync(c.CardCode);
        var total = decimal.Round(c.Volume * c.Price, 2, MidpointRounding.ToEven);
        var locations = string.Join("; ", c.DeliveryLocations.Select(l => l.CardName).Where(n => !string.IsNullOrWhiteSpace(n)));

        await AddCommonAsync(values, c.BranchCode, c.Code, c.Complement, c.CreationDate, c.DeliveryStartDate,
            c.DeliveryEndDate, c.AgentName, locations, c.ItemName, c.HarvestSeasonCode,
            c.Volume, c.UnitOfMeasureCode, c.Price, c.StandardCurrency, total, c.FreightTerms,
            c.PaymentTerms, c.StandardCashFlowDate, c.CardCode, ct);

        AddPartner(values, "cliente", partner);

        return values;
    }

    private async Task AddCommonAsync(
        Dictionary<string, string> v, string? branchCode, string? code, string? complement, DateTime? issued,
        DateTime deliveryStart, DateTime deliveryEnd, string? agentName, string? deliveryLocation, string? itemName,
        string? harvestSeasonCode, decimal volume, string? uom, decimal price, CurrencyType? currency, decimal total,
        FreightTerms freight, string? paymentTerms, DateTime? cashFlowDate, string cardCode, CancellationToken ct)
    {
        var branch = branchCode is null ? null
            : await context.Branchs.AsNoTracking().FirstOrDefaultAsync(b => b.Code == branchCode, ct);
        var season = harvestSeasonCode is null ? null
            : await context.HarvestSeasons.AsNoTracking().FirstOrDefaultAsync(h => h.Code == harvestSeasonCode, ct);

        v["numero"] = code ?? "";
        v["complemento"] = complement ?? "";
        v["emissao"] = Date(issued);
        v["emissao_extenso"] = issued is null ? "" : ContractDraftNumberSpeller.Date(issued.Value);
        v["data_inicio_entrega"] = Date(deliveryStart);
        v["data_termino_entrega"] = Date(deliveryEnd);
        v["representante_nome"] = agentName ?? "";
        v["local_entrega"] = deliveryLocation ?? "";
        v["produto_descricao"] = itemName ?? "";
        v["safra_descricao"] = season?.Name ?? harvestSeasonCode ?? "";
        v["quantidade"] = Number(volume, 3);
        v["unidade_medida"] = uom ?? "";
        v["preco"] = Price(price);
        v["moeda"] = currency == CurrencyType.Usd ? "US$" : "R$";
        v["valor_total"] = Number(total, 2);
        v["valor_total_extenso"] = ContractDraftNumberSpeller.Currency(total);
        v["tipo_frete"] = freight switch
        {
            FreightTerms.Cif => "CIF", FreightTerms.Fob => "FOB", FreightTerms.Ter => "Terceiro", _ => "Nenhum",
        };
        v["condicao_pagamento"] = paymentTerms ?? "";
        v["data_pagamento"] = Date(cashFlowDate);
        v["empresa_razao_social"] = branch?.BranchName ?? "";
        v["empresa_cnpj"] = TaxId(branch?.TaxId);
        v["filial_nome"] = branch?.ShortName ?? branch?.BranchName ?? "";

        var companySigners = await context.CompanySignatories.AsNoTracking()
            .Where(s => s.Active && (s.BranchCode == null || s.BranchCode == branchCode))
            .OrderBy(s => s.Order).ThenBy(s => s.Name)
            .Select(s => new SignerLine(s.Name, s.TaxId, s.Role)).ToListAsync(ct);
        var partnerSigners = await context.BusinessPartnerSignatories.AsNoTracking()
            .Where(s => s.Active && s.CardCode == cardCode)
            .OrderBy(s => s.Order).ThenBy(s => s.Name)
            .Select(s => new SignerLine(s.Name, s.TaxId, s.Role)).ToListAsync(ct);

        v["assinaturas_empresa"] = SignatureBlock(companySigners);
        v["assinaturas_parceiro"] = SignatureBlock(partnerSigners);
    }

    private static void AddPartner(Dictionary<string, string> v, string prefix, BusinessPartnerModel? partner)
    {
        // Endereço de faturamento ("B") quando houver; senão o primeiro. Em SAPB1 vem só um.
        var address = partner?.Addresses.FirstOrDefault(a => a.AdresType == "B") ?? partner?.Addresses.FirstOrDefault();

        v[$"{prefix}_razao_social"] = partner?.CardName ?? "";
        v[$"{prefix}_nome_fantasia"] = partner?.CardFName ?? "";
        v[$"{prefix}_cnpj"] = TaxId(partner?.TaxId);
        v[$"{prefix}_endereco"] = address?.Street ?? "";
        v[$"{prefix}_bairro"] = address?.Block ?? "";
        v[$"{prefix}_cep"] = ZipCode(address?.ZipCode);
        v[$"{prefix}_cidade"] = address?.City ?? "";
        v[$"{prefix}_uf"] = address?.State ?? "";
    }

    private record SignerLine(string Name, string TaxId, SignatoryRole Role);

    /// <summary>Uma tabela por signatário, no molde da Tagui, com nome, CPF e papel escapados.</summary>
    private static string SignatureBlock(IEnumerable<SignerLine> signers)
    {
        var sb = new StringBuilder();
        foreach (var s in signers)
        {
            sb.Append("<table class=\"signature\"><tr><td>ASSINATURA:</td><td>&nbsp;</td></tr>")
              .Append("<tr><td>").Append(WebUtility.HtmlEncode(RoleLabel(s.Role))).Append(":</td><td>")
              .Append(WebUtility.HtmlEncode(s.Name)).Append("</td></tr>")
              .Append("<tr><td>CPF:</td><td>").Append(TaxId(s.TaxId)).Append("</td></tr></table>");
        }
        return sb.ToString();
    }

    public static string RoleLabel(SignatoryRole role) => role switch
    {
        SignatoryRole.Sign => "Assinar",
        SignatoryRole.Approve => "Aprovador",
        SignatoryRole.Acknowledge => "Reconhecer",
        SignatoryRole.SignAsParty => "Parte",
        SignatoryRole.SignAsWitness => "Testemunha",
        SignatoryRole.SignAsIntervening => "Interveniente",
        SignatoryRole.AcknowledgeReceipt => "Acusar recebimento",
        SignatoryRole.SignAsIssuerEndorserGuarantor => "Emissor, endossante e avalista",
        SignatoryRole.SignAsIssuerEndorserGuarantorSurety => "Emissor, endossante, avalista e fiador",
        SignatoryRole.SignAsSurety => "Fiador",
        SignatoryRole.SignAsPartyAndSurety => "Parte e fiador",
        SignatoryRole.SignAsJointDebtor => "Responsável solidário",
        SignatoryRole.SignAsPartyAndJointDebtor => "Parte e responsável solidário",
        _ => role.ToString(),
    };

    private static string Date(DateTime? d) => d?.ToString("dd/MM/yyyy", PtBr) ?? "";
    private static string Number(decimal n, int decimals) => n.ToString($"N{decimals}", PtBr);

    /// <summary>Preço com no mínimo 2 casas e no máximo 8, sem zeros à direita além da segunda casa.</summary>
    private static string Price(decimal n)
    {
        var s = n.ToString("N8", PtBr).TrimEnd('0');
        var comma = s.IndexOf(',');
        return s.Length - comma - 1 < 2 ? n.ToString("N2", PtBr) : s;
    }

    private static string TaxId(string? digits)
    {
        var d = new string((digits ?? "").Where(char.IsDigit).ToArray());
        return d.Length switch
        {
            14 => $"{d[..2]}.{d[2..5]}.{d[5..8]}/{d[8..12]}-{d[12..]}",
            11 => $"{d[..3]}.{d[3..6]}.{d[6..9]}-{d[9..]}",
            _ => digits ?? "",
        };
    }

    private static string ZipCode(string? digits)
    {
        var d = new string((digits ?? "").Where(char.IsDigit).ToArray());
        return d.Length == 8 ? $"{d[..5]}-{d[5..]}" : digits ?? "";
    }
}
```

- [ ] **Step 5: Rodar os testes**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~ContractDrafts"`
Expected: PASS (todos, incluindo `Resolver_covers_every_catalog_name` para compra e a asserção equivalente para venda).

- [ ] **Step 6: Commit**

```bash
git add SiagroB1.Application/Services/ContractDrafts SiagroB1.Application.Tests/ContractDrafts
git commit -m "feat(platform): resolver placeholders da minuta a partir do contrato

Parceiro sempre por IBusinessPartnerService, nunca por navegação: em SAPB1 a
tabela local está vazia. Valores saem formatados em pt-BR e ausência vira vazio,
porque o modelo é do usuário. O teste de cobertura do catálogo impede que um
placeholder novo entre na lista sem valor.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 4: CRUD de modelos e signatários via OData, validação de placeholders e função de catálogo

**Files:**
- Create: `SiagroB1.Application/Services/ContractTemplates/ContractTemplateService.cs`
- Create: `SiagroB1.Application/Services/CompanySignatories/CompanySignatoryService.cs`
- Create: `SiagroB1.Application/Services/BusinessPartnerSignatories/BusinessPartnerSignatoryService.cs`
- Create: `SiagroB1.Web/Controllers/ContractTemplatesController.cs`, `CompanySignatoriesController.cs`, `BusinessPartnerSignatoriesController.cs`
- Create: `SiagroB1.Web/Functions/ContractTemplates/ContractTemplatesListPlaceholdersController.cs`
- Modify: `SiagroB1.Web/ODataConfig/ODataConfigurations.cs` (3 entity sets + 1 function)
- Modify: `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs` (3 registros)
- Test: `SiagroB1.Application.Tests/ContractTemplates/ContractTemplateServiceTests.cs`
- Test: `SiagroB1.Application.Tests/ContractTemplates/ContractTemplateEdmModelTests.cs`

**Interfaces:**
- Consumes: `BaseService<T, ID>` (Domain/Shared/Base), `ODataBaseController<T, ID>` (Web/Base), `ContractDraftTemplateRenderer.FindUnknown`, `ContractDraftPlaceholderCatalog`.
- Produces: `ContractTemplateService : BaseService<ContractTemplate, Guid>` com `CreateAsync`/`UpdateAsync` validando placeholders; `CompanySignatoryService`, `BusinessPartnerSignatoryService` (CRUD puro); entity sets `ContractTemplates`, `CompanySignatories`, `BusinessPartnerSignatories`; function `ContractTemplatesListPlaceholders(ContractType)`.

- [ ] **Step 1: Teste do serviço de modelo**

```csharp
// SiagroB1.Application.Tests/ContractTemplates/ContractTemplateServiceTests.cs
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.ContractTemplates;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.ContractTemplates;

/// <summary>Modelo só é salvo se todos os placeholders existirem no catálogo do seu escopo.</summary>
public class ContractTemplateServiceTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private ContractTemplateService Service() => new(_db.Context, NullLogger<ContractTemplateService>.Instance);

    private static ContractTemplate Template(string body, ContractTemplateScope scope = ContractTemplateScope.Purchase) => new()
    {
        Name = "Compra padrão", Title = "Contrato de Compra e Venda", ContractType = scope, BodyHtml = body,
    };

    [Fact]
    public async Task Create_accepts_known_placeholders()
    {
        var created = await Service().CreateAsync(Template("<p>{{numero}} {{fornecedor_cnpj}}</p>"));

        Assert.NotEqual(Guid.Empty, created.Key);
        Assert.Single(_db.Context.ContractTemplates);
    }

    [Fact]
    public async Task Create_rejects_unknown_placeholders_naming_them()
    {
        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => Service().CreateAsync(Template("<p>{{numero}} {{cnpj_fornecedor}}</p>")));

        Assert.Contains("cnpj_fornecedor", ex.Message);
        Assert.Empty(_db.Context.ContractTemplates);
    }

    [Fact]
    public async Task Shared_scope_rejects_side_specific_placeholders()
    {
        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => Service().CreateAsync(Template("{{fornecedor_cnpj}}", ContractTemplateScope.Both)));

        Assert.Contains("fornecedor_cnpj", ex.Message);
    }

    [Fact]
    public async Task Update_revalidates_the_body()
    {
        var created = await Service().CreateAsync(Template("{{numero}}"));
        created.BodyHtml = "{{numero}} {{xpto}}";

        await Assert.ThrowsAsync<BusinessException>(() => Service().UpdateAsync(created.Key, created));
    }
}
```

- [ ] **Step 2: Rodar — falha por compilação.** `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~ContractTemplateServiceTests"`

- [ ] **Step 3: Serviços**

```csharp
// SiagroB1.Application/Services/ContractTemplates/ContractTemplateService.cs
using Microsoft.Extensions.Logging;
using SiagroB1.Application.Services.ContractDrafts;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Shared.Base;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.ContractTemplates;

/// <summary>
/// CRUD do modelo de contrato. A única regra é a validação dos placeholders no salvamento: o
/// erro aparece na edição, e não semanas depois na hora de gerar a minuta.
/// </summary>
public class ContractTemplateService(AppDbContext context, ILogger<ContractTemplateService> logger)
    : BaseService<ContractTemplate, Guid>(context, logger)
{
    public override Task<ContractTemplate> CreateAsync(ContractTemplate entity)
    {
        Validate(entity);
        entity.CreatedAt = DateTime.Now;
        entity.UpdatedAt = DateTime.Now;
        return base.CreateAsync(entity);
    }

    public override Task<ContractTemplate?> UpdateAsync(Guid key, ContractTemplate entity)
    {
        Validate(entity);
        entity.UpdatedAt = DateTime.Now;
        return base.UpdateAsync(key, entity);
    }

    /// <summary>Lança <see cref="BusinessException"/> com os nomes desconhecidos; nada é gravado.</summary>
    public static void Validate(ContractTemplate template)
    {
        if (string.IsNullOrWhiteSpace(template.BodyHtml))
            throw new BusinessException("O texto do modelo não pode ficar vazio.");

        var unknown = ContractDraftTemplateRenderer.FindUnknown(
            template.BodyHtml, ContractDraftPlaceholderCatalog.NamesFor(template.ContractType));

        if (unknown.Count > 0)
            throw new BusinessException(ContractDraftTemplateRenderer.UnknownPlaceholdersMessage(unknown));
    }
}
```

```csharp
// SiagroB1.Application/Services/CompanySignatories/CompanySignatoryService.cs
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Shared.Base;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.CompanySignatories;

public class CompanySignatoryService(AppDbContext context, ILogger<CompanySignatoryService> logger)
    : BaseService<CompanySignatory, Guid>(context, logger)
{
}
```

```csharp
// SiagroB1.Application/Services/BusinessPartnerSignatories/BusinessPartnerSignatoryService.cs
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Shared.Base;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.BusinessPartnerSignatories;

public class BusinessPartnerSignatoryService(AppDbContext context, ILogger<BusinessPartnerSignatoryService> logger)
    : BaseService<BusinessPartnerSignatory, Guid>(context, logger)
{
}
```

`BaseService.CreateAsync` embrulha qualquer exceção em `DefaultException("Error creating entity.")`. Por isso `Validate` roda **antes** de chamar a base — a `BusinessException` chega intacta ao controller. Se o teste de rejeição vier com `DefaultException`, é sinal de que a validação foi parar dentro do `try` da base.

- [ ] **Step 4: Rodar os testes do serviço** → PASS (4).

- [ ] **Step 5: Controllers de entity set e a function de catálogo**

```csharp
// SiagroB1.Web/Controllers/ContractTemplatesController.cs
using Microsoft.AspNetCore.Mvc;
using SiagroB1.Application.Services.ContractTemplates;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Web.Base;

namespace SiagroB1.Web.Controllers;

/// <summary>
/// A base (`ODataBaseController`) engole toda exceção do serviço num `catch (Exception)` e devolve
/// 500 para o que não é `DefaultException`. Por isso a validação de placeholders roda AQUI, antes
/// de delegar — a `BusinessException` vira 400 com a mensagem pt-BR, como o editor espera.
/// </summary>
public class ContractTemplatesController(ContractTemplateService service)
    : ODataBaseController<ContractTemplate, Guid>(service)
{
    public override Task<IActionResult> Post([FromBody] ContractTemplate entity) =>
        Validated(entity, () => base.Post(entity));

    public override Task<IActionResult> Put([FromRoute] Guid key, [FromBody] ContractTemplate entity) =>
        Validated(entity, () => base.Put(key, entity));

    private static async Task<IActionResult> Validated(ContractTemplate entity, Func<Task<IActionResult>> next)
    {
        try { ContractTemplateService.Validate(entity); }
        catch (BusinessException e) { return new BadRequestObjectResult(e.Message); }

        return await next();
    }
}
```

O `Patch` da base aplica o delta e chama `UpdateAsync`, onde `Validate` lança dentro do `try` da base e viraria 500. Cubra-o também: sobrescreva `Patch` copiando o corpo da base (`GetByIdAsync` → `patch.Patch(t)` → `Validate(t)` dentro de `try/catch BusinessException` → `UpdateAsync`). O teste de fumaça do Step 9 exercita `POST`; teste `PATCH` à mão com `{{xpto}}` e confira o 400.

```csharp
// SiagroB1.Web/Controllers/CompanySignatoriesController.cs
using SiagroB1.Application.Services.CompanySignatories;
using SiagroB1.Domain.Entities;
using SiagroB1.Web.Base;

namespace SiagroB1.Web.Controllers;

public class CompanySignatoriesController(CompanySignatoryService service)
    : ODataBaseController<CompanySignatory, Guid>(service)
{
}
```

```csharp
// SiagroB1.Web/Controllers/BusinessPartnerSignatoriesController.cs
using SiagroB1.Application.Services.BusinessPartnerSignatories;
using SiagroB1.Domain.Entities;
using SiagroB1.Web.Base;

namespace SiagroB1.Web.Controllers;

public class BusinessPartnerSignatoriesController(BusinessPartnerSignatoryService service)
    : ODataBaseController<BusinessPartnerSignatory, Guid>(service)
{
}
```

```csharp
// SiagroB1.Web/Functions/ContractTemplates/ContractTemplatesListPlaceholdersController.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.ContractDrafts;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Web.Functions.ContractTemplates;

/// <summary>Lista de placeholders para o painel do editor de modelos. Escopo como string ("Purchase"/"Sales"/"Both").</summary>
public class ContractTemplatesListPlaceholdersController : ODataController
{
    [HttpGet("odata/ContractTemplatesListPlaceholders(ContractType={contractType})")]
    public ActionResult<ICollection<ContractDraftPlaceholderDto>> List([FromRoute] string contractType)
    {
        if (!Enum.TryParse<ContractTemplateScope>(contractType?.Trim('\''), true, out var scope))
            return BadRequest("Tipo de contrato inválido. Use Purchase, Sales ou Both.");

        return Ok(ContractDraftPlaceholderCatalog.For(scope));
    }
}
```

- [ ] **Step 6: EDM**

Em `ODataConfigurations.cs`, logo após `modelBuilder.EntitySet<SalesContractAttachment>("SalesContractsAttachments");`:

```csharp
        // Minutas — fase 1 (spec 2026-09-21)
        modelBuilder.EntitySet<ContractTemplate>("ContractTemplates");
        modelBuilder.EntitySet<CompanySignatory>("CompanySignatories");
        modelBuilder.EntitySet<BusinessPartnerSignatory>("BusinessPartnerSignatories");

        var contractTemplatesListPlaceholders = modelBuilder.Function("ContractTemplatesListPlaceholders");
        contractTemplatesListPlaceholders.Parameter<string>("ContractType");
        contractTemplatesListPlaceholders.ReturnsCollection<ContractDraftPlaceholderDto>();
```

`ContractDraftPlaceholderDto` é um `record` posicional sem chave: se o `ReturnsCollection<>` reclamar de tipo complexo sem `[Key]`, declare-o como complex type com `modelBuilder.ComplexType<ContractDraftPlaceholderDto>();` antes da function.

- [ ] **Step 7: DI** — em `AddApplicationServices()`, após o bloco `// sales contracts`:

```csharp
        // contract templates & signatories (minutas — fase 1)
        services.AddScoped<ContractTemplateService>();
        services.AddScoped<CompanySignatoryService>();
        services.AddScoped<BusinessPartnerSignatoryService>();
```

- [ ] **Step 8: Teste do EDM**

```csharp
// SiagroB1.Application.Tests/ContractTemplates/ContractTemplateEdmModelTests.cs
using Microsoft.OData.Edm;
using Microsoft.OData.ModelBuilder;
using SiagroB1.Web.ODataConfig;

namespace SiagroB1.Application.Tests.ContractTemplates;

public class ContractTemplateEdmModelTests
{
    private static IEdmModel BuildModel()
    {
        var builder = new ODataConventionModelBuilder { Namespace = "SIAGROB1" };
        builder.ConfigureODataEntities();
        return builder.GetEdmModel();
    }

    [Theory]
    [InlineData("ContractTemplates")]
    [InlineData("CompanySignatories")]
    [InlineData("BusinessPartnerSignatories")]
    public void The_entity_sets_are_exposed(string entitySet)
    {
        Assert.NotNull(BuildModel().EntityContainer.FindEntitySet(entitySet));
    }

    [Fact]
    public void List_placeholders_takes_the_scope_as_string()
    {
        var function = BuildModel().SchemaElements.OfType<IEdmFunction>()
            .Single(f => f.Name == "ContractTemplatesListPlaceholders");

        Assert.Equal("Edm.String", function.Parameters.Single(p => p.Name == "ContractType").Type.FullName());
    }
}
```

- [ ] **Step 9: Build, testes e fumaça manual**

Run: `dotnet build SiagroB1.sln` e `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~ContractTemplate"` → PASS (6).
Suba `SiagroB1.Web` (`dotnet run --project SiagroB1.Web --launch-profile dev`) e confira `GET http://localhost:50000/odata/ContractTemplatesListPlaceholders(ContractType='Purchase')` devolve 34 itens e `POST /odata/ContractTemplates` com `{{xpto}}` devolve 400 com a mensagem pt-BR.

- [ ] **Step 10: Commit**

```bash
git add SiagroB1.Application/Services/ContractTemplates SiagroB1.Application/Services/CompanySignatories SiagroB1.Application/Services/BusinessPartnerSignatories SiagroB1.Web SiagroB1.Application.Tests/ContractTemplates
git commit -m "feat(platform): expor modelos de contrato e signatários por OData

CRUD pelo ODataBaseController como os demais cadastros. O modelo valida os
placeholders ao salvar contra o catálogo do seu escopo, e a function de
catálogo alimenta o painel do editor na tela.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 5: PDF por Chromium — `IHtmlToPdfRenderer`, layout e aviso de boot

**Files:**
- Create: `SiagroB1.Domain/Interfaces/IHtmlToPdfRenderer.cs`
- Create: `SiagroB1.Infra/Pdf/ChromiumHtmlToPdfRenderer.cs`
- Create: `SiagroB1.Application/Services/ContractDrafts/ContractDraftPdfLayout.cs`
- Modify: `SiagroB1.Infra/SiagroB1.Infra.csproj` (PuppeteerSharp)
- Modify: `SiagroB1.Web/Program.cs` (registro singleton + `WarnIfContractDraftPdfIsUnavailable`)
- Modify: `SiagroB1.Web/appsettings.json`, `appsettings.Development.json`, `appsettings.Yokotobi-Development.json`, `appsettings.Yokotobi-Staging.json`, `appsettings.Yokotobi-Production.json`, `appsettings.MhAgro-Production.json` (seção `Signature`)
- Create: `SiagroB1.Application.Tests/Support/FakeHtmlToPdfRenderer.cs`
- Test: `SiagroB1.Application.Tests/ContractDrafts/ContractDraftPdfLayoutTests.cs`, `ChromiumHtmlToPdfRendererSmokeTests.cs`

**Interfaces:**
- Produces: `IHtmlToPdfRenderer.RenderAsync(string html, CancellationToken) : Task<byte[]>`; `ContractDraftPdfLayout.Wrap(string bodyHtml, string title) : string`; `ContractDraftPdfLayout.Css` (const, a mesma que o frontend aplicará no preview); `FakeHtmlToPdfRenderer` (grava o último HTML em `LastHtml`, devolve `[0x25,0x50,0x44,0x46]` = "%PDF").

- [ ] **Step 1: Teste do layout e fake**

```csharp
// SiagroB1.Application.Tests/Support/FakeHtmlToPdfRenderer.cs
using SiagroB1.Domain.Interfaces;

namespace SiagroB1.Application.Tests.Support;

public sealed class FakeHtmlToPdfRenderer : IHtmlToPdfRenderer
{
    public string? LastHtml { get; private set; }
    public int Calls { get; private set; }

    public Task<byte[]> RenderAsync(string html, CancellationToken ct = default)
    {
        LastHtml = html;
        Calls++;
        return Task.FromResult(new byte[] { 0x25, 0x50, 0x44, 0x46 });
    }
}
```

```csharp
// SiagroB1.Application.Tests/ContractDrafts/ContractDraftPdfLayoutTests.cs
using SiagroB1.Application.Services.ContractDrafts;

namespace SiagroB1.Application.Tests.ContractDrafts;

public class ContractDraftPdfLayoutTests
{
    [Fact]
    public void Wraps_body_in_a_full_document_with_charset_title_and_css()
    {
        var html = ContractDraftPdfLayout.Wrap("<p>corpo</p>", "Contrato <PC-1>");

        Assert.StartsWith("<!DOCTYPE html>", html);
        Assert.Contains("<meta charset=\"utf-8\">", html);
        Assert.Contains("<title>Contrato &lt;PC-1&gt;</title>", html);
        Assert.Contains(ContractDraftPdfLayout.Css, html);
        Assert.Contains("<p>corpo</p>", html);
    }
}
```

- [ ] **Step 2: Interface, layout e renderer**

```csharp
// SiagroB1.Domain/Interfaces/IHtmlToPdfRenderer.cs
namespace SiagroB1.Domain.Interfaces;

/// <summary>HTML completo → bytes de PDF. A implementação real usa Chromium headless.</summary>
public interface IHtmlToPdfRenderer
{
    Task<byte[]> RenderAsync(string html, CancellationToken ct = default);
}
```

```csharp
// SiagroB1.Application/Services/ContractDrafts/ContractDraftPdfLayout.cs
using System.Net;

namespace SiagroB1.Application.Services.ContractDrafts;

/// <summary>
/// Embrulha o BodyHtml da minuta num documento imprimível. O CSS é o MESMO que o editor UI5
/// aplica no preview — o que se vê é o que se imprime. Se mudar aqui, mude lá.
/// </summary>
public static class ContractDraftPdfLayout
{
    public const string Css =
        "body{font-family:Arial,Helvetica,sans-serif;font-size:11pt;line-height:1.45;color:#000}" +
        "h1,h2,h3{font-weight:bold}h1{font-size:14pt;text-align:center}h2{font-size:12pt}" +
        "p{margin:0 0 8pt;text-align:justify}" +
        "table{border-collapse:collapse;width:100%;margin:6pt 0}td,th{padding:2pt 4pt;vertical-align:top}" +
        "table.signature{width:auto;margin:18pt 0 6pt}table.signature td:first-child{font-weight:bold;padding-right:8pt}";

    public static string Wrap(string bodyHtml, string title) =>
        "<!DOCTYPE html><html lang=\"pt-BR\"><head><meta charset=\"utf-8\">" +
        $"<title>{WebUtility.HtmlEncode(title)}</title><style>{Css}</style></head>" +
        $"<body>{bodyHtml}</body></html>";
}
```

```csharp
// SiagroB1.Infra/Pdf/ChromiumHtmlToPdfRenderer.cs
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PuppeteerSharp;
using PuppeteerSharp.Media;
using SiagroB1.Domain.Interfaces;

namespace SiagroB1.Infra.Pdf;

/// <summary>
/// PDF por Chromium headless (PuppeteerSharp). Um browser por processo, criado no primeiro uso;
/// cada render abre e fecha uma página. Registrado como singleton.
///
/// <c>Signature:Pdf:ChromiumPath</c> aponta para o executável; vazio ⇒ <see cref="BrowserFetcher"/>
/// baixa para <c>{ContentRoot}/chromium</c> (precisa de internet no primeiro uso).
/// <c>Signature:Pdf:NoSandbox</c> liga <c>--no-sandbox</c>, necessário em alguns Windows Service e
/// Linux — só ligue quando o log pedir (Chromium ≥ 125 falha o sandbox em serviço sem sessão).
/// </summary>
public sealed class ChromiumHtmlToPdfRenderer(
    IConfiguration configuration,
    ILogger<ChromiumHtmlToPdfRenderer> logger) : IHtmlToPdfRenderer, IAsyncDisposable
{
    public const string ChromiumPathKey = "Signature:Pdf:ChromiumPath";
    public const string NoSandboxKey = "Signature:Pdf:NoSandbox";

    private readonly SemaphoreSlim _gate = new(1, 1);
    private IBrowser? _browser;

    public async Task<byte[]> RenderAsync(string html, CancellationToken ct = default)
    {
        var browser = await GetBrowserAsync(ct);

        await using var page = await browser.NewPageAsync();
        await page.SetContentAsync(html, new NavigationOptions { WaitUntil = [WaitUntilNavigation.Load] });

        return await page.PdfDataAsync(new PdfOptions
        {
            Format = PaperFormat.A4,
            PrintBackground = true,
            MarginOptions = new MarginOptions { Top = "20mm", Bottom = "20mm", Left = "20mm", Right = "20mm" },
        });
    }

    private async Task<IBrowser> GetBrowserAsync(CancellationToken ct)
    {
        if (_browser is { IsClosed: false })
            return _browser;

        await _gate.WaitAsync(ct);
        try
        {
            if (_browser is { IsClosed: false })
                return _browser;

            var executable = await ResolveExecutableAsync();
            var args = configuration.GetValue(NoSandboxKey, false) ? new[] { "--no-sandbox" } : [];

            _browser = await Puppeteer.LaunchAsync(new LaunchOptions
            {
                Headless = true, ExecutablePath = executable, Args = args,
            });

            logger.LogInformation("Chromium iniciado para PDF de minutas: {Executable}", executable);
            return _browser;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<string> ResolveExecutableAsync()
    {
        var configured = configuration[ChromiumPathKey];
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            return configured;

        var downloadPath = Path.Combine(AppContext.BaseDirectory, "chromium");
        var fetcher = new BrowserFetcher(new BrowserFetcherOptions { Path = downloadPath });
        var installed = await fetcher.DownloadAsync();

        return installed.GetExecutablePath();
    }

    /// <summary>Só diz se há executável sem baixar nada — para o aviso de boot.</summary>
    public static bool IsAvailable(IConfiguration configuration)
    {
        var configured = configuration[ChromiumPathKey];
        if (!string.IsNullOrWhiteSpace(configured))
            return File.Exists(configured);

        var fetcher = new BrowserFetcher(new BrowserFetcherOptions
            { Path = Path.Combine(AppContext.BaseDirectory, "chromium") });
        return fetcher.GetInstalledBrowsers().Any();
    }

    public async ValueTask DisposeAsync()
    {
        if (_browser is not null)
            await _browser.CloseAsync();
        _gate.Dispose();
    }
}
```

Adicione ao `SiagroB1.Infra.csproj`: `<PackageReference Include="PuppeteerSharp" Version="20.*" />` — rode `dotnet add SiagroB1.Infra package PuppeteerSharp` para fixar a versão mais recente e comitar o número exato. Se `PdfDataAsync`/`GetInstalledBrowsers` não existirem na versão instalada, consulte a API via context7 (`/websites/puppeteersharp`) e ajuste; `page.PdfDataAsync` existe desde a v6.

- [ ] **Step 3: Teste de fumaça do renderer real (pulado sem Chromium)**

```csharp
// SiagroB1.Application.Tests/ContractDrafts/ChromiumHtmlToPdfRendererSmokeTests.cs
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Infra.Pdf;

namespace SiagroB1.Application.Tests.ContractDrafts;

/// <summary>
/// Só roda quando a variável CHROMIUM_PATH aponta para um executável — a suíte não baixa 150 MB.
/// Rode local com: $env:CHROMIUM_PATH = 'C:\Program Files\Google\Chrome\Application\chrome.exe'.
/// </summary>
public class ChromiumHtmlToPdfRendererSmokeTests
{
    [Fact]
    [Trait("Category", "Chromium")]
    public async Task Renders_a_pdf_from_html()
    {
        var path = Environment.GetEnvironmentVariable("CHROMIUM_PATH");
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return; // sem Chromium na máquina: teste vira no-op, não falha.

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [ChromiumHtmlToPdfRenderer.ChromiumPathKey] = path })
            .Build();
        await using var renderer = new ChromiumHtmlToPdfRenderer(configuration, NullLogger<ChromiumHtmlToPdfRenderer>.Instance);

        var pdf = await renderer.RenderAsync("<!DOCTYPE html><html><body><h1>Minuta</h1></body></html>");

        Assert.True(pdf.Length > 1000);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
    }
}
```

- [ ] **Step 4: Registro, configuração e aviso de boot**

Em `Program.cs`, logo após o `AddHttpClient` do WhatsApp:

```csharp
// PDF das minutas por Chromium headless. Singleton: um browser por processo, páginas por render.
builder.Services.AddSingleton<IHtmlToPdfRenderer, ChromiumHtmlToPdfRenderer>();
```

Antes de `await app.RunAsync();`, após `WarnIfTruckScaleChannelIsUnauthenticated(app);`:

```csharp
WarnIfContractDraftPdfIsUnavailable(app);
```

E ao lado da função existente:

```csharp
/// <summary>
/// Avisa, no boot, que o PDF de minutas vai falhar: sem Chromium configurado nem baixado, o primeiro
/// download de minuta dispara um download de ~150 MB (ou falha sem internet). Não derruba o serviço —
/// o resto do sistema não depende disso.
/// </summary>
static void WarnIfContractDraftPdfIsUnavailable(WebApplication app)
{
    if (ChromiumHtmlToPdfRenderer.IsAvailable(app.Configuration))
        return;

    app.Services.GetRequiredService<ILoggerFactory>()
        .CreateLogger("ContractDraftPdf")
        .LogWarning(
            "CHROMIUM NÃO ENCONTRADO para PDF de minutas ({Key} vazio ou inválido). O primeiro PDF " +
            "vai tentar baixar o Chromium para {Path}; sem internet, falha.",
            ChromiumHtmlToPdfRenderer.ChromiumPathKey, Path.Combine(AppContext.BaseDirectory, "chromium"));
}
```

Seção nova em **todos** os `SiagroB1.Web/appsettings*.json` (base e 5 variantes), depois de `Notifications`:

```json
  "Signature": {
    "Enabled": false,
    "Provider": "D4Sign",
    "D4Sign": {
      "BaseUrl": "https://sandbox.d4sign.com.br/api/v1",
      "TokenApi": "",
      "CryptKey": "",
      "SafeId": "",
      "WebhookBaseUrl": "",
      "WebhookSecret": ""
    },
    "Pdf": {
      "ChromiumPath": "",
      "NoSandbox": false
    }
  }
```

(`D4Sign:*` fica em branco nesta fase; a Fase 2 lê. `Yokotobi-Production` e `MhAgro-Production` usam `BaseUrl` `https://secure.d4sign.com.br/api/v1`.)

- [ ] **Step 5: Build e testes**

Run: `dotnet build SiagroB1.sln`; `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~ContractDraftPdfLayoutTests|FullyQualifiedName~ChromiumHtmlToPdfRendererSmokeTests"` → PASS. Rode uma vez com `CHROMIUM_PATH` apontando para o Chrome local para ver o smoke passar de verdade. Suba o Web e confira o aviso `CHROMIUM NÃO ENCONTRADO` no log (esperado, `ChromiumPath` vazio).

- [ ] **Step 6: Commit**

```bash
git add SiagroB1.Domain/Interfaces/IHtmlToPdfRenderer.cs SiagroB1.Infra SiagroB1.Application/Services/ContractDrafts/ContractDraftPdfLayout.cs SiagroB1.Web/Program.cs SiagroB1.Web/appsettings*.json SiagroB1.Application.Tests
git commit -m "feat(platform): gerar PDF de minuta por Chromium headless

PuppeteerSharp atrás de IHtmlToPdfRenderer: fidelidade total ao editor, e o
fake nos testes mantém a suíte sem binário. Aviso de boot quando não há
Chromium, no molde da balança e do e-mail.

Atenção: NoSandbox só quando o log pedir; ligar por padrão abre o processo.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 6: Serviços da minuta — criar, editar, excluir, listar e PDF

**Files:**
- Create: `SiagroB1.Domain/Dtos/ContractDraftDto.cs`, `SiagroB1.Domain/Dtos/ContractDraftSignerDto.cs`
- Create: `SiagroB1.Application/Services/ContractDrafts/ContractDraftContractType.cs` (enum de parâmetro)
- Create: `SiagroB1.Application/Services/ContractDrafts/ContractDraftsLoader.cs`
- Create: `SiagroB1.Application/Services/ContractDrafts/ContractDraftsCreateService.cs`, `ContractDraftsUpdateService.cs`, `ContractDraftsDeleteService.cs`, `ContractDraftsGetService.cs`, `ContractDraftsGetPdfService.cs`
- Test: `SiagroB1.Application.Tests/ContractDrafts/ContractDraftsTestContext.cs`, `ContractDraftsCreateServiceTests.cs`, `ContractDraftsUpdateAndDeleteServiceTests.cs`, `ContractDraftsGetPdfServiceTests.cs`

**Interfaces:**
- Consumes: Task 1 (entidades), Task 2 (`ContractDraftTemplateRenderer.Render`), Task 3 (`ContractDraftPlaceholderResolver.ResolveAsync`), Task 5 (`IHtmlToPdfRenderer`, `ContractDraftPdfLayout.Wrap`), `PurchaseContractsChangeLogService.Register(Guid, string, string?, string?, string)` e `SalesContractsChangeLogService.Register(...)` (mesma assinatura), `PurchaseContractsAttachmentsGetService.GetByKey(Guid)` e `SalesContractsAttachmentsGetService.GetByKey(Guid)`.
- Produces: `ContractDraftContractType { Purchase, Sales }`; `ContractDraftsCreateService.ExecuteAsync(ContractDraftContractType, Guid contractKey, Guid templateKey, ContractDraftType, string description, string userName, CancellationToken) : Task<ContractDraft>`; `ContractDraftsUpdateService.ExecuteAsync(Guid key, string description, ContractDraftType, string bodyHtml, string userName)`; `ContractDraftsDeleteService.ExecuteAsync(Guid key)`; `ContractDraftsGetService.ListByContractAsync(ContractDraftContractType, Guid) : Task<List<ContractDraftDto>>`, `.GetBodyAsync(Guid) : Task<string>`; `ContractDraftsGetPdfService.ExecuteAsync(Guid, CancellationToken) : Task<(byte[] Bytes, string FileName)>`; `ContractDraftsLoader.RequireDraftAsync(Guid)`.

- [ ] **Step 1: DTOs e enum de parâmetro**

```csharp
// SiagroB1.Application/Services/ContractDrafts/ContractDraftContractType.cs
namespace SiagroB1.Application.Services.ContractDrafts;

/// <summary>Qual lado do check constraint a minuta ocupa. Viaja como string na action ("Purchase"/"Sales").</summary>
public enum ContractDraftContractType { Purchase, Sales }
```

```csharp
// SiagroB1.Domain/Dtos/ContractDraftSignerDto.cs
using System.Text.Json.Serialization;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Dtos;

public class ContractDraftSignerDto
{
    [JsonPropertyName("Key")] public Guid Key { get; set; }
    [JsonPropertyName("Side")] public SignerSide Side { get; set; }
    [JsonPropertyName("Name")] public string? Name { get; set; }
    [JsonPropertyName("Email")] public string? Email { get; set; }
    [JsonPropertyName("Role")] public SignatoryRole Role { get; set; }
    [JsonPropertyName("Order")] public int Order { get; set; }
    [JsonPropertyName("Status")] public SignerStatus Status { get; set; }
    [JsonPropertyName("SignedAt")] public DateTime? SignedAt { get; set; }
    [JsonPropertyName("LastMessage")] public string? LastMessage { get; set; }
}
```

```csharp
// SiagroB1.Domain/Dtos/ContractDraftDto.cs
using System.Text.Json.Serialization;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Dtos;

/// <summary>Linha da aba "Minutas": sem BodyHtml/PlaceholdersJson (pesados; a tela pede pela function GetBody).</summary>
public class ContractDraftDto
{
    [JsonPropertyName("Key")] public Guid Key { get; set; }
    [JsonPropertyName("ContractCode")] public string? ContractCode { get; set; }
    [JsonPropertyName("Sequence")] public int Sequence { get; set; }
    [JsonPropertyName("TemplateKey")] public Guid TemplateKey { get; set; }
    [JsonPropertyName("TemplateName")] public string? TemplateName { get; set; }
    [JsonPropertyName("DraftType")] public ContractDraftType DraftType { get; set; }
    [JsonPropertyName("Description")] public string? Description { get; set; }
    [JsonPropertyName("Status")] public ContractDraftStatus Status { get; set; }
    [JsonPropertyName("Provider")] public string? Provider { get; set; }
    [JsonPropertyName("SentAt")] public DateTime? SentAt { get; set; }
    [JsonPropertyName("SignedAt")] public DateTime? SignedAt { get; set; }
    [JsonPropertyName("LastError")] public string? LastError { get; set; }
    [JsonPropertyName("SignedAttachmentKey")] public Guid? SignedAttachmentKey { get; set; }
    [JsonPropertyName("CreatedAt")] public DateTime? CreatedAt { get; set; }
    [JsonPropertyName("CreatedBy")] public string? CreatedBy { get; set; }
    [JsonPropertyName("Signers")] public List<ContractDraftSignerDto> Signers { get; set; } = [];
}
```

- [ ] **Step 2: Contexto de teste compartilhado**

```csharp
// SiagroB1.Application.Tests/ContractDrafts/ContractDraftsTestContext.cs
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.ContractDrafts;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.ContractDrafts;

/// <summary>Montagem comum dos testes de minuta: banco InMemory, fakes e semente mínima.</summary>
public sealed class ContractDraftsTestContext
{
    public UnitOfWork Db { get; } = TestDb.CreateUnitOfWork();
    public FakeHtmlToPdfRenderer Pdf { get; } = new();

    public FakeBusinessPartnerService Partners { get; } = new(
        names: new() { ["F0001"] = "FAZENDA BOA VISTA LTDA", ["C0001"] = "TRADING SUL SA" },
        taxIds: new() { ["F0001"] = "12345678000190" });

    public ContractDraftPlaceholderResolver Resolver() => new(Db.Context, Partners);
    public ContractDraftsLoader Loader() => new(Db.Context);
    public PurchaseContractsChangeLogService PurchaseLog() => new(Db.Context);
    public SalesContractsChangeLogService SalesLog() => new(Db.Context);

    public ContractDraftsCreateService Create() => new(Db.Context, Resolver(), PurchaseLog(), SalesLog(),
        NullLogger<ContractDraftsCreateService>.Instance);
    public ContractDraftsUpdateService Update() => new(Db.Context, Loader());
    public ContractDraftsDeleteService Delete() => new(Db.Context, Loader());
    public ContractDraftsGetService Get() => new(Db.Context);
    public ContractDraftsGetPdfService GetPdf() => new(Db.Context, Loader(), Pdf,
        new PurchaseContractsAttachmentsGetService(Db, NullLogger<PurchaseContractsAttachmentsGetService>.Instance),
        new SalesContractsAttachmentsGetService(Db, NullLogger<SalesContractsAttachmentsGetService>.Instance));

    public async Task<ContractTemplate> SeedTemplateAsync(
        string body = "<p>Contrato {{numero}} com {{fornecedor_razao_social}}</p>{{assinaturas_empresa}}",
        ContractTemplateScope scope = ContractTemplateScope.Purchase, bool active = true)
    {
        var t = new ContractTemplate { Name = $"Modelo {Guid.NewGuid():N}", Title = "Contrato de Compra", ContractType = scope, BodyHtml = body, Active = active };
        Db.Context.ContractTemplates.Add(t);
        await Db.Context.SaveChangesAsync();
        return t;
    }

    public async Task<PurchaseContract> SeedPurchaseAsync(ContractStatus status = ContractStatus.Approved)
    {
        Db.Context.Branchs.Add(new Branch { Code = "01", BranchName = "Matriz", ShortName = "MTZ", TaxId = "98765432000110" });
        var c = new PurchaseContract
        {
            Key = Guid.NewGuid(), Code = "PC-000123", BranchCode = "01", CardCode = "F0001", ItemCode = "SOJA",
            ItemName = "SOJA EM GRÃOS", UnitOfMeasureCode = "KG", HarvestSeasonCode = "24/25", TotalVolume = 1000m,
            StandardPrice = 2m, DeliveryLocationCode = "01", Type = ContractType.Fixed, Status = status,
            DeliveryStartDate = new DateTime(2026, 10, 1), DeliveryEndDate = new DateTime(2026, 10, 31),
        };
        Db.Context.PurchaseContracts.Add(c);
        await Db.Context.SaveChangesAsync();
        return c;
    }

    public async Task<SalesContract> SeedSalesAsync(ContractStatus status = ContractStatus.Approved)
    {
        var c = new SalesContract
        {
            Key = Guid.NewGuid(), Code = "SC-000045", BranchCode = "01", CardCode = "C0001", ItemCode = "SOJA",
            UnitOfMeasureCode = "KG", HarvestSeasonCode = "24/25", Volume = 500m, Price = 3m, Type = ContractType.Fixed,
            Status = status, DeliveryStartDate = new DateTime(2026, 10, 1), DeliveryEndDate = new DateTime(2026, 10, 31),
        };
        Db.Context.SalesContracts.Add(c);
        await Db.Context.SaveChangesAsync();
        return c;
    }
}
```

`PurchaseContractsAttachmentsGetService` e `SalesContractsAttachmentsGetService` recebem `(IUnitOfWork db, ILogger<...> logger)` — por isso o `UnitOfWork` do `TestDb` é passado inteiro, não `Db.Context`.

- [ ] **Step 3: Testes do Create (falham: serviço não existe)**

```csharp
// SiagroB1.Application.Tests/ContractDrafts/ContractDraftsCreateServiceTests.cs
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.ContractDrafts;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Tests.ContractDrafts;

public class ContractDraftsCreateServiceTests
{
    private readonly ContractDraftsTestContext _ctx = new();

    [Fact]
    public async Task Creates_a_rendered_snapshot_with_sequence_and_change_log()
    {
        var template = await _ctx.SeedTemplateAsync();
        var contract = await _ctx.SeedPurchaseAsync();

        var draft = await _ctx.Create().ExecuteAsync(ContractDraftContractType.Purchase, contract.Key, template.Key,
            ContractDraftType.Contract, "Minuta inicial", "tester", CancellationToken.None);

        Assert.Equal(1, draft.Sequence);
        Assert.Equal("PC-000123", draft.ContractCode);
        Assert.Equal("01", draft.BranchCode);
        Assert.Equal(ContractDraftStatus.Draft, draft.Status);
        Assert.Contains("PC-000123", draft.BodyHtml);
        Assert.Contains("FAZENDA BOA VISTA LTDA", draft.BodyHtml);
        Assert.DoesNotContain("{{", draft.BodyHtml);
        Assert.Contains("\"numero\":\"PC-000123\"", draft.PlaceholdersJson);
        Assert.Equal(contract.Key, draft.PurchaseContractKey);
        Assert.Null(draft.SalesContractKey);

        var log = await _ctx.Db.Context.PurchaseContractsChangeLogs.SingleAsync();
        Assert.Equal(ContractChangeLogFields.Draft, log.Field);
        Assert.Equal("Minuta 1 criada", log.NewValue);
    }

    [Fact]
    public async Task Sequence_increments_per_contract()
    {
        var template = await _ctx.SeedTemplateAsync();
        var contract = await _ctx.SeedPurchaseAsync();
        var other = await _ctx.SeedSalesAsync();
        var salesTemplate = await _ctx.SeedTemplateAsync("{{numero}}", ContractTemplateScope.Sales);

        await _ctx.Create().ExecuteAsync(ContractDraftContractType.Purchase, contract.Key, template.Key, ContractDraftType.Contract, "1", "t", default);
        var second = await _ctx.Create().ExecuteAsync(ContractDraftContractType.Purchase, contract.Key, template.Key, ContractDraftType.Amendment, "2", "t", default);
        var salesFirst = await _ctx.Create().ExecuteAsync(ContractDraftContractType.Sales, other.Key, salesTemplate.Key, ContractDraftType.Contract, "v", "t", default);

        Assert.Equal(2, second.Sequence);
        Assert.Equal(1, salesFirst.Sequence);
        Assert.Equal(other.Key, salesFirst.SalesContractKey);
    }

    [Fact]
    public async Task Rejects_canceled_contract()
    {
        var template = await _ctx.SeedTemplateAsync();
        var contract = await _ctx.SeedPurchaseAsync(ContractStatus.Canceled);

        var ex = await Assert.ThrowsAsync<BusinessException>(() => _ctx.Create().ExecuteAsync(
            ContractDraftContractType.Purchase, contract.Key, template.Key, ContractDraftType.Contract, "x", "t", default));

        Assert.Equal("Contrato cancelado não recebe minuta.", ex.Message);
    }

    [Theory]
    [InlineData(false, ContractTemplateScope.Purchase, "Modelo inativo.")]
    [InlineData(true, ContractTemplateScope.Sales, "Modelo não se aplica a contrato de compra.")]
    public async Task Rejects_inactive_or_incompatible_template(bool active, ContractTemplateScope scope, string message)
    {
        var template = await _ctx.SeedTemplateAsync("{{numero}}", scope, active);
        var contract = await _ctx.SeedPurchaseAsync();

        var ex = await Assert.ThrowsAsync<BusinessException>(() => _ctx.Create().ExecuteAsync(
            ContractDraftContractType.Purchase, contract.Key, template.Key, ContractDraftType.Contract, "x", "t", default));

        Assert.Equal(message, ex.Message);
    }

    [Fact]
    public async Task Missing_contract_or_template_is_not_found()
    {
        var template = await _ctx.SeedTemplateAsync();

        await Assert.ThrowsAsync<NotFoundException>(() => _ctx.Create().ExecuteAsync(
            ContractDraftContractType.Purchase, Guid.NewGuid(), template.Key, ContractDraftType.Contract, "x", "t", default));
    }
}
```

- [ ] **Step 4: Loader e Create**

```csharp
// SiagroB1.Application/Services/ContractDrafts/ContractDraftsLoader.cs
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.ContractDrafts;

/// <summary>Carregamentos e guardas repetidos pelos serviços da minuta.</summary>
public class ContractDraftsLoader(AppDbContext context)
{
    public async Task<ContractDraft> RequireDraftAsync(Guid key, CancellationToken ct = default) =>
        await context.ContractDrafts.Include(d => d.Signers).FirstOrDefaultAsync(d => d.Key == key, ct)
        ?? throw new NotFoundException("Minuta não encontrada.");

    /// <summary>Só rascunho aceita edição e exclusão — depois do envio o texto é o que foi assinado.</summary>
    public static void RequireEditable(ContractDraft draft)
    {
        if (draft.Status != ContractDraftStatus.Draft)
            throw new BusinessException("Minuta já enviada para assinatura não pode ser alterada.");
    }
}
```

```csharp
// SiagroB1.Application/Services/ContractDrafts/ContractDraftsCreateService.cs
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.ContractDrafts;

/// <summary>
/// Cria a minuta: resolve os placeholders, renderiza e grava HTML + JSON como snapshot. Aceita
/// qualquer status de contrato exceto cancelado — aditivo e distrato existem justamente para
/// contrato aprovado ou encerrado. Log do contrato na mesma transação.
/// </summary>
public class ContractDraftsCreateService(
    AppDbContext context,
    ContractDraftPlaceholderResolver resolver,
    PurchaseContractsChangeLogService purchaseLog,
    SalesContractsChangeLogService salesLog,
    ILogger<ContractDraftsCreateService> logger)
{
    public async Task<ContractDraft> ExecuteAsync(
        ContractDraftContractType contractType, Guid contractKey, Guid templateKey,
        ContractDraftType draftType, string description, string userName, CancellationToken ct)
    {
        var template = await context.ContractTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.Key == templateKey, ct)
                       ?? throw new NotFoundException("Modelo de contrato não encontrado.");
        if (!template.Active)
            throw new BusinessException("Modelo inativo.");

        var draft = new ContractDraft
        {
            TemplateKey = template.Key,
            DraftType = draftType,
            Description = string.IsNullOrWhiteSpace(description) ? template.Title : description.Trim(),
            ContractCode = "",
            BodyHtml = "",
            PlaceholdersJson = "",
            Status = ContractDraftStatus.Draft,
            CreatedAt = DateTime.Now, CreatedBy = userName, UpdatedAt = DateTime.Now, UpdatedBy = userName,
        };

        Dictionary<string, string> values;

        if (contractType == ContractDraftContractType.Purchase)
        {
            var contract = await context.PurchaseContracts
                .Include(c => c.Brokers).FirstOrDefaultAsync(c => c.Key == contractKey, ct)
                ?? throw new NotFoundException("Contrato de compra não encontrado.");
            RequireNotCanceled(contract.Status);
            if (template.ContractType == ContractTemplateScope.Sales)
                throw new BusinessException("Modelo não se aplica a contrato de compra.");

            values = await resolver.ResolveAsync(contract, ct);
            draft.PurchaseContractKey = contract.Key;
            draft.ContractCode = contract.Code ?? "";
            draft.BranchCode = contract.BranchCode;
            draft.Sequence = 1 + (await context.ContractDrafts
                .Where(d => d.PurchaseContractKey == contract.Key).MaxAsync(d => (int?)d.Sequence, ct) ?? 0);
        }
        else
        {
            var contract = await context.SalesContracts
                .Include(c => c.DeliveryLocations).FirstOrDefaultAsync(c => c.Key == contractKey, ct)
                ?? throw new NotFoundException("Contrato de venda não encontrado.");
            RequireNotCanceled(contract.Status);
            if (template.ContractType == ContractTemplateScope.Purchase)
                throw new BusinessException("Modelo não se aplica a contrato de venda.");

            values = await resolver.ResolveAsync(contract, ct);
            draft.SalesContractKey = contract.Key;
            draft.ContractCode = contract.Code ?? "";
            draft.BranchCode = contract.BranchCode;
            draft.Sequence = 1 + (await context.ContractDrafts
                .Where(d => d.SalesContractKey == contract.Key).MaxAsync(d => (int?)d.Sequence, ct) ?? 0);
        }

        draft.BodyHtml = ContractDraftTemplateRenderer.Render(template.BodyHtml, values);
        draft.PlaceholdersJson = JsonSerializer.Serialize(values);

        await using var transaction = await context.Database.BeginTransactionAsync(ct);
        try
        {
            context.ContractDrafts.Add(draft);

            var what = ContractChangeLogFields.DescribeDraft(draft.Sequence, "criada");
            if (draft.PurchaseContractKey is { } pk) purchaseLog.Register(pk, ContractChangeLogFields.Draft, null, what, userName);
            if (draft.SalesContractKey is { } sk) salesLog.Register(sk, ContractChangeLogFields.Draft, null, what, userName);

            await context.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return draft;
        }
        catch (Exception e)
        {
            await transaction.RollbackAsync(ct);
            logger.LogError(e, "Falha ao criar minuta do contrato {ContractKey}", contractKey);
            throw;
        }
    }

    private static void RequireNotCanceled(ContractStatus? status)
    {
        if (status == ContractStatus.Canceled)
            throw new BusinessException("Contrato cancelado não recebe minuta.");
    }
}
```

- [ ] **Step 5: Rodar os testes do Create** → PASS (6).

- [ ] **Step 6: Testes de Update/Delete/Get/PDF**

```csharp
// SiagroB1.Application.Tests/ContractDrafts/ContractDraftsUpdateAndDeleteServiceTests.cs
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.ContractDrafts;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Tests.ContractDrafts;

public class ContractDraftsUpdateAndDeleteServiceTests
{
    private readonly ContractDraftsTestContext _ctx = new();

    private async Task<ContractDraft> DraftAsync(ContractDraftStatus status = ContractDraftStatus.Draft)
    {
        var template = await _ctx.SeedTemplateAsync();
        var contract = await _ctx.SeedPurchaseAsync();
        var draft = await _ctx.Create().ExecuteAsync(ContractDraftContractType.Purchase, contract.Key, template.Key,
            ContractDraftType.Contract, "m", "t", default);
        if (status != ContractDraftStatus.Draft)
        {
            draft.Status = status;
            await _ctx.Db.Context.SaveChangesAsync();
        }
        return draft;
    }

    [Fact]
    public async Task Update_changes_text_type_and_description_while_draft()
    {
        var draft = await DraftAsync();

        await _ctx.Update().ExecuteAsync(draft.Key, "Aditivo 1", ContractDraftType.Amendment, "<p>texto editado</p>", "editor");

        var reloaded = await _ctx.Db.Context.ContractDrafts.AsNoTracking().SingleAsync(d => d.Key == draft.Key);
        Assert.Equal("<p>texto editado</p>", reloaded.BodyHtml);
        Assert.Equal(ContractDraftType.Amendment, reloaded.DraftType);
        Assert.Equal("Aditivo 1", reloaded.Description);
        Assert.Equal("editor", reloaded.UpdatedBy);
    }

    [Theory]
    [InlineData(ContractDraftStatus.AwaitingSignature)]
    [InlineData(ContractDraftStatus.Signed)]
    [InlineData(ContractDraftStatus.Canceled)]
    public async Task Update_and_delete_are_refused_after_draft(ContractDraftStatus status)
    {
        var draft = await DraftAsync(status);

        await Assert.ThrowsAsync<BusinessException>(() => _ctx.Update().ExecuteAsync(draft.Key, "x", ContractDraftType.Contract, "<p/>", "t"));
        await Assert.ThrowsAsync<BusinessException>(() => _ctx.Delete().ExecuteAsync(draft.Key));
    }

    [Fact]
    public async Task Delete_removes_the_draft()
    {
        var draft = await DraftAsync();

        await _ctx.Delete().ExecuteAsync(draft.Key);

        Assert.Empty(_ctx.Db.Context.ContractDrafts);
    }

    [Fact]
    public async Task Update_rejects_empty_body()
    {
        var draft = await DraftAsync();

        await Assert.ThrowsAsync<BusinessException>(() => _ctx.Update().ExecuteAsync(draft.Key, "x", ContractDraftType.Contract, "  ", "t"));
    }

    [Fact]
    public async Task List_by_contract_returns_dto_without_body_and_get_body_returns_it()
    {
        var draft = await DraftAsync();

        var list = await _ctx.Get().ListByContractAsync(ContractDraftContractType.Purchase, draft.PurchaseContractKey!.Value);
        var body = await _ctx.Get().GetBodyAsync(draft.Key);

        var dto = Assert.Single(list);
        Assert.Equal(draft.Key, dto.Key);
        Assert.Equal(1, dto.Sequence);
        Assert.NotNull(dto.TemplateName);
        Assert.Contains("PC-000123", body);
    }
}
```

```csharp
// SiagroB1.Application.Tests/ContractDrafts/ContractDraftsGetPdfServiceTests.cs
using SiagroB1.Application.Services.ContractDrafts;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.ContractDrafts;

public class ContractDraftsGetPdfServiceTests
{
    private readonly ContractDraftsTestContext _ctx = new();

    [Fact]
    public async Task Renders_the_body_wrapped_in_the_layout_and_names_the_file()
    {
        var template = await _ctx.SeedTemplateAsync();
        var contract = await _ctx.SeedPurchaseAsync();
        var draft = await _ctx.Create().ExecuteAsync(ContractDraftContractType.Purchase, contract.Key, template.Key,
            ContractDraftType.Contract, "m", "t", default);

        var (bytes, fileName) = await _ctx.GetPdf().ExecuteAsync(draft.Key, default);

        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(bytes));
        Assert.Equal("PC-000123-minuta-1.pdf", fileName);
        Assert.StartsWith("<!DOCTYPE html>", _ctx.Pdf.LastHtml);
        Assert.Contains("FAZENDA BOA VISTA LTDA", _ctx.Pdf.LastHtml);
    }

    [Fact]
    public async Task Signed_draft_returns_the_archived_attachment_instead_of_rendering()
    {
        var template = await _ctx.SeedTemplateAsync();
        var contract = await _ctx.SeedPurchaseAsync();
        var draft = await _ctx.Create().ExecuteAsync(ContractDraftContractType.Purchase, contract.Key, template.Key,
            ContractDraftType.Contract, "m", "t", default);
        var attachment = new PurchaseContractAttachment
        {
            PurchaseContractKey = contract.Key, Description = "Minuta 1 assinada", FileName = "assinado.pdf",
            ContentType = "application/pdf", FileData = [1, 2, 3], CreatedAt = DateTime.Now, CreatedBy = "d4sign",
        };
        _ctx.Db.Context.PurchaseContractAttachments.Add(attachment);
        draft.Status = ContractDraftStatus.Signed;
        await _ctx.Db.Context.SaveChangesAsync();
        draft.SignedAttachmentKey = attachment.Key;
        await _ctx.Db.Context.SaveChangesAsync();

        var (bytes, fileName) = await _ctx.GetPdf().ExecuteAsync(draft.Key, default);

        Assert.Equal([1, 2, 3], bytes);
        Assert.Equal("assinado.pdf", fileName);
        Assert.Equal(0, _ctx.Pdf.Calls);
    }
}
```

- [ ] **Step 7: Update, Delete, Get e GetPdf**

```csharp
// SiagroB1.Application/Services/ContractDrafts/ContractDraftsUpdateService.cs
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.ContractDrafts;

/// <summary>Edição manual do texto renderizado, só em rascunho. Não re-resolve placeholders: o snapshot é do usuário agora.</summary>
public class ContractDraftsUpdateService(AppDbContext context, ContractDraftsLoader loader)
{
    public async Task ExecuteAsync(Guid key, string description, ContractDraftType draftType, string bodyHtml, string userName)
    {
        var draft = await loader.RequireDraftAsync(key);
        ContractDraftsLoader.RequireEditable(draft);

        if (string.IsNullOrWhiteSpace(bodyHtml))
            throw new BusinessException("O texto da minuta não pode ficar vazio.");

        draft.Description = string.IsNullOrWhiteSpace(description) ? draft.Description : description.Trim();
        draft.DraftType = draftType;
        draft.BodyHtml = bodyHtml;
        draft.UpdatedAt = DateTime.Now;
        draft.UpdatedBy = userName;

        await context.SaveChangesAsync();
    }
}
```

```csharp
// SiagroB1.Application/Services/ContractDrafts/ContractDraftsDeleteService.cs
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.ContractDrafts;

public class ContractDraftsDeleteService(AppDbContext context, ContractDraftsLoader loader)
{
    public async Task ExecuteAsync(Guid key)
    {
        var draft = await loader.RequireDraftAsync(key);
        ContractDraftsLoader.RequireEditable(draft);

        context.ContractDraftSigners.RemoveRange(draft.Signers);
        context.ContractDrafts.Remove(draft);
        await context.SaveChangesAsync();
    }
}
```

```csharp
// SiagroB1.Application/Services/ContractDrafts/ContractDraftsGetService.cs
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.ContractDrafts;

public class ContractDraftsGetService(AppDbContext context)
{
    public Task<List<ContractDraftDto>> ListByContractAsync(ContractDraftContractType contractType, Guid contractKey)
    {
        var query = contractType == ContractDraftContractType.Purchase
            ? context.ContractDrafts.Where(d => d.PurchaseContractKey == contractKey)
            : context.ContractDrafts.Where(d => d.SalesContractKey == contractKey);

        return query.AsNoTracking().OrderBy(d => d.Sequence).Select(d => new ContractDraftDto
        {
            Key = d.Key, ContractCode = d.ContractCode, Sequence = d.Sequence, TemplateKey = d.TemplateKey,
            TemplateName = d.Template!.Name, DraftType = d.DraftType, Description = d.Description, Status = d.Status,
            Provider = d.Provider, SentAt = d.SentAt, SignedAt = d.SignedAt, LastError = d.LastError,
            SignedAttachmentKey = d.SignedAttachmentKey, CreatedAt = d.CreatedAt, CreatedBy = d.CreatedBy,
            Signers = d.Signers.OrderBy(s => s.Side).ThenBy(s => s.Order).Select(s => new ContractDraftSignerDto
            {
                Key = s.Key, Side = s.Side, Name = s.Name, Email = s.Email, Role = s.Role, Order = s.Order,
                Status = s.Status, SignedAt = s.SignedAt, LastMessage = s.LastMessage,
            }).ToList(),
        }).ToListAsync();
    }

    public async Task<string> GetBodyAsync(Guid key) =>
        await context.ContractDrafts.AsNoTracking().Where(d => d.Key == key).Select(d => d.BodyHtml).FirstOrDefaultAsync()
        ?? throw new NotFoundException("Minuta não encontrada.");
}
```

```csharp
// SiagroB1.Application/Services/ContractDrafts/ContractDraftsGetPdfService.cs
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.ContractDrafts;

/// <summary>
/// PDF da minuta em qualquer status. Assinada com anexo arquivado ⇒ devolve o PDF ASSINADO do
/// anexo (tem as assinaturas e o certificado do provedor); senão renderiza o BodyHtml na hora.
/// </summary>
public class ContractDraftsGetPdfService(
    AppDbContext context,
    ContractDraftsLoader loader,
    IHtmlToPdfRenderer pdf,
    PurchaseContractsAttachmentsGetService purchaseAttachments,
    SalesContractsAttachmentsGetService salesAttachments)
{
    public async Task<(byte[] Bytes, string FileName)> ExecuteAsync(Guid key, CancellationToken ct)
    {
        var draft = await loader.RequireDraftAsync(key, ct);

        if (draft.Status == ContractDraftStatus.Signed && draft.SignedAttachmentKey is { } attachmentKey)
        {
            if (draft.PurchaseContractKey.HasValue)
            {
                var a = await purchaseAttachments.GetByKey(attachmentKey);
                if (a is not null) return (a.FileData, a.FileName);
            }
            else
            {
                var a = await salesAttachments.GetByKey(attachmentKey);
                if (a is not null) return (a.FileData, a.FileName);
            }
        }

        var title = draft.Template?.Title ?? context.ContractTemplates
            .Where(t => t.Key == draft.TemplateKey).Select(t => t.Title).FirstOrDefault() ?? "Minuta";
        var bytes = await pdf.RenderAsync(ContractDraftPdfLayout.Wrap(draft.BodyHtml, title), ct);

        return (bytes, $"{draft.ContractCode}-minuta-{draft.Sequence}.pdf");
    }
}
```

- [ ] **Step 8: Rodar tudo de minuta**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~ContractDrafts"` → PASS.

- [ ] **Step 9: Commit**

```bash
git add SiagroB1.Domain/Dtos/ContractDraftDto.cs SiagroB1.Domain/Dtos/ContractDraftSignerDto.cs SiagroB1.Application/Services/ContractDrafts SiagroB1.Application.Tests/ContractDrafts
git commit -m "feat(platform): criar, editar, excluir e imprimir minutas de contrato

A minuta é fotografia do contrato: HTML renderizado e JSON dos valores no
momento da criação, sequência por contrato, log no contrato na mesma transação.
Só rascunho aceita edição; assinada devolve o PDF arquivado, não uma nova
renderização.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 7: Actions e functions OData da minuta, DI e testes de EDM

**Files:**
- Create: `SiagroB1.Web/Actions/ContractDrafts/ContractDraftActionParameters.cs`
- Create: `SiagroB1.Web/Actions/ContractDrafts/ContractDraftsCreateController.cs`, `ContractDraftsUpdateController.cs`, `ContractDraftsDeleteController.cs`
- Create: `SiagroB1.Web/Functions/ContractDrafts/ContractDraftsListByContractController.cs`, `ContractDraftsGetBodyController.cs`, `ContractDraftsDownloadPdfController.cs`
- Create: `SiagroB1.Web/Controllers/ContractDraftsController.cs` (entity set só leitura)
- Modify: `SiagroB1.Web/ODataConfig/ODataConfigurations.cs`, `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs`
- Test: `SiagroB1.Application.Tests/ContractDrafts/ContractDraftEdmModelTests.cs`

**Interfaces:**
- Consumes: Task 6.
- Produces: entity sets `ContractDrafts`, `ContractDraftSigners`; actions `ContractDraftsCreate(ContractType, ContractKey, TemplateKey, DraftType, Description)`, `ContractDraftsUpdate(Key, Description, DraftType, BodyHtml)`, `ContractDraftsDelete(Key)`; functions `ContractDraftsListByContract(ContractType, ContractKey)`, `ContractDraftsGetBody(Key)`, `ContractDraftsDownloadPdf(Key)`.

- [ ] **Step 1: Teste do EDM (falha até o Step 4)**

```csharp
// SiagroB1.Application.Tests/ContractDrafts/ContractDraftEdmModelTests.cs
using Microsoft.OData.Edm;
using Microsoft.OData.ModelBuilder;
using SiagroB1.Web.ODataConfig;

namespace SiagroB1.Application.Tests.ContractDrafts;

public class ContractDraftEdmModelTests
{
    private static IEdmModel BuildModel()
    {
        var builder = new ODataConventionModelBuilder { Namespace = "SIAGROB1" };
        builder.ConfigureODataEntities();
        return builder.GetEdmModel();
    }

    [Theory]
    [InlineData("ContractDrafts")]
    [InlineData("ContractDraftSigners")]
    public void The_entity_sets_are_exposed(string entitySet) =>
        Assert.NotNull(BuildModel().EntityContainer.FindEntitySet(entitySet));

    [Theory]
    [InlineData("ContractDraftsCreate", "ContractType,ContractKey,TemplateKey,DraftType,Description")]
    [InlineData("ContractDraftsUpdate", "Key,Description,DraftType,BodyHtml")]
    [InlineData("ContractDraftsDelete", "Key")]
    public void The_actions_declare_their_parameters(string action, string parameters)
    {
        var edmAction = BuildModel().SchemaElements.OfType<IEdmAction>().Single(a => a.Name == action);
        foreach (var parameter in parameters.Split(','))
            Assert.Contains(edmAction.Parameters, p => p.Name == parameter);
    }

    [Theory]
    [InlineData("ContractDraftsListByContract", "ContractType,ContractKey")]
    [InlineData("ContractDraftsGetBody", "Key")]
    [InlineData("ContractDraftsDownloadPdf", "Key")]
    public void The_functions_declare_their_parameters(string function, string parameters)
    {
        var edmFunction = BuildModel().SchemaElements.OfType<IEdmFunction>().Single(f => f.Name == function);
        foreach (var parameter in parameters.Split(','))
            Assert.Contains(edmFunction.Parameters, p => p.Name == parameter);
    }

    [Fact]
    public void Enums_travel_as_string()
    {
        var create = BuildModel().SchemaElements.OfType<IEdmAction>().Single(a => a.Name == "ContractDraftsCreate");
        Assert.Equal("Edm.String", create.Parameters.Single(p => p.Name == "ContractType").Type.FullName());
        Assert.Equal("Edm.String", create.Parameters.Single(p => p.Name == "DraftType").Type.FullName());
    }
}
```

- [ ] **Step 2: Parser de parâmetros e controllers de action**

```csharp
// SiagroB1.Web/Actions/ContractDrafts/ContractDraftActionParameters.cs
using Microsoft.AspNetCore.OData.Formatter;
using SiagroB1.Application.Services.ContractDrafts;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Web.Actions.ContractDrafts;

/// <summary>Leitura dos parâmetros das actions de minuta. Enums chegam como string.</summary>
public static class ContractDraftActionParameters
{
    public const string InvalidContractTypeMessage = "Tipo de contrato inválido. Use Purchase ou Sales.";
    public const string InvalidDraftTypeMessage = "Tipo de minuta inválido. Use Contract, Amendment ou Termination.";

    public static bool TryGetGuid(ODataActionParameters? p, string name, out Guid value)
    {
        value = Guid.Empty;
        return p is not null && p.TryGetValue(name, out var obj) && obj is Guid g && (value = g) != Guid.Empty;
    }

    public static string? GetString(ODataActionParameters? p, string name) =>
        p is not null && p.TryGetValue(name, out var obj) ? obj?.ToString() : null;

    public static bool TryGetContractType(ODataActionParameters? p, out ContractDraftContractType type) =>
        Enum.TryParse(GetString(p, "ContractType"), true, out type);

    /// <summary>Ausente ⇒ Contract; presente e inválido ⇒ false.</summary>
    public static bool TryGetDraftType(ODataActionParameters? p, out ContractDraftType type)
    {
        type = ContractDraftType.Contract;
        var text = GetString(p, "DraftType");
        return string.IsNullOrWhiteSpace(text) || Enum.TryParse(text, true, out type);
    }
}
```

```csharp
// SiagroB1.Web/Actions/ContractDrafts/ContractDraftsCreateController.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.ContractDrafts;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.ContractDrafts;

public class ContractDraftsCreateController(ContractDraftsCreateService service) : ODataController
{
    [HttpPost("odata/ContractDraftsCreate")]
    public async Task<IActionResult> Create([FromBody] ODataActionParameters parameters, CancellationToken ct)
    {
        // parameters chega NULO quando nenhum parâmetro do EDM é enviado.
        if (!ContractDraftActionParameters.TryGetContractType(parameters, out var contractType))
            return BadRequest(ContractDraftActionParameters.InvalidContractTypeMessage);
        if (!ContractDraftActionParameters.TryGetGuid(parameters, "ContractKey", out var contractKey))
            return BadRequest("Contrato é obrigatório.");
        if (!ContractDraftActionParameters.TryGetGuid(parameters, "TemplateKey", out var templateKey))
            return BadRequest("Modelo é obrigatório.");
        if (!ContractDraftActionParameters.TryGetDraftType(parameters, out var draftType))
            return BadRequest(ContractDraftActionParameters.InvalidDraftTypeMessage);

        var description = ContractDraftActionParameters.GetString(parameters, "Description") ?? "";
        var userName = User.Identity?.Name ?? "Unknown";

        try
        {
            var draft = await service.ExecuteAsync(contractType, contractKey, templateKey, draftType, description, userName, ct);
            return Ok(new { draft.Key, draft.Sequence });
        }
        catch (Exception e)
        {
            if (e is NotFoundException or KeyNotFoundException) return NotFound(e.Message);
            if (e is DefaultException or BusinessException or ApplicationException) return BadRequest(e.Message);
            return StatusCode(500, e.Message);
        }
    }
}
```

```csharp
// SiagroB1.Web/Actions/ContractDrafts/ContractDraftsUpdateController.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.ContractDrafts;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.ContractDrafts;

public class ContractDraftsUpdateController(ContractDraftsUpdateService service) : ODataController
{
    [HttpPost("odata/ContractDraftsUpdate")]
    public async Task<IActionResult> Update([FromBody] ODataActionParameters parameters)
    {
        if (!ContractDraftActionParameters.TryGetGuid(parameters, "Key", out var key))
            return BadRequest("Minuta é obrigatória.");
        if (!ContractDraftActionParameters.TryGetDraftType(parameters, out var draftType))
            return BadRequest(ContractDraftActionParameters.InvalidDraftTypeMessage);

        var description = ContractDraftActionParameters.GetString(parameters, "Description") ?? "";
        var bodyHtml = ContractDraftActionParameters.GetString(parameters, "BodyHtml") ?? "";
        var userName = User.Identity?.Name ?? "Unknown";

        try
        {
            await service.ExecuteAsync(key, description, draftType, bodyHtml, userName);
            return Ok();
        }
        catch (Exception e)
        {
            if (e is NotFoundException or KeyNotFoundException) return NotFound(e.Message);
            if (e is DefaultException or BusinessException or ApplicationException) return BadRequest(e.Message);
            return StatusCode(500, e.Message);
        }
    }
}
```

```csharp
// SiagroB1.Web/Actions/ContractDrafts/ContractDraftsDeleteController.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.ContractDrafts;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.ContractDrafts;

public class ContractDraftsDeleteController(ContractDraftsDeleteService service) : ODataController
{
    [HttpPost("odata/ContractDraftsDelete")]
    public async Task<IActionResult> Delete([FromBody] ODataActionParameters parameters)
    {
        if (!ContractDraftActionParameters.TryGetGuid(parameters, "Key", out var key))
            return BadRequest("Minuta é obrigatória.");

        try
        {
            await service.ExecuteAsync(key);
            return NoContent();
        }
        catch (Exception e)
        {
            if (e is NotFoundException or KeyNotFoundException) return NotFound(e.Message);
            if (e is DefaultException or BusinessException or ApplicationException) return BadRequest(e.Message);
            return StatusCode(500, e.Message);
        }
    }
}
```

- [ ] **Step 3: Functions e entity set só leitura**

```csharp
// SiagroB1.Web/Functions/ContractDrafts/ContractDraftsListByContractController.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.ContractDrafts;
using SiagroB1.Domain.Dtos;
using SiagroB1.Web.Actions.ContractDrafts;

namespace SiagroB1.Web.Functions.ContractDrafts;

public class ContractDraftsListByContractController(ContractDraftsGetService service) : ODataController
{
    [HttpGet("odata/ContractDraftsListByContract(ContractType={contractType},ContractKey={contractKey})")]
    public async Task<ActionResult<ICollection<ContractDraftDto>>> List([FromRoute] string contractType, [FromRoute] Guid contractKey)
    {
        if (!Enum.TryParse<ContractDraftContractType>(contractType?.Trim('\''), true, out var type))
            return BadRequest(ContractDraftActionParameters.InvalidContractTypeMessage);

        return Ok(await service.ListByContractAsync(type, contractKey));
    }
}
```

```csharp
// SiagroB1.Web/Functions/ContractDrafts/ContractDraftsGetBodyController.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.ContractDrafts;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Functions.ContractDrafts;

public class ContractDraftsGetBodyController(ContractDraftsGetService service) : ODataController
{
    [HttpGet("odata/ContractDraftsGetBody(Key={key})")]
    public async Task<ActionResult<string>> GetBody([FromRoute] Guid key)
    {
        try { return Ok(await service.GetBodyAsync(key)); }
        catch (NotFoundException e) { return NotFound(e.Message); }
    }
}
```

```csharp
// SiagroB1.Web/Functions/ContractDrafts/ContractDraftsDownloadPdfController.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.ContractDrafts;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Functions.ContractDrafts;

public class ContractDraftsDownloadPdfController(ContractDraftsGetPdfService service) : ODataController
{
    [HttpGet("odata/ContractDraftsDownloadPdf(Key={key})")]
    public async Task<ActionResult> Download([FromRoute] Guid key, CancellationToken ct)
    {
        try
        {
            var (bytes, fileName) = await service.ExecuteAsync(key, ct);
            return File(bytes, "application/pdf", fileName);
        }
        catch (NotFoundException e) { return NotFound(e.Message); }
        catch (BusinessException e) { return BadRequest(e.Message); }
    }
}
```

```csharp
// SiagroB1.Web/Controllers/ContractDraftsController.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra.Context;

namespace SiagroB1.Web.Controllers;

/// <summary>Só leitura: toda mutação de minuta passa pelas actions (guardas de status).</summary>
public class ContractDraftsController(AppDbContext context) : ODataController
{
    [EnableQuery]
    public ActionResult<IQueryable<ContractDraft>> Get() => Ok(context.ContractDrafts.AsNoTracking());
}
```

- [ ] **Step 4: EDM e DI**

Em `ODataConfigurations.cs`, após as linhas da Task 4:

```csharp
        modelBuilder.EntitySet<ContractDraft>("ContractDrafts");
        modelBuilder.EntitySet<ContractDraftSigner>("ContractDraftSigners");

        var contractDraftsCreate = modelBuilder.Action("ContractDraftsCreate");
        contractDraftsCreate.Parameter<string>("ContractType");
        contractDraftsCreate.Parameter<Guid>("ContractKey");
        contractDraftsCreate.Parameter<Guid>("TemplateKey");
        contractDraftsCreate.Parameter<string>("DraftType").Optional();
        contractDraftsCreate.Parameter<string>("Description").Optional();
        contractDraftsCreate.Returns<IActionResult>();

        var contractDraftsUpdate = modelBuilder.Action("ContractDraftsUpdate");
        contractDraftsUpdate.Parameter<Guid>("Key");
        contractDraftsUpdate.Parameter<string>("Description").Optional();
        contractDraftsUpdate.Parameter<string>("DraftType").Optional();
        contractDraftsUpdate.Parameter<string>("BodyHtml");
        contractDraftsUpdate.Returns<IActionResult>();

        var contractDraftsDelete = modelBuilder.Action("ContractDraftsDelete");
        contractDraftsDelete.Parameter<Guid>("Key");
        contractDraftsDelete.Returns<IActionResult>();

        var contractDraftsListByContract = modelBuilder.Function("ContractDraftsListByContract");
        contractDraftsListByContract.Parameter<string>("ContractType");
        contractDraftsListByContract.Parameter<Guid>("ContractKey");
        contractDraftsListByContract.ReturnsCollection<ContractDraftDto>();

        var contractDraftsGetBody = modelBuilder.Function("ContractDraftsGetBody");
        contractDraftsGetBody.Parameter<Guid>("Key");
        contractDraftsGetBody.Returns<string>();

        var contractDraftsDownloadPdf = modelBuilder.Function("ContractDraftsDownloadPdf");
        contractDraftsDownloadPdf.Parameter<Guid>("Key");
        contractDraftsDownloadPdf.Returns<IActionResult>();
```

Se `ReturnsCollection<ContractDraftDto>` reclamar de tipo sem chave, use `modelBuilder.ComplexType<ContractDraftDto>()` e `ComplexType<ContractDraftSignerDto>()` antes — os DTOs de anexo funcionam sem isso porque só têm primitivos; este tem uma coleção aninhada.

Em `AddApplicationServices()`, após o bloco da Task 4:

```csharp
        // contract drafts (minutas — fase 1)
        services.AddScoped<ContractDraftPlaceholderResolver>();
        services.AddScoped<ContractDraftsLoader>();
        services.AddScoped<ContractDraftsCreateService>();
        services.AddScoped<ContractDraftsUpdateService>();
        services.AddScoped<ContractDraftsDeleteService>();
        services.AddScoped<ContractDraftsGetService>();
        services.AddScoped<ContractDraftsGetPdfService>();
```

- [ ] **Step 5: Build, testes e fumaça manual**

Run: `dotnet build SiagroB1.sln` e `dotnet test SiagroB1.Application.Tests` (suíte inteira) → PASS.
Suba o Web e, com um contrato de compra existente em homologação: `POST /odata/ContractDraftsCreate` `{"ContractType":"Purchase","ContractKey":"<guid>","TemplateKey":"<guid>","DraftType":"Contract","Description":"teste"}` → 200 com `Key`/`Sequence`; `GET /odata/ContractDraftsListByContract(ContractType='Purchase',ContractKey=<guid>)` lista; `GET /odata/ContractDraftsDownloadPdf(Key=<guid>)` baixa um PDF que abre (precisa de `Signature:Pdf:ChromiumPath` apontando para o Chrome local no `appsettings.Development.json` da sua máquina — não comite).

- [ ] **Step 6: Commit**

```bash
git add SiagroB1.Web SiagroB1.Application.Tests/ContractDrafts/ContractDraftEdmModelTests.cs
git commit -m "feat(platform): expor minutas por actions e functions OData

Entity set só leitura; criar, editar e excluir passam por action porque a guarda
de status mora no serviço. PDF e corpo por function, como os anexos.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 8: Menus (CommonContext) e mensagens

**Files:**
- Create: `SiagroB1.Migrations/CommonContext/<timestamp>_AddContractTemplateMenus.cs`

**Interfaces:**
- Produces: itens `contractTemplates` e `companySignatories` em `MENU_ITEMS` sob `registers`, liberados para `ADMIN`. As chaves PRECISAM ser iguais aos nomes das rotas no `manifest.json` do frontend (plano do frontend usa estes dois nomes).

- [ ] **Step 1: Descobrir a próxima ordem do grupo "registers"**

```powershell
grep -rh '"registers"' SiagroB1.Migrations/CommonContext/*.cs | Select-String -Pattern ', (\d+), "registers"' | ForEach-Object { $_.Matches[0].Groups[1].Value } | Sort-Object {[int]$_} | Select-Object -Last 1
```

Anote `N`. Se a busca não achar nada (formatação diferente), consulte o banco: `SELECT MAX([Order]) FROM MENU_ITEMS WHERE ParentKey = 'registers'`.

- [ ] **Step 2: Migration vazia e conteúdo**

```powershell
dotnet ef migrations add AddContractTemplateMenus --project SiagroB1.Migrations --startup-project SiagroB1.Web --context CommonDbContext
```

Substitua o corpo gerado por (troque `N+1`/`N+2` pelos números e gere dois GUIDs novos com `[guid]::NewGuid().ToString().ToUpper()`):

```csharp
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.CommonContext
{
    /// <summary>
    /// Menus das minutas (fase 1): modelos de contrato e signatários da empresa, no grupo
    /// "Cadastros" (registers). Signatários do parceiro não têm menu — são aba do parceiro.
    /// A Key de cada item PRECISA ser igual ao name da rota no manifest.json do frontend.
    /// </summary>
    public partial class AddContractTemplateMenus : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "MENU_ITEMS",
                columns: ["Key", "Title", "Icon", "Enabled", "Expanded", "Order", "ParentKey"],
                values: new object[,]
                {
                    { "contractTemplates", "Modelos de Contrato", "sap-icon://document-text", true, false, N+1, "registers" },
                    { "companySignatories", "Signatários da Empresa", "sap-icon://signature", true, false, N+2, "registers" },
                });

            migrationBuilder.InsertData(
                table: "ROLE_MENUS",
                columns: ["Id", "RoleCode", "MenuItemKey"],
                values: new object[,]
                {
                    { "<GUID-1>", "ADMIN", "contractTemplates" },
                    { "<GUID-2>", "ADMIN", "companySignatories" },
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(table: "ROLE_MENUS", keyColumn: "Id", keyValues: ["<GUID-1>", "<GUID-2>"]);
            migrationBuilder.DeleteData(table: "MENU_ITEMS", keyColumn: "Key", keyValues: ["contractTemplates", "companySignatories"]);
        }
    }
}
```

- [ ] **Step 3: Aplicar e conferir**

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Yokotobi-Development'
dotnet ef database update --project SiagroB1.Migrations --startup-project SiagroB1.Web --context CommonDbContext
```

Suba Gateway + Web, faça login como ADMIN e confira que `GET /security/menu` (ou o endpoint que `MenuService` expõe — veja `SiagroB1.Security/Services/MenuService.cs`) traz os dois itens sob "Cadastros".

- [ ] **Step 4: Commit**

```bash
git add SiagroB1.Migrations/CommonContext
git commit -m "feat(platform): adicionar menus de modelos de contrato e signatários

Chaves iguais às rotas que o frontend vai declarar (contractTemplates,
companySignatories). Só ADMIN; demais perfis configuram na tela.

DB: AddContractTemplateMenus

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

## Verificação final da fase

1. `dotnet build SiagroB1.sln` sem warnings novos; `dotnet test SiagroB1.Application.Tests` inteira verde.
2. Fluxo manual em homologação (`Yokotobi-Development`, modo SAPB1, e `Development`, modo STANDALONE): criar modelo com placeholders → 400 ao usar `{{xpto}}` → cadastrar um signatário da empresa e um do parceiro → criar minuta de compra e de venda → `ListByContract` mostra sequência e nome do modelo → editar corpo → PDF abre com nome do parceiro, CNPJ formatado e bloco de assinaturas → excluir. Em SAPB1, o nome/CNPJ do parceiro tem de vir do SAP (tabela local vazia).
3. `git log --oneline main..feature/contract-drafts-esignature` mostra 8 commits desta fase + o da spec, cada um com o rodapé exigido e `DB:` nos dois que têm migration.
4. Só então escrever a Fase 2 (`superpowers:writing-plans` sobre a seção "Provedor de assinatura / Webhook / Reconciliação" da spec).
