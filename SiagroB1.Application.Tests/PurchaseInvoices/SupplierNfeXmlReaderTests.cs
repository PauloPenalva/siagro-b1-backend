using System.Globalization;
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Tests.PurchaseInvoices;

/// <summary>Leitura do det da NF-e do fornecedor: nItem e tributação, no layout real (spec terceiro §6).</summary>
public class SupplierNfeXmlReaderTests
{
    [Fact]
    public void Reads_icms51_deferral_and_ibs_cbs()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pt-BR");
        try
        {
            var nfe = SupplierNfeXmlReader.Read(SupplierNfeXml.Bytes(SupplierNfeXml.Build(
                SupplierNfeXml.Det(1, "TRG", "TRIGO", 1000m, 1.5m, SupplierNfeXml.Icms51(1500m), SupplierNfeXml.IbsCbs()))));

            Assert.Equal(SupplierNfeXml.AccessKey, nfe.AccessKey);
            var item = Assert.Single(nfe.Items);
            Assert.Equal((1, "TRG", "5102", "10019900", "SP053521"), (item.ItemNumber, item.ProductCode, item.Cfop, item.Ncm, item.BenefitCode));
            Assert.Equal((1000m, 1.5m), (item.Quantity, item.UnitPrice));
            Assert.Equal(("51", (byte?)0, 1500m, 18m, 0m), (item.CstIcms, item.GoodsOrigin, item.IcmsBase, item.IcmsRate, item.IcmsValue));
            Assert.Equal((100m, 270m, 270m), (item.IcmsDeferral, item.IcmsOperationValue, item.IcmsDeferredValue));
            Assert.Equal(("49", "49"), (item.CstPis, item.CstCofins));
            Assert.Equal(("200", "200036", 1500m), (item.IbsCbsCst, item.IbsCbsClassCode, item.IbsCbsBase));
            Assert.Equal((0.1m, 0m, 60m, 0.6m, 0m), (item.IbsStateRate, item.IbsMunicipalRate, item.IbsRateReduction, item.IbsStateValue, item.IbsMunicipalValue));
            Assert.Equal((0.9m, 60m, 5.4m), (item.CbsRate, item.CbsRateReduction, item.CbsValue));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Reads_icms20_reduction_and_icms00_value()
    {
        var nfe = SupplierNfeXmlReader.Read(SupplierNfeXml.Bytes(SupplierNfeXml.Build(
            SupplierNfeXml.Det(1, "A", "A", 10m, 10m, SupplierNfeXml.Icms20(100m, 33.33m, 12m)),
            SupplierNfeXml.Det(2, "B", "B", 10m, 10m, SupplierNfeXml.Icms00(100m, 18m)))));

        Assert.Equal(("20", 33.33m, 12m), (nfe.Items[0].CstIcms, nfe.Items[0].IcmsBaseReduction, nfe.Items[0].IcmsRate));
        Assert.Equal(("00", 18m, 18m), (nfe.Items[1].CstIcms, nfe.Items[1].IcmsRate, nfe.Items[1].IcmsValue));
        Assert.Equal(2, nfe.Items[1].ItemNumber);
    }

    [Fact]
    public void Reads_csosn_in_the_icms_cst()
    {
        var nfe = SupplierNfeXmlReader.Read(SupplierNfeXml.Bytes(SupplierNfeXml.Build(
            SupplierNfeXml.Det(1, "A", "A", 1m, 1m, SupplierNfeXml.IcmsSn101()))));

        Assert.Equal("101", nfe.Items[0].CstIcms);
    }

    [Fact]
    public void Reads_ipi_and_st_values()
    {
        var nfe = SupplierNfeXmlReader.Read(SupplierNfeXml.Bytes(SupplierNfeXml.Build(
            SupplierNfeXml.Det(1, "A", "A", 1m, 1m, SupplierNfeXml.Icms10WithSt(), ipi: SupplierNfeXml.Ipi(50m)))));

        Assert.Equal((36m, 50m), (nfe.Items[0].IcmsStValue, nfe.Items[0].IpiValue));
    }

    [Fact]
    public void Reads_a_plain_nfe_without_the_protocol_envelope()
    {
        var nfe = SupplierNfeXmlReader.Read(SupplierNfeXml.Bytes(SupplierNfeXml.BuildPlain(
            SupplierNfeXml.Det(7, "A", "A", 1m, 1m, SupplierNfeXml.Icms51(1m)))));

        Assert.Equal(7, Assert.Single(nfe.Items).ItemNumber);
    }

    [Fact]
    public void Refuses_a_file_that_is_not_an_nfe()
    {
        var e = Assert.Throws<DefaultException>(() => SupplierNfeXmlReader.Read(SupplierNfeXml.Bytes("<a/>")));

        Assert.Equal("XML não parece uma NF-e: elemento infNFe não encontrado.", e.Message);
    }
}
