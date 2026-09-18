using Microsoft.OData.Edm;
using Microsoft.OData.ModelBuilder;
using SiagroB1.Domain.Entities;
using SiagroB1.Web.ODataConfig;

namespace SiagroB1.Application.Tests.ShipmentLoads;

/// <summary>
/// O EDM é contrato com a tela: action ou propriedade que não está aqui devolve 404/400 no
/// navegador, e nenhum teste de serviço acusa.
/// </summary>
public class ShipmentLoadDischargeEdmModelTests
{
    // Mesmo helper de ShipmentLoadEdmModelTests: o EDM real, montado pelo
    // ConfigureODataEntities que o Program usa.
    private static IEdmModel Model()
    {
        var builder = new ODataConventionModelBuilder();
        builder.ConfigureODataEntities();

        return builder.GetEdmModel();
    }

    [Theory]
    [InlineData("ShipmentLoadsDischargeCreate")]
    [InlineData("ShipmentLoadsDischargeUpdate")]
    [InlineData("ShipmentLoadsDischargeDelete")]
    [InlineData("ShipmentLoadsAttachmentUpload")]
    [InlineData("ShipmentLoadsAttachmentDelete")]
    public void Declares_the_write_actions(string name)
    {
        Assert.Contains(Model().SchemaElements.OfType<IEdmAction>(), a => a.Name == name);
    }

    [Theory]
    [InlineData("ShipmentLoadsAttachmentsList")]
    [InlineData("ShipmentLoadsAttachmentsDownload")]
    public void Declares_the_read_functions(string name)
    {
        Assert.Contains(Model().SchemaElements.OfType<IEdmFunction>(), f => f.Name == name);
    }

    [Fact]
    public void Declares_the_discharges_entity_set()
    {
        Assert.NotNull(Model().EntityContainer.FindEntitySet("ShipmentLoadsDischarges"));
    }

    [Fact]
    public void Exposes_the_derived_ticket_quantities()
    {
        var load = (IEdmStructuredType)Model().FindDeclaredType(typeof(ShipmentLoad).FullName);
        Assert.NotNull(load.FindProperty(nameof(ShipmentLoad.DischargedQuantity)));

        var item = (IEdmStructuredType)Model().FindDeclaredType(typeof(SalesInvoiceItem).FullName);
        Assert.NotNull(item.FindProperty(nameof(SalesInvoiceItem.TicketDeliveredQuantity)));
    }

    [Fact]
    public void Create_action_takes_the_ticket_parameters()
    {
        var action = Model().SchemaElements.OfType<IEdmAction>()
            .Single(a => a.Name == "ShipmentLoadsDischargeCreate");

        var names = action.Parameters.Select(p => p.Name).ToHashSet();
        Assert.Contains("LoadKey", names);
        Assert.Contains("SalesInvoiceKey", names);
        Assert.Contains("SalesInvoiceItemKey", names);
        Assert.Contains("TicketNumber", names);
        Assert.Contains("DischargeDate", names);
        Assert.Contains("Quantity", names);
    }

    /// <summary>
    /// ⚠️ Edm.Double, NUNCA Edm.Decimal: decimal faz o cliente serializar o número como string e
    /// o backend devolve 400 que não nomeia o campo. A data viaja como string pelo mesmo motivo —
    /// Edm.Date em parâmetro de action já custou uma sessão aqui.
    /// </summary>
    [Theory]
    [InlineData("ShipmentLoadsDischargeCreate")]
    [InlineData("ShipmentLoadsDischargeUpdate")]
    public void The_ticket_weight_travels_as_double_and_the_date_as_string(string name)
    {
        var action = Model().SchemaElements.OfType<IEdmAction>().Single(a => a.Name == name);

        Assert.Equal(
            "Edm.Double",
            action.Parameters.Single(p => p.Name == "Quantity").Type.Definition.FullTypeName());
        Assert.Equal(
            "Edm.String",
            action.Parameters.Single(p => p.Name == "DischargeDate").Type.Definition.FullTypeName());
    }

