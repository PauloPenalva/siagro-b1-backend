using System.Globalization;
using FastReport;
using Microsoft.Extensions.Configuration;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.PartnerSources;
using SiagroB1.Reports.Services;
using static SiagroB1.Application.Tests.Support.ContractPositionSeed;

namespace SiagroB1.Application.Tests.Reports;

/// <summary>GAC-1198: Posição do Lote e Extrato do Contrato de Compra com Lotes.</summary>
public class StorageLotStatementReportServiceTests
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    static StorageLotStatementReportServiceTests()
    {
        FastReport.Utils.RegisteredObjects.AddConnection(typeof(FastReport.Data.MsSqlDataConnection));
        FastReport.Utils.Config.WebMode = true;
    }

    private sealed class FakePartnerSource(ReportPartnerDto? partner = null, bool fails = false) : IPartnerSource
    {
        public Task<ReportPartnerDto?> GetByCardCodeAsync(string cardCode, CancellationToken ct = default) =>
            fails ? throw new InvalidOperationException("SAP fora do ar") : Task.FromResult(partner);
    }

    private static StorageLotStatementReportService Service(
        IUnitOfWork db, IPartnerSource? partners = null, IFastReportService? report = null) =>
        new(db, report ?? new RecordingFastReportService(), partners ?? new FakePartnerSource());

    private static StorageAddress Lot(string code = "00000001") => new()
    {
        Code = code, Description = "SOJA SAFRA 25/26", CardCode = "F001", CardName = "PRODUTOR RURAL",
        ItemCode = "10001", ItemName = "SOJA EM GRÃOS", WarehouseCode = "ARM01", WarehouseName = "ARMAZÉM CENTRAL",
        UoM = "KG", BranchCode = "01", CreationDate = new DateTime(2026, 8, 1),
    };

    private static StorageTransaction Ticket(
        string code, StorageTransactionType type, decimal net, DateTime date, string lot = "00000001",
        StorageTransactionsStatus status = StorageTransactionsStatus.Confirmed, decimal drying = 0m) => new()
    {
        Key = Guid.NewGuid(), Code = code, TransactionType = type, TransactionStatus = status, TransactionDate = date,
        StorageAddressCode = lot, CardCode = "F001", ItemCode = "10001", UnitOfMeasureCode = "KG", WarehouseCode = "ARM01",
        GrossWeight = net + drying, DryingDiscount = drying, NetWeight = net, TruckCode = "ABC1D23",
    };

    private static StorageTransactionQualityInspection Inspection(StorageTransaction t, string code, decimal value) => new()
    {
        Key = Guid.NewGuid(), StorageTransactionKey = t.Key, QualityAttribCode = code, Value = value,
    };

    private static void SeedAttribs(IUnitOfWork db) => db.Context.QualityAttribs.AddRange(
        new QualityAttrib { Code = "001", Name = "% UMIDADE", Type = default },
        new QualityAttrib { Code = "002", Name = "% IMPUREZAS", Type = default },
        new QualityAttrib { Code = "005", Name = "PH", Type = default });

    [Fact]
    public async Task Lists_only_balance_tickets_with_running_balance_and_quality_columns()
    {
        var db = TestDb.CreateUnitOfWork();
        SeedAttribs(db);
        db.Context.StorageAddresses.Add(Lot());
        var r1 = Ticket("R1", StorageTransactionType.Receipt, 1_000m, new DateTime(2026, 8, 2), drying: 20m);
        var r2 = Ticket("R2", StorageTransactionType.Receipt, 500.5m, new DateTime(2026, 8, 3));
        var s1 = Ticket("S1", StorageTransactionType.Shipment, 300m, new DateTime(2026, 8, 5));
        s1.IsOwnershipTransfer = "Y";
        var q1 = Ticket("Q1", StorageTransactionType.TechnicalLoss, 10.25m, new DateTime(2026, 8, 6),
            status: StorageTransactionsStatus.Invoiced);
        db.Context.StorageTransactions.AddRange(r1, r2, s1, q1,
            Ticket("P1", StorageTransactionType.Receipt, 999m, new DateTime(2026, 8, 4), status: StorageTransactionsStatus.Pending),
            Ticket("C1", StorageTransactionType.Receipt, 999m, new DateTime(2026, 8, 4), status: StorageTransactionsStatus.Cancelled),
            Ticket("D1", StorageTransactionType.Receipt, 999m, new DateTime(2026, 8, 4), status: StorageTransactionsStatus.Returned),
            Ticket("W1", StorageTransactionType.WarehouseLoss, 999m, new DateTime(2026, 8, 4)),
            Ticket("X1", StorageTransactionType.Receipt, 999m, new DateTime(2026, 8, 4), lot: "00000002"));
        db.Context.StorageTransactionQualityInspections.AddRange(
            Inspection(r1, "002", 1m), Inspection(r1, "001", 14.5m), Inspection(r2, "001", 13m), Inspection(r2, "005", 78m));
        await db.Context.SaveChangesAsync();

        var data = await Service(db).BuildAsync("00000001");

        var movement = data.Rows.Where(r => r.Section == StorageLotStatementReportService.MovementSection).ToList();
        Assert.Equal(new[] { "R1", "R2", "S1", "Q1", "" }, movement.Select(r => r.Document).ToArray());
        Assert.Equal(new[] { "Entrada", "Entrada", "Transf. titular.", "Quebra técnica", "" },
            movement.Select(r => r.Movement).ToArray());
        Assert.Equal(new[] { "1.000,000", "1.500,500", "1.200,500", "1.190,250", "1.190,250" },
            movement.Select(r => r.Q3).ToArray());
        Assert.Equal(("1.020,000", "20,000", "1.000,000", ""), (movement[0].W1, movement[0].W2, movement[0].Q1, movement[0].Q2));
        Assert.Equal(("", "300,000"), (movement[2].Q1, movement[2].Q2));

        // Classificação em ordem de código, com título curto.
        Assert.Equal(("UMIDAD", "IMPURE", "PH"), (movement[0].HC1, movement[0].HC2, movement[0].HC3));
        Assert.Equal(("14,5", "1,0", ""), (movement[0].C1, movement[0].C2, movement[0].C3));
        Assert.Equal(("13,0", "", "78,0"), (movement[1].C1, movement[1].C2, movement[1].C3));

        var total = movement[^1];
        Assert.True(total.Bold);
        Assert.Equal(("1.500,500", "310,250"), (total.Q1, total.Q2));

        Assert.Equal((1_500.5m, 300m, 10.25m, 1_190.25m),
            (data.Totals.Receipts, data.Totals.Shipments, data.Totals.TechnicalLoss, data.Totals.Balance));
    }

    [Fact]
    public async Task Expenses_split_ticket_services_and_charges_and_track_invoiced()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.StorageAddresses.Add(Lot());
        var r1 = Ticket("R1", StorageTransactionType.Receipt, 1_000m, new DateTime(2026, 8, 2));
        (r1.DryingServicePrice, r1.ReceiptServicePrice, r1.IsInvoiced) = (120m, 30m, true);
        var s1 = Ticket("S1", StorageTransactionType.Shipment, 300m, new DateTime(2026, 8, 5));
        s1.ShipmentPrice = 45.5m;
        db.Context.StorageTransactions.AddRange(r1, s1);
        db.Context.StorageCharges.AddRange(
            new StorageCharge
            {
                Key = Guid.NewGuid(), StorageAddressCode = "00000001", ChargeType = StorageChargeType.Storage,
                PeriodStart = new DateTime(2026, 8, 1), PeriodEnd = new DateTime(2026, 8, 31),
                BaseQuantity = 1_000m, UnitPriceOrRate = 0.0125m, TotalAmount = 200m, IsInvoiced = true,
            },
            new StorageCharge
            {
                Key = Guid.NewGuid(), StorageAddressCode = "00000001", ChargeType = StorageChargeType.Storage,
                PeriodStart = new DateTime(2026, 9, 1), PeriodEnd = new DateTime(2026, 9, 30),
                BaseQuantity = 700m, UnitPriceOrRate = 0.0125m, TotalAmount = 80m,
            },
            new StorageCharge
            {
                Key = Guid.NewGuid(), StorageAddressCode = "00000002", ChargeType = StorageChargeType.Storage,
                PeriodStart = new DateTime(2026, 9, 1), PeriodEnd = new DateTime(2026, 9, 30), TotalAmount = 9_999m,
            });
        await db.Context.SaveChangesAsync();

        var data = await Service(db).BuildAsync("00000001");

        var services = data.Rows.Where(r => r.Section == StorageLotStatementReportService.TicketServicesSection).ToList();
        Assert.Equal(new[] { ("R1", "150,00", "Faturado"), ("S1", "45,50", "A faturar"), ("", "195,50", "") },
            services.Select(r => (r.Document, r.Q1, r.Detail2)).ToArray());

        var charges = data.Rows.Where(r => r.Section == StorageLotStatementReportService.ChargesSection).ToList();
        Assert.Equal(new[] { ("01/08/2026", "31/08/2026", "200,00"), ("01/09/2026", "30/09/2026", "80,00"), ("Total", "", "280,00") },
            charges.Select(r => (r.Date, r.Document, r.Q1)).ToArray());
        Assert.Equal("0,0125", charges[0].W2);

        Assert.Equal((195.5m, 280m, 350m, 125.5m),
            (data.Totals.TicketServices, data.Totals.Charges, data.Totals.Invoiced, data.Totals.ToInvoice));

        var summary = StorageLotStatementReportService.Summary("KG", data.Totals);
        Assert.Matches(@"Saldo do lote \.*: +700,000 KG", summary);
        Assert.Matches(@"A faturar \.*: +125,50", summary);
    }

    [Fact]
    public async Task Extra_quality_attribs_beyond_six_are_reported_in_the_summary()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.StorageAddresses.Add(Lot());
        var r1 = Ticket("R1", StorageTransactionType.Receipt, 1_000m, new DateTime(2026, 8, 2));
        db.Context.StorageTransactions.Add(r1);
        for (var i = 1; i <= 8; i++)
        {
            // O cadastro precisa existir: a FK é obrigatória e o Include descarta a inspeção órfã.
            db.Context.QualityAttribs.Add(new QualityAttrib { Code = $"00{i}", Name = $"ATRIBUTO {i}", Type = default });
            db.Context.StorageTransactionQualityInspections.Add(Inspection(r1, $"00{i}", i));
        }
        await db.Context.SaveChangesAsync();

        var data = await Service(db).BuildAsync("00000001");

        Assert.Equal(2, data.Totals.HiddenQualityAttribs);
        Assert.Equal("6,0", data.Rows[0].C6);
        Assert.Contains("2 atributo(s) sem coluna", StorageLotStatementReportService.Summary("KG", data.Totals));
    }

    [Fact]
    public async Task Header_uses_partner_source_and_survives_its_failure()
    {
        var db = TestDb.CreateUnitOfWork();
        var lot = Lot();

        var withPartner = await Service(db, new FakePartnerSource(new ReportPartnerDto
        {
            CardName = "JOÃO DA SILVA", Cpf = "123.456.789-00", City = "Sorriso", State = "MT",
        })).HeaderAsync(lot);
        Assert.Contains("Depositante: F001 - JOÃO DA SILVA   CPF/CNPJ: 123.456.789-00   Sorriso/MT", withPartner);

        // SAPB1 com o SAP fora do ar: o papel sai com o nome que o lote guarda.
        var withoutSap = await Service(db, new FakePartnerSource(fails: true)).HeaderAsync(lot);
        Assert.Contains("Depositante: F001 - PRODUTOR RURAL", withoutSap);
        Assert.DoesNotContain("CPF/CNPJ", withoutSap);
    }

    [Fact]
    public async Task Unknown_lot_is_not_found()
    {
        var db = TestDb.CreateUnitOfWork();
        await Assert.ThrowsAsync<NotFoundException>(() => Service(db).ExecuteAsync("NOPE"));
    }

    [Fact]
    public async Task Execute_sends_rows_and_parameters_to_the_shared_template()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.StorageAddresses.Add(Lot());
        db.Context.StorageTransactions.Add(Ticket("R1", StorageTransactionType.Receipt, 1_000m, new DateTime(2026, 8, 2)));
        await db.Context.SaveChangesAsync();
        var report = new RecordingFastReportService();

        await Service(db, report: report).ExecuteAsync("00000001");

        Assert.Equal("StorageLotStatement.frx", report.LastReportName);
        Assert.Equal("POSIÇÃO DO LOTE DE ARMAZENAGEM", report.LastParameters!["pTitle"]);
        Assert.Equal(2, report.LastParameters["pRowCount"]);
        Assert.Contains("Lote: 00000001", (string)report.LastParameters["pHeader"]);
    }

    // ------------------------------------------------------------------ extrato do contrato com lotes

    private static PurchaseContractLotStatementReportService ContractService(IUnitOfWork db, string erp = "STANDALONE")
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Erp"] = erp }).Build();
        var report = new RecordingFastReportService();
        return new PurchaseContractLotStatementReportService(db, report, config,
            new ContractStatementReportService(db, report, config), Service(db, report: report));
    }

    private static async Task<(UnitOfWork Db, PurchaseContract Contract)> SeedContractWithLotAsync()
    {
        var db = TestDb.CreateUnitOfWork();
        SeedAttribs(db);
        var c = Purchase(total: 1_000m, price: 2m);
        db.Context.PurchaseContracts.Add(c);
        db.Context.StorageAddresses.AddRange(Lot(), Lot("00000002"), Lot("00000009"));

        // Lote de origem: uma entrada classificada e a saída da transferência.
        var receipt = Ticket("R1", StorageTransactionType.Receipt, 1_000m, new DateTime(2026, 8, 2), drying: 20m);
        var transferOut = Ticket("S1", StorageTransactionType.Shipment, 600m, new DateTime(2026, 8, 10));
        transferOut.IsOwnershipTransfer = "Y";
        // Perna comercial: Purchase sem lote, alocada no contrato.
        var purchase = Ticket("C1", StorageTransactionType.Purchase, 600m, new DateTime(2026, 8, 10), lot: null!);
        db.Context.StorageTransactions.AddRange(receipt, transferOut, purchase);
        db.Context.StorageTransactionQualityInspections.AddRange(
            Inspection(receipt, "001", 14.5m), Inspection(purchase, "002", 1m));
        db.Context.PurchaseContractsAllocations.Add(new PurchaseContractAllocation
        {
            Key = Guid.NewGuid(), PurchaseContractKey = c.Key, StorageTransactionKey = purchase.Key, Volume = 600m,
        });

        OwnershipTransfer Transfer(string origin, OwnershipTransferStatus status, Guid? contract) => new()
        {
            Key = Guid.NewGuid(), ItemCode = "10001", UomCode = "KG", Quantity = 600m, TransferStatus = status,
            StorageAddressOriginCode = origin, StorageAddressDestinationCode = "00000009", PurchaseContractKey = contract,
        };
        db.Context.OwnershipTransfers.AddRange(
            Transfer("00000001", OwnershipTransferStatus.Closed, c.Key),
            Transfer("00000001", OwnershipTransferStatus.Closed, c.Key),           // mesmo lote duas vezes: um bloco só
            Transfer("00000002", OwnershipTransferStatus.Cancelled, c.Key),        // cancelada não vincula
            Transfer("00000002", OwnershipTransferStatus.Closed, null));           // sem contrato não vincula

        db.Context.FinancialDocuments.Add(new FinancialDocument
        {
            Key = Guid.NewGuid(), Code = "T1", Nature = FinancialDocumentNature.Firm, NetAmount = 1_200m, SettledAmount = 500m,
            Direction = FinancialDirection.Payable, PurchaseContractKey = c.Key, Status = FinancialDocumentStatus.Open,
            CardCode = "F001", DueDate = new DateTime(2026, 9, 30), OriginType = FinancialDocumentOrigin.PurchaseInvoice,
        });
        await db.Context.SaveChangesAsync();
        return (db, c);
    }

    [Fact]
    public async Task Contract_statement_brings_origin_lots_then_contract_sections()
    {
        var (db, c) = await SeedContractWithLotAsync();

        var data = await ContractService(db).BuildAsync(c.Key);

        Assert.Equal(new[] { "00000001" }, data.LotCodes);
        Assert.Contains("Lotes de armazenagem: 00000001", data.Header);

        // Seções em ordem: lote primeiro, contrato depois; nunca voltam.
        Assert.Equal(data.Rows.Select(r => r.Section).Order(), data.Rows.Select(r => r.Section));

        var lotMovement = data.Rows.Where(r => r.Section == StorageLotStatementReportService.MovementSection).ToList();
        Assert.Equal("LOTE 00000001 - MOVIMENTAÇÃO DO LOTE", lotMovement[0].SectionTitle);
        Assert.Equal(new[] { "R1", "S1", "" }, lotMovement.Select(r => r.Document).ToArray());
        Assert.Equal("14,5", lotMovement[0].C1);

        var physical = data.Rows.Where(r => r.Section ==
            PurchaseContractLotStatementReportService.ContractSectionOffset + ContractStatementReportService.PhysicalSection).ToList();
        Assert.Equal("CONTRATO - MOVIMENTAÇÃO FÍSICA", physical[0].SectionTitle);
        // Romaneio do contrato: líquido e alocado nas colunas largas, com a classificação dele.
        Assert.Equal(("C1", "600,000", "600,000", "IMPURE", "1,0"),
            (physical[0].Document, physical[0].Q1, physical[0].Q2, physical[0].HC1, physical[0].C1));
        Assert.True(physical[^1].Bold);

        var titles = data.Rows.Where(r => r.Section ==
            PurchaseContractLotStatementReportService.ContractSectionOffset + ContractStatementReportService.TitlesSection).ToList();
        Assert.Equal(("T1", "1.200,00", "500,00", "700,00"), (titles[0].Document, titles[0].Q1, titles[0].Q2, titles[0].Q3));
        Assert.Equal(("Valor", "Baixado", "Em aberto"), (titles[0].HQ1, titles[0].HQ2, titles[0].HQ3));

        Assert.Contains("LOTE 00000001", data.Summary);
        Assert.Matches(@"Saldo do lote \.*: +400,000 KG", data.Summary);
        Assert.Contains("CONTRATO", data.Summary);
        Assert.Contains("Saldo físico", data.Summary);
    }

    [Fact]
    public async Task Contract_without_transfer_prints_only_the_contract()
    {
        var db = TestDb.CreateUnitOfWork();
        var c = Purchase(total: 1_000m, price: 2m);
        db.Context.PurchaseContracts.Add(c);
        await db.Context.SaveChangesAsync();

        var data = await ContractService(db).BuildAsync(c.Key);

        Assert.Empty(data.LotCodes);
        Assert.Empty(data.Rows);
        Assert.Contains("Lotes de armazenagem: nenhum", data.Header);
        Assert.StartsWith("CONTRATO", data.Summary);
    }

    [Fact]
    public async Task Sapb1_hides_icms_of_the_fiscal_section()
    {
        var (db, c) = await SeedContractWithLotAsync();
        var nf = new PurchaseInvoice
        {
            Key = Guid.NewGuid(), CardCode = "F001", InvoiceType = PurchaseInvoiceType.Normal,
            InvoiceStatus = InvoiceStatus.Confirmed, TaxDocumentNumber = "10", TaxDocumentSeries = "1",
            IssueDate = new DateTime(2026, 8, 11),
        };
        db.Context.PurchaseInvoices.Add(nf);
        db.Context.PurchaseInvoicesItems.Add(new PurchaseInvoiceItem
        {
            Key = Guid.NewGuid(), PurchaseInvoiceKey = nf.Key, PurchaseContractKey = c.Key, ItemCode = "10001",
            Quantity = 600m, UnitPrice = 2m, IcmsValue = 10m, Cfop = "1102",
        });
        await db.Context.SaveChangesAsync();
        var fiscalSection = PurchaseContractLotStatementReportService.ContractSectionOffset + ContractStatementReportService.FiscalSection;

        var standalone = (await ContractService(db).BuildAsync(c.Key)).Rows.First(r => r.Section == fiscalSection);
        Assert.Equal(("600,000", "2,0000", "1.200,00", "10,00", "ICMS"),
            (standalone.W1, standalone.W2, standalone.Q1, standalone.Q2, standalone.HQ2));

        var sap = (await ContractService(db, "SAPB1").BuildAsync(c.Key)).Rows.First(r => r.Section == fiscalSection);
        Assert.Equal(("1.200,00", "", ""), (sap.Q1, sap.Q2, sap.HQ2));
    }

    // ------------------------------------------------------------------ modelo real

    /// <summary>
    /// Prepara o .frx de verdade: prova que o modelo carrega, agrupa por seção sem reordenar e
    /// imprime as células e os parâmetros (o PDF sai como imagem; a página preparada é o último
    /// ponto onde o texto é legível).
    /// </summary>
    [Fact]
    public async Task Template_renders_sections_in_order_with_headers_and_summary()
    {
        var db = TestDb.CreateUnitOfWork();
        SeedAttribs(db);
        db.Context.StorageAddresses.Add(Lot());
        var r1 = Ticket("R1", StorageTransactionType.Receipt, 1_000m, new DateTime(2026, 8, 2), drying: 20m);
        r1.DryingServicePrice = 120m;
        db.Context.StorageTransactions.AddRange(r1, Ticket("S1", StorageTransactionType.Shipment, 300m, new DateTime(2026, 8, 5)));
        db.Context.StorageTransactionQualityInspections.Add(Inspection(r1, "001", 14.5m));
        await db.Context.SaveChangesAsync();
        var data = await Service(db).BuildAsync("00000001");

        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = PtBr;
        try
        {
            using var report = new Report();
            report.Load(Path.Combine(AppContext.BaseDirectory, "ReportTemplates", StorageLotStatementReportService.TemplateName));
            report.RegisterData(data.Rows, StorageLotStatementReportService.DataSourceName);
            report.GetDataSource(StorageLotStatementReportService.DataSourceName).Enabled = true;
            report.SetParameterValue("pTitle", "POSIÇÃO DO LOTE DE ARMAZENAGEM");
            report.SetParameterValue("pHeader", "Lote: 00000001");
            report.SetParameterValue("pSummaryTitle", "RESUMO DO LOTE");
            report.SetParameterValue("pSummary", StorageLotStatementReportService.Summary("KG", data.Totals));
            report.SetParameterValue("pEmptyText", "vazio");
            report.SetParameterValue("pFooterNote", "nota de rodapé");
            report.SetParameterValue("pRowCount", data.Rows.Count);
            Assert.True(report.Prepare());

            var texts = Enumerable.Range(0, report.PreparedPages.Count)
                .SelectMany(i => report.PreparedPages.GetPage(i).AllObjects.OfType<TextObject>())
                .Select(t => (t.Name, t.Text, Bold: t.Font.Bold))
                .ToList();

            Assert.Equal(new[] { "MOVIMENTAÇÃO DO LOTE", "SERVIÇOS DOS ROMANEIOS (R$)" },
                texts.Where(t => t.Name == "txtSectionTitle").Select(t => t.Text).ToArray());
            Assert.Equal(new[] { "R1", "S1", "", "R1", "" },
                texts.Where(t => t.Name == "txtRowDocument").Select(t => t.Text).ToArray());
            Assert.Equal(new[] { "1.000,000", "700,000", "700,000", "", "" },
                texts.Where(t => t.Name == "txtRowQ3").Select(t => t.Text).ToArray());
            // Só a linha de total sai em negrito.
            Assert.Equal(new[] { false, false, true, false, true },
                texts.Where(t => t.Name == "txtRowQ1").Select(t => t.Bold).ToArray());
            Assert.Contains(texts, t => t.Name == "hdrC1" && t.Text == "UMIDAD");
            Assert.Contains(texts, t => t.Name == "txtRowC1" && t.Text == "14,5");
            Assert.Contains(texts, t => t.Name == "txtSummary" && t.Text.Contains("Saldo do lote"));
            Assert.Contains(texts, t => t.Name == "lblSummary" && t.Text == "RESUMO DO LOTE");
            Assert.Contains(texts, t => t.Name == "txtEmpty" && t.Text == "");
            Assert.Contains(texts, t => t.Name == "txtFooterNote" && t.Text == "nota de rodapé");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
