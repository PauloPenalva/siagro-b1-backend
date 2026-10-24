using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Fiscal.Reinf;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.Reinf;

/// <summary>
/// Parâmetros da EFD-Reinf (dados do R-1000) por raiz do CNPJ. A situação do R-1000 não é editável aqui:
/// depois de aceito, salvar mantém Accepted e o próximo envio sai como alteração (há recibo).
/// </summary>
public partial class ReinfSettingsService(AppDbContext context, TimeProvider time)
{
    public async Task<ReinfSettings> GetAsync(string taxIdRoot, CancellationToken ct = default)
    {
        var root = ReinfFormat.Root(taxIdRoot);
        return await context.ReinfSettings.AsNoTracking().FirstOrDefaultAsync(s => s.TaxIdRoot == root, ct)
               ?? new ReinfSettings { TaxIdRoot = root };
    }

    public async Task<ReinfSettings> SaveAsync(ReinfSettings input, string user, CancellationToken ct = default)
    {
        var root = ReinfFormat.Root(input.TaxIdRoot);
        var cpf = ReinfFormat.Digits(input.ContactCpf);
        if (cpf.Length != 11) throw new BusinessException("CPF do contato deve ter 11 dígitos.");
        var responsibleCpf = ReinfFormat.Digits(input.ClosingResponsibleCpf);
        if (responsibleCpf.Length is not (0 or 11)) throw new BusinessException("CPF do responsável pelo fechamento deve ter 11 dígitos.");
        if (!TwoDigits().IsMatch(input.TaxClassification ?? "")) throw new BusinessException("Classificação tributária deve ter 2 dígitos.");
        if (!YearMonth().IsMatch(input.ValidFrom ?? "")) throw new BusinessException("Início da validade deve estar no formato AAAA-MM.");
        if (string.IsNullOrWhiteSpace(input.ContactName)) throw new BusinessException("Informe o nome do contato.");
        if (string.IsNullOrWhiteSpace(input.BranchCode)) throw new BusinessException("Informe a filial do certificado.");

        var s = await context.ReinfSettings.FirstOrDefaultAsync(x => x.TaxIdRoot == root, ct);
        if (s is null) { s = new ReinfSettings { TaxIdRoot = root }; context.ReinfSettings.Add(s); }
        else if (s.Environment != input.Environment && await context.ReinfEvents.AnyAsync(e => e.TaxIdRoot == root, ct))
            throw new BusinessException(EnvironmentLockedMessage);

        var r1000Before = R1000Fingerprint(s);

        s.BranchCode = input.BranchCode;
        s.Environment = input.Environment;
        s.TaxClassification = input.TaxClassification!;
        s.KeepsBookkeeping = input.KeepsBookkeeping;
        s.PayrollRelief = input.PayrollRelief;
        s.FineExemptionAgreement = input.FineExemptionAgreement;
        s.CompanySituation = input.CompanySituation;
        s.ContactName = input.ContactName.Trim();
        s.ContactCpf = cpf;
        s.ContactPhone = Blank(input.ContactPhone);
        s.ContactEmail = Blank(input.ContactEmail);
        s.ValidFrom = input.ValidFrom!;
        s.ClosingResponsibleName = Blank(input.ClosingResponsibleName);
        s.ClosingResponsibleCpf = Blank(responsibleCpf);
        s.ClosingResponsiblePhone = Blank(input.ClosingResponsiblePhone);
        s.ClosingResponsibleEmail = Blank(input.ClosingResponsibleEmail);
        if (s.RegistrationStatus == ReinfRegistrationStatus.Accepted && R1000Fingerprint(s) != r1000Before)
            s.RegistrationUpdatePending = true;
        s.UpdatedAt = time.GetUtcNow().UtcDateTime;
        s.UpdatedBy = user;
        await context.SaveChangesAsync(ct);
        return s;
    }

    public const string EnvironmentLockedMessage = "Não é possível trocar o ambiente da EFD-Reinf depois de enviar eventos neste ambiente.";

    /// <summary>Campos que vão no R-1000: mudou depois do aceite, falta o R-1000 de alteração.</summary>
    private static string R1000Fingerprint(ReinfSettings s) => string.Join('|', s.TaxClassification, s.KeepsBookkeeping,
        s.PayrollRelief, s.FineExemptionAgreement, s.CompanySituation, s.ContactName, s.ContactCpf, s.ContactPhone, s.ContactEmail, s.ValidFrom);

    private static string? Blank(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    [GeneratedRegex(@"^\d{2}$")] private static partial Regex TwoDigits();
    [GeneratedRegex(@"^\d{4}-(0[1-9]|1[0-2])$")] private static partial Regex YearMonth();
}
