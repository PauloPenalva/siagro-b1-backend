using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Interfaces;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.PurchaseContracts;

/// <summary>
/// Marca/desmarca a liberação de pagamento de UM departamento no contrato de COMPRA (GAC-1188).
/// Cada departamento exige a sua própria permissão (<see cref="PermissionCodes.ForDepartment"/>).
/// </summary>
/// <remarks>
/// Sem guarda de status do contrato, deliberadamente: é fato documental, vale também em contrato
/// encerrado ou cancelado. Ausência de linha equivale a "não liberado".
/// </remarks>
public class PurchaseContractsSetDepartmentReleaseService(
    AppDbContext context,
    PurchaseContractsChangeLogService changeLog,
    IUserPermissions permissions)
{
    public async Task ExecuteAsync(Guid key, PaymentReleaseDepartment department, bool released, string userName)
    {
        var label = ContractChangeLogFields.DescribeDepartment(department);

        if (!await permissions.HasAsync(userName, PermissionCodes.ForDepartment(department)))
            throw new DefaultException($"Você não tem permissão para a {label}.");

        var contract = await context.PurchaseContracts.FirstOrDefaultAsync(x => x.Key == key)
                       ?? throw new NotFoundException("Contrato não encontrado.");

        if (department == PaymentReleaseDepartment.Store && !contract.IsBarter)
            throw new DefaultException("Lib. Loja só se aplica a contrato com barter.");

        var row = await context.PurchaseContractsPaymentReleases
            .FirstOrDefaultAsync(x => x.PurchaseContractKey == key && x.Department == department);

        // Sem linha = Não: desmarcar o que nunca foi liberado não é mudança (sem log, sem UpdatedAt).
        if ((row?.Released ?? false) == released)
            return;

        if (row is null)
        {
            row = new PurchaseContractPaymentRelease { PurchaseContractKey = key, Department = department };
            context.PurchaseContractsPaymentReleases.Add(row);
        }

        row.Released = released;
        row.ReleasedAt = released ? DateTime.Now : null;
        row.ReleasedBy = released ? userName : null;

        contract.UpdatedAt = DateTime.Now;
        contract.UpdatedBy = userName;

        changeLog.Register(
            key,
            ContractChangeLogFields.DepartmentRelease,
            $"{label}: {(released ? "Não" : "Sim")}",
            $"{label}: {(released ? "Sim" : "Não")}",
            userName);

        await context.SaveChangesAsync();
    }
}
