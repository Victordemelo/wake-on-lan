using RemoteWake.Shared;

namespace RemoteWake.Tests.Unit;

public sealed class MagicPacketTests
{
    [Theory]
    [InlineData("AA:BB:CC:DD:EE:FF")]
    [InlineData("aa-bb-cc-dd-ee-ff")]
    [InlineData("AABB.CCDD.EEFF")]
    [InlineData("aabbccddeeff")]
    [InlineData(" AA BB CC DD EE FF ")]
    public void Accepts_common_mac_formats(string input)
    {
        Assert.True(MagicPacket.TryNormalizeMac(input, out var normalized));
        Assert.Equal("AABBCCDDEEFF", normalized);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("AA:BB:CC:DD:EE")]
    [InlineData("AA:BB:CC:DD:EE:FF:00")]
    [InlineData("GG:BB:CC:DD:EE:FF")]
    [InlineData("xyAABBCCDDEEFF")]
    [InlineData("AA:BB:CC:DD:EE:FF; shutdown")]
    public void Rejects_invalid_macs(string? input)
    {
        Assert.False(MagicPacket.TryNormalizeMac(input, out var normalized));
        Assert.Empty(normalized);
    }

    [Fact]
    public void Packet_is_six_ff_bytes_followed_by_the_mac_sixteen_times()
    {
        var packet = MagicPacket.Build("01:23:45:67:89:AB");

        Assert.Equal(MagicPacket.Length, packet.Length);
        Assert.All(packet[..6], value => Assert.Equal(0xFF, value));
        byte[] mac = [0x01, 0x23, 0x45, 0x67, 0x89, 0xAB];
        for (var repetition = 0; repetition < 16; repetition++)
        {
            var offset = 6 + repetition * 6;
            Assert.Equal(mac, packet[offset..(offset + 6)]);
        }
    }

    [Fact]
    public void Building_with_an_invalid_mac_fails()
    {
        Assert.Throws<ArgumentException>(() => MagicPacket.Build("not-a-mac"));
    }
}
