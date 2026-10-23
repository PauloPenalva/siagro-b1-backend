using System.Linq.Expressions;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SiagroB1.Application.Jobs;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Infra.Companies;

namespace SiagroB1.Application.Services.Cte;

/// <summary>
/// Transmissão e averbação de vários CT-e: um job do Hangfire POR documento (um por vez, sem um derrubar o outro).
/// Chave fora de situação é ignorada em silêncio; o retorno é quantos foram enfileirados. O resultado fica no próprio
/// documento (situação/motivo e situação da averbação).
/// </summary>
public class CteBatchService(
    IUnitOfWork db,
    IConfiguration configuration,
    IBackgroundJobClient backgroundJobClient,
    ICompanyContext companyContext)
{
    public const int MaxKeys = 200;
    public const string TooMany = "Selecione no máximo 200 CT-e por vez.";

    private static readonly CteStatus[] Transmittable = [CteStatus.Confirmed, CteStatus.Rejected, CteStatus.Processing];

    public async Task<int> EnqueueTransmitAsync(IReadOnlyList<Guid> keys, string user)
    {
        var eligible = await EligibleAsync(keys, x => Transmittable.Contains(x.Status));
        var company = companyContext.CompanyCode;
        foreach (var key in eligible)
            backgroundJobClient.Enqueue<CteBatchJob>(job => job.TransmitAsync(company, key, user));
        return eligible.Count;
    }

    public async Task<int> EnqueueEndorseAsync(IReadOnlyList<Guid> keys, string user)
    {
        var eligible = await EligibleAsync(keys,
            x => x.Status == CteStatus.Authorized && x.EndorsementStatus == CteEndorsementStatus.None);
        var company = companyContext.CompanyCode;
        foreach (var key in eligible)
            backgroundJobClient.Enqueue<CteBatchJob>(job => job.EndorseAsync(company, key, user));
        return eligible.Count;
    }

    private async Task<List<Guid>> EligibleAsync(IReadOnlyList<Guid> keys, Expression<Func<TransportDocument, bool>> filter)
    {
        TransportDocumentsSaveService.EnsureStandalone(configuration);

        var distinct = keys.Distinct().ToList();
        if (distinct.Count > MaxKeys)
            throw new DefaultException(TooMany);

        return await db.Context.TransportDocuments.AsNoTracking()
            .Where(x => distinct.Contains(x.Key)).Where(filter)
            .Select(x => x.Key).ToListAsync();
    }
}
