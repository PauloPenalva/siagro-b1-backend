using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Tests.Financials;

/// <summary>
/// Os ganchos de DESFAZIMENTO do documento financeiro provisório (Task 7): estorno de
/// fixação confirmada, cancelamento de contrato e encerramento de contrato — mais a
/// regeneração ao reaprovar depois de um estorno.
///
/// Cobre só o COMPORTAMENTO DO GANCHO — quando ele dispara e sobre qual documento — não as
/// regras de cálculo do documento em si (isso é <see cref="FinancialDocumentsGenerateServiceTests"/>).
/// A maioria dos testes usa só o lado COMPRA: os serviços de venda são espelho byte-a-byte
/// (mesma assinatura, mesmo corpo, só a entidade troca) e não haveria comportamento novo a
/// pegar do lado de venda. As DUAS exceções — <see cref="ReversingConfirmedSalesFixation_CancelsItsProvisional_WithReceivableDirectionAndSalesOrigin"/>
/// e <see cref="CancelingASalesContract_CancelsItsOpenProvisionals_WithReceivableDirection"/> —
/// existem para prender os valores que são fáceis de trocar ao colar um espelho:
/// <c>FinancialDirection.Receivable</c> e <c>FinancialDocumentOrigin.SalesContractPriceFixation</c>.
/// Uma leitura humana confirma que o código de produção usa os valores certos hoje; só um teste
/// que roda em CI garante que continuam certos amanhã.
///
/// Cada fase (semear, agir, conferir) usa uma instância de <see cref="AppDbContext"/> própria,
/// todas apontando para o mesmo banco InMemory — evita que o change tracker "conserte" sozinho
/// uma navegação que a query de produção não incluiu.
/// </summary>
public class FinancialDocumentUndoHooksTests
{
    private static AppDbContext NewContext(string dbName) => new(
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static PurchaseContract NewContract(ContractStatus status = ContractStatus.Approved) => new()
    {
        Key = Guid.NewGuid(),
        Code = "PC-UNDO-001",
        CardCode = "F0001",
        ItemCode = "SOJA",
        UnitOfMeasureCode = "KG",
        HarvestSeasonCode = "24/25",
        DeliveryLocationCode = "01",
        TotalVolume = 1_000m,
        Type = ContractType.Fixed,
        StandardCashFlowDate = new DateTime(2026, 12, 31),
        StandardCurrency = CurrencyType.Brl,
        Status = status,
    };

    private static FinancialDocument NewProvisional(
        Guid contractKey, Guid originKey, FinancialDocumentStatus status = FinancialDocumentStatus.Open) => new()
    {
        Key = Guid.NewGuid(),
        Code = $"FD-{Guid.NewGuid():N}"[..10],
        CardCode = "F0001",
        Direction = FinancialDirection.Payable,
        Nature = FinancialDocumentNature.Provisional,
        Status = status,
        DueDate = new DateTime(2026, 12, 31),
        NetAmount = 1_000m,
        OriginType = FinancialDocumentOrigin.PurchaseContractPriceFixation,
        OriginKey = originKey,
        PurchaseContractKey = contractKey,
    };

    private static SalesContract NewSalesContract(ContractStatus status = ContractStatus.Approved) => new()
    {
        Key = Guid.NewGuid(),
        Code = "SC-UNDO-001",
        CardCode = "C0001",
        ItemCode = "SOJA",
        UnitOfMeasureCode = "KG",
        HarvestSeasonCode = "24/25",
        TotalVolume = 1_000m,
        Price = 3m,
        Type = ContractType.Fixed,
        StandardCashFlowDate = new DateTime(2026, 12, 31),
        StandardCurrency = CurrencyType.Brl,
        Status = status,
    };

    /// <summary>
    /// Espelha <see cref="NewProvisional"/>, mas com os dois valores que <b>não</b> podem ser
    /// colados sem trocar: <c>Direction = Receivable</c> (não <c>Payable</c>) e
    /// <c>OriginType = SalesContractPriceFixation</c> (não <c>PurchaseContractPriceFixation</c>).
    /// </summary>
    private static FinancialDocument NewSalesProvisional(
        Guid contractKey, Guid originKey, FinancialDocumentStatus status = FinancialDocumentStatus.Open) => new()
    {
        Key = Guid.NewGuid(),
        Code = $"FD-{Guid.NewGuid():N}"[..10],
        CardCode = "C0001",
        Direction = FinancialDirection.Receivable,
        Nature = FinancialDocumentNature.Provisional,
        Status = status,
        DueDate = new DateTime(2026, 12, 31),
        NetAmount = 1_000m,
        OriginType = FinancialDocumentOrigin.SalesContractPriceFixation,
        OriginKey = originKey,
        SalesContractKey = contractKey,
    };

    [Fact]
    public async Task ReversingConfirmedFixation_CancelsItsProvisional_ButKeepsTheRowAlive()
    {
        var dbName = Guid.NewGuid().ToString();
        var contractKey = Guid.NewGuid();
        var fixationKey = Guid.NewGuid();
        var documentKey = Guid.NewGuid();

        await using (var seed = NewContext(dbName))
        {
            var contract = NewContract();
            contract.Key = contractKey;
            seed.PurchaseContracts.Add(contract);

            seed.PurchaseContractsPriceFixations.Add(new PurchaseContractPriceFixation
            {
                Key = fixationKey,
                PurchaseContractKey = contractKey,
                FixationVolume = 1_000m,
                FixationPrice = 2m,
                Status = PriceFixationStatus.Confirmed,
                ApprovedBy = "diretoria",
                ApprovedAt = DateTime.Now,
            });

            var seededDocument = NewProvisional(contractKey, fixationKey);
            seededDocument.Key = documentKey;
            seed.FinancialDocuments.Add(seededDocument);

            await seed.SaveChangesAsync();
        }

        await using (var act = NewContext(dbName))
        {
            await new PurchaseContractsPriceFixationsCancelService(
                    act,
                    new PurchaseContractsFixedVolumeService(act),
                    new PurchaseContractsChangeLogService(act),
                    TestNotificationOutbox.For(act),
                    FinancialDocumentTestServices.Cancel(act))
                .ExecuteAsync(fixationKey, "tester");
        }

        await using var assert = NewContext(dbName);

        var fixation = await assert.PurchaseContractsPriceFixations.SingleAsync(x => x.Key == fixationKey);
        Assert.Equal(PriceFixationStatus.InApproval, fixation.Status);

        // Continua existindo — CANCELA, não apaga.
        var document = await assert.FinancialDocuments.SingleAsync(x => x.Key == documentKey);
        Assert.Equal(FinancialDocumentStatus.Canceled, document.Status);
        Assert.Equal("Fixação de preço estornada", document.CancellationReason);
    }

    [Fact]
    public async Task ReapprovingAReversedFixation_GeneratesANewDocument_AndTheOldOneStaysCanceled()
    {
        var dbName = Guid.NewGuid().ToString();
        var contractKey = Guid.NewGuid();
        var fixationKey = Guid.NewGuid();
        var originalDocumentKey = Guid.NewGuid();

        await using (var seed = NewContext(dbName))
        {
            var contract = NewContract();
            contract.Key = contractKey;
            seed.PurchaseContracts.Add(contract);

            seed.PurchaseContractsPriceFixations.Add(new PurchaseContractPriceFixation
            {
                Key = fixationKey,
                PurchaseContractKey = contractKey,
                FixationVolume = 1_000m,
                FixationPrice = 2m,
                Status = PriceFixationStatus.Confirmed,
            });

            var seededDocument = NewProvisional(contractKey, fixationKey);
            seededDocument.Key = originalDocumentKey;
            seed.FinancialDocuments.Add(seededDocument);

            await seed.SaveChangesAsync();
        }

        // Fase 1: estorna — cancela o provisório original.
        await using (var reverse = NewContext(dbName))
        {
            await new PurchaseContractsPriceFixationsCancelService(
                    reverse,
                    new PurchaseContractsFixedVolumeService(reverse),
                    new PurchaseContractsChangeLogService(reverse),
                    TestNotificationOutbox.For(reverse),
                    FinancialDocumentTestServices.Cancel(reverse))
                .ExecuteAsync(fixationKey, "tester");
        }

        // Fase 2: reaprova — o índice único filtrado ignora o cancelado, então gera um NOVO.
        await using (var reapprove = NewContext(dbName))
        {
            await new PurchaseContractsPriceFixationsApprovalService(
                    reapprove,
                    new PurchaseContractsFixedVolumeService(reapprove),
                    new PurchaseContractsChangeLogService(reapprove),
                    TestNotificationOutbox.For(reapprove),
                    FinancialDocumentTestServices.Generate(reapprove))
                .ExecuteAsync(fixationKey, "ok novamente", "diretoria");
        }

        await using var assert = NewContext(dbName);

        var documents = await assert.FinancialDocuments
            .Where(x => x.OriginType == FinancialDocumentOrigin.PurchaseContractPriceFixation
                        && x.OriginKey == fixationKey)
            .ToListAsync();

        Assert.Equal(2, documents.Count);

        var original = Assert.Single(documents, x => x.Key == originalDocumentKey);
        Assert.Equal(FinancialDocumentStatus.Canceled, original.Status);

        var replacement = Assert.Single(documents, x => x.Key != originalDocumentKey);
        Assert.Equal(FinancialDocumentStatus.Open, replacement.Status);
    }

    [Fact]
    public async Task CancelingAContract_CancelsItsOpenProvisionals()
    {
        var dbName = Guid.NewGuid().ToString();
        var contractKey = Guid.NewGuid();
        var documentKey = Guid.NewGuid();

        await using (var seed = NewContext(dbName))
        {
            var contract = NewContract();
            contract.Key = contractKey;
            seed.PurchaseContracts.Add(contract);

            var seededDocument = NewProvisional(contractKey, Guid.NewGuid());
            seededDocument.Key = documentKey;
            seed.FinancialDocuments.Add(seededDocument);

            await seed.SaveChangesAsync();
        }

        await using (var act = NewContext(dbName))
        {
            var db = new UnitOfWork(act);
            await new PurchaseContractsCancelService(db, TestNotificationOutbox.For(act), FinancialDocumentTestServices.Cancel(act))
                .ExecuteAsync(contractKey, "washout", "tester");
        }

        await using var assert = NewContext(dbName);

        Assert.Equal(ContractStatus.Canceled, (await assert.PurchaseContracts.SingleAsync(x => x.Key == contractKey)).Status);

        var document = await assert.FinancialDocuments.SingleAsync(x => x.Key == documentKey);
        Assert.Equal(FinancialDocumentStatus.Canceled, document.Status);
        Assert.Equal("Contrato cancelado", document.CancellationReason);
    }

    [Fact]
    public async Task ClosingAContract_CancelsTheRemainingOpenProvisionals()
    {
        var dbName = Guid.NewGuid().ToString();
        var contractKey = Guid.NewGuid();
        var documentKey = Guid.NewGuid();

        await using (var seed = NewContext(dbName))
        {
            var contract = NewContract();
            contract.Key = contractKey;
            seed.PurchaseContracts.Add(contract);

            var seededDocument = NewProvisional(contractKey, Guid.NewGuid());
            seededDocument.Key = documentKey;
            seed.FinancialDocuments.Add(seededDocument);

            await seed.SaveChangesAsync();
        }

        await using (var act = NewContext(dbName))
        {
            await new PurchaseContractsCloseService(
                    act,
                    new PurchaseContractsFixedVolumeService(act),
                    TestNotificationOutbox.For(act),
                    FinancialDocumentTestServices.Cancel(act))
                .ExecuteAsync(contractKey, "tester");
        }

        await using var assert = NewContext(dbName);

        Assert.Equal(ContractStatus.Finished, (await assert.PurchaseContracts.SingleAsync(x => x.Key == contractKey)).Status);

        var document = await assert.FinancialDocuments.SingleAsync(x => x.Key == documentKey);
        Assert.Equal(FinancialDocumentStatus.Canceled, document.Status);
        Assert.Equal("Contrato encerrado", document.CancellationReason);
    }

    /// <summary>
    /// Questão de dinheiro, não de detalhe: o adiantamento pode já ter sido pago. Cancelar o
    /// contrato NÃO pode apagar essa obrigação junto — só os provisórios (Nature = Provisional)
    /// são alcançados.
    /// </summary>
    [Fact]
    public async Task CancelingAContract_DoesNotTouchAnAdvanceLinkedToIt()
    {
        var dbName = Guid.NewGuid().ToString();
        var contractKey = Guid.NewGuid();
        var advanceKey = Guid.NewGuid();

        await using (var seed = NewContext(dbName))
        {
            var contract = NewContract();
            contract.Key = contractKey;
            seed.PurchaseContracts.Add(contract);

            var seededAdvance = NewProvisional(contractKey, Guid.NewGuid());
            seededAdvance.Key = advanceKey;
            seededAdvance.Nature = FinancialDocumentNature.Advance;
            seededAdvance.SettledAmount = 1_000m; // já pago
            seed.FinancialDocuments.Add(seededAdvance);

            await seed.SaveChangesAsync();
        }

        await using (var act = NewContext(dbName))
        {
            var db = new UnitOfWork(act);
            await new PurchaseContractsCancelService(db, TestNotificationOutbox.For(act), FinancialDocumentTestServices.Cancel(act))
                .ExecuteAsync(contractKey, "washout", "tester");
        }

        await using var assert = NewContext(dbName);

        Assert.Equal(ContractStatus.Canceled, (await assert.PurchaseContracts.SingleAsync(x => x.Key == contractKey)).Status);

        var advance = await assert.FinancialDocuments.SingleAsync(x => x.Key == advanceKey);
        Assert.Equal(FinancialDocumentStatus.Open, advance.Status);
        Assert.Null(advance.CancellationReason);
    }

    /// <summary>
    /// Espelho de <see cref="ReversingConfirmedFixation_CancelsItsProvisional_ButKeepsTheRowAlive"/>
    /// do lado VENDA. Além do que o par de compra já cobre, prende os dois valores que um
    /// espelho colado errado trocaria sem que nenhum teste acusasse: Direction e OriginType.
    /// </summary>
    [Fact]
    public async Task ReversingConfirmedSalesFixation_CancelsItsProvisional_WithReceivableDirectionAndSalesOrigin()
    {
        var dbName = Guid.NewGuid().ToString();
        var contractKey = Guid.NewGuid();
        var fixationKey = Guid.NewGuid();
        var documentKey = Guid.NewGuid();

        await using (var seed = NewContext(dbName))
        {
            var contract = NewSalesContract();
            contract.Key = contractKey;
            seed.SalesContracts.Add(contract);

            seed.SalesContractsPriceFixations.Add(new SalesContractPriceFixation
            {
                Key = fixationKey,
                SalesContractKey = contractKey,
                FixationVolume = 1_000m,
                FixationPrice = 3m,
                Status = PriceFixationStatus.Confirmed,
                ApprovedBy = "diretoria",
                ApprovedAt = DateTime.Now,
            });

            var seededDocument = NewSalesProvisional(contractKey, fixationKey);
            seededDocument.Key = documentKey;
            seed.FinancialDocuments.Add(seededDocument);

            await seed.SaveChangesAsync();
        }

        await using (var act = NewContext(dbName))
        {
            await new SalesContractsPriceFixationsCancelService(
                    act,
                    new SalesContractsFixedVolumeService(act),
                    new SalesContractsChangeLogService(act),
                    TestNotificationOutbox.For(act),
                    FinancialDocumentTestServices.Cancel(act))
                .ExecuteAsync(fixationKey, "tester");
        }

        await using var assert = NewContext(dbName);

        var fixation = await assert.SalesContractsPriceFixations.SingleAsync(x => x.Key == fixationKey);
        Assert.Equal(PriceFixationStatus.InApproval, fixation.Status);

        // Continua existindo — CANCELA, não apaga.
        var document = await assert.FinancialDocuments.SingleAsync(x => x.Key == documentKey);
        Assert.Equal(FinancialDocumentStatus.Canceled, document.Status);
        Assert.Equal("Fixação de preço estornada", document.CancellationReason);

        // Os dois valores que um espelho colado errado trocaria em silêncio.
        Assert.Equal(FinancialDirection.Receivable, document.Direction);
        Assert.Equal(FinancialDocumentOrigin.SalesContractPriceFixation, document.OriginType);
    }

    /// <summary>
    /// Espelho de <see cref="CancelingAContract_CancelsItsOpenProvisionals"/> do lado VENDA —
    /// mesmo motivo: prende Direction = Receivable, que <c>EnqueueCancelByContractAsync</c> não
    /// decide (ele só filtra por Nature/status), mas que o documento SEMEADO tem de carregar
    /// certo para o teste valer alguma coisa.
    /// </summary>
    [Fact]
    public async Task CancelingASalesContract_CancelsItsOpenProvisionals_WithReceivableDirection()
    {
        var dbName = Guid.NewGuid().ToString();
        var contractKey = Guid.NewGuid();
        var documentKey = Guid.NewGuid();

        await using (var seed = NewContext(dbName))
        {
            var contract = NewSalesContract();
            contract.Key = contractKey;
            seed.SalesContracts.Add(contract);

            var seededDocument = NewSalesProvisional(contractKey, Guid.NewGuid());
            seededDocument.Key = documentKey;
            seed.FinancialDocuments.Add(seededDocument);

            await seed.SaveChangesAsync();
        }

        await using (var act = NewContext(dbName))
        {
            var db = new UnitOfWork(act);
            await new SalesContractsCancelService(db, TestNotificationOutbox.For(act), FinancialDocumentTestServices.Cancel(act))
                .ExecuteAsync(contractKey, "washout", "tester");
        }

        await using var assert = NewContext(dbName);

        Assert.Equal(ContractStatus.Canceled, (await assert.SalesContracts.SingleAsync(x => x.Key == contractKey)).Status);

        var document = await assert.FinancialDocuments.SingleAsync(x => x.Key == documentKey);
        Assert.Equal(FinancialDocumentStatus.Canceled, document.Status);
        Assert.Equal("Contrato cancelado", document.CancellationReason);
        Assert.Equal(FinancialDirection.Receivable, document.Direction);
    }

    /// <summary>
    /// Regra 11 do spec, provada pelo caminho REAL da tela — não por proxy do gerador nem do
    /// gancho de estorno de fixação (esses já são cobertos acima). Encerrar cancela o provisório
    /// (mesmo gancho de <see cref="ClosingAContract_CancelsTheRemainingOpenProvisionals"/>);
    /// reabrir precisa fazer o índice único filtrado enxergar o cancelado como "livre" e gerar um
    /// documento NOVO para a MESMA fixação confirmada — sem isso o contrato reaberto ficaria sem
    /// nenhum provisório em aberto.
    /// </summary>
    [Fact]
    public async Task ReopeningAClosedContract_RegeneratesTheProvisional_ThroughTheRealCloseAndReopenServices()
    {
        var dbName = Guid.NewGuid().ToString();
        var contractKey = Guid.NewGuid();
        var fixationKey = Guid.NewGuid();
        var originalDocumentKey = Guid.NewGuid();

        await using (var seed = NewContext(dbName))
        {
            var contract = NewContract();
            contract.Key = contractKey;
            seed.PurchaseContracts.Add(contract);

            seed.PurchaseContractsPriceFixations.Add(new PurchaseContractPriceFixation
            {
                Key = fixationKey,
                PurchaseContractKey = contractKey,
                FixationVolume = 1_000m,
                FixationPrice = 2m,
                Status = PriceFixationStatus.Confirmed,
                ApprovedBy = "diretoria",
                ApprovedAt = DateTime.Now,
            });

            var seededDocument = NewProvisional(contractKey, fixationKey);
            seededDocument.Key = originalDocumentKey;
            seed.FinancialDocuments.Add(seededDocument);

            await seed.SaveChangesAsync();
        }

        // Fase 1: encerra — cancela o provisório aberto (mesmo gancho do teste acima).
        await using (var close = NewContext(dbName))
        {
            await new PurchaseContractsCloseService(
                    close,
                    new PurchaseContractsFixedVolumeService(close),
                    TestNotificationOutbox.For(close),
                    FinancialDocumentTestServices.Cancel(close))
                .ExecuteAsync(contractKey, "tester");
        }

        // Fase 2: reabre — o índice único filtrado ignora o cancelado, então regenera um NOVO
        // provisório para a MESMA fixação confirmada.
        await using (var reopen = NewContext(dbName))
        {
            await new PurchaseContractsReopenService(
                    reopen,
                    TestNotificationOutbox.For(reopen),
                    FinancialDocumentTestServices.Generate(reopen))
                .ExecuteAsync(contractKey, "tester");
        }

        await using var assert = NewContext(dbName);

        Assert.Equal(ContractStatus.Approved, (await assert.PurchaseContracts.SingleAsync(x => x.Key == contractKey)).Status);

        var documents = await assert.FinancialDocuments
            .Where(x => x.OriginType == FinancialDocumentOrigin.PurchaseContractPriceFixation
                        && x.OriginKey == fixationKey)
            .ToListAsync();

        Assert.Equal(2, documents.Count);

        var original = Assert.Single(documents, x => x.Key == originalDocumentKey);
        Assert.Equal(FinancialDocumentStatus.Canceled, original.Status);

        var replacement = Assert.Single(documents, x => x.Key != originalDocumentKey);
        Assert.Equal(FinancialDocumentStatus.Open, replacement.Status);
        Assert.NotEqual(original.Key, replacement.Key);
    }
}
