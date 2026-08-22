using System.Collections.Generic;
using System.Linq;
using Coti.Shared;
using Xunit;
using ClientDto = Coti.Client.CotiDeviceDto;
using ClientTable = Coti.Client.CotiHostTableDto;
using ClientPublishResult = Coti.Client.CotiPublishResultDto;
using ServerDto = Coti.Server.CotiDeviceDto;
using ServerTable = Coti.Server.CotiHostTableDto;
using ServerPublishResult = Coti.Server.CotiPublishResultDto;

/// <summary>
/// The server serialises with System.Text.Json, which is CASE-SENSITIVE with no naming
/// policy; the client deserialises with Newtonsoft, which is case-insensitive. So a rename on
/// the server binds the client's property to its default and throws nothing at all. This test
/// is the only thing that catches it.
/// </summary>
public class CotiWireContractTests
{
    private static CotiDeviceFile Sample()
    {
        var d = new CotiDeviceFile
        {
            Schema = 1, Device = "argus_chimera", DisplayName = "Argus Chimera Panoramic Bridge",
            Requires = "com.c11.truenorth4", Tuned = true,
            Mask = new CotiMaskBlock { CenterX = 0.5f, CenterY = 0.51f, Radius = 0.28f, Feather = 0.012f },
            Mount = new CotiMountBlock
            {
                AnchorBone = "axis",
                PositionX = 0.027f, PositionY = -0.037f, PositionZ = -0.075f,
                RotationX = -90f, RotationY = 1f, RotationZ = 2f,
                RollDegrees = -26f, PitchDegrees = 90f, YawDegrees = 2f, Scale = 1.46f,
            },
        };
        d.Hosts.Add(new CotiHostRef { Id = "69e29e097259deabbcff1884", Prefab = "chimera.bundle", Label = "Tan" });
        return d;
    }

    [Fact]
    public void ServerJsonDeserialisesIntoTheClientDtoWithNothingLost()
    {
        var table = new ServerTable { Devices = { ServerDto.FromShared(Sample()) } };
        var json = System.Text.Json.JsonSerializer.Serialize(table);

        var back = Newtonsoft.Json.JsonConvert.DeserializeObject<ClientTable>(json)!;
        var got = back.Devices.Single().ToShared();
        var want = Sample();

        Assert.Equal(want.Schema, got.Schema);
        Assert.Equal(want.Device, got.Device);
        Assert.Equal(want.DisplayName, got.DisplayName);
        Assert.Equal(want.Requires, got.Requires);
        Assert.Equal(want.Tuned, got.Tuned);
        Assert.Equal(want.Hosts.Single().Id, got.Hosts.Single().Id);
        Assert.Equal(want.Hosts.Single().Prefab, got.Hosts.Single().Prefab);
        Assert.Equal(want.Hosts.Single().Label, got.Hosts.Single().Label);
        Assert.Equal(want.Mask.CenterX, got.Mask.CenterX);
        Assert.Equal(want.Mask.CenterY, got.Mask.CenterY);
        Assert.Equal(want.Mask.Radius, got.Mask.Radius);
        Assert.Equal(want.Mask.Feather, got.Mask.Feather);
        Assert.Equal(want.Mount.AnchorBone, got.Mount.AnchorBone);
        Assert.Equal(want.Mount.PositionX, got.Mount.PositionX);
        Assert.Equal(want.Mount.PositionY, got.Mount.PositionY);
        Assert.Equal(want.Mount.PositionZ, got.Mount.PositionZ);
        Assert.Equal(want.Mount.RotationX, got.Mount.RotationX);
        Assert.Equal(want.Mount.RotationY, got.Mount.RotationY);
        Assert.Equal(want.Mount.RotationZ, got.Mount.RotationZ);
        Assert.Equal(want.Mount.RollDegrees, got.Mount.RollDegrees);
        Assert.Equal(want.Mount.PitchDegrees, got.Mount.PitchDegrees);
        Assert.Equal(want.Mount.YawDegrees, got.Mount.YawDegrees);
        Assert.Equal(want.Mount.Scale, got.Mount.Scale);
    }

