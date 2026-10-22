using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SiagroB1.Commons.Validation;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Models;
using SiagroB1.Fiscal.Certificates;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Transport;

/// <summary>
/// Configuração de transporte da filial (só STANDALONE): RNTRC, seguro padrão e AT&amp;M. A senha da
/// AT&amp;M é cifrada com a chave do certificado (<c>Nfe:CertificateKey</c>) e nunca sai.
/// </summary>
public class BranchTransportSettingsService(IUnitOfWork db, IConfiguration configuration, IAverbaClient averba)
{
    public const string StandaloneOnly = "A configuração de transporte só existe no modo STANDALONE.";

    public async Task<BranchTransportSettingsModel> GetAsync(string branchCode)
    {
        EnsureStandalone();

        var row = await db.Context.BranchTransportSettings.AsNoTracking().FirstOrDefaultAsync(x => x.BranchCode == branchCode);

        return ToModel(row ?? new BranchTransportSettings { BranchCode = branchCode });
    }

    public async Task<BranchTransportSettingsModel> SaveAsync(string branchCode, BranchTransportSettingsModel model, string userName)
    {
        EnsureStandalone();

        var rntrc = Blank(model.CompanyRntrc);
        if (rntrc is not null && (rntrc.Length != 8 || !rntrc.All(char.IsAsciiDigit)))
            throw new DefaultException("O RNTRC tem 8 dígitos.");

        var insurerTaxId = Blank(model.InsurerTaxId) is null ? null : TaxIdValidator.Normalize(model.InsurerTaxId);
        if (insurerTaxId is not null && (insurerTaxId.Length != 14 || !TaxIdValidator.IsValid(insurerTaxId)))
            throw new DefaultException("CNPJ da seguradora inválido.");

        if (model.InsuranceResponsible is { } responsible && !Enum.IsDefined(responsible))
            throw new DefaultException("Escolha o responsável pelo seguro.");

        if (!Enum.IsDefined(model.AverbaEnvironment))
            throw new DefaultException("Escolha o ambiente da AT&M: produção ou homologação.");

        var row = await db.Context.BranchTransportSettings.FirstOrDefaultAsync(x => x.BranchCode == branchCode);
        var newPassword = Blank(model.AverbaPassword);
        var hasPassword = newPassword is not null || row?.AverbaPasswordCipher is { Length: > 0 };

        if (model.AverbaEnabled && (Blank(model.AverbaUser) is null || Blank(model.AverbaCode) is null || !hasPassword))
            throw new DefaultException("Para averbar, informe usuário, senha e código da AT&M.");

        if (row is null)
        {
            row = new BranchTransportSettings { BranchCode = branchCode };
            db.Context.BranchTransportSettings.Add(row);
        }

        row.CompanyRntrc = rntrc;
        row.InsuranceResponsible = model.InsuranceResponsible;
        row.InsurerTaxId = insurerTaxId;
        row.InsurerName = Blank(model.InsurerName);
        row.PolicyNumber = Blank(model.PolicyNumber);
        row.AverbaEnabled = model.AverbaEnabled;
        row.AverbaUser = Blank(model.AverbaUser);
        row.AverbaCode = Blank(model.AverbaCode);
        row.AverbaEnvironment = model.AverbaEnvironment;

        // Senha vazia mantém a gravada; só a nova troca a cifra.
        if (newPassword is not null)
            row.AverbaPasswordCipher = Cipher().Encrypt(newPassword);

        row.UpdatedAt = DateTime.Now;
        row.UpdatedBy = userName;

        await db.SaveChangesAsync();

        return ToModel(row);
    }

    /// <summary>Só faz o login na AT&amp;M; não grava nada. Sem senha digitada, usa a gravada.</summary>
    public async Task<AverbaTestResult> TestAverbaAsync(
        string branchCode, string? user, string? password, string? code, NfeEnvironment environment, CancellationToken ct = default)
    {
        EnsureStandalone();

        var effectivePassword = Blank(password);
        if (effectivePassword is null)
        {
            var row = await db.Context.BranchTransportSettings.AsNoTracking().FirstOrDefaultAsync(x => x.BranchCode == branchCode, ct);
            if (row?.AverbaPasswordCipher is { Length: > 0 } cipher)
                effectivePassword = Cipher().Decrypt(cipher);
        }

        if (Blank(user) is null || Blank(code) is null || effectivePassword is null)
            throw new DefaultException("Informe usuário, senha e código da AT&M para testar.");

        var result = await averba.LoginAsync(environment, user!.Trim(), effectivePassword, code!.Trim(), ct);

        return new AverbaTestResult
        {
            Success = result.Success,
            Message = result.Success ? "Conexão com a AT&M OK." : result.Error ?? "Falha na conexão com a AT&M.",
        };
    }

    private static BranchTransportSettingsModel ToModel(BranchTransportSettings row) => new()
    {
        BranchCode = row.BranchCode,
        CompanyRntrc = row.CompanyRntrc,
        InsuranceResponsible = row.InsuranceResponsible,
        InsurerTaxId = row.InsurerTaxId,
        InsurerName = row.InsurerName,
        PolicyNumber = row.PolicyNumber,
        AverbaEnabled = row.AverbaEnabled,
        AverbaUser = row.AverbaUser,
        HasAverbaPassword = row.AverbaPasswordCipher is { Length: > 0 },
        AverbaCode = row.AverbaCode,
        AverbaEnvironment = row.AverbaEnvironment,
    };

    private void EnsureStandalone()
    {
        if (!ErpMode.IsStandalone(configuration))
            throw new DefaultException(StandaloneOnly);
    }

    private CertificatePasswordCipher Cipher() =>
        CertificatePasswordCipher.FromBase64(configuration[CertificatePasswordCipher.ConfigurationKey]);

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
