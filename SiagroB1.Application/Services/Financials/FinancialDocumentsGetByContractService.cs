using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Dtos;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Financials;

public class FinancialDocumentsGetByContractService(IUnitOfWork db)
{
    public async Task<IEnumerable<FinancialDocumentByContractDto>> ExecuteAsync(
        string contractType, Guid contractKey)
    {
        var isPurchase = string.Equals(contractType, "Purchase", StringComparison.OrdinalIgnoreCase);
        var isSales = string.Equals(contractType, "Sales", StringComparison.OrdinalIgnoreCase);

        if (!isPurchase && !isSales)
            throw new ApplicationException("Tipo de contrato inválido. Informe Purchase ou Sales.");

        var documents = await db.Context.FinancialDocuments.AsNoTracking()
            .Where(x => isPurchase
                ? x.PurchaseContractKey == contractKey
                : x.SalesContractKey == contractKey)
            .OrderBy(x => x.DueDate)
            .ToListAsync();

        // Projetado em memória porque OpenAmount é [NotMapped] e o EF não a traduz.
        return documents.Select(x => new FinancialDocumentByContractDto
        {
            Key = x.Key,
            Code = x.Code,
            Nature = x.Nature.ToString(),
            Status = x.Status.ToString(),
            DueDate = x.DueDate,
            NetAmount = x.NetAmount,
            SettledAmount = x.SettledAmount,
            OpenAmount = x.OpenAmount
        }).ToList();
    }
}
