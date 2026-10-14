using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Financials;

/// <summary>
/// Dação (spec 2026-10-13, títulos firmes, regra 17 e D4): a composição para no valor do provisório; a sobra de um
/// firme faturado acima do contrato é baixada pelo OPERADOR, com motivo, sem conta e sem dinheiro. Só em firme da
/// conciliação (não em devolução nem provisório). Estornável pelo estorno genérico.
/// </summary>
public class FinancialDocumentsDationService(IUnitOfWork db)
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public async Task<FinancialSettlement> ExecuteAsync(
        Guid documentKey, decimal amount, DateTime settlementDate, string? notes, string userName)
    {
        var document = await db.Context.FinancialDocuments.FirstOrDefaultAsync(x => x.Key == documentKey)
                       ?? throw new NotFoundException("Documento financeiro não encontrado.");

        if (!document.IsPurchaseFirmPayable || document.Nature != FinancialDocumentNature.Firm)
            throw new ApplicationException("Dação só se aplica a título firme da conciliação fiscal.");

        if (document.Status == FinancialDocumentStatus.Canceled)
            throw new ApplicationException($"O título {document.Code} está cancelado.");

        if (string.IsNullOrWhiteSpace(notes))
            throw new ApplicationException("Informe o motivo da dação.");

        var value = decimal.Round(amount, 2, MidpointRounding.ToEven);
        if (value <= 0m)
            throw new ApplicationException("O valor da dação deve ser maior que zero.");

        if (value > document.OpenAmount)
            throw new ApplicationException(
                $"O valor da dação ({value.ToString("N2", PtBr)}) excede o saldo em aberto do título ({document.OpenAmount.ToString("N2", PtBr)}).");

        var now = DateTime.Now;
        var settlement = new FinancialSettlement
        {
            Key = Guid.NewGuid(),
            FinancialDocumentKey = document.Key,
            FinancialAccountCode = null,
            SettlementDate = settlementDate.Date,
            Amount = value,
            Origin = FinancialSettlementOrigin.Dation,
            Notes = notes.Trim(),
            CreatedAt = now, CreatedBy = userName, UpdatedAt = now, UpdatedBy = userName,
        };

        db.Context.FinancialSettlements.Add(settlement);
        await FinancialDocumentsRecalculateBalanceService.RecalculateAsync(db.Context, document.Key);
        await db.SaveChangesAsync();

        return settlement;
    }
}
