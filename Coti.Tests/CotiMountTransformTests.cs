using System;
using Coti.Shared;
using Xunit;

public class CotiMountTransformTests
{
    private const float Tol = 1e-5f;

    private static void AssertQuat(CotiQuat actual, float x, float y, float z, float w)
    {
        Assert.Equal(x, actual.X, Tol);
        Assert.Equal(y, actual.Y, Tol);
        Assert.Equal(z, actual.Z, Tol);
        Assert.Equal(w, actual.W, Tol);
    }

    private static CotiMountBlock Block(
        float px = 0, float py = 0, float pz = 0,
        float rx = 0, float ry = 0, float rz = 0,
        float yaw = 0, float pitch = 0, float roll = 0,
        float scale = 1f)
    {
        return new CotiMountBlock
        {
            PositionX = px, PositionY = py, PositionZ = pz,
            RotationX = rx, RotationY = ry, RotationZ = rz,
            YawDegrees = yaw, PitchDegrees = pitch, RollDegrees = roll,
            Scale = scale,
        };
    }

    [Fact]
    public void NullMountIsIdentityAtUnitScale()
    {
        var pose = CotiMountTransform.Compute(null);

        Assert.Equal(0f, pose.Position.X);
        Assert.Equal(0f, pose.Position.Y);
        Assert.Equal(0f, pose.Position.Z);
        AssertQuat(pose.Rotation, 0, 0, 0, 1);
        Assert.Equal(1f, pose.Scale);
    }

    // A host entry predating the scale field deserialises to 0.
    [Fact]
    public void ZeroScaleBecomesOne()
    {
        Assert.Equal(1f, CotiMountTransform.Compute(Block(scale: 0f)).Scale);
    }

    [Fact]
    public void ScaleIsClampedToTheMinimum()
    {
        var pose = CotiMountTransform.Compute(Block(scale: 0.5f), default, default, -10f);

        Assert.Equal(CotiMountTransform.MinimumScale, pose.Scale);
    }

    [Fact]
    public void ScaleDeltaIsAdded()
    {
        Assert.Equal(1.5f, CotiMountTransform.Compute(Block(scale: 1f), default, default, 0.5f).Scale, Tol);
    }

    [Fact]
    public void PositionDeltaIsAdded()
    {
        var pose = CotiMountTransform.Compute(
            Block(px: 1f, py: 2f, pz: 3f), new CotiVec3(0.5f, -0.5f, 0.25f), default, 0f);

        Assert.Equal(1.5f, pose.Position.X, Tol);
        Assert.Equal(1.5f, pose.Position.Y, Tol);
        Assert.Equal(3.25f, pose.Position.Z, Tol);
    }

    [Fact]
    public void BasisAloneIsTheEulerRotation()
    {
        AssertQuat(CotiMountTransform.Compute(Block(rx: -90f)).Rotation, -0.7071068f, 0, 0, 0.7071068f);
    }

    [Fact]
    public void RollTurnsAboutForward()
    {
        AssertQuat(CotiMountTransform.Compute(Block(roll: 90f)).Rotation, 0, 0, 0.7071068f, 0.7071068f);
    }

    [Fact]
    public void PitchTurnsAboutRight()
    {
        AssertQuat(CotiMountTransform.Compute(Block(pitch: 90f)).Rotation, 0.7071068f, 0, 0, 0.7071068f);
    }

    [Fact]
    public void YawTurnsAboutUp()
    {
        AssertQuat(CotiMountTransform.Compute(Block(yaw: 90f)).Rotation, 0, 0.7071068f, 0, 0.7071068f);
    }

    // The delta vector is not in mount-field order: x drives pitch, y yaw, z roll.
    [Fact]
    public void RotationDeltaMapsXToPitchYToYawZToRoll()
    {
        var viaDelta = CotiMountTransform.Compute(
            Block(), default, new CotiVec3(10f, 20f, 30f), 0f).Rotation;
        var viaFields = CotiMountTransform.Compute(Block(yaw: 20f, pitch: 10f, roll: 30f)).Rotation;

        AssertQuat(viaDelta, viaFields.X, viaFields.Y, viaFields.Z, viaFields.W);
    }

