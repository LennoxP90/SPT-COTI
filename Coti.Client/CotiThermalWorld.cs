using System.Collections.Generic;
using System.IO;
using Comfort.Common;
using Coti.Shared;
using EFT;
using EFT.Interactive;
using EFT.Visual;
using UnityEngine;
using UnityEngine.Rendering;

namespace Coti.Client
{
  /// <summary>
  /// What the heat-only shader needs from the world that EFT does not give it: the air's temperature to read contrast
  /// against, a RenderType on solid surfaces and glass, each body's temperature (cultists just above the air), heat on
  /// burning fires, unfired weapons back at rest, and, for glass reflections, every body nearby as capsules. Run every
  /// frame from the thermal camera's render setup; the scans run on timers, per raid.
  /// </summary>
  internal static class CotiThermalWorld
  {
    private const float SweepSeconds = 30f;
    private const float BodySweepSeconds = 0.5f;
    private const float LampSweepSeconds = 2f;
    private const float FallbackAirCelsius = 15f;
    private const float ReflectedBodyMetres = 30f;
    private const int MaxCapsules = 160;
    private const int CapsulesPerBody = 18;

    private static readonly int HeatTempId = Shader.PropertyToID( "_CotiHeatTemp" );
    private static readonly int BulbsId = Shader.PropertyToID( "_CotiBulbs" );
    private static readonly int BulbCountId = Shader.PropertyToID( "_CotiBulbCount" );
    private static readonly int HousingId = Shader.PropertyToID( "_CotiHousing" );
    private static readonly int GlowId = Shader.PropertyToID( "_CotiGlow" );
    private static readonly int BulbAxesId = Shader.PropertyToID( "_CotiBulbAxes" );
    private static readonly int DimId = Shader.PropertyToID( "_CotiDim" );
    // EFT's rain on the player's own weapon and hands keeps a block of its own per material and re-applies it every
    // frame while it rains, so COTI's values have to be in that block too. Private, so read by reflection.
    private static readonly System.Reflection.FieldInfo RainBlockField =
        HarmonyLib.AccessTools.Field( typeof( RainCondensator ), "_materialPropertyBlock" );
    private const float DeviceRefreshSeconds = 10f;
    // The shader's arrays are MaxBulbs long, and a block fixes an array's length the first time it is set. Bulbs and Axes
    // are one renderer's heat boxes in its own space, filled just before they are written; WorldBoxes and WorldAxes a
    // device's lenses before each renderer takes them in.
    private static readonly Vector4[] Bulbs = new Vector4[CotiLampHeat.MaxBulbs];
    private static readonly Vector4[] Axes = new Vector4[CotiLampHeat.MaxBulbs];
    private static readonly Vector4[] NoBulbs = new Vector4[CotiLampHeat.MaxBulbs];
    private static readonly Vector4[] WorldBoxes = new Vector4[CotiLampHeat.MaxBulbs];
    private static readonly Vector3[] WorldAxes = new Vector3[CotiLampHeat.MaxBulbs];
    private static readonly int CapsuleAId = Shader.PropertyToID( "_CotiCapsuleA" );
    private static readonly int CapsuleBId = Shader.PropertyToID( "_CotiCapsuleB" );
    private static readonly int CapsuleCountId = Shader.PropertyToID( "_CotiCapsuleCount" );
    private static readonly int AirId = Shader.PropertyToID( "_CotiAirCelsius" );

    private static readonly List<Material> Glass = new List<Material>();
    private static readonly HashSet<Material> MeshMaterials = new HashSet<Material>();
    private static readonly HashSet<Material> SolidMaterials = new HashSet<Material>();
    private static readonly MaterialPropertyBlock Block = new MaterialPropertyBlock();
    private static readonly Vector4[] CapsuleA = new Vector4[MaxCapsules];
    private static readonly Vector4[] CapsuleB = new Vector4[MaxCapsules];
    private static readonly Dictionary<Player, List<HotObject>> Skins = new Dictionary<Player, List<HotObject>>();
    private static readonly List<LampController> Lamps = new List<LampController>();
    // What each lamp was last written as: 0 off, 1 lit before its shape was found, 3 lit with it.
    private static readonly Dictionary<LampController, int> Written = new Dictionary<LampController, int>();
    // The lamp types lamps.json lists; only these are ever heated.
    private static HashSet<string> _heatedLamps = new HashSet<string>();
    private static readonly List<TacticalComboVisualController> Devices = new List<TacticalComboVisualController>();
    private static float _nextDeviceRefresh;

