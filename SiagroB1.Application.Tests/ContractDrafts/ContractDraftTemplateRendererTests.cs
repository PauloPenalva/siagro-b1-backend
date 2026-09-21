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
