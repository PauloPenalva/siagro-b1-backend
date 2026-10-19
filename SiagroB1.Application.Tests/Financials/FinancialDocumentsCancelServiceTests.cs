using SiagroB1.Application.Services.Financials;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Financials;

/// <summary>
/// <see cref="FinancialDocumentsCancelService.ExecuteAsync"/> — o ponto de entrada da tela, não os
/// dois Enqueue* usados pelos ganchos de contrato (esses já são cobertos em
/// <see cref="FinancialDocumentUndoHooksTests"/>). A trava importante aqui é a Regra 12 do spec:
/// recusar cancelar um documento com baixas, porque cancelar apaga o vencimento mas SettledAmount
/// continua contando dinheiro que já saiu — cancelar sem estornar as baixas primeiro rasgaria o
/// rastro contábil.
/// </summary>
public class FinancialDocumentsCancelServiceTests
{
    private readonly IUnitOfWork _db = TestDb.CreateUnitOfWork();

    private FinancialDocumentsCancelService Service() => new(_db);

    private async Task<FinancialDocument> SeedAsync(
        FinancialDocumentNature nature = FinancialDocumentNature.Provisional,
        FinancialDocumentStatus status = FinancialDocumentStatus.Open,
        decimal settledAmount = 0m)
    {
        var document = new FinancialDocument
        {
            Key = Guid.NewGuid(),
            Code = "FN000001",
            CardCode = "F0001",
            Direction = FinancialDirection.Payable,
            Nature = nature,
            Status = status,
            DueDate = DateTime.Today.AddDays(30),
            NetAmount = 1000m,
            SettledAmount = settledAmount
        };

        _db.Context.FinancialDocuments.Add(document);
        await _db.Context.SaveChangesAsync();
        return document;
    }

    [Fact]
    public async Task Refuses_a_blank_reason()
    {
        var document = await SeedAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(document.Key, "   ", "tester"));

        Assert.Contains("motivo", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Refuses_an_already_canceled_document()
    {
        var document = await SeedAsync(status: FinancialDocumentStatus.Canceled);

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(document.Key, "Duplicado", "tester"));

        Assert.Contains(document.Code!, error.Message);
    }

    // A trava importante: uma vez que dinheiro saiu (SettledAmount != 0), cancelar sem estornar
    // primeiro apagaria o vencimento e deixaria a baixa órfã — o usuário precisa desfazer a baixa
    // antes.
    [Fact]
    public async Task Refuses_a_document_that_has_settlements()
    {
        var document = await SeedAsync(nature: FinancialDocumentNature.Advance, settledAmount: 1000m);

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(document.Key, "Motivo qualquer", "tester"));

        Assert.Contains("possui baixa financeira", error.Message);
    }

    // Só título lançado à mão sai pela tela: o gerado pelo sistema nasce e morre com a sua origem.
    [Theory]
    [InlineData(FinancialDocumentOrigin.PurchaseContractPriceFixation)]
    [InlineData(FinancialDocumentOrigin.SalesContractPriceFixation)]
    [InlineData(FinancialDocumentOrigin.FinancialDocument)]
    public async Task Refuses_a_document_not_created_manually(FinancialDocumentOrigin origin)
    {
        var document = await SeedAsync();
        document.OriginType = origin;
        await _db.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(document.Key, "Motivo qualquer", "tester"));

        Assert.Contains("gerado automaticamente", error.Message);
    }

    // Baixa vence a origem: a mensagem que o usuário vê é a da baixa.
    [Fact]
    public async Task Settled_generated_document_reports_the_settlement()
    {
        var document = await SeedAsync(settledAmount: 10m);
        document.OriginType = FinancialDocumentOrigin.PurchaseContractPriceFixation;
        await _db.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(document.Key, "Motivo qualquer", "tester"));

        Assert.Contains("possui baixa financeira", error.Message);
    }

    [Fact]
    public async Task Cancels_an_open_document_and_the_row_survives()
    {
        var document = await SeedAsync();

        await Service().ExecuteAsync(document.Key, "Contrato cancelado", "tester");

        Assert.Equal(FinancialDocumentStatus.Canceled, document.Status);
        Assert.Equal("Contrato cancelado", document.CancellationReason);
        Assert.Equal("tester", document.CanceledBy);
        Assert.NotNull(document.CanceledAt);

        // Prova de sobrevivência: um Remove também deixaria as asserções acima inalcançáveis
        // (a query teria lançado antes), então busca de novo pela chave para provar que a linha
        // continua no banco — CANCELA, nunca apaga.
        var reloaded = await _db.Context.FinancialDocuments.FindAsync(document.Key);
        Assert.NotNull(reloaded);
        Assert.Equal(FinancialDocumentStatus.Canceled, reloaded!.Status);
    }
}
