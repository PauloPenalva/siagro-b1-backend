using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.SalesShipmentReleases;

public class SalesShipmentReleasesCreateService(
    IUnitOfWork db,
    IBusinessPartnerService businessPartnerService,
    ILogger<SalesShipmentReleasesCreateService> logger)
{
    public async Task<SalesShipmentRelease> ExecuteAsync(SalesShipmentRelease entity, string userName)
    {
        var salesContract = await db.Context.SalesContracts
            .AsNoTracking()
            .Include(x => x.SalesInvoiceItems).ThenInclude(i => i.SalesInvoice)
            .Include(x => x.SalesShipmentReleases)
            .Include(x => x.DeliveryLocations)
            .FirstOrDefaultAsync(x => x.Key == entity.SalesContractKey) ??
                            throw new NotFoundException("Sales contract not found.");

        if (salesContract.Status == ContractStatus.Finished)
            throw new ApplicationException("Contrato encerrado: não é possível criar liberação de entrega.");

        // A entrega só pode sair para um dos locais informados no contrato — não para
        // qualquer cliente da base. O value help da tela já lista apenas esses locais;
        // aqui a mesma regra fecha o caminho de quem chama a API direto.
        if (salesContract.DeliveryLocations.Count == 0)
            throw new ApplicationException(
                "Contrato de venda sem local de entrega cadastrado: informe os locais de " +
                "entrega no contrato antes de solicitar a liberação.");

        if (salesContract.DeliveryLocations.All(l => l.CardCode != entity.DeliveryLocationCode))
            throw new ApplicationException(
                $"Local de entrega '{entity.DeliveryLocationCode}' não está entre os locais de " +
                "entrega do contrato de venda.");

        // Saldo FÍSICO: não permite liberar mais do que o contrato tem para embarcar
        // (o fluxo legado fatura direto no contrato, sem passar por liberação — por isso
        // olhar só o eixo de liberação deixaria criar liberação em contrato já esgotado).
        if (entity.ReleasedQuantity > salesContract.PhysicalAvailableToRelease)
            throw new ApplicationException(
                $"Saldo físico do contrato insuficiente para liberar essa quantidade. " +
                $"Disponível: {salesContract.PhysicalAvailableToRelease:N3}.");

        try
        {
            entity.BranchCode = salesContract.BranchCode;
            entity.Status = ReleaseStatus.Pending;
            entity.CreatedAt = DateTime.Now;
            entity.CreatedBy = userName;
            // No escopo de venda, o local de entrega é um cliente (business partner),
            // não um armazém: o nome vem do CardName do parceiro.
            entity.DeliveryLocationName = (await businessPartnerService.GetByIdAsync(entity.DeliveryLocationCode))?.CardName;
            await db.Context.SalesShipmentReleases.AddAsync(entity);
            await db.SaveChangesAsync();
            return entity;
        }
        catch (Exception ex)
        {
            if (ex is DefaultException)
            {
                throw;
            }

            logger.LogError(ex, "Error creating entity.");
            throw new DefaultException("Error creating entity.");
        }
    }
}
