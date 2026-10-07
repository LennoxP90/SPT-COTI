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
  /// frame from the thermal camera's render setup; the scans run on timers, per raid, one timed job a frame.
  /// </summary>
  internal static class CotiThermalWorld
  {
    private const float SweepSeconds = 30f;
    private const float BodySweepSeconds = 0.5f;
    private const int BodyFrames = 4;
    private const float LampSweepSeconds = 2f;
    private const float FallbackAirCelsius = 15f;
    private const float ReflectedBodyMetres = 30f;
    private const int MaxDeviceLights = 32;

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
    private static readonly int CapsulesId = Shader.PropertyToID( "_CotiCapsules" );
    private static readonly int BodiesId = Shader.PropertyToID( "_CotiBodies" );
    private static readonly int BodyCountId = Shader.PropertyToID( "_CotiBodyCount" );
    private static readonly int AirId = Shader.PropertyToID( "_CotiAirCelsius" );

    private static readonly List<Material> Glass = new List<Material>();
    private static readonly HashSet<Material> MeshMaterials = new HashSet<Material>();
    private static readonly HashSet<int> SolidMaterials = new HashSet<int>();
    // Materials TagUntaggedMaterials has decided this raid, by instance id, each with the shader it was decided against.
    private static readonly Dictionary<int, int> DecidedMaterials = new Dictionary<int, int>();
    // Materials TagParticleOnly has decided for good this raid, by instance id.
    private static readonly HashSet<int> DecidedParticleMaterials = new HashSet<int>();
    // HotObjects that are a body's skin, which RestStaleWeapons leaves alone.
    private static readonly HashSet<int> SkinHotObjects = new HashSet<int>();
    private static readonly MaterialPropertyBlock Block = new MaterialPropertyBlock();
    private static readonly Vector4[] Capsules = new Vector4[CotiCapsuleBounds.MaxBodies * CotiCapsuleBounds.CapsulesPerBody * 2];
    private static readonly Vector4[] Bodies = new Vector4[CotiCapsuleBounds.MaxBodies];
    private static ComputeBuffer _capsuleBuffer, _bodyBuffer;
    private static readonly CotiSensorPacer CapsulePacer = new CotiSensorPacer();
    private static readonly Player[] Near = new Player[CotiCapsuleBounds.MaxBodies];
    private static readonly float[] NearDistance = new float[CotiCapsuleBounds.MaxBodies];
    private static readonly Dictionary<Player, Skin> Skins = new Dictionary<Player, Skin>();
    // A role's name, once per role: the shared body rule takes the name.
    private static readonly Dictionary<int, string> RoleNames = new Dictionary<int, string>();
    private static readonly List<LampController> Lamps = new List<LampController>();
    // Every lamp, fire, HotObject and weapon light, registered as it wakes (CotiWorldObjectPatches), so nothing is found by
    // scanning the scene: each FindObjectsByType walks every object in it, about 100 ms on Streets.
    private static readonly List<LampController> SpawnedLamps = new List<LampController>();
    private static readonly List<FlameDamageTrigger> SpawnedFires = new List<FlameDamageTrigger>();
    private static readonly List<HotObject> SpawnedHotObjects = new List<HotObject>();
    private static readonly List<TacticalComboVisualController> SpawnedDevices = new List<TacticalComboVisualController>();
    // Whether each lamp seen this raid is a listed type, by instance id: its name never changes.
    private static readonly Dictionary<int, bool> ListedLamps = new Dictionary<int, bool>();
    // Whether each lamp state is lit, once per state.
    private static readonly Dictionary<int, bool> LitStates = new Dictionary<int, bool>();
    // What each lamp was last written as: 0 off, 1 lit before its shape was found, 3 lit with it.
    private static readonly Dictionary<LampController, int> Written = new Dictionary<LampController, int>();
    // The lamp types lamps.json lists; only these are ever heated.
    private static HashSet<string> _heatedLamps = new HashSet<string>();
    private static Dictionary<int, Device> _devices = new Dictionary<int, Device>();
    private static Dictionary<int, Device> _foundDevices = new Dictionary<int, Device>();
    private static float _nextDeviceRefresh;

    // Scratch lists for component and material reads; each is filled and read before anything else fills it.
    private static readonly List<Material> Slots = new List<Material>();
    private static readonly List<Collider> Colliders = new List<Collider>();
    private static readonly List<Renderer> FoundRenderers = new List<Renderer>();
    private static readonly List<HotObject> FoundHotObjects = new List<HotObject>();
    private static readonly List<IkLight> FoundInfrared = new List<IkLight>();
    private static readonly List<Light> FoundLights = new List<Light>();

    // The 30 s sweep, one job a frame. After a world change the tagging has already run, so only the heat jobs follow.
    private static readonly System.Action[] SweepJobs = { FindLamps, HeatFires, RestStaleWeapons, TagParticleOnly, TagUntaggedMaterials };
    private const int HeatJobs = 3;
    private static int _sweepNext, _sweepEnd;

    private static GameWorld _world;
    private static CotiGlassMode _glassMode;
    private static bool _scopeGlassReflections;
    private static int _frame = -1;
    private static bool _firstFrame;
    private static int _bodies;
    // The bodies of the sweep under way, taken when it starts, so a death mid-sweep shifts nobody past the cursor.
    private static readonly List<Player> BodyCycle = new List<Player>();
    private static int _bodyCursor;
    // No timed job before this frame: a new raid's first frame tags surfaces and the next builds the terrain.
    private static int _resumeFrame;
    private static int _fireParts = -1;
    private static float _nextSweep;
    private static float _nextBodySweep;
    private static float _nextLampSweep;
    private static float _nextDeviceSweep;

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
      var began = world != _world;
      if( began )
        BeginWorld( world, glass );
      else if( glass != _glassMode || Plugin.Config.Image.ScopeGlassReflections != _scopeGlassReflections )
        TagGlass( glass );

      UploadCapsules( world, glass == CotiGlassMode.Reflections && CotiState.Active, air );

      // The first thermal frame of a world set up during deploy: every body heated before it is drawn, and the terrain,
      // which builds this frame, gets the frame to itself.
      var first = began || _firstFrame;
      if( _firstFrame && !began )
        _resumeFrame = Time.frameCount + 1;
      _firstFrame = false;

      var now = Time.realtimeSinceStartup;
      MarkBodies( world, air, now, all: first );
      if( Time.frameCount < _resumeFrame )
        return;

      // One timed job a frame: the timers fall due together after COTI has been off a while, and after a raid's first frame.
      if( _sweepNext < _sweepEnd )
        SweepJobs[_sweepNext++]();
      else if( now >= _nextLampSweep )
      {
        _nextLampSweep = now + LampSweepSeconds;
        HeatLamps();
      }
      else if( Plugin.Config.Image.RenderFlashlightHeat && now >= _nextDeviceRefresh )
      {
        // Devices come and go with the players carrying them.
        _nextDeviceRefresh = now + DeviceRefreshSeconds;
        FindDevices();
      }
      else if( now >= _nextDeviceSweep )
      {
        _nextDeviceSweep = now + LampSweepSeconds;
        HeatDevices( air );
      }
      else if( now >= _nextSweep )
      {
        _nextSweep = now + SweepSeconds;
        _sweepNext = 0;
        _sweepEnd = SweepJobs.Length;
        SweepJobs[_sweepNext++]();
      }
    }

    /// <summary>
    /// A new raid's first thermal frame. What decides how surfaces draw runs now, before the first picture: glass, solid
    /// surfaces, particle-only materials, and the bodies. The terrain builds on the next frame, and the heat on lamps,
    /// fires, weapons and devices follows from the frame after, one job a frame.
    /// </summary>
    private static void BeginWorld( GameWorld world, CotiGlassMode glass )
    {
      _world = world;
      Skins.Clear();
      Lamps.Clear();
      ListedLamps.Clear();
      Written.Clear();
      CotiLampShapes.Clear();
      LoadHeatedLamps();
      _devices.Clear();
      DecidedMaterials.Clear();
      DecidedParticleMaterials.Clear();
      SkinHotObjects.Clear();
      System.Array.Clear( Near, 0, Near.Length );
      _fireParts = -1;
      BodyCycle.Clear();
      _bodyCursor = 0;
      _resumeFrame = Time.frameCount + 2;
      _nextBodySweep = 0f;
      _nextLampSweep = 0f;
      _nextDeviceSweep = 0f;
      _nextDeviceRefresh = 0f;
      _nextSweep = Time.realtimeSinceStartup + SweepSeconds;
      _sweepNext = 0;
      _sweepEnd = HeatJobs;

      Prune( SpawnedHotObjects );
      Prune( SpawnedDevices );
      Prune( SpawnedLamps );
      Prune( SpawnedFires );
      _firstFrame = true;
      FindGlass();
      TagGlass( glass );
      TagParticleOnly();
      TagUntaggedMaterials();
    }

    /// <summary>
    /// Sets up a raid's world ahead of the first thermal frame, while the raid is still deploying: one frame, once a raid.
    /// True when it did the work this frame.
    /// </summary>
    internal static bool WarmUp()
    {
      var world = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null;
      if( world == null || world == _world || world is HideoutGameWorld )
        return false;
      BeginWorld( world, Plugin.Config.Image.Glass );
      return true;
    }

    /// <summary>Bots and loot load their materials around the start of play, so the material sweep runs straight after.</summary>
    internal static void OnGameStarted()
    {
      _nextSweep = 0f;
    }

    internal static void Register( HotObject hot )
    {
      Add( SpawnedHotObjects, hot, ref _hotPruneAt );
    }

    internal static void Register( TacticalComboVisualController device )
    {
      Add( SpawnedDevices, device, ref _devicePruneAt );
    }

    internal static void Register( LampController lamp )
    {
      Add( SpawnedLamps, lamp, ref _lampPruneAt );
    }

    internal static void Register( FlameDamageTrigger fire )
    {
      Add( SpawnedFires, fire, ref _firePruneAt );
    }

    private static int _hotPruneAt = MinPruneAt, _devicePruneAt = MinPruneAt, _lampPruneAt = MinPruneAt, _firePruneAt = MinPruneAt;
    private const int MinPruneAt = 4096;

    // Pruned whenever the list doubles, so raids played with COTI off never let destroyed objects pile up.
    private static void Add<T>( List<T> list, T item, ref int pruneAt ) where T : Object
    {
      list.Add( item );
      if( list.Count < pruneAt )
        return;
      Prune( list );
      pruneAt = System.Math.Max( MinPruneAt, list.Count * 2 );
    }

    // Drops destroyed entries, in place and out of order.
    private static void Prune<T>( List<T> list ) where T : Object
    {
      for( var i = list.Count - 1; i >= 0; i-- )
      {
        if( list[i] != null )
          continue;
        list[i] = list[list.Count - 1];
        list.RemoveAt( list.Count - 1 );
      }
    }

    /// <summary>
    /// Gives every loaded material in the opaque queues a RenderType the replacement draws, where its own is none or one
    /// the heat-only shader has no sub-shader for (CotiRenderTypeTag.For), so it hides the heat behind it. Swept again for
    /// materials loaded since: bots' gear and streamed scenes. A material in the opaque queues is decided once per shader:
    /// tagged, its tag is one the shader draws, and left alone, its own tag decides at any queue. One outside them is
    /// never tagged whatever its tag, so its tag is not read, and it is looked at again only in case its queue moves.
    /// </summary>
    private static void TagUntaggedMaterials()
    {
      var verbose = Plugin.Config != null && Plugin.Config.VerboseLogging;
      var started = verbose ? Time.realtimeSinceStartup : 0f;
      var materials = Resources.FindObjectsOfTypeAll<Material>();
      var tagged = 0;
      foreach( var material in materials )
      {
        if( material == null )
          continue;
        var queue = material.renderQueue;
        if( CotiRenderTypeTag.For( null, queue ) == null )
          continue;
        var shader = material.shader;
        if( shader == null )
          continue;
        var id = material.GetInstanceID();
        var shaderId = shader.GetInstanceID();
        if( DecidedMaterials.TryGetValue( id, out var decided ) && decided == shaderId )
          continue;
        DecidedMaterials[id] = shaderId;
        var tag = CotiRenderTypeTag.For( material.GetTag( "RenderType", false, "" ), queue, SolidMaterials.Contains( id ) );
        if( tag == null )
          continue;
        material.SetOverrideTag( "RenderType", tag );
        tagged++;
      }

      if( tagged > 0 && verbose )
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
      foreach( var renderer in Object.FindObjectsByType<Renderer>( FindObjectsSortMode.None ) )
      {
        if( renderer == null || !renderer.enabled )
          continue;
        renderer.GetSharedMaterials( Slots );
        var isParticles = renderer is ParticleSystemRenderer;
        if( !isParticles )
          foreach( var material in Slots )
            if( material != null )
              MeshMaterials.Add( material );
        // Collision is looked up only for renderers with a material it can decide: on a large map most renderers have
        // none, and the lookup is too slow to run on every renderer in one frame.
        if( isParticles || !AnyCandidate( Slots, candidates ) )
          continue;
        var onCollider = OnSolidCollider( renderer.transform );
        var carried = onCollider && ( renderer.GetComponentInParent<Player>() != null || renderer.GetComponentInParent<LootItem>() != null );
        if( onCollider && !carried )
          foreach( var material in Slots )
            if( material != null )
              SolidMaterials.Add( material.GetInstanceID() );
        if( !AnySeeThrough( Slots ) )
          continue;

        foreach( var material in Slots )
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
    /// gets a RenderType the heat-only shader never draws (CotiRenderTypeTag.ParticleOnly). Each material is decided once a
    /// raid: whether a mesh draws it is fixed once FindGlass has run.
    /// </summary>
    private static void TagParticleOnly()
    {
      foreach( var particles in Resources.FindObjectsOfTypeAll<ParticleSystemRenderer>() )
      {
        if( particles == null )
          continue;
        particles.GetSharedMaterials( Slots );
        foreach( var material in Slots )
          if( material != null && DecidedParticleMaterials.Add( material.GetInstanceID() )
              && CotiRenderTypeTag.IsParticleOnly( true, MeshMaterials.Contains( material ) ) )
            material.SetOverrideTag( "RenderType", CotiRenderTypeTag.ParticleOnly );
      }
    }

    // A material collision can decide: see-through (glass or not), or in the opaque queues with a tag the heat-only
    // shader has no sub-shader for. Cached per material, which most renderers share.
    private static bool AnyCandidate( List<Material> materials, Dictionary<Material, bool> candidates )
    {
      foreach( var material in materials )
      {
        if( material == null )
          continue;
        if( !candidates.TryGetValue( material, out var candidate ) )
        {
          var queue = material.renderQueue;
          candidate = queue > CotiRenderTypeTag.GeometryLast
                      || CotiRenderTypeTag.For( null, queue ) != null
                      && CotiRenderTypeTag.For( material.GetTag( "RenderType", false, "" ), queue, true ) != null;
          candidates[material] = candidate;
        }
        if( candidate )
          return true;
      }
      return false;
    }

    private static bool AnySeeThrough( List<Material> materials )
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
      {
        t.GetComponents( Colliders );
        foreach( var collider in Colliders )
          if( collider != null && collider.enabled && !collider.isTrigger )
            return true;
      }
      return false;
    }

    private static void TagGlass( CotiGlassMode mode )
    {
      _glassMode = mode;
      var tag = CotiRenderTypeTag.GlassTag( mode );
      foreach( var material in Glass )
        if( material != null )
          material.SetOverrideTag( "RenderType", tag );
      _scopeGlassReflections = Plugin.Config.Image.ScopeGlassReflections;
      CotiScopeGlassTagger.Retag( CotiScopeGlass.Mode( mode, _scopeGlassReflections ), Glass );
    }

    // The listed lamps loaded now. A lamp's type is read from its name once a raid.
    private static void FindLamps()
    {
      Prune( SpawnedLamps );
      Lamps.Clear();
      foreach( var lamp in SpawnedLamps )
      {
        if( !lamp.gameObject.activeInHierarchy )
          continue;
        var id = lamp.GetInstanceID();
        if( !ListedLamps.TryGetValue( id, out var listed ) )
          ListedLamps[id] = listed = _heatedLamps.Contains( CotiLampHeat.TypeOf( lamp.name ) );
        if( listed )
          Lamps.Add( lamp );
      }
    }

    /// <summary>
    /// Every burning fire burns a player who stands in it through a FlameDamageTrigger, live only while the fire is lit.
    /// The renderers of the fire's own prop inside that volume (CotiWorldHeat.IsFireProp) get a COTI-only block value,
    /// which EFT's own shaders never read, so a lit barrel or bonfire draws hot and an unlit one does not.
    /// </summary>
    private static void HeatFires()
    {
      var parts = 0;
      Prune( SpawnedFires );
      foreach( var fire in SpawnedFires )
      {
        if( !fire.gameObject.activeInHierarchy )
          continue;
        var volume = fire.GetComponent<Collider>();
        if( volume == null || !volume.enabled )
          continue;
        var bounds = volume.bounds;
        var fireMetres = bounds.size.magnitude;
        bounds.Expand( 1f );

        // The bounds and size tests keep anything else under the prop out.
        FirePropRoot( fire.transform ).GetComponentsInChildren( FoundRenderers );
        foreach( var renderer in FoundRenderers )
        {
          if( renderer.shadowCastingMode == ShadowCastingMode.ShadowsOnly )
            continue;
          if( !CotiWorldHeat.IsFireProp( fireMetres, renderer.bounds.size.magnitude, bounds.Intersects( renderer.bounds ), renderer is ParticleSystemRenderer ) )
            continue;
          SetCotiHeat( renderer, CotiWorldHeat.FireCelsius );
          parts++;
        }
      }
      if( parts == _fireParts )
        return;
      _fireParts = parts;
      if( parts > 0 && Plugin.Config != null && Plugin.Config.VerboseLogging )
        Plugin.Log.LogInfo( $"Thermal: {parts} burning fire part(s) heated" );
    }

    // The fire's prop: Barrel_fire_on/Small_Fire_no_Light/<trigger>, or the trigger's parent when it sits directly on the prop.
    private static Transform FirePropRoot( Transform trigger )
    {
      var parent = trigger.parent;
      return parent != null && parent.parent != null ? parent.parent : parent != null ? parent : trigger;
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
        var lit = on && IsLit( lamp );
        var shape = lit ? CotiLampShapes.Of( lamp ) : CotiLampShapes.Known( lamp );
        var state = !lit ? 0 : shape != null ? 3 : 1;
        if( Written.TryGetValue( lamp, out var was ) && was == state )
          continue;
        Written[lamp] = state;
        HeatLamp( lamp, shape, lit ? CotiWorldHeat.LampCelsius : (float?)null );
      }
    }

    private static bool IsLit( LampController lamp )
    {
      var state = lamp.LampState;
      if( !LitStates.TryGetValue( (int)state, out var lit ) )
        LitStates[(int)state] = lit = CotiWorldHeat.IsLampLit( state.ToString() );
      return lit;
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
          WriteEveryBlock( renderer, Boxed( renderer, housing, boxes, null, count, 0f, CotiLampHeat.HousingWarmth ), onlyStale: false );
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
      renderer.GetSharedMaterials( Slots );
      var slots = Slots.Count;
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
    /// rain keeps on the player's own weapon and hands, which it re-applies every frame while it rains. onlyStale: only the
    /// blocks that no longer hold them, which is how a block EFT has replaced gets COTI's values back without the
    /// unchanged ones being written again.
    /// </summary>
    private static void WriteEveryBlock( Renderer renderer, HeatValues values, bool onlyStale )
    {
      Block.Clear();
      renderer.GetPropertyBlock( Block );
      if( !onlyStale || !Holds( Block, values ) )
      {
        Write( Block, values );
        renderer.SetPropertyBlock( Block );
      }

      renderer.GetSharedMaterials( Slots );
      var slots = Slots.Count;
      for( var i = 0; i < slots; i++ )
      {
        Block.Clear();
        renderer.GetPropertyBlock( Block, i );
        if( Block.isEmpty || onlyStale && Holds( Block, values ) )
          continue;
        Write( Block, values );
        renderer.SetPropertyBlock( Block, i );
      }

      var rain = RainBlockOf( renderer );
      if( rain != null && ( !onlyStale || !Holds( rain, values ) ) )
        Write( rain, values );
    }

    // Whether a block still holds these values. EFT replaces a material's whole block when its own temperature changes
    // (HotObject), dropping COTI's: its heat reads 0 and its dimming 0, so the heat (and, past HeatOnly, the dimming and
    // box count) find a replaced block without reading the boxes.
    private static bool Holds( MaterialPropertyBlock block, HeatValues values )
    {
      return CotiHeatRewrite.Holds( block.GetFloat( HeatTempId ), CotiWorldHeat.BlockValue( values.Celsius ) )
             && ( values.HeatOnly || block.GetFloat( DimId ) == values.Dim && block.GetFloat( BulbCountId ) == values.Count );
    }

    private static MaterialPropertyBlock RainBlockOf( Renderer renderer )
    {
      return RainBlockField != null && renderer.TryGetComponent<RainCondensator>( out var rain )
          ? RainBlockField.GetValue( rain ) as MaterialPropertyBlock
          : null;
    }

    // A weapon light's parts, found once: its hierarchy is fixed while it exists. No vanilla tactical device has a slot
    // (items.json, 4.1), so nothing is attached under one mid-raid.
    private sealed class Device
    {
      internal readonly TacticalComboVisualController Controller;
      // Every renderer but particle systems, shown or not, so one shown later already holds the values.
      internal readonly List<Renderer> Renderers = new List<Renderer>();
      // Its LOD group's renderers, which its body is drawn with.
      internal readonly List<Renderer> Lods = new List<Renderer>();
      // Its spot lights that are neither infrared nor a laser's, in hierarchy order, one bit each in a written mask.
      internal readonly List<Light> Lights = new List<Light>();
      internal int WrittenLights = -1;
      internal float WrittenHeat;

      internal Device( TacticalComboVisualController controller )
      {
        Controller = controller;
        controller.GetComponentsInChildren( true, FoundRenderers );
        foreach( var renderer in FoundRenderers )
          if( !( renderer is ParticleSystemRenderer ) )
            Renderers.Add( renderer );
        var lods = controller.GetComponent<LODGroup>();
        if( lods != null )
          foreach( var lod in lods.GetLODs() )
            foreach( var renderer in lod.renderers )
              if( renderer != null )
                Lods.Add( renderer );
        controller.GetComponentsInChildren( true, FoundInfrared );
        controller.GetComponentsInChildren( true, FoundLights );
        foreach( var light in FoundLights )
          if( Lights.Count < MaxDeviceLights && light.type == LightType.Spot && !IsInfrared( light )
              && light.GetComponentInParent<LaserBeam>( true ) == null )
            Lights.Add( light );
      }

      private static bool IsInfrared( Light light )
      {
        foreach( var ik in FoundInfrared )
          if( ik.Light == light )
            return true;
        return false;
      }
    }

    /// <summary>
    /// Flashlights and weapon lights: while a device's visible light is on, its head reads a little warmer than the air
    /// (CotiWorldHeat.FlashlightWarmth) in a box from its lens back along the beam: warmest at the lens, fading to nothing
    /// at the box's back. Its tail, switch and mount stay cold, and so does every device that shows no visible light:
    /// lasers and infrared illuminators. The boxes are in each renderer's own space, so they move with the light between
    /// sweeps, and it draws at CotiWorldHeat.FlashlightBrightness. A device is written in full when its lit lights or its
    /// heat change; otherwise only a block EFT has replaced is written again. Render Flashlight Heat off, every device goes
    /// cold once and none is looked for until it is back on.
    /// </summary>
    private static void HeatDevices( float air )
    {
      var on = Plugin.Config.Image.RenderFlashlightHeat;
      var dim = 1f - CotiWorldHeat.FlashlightBrightness;
      foreach( var device in _devices.Values )
      {
        if( device.Controller == null )
          continue;
        var count = 0;
        var lit = on && device.Controller.LightMod != null && device.Controller.LightMod.IsActive ? LitLights( device, out count ) : 0;
        var celsius = count > 0 ? air + CotiWorldHeat.FlashlightWarmth : (float?)null;
        var heat = CotiWorldHeat.BlockValue( celsius );
        var changed = lit != device.WrittenLights || !CotiHeatRewrite.Holds( device.WrittenHeat, heat );
        if( changed )
        {
          device.WrittenLights = lit;
          device.WrittenHeat = heat;
        }
        if( lit != 0 )
          Lenses( device, lit );
        foreach( var renderer in device.Renderers )
        {
          // Unchanged, a hidden part waits until it shows; changed, it is written too, so it shows right.
          if( renderer == null || !changed && !renderer.gameObject.activeInHierarchy )
            continue;
          var values = Boxed( renderer, celsius, WorldBoxes, WorldAxes, count, 0f, 1f );
          values.Dim = dim;
          WriteEveryBlock( renderer, values, onlyStale: !changed );
        }
      }
      if( on )
        return;
      _devices.Clear();
      _nextDeviceRefresh = 0f;
    }

    // The devices loaded now, each keeping the parts found when it was first seen.
    private static void FindDevices()
    {
      _foundDevices.Clear();
      Prune( SpawnedDevices );
      foreach( var controller in SpawnedDevices )
      {
        if( !controller.gameObject.activeInHierarchy )
          continue;
        var id = controller.GetInstanceID();
        _foundDevices[id] = _devices.TryGetValue( id, out var device ) ? device : new Device( controller );
      }
      var was = _devices;
      _devices = _foundDevices;
      _foundDevices = was;
    }

    /// <summary>
    /// The device's visible lights shining now, a bit each, and how many (at most one per heat box). Visible light only: a
    /// light EFT draws for night vision alone (IkLight, infrared), a laser's (LaserBeam), and an indicator that lights
    /// nothing (range 0) give no heat.
    /// </summary>
    private static int LitLights( Device device, out int count )
    {
      var lit = 0;
      count = 0;
      for( var i = 0; i < device.Lights.Count && count < WorldBoxes.Length; i++ )
      {
        var light = device.Lights[i];
        if( light == null || !light.gameObject.activeInHierarchy || light.range <= 0f )
          continue;
        lit |= 1 << i;
        count++;
      }
      return lit;
    }

    /// <summary>
    /// A lit device's lights, each as the lens it shines from, into WorldBoxes and WorldAxes. The lens is the light's own
    /// position carried forward along its beam to the front of the device's body: a flashlight's light sits wherever its
    /// artist put it, 11 cm back near the tail on the WF-501B.
    /// </summary>
    private static void Lenses( Device device, int lit )
    {
      var count = 0;
      for( var i = 0; i < device.Lights.Count; i++ )
      {
        if( ( lit & ( 1 << i ) ) == 0 )
          continue;
        var light = device.Lights[i];
        var axis = light.transform.forward;
        var from = Vector3.Dot( light.transform.position, axis );
        var front = FrontOf( device, axis, from );
        var lens = light.transform.position + axis * ( front - from );
        WorldBoxes[count] = new Vector4( lens.x, lens.y, lens.z, CotiWorldHeat.FlashlightHeatMetres );
        WorldAxes[count++] = axis;
      }
    }

    // How far along a direction the device's body reaches: its LOD group's renderers (not a beam's glow or a sprite that
    // sits in front of the lens), or every shown renderer of a device with no LOD group.
    private static float FrontOf( Device device, Vector3 axis, float front )
    {
      var any = false;
      foreach( var renderer in device.Lods )
        if( renderer != null )
        {
          any = true;
          front = Mathf.Max( front, FrontAlong( renderer, axis ) );
        }
      if( !any )
        foreach( var renderer in device.Renderers )
          if( renderer != null && renderer.gameObject.activeInHierarchy )
            front = Mathf.Max( front, FrontAlong( renderer, axis ) );
      return front;
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
    /// are left alone, and once known as skins are not looked at again.
    /// </summary>
    private static void RestStaleWeapons()
    {
      Prune( SpawnedHotObjects );
      foreach( var hot in SpawnedHotObjects )
      {
        if( !hot.gameObject.activeInHierarchy )
          continue;
        var id = hot.GetInstanceID();
        if( SkinHotObjects.Contains( id ) )
          continue;
        if( hot.GetComponentInParent<LoddedSkin>() != null )
        {
          SkinHotObjects.Add( id );
          continue;
        }
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

    // A player's skin HotObjects and their renderers, found once: the body's parts are fixed for its life.
    private sealed class Skin
    {
      internal readonly List<HotObject> HotObjects = new List<HotObject>();
      internal readonly List<Renderer> Renderers = new List<Renderer>();

      internal bool Whole()
      {
        for( var i = 0; i < HotObjects.Count; i++ )
          if( HotObjects[i] == null || Renderers[i] == null )
            return false;
        return true;
      }
    }

    /// <summary>
    /// Every living body's skin gets its temperature for the thermal image (CotiBodyHeat.Celsius): its own, or a
    /// cultist's just above the air. COTI's own block value, so EFT's state is untouched. EFT rewrites a skin's block
    /// when its temperature changes, which drops this until the next sweep half a second later. Every body once a sweep,
    /// a share of them a frame over BodyFrames frames, so a full map's bodies never land on one frame; all of them at once
    /// on a raid's first frame, so none is first drawn without COTI's heat.
    /// </summary>
    private static void MarkBodies( GameWorld world, float air, float now, bool all )
    {
      // A cycle left unfinished for a whole period (COTI switched off part way) restarts from the bodies alive now.
      if( _bodyCursor >= BodyCycle.Count || now >= _nextBodySweep + BodySweepSeconds )
      {
        if( now < _nextBodySweep )
          return;
        _nextBodySweep = now + BodySweepSeconds;
        BodyCycle.Clear();
        BodyCycle.AddRange( world.AllAlivePlayersList );
        _bodyCursor = 0;
      }
      var share = all ? BodyCycle.Count : ( BodyCycle.Count + BodyFrames - 1 ) / BodyFrames;
      var end = Mathf.Min( BodyCycle.Count, _bodyCursor + share );
      // The cursor moves before each body, so one that throws costs a single frame instead of stalling every job after it.
      while( _bodyCursor < end )
        MarkBody( BodyCycle[_bodyCursor++], air );
    }

    private static void MarkBody( Player player, float air )
    {
      if( player == null )
        return;
      var role = player.IsYourPlayer ? null : RoleOf( player );
      var skin = SkinOf( player );
      for( var i = 0; i < skin.HotObjects.Count; i++ )
        SetCotiHeat( skin.Renderers[i], CotiBodyHeat.Celsius( role, skin.HotObjects[i].TemperatureCelsio, air ) );
    }

    private static Skin SkinOf( Player player )
    {
      if( Skins.TryGetValue( player, out var skin ) && skin.Whole() )
        return skin;
      skin = new Skin();
      player.GetComponentsInChildren( true, FoundHotObjects );
      foreach( var hot in FoundHotObjects )
      {
        if( hot.GetComponentInParent<LoddedSkin>() == null )
          continue;
        var renderer = hot.GetComponent<Renderer>();
        if( renderer == null )
          continue;
        skin.HotObjects.Add( hot );
        skin.Renderers.Add( renderer );
      }
      Skins[player] = skin;
      return skin;
    }

    // A body's or a burning fire's temperature, into every block its materials read that does not already hold it.
    private static void SetCotiHeat( Renderer renderer, float celsius )
    {
      if( renderer != null )
        WriteEveryBlock( renderer, new HeatValues { Celsius = celsius, HeatOnly = true }, onlyStale: true );
    }

    private static string RoleOf( Player player )
    {
      var settings = player.Profile?.Info?.Settings;
      if( settings == null )
        return null;
      var role = settings.Role;
      if( !RoleNames.TryGetValue( (int)role, out var name ) )
        RoleNames[(int)role] = name = role.ToString();
      return name;
    }

    private static float CelsiusOf( Player player, float air )
    {
      var health = player.HealthController;
      return CotiBodyHeat.Celsius( player.IsYourPlayer ? null : RoleOf( player ), health != null ? health.Temperature.Current : 36.6f, air );
    }

    /// <summary>
    /// For glass reflections: the nearest living bodies within reach (up to CotiCapsuleBounds.MaxBodies), each as
    /// CotiBodyCapsules' capsules with its temperature and a bounding sphere, in two GPU buffers the mirror sub-shader
    /// casts its reflected rays against. Off (Glass Plain, or no COTI rendering), the count is zeroed and nothing is
    /// gathered.
    /// </summary>
    private static void UploadCapsules( GameWorld world, bool on, float air )
    {
      EnsureCapsuleBuffers();
      // At the sensor's rate, like the thermal camera that reads them: rebuilding on frames it does not render is waste.
      if( on && _bodies > 0 && !CapsulePacer.Due( Time.realtimeSinceStartupAsDouble, Plugin.Config.ThermalCamera?.Hz ?? 0 ) )
        return;
      var bodies = on ? Nearest( world ) : 0;
      for( var b = 0; b < bodies; b++ )
        Bodies[b] = CotiBodyCapsules.Write( Near[b], Near[b].PlayerBones, CelsiusOf( Near[b], air ), Capsules, b * CotiCapsuleBounds.CapsulesPerBody );

      if( bodies == 0 && _bodies == 0 )
        return;
      _bodies = bodies;
      if( bodies > 0 )
      {
        _capsuleBuffer.SetData( Capsules, 0, 0, bodies * CotiCapsuleBounds.CapsulesPerBody * 2 );
        _bodyBuffer.SetData( Bodies, 0, 0, bodies );
      }
      Shader.SetGlobalFloat( BodyCountId, bodies );
    }

    // The nearest living bodies within reach, closest first, into Near; an insertion sort over a fixed array, no garbage.
    // The rest of Near is cleared, so it holds no player it no longer reflects.
    private static int Nearest( GameWorld world )
    {
      var count = 0;
      var eye = CotiFrame.Main != null ? CotiFrame.Main.transform.position : Vector3.zero;
      foreach( var player in world.AllAlivePlayersList )
      {
        if( player == null || player.PlayerBones == null )
          continue;
        var d = ( player.Position - eye ).sqrMagnitude;
        if( d > ReflectedBodyMetres * ReflectedBodyMetres )
          continue;
        if( count == CotiCapsuleBounds.MaxBodies && d >= NearDistance[count - 1] )
          continue;
        var i = count < CotiCapsuleBounds.MaxBodies ? count++ : count - 1;
        for( ; i > 0 && NearDistance[i - 1] > d; i-- )
        {
          Near[i] = Near[i - 1];
          NearDistance[i] = NearDistance[i - 1];
        }
        Near[i] = player;
        NearDistance[i] = d;
      }
      System.Array.Clear( Near, count, Near.Length - count );
      return count;
    }

    // Made once and bound globally for good: the mirror shader reads them whenever glass draws, Plain or not. Released
    // when the game quits, so the GPU memory is not left to the finalizer.
    private static void EnsureCapsuleBuffers()
    {
      if( _capsuleBuffer != null )
        return;
      _capsuleBuffer = new ComputeBuffer( Capsules.Length, 16 );
      _bodyBuffer = new ComputeBuffer( Bodies.Length, 16 );
      Shader.SetGlobalBuffer( CapsulesId, _capsuleBuffer );
      Shader.SetGlobalBuffer( BodiesId, _bodyBuffer );
      Application.quitting += ReleaseCapsuleBuffers;
    }

    private static void ReleaseCapsuleBuffers()
    {
      Application.quitting -= ReleaseCapsuleBuffers;
      _capsuleBuffer?.Release();
      _bodyBuffer?.Release();
      _capsuleBuffer = null;
      _bodyBuffer = null;
    }
  }
}