    private static GameWorld _world;
    private static CotiGlassMode _glassMode;
    private static int _frame = -1;
    private static int _capsules;
    private static float _nextSweep;
    private static float _nextBodySweep;
    private static float _nextLampSweep;

    internal static void Update( Camera camera )
    {

      var world0 = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null;
      if( world0 != null && world0 == _world )
        CotiThermalTerrain.Update( camera, MeshMaterials );

      // Both thermal cameras call this; once a frame is enough.
      if( _frame == Time.frameCount )
        return;
      _frame = Time.frameCount;
      CotiLampShapes.Update();

      var world = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null;
      if( world == null )
        return;

      var air = AirCelsius();
      Shader.SetGlobalFloat( AirId, air );

      var glass = Plugin.Config.Image.Glass;
      if( world != _world )
      {
        _world = world;
        Skins.Clear();
        Lamps.Clear();
        Written.Clear();
        CotiLampShapes.Clear();
        LoadHeatedLamps();
        Devices.Clear();
        _nextLampSweep = 0f;
        _nextDeviceRefresh = 0f;
        _nextSweep = 0f;
        _nextBodySweep = 0f;
        FindGlass();
        TagGlass( glass );
        TagParticleOnly();
      }
      else if( glass != _glassMode )
        TagGlass( glass );

      UploadCapsules( world, glass == CotiGlassMode.Reflections, air );

      var now = Time.realtimeSinceStartup;
      if( now >= _nextBodySweep )
      {
        _nextBodySweep = now + BodySweepSeconds;
        MarkBodies( world, air );
      }
      if( now >= _nextLampSweep )
      {
        _nextLampSweep = now + LampSweepSeconds;
        HeatLamps();
        HeatDevices( air, now );
      }
      if( now >= _nextSweep )
      {
        _nextSweep = now + SweepSeconds;
        Lamps.Clear();
        foreach( var lamp in Object.FindObjectsOfType<LampController>() )
          if( _heatedLamps.Contains( CotiLampHeat.TypeOf( lamp.name ) ) )
            Lamps.Add( lamp );
        TagParticleOnly();
        TagUntaggedMaterials();
        HeatFires();
        RestStaleWeapons();
      }
    }

    /// <summary>
    /// Gives every loaded material in the opaque queues a RenderType the replacement draws, where its own is none or one
    /// the heat-only shader has no sub-shader for (CotiRenderTypeTag.For), so it hides the heat behind it. Swept again for materials loaded since: bots' gear and
    /// streamed scenes. A tagged material reads its override back, so each is tagged only once.
    /// </summary>
    private static void TagUntaggedMaterials()
    {
      var started = Time.realtimeSinceStartup;
      var materials = Resources.FindObjectsOfTypeAll<Material>();
      var tagged = 0;
      foreach( var material in materials )
      {
        if( material == null || material.shader == null )
          continue;
        var tag = CotiRenderTypeTag.For( material.GetTag( "RenderType", false, "" ), material.renderQueue, SolidMaterials.Contains( material ) );
        if( tag == null )
          continue;
        material.SetOverrideTag( "RenderType", tag );
        tagged++;
      }

      if( tagged > 0 )
        Plugin.Log.LogInfo( $"Thermal: tagged {tagged} of {materials.Length} material(s) with no RenderType in {( Time.realtimeSinceStartup - started ) * 1000f:0} ms" );
    }

