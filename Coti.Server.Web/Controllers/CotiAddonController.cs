using Coti.Server;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Coti.Server.Web.Controllers;

/// <summary>Hands back an addon archive for one source mod as a file download.</summary>
[ApiController]
[Route("coti/addon")]
[Authorize(Policy = "Administrator")]
public class CotiAddonController(CotiDeviceStore deviceStore) : ControllerBase
{
  private const string SevenZip = "application/x-7z-compressed";

  [HttpGet("{requires}")]
  public IActionResult Get(string requires)
  {
    if (string.IsNullOrWhiteSpace(requires))
    {
      return BadRequest("No mod given.");
    }

    var groups = CotiAddonPackager.GroupBySourceMod(deviceStore.Current.ResolvedDevices);

    if (!groups.TryGetValue(requires, out var devices) || devices.Count == 0)
    {
      return NotFound($"No devices depend on {requires}.");
    }

    return File(
      CotiAddonPackager.Build(requires, devices),
      SevenZip,
      CotiAddonPackager.FileNameFor(requires));
  }
}