    // Pinned from hosts/vanilla_pvs14.json.
    [Fact]
    public void VanillaPvs14MatchesTheVerifiedPose()
    {
        var pose = CotiMountTransform.Compute(
            Block(px: 0.015f, py: -0.024f, pz: 0.039f, rx: -90f, roll: 104f, scale: 1.16f));

        Assert.Equal(0.015f, pose.Position.X, Tol);
        Assert.Equal(-0.024f, pose.Position.Y, Tol);
        Assert.Equal(0.039f, pose.Position.Z, Tol);
        Assert.Equal(1.16f, pose.Scale, Tol);
        AssertQuat(pose.Rotation, -0.4353384f, -0.5572077f, 0.5572077f, 0.4353384f);
    }

    [Fact]
    public void VanillaN15MatchesTheVerifiedPose()
    {
        var pose = CotiMountTransform.Compute(
            Block(px: 0.033f, py: -0.023f, pz: 0.037f, rx: -90f, roll: -42f, scale: 1.24f));

        AssertQuat(pose.Rotation, -0.6601411f, 0.2534044f, -0.2534044f, 0.6601411f);
    }

    // Yaw, pitch and roll do not commute, so the order matters.
    [Fact]
    public void RotationOrderIsNotCommutative()
    {
        var rollThenPitch = CotiMountTransform.Compute(Block(pitch: 40f, roll: 70f)).Rotation;
        var pitchThenRoll = CotiMountTransform.Compute(Block(pitch: 70f, roll: 40f)).Rotation;

        Assert.False(
            Math.Abs(rollThenPitch.X - pitchThenRoll.X) < Tol &&
            Math.Abs(rollThenPitch.Y - pitchThenRoll.Y) < Tol &&
            Math.Abs(rollThenPitch.Z - pitchThenRoll.Z) < Tol);
    }

    [Fact]
    public void RotationIsNormalised()
    {
        var q = CotiMountTransform.Compute(Block(rx: 33f, ry: -12f, rz: 87f, yaw: 15f, pitch: -40f, roll: 104f)).Rotation;
        var length = Math.Sqrt(q.X * q.X + q.Y * q.Y + q.Z * q.Z + q.W * q.W);

        Assert.Equal(1.0, length, 1e-5);
    }
}

// Every field distinct, so a crossed or dropped mapping shows up as a wrong value.
public class CotiNvgHostConfigMountTests
{
    [Fact]
    public void ToMountBlockCarriesEveryField()
    {
        var host = new Coti.Client.CotiNvgHostConfig
        {
            MountAnchorBone = "bone_x",
            MountPositionX = 1f, MountPositionY = 2f, MountPositionZ = 3f,
            MountRotationX = 4f, MountRotationY = 5f, MountRotationZ = 6f,
            MountRollDegrees = 7f, MountPitchDegrees = 8f, MountYawDegrees = 9f,
            MountScale = 10f,
        };

        var block = host.ToMountBlock();

        Assert.Equal("bone_x", block.AnchorBone);
        Assert.Equal(1f, block.PositionX);
        Assert.Equal(2f, block.PositionY);
        Assert.Equal(3f, block.PositionZ);
        Assert.Equal(4f, block.RotationX);
        Assert.Equal(5f, block.RotationY);
        Assert.Equal(6f, block.RotationZ);
        Assert.Equal(7f, block.RollDegrees);
        Assert.Equal(8f, block.PitchDegrees);
        Assert.Equal(9f, block.YawDegrees);
        Assert.Equal(10f, block.Scale);
    }

    // The pinned PVS-14 pose, reached the way the client reaches it.
    [Fact]
    public void ClientPathReachesTheVerifiedPvs14Pose()
    {
        var host = new Coti.Client.CotiNvgHostConfig
        {
            MountPositionX = 0.015f, MountPositionY = -0.024f, MountPositionZ = 0.039f,
            MountRotationX = -90f, MountRollDegrees = 104f, MountScale = 1.16f,
        };

        var pose = CotiMountTransform.Compute(host.ToMountBlock());

        Assert.Equal(-0.4353384f, pose.Rotation.X, 1e-5f);
        Assert.Equal(-0.5572077f, pose.Rotation.Y, 1e-5f);
        Assert.Equal(0.5572077f, pose.Rotation.Z, 1e-5f);
        Assert.Equal(0.4353384f, pose.Rotation.W, 1e-5f);
        Assert.Equal(1.16f, pose.Scale, 1e-5f);
    }
}
