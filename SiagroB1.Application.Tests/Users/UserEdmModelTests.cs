using Microsoft.OData.Edm;
using Microsoft.OData.ModelBuilder;
using SiagroB1.Domain.Entities.Common;
using SiagroB1.Web.ODataConfig;

namespace SiagroB1.Application.Tests.Users;

/// <summary>
/// O EDM real do serviço, montado pelo mesmo <c>ConfigureODataEntities</c> que o Program usa.
/// Propriedade fora do EDM não existe para a tela: o UI5 não acha o tipo, cai no
/// <c>sap.ui.model.odata.type.Raw</c> e o binding estoura em tempo de execução.
/// </summary>
public class UserEdmModelTests
{
    private static IEdmStructuredType UserType()
    {
        var builder = new ODataConventionModelBuilder();
        builder.ConfigureODataEntities();

        return builder.GetEdmModel()
            .SchemaElements
            .OfType<IEdmEntityType>()
            .Single(t => t.Name == nameof(User));
    }

    [Fact]
    public void Users_exposes_the_plain_password_so_the_create_screen_can_send_it()
    {
        // [NotMapped] tira a propriedade do banco E do EDM - o ODataConventionModelBuilder
        // enxerga o atributo. Sem uma inclusão explícita, a tela de novo usuário quebra e o
        // POST nunca leva a senha.
        Assert.Contains(UserType().Properties(), p => p.Name == nameof(User.Password));
    }

    [Fact]
    public void Users_hides_the_password_hash_and_the_photo_blob()
    {
        var properties = UserType().Properties().Select(p => p.Name).ToArray();

        Assert.DoesNotContain(nameof(User.PasswordHash), properties);
        Assert.DoesNotContain(nameof(User.PhotoContent), properties);
        Assert.DoesNotContain(nameof(User.PhotoContentType), properties);
    }
}
