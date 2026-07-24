using SiagroB1.Domain.Entities;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.SalesContracts;

/// <summary>
/// Porta única de escrita do log de alterações do contrato de VENDA.
///
/// Apenas enfileira a linha no contexto — quem chama decide quando salvar, para que o log e a
/// alteração que ele descreve entrem no mesmo <c>SaveChanges</c> (nunca sobra log de uma
/// alteração que falhou, nem alteração sem log).
/// </summary>
public class SalesContractsChangeLogService(AppDbContext context)
{
    public void Register(
        Guid contractKey,
        string field,
        string? oldValue,
        string? newValue,
        string userName)
    {
        context.SalesContractsChangeLogs.Add(new SalesContractChangeLog
        {
            SalesContractKey = contractKey,
            ChangedAt = DateTime.Now,
            ChangedBy = userName,
            Field = field,
            OldValue = Truncate(oldValue),
            NewValue = Truncate(newValue),
        });
    }

    /// <summary>
    /// As colunas de valor são VARCHAR(500): uma observação longa não pode derrubar a gravação
    /// da alteração que o log só acompanha.
    /// </summary>
    private static string? Truncate(string? value) =>
        value is { Length: > 500 } ? value[..500] : value;
}
