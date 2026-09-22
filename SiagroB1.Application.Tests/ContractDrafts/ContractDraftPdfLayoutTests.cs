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
