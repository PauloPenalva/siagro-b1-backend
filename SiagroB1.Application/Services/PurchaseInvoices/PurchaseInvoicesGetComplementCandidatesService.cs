using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.PurchaseInvoices;

/// <summary>
/// Itens que o complemento de valor próprio pode apontar (spec 2026-10-14, regras 2 e 3): entradas próprias Normais
/// (inclusive contra-nota) com NF-e autorizada, não canceladas, do produtor e da filial do cabeçalho. Com ou sem
/// contrato (D5): sem contrato, o operador escolhe o contrato na linha.
/// </summary>
public class PurchaseInvoicesGetComplementCandidatesService(IUnitOfWork db)
{
    public async Task<IReadOnlyList<PurchaseInvoiceComplementCandidateDto>> ExecuteAsync(string? cardCode, string? branchCode)
    {
        if (string.IsNullOrWhiteSpace(cardCode) || string.IsNullOrWhiteSpace(branchCode))
            return [];

        // O banco só filtra produtor, filial e NF-e autorizada; a elegibilidade completa (própria, Normal, não cancelada)
        // é decidida uma única vez, em memória, por IsComplementableOrigin — sem repetir as condições aqui.
        var invoices = await db.Context.PurchaseInvoices.AsNoTracking()
            .Include(i => i.Items).ThenInclude(l => l.PurchaseContract)
            .Where(i => i.CardCode == cardCode && i.BranchCode == branchCode && i.NfeStatus == NfeStatus.Authorized)
            .ToListAsync();

        return invoices
            .Where(PurchaseInvoiceValueComplement.IsComplementableOrigin)
            .OrderByDescending(i => i.IssueDate).ThenByDescending(i => i.InvoiceNumber)
            .SelectMany(invoice => NfeItemNumbering.Ordered(invoice.Items)
                .Select(item => new PurchaseInvoiceComplementCandidateDto
                {
                    ItemKey = item.Key!.Value,
                    InvoiceKey = invoice.Key,
                    InvoiceNumber = invoice.InvoiceNumber,
                    InvoiceDate = invoice.IssueDate,
                    AccessKey = invoice.ChaveNFe,
                    LineNumber = NfeItemNumbering.OriginNumber(item, invoice.Items.Count),
                    ItemCode = item.ItemCode,
                    ItemName = item.ItemName,
                    UnitOfMeasure = item.UnitOfMeasureCode,
                    Quantity = (double)item.Quantity,
                    UnitPrice = (double)item.UnitPrice,
                    Total = (double)item.Total,
                    UsageCode = item.UsageCode,
                    UsageName = item.UsageName,
                    PurchaseContractKey = item.PurchaseContractKey,
                    PurchaseContractCode = item.PurchaseContract?.Code,
                }))
            .ToList();
    }
}
