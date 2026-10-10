using System.Globalization;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Commons.Validation;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Infra.Queries;

namespace SiagroB1.Application.Services.RoyaltyWriteOffs;

/// <summary>
/// Registra no SiagroB1 a baixa feita no site do ITS. Uma baixa cobre um ou mais romaneios
/// (recebimento ou compra) da MESMA situação, cada um com peso ≤ saldo (baixa parcial permitida).
/// </summary>
public class RoyaltyWriteOffsCreateService(IUnitOfWork db)
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public async Task<RoyaltyWriteOff> ExecuteAsync(RoyaltyWriteOffInput input, string userName)
    {
        var itsId = input.ItsId?.Trim() ?? "";
        var name = input.Name?.Trim() ?? "";
        if (itsId.Length == 0) throw new ApplicationException("Informe o ID do ITS.");
        if (!TaxIdValidator.IsValid(input.TaxId)) throw new ApplicationException("CPF/CNPJ inválido.");
        if (name.Length == 0) throw new ApplicationException("Informe o nome.");
        if (itsId.Length > 50) throw new ApplicationException("O ID do ITS deve ter no máximo 50 caracteres.");
        if (name.Length > 200) throw new ApplicationException("O nome deve ter no máximo 200 caracteres.");
        var comments = string.IsNullOrWhiteSpace(input.Comments) ? null : input.Comments.Trim();
        if (comments?.Length > 500) throw new ApplicationException("As observações devem ter no máximo 500 caracteres.");
        if (input.Lines is not { Count: > 0 } || input.Lines.Any(l => l.StorageTransactionKey == Guid.Empty))
            throw new ApplicationException("Selecione ao menos um romaneio.");
        if (input.Lines.Any(l => l.Weight <= 0))
            throw new ApplicationException("O peso baixado deve ser maior que zero.");
        if (input.Lines.GroupBy(l => l.StorageTransactionKey).Any(g => g.Count() > 1))
            throw new ApplicationException("Um romaneio não pode aparecer mais de uma vez na mesma baixa.");

        try
        {
            await db.BeginTransactionAsync();

            var keys = input.Lines.Select(l => l.StorageTransactionKey).ToList();
            var balances = await RoyaltyBalanceQuery.Build(db.Context)
                .Where(x => keys.Contains(x.StorageTransactionKey))
                .ToDictionaryAsync(x => x.StorageTransactionKey);

            var missing = keys.Where(k => !balances.ContainsKey(k)).Select(k => (Guid?)k).FirstOrDefault();
            if (missing != null)
            {
                var code = await db.Context.StorageTransactions.Where(x => x.Key == missing.Value)
                    .Select(x => x.Code).FirstOrDefaultAsync();
                throw new ApplicationException($"Romaneio {code ?? missing.ToString()} não está apto para baixa ITS.");
            }

            var situations = balances.Values.Select(x => x.Situation).Distinct().ToList();
            if (situations.Count > 1)
                throw new ApplicationException("Selecione romaneios de uma mesma situação.");

            foreach (var line in input.Lines)
            {
                var row = balances[line.StorageTransactionKey];
                if (line.Weight > row.Balance)
                    throw new ApplicationException(
                        $"O peso baixado excede o saldo do romaneio {row.Code} (saldo {row.Balance.ToString("N3", PtBr)} kg).");
            }

            var duplicated = await db.Context.RoyaltyWriteOffs
                .Where(x => x.ItsId == itsId && x.Status == RoyaltyWriteOffStatus.Active)
                .Select(x => (int?)x.RowId)
                .FirstOrDefaultAsync();
            if (duplicated != null)
                throw new ApplicationException($"ID do ITS {itsId} já utilizado na baixa {duplicated}.");

            var writeOff = new RoyaltyWriteOff
            {
                WriteOffDate = input.WriteOffDate.Date,
                ItsId = itsId,
                TaxId = TaxIdValidator.Normalize(input.TaxId),
                Name = name,
                Situation = situations[0],
                Status = RoyaltyWriteOffStatus.Active,
                Comments = comments,
                CreatedBy = userName,
                CreatedAt = DateTime.Now,
                UpdatedBy = userName,
                UpdatedAt = DateTime.Now,
            };
            foreach (var line in input.Lines)
                writeOff.Lines.Add(new RoyaltyWriteOffLine
                {
                    StorageTransactionKey = line.StorageTransactionKey,
                    Weight = line.Weight,
                });

            await db.Context.RoyaltyWriteOffs.AddAsync(writeOff);
            await db.SaveChangesAsync();
            await db.CommitAsync();
            return writeOff;
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            // Corrida com outra baixa do mesmo ID: o índice único filtrado é a trava final.
            await db.RollbackAsync();
            throw new ApplicationException($"ID do ITS {itsId} já utilizado em outra baixa ativa.");
        }
        catch (Exception e)
        {
            await db.RollbackAsync();
            throw e as ApplicationException ?? new ApplicationException(e.Message);
        }
    }
}