    /// <summary>
    /// Round-trips the whole publish result - not just its device field - server-writes to
    /// client-reads. Every field carries a non-default value, so a property left bound to its
    /// default is a visible failure rather than a silent one.
    /// </summary>
    [Fact]
    public void ServerPublishResultDeserialisesIntoTheClientDtoWithNothingLost()
    {
        var want = new ServerPublishResult
        {
            Ok = true,
            Error = "unexpected token at line 4",
            Device = ServerDto.FromShared(Sample()),
            UnfitHosts = new List<string> { "111: InvalidId", "222: NoSlotsCollection" },
        };

        var json = System.Text.Json.JsonSerializer.Serialize(want);
        var got = Newtonsoft.Json.JsonConvert.DeserializeObject<ClientPublishResult>(json)!;

        Assert.Equal(want.Ok, got.Ok);
        Assert.Equal(want.Error, got.Error);
        Assert.Equal(want.UnfitHosts, got.UnfitHosts);

        Assert.NotNull(got.Device);
        var gotDevice = got.Device!.ToShared();
        var wantDevice = Sample();
        Assert.Equal(wantDevice.Device, gotDevice.Device);
        Assert.Equal(wantDevice.DisplayName, gotDevice.DisplayName);
        Assert.Equal(wantDevice.Mount.Scale, gotDevice.Mount.Scale);
        Assert.Equal(wantDevice.Hosts.Single().Id, gotDevice.Hosts.Single().Id);
    }

    [Fact]
    public void ClientJsonDeserialisesIntoTheServerDto()
    {
        // The publish direction. A name that only works one way is still a broken contract.
        var json = Newtonsoft.Json.JsonConvert.SerializeObject(ClientDto.FromShared(Sample()));

        var got = System.Text.Json.JsonSerializer.Deserialize<ServerDto>(json)!.ToShared();

        Assert.Equal("axis", got.Mount.AnchorBone);
        Assert.Equal(1.46f, got.Mount.Scale);
        Assert.Equal("chimera.bundle", got.Hosts.Single().Prefab);
    }

    /// <summary>
    /// POST /coti/hosts/publish calls ToShared() straight off the request, before any validation,
    /// and that route is deliberately ungated - so an explicit null in any of the three object
    /// members, or a null entry inside hosts, must come back as a rejection rather than an
    /// exception out of the route. Both serialisers assign null over a "= new()" initialiser, so
    /// both halves of the contract are pinned here.
    /// </summary>
    [Theory]
    [InlineData("""{ "schema": 1, "device": "x", "displayName": "X", "hosts": null, "mask": null, "mount": null }""")]
    [InlineData("""{ "schema": 1, "device": "x", "displayName": "X", "hosts": [null] }""")]
    [InlineData("""{ "schema": 1, "device": "x", "displayName": "X", "hosts": [null, { "id": "111" }] }""")]
    public void AnExplicitNullBlockConvertsInsteadOfThrowing(string json)
    {
        var fromServerDto = System.Text.Json.JsonSerializer.Deserialize<ServerDto>(json)!.ToShared();
        var fromClientDto = Newtonsoft.Json.JsonConvert.DeserializeObject<ClientDto>(json)!.ToShared();

        foreach (var got in new[] { fromServerDto, fromClientDto })
        {
            Assert.NotNull(got.Mask);
            Assert.NotNull(got.Mount);
            Assert.NotNull(got.Hosts);
            Assert.All(got.Hosts, host => Assert.NotNull(host));
        }
    }