    /// <summary>
    /// The see-through materials that are glass (CotiRenderTypeTag.IsGlass), decided per material from every world
    /// renderer using it: one window pane standing on its collider makes the material glass for all its panes. A
    /// material any particle system uses is never glass. Also records every material a mesh draws, which keeps a
    /// material shared with particles solid (TagParticleOnly), and every one a world renderer on a solid collider draws,
    /// which makes an opaque-queue material solid whatever it is tagged (CotiRenderTypeTag.For). Once per raid.
    /// </summary>
    private static void FindGlass()
    {
      var started = Time.realtimeSinceStartup;
      var glass = new HashSet<Material>();
      var particles = new HashSet<Material>();
      var candidates = new Dictionary<Material, bool>();
      MeshMaterials.Clear();
      SolidMaterials.Clear();
      foreach( var renderer in Object.FindObjectsOfType<Renderer>() )
      {
        if( renderer == null || !renderer.enabled )
          continue;
        var materials = renderer.sharedMaterials;
        var isParticles = renderer is ParticleSystemRenderer;
        if( !isParticles )
          foreach( var material in materials )
            if( material != null )
              MeshMaterials.Add( material );
        // Collision is looked up only for renderers with a material it can decide: on a large map most renderers have
        // none, and checking every one stalled the first thermal frame for most of a second.
        if( isParticles || !AnyCandidate( materials, candidates ) )
          continue;
        var onCollider = OnSolidCollider( renderer.transform );
        var carried = onCollider && ( renderer.GetComponentInParent<Player>() != null || renderer.GetComponentInParent<LootItem>() != null );
        if( onCollider && !carried )
          foreach( var material in materials )
            if( material != null )
              SolidMaterials.Add( material );
        if( !AnySeeThrough( materials ) )
          continue;

        foreach( var material in materials )
        {
          if( material == null )
            continue;
          if( isParticles )
            particles.Add( material );
          else if( CotiRenderTypeTag.IsGlass( material.renderQueue, false, onCollider, carried ) )
            glass.Add( material );
        }
      }

      glass.ExceptWith( particles );
      Glass.Clear();
      Glass.AddRange( glass );
      Plugin.Log.LogInfo( $"Thermal: {Glass.Count} glass material(s) found in {( Time.realtimeSinceStartup - started ) * 1000f:0} ms" );
    }

    /// <summary>
    /// Every material only particle systems draw, the pooled and inactive effects included (EFT's heat haze among them),
    /// gets a RenderType the heat-only shader never draws (CotiRenderTypeTag.ParticleOnly).
    /// </summary>
    private static void TagParticleOnly()
    {
      foreach( var particles in Resources.FindObjectsOfTypeAll<ParticleSystemRenderer>() )
      {
        if( particles == null )
          continue;
        foreach( var material in particles.sharedMaterials )
          if( material != null && CotiRenderTypeTag.IsParticleOnly( true, MeshMaterials.Contains( material ) ) )
            material.SetOverrideTag( "RenderType", CotiRenderTypeTag.ParticleOnly );
      }
    }

    // A material collision can decide: see-through (glass or not), or in the opaque queues with a tag the heat-only
    // shader has no sub-shader for. Cached per material, which most renderers share.
    private static bool AnyCandidate( Material[] materials, Dictionary<Material, bool> candidates )
    {
      foreach( var material in materials )
      {
        if( material == null )
          continue;
        if( !candidates.TryGetValue( material, out var candidate ) )
        {
          candidate = material.renderQueue > CotiRenderTypeTag.GeometryLast
                      || CotiRenderTypeTag.For( material.GetTag( "RenderType", false, "" ), material.renderQueue, true ) != null;
          candidates[material] = candidate;
        }
        if( candidate )
          return true;
      }
      return false;
    }

    private static bool AnySeeThrough( Material[] materials )
    {
      foreach( var material in materials )
        if( material != null && material.renderQueue > CotiRenderTypeTag.GeometryLast )
          return true;
      return false;
    }

    // A non-trigger collider on the object or its parent: a window's pane often sits under its frame.
    private static bool OnSolidCollider( Transform t )
    {
      for( var i = 0; t != null && i < 2; i++, t = t.parent )
        foreach( var collider in t.GetComponents<Collider>() )
          if( collider != null && collider.enabled && !collider.isTrigger )
            return true;
      return false;
    }

    private static void TagGlass( CotiGlassMode mode )
    {
      _glassMode = mode;
      var tag = CotiRenderTypeTag.GlassTag( mode );
      foreach( var material in Glass )
        if( material != null )
          material.SetOverrideTag( "RenderType", tag );
    }

    /// <summary>
    /// Every burning fire burns a player who stands in it through a FlameDamageTrigger, live only while the fire is lit.
    /// The renderers of the fire's own prop inside that volume (CotiWorldHeat.IsFireProp) get a COTI-only block value,
    /// which EFT's own shaders never read, so a lit barrel or bonfire draws hot and an unlit one does not.
    /// </summary>
    private static void HeatFires()
    {
      var heated = 0;
      foreach( var fire in Object.FindObjectsOfType<FlameDamageTrigger>() )
      {
        var volume = fire.GetComponent<Collider>();
        if( volume == null || !volume.enabled )
          continue;
        var bounds = volume.bounds;
        var fireMetres = bounds.size.magnitude;
        bounds.Expand( 1f );

        // The fire's prop: Barrel_fire_on/Small_Fire_no_Light/<trigger>, or the trigger's parent when it sits directly
        // on the prop. The bounds and size tests keep anything else under it out.
        var parent = fire.transform.parent;
        var root = parent != null && parent.parent != null ? parent.parent : parent != null ? parent : fire.transform;
        foreach( var renderer in root.GetComponentsInChildren<Renderer>() )
        {
          if( renderer.shadowCastingMode == ShadowCastingMode.ShadowsOnly )
            continue;
          if( !CotiWorldHeat.IsFireProp( fireMetres, renderer.bounds.size.magnitude, bounds.Intersects( renderer.bounds ), renderer is ParticleSystemRenderer ) )
            continue;
          SetCotiHeat( renderer, CotiWorldHeat.FireCelsius );
          heated++;
        }
      }
      if( heated > 0 )
        Plugin.Log.LogInfo( $"Thermal: {heated} burning fire part(s) heated" );
    }

