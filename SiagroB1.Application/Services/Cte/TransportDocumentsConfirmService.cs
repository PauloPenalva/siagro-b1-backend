using System.Globalization;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Fiscal.Cte;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Cte;

/// <summary>
/// Confirma o CT-e (Rascunho → Confirmado). Na primeira confirmação reserva o número, sorteia o cCT e monta a chave
/// de acesso (mês da confirmação); depois disso número, cCT e chave nunca mudam — reabrir e confirmar de novo só
/// reaproveita. Para trocar o número é preciso "Descartar número" (que o manda para a inutilização).
/// </summary>
public class TransportDocumentsConfirmService(
    IUnitOfWork db,
    IConfiguration configuration,
    CteNumberReservationService numbers,
    CteReadinessValidator validator)
{
    public const string NotDraft = "Só é possível confirmar CT-e em rascunho.";

    // Código IBGE da UF (cUF da chave de acesso).
    private static readonly Dictionary<string, string> UfCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["RO"] = "11", ["AC"] = "12", ["AM"] = "13", ["RR"] = "14", ["PA"] = "15", ["AP"] = "16", ["TO"] = "17",
        ["MA"] = "21", ["PI"] = "22", ["CE"] = "23", ["RN"] = "24", ["PB"] = "25", ["PE"] = "26", ["AL"] = "27",
        ["SE"] = "28", ["BA"] = "29", ["MG"] = "31", ["ES"] = "32", ["RJ"] = "33", ["SP"] = "35", ["PR"] = "41",
        ["SC"] = "42", ["RS"] = "43", ["MS"] = "50", ["MT"] = "51", ["GO"] = "52", ["DF"] = "53",
    };

    public async Task ConfirmAsync(Guid key, string user)
    {
        TransportDocumentsSaveService.EnsureStandalone(configuration);

        // Trava por documento: duas confirmações simultâneas não reservam dois números.
        await using var emissionLock = await numbers.AcquireEmissionLockAsync(key);

        var doc = await db.Context.TransportDocuments.FirstOrDefaultAsync(x => x.Key == key)
                  ?? throw new DefaultException("CT-e não encontrado.");
        if (doc.Status != CteStatus.Draft)
            throw new DefaultException(NotDraft);

        await validator.EnsureReadyAsync(doc);

        var settings = await db.Context.BranchCteSettings.AsNoTracking().FirstAsync(x => x.BranchCode == doc.BranchCode);

        if (doc.Number is null)
        {
            var branch = await db.Context.Branchs.AsNoTracking().FirstOrDefaultAsync(x => x.Code == doc.BranchCode)
                         ?? throw new DefaultException($"Filial {doc.BranchCode} não encontrada.");
            var ufCode = UfCodes.GetValueOrDefault(branch.StateCode?.Trim() ?? string.Empty)
                         ?? throw new DefaultException($"A filial {doc.BranchCode} está sem UF válida no cadastro.");
            if (string.IsNullOrWhiteSpace(branch.TaxId))
                throw new DefaultException($"A filial {doc.BranchCode} está sem CNPJ no cadastro.");

            // Conexão própria, fora da transação do EF: o número reservado não volta se o save falhar.
            var number = await numbers.ReserveAsync(doc.BranchCode, doc.Series);
            var now = DateTime.Now;
            doc.Number = number;
            doc.RandomCode = GenerateRandomCode(number);
            doc.IssuedAt = now;
            doc.AccessKey = CteAccessKey.Build(ufCode, now, branch.TaxId.Trim(), doc.Series, number, 1, doc.RandomCode);
        }

        doc.Environment = settings.Environment;
        doc.Status = CteStatus.Confirmed;
        doc.UpdatedAt = DateTime.Now;
        doc.UpdatedBy = user;
        await db.SaveChangesAsync();
    }

    /// <summary>cCT: 8 dígitos aleatórios, nunca iguais ao número com zeros à esquerda (regra da SEFAZ).</summary>
    internal static string GenerateRandomCode(int number)
    {
        var forbidden = number.ToString("00000000", CultureInfo.InvariantCulture);
        string code;
        do
        {
            code = RandomNumberGenerator.GetInt32(0, 100_000_000).ToString("00000000", CultureInfo.InvariantCulture);
        } while (code == forbidden);
        return code;
    }
}
