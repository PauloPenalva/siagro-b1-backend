using SiagroB1.Commons.Scales;

namespace SiagroB1.Application.Tests.Scales;

public class ScaleFrameBufferTests
{
    [Fact]
    public void Returns_complete_frames_only()
    {
        var buffer = new ScaleFrameBuffer("\n");

        Assert.Empty(buffer.Append("=0123"));
        Assert.Equal(["=012345"], buffer.Append("45\n").ToArray());
    }

    [Fact]
    public void Returns_every_frame_present_in_a_single_chunk()
    {
        var buffer = new ScaleFrameBuffer("\n");

        Assert.Equal(["=000100", "=000200"], buffer.Append("=000100\n=000200\n").ToArray());
    }

    [Fact]
    public void Keeps_the_incomplete_tail_for_the_next_chunk()
    {
        var buffer = new ScaleFrameBuffer("\n");

        Assert.Equal(["=000100"], buffer.Append("=000100\n=0002").ToArray());
        Assert.Equal(["=000200"], buffer.Append("00\n").ToArray());
    }

    [Fact]
    public void Discards_the_buffer_when_no_terminator_ever_arrives()
    {
        var buffer = new ScaleFrameBuffer("\n", maxLength: 16);

        Assert.Empty(buffer.Append(new string('x', 32)));
        Assert.Equal(["=000100"], buffer.Append("=000100\n").ToArray());
    }
}
