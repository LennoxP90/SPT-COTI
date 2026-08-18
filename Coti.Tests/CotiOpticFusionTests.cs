using Coti.Client;
using Xunit;

public class CotiOpticFusionTests
{
    // Every field of view below was measured in a 4.1 raid with the COTI_DEV camera probe on
    // 2026-08-17, not invented. See docs/superpowers/specs/2026-08-17-optic-magnified-thermal-design.md
    private const float MainHipfire = 75.00f;
    private const float MainAiming = 35.00f;
    private const float MainEotech1x = 60.00f;
    private const float OpticMinZoom = 26.50f;   // variable scope, bottom stop
    private const float OpticMaxZoom = 4.42f;    // same scope, top stop
    private const float Optic3xMagnifier = 5.83f;
    private const float NoOpticCamera = 0f;      // what the caller passes when none exists

    [Theory]
    [InlineData(MainAiming, OpticMaxZoom, 7.92f)]
    [InlineData(MainAiming, Optic3xMagnifier, 6.00f)]
    [InlineData(MainAiming, OpticMinZoom, 1.32f)]
    public void MagnificationIsTheRatioOfTheTwoFieldsOfView(float main, float optic, float expected)
    {
        Assert.Equal(expected, CotiOpticFusion.Magnification(main, optic), precision: 2);
    }

    [Theory]
    [InlineData(MainAiming, NoOpticCamera)]      // no optic camera: the common case
    [InlineData(MainHipfire, NoOpticCamera)]
    [InlineData(0f, OpticMaxZoom)]               // main camera gone
    [InlineData(MainAiming, -1f)]                // camera mid-teardown
    public void MagnificationIsOneWhenTheInputsCannotProduceARatio(float main, float optic)
    {
        Assert.Equal(1f, CotiOpticFusion.Magnification(main, optic));
    }

    [Theory]
    [InlineData(MainAiming, OpticMaxZoom)]       // 7.9x
    [InlineData(MainAiming, Optic3xMagnifier)]   // 6.0x
    [InlineData(MainAiming, OpticMinZoom)]       // 1.32x, above the floor
    public void MagnifiesWhenAnOpticIsActuallyMagnifying(float main, float optic)
    {
        Assert.True(CotiOpticFusion.ShouldMagnify(configEnabled: true, cotiActive: true, main, optic));
    }

    [Theory]
    [InlineData(MainAiming, NoOpticCamera)]      // iron sights
    [InlineData(MainEotech1x, NoOpticCamera)]    // measured: a 1x EOTech creates no optic camera
    [InlineData(MainHipfire, NoOpticCamera)]
    [InlineData(MainAiming, MainAiming)]         // a 1x optic, were one to exist
    public void DoesNotMagnifyWithoutMagnification(float main, float optic)
    {
        Assert.False(CotiOpticFusion.ShouldMagnify(configEnabled: true, cotiActive: true, main, optic));
    }

    [Fact]
    public void TheSettingGatesEverything()
    {
        Assert.False(CotiOpticFusion.ShouldMagnify(
            configEnabled: false, cotiActive: true, MainAiming, OpticMaxZoom));
    }

    [Fact]
    public void AnInactiveCotiNeverMagnifies()
    {
        // The device being off has to win over a scope being up, or the second camera would render
        // for a device that is not switched on.
        Assert.False(CotiOpticFusion.ShouldMagnify(
            configEnabled: true, cotiActive: false, MainAiming, OpticMaxZoom));
    }

    // ---- the lens ellipse the overlay is kept out of ----
    //
    // Measured reference: a 2024x847 render, which is what EFT produced against a 3440x1440 screen.
    // The numbers below are in that camera's own pixels, never the screen's.
    private const float ViewWidth = 2024f;
    private const float ViewHeight = 847f;

    private static (float u, float v, float ru, float rv) Ellipse(
        float minX, float minY, float maxX, float maxY, float scale = 1f,
        float width = ViewWidth, float height = ViewHeight)
    {
        Assert.True(CotiOpticFusion.TryLensEllipse(
            minX, minY, maxX, maxY, width, height, scale,
            out var u, out var v, out var ru, out var rv));
        return (u, v, ru, rv);
    }

