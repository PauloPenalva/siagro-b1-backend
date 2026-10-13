using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Fiscal.Payments;

namespace SiagroB1.Application.Tests.Nfe;

/// <summary>Nota de custódia não tem pagamento (tPag 90); a devolução simbólica referencia a entrada (spec §4.2, §4.3).</summary>
public class StorageCustodyNfeTests
{
    private static readonly DateTimeOffset IssuedAt = new(2026, 10, 12, 10, 0, 0, TimeSpan.FromHours(-3));
    private const string EntryKey = "35261068583898000101550090000000011990112066";

    private static async Task<(SalesInvoice Invoice, NfeIssueContext Context, NfeOptions Options)> PrepareAsync(StorageCustodyRole role)
    {
        var scenario = await NfeTestSeed.SeedAsync();
        var invoice = await scenario.Db.Context.SalesInvoices.Include(i => i.Items).SingleAsync(i => i.Key == scenario.InvoiceKey);
        invoice.TaxDocumentNumber = "000000001";
        invoice.TaxDocumentSeries = "1";
        invoice.NfeRandomCode = "48151623";
        invoice.CustodyRole = role;
        invoice.PaymentConditionCode = null;
        await scenario.Db.SaveChangesAsync();

        var options = new NfeOptions(NfeTestSeed.Config());
        // Sem condição de pagamento e sem recusa: a nota de custódia não tem pagamento.
        var context = await new NfeReadinessValidator(scenario.Db, options).ValidateAsync(invoice);
        return (invoice, context, options);
    }

    [Fact]
    public async Task Symbolic_return_references_the_processing_entry_and_has_no_payment()
    {
        var (invoice, context, options) = await PrepareAsync(StorageCustodyRole.ProcessingSymbolicReturn);

        var input = NfeIssueInputAssembler.Build(invoice, context with { CustodyReferencedKey = EntryKey }, IssuedAt, options.TechnicalResponsible);

        Assert.Equal([EntryKey], input.ReferencedKeys);
        Assert.Equal(PaymentMeansCodes.NoPayment, input.Payment.PaymentMeans);
    }

    [Fact]
    public async Task Storage_return_has_no_reference_and_no_payment()
    {
        var (invoice, context, options) = await PrepareAsync(StorageCustodyRole.StorageReturn);

        var input = NfeIssueInputAssembler.Build(invoice, context, IssuedAt, options.TechnicalResponsible);

        Assert.Empty(input.ReferencedKeys);
        Assert.Equal(PaymentMeansCodes.NoPayment, input.Payment.PaymentMeans);
    }
}
