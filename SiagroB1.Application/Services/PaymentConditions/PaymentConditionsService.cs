using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Fiscal.Payments;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.PaymentConditions;

/// <summary>Cadastro das condições de pagamento (só STANDALONE; o controller recusa nos demais modos).</summary>
public class PaymentConditionsService(IUnitOfWork db)
{
    public IQueryable<PaymentCondition> QueryAll() => db.Context.PaymentConditions.AsNoTracking();

    public async Task<PaymentCondition?> GetByIdAsync(int code) => await db.Context.PaymentConditions.FindAsync(code);

    public async Task<PaymentCondition> CreateAsync(PaymentCondition entity)
    {
        Normalize(entity);

        db.Context.PaymentConditions.Add(entity);
        await db.SaveChangesAsync();

        return entity;
    }

    /// <summary>A entidade chega rastreada (o controller a carrega e aplica o Delta).</summary>
    public async Task<PaymentCondition> UpdateAsync(PaymentCondition entity)
    {
        Normalize(entity);

        await db.SaveChangesAsync();

        return entity;
    }

    public async Task<bool> DeleteAsync(int code)
    {
        var entity = await db.Context.PaymentConditions.FindAsync(code);

        if (entity is null)
            return false;

        var inUse =
            await db.Context.SalesInvoices.AnyAsync(i => i.PaymentConditionCode == code) ||
            await db.Context.BusinessPartners.AnyAsync(p => p.PaymentConditionCode == code);

        if (inUse)
            throw new DefaultException(
                $"A condição de pagamento {entity.Name} está em uso em documento ou parceiro; inative-a em vez de excluir.");

        db.Context.PaymentConditions.Remove(entity);
        await db.SaveChangesAsync();

        return true;
    }

    private static void Normalize(PaymentCondition entity)
    {
        if (string.IsNullOrWhiteSpace(entity.Name))
            throw new DefaultException("Informe o nome da condição de pagamento.");

        entity.Name = entity.Name.Trim();
        entity.PaymentMeans = (entity.PaymentMeans ?? string.Empty).Trim();

        if (!PaymentMeansCodes.All.ContainsKey(entity.PaymentMeans))
            throw new DefaultException($"Meio de pagamento {entity.PaymentMeans} não é aceito pela condição de pagamento.");

        if (!Enum.IsDefined(entity.StartRule))
            throw new DefaultException("Escolha o início da contagem: data de emissão ou fora o mês.");

        entity.Days = string.Join(",", PaymentInstallmentCalculator.ParseDays(entity.Days));
    }
}
