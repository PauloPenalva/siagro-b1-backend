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

    [Fact]
    public async Task Delete_removes_a_template_no_draft_uses()
    {
        var created = await Service().CreateAsync(Template("{{numero}}"));

        Assert.True(await Service().DeleteAsync(created.Key));
        Assert.Empty(_db.Context.ContractTemplates);
    }

    /// <summary>
    /// A FK CONTRACT_DRAFTS -> CONTRACT_TEMPLATES e NoAction: sem esta guarda o banco recusa, o
    /// BaseService engole a excecao e o usuario recebe "Error deleting entity." em ingles.
    /// </summary>
    [Fact]
    public async Task Delete_refuses_a_template_in_use_and_says_how_many_drafts()
    {
        var created = await Service().CreateAsync(Template("{{numero}}"));

        _db.Context.ContractDrafts.Add(new ContractDraft
        {
            TemplateKey = created.Key,
            PurchaseContractKey = Guid.NewGuid(),
            ContractCode = "CC0001",
            Sequence = 1,
            Description = "Minuta 1",
            BodyHtml = "<p>x</p>",
            PlaceholdersJson = "{}",
        });
        await _db.Context.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() => Service().DeleteAsync(created.Key));

        Assert.Contains("1", ex.Message);
        Assert.Contains("minuta", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Single(_db.Context.ContractTemplates);
    }
}
