using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Mdfe;

/// <summary>CT-e que podem entrar num MDF-e: autorizados, da filial, sem vínculo ativo com outro MDF-e.</summary>
public class MdfeEligibleCtesQuery(IUnitOfWork db, IConfiguration configuration)
{
    public async Task<IReadOnlyList<TransportDocument>> ListAsync(string branchCode)
    {
        TransportManifestsCreateFromCtesService.EnsureStandalone(configuration);
        return await Eligible(db, branchCode).AsNoTracking().OrderBy(d => d.Number).ToListAsync();
    }

    internal static IQueryable<TransportDocument> Eligible(IUnitOfWork db, string branchCode) =>
        db.Context.TransportDocuments.Where(d => d.BranchCode == branchCode && d.Status == CteStatus.Authorized
            && !db.Context.TransportManifestCtes.Any(l => l.TransportDocumentKey == d.Key && l.IsActive));

    /// <summary>"O CT-e {n} já está no MDF-e {n}." quando algum dos CT-e tem vínculo ativo com outro MDF-e.</summary>
    internal static async Task EnsureNotInOtherActiveManifestAsync(IUnitOfWork db, IReadOnlyCollection<Guid> cteKeys, Guid? ownManifest)
    {
        var clash = await db.Context.TransportManifestCtes.AsNoTracking()
            .Where(l => cteKeys.Contains(l.TransportDocumentKey) && l.IsActive && l.ManifestKey != ownManifest)
            .Select(l => new { CteNumber = l.TransportDocument!.Number, ManifestNumber = l.Manifest!.Number })
            .FirstOrDefaultAsync();
        if (clash is not null)
            throw new DefaultException(AlreadyInManifest(clash.CteNumber, clash.ManifestNumber));
    }

    /// <summary>Número do erro do SQL Server por trás da falha de gravação, se houver.</summary>
    public static int? SqlErrorNumber(DbUpdateException ex) => ex.InnerException is SqlException sql ? sql.Number : null;

    /// <summary>
    /// Chave duplicada (2601/2627) no índice filtrado do vínculo ativo vira "O CT-e {n} já está no MDF-e {n}.",
    /// procurando quem colidiu. Outro erro, ou colisão não encontrada, devolve null (o chamador relança o original).
    /// </summary>
    public static async Task<DefaultException?> MapUniqueViolationAsync(IUnitOfWork db, int? sqlNumber,
        IReadOnlyCollection<Guid> cteKeys, Guid ownManifest)
    {
        if (sqlNumber is not (2601 or 2627)) return null;
        try
        {
            await EnsureNotInOtherActiveManifestAsync(db, cteKeys, ownManifest);
            return null;
        }
        catch (DefaultException e)
        {
            return e;
        }
    }

    internal static string AlreadyInManifest(int? cteNumber, int? manifestNumber) =>
        $"O CT-e {cteNumber} já está no MDF-e {(manifestNumber is { } n ? n.ToString() : "em rascunho")}.";
}
