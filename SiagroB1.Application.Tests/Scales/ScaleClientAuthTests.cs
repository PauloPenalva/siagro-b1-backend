using SiagroB1.Commons.Scales;

namespace SiagroB1.Application.Tests.Scales;

public class ScaleClientAuthTests
{
    private const string Configured = "chave-da-balanca";

    [Fact]
    public void A_request_with_the_configured_key_is_accepted()
    {
        Assert.True(ScaleClientAuth.IsAuthorized(Configured, Configured));
    }

    [Fact]
    public void A_request_without_the_header_is_refused()
    {
        Assert.False(ScaleClientAuth.IsAuthorized(Configured, null));
    }

    [Fact]
    public void A_request_with_the_wrong_key_is_refused()
    {
        Assert.False(ScaleClientAuth.IsAuthorized(Configured, "outra-chave"));
    }

    [Fact]
    public void A_key_that_only_differs_in_case_is_refused()
    {
        Assert.False(ScaleClientAuth.IsAuthorized(Configured, Configured.ToUpperInvariant()));
    }

    [Fact]
    public void Every_request_is_accepted_when_no_key_is_configured()
    {
        Assert.True(ScaleClientAuth.IsAuthorized(null, null));
    }

    [Fact]
    public void A_blank_configured_key_counts_as_not_configured()
    {
        Assert.True(ScaleClientAuth.IsAuthorized("   ", null));
    }
}
