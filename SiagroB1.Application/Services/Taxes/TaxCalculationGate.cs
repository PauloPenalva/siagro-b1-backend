using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Taxes;

/// <summary>
/// A regra única da tributação da NF-e STANDALONE: ativa quando o modo é STANDALONE E a filial
/// do documento tem a chave "Emite NF-e pelo Siagro". Cálculo, trava, guardas e telas perguntam
/// AQUI — ninguém combina modo e chave por conta própria. Em SAPB1 a chave é ignorada mesmo
/// gravada no banco; é isso que garante que nada desta feature reflita na Yokotobi, e a MH
/// Agro (STANDALONE sem NF-e) fica de fora por ter a chave desligada.
/// </summary>
public class TaxCalculationGate(IUnitOfWork db, IConfiguration configuration)
{
    public bool IsStandalone => ErpMode.IsStandalone(configuration);

    public async Task<bool> IsActiveAsync(string? branchCode)
    {
        if (!IsStandalone || string.IsNullOrWhiteSpace(branchCode))
            return false;

        return await db.Context.Branchs
            .AsNoTracking()
            .AnyAsync(b => b.Code == branchCode && b.IssuesNfe);
    }
}
