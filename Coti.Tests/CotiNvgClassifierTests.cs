using System.Collections.Generic;
using System.Linq;
using Coti.Shared;
using Xunit;

public class CotiNvgClassifierTests
{
    private sealed class FakeItems : ICotiItemView
    {
        public readonly Dictionary<string, string> Parents = new();
        public bool Exists(string id) => Parents.ContainsKey(id);
        public string? PrefabPath(string id) => null;
        public string? ParentOf(string id) => Parents.TryGetValue(id, out var p) ? p : null;
        public IEnumerable<string> AllIds() => Parents.Keys;
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
