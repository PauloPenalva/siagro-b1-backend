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