    /// <summary>
    /// lamps.json beside the plugin, read at the start of every raid so a change applies from the next one. Missing or
    /// unreadable, no lamp is heated.
    /// </summary>
    private static void LoadHeatedLamps()
    {
      var path = Path.Combine( Plugin.PluginDirectory, CotiLampFile.FileName );
      _heatedLamps = new HashSet<string>();
      try
      {
        if( !File.Exists( path ) )
        {
          Plugin.Log.LogWarning( $"[COTI] {path} is missing - no lamp shows heat" );
          return;
        }
        var heated = CotiLampFile.Parse( File.ReadAllText( path ), out var error );
        if( heated == null )
        {
          Plugin.Log.LogWarning( $"[COTI] {path} not read: {error} - no lamp shows heat" );
          return;
        }
        _heatedLamps = heated;
        Plugin.Log.LogInfo( $"Thermal: {heated.Count} lamp type(s) heated, from {path}" );
      }
      catch( System.Exception ex )
      {
        Plugin.Log.LogWarning( $"[COTI] Could not read {path}: {ex.Message} - no lamp shows heat" );
      }
    }

    /// <summary>
    /// Every listed lamp's state as heat, laid over its parts by its shape (CotiLampShapes): lit, its glow at 50 C with
    /// its other parts warm around each glowing spot and cold beyond; off, none. A lamp is written again only when it
    /// changes, lit or not, or once its shape is found. The glowing parts' values go into the block each keeps for itself
    /// (MaterialData._mpb), which its controller re-applies on every change of brightness, so a flicker carries them.
    /// Render Searchlight Heat off, every lamp is written as off.
    /// </summary>
    private static void HeatLamps()
    {
      var on = Plugin.Config.Image.RenderSearchlightHeat;
      foreach( var lamp in Lamps )
      {
        if( lamp == null )
          continue;
        var lit = on && CotiWorldHeat.IsLampLit( lamp.LampState.ToString() );
        var shape = lit ? CotiLampShapes.Of( lamp ) : CotiLampShapes.Known( lamp );
        var state = !lit ? 0 : shape != null ? 3 : 1;
        if( Written.TryGetValue( lamp, out var was ) && was == state )
          continue;
        Written[lamp] = state;
        HeatLamp( lamp, shape, lit ? CotiWorldHeat.LampCelsius : (float?)null );
      }
    }

    private static void HeatLamp( LampController lamp, CotiLampShapes.Shape shape, float? celsius )
    {
      var boxes = shape != null ? shape.Bulbs : NoBulbs;
      var count = shape != null ? shape.BulbCount : 0;
      // The housing is warm only in boxes: with none (the shape still being found, or no glow found) only the glow
      // itself shows, never the whole stand.
      var housing = count > 0 ? celsius : null;
      var glowSlots = new Dictionary<Renderer, HashSet<int>>();
      foreach( var part in CotiLampShapes.Parts( lamp ) )
        HeatPart( part, celsius, boxes, count, glowSlots );
      foreach( var pair in glowSlots )
        HeatOtherSlots( pair.Key, pair.Value, housing, boxes, count );
      if( shape == null )
        return;
      foreach( var renderer in shape.Housing )
        if( renderer != null )
          WriteEveryBlock( renderer, Boxed( renderer, housing, boxes, null, count, 0f, CotiLampHeat.HousingWarmth ) );
    }

