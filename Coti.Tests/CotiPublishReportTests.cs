using System.Collections.Generic;
using Coti.Client;
using Xunit;

public class CotiPublishReportTests
{
    [Fact]
    public void AFailedPublishReportsTheError()
    {
        var message = CotiPublishReport.Describe(false, "rejected by validation", new List<string>());
        Assert.Equal("Publish failed: rejected by validation", message);
    }

    [Fact]
    public void AFailedPublishWithNoErrorStillSaysSomething()
    {
        var message = CotiPublishReport.Describe(false, null, new List<string>());
        Assert.Equal("Publish failed: no reason given", message);
    }

    [Fact]
    public void OkWithNoUnfitHostsIsAPlainSuccess()
    {
        var message = CotiPublishReport.Describe(true, null, new List<string>());
        Assert.Equal("Published", message);
    }

    [Fact]
    public void OkWithUnfitHostsIsNotReportedAsAPlainSuccess()
    {
        // Ok stays true when the file was written but no host could be fitted, so a caller checking
        // only Ok would report a clean success. The message must say otherwise.
        var message = CotiPublishReport.Describe(true, null, new List<string> { "host1: InvalidId" });
        Assert.Contains("could not be fitted", message);
        Assert.Contains("host1: InvalidId", message);
    }

    [Fact]
    public void ANullUnfitHostsListIsTreatedAsNone()
    {
        var message = CotiPublishReport.Describe(true, null, null);
        Assert.Equal("Published", message);
    }
}
