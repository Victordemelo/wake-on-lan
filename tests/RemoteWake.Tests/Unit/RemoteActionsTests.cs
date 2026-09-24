using System.Net;
using RemoteWake.Shared;

namespace RemoteWake.Tests.Unit;

public sealed class RemoteActionsTests
{
    [Theory]
    [InlineData("shutdown", false)]
    [InlineData("restart", false)]
    [InlineData("suspend", true)]
    [InlineData("hibernate", true)]
    public void Power_actions_are_allowed(string action, bool sleep)
    {
        Assert.True(RemoteActions.IsPowerAction(action));
        Assert.Equal(sleep, RemoteActions.IsSleep(action));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("wake")]
    [InlineData("online")]
    [InlineData("SHUTDOWN")]
    [InlineData("shutdown; rm -rf /")]
    [InlineData("format c:")]
    public void Anything_else_is_rejected(string? action)
    {
        Assert.False(RemoteActions.IsPowerAction(action));
    }

    [Theory]
    [InlineData("shutdown", true, "shutdown.exe", "/s /t 30")]
    [InlineData("restart", true, "shutdown.exe", "/r /t 30")]
    [InlineData("shutdown", false, "shutdown", "-h +1")]
    [InlineData("restart", false, "shutdown", "-r +1")]
    [InlineData("hibernate", true, "shutdown.exe", "/h")]
    [InlineData("suspend", false, "systemctl", "suspend")]
    [InlineData("hibernate", false, "systemctl", "hibernate")]
    public void Actions_map_to_native_commands(string action, bool windows, string file, string arguments)
    {
        var command = PowerCommand.For(action, windows);

        Assert.Equal(file, command.FileName);
        Assert.Equal(arguments, string.Join(' ', command.Arguments));
    }

    [Fact]
    public void Windows_suspend_does_not_use_rundll32_which_may_hibernate()
    {
        var command = PowerCommand.For(RemoteActions.Suspend, windows: true);

        Assert.Equal("powershell.exe", command.FileName);
        Assert.Contains("SetSuspendState('Suspend'", command.Arguments[^1]);
        Assert.Contains("exit 1", command.Arguments[^1]);
    }

    [Fact]
    public void Unknown_actions_have_no_command()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PowerCommand.For("format", windows: true));
    }
}

public sealed class GatewayAllowListTests
{
    [Fact]
    public void Only_listed_ipv4_destinations_and_valid_ports_are_permitted()
    {
        var allowed = GatewayAllowList.Parse(" 192.168.1.255 , 10.0.0.255,,");

        Assert.Equal(["192.168.1.255", "10.0.0.255"], allowed);
        Assert.True(GatewayAllowList.Permits(allowed, "192.168.1.255", 9, out var address));
        Assert.Equal(IPAddress.Parse("192.168.1.255"), address);
        Assert.False(GatewayAllowList.Permits(allowed, "192.168.2.255", 9, out _));
        Assert.False(GatewayAllowList.Permits(allowed, null, 9, out _));
        Assert.False(GatewayAllowList.Permits(allowed, "192.168.1.255", 0, out _));
        Assert.False(GatewayAllowList.Permits(allowed, "192.168.1.255", 65536, out _));
    }

    [Fact]
    public void Nothing_is_permitted_without_configuration()
    {
        Assert.Empty(GatewayAllowList.Parse(null));
        Assert.False(GatewayAllowList.Permits(GatewayAllowList.Parse(""), "192.168.1.255", 9, out _));
    }

    [Fact]
    public void Hostnames_and_ipv6_are_never_permitted()
    {
        var allowed = GatewayAllowList.Parse("my-pc.local,::1");

        Assert.False(GatewayAllowList.Permits(allowed, "my-pc.local", 9, out _));
        Assert.False(GatewayAllowList.Permits(allowed, "::1", 9, out _));
    }
}