    // A glowing part glows by COTI's say alone, whichever way EFT drives it (_EmissionVisibility, or colour on the older
    // lamps).
    private static void HeatPart( MaterialData part, float? celsius, Vector4[] boxes, int count, Dictionary<Renderer, HashSet<int>> glowSlots )
    {
      if( part == null || part.Renderer == null || part._mpb == null )
        return;
      Write( part._mpb, Boxed( part.Renderer, celsius, boxes, null, count, 1f, CotiLampHeat.HousingWarmth ) );
      part.Renderer.SetPropertyBlock( part._mpb );
      if( !glowSlots.TryGetValue( part.Renderer, out var slots ) )
        glowSlots[part.Renderer] = slots = new HashSet<int>();
      slots.Add( part.MaterialId );
    }

    /// <summary>
    /// A glowing renderer's other material slots (a hotel lamp's wood and brass beside its glass): the controller's block
    /// is the whole renderer's, so without this they would read as glowing too. Each gets a block of its own, copied
    /// from the renderer's, that never glows; it stays housing, warm within the boxes.
    /// </summary>
    private static void HeatOtherSlots( Renderer renderer, HashSet<int> glowSlots, float? celsius, Vector4[] boxes, int count )
    {
      var slots = renderer.sharedMaterials.Length;
      for( var i = 0; i < slots && slots > 1; i++ )
      {
        if( glowSlots.Contains( i ) )
          continue;
        Block.Clear();
        renderer.GetPropertyBlock( Block );
        Write( Block, Boxed( renderer, celsius, boxes, null, count, 0f, CotiLampHeat.HousingWarmth ) );
        renderer.SetPropertyBlock( Block, i );
      }
    }

    // What COTI writes on a surface: its temperature (none, null), the boxes its heat sits in (in the renderer's own
    // space) and how warm, whether it glows, and how much dimmer than everything else it draws (0, none). HeatOnly leaves
    // the rest alone, for bodies and fires.
    private struct HeatValues
    {
      internal float? Celsius;
      internal Vector4[] Bulbs;
      internal Vector4[] Axes;
      internal int Count;
      internal float Glow;
      internal float Housing;
      internal float Dim;
      internal bool HeatOnly;
    }

    // One renderer's values with world-space heat boxes taken into its own space (axes: a flashlight's beams; null, a
    // lamp's cubes). No heat, no boxes: the shader walks them for every pixel it draws.
    private static HeatValues Boxed( Renderer renderer, float? celsius, Vector4[] boxes, Vector3[] axes, int count, float glow, float housing )
    {
      return new HeatValues
      {
        Celsius = celsius,
        Bulbs = Bulbs,
        Axes = Axes,
        Count = celsius.HasValue ? LocalBoxes( renderer, boxes, axes, count ) : 0,
        Glow = glow,
        Housing = housing,
      };
    }

    /// <summary>
    /// World-space heat boxes into Bulbs and Axes in the renderer's own space, where the shader measures its vertices, so
    /// a box moves with what it is on. A static-batched mesh's vertices are already in world space, so its boxes stay
    /// there. Sizes scale with the renderer, by its largest axis.
    /// </summary>
    private static int LocalBoxes( Renderer renderer, Vector4[] boxes, Vector3[] axes, int count )
    {
      count = Mathf.Min( count, Bulbs.Length );
      if( renderer.isPartOfStaticBatch )
      {
        for( var i = 0; i < count; i++ )
        {
          Bulbs[i] = boxes[i];
          Axes[i] = axes != null ? (Vector4)axes[i] : Vector4.zero;
        }
        return count;
      }
      var space = renderer.transform;
      var scale = space.lossyScale;
      var largest = Mathf.Max( Mathf.Abs( scale.x ), Mathf.Max( Mathf.Abs( scale.y ), Mathf.Abs( scale.z ) ) );
      var toLocal = 1f / Mathf.Max( largest, 1e-4f );
      for( var i = 0; i < count; i++ )
      {
        var at = space.InverseTransformPoint( boxes[i] );
        Bulbs[i] = new Vector4( at.x, at.y, at.z, boxes[i].w * toLocal );
        Axes[i] = axes != null ? (Vector4)space.InverseTransformVector( axes[i] ).normalized : Vector4.zero;
      }
      return count;
    }

    private static void Write( MaterialPropertyBlock block, HeatValues values )
    {
      block.SetFloat( HeatTempId, CotiWorldHeat.BlockValue( values.Celsius ) );
      if( values.HeatOnly )
        return;
      block.SetVectorArray( BulbsId, values.Bulbs ?? NoBulbs );
      block.SetVectorArray( BulbAxesId, values.Axes ?? NoBulbs );
      block.SetFloat( BulbCountId, values.Count );
      block.SetFloat( GlowId, values.Glow );
      block.SetFloat( HousingId, values.Housing );
      block.SetFloat( DimId, values.Dim );
    }

