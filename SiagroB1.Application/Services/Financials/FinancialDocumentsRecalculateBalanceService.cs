using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.Financials;

/// <summary>
/// ESCRITOR ÚNICO de <see cref="FinancialDocument.SettledAmount"/> e de
/// <see cref="FinancialDocument.Status"/>.
///
/// O saldo é sempre derivado da SOMA do ledger, nunca incremental: é a mesma regra de
/// SalesContractsRecalculateBalanceService, e é o que faz baixa, estorno e (nas fases
/// seguintes) abatimento e compensação conviverem sem divergir.
///
/// A única exceção ao "escritor único" é <see cref="FinancialDocumentStatus.Canceled"/>, que
/// só o cancelamento grava e que este serviço NUNCA sobrescreve.
/// </summary>
public class FinancialDocumentsRecalculateBalanceService(IUnitOfWork db)
{
    /// <summary>
    /// Recalcula e apenas ENFILEIRA a alteração — quem chama decide quando salvar. É esta
    /// sobrecarga que a baixa e o estorno usam, de dentro da transação deles.
    /// </summary>
    public static async Task RecalculateAsync(AppDbContext context, Guid documentKey)
    {
        var document = await context.FinancialDocuments
                           .FirstOrDefaultAsync(x => x.Key == documentKey)
                       ?? throw new NotFoundException("Documento financeiro não encontrado.");

        var settled = await context.FinancialSettlements
            .Where(x => x.FinancialDocumentKey == documentKey)
            .SumAsync(x => (decimal?)x.Amount) ?? 0m;

        document.SettledAmount = decimal.Round(settled, 2, MidpointRounding.ToEven);

        if (document.Status == FinancialDocumentStatus.Canceled)
            return;

        // Documento sem valor nao tem o que liquidar: nasce e permanece quitado. Sem isto, o
        // braco `<= 0` da expressao abaixo o classificaria como Aberto para sempre, e ele ficaria
        // entulhando a tela de Contas a Pagar com saldo zero.
        document.Status = document.NetAmount <= 0m
            ? FinancialDocumentStatus.Settled
            : document.SettledAmount switch
            {
                <= 0m => FinancialDocumentStatus.Open,
                var s when s >= document.NetAmount => FinancialDocumentStatus.Settled,
                _ => FinancialDocumentStatus.PartiallySettled
            };
    }

    public async Task<FinancialDocumentRecalcResultDto> ExecuteAsync(Guid key)
    {
        await RecalculateAsync(db.Context, key);
        await db.SaveChangesAsync();

        var document = await db.Context.FinancialDocuments.AsNoTracking()
                           .FirstAsync(x => x.Key == key);

        return new FinancialDocumentRecalcResultDto
        {
            Key = document.Key,
            Code = document.Code,
            NetAmount = document.NetAmount,
            SettledAmount = document.SettledAmount,
            OpenAmount = document.OpenAmount,
            Status = document.Status.ToString()
        };
    }
}
