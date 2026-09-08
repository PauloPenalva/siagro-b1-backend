using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Financials;

/// <summary>
/// Geração deliberada de documentos provisórios para contratos JÁ aprovados — compra e venda.
///
/// Isto NÃO é migration de propósito. Uma migration roda no deploy e criaria em silêncio
/// milhares de documentos, muitos sem vencimento e muitos de contratos já entregues e pagos
/// fora do sistema — o módulo estrearia com um backlog falso e impagável, com risco real de
/// pagamento em duplicidade. E a numeração via Dapper num laço queimaria milhares de números
/// sob UPDLOCK. Aqui o financeiro roda por filial, confere o DryRun, e só então efetiva.
///
/// Contrato sem vencimento resolvível é RELATADO, nunca lançado: um contrato ruim no meio do
/// lote não pode derrubar o lote inteiro. Isto é o oposto do caminho de aprovação, onde a
/// falta de vencimento corretamente recusa a operação (FinancialDocumentsGenerateService
/// lança ApplicationException ali) — aqui não há operador esperando na tela, então a exceção
/// vira contagem e o laço segue.
///
/// Compra e venda: nem o spec nem o DTO de resultado limitam este backlog a um lado do
/// portfólio. Os dois laços são espelhados de propósito e somam nos MESMOS contadores.
/// </summary>
public class FinancialDocumentsGenerateBacklogService(
    IUnitOfWork db,
    FinancialDocumentsGenerateService generateService)
{
    public async Task<FinancialDocumentBacklogResultDto> ExecuteAsync(
        string? branchCode, DateTime fromDate, DateTime toDate, bool dryRun, string userName)
    {
        var result = new FinancialDocumentBacklogResultDto { DryRun = dryRun };

        await ProcessPurchaseContractsAsync(branchCode, fromDate, toDate, dryRun, userName, result);
        await ProcessSalesContractsAsync(branchCode, fromDate, toDate, dryRun, userName, result);

        return result;
    }

    private async Task ProcessPurchaseContractsAsync(
        string? branchCode, DateTime fromDate, DateTime toDate, bool dryRun, string userName,
        FinancialDocumentBacklogResultDto result)
    {
        var contracts = await db.Context.PurchaseContracts
            .Include(x => x.PriceFixations)
            .Where(x => x.Status == ContractStatus.Approved &&
                        x.CreationDate >= fromDate && x.CreationDate <= toDate &&
                        (branchCode == null || x.BranchCode == branchCode))
            .ToListAsync();

        foreach (var contract in contracts)
        {
            foreach (var fixation in contract.PriceFixations
                         .Where(f => f.Status == PriceFixationStatus.Confirmed))
            {
                result.EligibleFixations++;

                var alreadyGenerated = await db.Context.FinancialDocuments.AnyAsync(x =>
                    x.OriginType == FinancialDocumentOrigin.PurchaseContractPriceFixation &&
                    x.OriginKey == fixation.Key &&
                    x.Nature == FinancialDocumentNature.Provisional &&
                    x.Status != FinancialDocumentStatus.Canceled);

                if (alreadyGenerated) { result.SkippedAlreadyGenerated++; continue; }

                if ((fixation.FinancialDueDate ?? contract.StandardCashFlowDate) is null)
                {
                    result.SkippedWithoutDueDate++;
                    continue;
                }

                if (dryRun) continue;

                await generateService.EnqueueForPurchaseFixationAsync(contract, fixation, userName);
                await db.SaveChangesAsync();
                result.Generated++;
            }
        }
    }

    private async Task ProcessSalesContractsAsync(
        string? branchCode, DateTime fromDate, DateTime toDate, bool dryRun, string userName,
        FinancialDocumentBacklogResultDto result)
    {
        var contracts = await db.Context.SalesContracts
            .Include(x => x.PriceFixations)
            .Where(x => x.Status == ContractStatus.Approved &&
                        x.CreationDate >= fromDate && x.CreationDate <= toDate &&
                        (branchCode == null || x.BranchCode == branchCode))
            .ToListAsync();

        foreach (var contract in contracts)
        {
            foreach (var fixation in contract.PriceFixations
                         .Where(f => f.Status == PriceFixationStatus.Confirmed))
            {
                result.EligibleFixations++;

                var alreadyGenerated = await db.Context.FinancialDocuments.AnyAsync(x =>
                    x.OriginType == FinancialDocumentOrigin.SalesContractPriceFixation &&
                    x.OriginKey == fixation.Key &&
                    x.Nature == FinancialDocumentNature.Provisional &&
                    x.Status != FinancialDocumentStatus.Canceled);

                if (alreadyGenerated) { result.SkippedAlreadyGenerated++; continue; }

                if ((fixation.FinancialDueDate ?? contract.StandardCashFlowDate) is null)
                {
                    result.SkippedWithoutDueDate++;
                    continue;
                }

                if (dryRun) continue;

                await generateService.EnqueueForSalesFixationAsync(contract, fixation, userName);
                await db.SaveChangesAsync();
                result.Generated++;
            }
        }
    }
}