    private static bool Rejected(
        float minX, float minY, float maxX, float maxY, float scale = 1f,
        float width = ViewWidth, float height = ViewHeight)
    {
        return !CotiOpticFusion.TryLensEllipse(
            minX, minY, maxX, maxY, width, height, scale,
            out _, out _, out _, out _);
    }

    [Fact]
    public void ALensBoxBecomesACentredNormalisedEllipse()
    {
        // A 200x200 pixel box centred in the view: the centre normalises differently on each axis
        // because the view is not square, and so does the radius.
        var e = Ellipse(912f, 323.5f, 1112f, 523.5f);

        Assert.Equal(0.5f, e.u, precision: 4);
        Assert.Equal(0.5f, e.v, precision: 4);
        Assert.Equal(100f / ViewWidth, e.ru, precision: 5);
        Assert.Equal(100f / ViewHeight, e.rv, precision: 5);
    }

    [Fact]
    public void AnOffCentreLensKeepsItsOwnCentre()
    {
        var e = Ellipse(1400f, 500f, 1500f, 600f);

        Assert.Equal(1450f / ViewWidth, e.u, precision: 5);
        Assert.Equal(550f / ViewHeight, e.v, precision: 5);
    }

    [Fact]
    public void TheCoverScaleOnlyMovesTheRadius()
    {
        var full = Ellipse(912f, 323.5f, 1112f, 523.5f);
        var half = Ellipse(912f, 323.5f, 1112f, 523.5f, scale: 0.5f);

        Assert.Equal(full.u, half.u, precision: 5);
        Assert.Equal(full.v, half.v, precision: 5);
        Assert.Equal(full.ru * 0.5f, half.ru, precision: 5);
        Assert.Equal(full.rv * 0.5f, half.rv, precision: 5);
    }

    [Fact]
    public void ZeroCoverIsRejectedRatherThanSentAsAnEmptyEllipse()
    {
        // Zero is a legitimate setting - it means "leave the 1x heat on the lens" - and it must come
        // back as "no lens" rather than as a degenerate shape for the shader to interpret.
        Assert.True(Rejected(912f, 323.5f, 1112f, 523.5f, scale: 0f));
    }

    [Theory]
    // Empty on one axis, inverted, and both: a projection can produce any of these.
    [InlineData(500f, 300f, 500f, 400f)]
    [InlineData(500f, 300f, 600f, 300f)]
    [InlineData(600f, 400f, 500f, 300f)]
    public void ADegenerateBoxIsNotALens(float minX, float minY, float maxX, float maxY)
    {
        Assert.True(Rejected(minX, minY, maxX, maxY));
    }

    [Fact]
    public void AViewWithNoAreaIsNotMeasurableAgainst()
    {
        Assert.True(Rejected(912f, 323.5f, 1112f, 523.5f, width: 0f));
        Assert.True(Rejected(912f, 323.5f, 1112f, 523.5f, height: 0f));
    }

    [Fact]
    public void ARunawayBoxIsRejectedOnEitherAxis()
    {
        // The guard that matters: this box is used to DELETE overlay, so a projection that has blown
        // up must switch the exclusion off rather than switch the thermal off across the screen.
        var wide = ViewWidth * CotiOpticFusion.MaximumLensExtent + 2f;
        Assert.True(Rejected(0f, 300f, wide, 400f));

        var tall = ViewHeight * CotiOpticFusion.MaximumLensExtent + 2f;
        Assert.True(Rejected(500f, 0f, 600f, tall));
    }

    [Fact]
    public void ABoxAtTheLimitIsStillALens()
    {
        // The bound is inclusive, so the largest credible lens is accepted rather than sitting one
        // float either side of a cliff.
        var wide = ViewWidth * CotiOpticFusion.MaximumLensExtent;
        var e = Ellipse(0f, 300f, wide, 400f);

        Assert.Equal(CotiOpticFusion.MaximumLensExtent * 0.5f, e.ru, precision: 5);
    }

    [Fact]
    public void TheCoverDialCanExceedTheCredibilityBound()
    {
        // Deliberate: the bound guards the MEASUREMENT, and the scale is the player's own dial. A
        // cover of 2 on a large lens is a choice, not a bad projection.
        var wide = ViewWidth * CotiOpticFusion.MaximumLensExtent;
        var e = Ellipse(0f, 300f, wide, 400f, scale: 2f);

        Assert.Equal(CotiOpticFusion.MaximumLensExtent, e.ru, precision: 5);
    }

