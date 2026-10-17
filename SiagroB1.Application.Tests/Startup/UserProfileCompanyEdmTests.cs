using Microsoft.OData.Edm;
using Microsoft.OData.ModelBuilder;
using SiagroB1.Web.ODataConfig;

namespace SiagroB1.Application.Tests.Startup;

/// <summary>
/// A string de conexão da empresa (com a senha do banco) não pode sair pelo $expand=Company do perfil.
/// </summary>
public class UserProfileCompanyEdmTests
{
    private static IEdmModel BuildModel()
    {
        var builder = new ODataConventionModelBuilder { Namespace = "SIAGROB1" };
        builder.ConfigureODataEntities();
        return builder.GetEdmModel();
    }

    [Fact]
    public void UserProfile_does_not_expose_the_company_navigation()
    {
        var model = BuildModel();
        var profile = model.SchemaElements.OfType<IEdmEntityType>().Single(t => t.Name == "UserProfile");
        var company = model.SchemaElements.OfType<IEdmEntityType>().Single(t => t.Name == "Company");

        Assert.Null(profile.FindProperty("Company"));
        Assert.NotNull(company.FindProperty("ConnectionString"));
    }
}
