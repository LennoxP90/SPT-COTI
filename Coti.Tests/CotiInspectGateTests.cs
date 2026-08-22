using Coti.Client;
using Xunit;

// Three states, all pinned: absent for a non-host, disabled for an empty slot, enabled for a
// host carrying a COTI.
public class CotiInspectGateTests
{
    [Theory]
    [InlineData( false, false )]
    [InlineData( false, true )]
    public void ANonHostNeverGetsAButton( bool isKnownHost, bool cotiSlotFilled )
    {
        Assert.Equal( CotiInspectGate.NoButton, CotiInspectGateResolver.Resolve( isKnownHost, cotiSlotFilled ) );
    }

    [Fact]
    public void AHostWithAnEmptySlotGetsADisabledButton()
    {
        Assert.Equal( CotiInspectGate.Disabled, CotiInspectGateResolver.Resolve( isKnownHost: true, cotiSlotFilled: false ) );
    }

    [Fact]
    public void AHostWithAFilledSlotGetsAnEnabledButton()
    {
        Assert.Equal( CotiInspectGate.Enabled, CotiInspectGateResolver.Resolve( isKnownHost: true, cotiSlotFilled: true ) );
    }

    [Fact]
    public void HasFilledSlotIsFalseWhenTheSlotIsAbsentEntirely()
    {
        var slots = new[] { new CotiSlotSnapshot( "mod_nvg", true ) };
        Assert.False( CotiInspectGateResolver.HasFilledSlot( slots, "mod_coti" ) );
    }

    [Fact]
    public void HasFilledSlotIsFalseWhenNoSlotsExistAtAll()
    {
        Assert.False( CotiInspectGateResolver.HasFilledSlot( new CotiSlotSnapshot[0], "mod_coti" ) );
    }

    [Fact]
    public void HasFilledSlotIsFalseWhenThePresentSlotIsEmpty()
    {
        var slots = new[] { new CotiSlotSnapshot( "mod_coti", false ) };
        Assert.False( CotiInspectGateResolver.HasFilledSlot( slots, "mod_coti" ) );
    }

    [Fact]
    public void HasFilledSlotIsTrueOnlyForTheMatchingFilledSlot()
    {
        var slots = new[]
        {
            new CotiSlotSnapshot( "mod_nvg", true ),
            new CotiSlotSnapshot( "mod_coti", true ),
            new CotiSlotSnapshot( "mod_mount", false ),
        };

        Assert.True( CotiInspectGateResolver.HasFilledSlot( slots, "mod_coti" ) );
    }
}
