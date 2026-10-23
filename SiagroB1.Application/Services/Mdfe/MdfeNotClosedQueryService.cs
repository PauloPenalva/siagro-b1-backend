using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Fiscal.Mdfe;
using SiagroB1.Fiscal.Nfe;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Mdfe;

/// <summary>
/// MDF-e autorizados e NÃO encerrados do CNPJ da filial na SEFAZ, cruzados com os MDF-e do sistema pela chave (única; não depende da filial gravada).
/// Chave que o sistema não conhece vem sem <see cref="MdfeNotClosedItem.ManifestKey"/> ("MDF-e fora do sistema").
/// Somente leitura: não usa trava de emissão.
/// </summary>
public class MdfeNotClosedQueryService(
    IUnitOfWork db,
    IConfiguration configuration,
    MdfeServiceContextFactory contexts,
    IMdfeSefazClient sefaz)
{
    public const string NoResponse = "Sem resposta da SEFAZ: tente consultar de novo.";

    public async Task<IReadOnlyList<MdfeNotClosedItem>> ListAsync(string branchCode)
    {
        TransportManifestsCreateFromCtesService.EnsureStandalone(configuration);
        MdfeZeusConfiguration.PinGlobalLayout();

        var branch = await db.Context.Branchs.AsNoTracking().FirstOrDefaultAsync(b => b.Code == branchCode)
                     ?? throw new DefaultException($"Filial {branchCode} não encontrada.");
        var settings = await db.Context.BranchMdfeSettings.AsNoTracking().FirstOrDefaultAsync(x => x.BranchCode == branchCode)
                       ?? throw new DefaultException($"A filial {branchCode} não tem a Configuração do MDF-e.");
        var cnpj = new string((branch.TaxId ?? "").Where(char.IsDigit).ToArray());
        if (cnpj.Length != 14)
            throw new DefaultException($"A filial {branchCode} não tem CNPJ válido.");

        using var context = await contexts.OpenAsync(branchCode, settings.Environment);

        IReadOnlyList<MdfeNotClosed> open;
        try
        {
            open = await sefaz.ListNotClosedAsync(cnpj, context.Settings);
        }
        catch (MdfeSefazRejectedException e)
        {
            throw new DefaultException($"A SEFAZ recusou a consulta de MDF-e não encerrados: {e.StatusCode} - {e.Reason}");
        }
        catch (NfeCommunicationException)
        {
            throw new DefaultException(NoResponse);
        }

        if (open.Count == 0) return [];

        var keys = open.Select(x => x.AccessKey).ToList();
        var known = (await db.Context.TransportManifests.AsNoTracking()
                .Where(m => m.AccessKey != null && keys.Contains(m.AccessKey))
                .Select(m => new { m.Key, m.AccessKey, m.Number, m.Series, m.Status, m.TractionPlate })
                .ToListAsync())
            .GroupBy(m => m.AccessKey!)
            .ToDictionary(g => g.Key, g => g.First());

        return open.Select(o => known.TryGetValue(o.AccessKey, out var m)
                ? new MdfeNotClosedItem
                {
                    AccessKey = o.AccessKey, Protocol = o.Protocol, ManifestKey = m.Key, Number = m.Number, Series = m.Series,
                    Status = m.Status, TractionPlate = m.TractionPlate,
                    StatusDiverges = m.Status is not (MdfeStatus.Authorized or MdfeStatus.Processing),
                }
                : new MdfeNotClosedItem { AccessKey = o.AccessKey, Protocol = o.Protocol, OutsideSystem = true })
            .ToList();
    }
}

/// <summary>
/// Um MDF-e não encerrado na SEFAZ. Os campos do sistema ficam nulos quando a chave não existe aqui.
/// Status é o mesmo enum MdfeStatus do TransportManifest (serializa como nome).
/// </summary>
public class MdfeNotClosedItem
{
    public string AccessKey { get; set; } = string.Empty;
    public string Protocol { get; set; } = string.Empty;
    public Guid? ManifestKey { get; set; }
    public int? Number { get; set; }
    public int? Series { get; set; }
    public MdfeStatus? Status { get; set; }
    public string? TractionPlate { get; set; }
    /// <summary>Existe aqui, mas a situação local não é Autorizado/Processamento (a SEFAZ ainda o tem aberto).</summary>
    public bool StatusDiverges { get; set; }

    /// <summary>A chave não existe no sistema ("MDF-e fora do sistema"): ManifestKey nulo.</summary>
    public bool OutsideSystem { get; set; }
}
