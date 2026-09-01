using FastReport;
using FastReport.Export.PdfSimple;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.PartnerSources;

namespace SiagroB1.Reports.Services;

/// <summary>
/// Gera o "pré-contrato de compra": documento preliminar impresso a partir de um
/// contrato de compra e encaminhado ao departamento jurídico, que redige o contrato
/// definitivo com o produtor/fornecedor.
/// </summary>
/// <remarks>
/// O layout é uma personalização do cliente Yokotobi e depende de campos que só
/// existem no banco do SAP Business One (CRD7/CRD1/OCNT), por isso a impressão é
/// recusada fora do modo SAPB1. Novos clientes que quiserem esse documento precisam
/// de um layout e de uma origem de dados próprios.
/// </remarks>
public class PrePurchaseContractReportService(
    IUnitOfWork db,
    IWebHostEnvironment env,
    IPartnerSource partnerSource,
    IConfiguration configuration,
    ReportHeaderService reportHeader)
{
    public async Task<byte[]> GeneratePdfAsync(Guid key)
    {
        var erp = configuration.GetValue<string>("Erp") ?? "STANDALONE";

        if (!string.Equals(erp, "SAPB1", StringComparison.OrdinalIgnoreCase))
            throw new BusinessException(
                "A impressão do pré-contrato está disponível apenas na integração com o SAP Business One.");

        var data = await GetAsync(key);

        var reportPath = Path.Combine(
            env.ContentRootPath,
            "Reports",
            "Templates",
            "PrePurchaseContract.frx");

        FastReport.Utils.Config.WebMode = true;
        using var report = new Report();
        report.Load(reportPath);
        reportHeader.Apply(report);

        report.RegisterData(new List<PrePurchaseContractPrintDto> { data }, "PreContract");
        report.RegisterData(data.QualityParameters, "QualityParameters");
        report.RegisterData(data.Taxes, "Taxes");

        report.GetDataSource("PreContract").Enabled = true;
        report.GetDataSource("QualityParameters").Enabled = true;
        report.GetDataSource("Taxes").Enabled = true;

        // Ao contrário dos demais relatórios, não devolvemos PDF vazio em silêncio.
        if (!await report.PrepareAsync())
            throw new BusinessException("Falha ao preparar o pré-contrato para impressão.");

        var pdfExport = new PDFSimpleExport { ShowProgress = false, Title = "PreContrato" };

        using var stream = new MemoryStream();
        report.Export(pdfExport, stream);

        return stream.ToArray();
    }

    private async Task<PrePurchaseContractPrintDto> GetAsync(Guid key)
    {
        var contract = await db.Context.PurchaseContracts
            .AsNoTracking()
            .Include(x => x.Branch)
            .Include(x => x.HarvestSeason)
            .Include(x => x.QualityParameters)
                .ThenInclude(q => q.QualityAttrib)
            .Include(x => x.Taxes)
                .ThenInclude(t => t.Tax)
            // PriceFixations alimenta TotalPrice, que é a base de cálculo dos impostos.
            .Include(x => x.PriceFixations)
            .FirstOrDefaultAsync(x => x.Key == key)
            ?? throw new NotFoundException("Contrato de compra não encontrado.");

        // A origem do parceiro (tabelas do Siagro ou OCRD/CRD1 do SAP) é resolvida por
        // IPartnerSource conforme a chave de configuração Erp. Se ainda assim não houver
        // parceiro, caímos no CardName já desnormalizado no contrato e os demais campos do
        // vendedor saem em branco, para o jurídico preencher.
        var partner = await partnerSource.GetByCardCodeAsync(contract.CardCode);

        var warehouse = await db.Context.Warehouses
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.Code == contract.DeliveryLocationCode);

        var totalPrice = contract.TotalPrice;

        return new PrePurchaseContractPrintDto
        {
            CompanyName = contract.Branch?.BranchName,
            CompanyTaxId = contract.Branch?.TaxId,

            CardCode = contract.CardCode,
            CardName = partner?.CardName ?? contract.CardName,
            Cnpj = partner?.Cnpj,
            Cpf = partner?.Cpf,
            StateRegistration = partner?.StateRegistration,
            ManagingPartners = partner?.ManagingPartners,
            ContractContact = partner?.ContractContact,
            // Endereço já concatenado (logradouro, nº, bairro, município, UF, CEP).
            Street = partner?.FullAddress ?? partner?.Street,

            Code = contract.Code,
            CreationDate = contract.CreationDate,
            ItemCode = contract.ItemCode,
            ItemName = contract.ItemName,
            HarvestSeasonName = contract.HarvestSeason?.Name ?? contract.HarvestSeasonCode,
            TotalVolume = contract.TotalVolume,
            UnitOfMeasureCode = contract.UnitOfMeasureCode,
            StandardPrice = contract.StandardPrice,
            PaymentTerms = contract.PaymentTerms,
            StandardCashFlowDate = contract.StandardCashFlowDate,

            DeliveryStartDate = contract.DeliveryStartDate,
            DeliveryEndDate = contract.DeliveryEndDate,
            DeliveryLocationName = contract.DeliveryLocationName ?? contract.DeliveryLocationCode,
            WarehouseTaxId = warehouse?.TaxId,

            FreightTermsText = contract.FreightTerms switch
            {
                FreightTerms.Cif => "CIF",
                FreightTerms.Fob => "FOB",
                _ => "SEM FRETE"
            },
            FreightCostStandard = contract.FreightCostStandard,
            FreightUmCode = contract.FreightUmCode,
            FunruralTypeText = contract.FunruralType switch
            {
                FunruralType.Livre => "LIVRE",
                FunruralType.Bruto => "BRUTO",
                _ => null
            },

            Comments = contract.Comments,

            QualityParameters = contract.QualityParameters
                .OrderBy(x => x.QualityAttribCode)
                .Select(x => new PrePurchaseContractQualityDto
                {
                    AttribCode = x.QualityAttribCode,
                    AttribName = x.QualityAttrib?.Name ?? x.QualityAttribCode,
                    MaxLimitRate = x.MaxLimitRate
                })
                .ToList(),

            Taxes = contract.Taxes
                .OrderBy(x => x.TaxCode)
                .Select(x => new PrePurchaseContractTaxDto
                {
                    TaxCode = x.TaxCode,
                    TaxName = x.Tax?.Name ?? x.TaxCode,
                    Rate = x.Tax?.Rate ?? 0,
                    // Calculado aqui em vez de usar PurchaseContractTax.TotalTax: aquela
                    // propriedade depende da navegação inversa PurchaseContract estar populada.
                    TotalTax = decimal.Round(
                        totalPrice / 100 * (x.Tax?.Rate ?? 0), 2, MidpointRounding.ToEven)
                })
                .ToList()
        };
    }

}
