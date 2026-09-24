using RemoteWake.Api.Contracts;
using RemoteWake.Api.Models;
using RemoteWake.Api.Services;

namespace RemoteWake.Tests.Unit;

public sealed class MachineRulesTests
{
    private static MachineRequest Request(string name = "PC da sala", string mac = "aa-bb-cc-dd-ee-ff",
        string destination = "192.168.1.255", WakeMethod method = WakeMethod.LocalBroadcast, int port = 9,
        string? hostname = null) => new(name, mac, hostname, destination, port, method);

    [Fact]
    public void Valid_machine_is_accepted_with_normalized_mac()
    {
        Assert.True(MachineRules.TryValidate(Request(), out var mac, out var error));
        Assert.Equal("AABBCCDDEEFF", mac);
        Assert.Empty(error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("A")]
    public void Name_needs_two_to_one_hundred_characters(string name)
    {
        Assert.False(MachineRules.TryValidate(Request(name: name), out _, out var error));
        Assert.Contains("nome", error);
        Assert.False(MachineRules.TryValidate(Request(name: new string('x', 101)), out _, out _));
    }

    [Fact]
    public void Mac_must_be_valid()
    {
        Assert.False(MachineRules.TryValidate(Request(mac: "AA:BB:CC"), out _, out var error));
        Assert.Contains("MAC", error);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    public void Port_must_be_in_range(int port)
    {
        Assert.False(MachineRules.TryValidate(Request(port: port), out _, out var error));
        Assert.Contains("porta", error);
    }

    [Fact]
    public void Wake_method_must_be_known()
    {
        Assert.False(MachineRules.TryValidate(Request(method: (WakeMethod)99), out _, out _));
    }

    [Theory]
    [InlineData("192.168.1.255")]
    [InlineData("255.255.255.255")]
    [InlineData("casa.duckdns.org")]
    [InlineData("203.0.113.10")]
    public void Direct_modes_accept_ipv4_or_ddns(string destination)
    {
        Assert.True(MachineRules.TryValidate(Request(destination: destination, method: WakeMethod.WakeOnWan), out _, out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("10.1")]
    [InlineData("192.168.1")]
    [InlineData("::1")]
    [InlineData("http://example.com")]
    [InlineData("two words")]
    public void Invalid_destinations_are_rejected(string destination)
    {
        Assert.False(MachineRules.TryValidate(Request(destination: destination), out _, out _));
        Assert.False(MachineRules.TryValidate(Request(destination: new string('a', 254)), out _, out _));
    }

    [Fact]
    public void Gateway_mode_requires_an_ipv4_broadcast()
    {
        Assert.True(MachineRules.TryValidate(Request(method: WakeMethod.TailscaleGateway), out _, out _));
        Assert.False(MachineRules.TryValidate(
            Request(destination: "casa.duckdns.org", method: WakeMethod.TailscaleGateway), out _, out var error));
        Assert.Contains("gateway", error);
    }

    [Fact]
    public void Hostname_is_limited()
    {
        Assert.False(MachineRules.TryValidate(Request(hostname: new string('h', 254)), out _, out var error));
        Assert.Contains("hostname", error);
    }
}