    // ---- outline width against sensor resolution ----

    [Fact]
    public void TheReferenceResolutionChangesNothing()
    {
        // The whole point: anyone who never touches the resolution must see no difference at all.
        Assert.Equal(1.5f, CotiOverlayScale.OutlineWidth(1.5f, CotiOverlayScale.ReferenceRows));
    }

    [Theory]
    [InlineData(1152, 3.0f)]   // twice the rows, twice the texels, twice the width
    [InlineData(1536, 4.0f)]
    // Scaling down is covered by ScalingDownStillAppliesWhereThereIsRoom instead: at the default
    // 1.5 the sub-texel floor takes over below 576, so this case cannot also test the ratio.
    public void WidthTracksTheTexelCount(int rows, float expected)
    {
        Assert.Equal(expected, CotiOverlayScale.OutlineWidth(1.5f, rows), precision: 4);
    }

    [Theory]
    [InlineData(650f)]   // the mask band
    [InlineData(750f)]   // the raw thermal band
    [InlineData(950f)]   // does the pass run at all
    public void DiagnosticBandsAreNeverScaled(float band)
    {
        // These are not widths. Scaling one lands on a different band, or on none, and the
        // diagnostic then quietly answers a question nobody asked.
        Assert.Equal(band, CotiOverlayScale.OutlineWidth(band, 1536));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void AnImpossibleRowCountLeavesTheWidthAlone(int rows)
    {
        // A camera mid-teardown can report anything. Refusing to scale is always safer than
        // scaling by a number that came out of one.
        Assert.Equal(1.5f, CotiOverlayScale.OutlineWidth(1.5f, rows));
    }

    [Theory]
    [InlineData(288)]   // scale 0.5 would give 0.75 texels
    [InlineData(144)]   // scale 0.25 would give 0.375
    public void ScalingDownNeverProducesASubTexelRim(int rows)
    {
        // Below a texel the erosion taps land back inside the pixel they came from, inner converges
        // on solid, and contour mode renders nothing. The low resolutions stand in for distance, so
        // they must not also switch the contours off and wreck the comparison they exist for.
        Assert.Equal(CotiOverlayScale.MinimumTexels, CotiOverlayScale.OutlineWidth(1.5f, rows));
    }

    [Fact]
    public void ScalingDownStillAppliesWhereThereIsRoom()
    {
        // The clamp is a floor, not a replacement for the scaling.
        Assert.Equal(2.0f, CotiOverlayScale.OutlineWidth(4.0f, 288), precision: 4);
    }

    // ---- the sensor resolution table ----

    [Theory]
    [InlineData(288, 384)]
    [InlineData(576, 768)]
    [InlineData(1152, 1536)]
    [InlineData(1536, 288)]   // wraps, so one key can reach every value
    public void ResolutionStepsUpAndWraps(int current, int expected)
    {
        Assert.Equal(expected, CotiSensorResolutions.Next(current));
    }

    [Theory]
    [InlineData(1, 288)]      // below the list
    [InlineData(600, 768)]    // between two entries
    [InlineData(99999, 288)]  // above it
    public void AnOffListResolutionSnapsBackOntoTheList(int current, int expected)
    {
        // Config can hold anything. A dev key that does nothing because the current value is
        // unexpected is worse than one that gets you back onto the list.
        Assert.Equal(expected, CotiSensorResolutions.Next(current));
    }

    [Fact]
    public void WidthKeepsTheSensorsFourThreeRatio()
    {
        Assert.Equal(768, CotiSensorResolutions.WidthFor(576));
        Assert.Equal(1536, CotiSensorResolutions.WidthFor(1152));
    }

    [Fact]
    public void TheSubTexelFloorAppliesAtTheReferenceResolutionToo()
    {
        // It used to sit behind an early return for rows == ReferenceRows, so a host configured at
        // or below half a texel handed the shader half a texel on the DEFAULT 576 - the one row
        // count where the guard was skipped, and the one everybody runs. Contour mode then renders
        // nothing, which is exactly what MinimumTexels exists to prevent.
        Assert.Equal(CotiOverlayScale.MinimumTexels,
            CotiOverlayScale.OutlineWidth(0.5f, CotiOverlayScale.ReferenceRows));
    }
}

