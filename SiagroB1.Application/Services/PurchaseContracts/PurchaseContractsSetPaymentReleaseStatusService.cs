using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Interfaces;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.PurchaseContracts;

/// <summary>
/// Liberação de pagamento do contrato de COMPRA (GAC-1188): situação financeira e a flag
/// "Antecipação Liberada pela Diretoria". Só quem tem <c>PAYMENT_RELEASE_FINANCIAL</c> altera.
/// </summary>
/// <remarks>
/// Sem guarda de status do contrato, deliberadamente: é fato documental/financeiro, vale também
/// em contrato encerrado ou cancelado (mesmo princípio de <see cref="PurchaseContractsSetSignatureStatusService"/>).
///
/// A regra do "sobre rodas" vale só no momento da mudança. Se o contrato for desmarcado depois de
/// Pré-aprovado, o status gravado fica; a PRÓXIMA mudança é que volta a seguir a regra.
/// </remarks>
public class PurchaseContractsSetPaymentReleaseStatusService(
    AppDbContext context,
    PurchaseContractsChangeLogService changeLog,
    IUserPermissions permissions)
{
    private const string NoPermissionMessage = "Você não tem permissão para alterar a liberação financeira do contrato.";

    public async Task SetStatusAsync(Guid key, PaymentReleaseStatus? status, string userName)
    {
        var contract = await LoadAuthorizedAsync(key, userName);

        // Nulo equivale a Pendente para o usuário: trocar um pelo outro não é mudança (sem log, sem UpdatedAt).
        var previous = contract.PaymentReleaseStatus;
        if ((previous ?? PaymentReleaseStatus.Pending) == (status ?? PaymentReleaseStatus.Pending))
            return;

        if (status == PaymentReleaseStatus.PreApproved && !contract.IsOnWheels)
            throw new DefaultException("Pré-aprovado só se aplica a contrato sobre rodas.");

        if (status == PaymentReleaseStatus.Approved && contract.IsOnWheels)
            throw new DefaultException("Contrato sobre rodas deve ser marcado como Pré-aprovado.");

        contract.PaymentReleaseStatus = status;
        Touch(contract, userName);

        changeLog.Register(
            contract.Key,
            ContractChangeLogFields.PaymentReleaseStatus,
            ContractChangeLogFields.DescribePaymentReleaseStatus(previous),
            ContractChangeLogFields.DescribePaymentReleaseStatus(status),
            userName);

        await context.SaveChangesAsync();
    }

    public async Task SetEarlyPaymentAuthorizedAsync(Guid key, bool authorized, string userName)
    {
        var contract = await LoadAuthorizedAsync(key, userName);

        var previous = contract.EarlyPaymentAuthorized;
        if (previous == authorized)
            return;

        contract.EarlyPaymentAuthorized = authorized;
        Touch(contract, userName);

        changeLog.Register(
            contract.Key,
            ContractChangeLogFields.EarlyPaymentAuthorized,
            previous ? "Sim" : "Não",
            authorized ? "Sim" : "Não",
            userName);

        await context.SaveChangesAsync();
    }

    private async Task<PurchaseContract> LoadAuthorizedAsync(Guid key, string userName)
    {
        if (!await permissions.HasAsync(userName, PermissionCodes.PaymentReleaseFinancial))
            throw new DefaultException(NoPermissionMessage);

        return await context.PurchaseContracts.FirstOrDefaultAsync(x => x.Key == key)
               ?? throw new NotFoundException("Contrato não encontrado.");
    }

    private static void Touch(PurchaseContract contract, string userName)
    {
        contract.UpdatedAt = DateTime.Now;
        contract.UpdatedBy = userName;
    }
}
