using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Infra.Enums;

namespace SiagroB1.Application.Tests.Support;

/// <summary>
/// Confirmação simulada: muda o rastreador como a real (status Confirmado) e, com
/// <paramref name="failWith"/>, estoura DEPOIS de mexer — é o que prova que o tratamento da
/// falha não grava confirmação pela metade.
/// </summary>
public sealed class RecordingConfirmService(UnitOfWork db, Exception? failWith = null)
    : SalesInvoicesConfirmService(null!, null!, null!, null!, null!, null!, null!, null!, null!)
{
    public int Calls { get; private set; }

    public override async Task ExecuteAsync(
        Guid key, string userName, CommitMode commitMode = CommitMode.Auto,
        IReadOnlyDictionary<Guid, StorageTransactionsStatus>? shipmentOutcomes = null)
    {
        Calls++;

        var invoice = await db.Context.SalesInvoices.FirstAsync(i => i.Key == key);
        invoice.InvoiceStatus = InvoiceStatus.Confirmed;

        if (failWith is not null)
            throw failWith;

        invoice.ApprovedBy = userName;
        await db.SaveChangesAsync();
    }
}
