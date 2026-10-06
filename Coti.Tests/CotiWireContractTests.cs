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
/// The server serialises with System.Text.Json, which is case-sensitive with no naming policy;
/// the client deserialises with Newtonsoft, which is case-insensitive. A rename on the server
/// binds the client's property to its default and throws nothing, so these tests pin the names.
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
    /// Round-trips the whole publish result, including its device field, from server write to
    /// client read. Every field carries a non-default value, so a property left bound to its
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
        // The publish direction: names must bind both ways.
        var json = Newtonsoft.Json.JsonConvert.SerializeObject(ClientDto.FromShared(Sample()));

        var got = System.Text.Json.JsonSerializer.Deserialize<ServerDto>(json)!.ToShared();

        Assert.Equal("axis", got.Mount.AnchorBone);
        Assert.Equal(1.46f, got.Mount.Scale);
        Assert.Equal("chimera.bundle", got.Hosts.Single().Prefab);
    }

    /// <summary>
    /// POST /coti/hosts/publish calls ToShared() straight off the request, before any validation,
    /// and that route is ungated, so an explicit null in any of the three object members, or a
    /// null entry inside hosts, has to come back as a rejection rather than an exception. Both serialisers assign null over a "= new()" initialiser, so
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
    /// The substituted defaults above are then rejected: a zero mask radius generates no mask and
    /// a device with no hosts can mount nothing, so the shared merge rules return a named
    /// rejection rather than a 500.
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
        // Hand-authored addon files are read by the server dto, so its names are the public
        // file format, which uses lower camelCase.
        var json = System.Text.Json.JsonSerializer.Serialize(ServerDto.FromShared(Sample()));

        foreach (var name in new[] { "schema", "device", "displayName", "requires", "tuned",
                                     "hosts", "mask", "mount", "anchorBone", "positionX", "centerX" })
            Assert.Contains("\"" + name + "\"", json);
    }

    [Fact]
    public void AHandAuthoredDeviceFileParsesWithEveryFieldPopulated()
    {
        // Device files are hand-written by addon authors, so the server DTO's names are the
        // public file format. Round-tripping objects pins the two DTOs against each other but
        // does not prove the documented spelling parses from text, and shipped devices omit
        // `requires`, so no shipped file covers it.
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

    private static CotiDeviceFile MultiTube()
    {
        var d = Sample();
        d.Layout = "quad";
        d.Tubes = new Dictionary<string, CotiTube>
        {
            ["tube_1"] = new CotiTube
            {
                Mount = new CotiMountBlock
                {
                    AnchorBone = "axis_3",
                    PositionX = -0.007f, PositionY = -0.0435f, PositionZ = -0.0525f,
                    RotationX = 1f, RotationY = 2f, RotationZ = 3f,
                    RollDegrees = 4f, PitchDegrees = 5f, YawDegrees = -28f, Scale = 1.518f,
                },
                Pod = new CotiPodBlock { Bone = "axis_3", DownX = -33f, DownY = 1.5f, DownZ = -2.5f },
                Text = new CotiTextBlock { Align = "left", Edge = 0.7f, Y = 0.05f },
            },
            ["tube_2"] = new CotiTube { Mount = new CotiMountBlock { AnchorBone = "axis_2", PositionX = 0.007f, Scale = 1.518f } },
        };
        return d;
    }

    private static void AssertSameTubes(CotiDeviceFile want, CotiDeviceFile got)
    {
        Assert.Equal(want.Layout, got.Layout);
        Assert.Equal(want.Tubes!.Keys.OrderBy(k => k).ToArray(), got.Tubes!.Keys.OrderBy(k => k).ToArray());

        foreach (var (label, tube) in want.Tubes)
        {
            var m = got.Tubes[label].Mount;
            Assert.Equal(tube.Mount.AnchorBone, m.AnchorBone);
            Assert.Equal(tube.Mount.PositionX, m.PositionX);
            Assert.Equal(tube.Mount.PositionY, m.PositionY);
            Assert.Equal(tube.Mount.PositionZ, m.PositionZ);
            Assert.Equal(tube.Mount.RotationX, m.RotationX);
            Assert.Equal(tube.Mount.RotationY, m.RotationY);
            Assert.Equal(tube.Mount.RotationZ, m.RotationZ);
            Assert.Equal(tube.Mount.RollDegrees, m.RollDegrees);
            Assert.Equal(tube.Mount.PitchDegrees, m.PitchDegrees);
            Assert.Equal(tube.Mount.YawDegrees, m.YawDegrees);
            Assert.Equal(tube.Mount.Scale, m.Scale);

            var pod = got.Tubes[label].Pod;
            Assert.Equal(tube.Pod is null, pod is null);
            Assert.Equal(tube.Pod?.Bone, pod?.Bone);
            Assert.Equal(tube.Pod?.DownX, pod?.DownX);
            Assert.Equal(tube.Pod?.DownY, pod?.DownY);
            Assert.Equal(tube.Pod?.DownZ, pod?.DownZ);

            var text = got.Tubes[label].Text;
            Assert.Equal(tube.Text is null, text is null);
            Assert.Equal(tube.Text?.Align, text?.Align);
            Assert.Equal(tube.Text?.Edge, text?.Edge);
            Assert.Equal(tube.Text?.Y, text?.Y);
        }
    }

    [Fact]
    public void TubesCrossFromServerToClientWithNothingLost()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new ServerTable { Devices = { ServerDto.FromShared(MultiTube()) } });

        var got = Newtonsoft.Json.JsonConvert.DeserializeObject<ClientTable>(json)!.Devices.Single().ToShared();

        AssertSameTubes(MultiTube(), got);
    }

    [Fact]
    public void TubesCrossFromClientToServerWithNothingLost()
    {
        var json = Newtonsoft.Json.JsonConvert.SerializeObject(ClientDto.FromShared(MultiTube()));

        var got = System.Text.Json.JsonSerializer.Deserialize<ServerDto>(json)!.ToShared();

        AssertSameTubes(MultiTube(), got);
    }

    [Fact]
    public void TheClientHalfKeepsANullTubeAndAnUnposedTubeNullForValidationToDrop()
    {
        // The 4.0 tuner publishes a copy of the last applied device, which may hold either.
        var device = MultiTube();
        device.Tubes!["tube_0"] = null!;
        device.Tubes["tube_2"].Mount = null!;

        var tubes = ClientDto.FromShared(device).Tubes!;

        Assert.Null(tubes["tube_0"]);
        Assert.Null(tubes["tube_2"]!.Mount);
        Assert.Equal("axis_3", tubes["tube_1"]!.Mount!.AnchorBone);
    }

    [Fact]
    public void AV1DeviceWritesNoLayoutOrTubesFromEitherHalf()
    {
        // A v1 device keeps 3.2.0's shape on the wire and in files, whatever options the writer uses.
        var fromServer = System.Text.Json.JsonSerializer.Serialize(ServerDto.FromShared(Sample()));
        var fromClient = Newtonsoft.Json.JsonConvert.SerializeObject(ClientDto.FromShared(Sample()));

        foreach (var json in new[] { fromServer, fromClient })
        {
            Assert.DoesNotContain("\"layout\"", json);
            Assert.DoesNotContain("\"tubes\"", json);
            Assert.DoesNotContain("\"text\"", json);
        }
    }

    [Fact]
    public void ATubeWithoutATextBlockWritesNoTextKey()
    {
        var device = MultiTube();
        device.Tubes!.Remove("tube_1");

        var fromServer = System.Text.Json.JsonSerializer.Serialize(ServerDto.FromShared(device));
        var fromClient = Newtonsoft.Json.JsonConvert.SerializeObject(ClientDto.FromShared(device));

        foreach (var json in new[] { fromServer, fromClient })
        {
            Assert.Contains("\"tube_2\"", json);
            Assert.DoesNotContain("\"text\"", json);
        }
    }

    /// <summary>
    /// A text block writes only the fields it sets, from either half, so a file keeps only what differs from the rule;
    /// a v1 device's top-level block crosses both ways.
    /// </summary>
    [Fact]
    public void ATextBlockWritesOnlyItsOwnFieldsAndCrossesBothWays()
    {
        var device = Sample();
        device.Text = new CotiTextBlock { Y = -0.1f };

        var fromServer = System.Text.Json.JsonSerializer.Serialize(ServerDto.FromShared(device));
        var fromClient = Newtonsoft.Json.JsonConvert.SerializeObject(ClientDto.FromShared(device));

        foreach (var json in new[] { fromServer, fromClient })
        {
            Assert.Contains("\"text\"", json);
            Assert.Contains("\"y\"", json);
            Assert.DoesNotContain("\"align\"", json);
            Assert.DoesNotContain("\"edge\"", json);
        }

        var toClient = Newtonsoft.Json.JsonConvert.DeserializeObject<ClientDto>(fromServer)!.ToShared();
        var toServer = System.Text.Json.JsonSerializer.Deserialize<ServerDto>(fromClient)!.ToShared();

        foreach (var got in new[] { toClient, toServer })
        {
            Assert.Null(got.Text!.Align);
            Assert.Null(got.Text.Edge);
            Assert.Equal(-0.1f, got.Text.Y);
        }
    }

    [Fact]
    public void EveryTubeWireNameIsLowerCamelCase()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(ServerDto.FromShared(MultiTube()));

        foreach (var name in new[] { "layout", "tubes", "tube_1", "tube_2", "mount", "pod", "bone", "downX", "downY", "downZ",
                                     "text", "align", "edge", "y" })
            Assert.Contains("\"" + name + "\"", json);
    }

    [Fact]
    public void AHandAuthoredMultiTubeFileParsesInBothHalves()
    {
        const string json = """
        {
          "schema": 1,
          "device": "com.c11.truenorth4_dtnvs",
          "displayName": "DTNVS",
          "tuned": true,
          "hosts": [ { "id": "111" } ],
          "mask": { "centerX": 0.5353, "centerY": 0.4991, "radius": 0.27362, "feather": 0.01 },
          "mount": { "anchorBone": "axis_2", "scale": 1.065 },
          "layout": "dual",
          "tubes": {
            "tube_1": {
              "mount": { "anchorBone": "axis_1", "positionX": -0.0005, "scale": 1.065 },
              "pod": { "downX": -90.0, "downY": 0.0, "downZ": 0.0 }
            },
            "tube_2": {
              "mount": { "anchorBone": "axis_2", "positionX": 0.0005, "scale": 1.065 },
              "pod": { "bone": "axis_2", "downX": -90.0, "downY": 0.0, "downZ": 0.0 },
              "text": { "align": "right", "edge": 0.55, "y": 0.1 }
            }
          }
        }
        """;

        var fromServer = System.Text.Json.JsonSerializer.Deserialize<ServerDto>(json)!.ToShared();
        var fromClient = Newtonsoft.Json.JsonConvert.DeserializeObject<ClientDto>(json)!.ToShared();

        foreach (var got in new[] { fromServer, fromClient })
        {
            Assert.Equal("dual", got.Layout);
            Assert.Equal("axis_1", got.Tubes!["tube_1"].Mount.AnchorBone);
            Assert.Equal(-0.0005f, got.Tubes["tube_1"].Mount.PositionX);
            Assert.Null(got.Tubes["tube_1"].Pod!.Bone);
            Assert.Equal(-90f, got.Tubes["tube_1"].Pod!.DownX);
            Assert.Equal("axis_2", got.Tubes["tube_2"].Pod!.Bone);
            Assert.Equal(1.065f, got.Tubes["tube_2"].Mount.Scale);
            Assert.Null(got.Tubes["tube_1"].Text);
            Assert.Equal("right", got.Tubes["tube_2"].Text!.Align);
            Assert.Equal(0.55f, got.Tubes["tube_2"].Text!.Edge);
            Assert.Equal(0.1f, got.Tubes["tube_2"].Text!.Y);
        }
    }

    /// <summary>
    /// A tube with no mount must not become a default mount at the anchor's origin. Both halves pass the null through
    /// and the shared validation drops that tube with a warning, keeping the device.
    /// </summary>
    [Theory]
    [InlineData("""{ "tube_1": null, "tube_2": { "mount": { "anchorBone": "axis_2" } } }""")]
    [InlineData("""{ "tube_1": { "pod": { "downX": -90.0 } }, "tube_2": { "mount": { "anchorBone": "axis_2" } } }""")]
    public void ATubeWithNoMountConvertsAndIsThenDroppedByValidation(string tubes)
    {
        var json = """{ "schema": 1, "device": "x", "displayName": "X", "hosts": [ { "id": "111" } ], "mask": { "radius": 0.27 }, "mount": {}, "layout": "dual", "tubes": """ + tubes + " }";

        var fromServer = System.Text.Json.JsonSerializer.Deserialize<ServerDto>(json)!.ToShared();
        var fromClient = Newtonsoft.Json.JsonConvert.DeserializeObject<ClientDto>(json)!.ToShared();

        foreach (var device in new[] { fromServer, fromClient })
        {
            var merged = CotiDeviceMerge.Merge(new[] { new CotiParsedFile { Path = "<published>", Device = device } });

            var kept = Assert.Single(merged.Devices);
            Assert.Equal(new[] { "tube_2" }, kept.Tubes!.Keys.ToArray());
            Assert.Contains(merged.Warnings, w => w.Contains("tube_1"));
        }
    }
}
