using SiagroB1.Application.Services.Nfe;
using SiagroB1.Application.Services.Reinf;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Fiscal.Certificates;
using SiagroB1.Fiscal.Tests.Support;
using Xunit;

namespace SiagroB1.Application.Tests.Reinf;

public class ReinfCertificateProviderTests
{
    private readonly SiagroB1.Infra.UnitOfWork _db = TestDb.CreateUnitOfWork(Guid.NewGuid().ToString());

    private void Branch(string code, string taxId, string certTaxId)
    {
        _db.Context.Branchs.Add(new Branch { Code = code, BranchName = code, TaxId = taxId });
        _db.Context.BranchNfeSettings.Add(new BranchNfeSettings
        {
            BranchCode = code,
            CertificatePfx = TestCertificates.CreatePfx(certTaxId),
            CertificatePasswordCipher = CertificatePasswordCipher.FromBase64(NfeTestSeed.CertificateKey).Encrypt(TestCertificates.Password),
            CertificateValidUntil = DateTime.Now.AddYears(1),
        });
    }

    private void Settings(string branch)
    {
        _db.Context.ReinfSettings.Add(new ReinfSettings { TaxIdRoot = "12345678", BranchCode = branch });
        _db.Context.SaveChanges();
    }

    private ReinfCertificateProvider Provider() => new(_db.Context, new NfeOptions(NfeTestSeed.Config()));

    [Fact]
    public async Task Uses_certificate_of_the_settings_branch()
    {
        Branch("01", "12345678000195", "12345678000195");
        Branch("02", "12345678000276", "12345678000276");
        Settings("02");

        using var cert = await Provider().GetAsync("12345678");
        Assert.Equal("12345678000276", CertificateInspector.Inspect(cert).TaxId);
    }

    [Fact]
    public async Task Falls_back_to_head_office_without_settings_branch()
    {
        Branch("01", "12345678000195", "12345678000195");
        Branch("02", "12345678000276", "12345678000276");
        Settings("");

        using var cert = await Provider().GetAsync("12345678");
        Assert.Equal("12345678000195", CertificateInspector.Inspect(cert).TaxId);
    }

    [Fact]
    public async Task Certificate_of_other_root_is_refused()
    {
        Branch("02", "12345678000276", "99999999000191");
        Settings("02");

        var ex = await Assert.ThrowsAsync<BusinessException>(() => Provider().GetAsync("12345678"));
        Assert.Contains("diferente do contribuinte", ex.Message);
    }
}
