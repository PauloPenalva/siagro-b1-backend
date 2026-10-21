using Microsoft.Extensions.Configuration;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Dimensions;

/// <summary>
/// Obrigatoriedade da amarração contábil ao confirmar uma nota ou criar um título/adiantamento/lançamento.
/// </summary>
// TODO Task 3: substituir por AccountingAssignmentGuard (flags Requires*/Accepts* da conta contábil).
public class DimensionRequirementGuard(IUnitOfWork db, IConfiguration configuration)
{
    public Task EnsureAsync(IEnumerable<IAccountingAssignment> rows, Func<int, string>? rowLabel = null)
    {
        _ = db;
        _ = configuration;
        return Task.CompletedTask;
    }
}