    /// <summary>
    /// The substituted defaults above are not silently accepted either: a zero mask radius
    /// generates no mask at all and a device with no hosts can mount nothing, so the shared merge
    /// rules reject both - which is what turns a would-be 500 into a named rejection.
    /// </summary>
    [Fact]
    public void ADeviceWithNullBlocksIsRejectedByTheSharedMergeRules()
    {
        const string json = """{ "schema": 1, "device": "x", "displayName": "X", "hosts": null, "mask": null, "mount": null }""";
        var device = System.Text.Json.JsonSerializer.Deserialize<ServerDto>(json)!.ToShared();

        var merged = CotiDeviceMerge.Merge(
            new[] { new CotiParsedFile { Path = "<published>", Device = device } });

        Assert.Empty(merged.Devices);
        Assert.NotEmpty(merged.Warnings);
    }

    [Fact]
    public void EveryWireNameIsLowerCamelCase()
    {
        // Hand-authored addon files are read by the SERVER dto, so its names are the public
        // file format. PascalCase would leak C# convention into a format humans write.
        var json = System.Text.Json.JsonSerializer.Serialize(ServerDto.FromShared(Sample()));

        foreach (var name in new[] { "schema", "device", "displayName", "requires", "tuned",
                                     "hosts", "mask", "mount", "anchorBone", "positionX", "centerX" })
            Assert.Contains("\"" + name + "\"", json);
    }

    [Fact]
    public void AHandAuthoredDeviceFileParsesWithEveryFieldPopulated()
    {
        // Device files are hand-written by addon authors, so the server DTO's names ARE the
        // public file format. Round-tripping objects pins the two DTOs against each other but
        // never proves the documented spelling parses from text - and `requires` has no shipped
        // file to check against, because shipped devices deliberately omit it.
        const string json = """
        {
          "schema": 1,
          "device": "argus_chimera",
          "displayName": "Argus Chimera Panoramic Bridge",
          "requires": "com.c11.truenorth4",
          "tuned": true,
          "hosts": [
            { "id": "69e29e097259deabbcff1884", "prefab": "chimera.bundle", "label": "Tan" }
          ],
          "mask": { "centerX": 0.5, "centerY": 0.51, "radius": 0.28, "feather": 0.012 },
          "mount": {
            "anchorBone": "axis",
            "positionX": 0.027, "positionY": -0.037, "positionZ": -0.075,
            "rotationX": -90.0, "rotationY": 1.0, "rotationZ": 2.0,
            "rollDegrees": -26.0, "pitchDegrees": 90.0, "yawDegrees": 2.0,
            "scale": 1.46
          }
        }
        """;

        var got = System.Text.Json.JsonSerializer.Deserialize<ServerDto>(json)!.ToShared();

        Assert.Equal(1, got.Schema);
        Assert.Equal("argus_chimera", got.Device);
        Assert.Equal("Argus Chimera Panoramic Bridge", got.DisplayName);
        Assert.Equal("com.c11.truenorth4", got.Requires);
        Assert.True(got.Tuned);

        var host = got.Hosts.Single();
        Assert.Equal("69e29e097259deabbcff1884", host.Id);
        Assert.Equal("chimera.bundle", host.Prefab);
        Assert.Equal("Tan", host.Label);

        Assert.Equal(0.5f, got.Mask.CenterX);
        Assert.Equal(0.51f, got.Mask.CenterY);
        Assert.Equal(0.28f, got.Mask.Radius);
        Assert.Equal(0.012f, got.Mask.Feather);

        Assert.Equal("axis", got.Mount.AnchorBone);
        Assert.Equal(0.027f, got.Mount.PositionX);
        Assert.Equal(-0.037f, got.Mount.PositionY);
        Assert.Equal(-0.075f, got.Mount.PositionZ);
        Assert.Equal(-90f, got.Mount.RotationX);
        Assert.Equal(1f, got.Mount.RotationY);
        Assert.Equal(2f, got.Mount.RotationZ);
        Assert.Equal(-26f, got.Mount.RollDegrees);
        Assert.Equal(90f, got.Mount.PitchDegrees);
        Assert.Equal(2f, got.Mount.YawDegrees);
        Assert.Equal(1.46f, got.Mount.Scale);
    }
}
