using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.SalesContracts;

/// <summary>
/// Altera a situação de assinatura do contrato de VENDA. Espelho de
/// <c>PurchaseContractsSetSignatureStatusService</c>.
/// </summary>
/// <remarks>
/// NÃO tem guarda de status, e isso é deliberado: assinatura é fato documental, não movimento —
/// não altera valor nem saldo. Vale em QUALQUER status, inclusive Encerrado e Cancelado, pelo
/// mesmo princípio do anexo e do comentário do contrato.
///
/// Existe como action justamente porque o PATCH do cabeçalho
/// (<see cref="SalesContractsUpdateService"/>) recusa contrato fora de Rascunho e copia a
/// entidade inteira com <c>SetValues</c>, sem como isentar uma coluna.
/// </remarks>
public class SalesContractsSetSignatureStatusService(
    AppDbContext context,
    SalesContractsChangeLogService changeLog)
{
    public async Task ExecuteAsync(Guid key, SignatureStatus? status, string userName)
    {
        var contract = await context.SalesContracts.FirstOrDefaultAsync(x => x.Key == key)
                       ?? throw new NotFoundException("Contrato não encontrado.");

        var previous = contract.SignatureStatus;

        // Sem mudança não gera linha de log — o log do contrato é lido pelo usuário, ruído nele custa.
        if (previous == status)
            return;

        contract.SignatureStatus = status;
        contract.UpdatedAt = DateTime.Now;
        contract.UpdatedBy = userName;

        changeLog.Register(
            contract.Key,
            ContractChangeLogFields.SignatureStatus,
            ContractChangeLogFields.DescribeSignatureStatus(previous),
            ContractChangeLogFields.DescribeSignatureStatus(status),
            userName);

        await context.SaveChangesAsync();
    }
}