    /// <summary>
    /// COTI's values into every block a material of the renderer reads: the renderer's own, which every material without
    /// a block of its own uses, each material's own where it has one (Unity draws a material with its own block alone;
    /// HotObject writes per material, and a fire barrel's coals carry one where its metal does not), and the block EFT's
    /// rain keeps on the player's own weapon and hands, which it re-applies every frame while it rains.
    /// </summary>
    private static void WriteEveryBlock( Renderer renderer, HeatValues values )
    {
      Block.Clear();
      renderer.GetPropertyBlock( Block );
      Write( Block, values );
      renderer.SetPropertyBlock( Block );

      var slots = renderer.sharedMaterials.Length;
      for( var i = 0; i < slots; i++ )
      {
        Block.Clear();
        renderer.GetPropertyBlock( Block, i );
        if( Block.isEmpty )
          continue;
        Write( Block, values );
        renderer.SetPropertyBlock( Block, i );
      }

      if( RainBlockField != null && renderer.TryGetComponent<RainCondensator>( out var rain ) &&
          RainBlockField.GetValue( rain ) is MaterialPropertyBlock rainBlock )
        Write( rainBlock, values );
    }

    /// <summary>
    /// Flashlights and weapon lights: while a device's visible light is on, its head reads a little warmer than the air
    /// (CotiWorldHeat.FlashlightWarmth) in a box from its lens back along the beam: warmest at the lens, fading to nothing
    /// at the box's back. Its tail, switch and mount stay cold, and so does every device that shows no visible light:
    /// lasers and infrared illuminators. The boxes are in each renderer's own space, so they move with the light between
    /// sweeps, and it draws at CotiWorldHeat.FlashlightBrightness. Render Flashlight Heat off, every device goes cold once
    /// and none is looked for until it is back on.
    /// </summary>
    private static void HeatDevices( float air, float now )
    {
      var on = Plugin.Config.Image.RenderFlashlightHeat;
      if( on && now >= _nextDeviceRefresh )
      {
        // Devices come and go with the players carrying them.
        _nextDeviceRefresh = now + DeviceRefreshSeconds;
        Devices.Clear();
        Devices.AddRange( Object.FindObjectsOfType<TacticalComboVisualController>() );
      }
      foreach( var device in Devices )
      {
        if( device == null )
          continue;
        var renderers = device.GetComponentsInChildren<Renderer>();
        var count = on && device.LightMod != null && device.LightMod.IsActive ? Lenses( device, Body( device, renderers ) ) : 0;
        var celsius = count > 0 ? air + CotiWorldHeat.FlashlightWarmth : (float?)null;
        foreach( var renderer in renderers )
        {
          if( renderer is ParticleSystemRenderer )
            continue;
          var values = Boxed( renderer, celsius, WorldBoxes, WorldAxes, count, 0f, 1f );
          values.Dim = 1f - CotiWorldHeat.FlashlightBrightness;
          WriteEveryBlock( renderer, values );
        }
      }
      if( on )
        return;
      Devices.Clear();
      _nextDeviceRefresh = 0f;
    }

    // What a device's body is drawn with: its LOD group's renderers (not a beam's glow or a sprite that sits in front of
    // the lens), or every renderer of a device with no LOD group.
    private static List<Renderer> Body( TacticalComboVisualController device, Renderer[] renderers )
    {
      var body = new List<Renderer>();
      var lods = device.GetComponent<LODGroup>();
      if( lods != null )
        foreach( var lod in lods.GetLODs() )
          foreach( var renderer in lod.renderers )
            if( renderer != null )
              body.Add( renderer );
      if( body.Count == 0 )
        foreach( var renderer in renderers )
          if( !( renderer is ParticleSystemRenderer ) )
            body.Add( renderer );
      return body;
    }

