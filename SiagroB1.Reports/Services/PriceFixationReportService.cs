using FastReport;
using FastReport.Export.PdfSimple;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.PartnerSources;

namespace SiagroB1.Reports.Services;

/// <summary>
/// Gera o "espelho de fixação de preço": comprovante enviado ao produtor/fornecedor
/// confirmando o preço fixado para uma parcela de um contrato a fixar (PAF).
/// </summary>
public class PriceFixationReportService(
    IUnitOfWork db,
    IWebHostEnvironment env,
    IPartnerSource partnerSource,
    ReportHeaderService reportHeader)
{
    public async Task<byte[]> GeneratePdfAsync(Guid key)
    {
        var data = await GetAsync(key);

        var reportPath = Path.Combine(
            env.ContentRootPath,
            "Reports",
            "Templates",
            "PriceFixation.frx");

        FastReport.Utils.Config.WebMode = true;
        using var report = new Report();
        report.Load(reportPath);
        reportHeader.Apply(report);

        report.RegisterData(new List<PriceFixationPrintDto> { data }, "PriceFixation");
        report.GetDataSource("PriceFixation").Enabled = true;

        if (!await report.PrepareAsync())
            throw new BusinessException("Falha ao preparar o espelho de fixação para impressão.");

        var pdfExport = new PDFSimpleExport { ShowProgress = false, Title = "EspelhoFixacao" };

        using var stream = new MemoryStream();
        report.Export(pdfExport, stream);

        return stream.ToArray();
    }

    private async Task<PriceFixationPrintDto> GetAsync(Guid key)
    {
        var fixation = await db.Context.PurchaseContractsPriceFixations
            .AsNoTracking()
            .Include(x => x.PurchaseContract)
                .ThenInclude(c => c!.Branch)
            .Include(x => x.PurchaseContract)
                .ThenInclude(c => c!.HarvestSeason)
            .FirstOrDefaultAsync(x => x.Key == key)
            ?? throw new NotFoundException("Fixação de preço não encontrada.");

        // O espelho é um comprovante de compromisso firmado. Emitir para fixação em
        // aprovação, rejeitada ou estornada mandaria ao produtor um documento sobre
        // um preço que não vale.
        if (fixation.Status != PriceFixationStatus.Confirmed)
            throw new BusinessException(
                $"Espelho de fixação só é emitido para fixação confirmada. " +
                $"Status atual: {fixation.Status}.");

        var contract = fixation.PurchaseContract
            ?? throw new NotFoundException("Contrato de compra não encontrado.");

        // A origem do parceiro (tabelas do Siagro ou OCRD/CRD1 do SAP) é resolvida por
        // IPartnerSource conforme a chave de configuração Erp. Ler BUSINESS_PARTNERS
        // direto deixaria o documento em branco em toda instalação integrada ao SAP.
        var partner = await partnerSource.GetByCardCodeAsync(contract.CardCode);

        return new PriceFixationPrintDto
        {
            CompanyName = contract.Branch?.BranchName,
            CompanyTaxId = contract.Branch?.TaxId,

            CardCode = contract.CardCode,
            CardName = partner?.CardName ?? contract.CardName,
            TaxId = partner?.TaxId,
            Street = partner?.Street,
            CityStateZip = BuildCityStateZip(partner),

            ContractCode = contract.Code,
            ItemCode = contract.ItemCode,
            ItemName = contract.ItemName,
            HarvestSeasonName = contract.HarvestSeason?.Name ?? contract.HarvestSeasonCode,
            UnitOfMeasureCode = contract.UnitOfMeasureCode,

            FixationDate = fixation.FixationDate,
            FixationVolume = fixation.FixationVolume,
            FixationPrice = fixation.FixationPrice,
            FreightCost = fixation.FreightCost,
            FixationTotal = decimal.Round(
                fixation.FixationVolume * fixation.FixationPrice, 2, MidpointRounding.ToEven),

            ApprovedBy = fixation.ApprovedBy,
            ApprovedAt = fixation.ApprovedAt,
            ApprovalComments = fixation.ApprovalComments,

            ContractTotalVolume = contract.TotalVolume,
            ContractFixedVolume = contract.FixedVolume,
            ContractAvailableVolumeToPricing = contract.AvailableVolumeToPricing,
        };
    }

    private static string? BuildCityStateZip(ReportPartnerDto? partner)
    {
        if (partner is null) return null;

        var cityState = string.Join("/", new[] { partner.City, partner.State }
            .Where(x => !string.IsNullOrWhiteSpace(x)));

        var parts = new[] { cityState, partner.ZipCode }
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToArray();

        return parts.Length == 0 ? null : string.Join(" - ", parts);
    }
}
