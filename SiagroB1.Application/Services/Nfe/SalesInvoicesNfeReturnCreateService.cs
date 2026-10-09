using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Application.Services.SalesInvoices.Factories;
using SiagroB1.Application.Services.Taxes;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Infra.Enums;

namespace SiagroB1.Application.Services.Nfe;

public sealed record SalesInvoiceNfeReturnItem(Guid OriginItemKey, decimal Quantity);

public sealed record SalesInvoiceNfeReturnRequest(
    Guid SalesInvoiceKey, IReadOnlyList<SalesInvoiceNfeReturnItem> Items, string Reason);

/// <summary>
/// "Devolver" (spec §6): a partir de uma venda autorizada pelo Siagro, cria a devolução Pendente que
/// sai com NF-e PRÓPRIA de entrada (finalidade 4). Não confirma: quem confirma é a emissão — e é na
/// confirmação que a venda recebe a quantidade devolvida, como em toda devolução.
/// </summary>
public class SalesInvoicesNfeReturnCreateService(
    IUnitOfWork db,
    TaxCalculationGate gate,
    SalesInvoicesCreateService createService,
    SalesInvoiceNfeReturnBuilder builder,
    ILogger<SalesInvoicesNfeReturnCreateService> logger)
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public async Task<SalesInvoice> ExecuteAsync(SalesInvoiceNfeReturnRequest request, string userName)
    {
        var origin = await db.Context.SalesInvoices
                         .Include(i => i.Items)
                         .Include(i => i.SalesTransactions)
                         .FirstOrDefaultAsync(i => i.Key == request.SalesInvoiceKey)
                     ?? throw new NotFoundException("Documento de saída não encontrado.");

        // TODA a validação antes de qualquer escrita.
        await ValidateOriginAsync(origin);

        if (string.IsNullOrWhiteSpace(request.Reason))
            throw new DefaultException("Informe o motivo da devolução.");

        var quantities = await ResolveQuantitiesAsync(origin, request.Items);
        var returnInvoice = await builder.BuildAsync(
            origin, quantities,
            $"{SalesInvoiceNfeReturnBuilder.ReferenceText(origin)} Motivo: {request.Reason.Trim()}",
            userName);

        try
        {
            await db.BeginTransactionAsync();

            await createService.ExecuteAsync(returnInvoice, userName, CommitMode.Deferred, nfeReturn: true);

            await db.SaveChangesAsync();
            await db.CommitAsync();

            return returnInvoice;
        }
        catch (Exception e)
        {
            await db.RollbackAsync();
            logger.LogError(e, "Erro ao criar a devolução do documento de saída {Number}", origin.InvoiceNumber);
            throw;
        }
    }

    private async Task ValidateOriginAsync(SalesInvoice origin)
    {
        if (!await gate.IsActiveAsync(origin.BranchCode))
            throw new DefaultException($"A filial {origin.BranchCode} não emite NF-e pelo Siagro.");

        if (origin.InvoiceType != SalesInvoiceType.Normal)
            throw new DefaultException("Só um documento de venda (Normal) pode ser devolvido por aqui.");

        SalesInvoiceNfeReturnBuilder.EnsureOriginAuthorized(origin);

        if (origin.ShipmentLoadKey != null || origin.SalesTransactions.Count > 0)
            throw new DefaultException("Documento com romaneio/carga: a devolução com NF-e ainda não é suportada.");
    }

    private async Task<Dictionary<Guid, decimal>> ResolveQuantitiesAsync(
        SalesInvoice origin, IReadOnlyList<SalesInvoiceNfeReturnItem> items)
    {
        if (items.Any(i => i.Quantity < 0))
            throw new DefaultException("A quantidade a devolver não pode ser negativa.");

        var returned = await SalesInvoiceNfeReturnBalance.ReturnedByOriginItemAsync(db.Context, origin.Key, null);
        var result = new Dictionary<Guid, decimal>();

        foreach (var requested in items.Where(i => i.Quantity > 0))
        {
            var sold = origin.Items.FirstOrDefault(i => i.Key == requested.OriginItemKey)
                       ?? throw new DefaultException("Item informado não pertence ao documento de saída.");
            var available = sold.Quantity - returned.GetValueOrDefault(requested.OriginItemKey);

            if (requested.Quantity > available)
                throw new DefaultException(
                    $"Item {sold.ItemCode}: a quantidade a devolver ({requested.Quantity.ToString("N3", PtBr)}) " +
                    $"passa do saldo devolvível ({available.ToString("N3", PtBr)}).");

            result[requested.OriginItemKey] = requested.Quantity;
        }

        return result.Count == 0
            ? throw new DefaultException("Informe a quantidade a devolver de ao menos um item.")
            : result;
    }
}