    /// <summary>
    /// A lit device's visible lights, each as the lens it shines from, into WorldBoxes and WorldAxes; returns how many.
    /// The lens is the light's own position carried forward along its beam to the front of the device's body: a
    /// flashlight's light sits wherever its artist put it, 11 cm back near the tail on the WF-501B. Visible light only: a
    /// light EFT draws for night vision alone (IkLight, infrared), a laser's (LaserBeam), and an indicator that lights
    /// nothing (range 0) give no heat.
    /// </summary>
    private static int Lenses( TacticalComboVisualController device, List<Renderer> body )
    {
      var infrared = new HashSet<Light>();
      foreach( var ik in device.GetComponentsInChildren<IkLight>( true ) )
        if( ik.Light != null )
          infrared.Add( ik.Light );
      var count = 0;
      foreach( var light in device.GetComponentsInChildren<Light>() )
      {
        if( count >= WorldBoxes.Length )
          break;
        if( light.type != LightType.Spot || light.range <= 0f || infrared.Contains( light ) || light.GetComponentInParent<LaserBeam>() != null )
          continue;
        var axis = light.transform.forward;
        var from = Vector3.Dot( light.transform.position, axis );
        var front = from;
        foreach( var renderer in body )
          front = Mathf.Max( front, FrontAlong( renderer, axis ) );
        var lens = light.transform.position + axis * ( front - from );
        WorldBoxes[count] = new Vector4( lens.x, lens.y, lens.z, CotiWorldHeat.FlashlightHeatMetres );
        WorldAxes[count++] = axis;
      }
      return count;
    }

    // How far along a direction a renderer's mesh reaches: the farthest corner of its own bounds, in the world.
    private static float FrontAlong( Renderer renderer, Vector3 axis )
    {
      var bounds = renderer.localBounds;
      var toWorld = renderer.localToWorldMatrix;
      var farthest = float.MinValue;
      for( var i = 0; i < 8; i++ )
      {
        var corner = bounds.center + Vector3.Scale( bounds.extents,
            new Vector3( ( i & 1 ) == 0 ? -1f : 1f, ( i & 2 ) == 0 ? -1f : 1f, ( i & 4 ) == 0 ? -1f : 1f ) );
        farthest = Mathf.Max( farthest, Vector3.Dot( toWorld.MultiplyPoint3x4( corner ), axis ) );
      }
      return farthest;
    }

    /// <summary>
    /// A weapon part warmer than its weapon's overheat says is a prefab's saved value that EFT never rewrote, because it
    /// rewrites parts only when overheat changes: put back to what the overheat says, as EFT itself would. Body skins
    /// are left alone.
    /// </summary>
    private static void RestStaleWeapons()
    {
      foreach( var hot in Object.FindObjectsOfType<HotObject>() )
      {
        if( hot.GetComponentInParent<LoddedSkin>() != null )
          continue;
        var weapon = hot.GetComponentInParent<WeaponPrefab>();
        var expected = CotiWorldHeat.WeaponCelsius( weapon != null ? weapon.CurrentOverheat : (float?)null );
        if( CotiWorldHeat.IsStaleWeaponHeat( hot.TemperatureCelsio, expected ) )
          hot.SetTemperatureToRenderer( expected, force: true );
      }
    }

    /// <summary>The weather's air temperature, which the thermal image reads every surface's contrast against.</summary>
    private static float AirCelsius()
    {
      var curve = EFT.Weather.WeatherController.Instance != null ? EFT.Weather.WeatherController.Instance.WeatherCurve : null;
      return curve != null ? curve.Temperature : FallbackAirCelsius;
    }

    /// <summary>
    /// Every living body's skin gets its temperature for the thermal image (CotiBodyHeat.Celsius): its own, or a
    /// cultist's just above the air. COTI's own block value, so EFT's state is untouched. EFT rewrites a skin's block
    /// when its temperature changes, which drops this until the next sweep half a second later.
    /// </summary>
    private static void MarkBodies( GameWorld world, float air )
    {
      foreach( var player in world.AllAlivePlayersList )
      {
        if( player == null )
          continue;
        var role = player.IsYourPlayer ? null : RoleOf( player );
        foreach( var skin in SkinOf( player ) )
          SetCotiHeat( skin.GetComponent<Renderer>(), CotiBodyHeat.Celsius( role, skin.TemperatureCelsio, air ) );
      }
    }

    // A player's skin HotObjects, found once: the body's parts are fixed for its life.
    private static List<HotObject> SkinOf( Player player )
    {
      if( Skins.TryGetValue( player, out var skins ) && !skins.Exists( s => s == null ) )
        return skins;
      skins = new List<HotObject>();
      foreach( var hot in player.GetComponentsInChildren<HotObject>( true ) )
        if( hot.GetComponentInParent<LoddedSkin>() != null && hot.GetComponent<Renderer>() != null )
          skins.Add( hot );
      Skins[player] = skins;
      return skins;
    }

