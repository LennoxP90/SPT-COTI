using System.Collections.Generic;
using System.Linq;
using Coti.Shared;
using Xunit;

public class CotiNvgClassifierTests
{
    private sealed class FakeItems : ICotiItemView
    {
        public readonly Dictionary<string, string> Parents = new();
        public int ParentReads;
        public bool Exists(string id) => Parents.ContainsKey(id);
        public string? PrefabPath(string id) => null;
        public string? ParentOf(string id) { ParentReads++; return Parents.TryGetValue(id, out var p) ? p : null; }
        public IEnumerable<string> AllIds() => Parents.Keys;
    }

    private static FakeItems MixedTable()
    {
        var items = new FakeItems();
        items.Parents[CotiNvgClassifier.NightVisionNodeId] = "itemroot";
        items.Parents["pvs14"] = CotiNvgClassifier.NightVisionNodeId;
        items.Parents["moddednode"] = CotiNvgClassifier.NightVisionNodeId;
        items.Parents["chimera"] = "moddednode";
        items.Parents["chimera2"] = "moddednode";
        items.Parents["t7"] = "thermalnode";
        items.Parents["thermalnode"] = "itemroot";
        items.Parents["a"] = "b";
        items.Parents["b"] = "a";
        items.Parents["tail"] = "a";
        items.Parents["self"] = "self";
        return items;
    }

    private static readonly Dictionary<string, bool> Expected = new()
    {
        [CotiNvgClassifier.NightVisionNodeId] = false,
        ["pvs14"] = true, ["moddednode"] = true, ["chimera"] = true, ["chimera2"] = true,
        ["t7"] = false, ["thermalnode"] = false, ["itemroot"] = false,
        ["a"] = false, ["b"] = false, ["tail"] = false, ["self"] = false,
        ["missing"] = false, [""] = false,
    };

    [Fact]
    public void ASharedMemoMatchesTheTruthTableInEitherOrder()
    {
        var ids = Expected.Keys.ToList();

        foreach (var order in new[] { ids, Enumerable.Reverse(ids).ToList() })
        {
            var items = MixedTable();
            var known = new Dictionary<string, bool>();

            foreach (var id in order)
            {
                Assert.Equal(Expected[id], CotiNvgClassifier.IsNightVision(items, id, known));
                Assert.Equal(Expected[id], CotiNvgClassifier.IsNightVision(items, id));
            }
        }
    }

    [Fact]
    public void ChildrenOfTheNodeAreTrueAfterTheNodeItselfIsMemoisedFalse()
    {
        var items = MixedTable();
        var known = new Dictionary<string, bool>();

        Assert.False(CotiNvgClassifier.IsNightVision(items, CotiNvgClassifier.NightVisionNodeId, known));
        Assert.True(CotiNvgClassifier.IsNightVision(items, "pvs14", known));
        Assert.True(CotiNvgClassifier.IsNightVision(items, "chimera", known));
    }

    [Fact]
    public void AnInteriorNodeIsReadOncePerMemo()
    {
        var items = MixedTable();
        var known = new Dictionary<string, bool>();

        Assert.True(CotiNvgClassifier.IsNightVision(items, "chimera", known));
        items.ParentReads = 0;

        Assert.True(CotiNvgClassifier.IsNightVision(items, "chimera2", known));
        Assert.Equal(1, items.ParentReads);
    }

    [Fact]
    public void ADirectChildOfTheNightVisionNodeIsAnNvg()
    {
        var items = new FakeItems();
        items.Parents["pvs14"] = CotiNvgClassifier.NightVisionNodeId;

        Assert.True(CotiNvgClassifier.IsNightVision(items, "pvs14"));
    }

    [Fact]
    public void AnInterposedSubNodeIsStillAnNvg()
    {
        // A chain walk, not equality: a mod that inserts its own node under NightVision would
        // otherwise be invisible, and its devices would silently never get a slot.
        var items = new FakeItems();
        items.Parents["moddednode"] = CotiNvgClassifier.NightVisionNodeId;
        items.Parents["chimera"] = "moddednode";

        Assert.True(CotiNvgClassifier.IsNightVision(items, "chimera"));
    }

    [Fact]
    public void AThermalIsNotAnNvg()
    {
        var items = new FakeItems();
        items.Parents["t7"] = "somethermalnode";
        items.Parents["somethermalnode"] = "55818aeb4bdc2ddc698b456a";

        Assert.False(CotiNvgClassifier.IsNightVision(items, "t7"));
    }

    [Fact]
    public void ACycleTerminatesRatherThanHanging()
    {
        // Hand-edited or mod-generated data can be circular, and a load-time hang is
        // indistinguishable from a crashed server.
        var items = new FakeItems();
        items.Parents["a"] = "b";
        items.Parents["b"] = "a";

        Assert.False(CotiNvgClassifier.IsNightVision(items, "a"));
    }
}
