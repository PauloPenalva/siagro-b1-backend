using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Cte;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Fiscal.Certificates;
using SiagroB1.Fiscal.Tests.Support;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Cte;

/// <summary>Base dos testes de cancelamento, CC-e e inutilização: filial, certificado e CT-e autorizado semeados.</summary>
public abstract class CteEventsTestBase
{
    protected static readonly DateTimeOffset Now = new(2026, 10, 23, 10, 0, 0, TimeSpan.FromHours(-3));

    protected readonly UnitOfWork Db = TestDb.CreateUnitOfWork();
    protected readonly FakeCteNumberReservationService Numbers = new(100);
    protected readonly FakeCteSefazClient Sefaz = new();
    protected DateTimeOffset Clock = Now;

    protected CteServiceContextFactory Contexts() => new(Db, new NfeOptions(NfeTestSeed.Config()));

    protected async Task<Guid> SeedAsync(CteStatus status = CteStatus.Authorized, int number = 100, DateTime? authorizedAt = null)
    {
        if (!await Db.Context.Branchs.AnyAsync())
        {
            Db.Context.Branchs.Add(new Branch
            {
                Code = "01", BranchName = "TRANSPORTADORA", TaxId = "12345678000195", StateCode = "SP",
                StateRegistration = "387000000110", TaxRegime = TaxRegime.Normal, IssuesNfe = true,
            });
            Db.Context.BranchCteSettings.Add(new BranchCteSettings { BranchCode = "01", Enabled = true });
            Db.Context.BranchNfeSettings.Add(new BranchNfeSettings
            {
                BranchCode = "01",
                CertificatePfx = TestCertificates.CreatePfx("12345678000195"),
                CertificatePasswordCipher = CertificatePasswordCipher.FromBase64(NfeTestSeed.CertificateKey).Encrypt(TestCertificates.Password),
                CertificateValidUntil = DateTime.Now.AddYears(1),
            });
        }

        var doc = new TransportDocument
        {
            BranchCode = "01", Series = 1, Number = number, Status = status, Environment = NfeEnvironment.Homologation,
            AccessKey = "35261012345678000195570010000001001" + number.ToString("00000000"),
            Protocol = FakeCteSefazClient.AuthorizationProtocol,
            AuthorizedAt = authorizedAt ?? Now.DateTime.AddHours(-2),
        };
        Db.Context.TransportDocuments.Add(doc);
        await Db.SaveChangesAsync();
        return doc.Key;
    }

    protected Task<TransportDocument> LoadAsync(Guid key) =>
        Db.Context.TransportDocuments.AsNoTracking().Include(x => x.Xmls).SingleAsync(x => x.Key == key);
}