    // A body's or a burning fire's temperature, into every block its materials read.
    private static void SetCotiHeat( Renderer renderer, float celsius )
    {
      if( renderer != null )
        WriteEveryBlock( renderer, new HeatValues { Celsius = celsius, HeatOnly = true } );
    }

    private static string RoleOf( Player player )
    {
      return player.Profile?.Info?.Settings?.Role.ToString();
    }

    private static float CelsiusOf( Player player, float air )
    {
      var health = player.HealthController;
      return CotiBodyHeat.Celsius( player.IsYourPlayer ? null : RoleOf( player ), health != null ? health.Temperature.Current : 36.6f, air );
    }

    /// <summary>
    /// For glass reflections: every living body within reach as 18 capsules along its bones, with its temperature, for
    /// the mirror sub-shader to cast its reflected rays against. Slim limbs, shoulders, neck, hands and feet, so the
    /// reflection reads as a person rather than a rounded outline. Off, the count is zeroed and nothing is gathered.
    /// </summary>
    private static void UploadCapsules( GameWorld world, bool on, float air )
    {
      var count = 0;
      var eye = Camera.main != null ? Camera.main.transform.position : Vector3.zero;
      if( on )
        foreach( var player in world.AllAlivePlayersList )
        {
          if( count + CapsulesPerBody > MaxCapsules )
            break;
          var bones = player != null ? player.PlayerBones : null;
          if( bones == null || ( player.Position - eye ).sqrMagnitude > ReflectedBodyMetres * ReflectedBodyMetres )
            continue;
          var celsius = CelsiusOf( player, air );
          var up = Vector3.up;
          var forward = player.Transform.forward;
          var head = bones.Head.position;
          var neck = bones.Neck.position;
          var ribcage = bones.Ribcage.position;
          var pelvis = bones.Pelvis.position;
          var ground = player.Position.y;

          Capsule( ref count, head + up * 0.02f, head + up * 0.09f, 0.095f, celsius );
          Capsule( ref count, neck, head, 0.055f, celsius );
          Capsule( ref count, neck - up * 0.05f, ribcage, 0.15f, celsius );
          Capsule( ref count, ribcage, pelvis, 0.13f, celsius );
          Capsule( ref count, bones.Upperarms[0].position, bones.Upperarms[1].position, 0.075f, celsius );
          Capsule( ref count, bones.LeftThigh1.position, bones.RightThigh1.position, 0.1f, celsius );
          Arm( ref count, bones.Upperarms[0].position, bones.Forearms[0].position, bones.LeftPalm.position, celsius );
          Arm( ref count, bones.Upperarms[1].position, bones.Forearms[1].position, bones.RightPalm.position, celsius );
          Leg( ref count, bones.LeftThigh1.position, bones.LeftThigh2.position, ground, forward, celsius );
          Leg( ref count, bones.RightThigh1.position, bones.RightThigh2.position, ground, forward, celsius );
        }

      if( count == 0 && _capsules == 0 )
        return;
      _capsules = count;
      Shader.SetGlobalVectorArray( CapsuleAId, CapsuleA );
      Shader.SetGlobalVectorArray( CapsuleBId, CapsuleB );
      Shader.SetGlobalFloat( CapsuleCountId, count );
    }

    // Upper arm, forearm, and the hand around the palm.
    private static void Arm( ref int count, Vector3 shoulder, Vector3 elbow, Vector3 palm, float celsius )
    {
      Capsule( ref count, shoulder, elbow, 0.055f, celsius );
      Capsule( ref count, elbow, palm, 0.045f, celsius );
      Capsule( ref count, palm, palm + ( palm - elbow ).normalized * 0.08f, 0.04f, celsius );
    }

    // Thigh, calf to the ankle, and the foot ahead of it.
    private static void Leg( ref int count, Vector3 hip, Vector3 knee, float ground, Vector3 forward, float celsius )
    {
      var ankle = new Vector3( knee.x, ground + 0.08f, knee.z );
      Capsule( ref count, hip, knee, 0.075f, celsius );
      Capsule( ref count, knee, ankle, 0.055f, celsius );
      Capsule( ref count, ankle, ankle + forward * 0.18f - Vector3.up * 0.03f, 0.045f, celsius );
    }

    private static void Capsule( ref int count, Vector3 a, Vector3 b, float radius, float celsius )
    {
      CapsuleA[count] = new Vector4( a.x, a.y, a.z, celsius );
      CapsuleB[count] = new Vector4( b.x, b.y, b.z, radius );
      count++;
    }
  }
}
