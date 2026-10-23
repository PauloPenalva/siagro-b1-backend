using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Services.Sped;

namespace SiagroB1.Reports.Services.SpedFiscal;

/// <summary>
/// Lê do banco tudo o que o SPED Fiscal do mês precisa: linhas escrituráveis (as mesmas da
/// Conferência Fiscal), NF-e próprias canceladas/denegadas/inutilizadas, participantes, produtos,
/// unidades, fatores de conversão e, opcionalmente, o inventário.
/// </summary>
public class SpedFiscalDataLoader(IUnitOfWork db, FiscalConferenceService conference, FiscalInventoryService inventory,
    SpedMasterDataLoader masterData)
{
    /// <summary>Só o documento de terceiro tem C170; a unidade da NF-e própria não exige 0220 nem bloqueia o arquivo.</summary>
    private static bool WritesItemLine(FiscalDocumentLineDto line) => line.IssuerType == "1";

    public async Task<SpedFiscalData> LoadAsync(SpedFiscalRequest request)
    {
        var branch = await db.Context.Branchs.AsNoTracking().FirstOrDefaultAsync(b => b.Code == request.BranchCode)
                     ?? throw new InvalidOperationException($"Filial {request.BranchCode} não encontrada.");
        var settings = await db.Context.SpedFiscalSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.BranchCode == request.BranchCode);

        var (lines, _) = await conference.LoadLinesAsync(new FiscalConferenceRequest
        {
            BranchCode = request.BranchCode, Year = request.Year, Month = request.Month,
        });

        FiscalInventoryData? inventoryData = null;
        if (request.IncludeInventory)
            inventoryData = await inventory.BuildAsync(new FiscalInventoryRequest
            {
                BranchCode = request.BranchCode,
                Date = request.InventoryDate ?? new DateTime(request.Year - 1, 12, 31),
            });

        var master = await masterData.LoadAsync(branch, lines, inventoryData?.Rows, WritesItemLine);

        return new SpedFiscalData
        {
            Branch = branch,
            Settings = settings,
            Lines = lines,
            VoidDocuments = await SpedVoidDocumentLoader.LoadAsync(db, request.BranchCode, request.Year, request.Month),
            Participants = master.Participants,
            MissingParticipantCodes = master.MissingParticipantCodes,
            UnconvertibleUnits = master.UnconvertibleUnits,
            Items = master.Items,
            Units = master.Units,
            Conversions = master.Conversions,
            Inventory = inventoryData,
        };
    }

    /// <summary>Produtos e conversões pela regra do SPED Fiscal (só a linha de terceiro é escrita).</summary>
    public async Task<(List<SpedItem> Items, List<SpedUnitConversion> Conversions, List<SpedUnconvertibleUnit> Unconvertible)> LoadItemsAsync(
        Branch branch, List<FiscalDocumentLineDto> lines, FiscalInventoryData? inventoryData)
    {
        var data = await masterData.LoadAsync(branch, lines, inventoryData?.Rows, WritesItemLine);
        return (data.Items, data.Conversions, data.UnconvertibleUnits);
    }
}
