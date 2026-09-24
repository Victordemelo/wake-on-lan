using Microsoft.Extensions.Configuration;
using RemoteWake.Api.Services;

namespace RemoteWake.Tests.Unit;

public sealed class WorkerKeysTests
{
    private static WorkerKeys Keys(string? master = "agent-master-key", string? gateway = "gateway-key",
        string? owner = " Owner@Example.com ") =>
        new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Agent:MasterKey"] = master,
            ["Gateway:Key"] = gateway,
            ["Gateway:OwnerEmail"] = owner
        }).Build());

    [Fact]
    public void Agent_key_is_stable_and_unique_per_machine()
    {
        var keys = Keys();
        var machine = Guid.NewGuid();

        Assert.Equal(keys.AgentKey(machine), keys.AgentKey(machine));
        Assert.NotEqual(keys.AgentKey(machine), keys.AgentKey(Guid.NewGuid()));
        Assert.Matches("^[0-9A-F]{64}$", keys.AgentKey(machine)!);
    }

    [Fact]
    public void Revocation_bumps_the_version_and_invalidates_the_previous_key()
    {
        var keys = Keys();
        var machine = Guid.NewGuid();
        var original = keys.AgentKey(machine, 0)!;

        Assert.True(keys.IsAgent(machine, 0, original));
        Assert.False(keys.IsAgent(machine, 1, original));
        Assert.True(keys.IsAgent(machine, 1, keys.AgentKey(machine, 1)));
    }

    [Fact]
    public void Keys_depend_on_the_master_key()
    {
        var machine = Guid.NewGuid();

        Assert.NotEqual(Keys("first").AgentKey(machine), Keys("second").AgentKey(machine));
        Assert.False(Keys("second").IsAgent(machine, 0, Keys("first").AgentKey(machine)));
    }

    [Fact]
    public void No_agent_is_accepted_without_a_master_key()
    {
        var keys = Keys(master: null);
        var machine = Guid.NewGuid();

        Assert.Null(keys.AgentKey(machine));
        Assert.False(keys.IsAgent(machine, 0, "anything"));
    }

    [Fact]
    public void Gateway_key_must_match_exactly()
    {
        var keys = Keys();

        Assert.True(keys.IsGateway("gateway-key"));
        Assert.False(keys.IsGateway("gateway-key "));
        Assert.False(keys.IsGateway(null));
        Assert.False(Keys(gateway: "").IsGateway(""));
        Assert.Equal("owner@example.com", keys.GatewayOwnerEmail);
    }
}
