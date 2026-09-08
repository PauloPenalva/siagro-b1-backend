using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.SalesShipmentReleases;

public class SalesShipmentReleasesGetAvailableService(
    IUnitOfWork db,
    IBusinessPartnerService businessPartnerService,
    ILogger<SalesShipmentReleasesGetAvailableService> logger)
{
    /// <summary>
    /// Alimenta o dialog de faturamento (<c>/shipment-billing</c>): liberações de venda
    /// <c>Actived</c> com saldo (<c>ReleasedQuantity − ShippedQuantity &gt; 0</c>) para o
    /// produto embarcado, já enriquecidas com dados do contrato (cliente, preço, UoM).
    /// Espelho em SQL da regra <see cref="Domain.Entities.SalesShipmentRelease.AvailableQuantity"/>
    /// (EF não traduz a propriedade [NotMapped]).
    /// </summary>
    /// <param name="includeContractsWithoutBalance">
    /// Remove APENAS a cláusula de saldo do CONTRATO, revelando contratos com saldo zero ou
    /// negativo. A cláusula <c>Status == Approved</c> e o filtro de saldo da LIBERAÇÃO
    /// continuam valendo nos dois casos.
    /// <para>
    /// É conveniência de consulta, não autorização: o faturamento <b>não tem guard de saldo de
    /// contrato</b> (ver o <c>&lt;remarks&gt;</c> de
    /// <c>ShipmentBillingCreateSalesInvoiceService</c>), então ligar ou desligar isto não muda
    /// o que o serviço aceita — só o que a lista mostra. O escape existe justamente para o
    /// filtro não virar a trava que aquela decisão removeu: esconder o contrato sem saldo
    /// levaria o usuário a criar um contrato "AJUSTE DE SALDO", que é o desfecho que a
    /// decisão evita.
    /// </para>
    /// </param>
    public IQueryable<SalesShipmentReleaseAvailableDto> Query(
        string itemCode, bool includeContractsWithoutBalance = false)
    {
        var query = db.Context.SalesShipmentReleases
            .Where(r => r.Status == ReleaseStatus.Actived
                        && r.SalesContract != null
                        && r.SalesContract.ItemCode == itemCode
                        && r.SalesContract.Status == ContractStatus.Approved
                        && (r.ReleasedQuantity - r.ShippedQuantity) > 0);

        if (!includeContractsWithoutBalance)
        {
            // Expandido em SQL porque AvaiableVolume é [NotMapped] e o EF não o traduz.
            query = query.Where(r =>
                r.SalesContract!.TotalVolume - r.SalesContract.AllocatedVolume > 0);
        }

        return query
            .OrderBy(r => r.SalesContract!.StandardCashFlowDate)
            .ThenByDescending(r => r.RowId)
            .Select(r => new SalesShipmentReleaseAvailableDto
            {
                SalesShipmentReleaseKey = r.Key.ToString(),
                RowId = r.RowId,
                BranchShortName = r.Branch != null ? r.Branch.ShortName : null,
                SalesContractKey = r.SalesContractKey.ToString(),
                SalesContractCode = r.SalesContract!.Code,
                Complement = r.SalesContract.Complement,
                CardCode = r.SalesContract.CardCode,
                CardName = r.SalesContract.CardName,
                CardFName = r.SalesContract.CardFName,
                ItemCode = r.SalesContract.ItemCode,
                ItemName = r.SalesContract.ItemName,
                UnitOfMeasureCode = r.SalesContract.UnitOfMeasureCode,
                Price = r.SalesContract.Price,
                // itemCode é fixo para toda a query (parâmetro do método), então dá para
                // correlacionar direto sem join por linha.
                // Os dois campos andam juntos: com a UoM preenchida e o fator nulo, o preco caia
                // para KG enquanto a sigla continuava "SC" — par inconsistente na tela.
                CommercialUnitOfMeasureCode = db.Context.ItemComplements
                    .Where(c => c.ItemCode == itemCode && c.CommercialFactor != null)
                    .Select(c => c.CommercialUnitOfMeasureCode)
                    .FirstOrDefault(),
                CommercialPrice = db.Context.ItemComplements
                    .Where(c => c.ItemCode == itemCode && c.CommercialUnitOfMeasureCode != null)
                    .Select(c => r.SalesContract.Price * c.CommercialFactor)
                    .FirstOrDefault(),
                DeliveryLocationCode = r.DeliveryLocationCode,
                DeliveryLocationName = r.DeliveryLocationName,
                AvailableQuantity = r.ReleasedQuantity - r.ShippedQuantity,
                SalesContractStatus = r.SalesContract.Status,
                SalesContractAvailableVolume =
                    r.SalesContract.TotalVolume - r.SalesContract.AllocatedVolume,
                StandardCashFlowDate = r.SalesContract.StandardCashFlowDate,
                SalesContractFreightCostStandard = r.SalesContract.FreightCostStandard,
                SalesContractFreightUmCode = r.SalesContract.FreightUmCode,
            });
    }

    /// <summary>
    /// Mesma consulta de <see cref="Query"/>, com o "Nome Fantasia" resolvido pelo cadastro VIVO
    /// do parceiro. É esta que o endpoint usa.
    /// </summary>
    /// <remarks>
    /// O contrato guarda um snapshot de <c>CardFName</c> gravado na criação, e ele não serve para
    /// esta coluna: até 27/03/2026 a entidade do SAP lia <c>OCRD.CardFName</c> (o "Nome
    /// estrangeiro", vazio na maioria dos parceiros brasileiros) e não <c>OCRD.AliasName</c>, de
    /// modo que todo contrato anterior ficou com o snapshot vazio — e cadastrar a fantasia no SAP
    /// depois não corrigia contrato nenhum. Ler o parceiro resolve as duas metades.
    /// <para>
    /// O parceiro vem por <see cref="IBusinessPartnerService"/>, que já roteia SAPB1 x STANDALONE:
    /// uma consulta em lote pelos CardCodes distintos da lista, nunca uma por linha. Quando o
    /// parceiro não é alcançável (removido do SAP, ou base local vazia), a coluna cai para o
    /// snapshot do contrato e, na falta dele, para a razão social — nunca fica vazia.
    /// </para>
    /// </remarks>
    public async Task<List<SalesShipmentReleaseAvailableDto>> ExecuteAsync(
        string itemCode, bool includeContractsWithoutBalance = false)
    {
        var rows = await Query(itemCode, includeContractsWithoutBalance).ToListAsync();

        var cardCodes = rows
            .Select(r => r.CardCode)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct()
            .ToList()!;

        // O cadastro é ACESSÓRIO aqui: a coluna é informativa e o SAP oscila. Se ele não
        // responder, a lista continua saindo com o snapshot do contrato — o inverso (deixar a
        // exceção subir, como faz ShipmentReleasesBalanceService) bloquearia o faturamento
        // inteiro por causa de um nome de exibição.
        Dictionary<string, SupplierInfo> partners;

        try
        {
            partners = await businessPartnerService.LoadSuppliersAsync(cardCodes!);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Nome fantasia não resolvido para o ItemCode {ItemCode}: o cadastro de parceiros "
                + "não respondeu. A lista segue com o dado gravado no contrato.", itemCode);

            partners = [];
        }

        foreach (var row in rows)
        {
            partners.TryGetValue(row.CardCode ?? string.Empty, out var partner);

            row.CardFName = FirstFilled(partner?.CardFName, row.CardFName, partner?.CardName, row.CardName);
        }

        return rows;
    }

    private static string? FirstFilled(params string?[] candidates) =>
        candidates.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c));
}
