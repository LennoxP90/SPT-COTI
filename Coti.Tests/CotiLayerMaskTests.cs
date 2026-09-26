using System.Collections.Generic;
using Coti.Client;
using Xunit;

public class CotiLayerMaskTests
{
    [Fact]
    public void AnEmptySetFallsBackToTheFallbackLayer()
    {
        var mask = CotiLayerMask.FoldLayerMask(new List<int>(), 5);
        Assert.Equal(1 << 5, mask);
    }

    [Fact]
    public void ASingleLayerFoldsToThatLayerAlone()
    {
        var mask = CotiLayerMask.FoldLayerMask(new List<int> { 3 }, 0);
        Assert.Equal(1 << 3, mask);
    }

    [Fact]
    public void DuplicateLayersFoldTheSameAsOneCopy()
    {
        var withDuplicates = CotiLayerMask.FoldLayerMask(new List<int> { 3, 3, 3 }, 0);
        var withoutDuplicates = CotiLayerMask.FoldLayerMask(new List<int> { 3 }, 0);

        Assert.Equal(withoutDuplicates, withDuplicates);
    }

    [Fact]
    public void MultipleDistinctLayersUnionTogether()
    {
        var mask = CotiLayerMask.FoldLayerMask(new List<int> { 0, 3, 9 }, 0);
        Assert.Equal((1 << 0) | (1 << 3) | (1 << 9), mask);
    }

    [Fact]
    public void TheFallbackLayerIsIgnoredWhenTheSetIsNotEmpty()
    {
        // The fallback only applies to an empty set; a non-empty set is never unioned with a
        // layer nothing reported.
        var mask = CotiLayerMask.FoldLayerMask(new List<int> { 3 }, 9);
        Assert.Equal(1 << 3, mask);
    }
}
