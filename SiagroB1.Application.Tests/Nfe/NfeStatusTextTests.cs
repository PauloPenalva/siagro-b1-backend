using SiagroB1.Application.Services.Nfe;

namespace SiagroB1.Application.Tests.Nfe;

public class NfeStatusTextTests
{
    [Fact]
    public void Reason_is_cut_at_500_characters()
    {
        Assert.Equal(500, NfeStatusText.Truncate(new string('x', 600)).Length);
        Assert.Equal("curto", NfeStatusText.Truncate("curto"));
    }
}