    /// <summary>
    /// ⚠️ O <c>ODataParameterReader</c> RECUSA o payload que não traga todo parâmetro não-opcional
    /// da assinatura. O caminho feliz da tela é registrar o ticket SEM arquivo, então um
    /// <c>.Optional()</c> esquecido em File/FileName/ContentType derruba o caminho principal com
    /// um 400 que não nomeia o campo.
    /// </summary>
    [Theory]
    [InlineData("TicketNumber")]
    [InlineData("Comments")]
    [InlineData("File")]
    [InlineData("FileName")]
    [InlineData("ContentType")]
    public void The_create_action_lets_the_optional_parameters_be_omitted(string name)
    {
        var action = Model().SchemaElements.OfType<IEdmAction>()
            .Single(a => a.Name == "ShipmentLoadsDischargeCreate");

        Assert.IsAssignableFrom<IEdmOptionalParameter>(action.Parameters.Single(p => p.Name == name));
    }

    /// <summary>
    /// Espelho do anterior no Update — com a diferença que importa: a DATA é obrigatória aqui.
    /// O serviço grava <c>DischargeDate</c> sem condição, então deixá-la faltar carimbaria a data
    /// de hoje por cima da data já registrada, sem erro e sem log.
    /// </summary>
    [Fact]
    public void The_update_action_requires_the_date_and_lets_the_text_be_omitted()
    {
        var action = Model().SchemaElements.OfType<IEdmAction>()
            .Single(a => a.Name == "ShipmentLoadsDischargeUpdate");

        Assert.IsAssignableFrom<IEdmOptionalParameter>(
            action.Parameters.Single(p => p.Name == "TicketNumber"));
        Assert.IsAssignableFrom<IEdmOptionalParameter>(
            action.Parameters.Single(p => p.Name == "Comments"));

        Assert.IsNotAssignableFrom<IEdmOptionalParameter>(
            action.Parameters.Single(p => p.Name == "DischargeDate"));
        Assert.IsNotAssignableFrom<IEdmOptionalParameter>(
            action.Parameters.Single(p => p.Name == "Quantity"));
    }

    /// <summary>
    /// O tipo do anexo viaja como string, como todo enum em parâmetro de action neste EDM.
    /// </summary>
    [Fact]
    public void Attachment_upload_takes_the_typed_parameters()
    {
        var action = Model().SchemaElements.OfType<IEdmAction>()
            .Single(a => a.Name == "ShipmentLoadsAttachmentUpload");

        var names = action.Parameters.Select(p => p.Name).ToHashSet();
        Assert.Contains("LoadKey", names);
        Assert.Contains("AttachmentType", names);
        Assert.Contains("Description", names);
        Assert.Contains("File", names);
        Assert.Contains("FileName", names);
        Assert.Contains("ContentType", names);

        Assert.Equal(
            "Edm.String",
            action.Parameters.Single(p => p.Name == "AttachmentType").Type.Definition.FullTypeName());

        // O controller tem default para os três — o que só serve para alguma coisa se o payload
        // puder omiti-los. Description e File seguem obrigatórios porque o controller os exige.
        Assert.IsAssignableFrom<IEdmOptionalParameter>(
            action.Parameters.Single(p => p.Name == "AttachmentType"));
        Assert.IsAssignableFrom<IEdmOptionalParameter>(
            action.Parameters.Single(p => p.Name == "FileName"));
        Assert.IsAssignableFrom<IEdmOptionalParameter>(
            action.Parameters.Single(p => p.Name == "ContentType"));

        Assert.IsNotAssignableFrom<IEdmOptionalParameter>(
            action.Parameters.Single(p => p.Name == "Description"));
        Assert.IsNotAssignableFrom<IEdmOptionalParameter>(
            action.Parameters.Single(p => p.Name == "File"));
    }
}
