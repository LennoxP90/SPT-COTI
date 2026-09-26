using System.Collections.Generic;
using Coti.Client;
using Xunit;

public class CotiAnchorAdvisorTests
{
    [Fact]
    public void NoRotatorSuggestsNothing()
    {
        Assert.Null(CotiAnchorAdvisor.SuggestAnchorBone(false, false, null));
    }

    [Fact]
    public void NoRotatorSuggestsNothingEvenIfANameIsSupplied()
    {
        // Defensive: a caller should never pass a name without rotatorPresent, but the function
        // must not fabricate a suggestion out of stale data if it happens anyway.
        Assert.Null(CotiAnchorAdvisor.SuggestAnchorBone(false, false, "mod_nvg"));
    }

    [Fact]
    public void RotatorOnTheRootSuggestsTheRootAsEmptyString()
    {
        // Empty string is the MountAnchorBone convention for "host root", a third state distinct
        // from "no suggestion" (null) and from a bone name.
        var suggestion = CotiAnchorAdvisor.SuggestAnchorBone(true, true, "nvg_pvs_14(Clone)");
        Assert.Equal(string.Empty, suggestion);
    }

    [Fact]
    public void RotatorOnANamedChildSuggestsThatChildsName()
    {
        var suggestion = CotiAnchorAdvisor.SuggestAnchorBone(true, false, "axis");
        Assert.Equal("axis", suggestion);
    }

    [Fact]
    public void CyclingForwardAdvancesByOne()
    {
        Assert.Equal(1, CotiAnchorAdvisor.NextCandidateIndex(0, 3, 1));
    }

    [Fact]
    public void CyclingForwardFromTheLastEntryWrapsToTheFirst()
    {
        Assert.Equal(0, CotiAnchorAdvisor.NextCandidateIndex(2, 3, 1));
    }

    [Fact]
    public void CyclingBackwardFromTheFirstEntryWrapsToTheLast()
    {
        Assert.Equal(2, CotiAnchorAdvisor.NextCandidateIndex(0, 3, -1));
    }

    [Fact]
    public void AnUnmatchedStartingIndexStillLandsOnARealEntry()
    {
        // -1 is what List<T>.IndexOf returns when the current anchor name is not among the
        // candidates at all (a hand-edited value, or a bone the depth-limited scan never
        // reached). Cycling from there must not throw and must not stand still.
        var index = CotiAnchorAdvisor.NextCandidateIndex(-1, 3, 1);
        Assert.InRange(index, 0, 2);
    }

    [Fact]
    public void EmptyCandidateListNeverThrows()
    {
        Assert.Equal(0, CotiAnchorAdvisor.NextCandidateIndex(0, 0, 1));
    }

    [Fact]
    public void ASuggestionDeeperThanTheDepthLimitedScanIsStillAddedAsACandidate()
    {
        // ResolveSuggestedBone's GetComponentInChildren search has no depth limit, but
        // CollectNameList caps the cycle candidates at three levels. A rotator transform past
        // that cap must still be reachable by cycling, not only by "Use", so the suggestion and
        // the cycle list agree about what is selectable.
        var candidates = new List<string> { "", "mod_nvg", "mod_mount" };
        var result = CotiAnchorAdvisor.EnsureSuggestedIsCandidate(candidates, "axis_deep");

        Assert.Contains("axis_deep", result);
    }

    [Fact]
    public void ASuggestionAlreadyPresentIsNotDuplicated()
    {
        var candidates = new List<string> { "", "axis" };
        var result = CotiAnchorAdvisor.EnsureSuggestedIsCandidate(candidates, "axis");

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void ASuggestionMatchingByCaseAloneIsNotDuplicated()
    {
        // Must agree with CycleAnchorBone's own OrdinalIgnoreCase lookup and with
        // EftCompat.FindTransformRecursive's ignoreCase match at mount time. A case-sensitive
        // check here would add a spurious entry for a bone that, at mount time, is the one
        // already listed.
        var candidates = new List<string> { "", "Axis" };
        var result = CotiAnchorAdvisor.EnsureSuggestedIsCandidate(candidates, "axis");

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void NoSuggestionLeavesTheListUnchanged()
    {
        var candidates = new List<string> { "", "mod_nvg" };
        var result = CotiAnchorAdvisor.EnsureSuggestedIsCandidate(candidates, null);

        Assert.Equal(candidates, result);
    }

    [Fact]
    public void ARootSuggestionIsNotDuplicatedAgainstTheAlreadyPresentRootEntry()
    {
        var candidates = new List<string> { "", "mod_nvg" };
        var result = CotiAnchorAdvisor.EnsureSuggestedIsCandidate(candidates, string.Empty);

        Assert.Equal(2, result.Count);
    }
}
