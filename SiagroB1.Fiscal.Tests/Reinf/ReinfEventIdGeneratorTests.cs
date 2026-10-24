using SiagroB1.Fiscal.Reinf;

namespace SiagroB1.Fiscal.Tests.Reinf;

public class ReinfEventIdGeneratorTests
{
    [Fact]
    public void Next_SequencePerSecondAndResets()
    {
        var gen = new ReinfEventIdGenerator();
        var t = new DateTime(2026, 10, 24, 9, 5, 7);
        Assert.Equal("ID11234567800000020261024090507" + "00001", gen.Next("12345678", t));
        Assert.Equal("ID11234567800000020261024090507" + "00002", gen.Next("12345678", t));
        Assert.Equal("ID11234567800000020261024090508" + "00001", gen.Next("12345678", t.AddSeconds(1)));
    }

    [Fact]
    public void Next_IsUniqueUnderConcurrency()
    {
        var gen = new ReinfEventIdGenerator();
        var t = new DateTime(2026, 10, 24, 9, 5, 7);
        var ids = new System.Collections.Concurrent.ConcurrentBag<string>();
        Parallel.For(0, 500, _ => ids.Add(gen.Next("12345678", t)));
        Assert.Equal(500, ids.Distinct().Count());
        Assert.All(ids, i => Assert.Equal(36, i.Length));
    }
}
