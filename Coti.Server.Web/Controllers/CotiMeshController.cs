using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Coti.Server.Web.Controllers;

/// <summary>Serves the host and device meshes as glTF binary.</summary>
[ApiController]
[Route("coti/mesh")]
[Authorize]
public class CotiMeshController : ControllerBase
{
  private const string GltfBinary = "model/gltf-binary";

  [HttpGet("{slug}")]
  public IActionResult Get(string slug)
  {
    // Rejects anything that is not a bare file name.
    if (string.IsNullOrWhiteSpace(slug) || slug.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
        || slug.Contains('.') || slug.Contains('/') || slug.Contains('\\'))
    {
      return BadRequest("Bad mesh name.");
    }

    // Local meshes win over the shipped set.
    var dir = CotiHostMeshes.FolderFor(slug);

    if (string.IsNullOrEmpty(dir))
    {
      return NotFound();
    }

    var file = Path.GetFullPath(Path.Combine(dir, slug + ".glb"));

    if (!file.StartsWith(Path.GetFullPath(dir), StringComparison.OrdinalIgnoreCase)
        || !System.IO.File.Exists(file))
    {
      return NotFound();
    }

    return PhysicalFile(file, GltfBinary);
  }
}
