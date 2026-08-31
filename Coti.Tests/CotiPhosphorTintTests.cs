using Coti.Shared;
using Xunit;

public class CotiPhosphorTintTests
{
    // Borkel 3.0's RealisticNvgSettings defaults - the colour COTI must now be reading, and the
    // one it was NOT reading when the report came in.
    private const float BorkelRed = 0.62f;
    private const float BorkelGreen = 0.92f;
    private const float BorkelBlue = 0.98f;

    [Fact]
    public void ADimTubeAndABrightOneOfTheSameHueGiveTheSameTint()
    {
        // The whole reason brightness is divided out: an already-dim phosphor must not also make
        // the heat dimmer, because the overlay's intensity setting owns that.
        CotiPhosphorTint.TryHue(0.31f, 0.46f, 0.49f, out var dimR, out var dimG, out var dimB);
        CotiPhosphorTint.TryHue(BorkelRed, BorkelGreen, BorkelBlue, out var r, out var g, out var b);

        Assert.Equal(r, dimR, 5);
        Assert.Equal(g, dimG, 5);
        Assert.Equal(b, dimB, 5);
    }

    [Fact]
    public void TheBrightestChannelReachesFull()
    {
        CotiPhosphorTint.TryHue(BorkelRed, BorkelGreen, BorkelBlue, out var r, out var g, out var b);

        Assert.Equal(1f, b, 5);
        Assert.True(r < 1f);
        Assert.True(g < 1f);
    }

    [Fact]
    public void AnUnsetColourIsRejectedRatherThanSubstituted()
    {
        // What NightVision.Color can now hold with Borkel 3.0 installed: nothing writes it, so
        // black is a real possibility and must leave the shader's warm-white defaults in place.
        Assert.False(CotiPhosphorTint.TryHue(0f, 0f, 0f, out _, out _, out _));
    }

    [Fact]
    public void AColourSpreadTooThinToCarryAHueIsRejected()
    {
        // Sum clears MinimumBrightness, every channel individually does not. Dividing by that peak
        // would push the hue far past 1 and blow the overlay out.
        Assert.False(CotiPhosphorTint.TryHue(0.005f, 0.005f, 0.005f, out _, out _, out _));
    }

    [Fact]
    public void HotIsBrighterThanTheHueInEveryChannel()
    {
        CotiPhosphorTint.TryHue(BorkelRed, BorkelGreen, BorkelBlue, out var r, out var g, out var b);
        CotiPhosphorTint.Hot(r, g, b, out var hotR, out var hotG, out var hotB);

        Assert.True(hotR > r);
        Assert.True(hotG > g);
        Assert.True(hotB >= b);
    }

    [Fact]
    public void HotStaysWithinRange()
    {
        CotiPhosphorTint.TryHue(BorkelRed, BorkelGreen, BorkelBlue, out var r, out var g, out var b);
        CotiPhosphorTint.Hot(r, g, b, out var hotR, out var hotG, out var hotB);

        Assert.InRange(hotR, 0f, 1f);
        Assert.InRange(hotG, 0f, 1f);
        Assert.InRange(hotB, 0f, 1f);
    }

    [Fact]
    public void APureHueIsLeftAtFullOnItsOwnChannel()
    {
        CotiPhosphorTint.TryHue(0f, 0.8f, 0f, out var r, out var g, out var b);

        Assert.Equal(0f, r, 5);
        Assert.Equal(1f, g, 5);
        Assert.Equal(0f, b, 5);
    }

    [Fact]
    public void WhiteHotOnAWhitePhosphorIsStillWhite()
    {
        CotiPhosphorTint.TryHue(1f, 1f, 1f, out var r, out var g, out var b);
        CotiPhosphorTint.Hot(r, g, b, out var hotR, out var hotG, out var hotB);

        Assert.Equal(1f, hotR, 5);
        Assert.Equal(1f, hotG, 5);
        Assert.Equal(1f, hotB, 5);
    }
}

public class CotiPhosphorFadeTests
{
    // The configured sum measured in raid on a PVS-31A class tube.
    private const float Configured = 1.596f;

    [Fact]
    public void ASettledTubeRidesAtFull()
    {
        Assert.Equal(1f, CotiPhosphorTint.Fade(1.596f, Configured), 3);
    }

    [Fact]
    public void TheNegativeSwingOfTheFlashIsClampedToZero()
    {
        // The real reason the clamp exists: EFT drives CurrentColor as 1 - 2 * value, so it swings
        // negative through the flash. -1.628 against 1.596 is a measured frame, not a made-up one.
        Assert.Equal(0f, CotiPhosphorTint.Fade(-1.628f, Configured), 5);
    }

    [Fact]
    public void MidFlashIsProportional()
    {
        // Another measured frame: current 0.720 of a configured 1.596.
        Assert.Equal(0.451f, CotiPhosphorTint.Fade(0.720f, Configured), 3);
    }

    [Fact]
    public void AnOvershootAboveTheConfiguredColourIsClampedToOne()
    {
        Assert.Equal(1f, CotiPhosphorTint.Fade(2.5f, Configured), 5);
    }

    [Fact]
    public void AnUnsetConfiguredColourLeavesTheOverlayVisible()
    {
        // Dividing by it would hide the overlay for the rest of the raid, which is strictly worse
        // than ignoring a fade nobody can see anyway.
        Assert.Equal(1f, CotiPhosphorTint.Fade(0f, 0f), 5);
    }
}
