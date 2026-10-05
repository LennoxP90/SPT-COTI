namespace Coti.Shared
{
  /// <summary>
  /// The RenderType a material is given so the heat-only replacement shader draws it. Replacement reads the tag off
  /// the material's own shader, never a Fallback's, and draws nothing where it has no matching sub-shader. A solid
  /// surface whose shader declares no RenderType therefore wrote no depth in the thermal image, and heat behind it
  /// showed through: the admin office wall on Factory, which uses a vertex-paint shader with no tag of its own.
  /// </summary>
  public static class CotiRenderTypeTag
  {
    /// <summary>Unity's Geometry queue, where solid surfaces start.</summary>
    public const int Geometry = 2000;

    /// <summary>Unity's AlphaTest queue, for surfaces cut by their texture's alpha.</summary>
    public const int AlphaTest = 2450;

    /// <summary>Unity's last opaque queue. Above it everything is see-through and must not hide heat.</summary>
    public const int GeometryLast = 2500;

    /// <summary>The RenderTypes Coti/HeatOnly has a sub-shader for, beside COTI's own (all named Coti...).</summary>
    private static readonly string[] Drawn =
      { "Opaque", "TransparentCutout", "TreeOpaque", "TreeBark", "TreeLeaf", "TreeTransparentCutout", "Grass" };

    /// <summary>
    /// The tag to give a material, or null to leave it alone. A material in the opaque queues with no tag draws as a
    /// solid surface. One whose tag the heat-only shader has no sub-shader for (Transparent on a road decal that is the
    /// ground on a bridge) does too when a world renderer using it stands on a solid collider: collision says it is
    /// solid, where a flashlight's beam mesh, in the same queue with the same tag, has none. Left alone: a tag already
    /// drawn, COTI's own tags, and anything outside the opaque queues (sky, transparents, effects).
    /// </summary>
    public static string For( string ownTag, int renderQueue, bool onSolidCollider = false )
    {
      if( !string.IsNullOrEmpty( ownTag ) && ( System.Array.IndexOf( Drawn, ownTag ) >= 0 || ownTag.StartsWith( "Coti" ) || !onSolidCollider ) )
        return null;
      if( renderQueue < Geometry || renderQueue > GeometryLast )
        return null;
      return renderQueue >= AlphaTest ? "TransparentCutout" : "Opaque";
    }

    /// <summary>
    /// Whether a see-through material is glass to the thermal image, which draws it as a solid surface. Glass stops
    /// bullets, so it stands on a solid collider; smoke, sparks and dust are particle systems, and rain, tracers and
    /// muzzle flashes have no collider. Anything a player carries or that lies as loot is left see-through, so a
    /// player's own sight lens and a dropped item never black out the image.
    /// </summary>
    public static bool IsGlass( int renderQueue, bool particles, bool onSolidCollider, bool carriedOrLoot )
    {
      return renderQueue > GeometryLast && !particles && onSolidCollider && !carriedOrLoot;
    }

    /// <summary>
    /// The RenderType for a terrain's own material: Coti/HeatOnly has no sub-shader for it, so the replacement skips it,
    /// and COTI draws that terrain itself from its heightmap (CotiThermalTerrain).
    /// </summary>
    public const string DrawnByCoti = "CotiDrawnByCoti";

    /// <summary>
    /// The RenderType for materials only particle systems draw. Coti/HeatOnly has no sub-shader for it, so they are never
    /// drawn: smoke, sparks and heat haze are not surfaces, whatever their shaders declare. EFT's heat haze declares
    /// Opaque, and drawn as solid it blacked out a hot barrel exactly while it was hot enough to shimmer.
    /// </summary>
    public const string ParticleOnly = "CotiParticle";

    /// <summary>
    /// Whether a material is drawn only by particle systems. One a mesh also uses (a rock material on flying debris)
    /// keeps its own tag, so the mesh still hides what is behind it.
    /// </summary>
    public static bool IsParticleOnly( bool usedByParticles, bool usedByMesh )
    {
      return usedByParticles && !usedByMesh;
    }

    /// <summary>The RenderType glass is given for each mode, one sub-shader of Coti/HeatOnly each.</summary>
    public static string GlassTag( CotiGlassMode mode )
    {
      return mode == CotiGlassMode.Reflections ? "CotiGlassMirror" : "CotiGlass";
    }
  }

  /// <summary>
  /// How glass draws in the thermal image. Plain: a cold surface that hides what is behind it. Reflections: the same,
  /// plus the heat in front of it mirrored in it, strongest at grazing angles, as glass does in long-wave infrared.
  /// </summary>
  public enum CotiGlassMode
  {
    Plain,
    Reflections,
  }
}
