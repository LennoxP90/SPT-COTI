using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using EFT.CameraControl;
using HarmonyLib;
using UnityEngine;
#if SPT41
using EFT;
using EFT.InventoryLogic;
using EFT.Utilities;
#endif

namespace Coti.Client
{
  /// <summary>
  /// Everything that differs between the SPT 4.0 and 4.1 game builds.
  ///
  /// Most types keep their real names on both, but Player.ToggleGoggles becomes method_15 and
  /// NightVision.CurrentColor becomes Color_0, so both are matched by shape - a local-variable type
  /// for the method, a return type for the property. ToggleGoggles is still PUBLIC on 4.0 despite
  /// the rename, so do not filter on visibility.
  ///
  /// Shape-based binding fails QUIETLY: it returns a plausible member, the patch applies, and
  /// nothing works. Verify a new lookup against the real assembly rather than the compiler's error
  /// list, which stops naming types once an earlier name in the same statement has failed.
  /// </summary>
  internal static class EftCompat
  {
#if SPT40
    private static readonly Assembly Game = typeof( EFT.InventoryLogic.Item ).Assembly;

    /// <summary>
    /// Assembly-CSharp declares types that outright fail to load - confirmed by metadata
    /// dump, some of its own delegate types are missing the sealed flag a delegate must
    /// have, which the type loader rejects. GetTypes() throws for the WHOLE assembly the
    /// moment any one member fails, so every lookup here needs the types that did load,
    /// not a bare GetTypes() call.
    /// </summary>
    private static Type[] _gameTypes;

    private static Type[] GameTypes()
    {
      if( _gameTypes != null )
        return _gameTypes;

      try
      {
        return _gameTypes = Game.GetTypes();
      }
      catch( ReflectionTypeLoadException ex )
      {
        return _gameTypes = ex.Types.Where( t => t != null ).ToArray();
      }
    }

    /// <summary>
    /// Declaring type per searched member name, resolved once.
    ///
    /// Scans all types and demands exactly one match: metadata order is not preserved across game
    /// updates, so a second declaring type would otherwise change which one gets patched.
    /// </summary>
    private static readonly Dictionary<string, Type> DeclaringTypes = new Dictionary<string, Type>();

    private static Type TypeDeclaring( string methodName )
    {
      Type cached;
      if( DeclaringTypes.TryGetValue( methodName, out cached ) )
        return cached;

      var found = ScanForTypeDeclaring( methodName );
      DeclaringTypes[methodName] = found;
      return found;
    }

    private static Type ScanForTypeDeclaring( string methodName )
    {
      var hits = new List<Type>();

      foreach( var t in GameTypes() )
      {
        try
        {
          if( t.GetMethod( methodName, AccessTools.all | BindingFlags.DeclaredOnly ) != null )
            hits.Add( t );
        }
        catch( AmbiguousMatchException )
        {
          // Two overloads declared on this type. Still this type, and the caller picks the
          // overload - but uncaught it would take the whole patch down.
          hits.Add( t );
        }
        catch( TypeLoadException )
        {
          // Assembly-CSharp declares types that fail to load; see GameTypes.
        }
      }

      if( hits.Count == 1 )
        return hits[0];

      if( hits.Count == 0 )
        throw new InvalidOperationException( $"[COTI] no type in Assembly-CSharp declares {methodName}" );

      throw new InvalidOperationException(
          $"[COTI] {hits.Count} types in Assembly-CSharp declare {methodName} " +
          $"({string.Join( ", ", hits.Select( t => t.FullName ).ToArray() )}) - cannot tell which one is meant" );
    }

    internal static Type IconCreatorType => TypeDeclaring( "GetItemIcon" );
    private static Type ObjectsFactoryType => TypeDeclaring( "CreateItemAsync" );

    internal static MethodBase GetItemIconMethod()
    {
      return AccessTools.Method( IconCreatorType, "GetItemIcon" );
    }

    internal static MethodBase CreateItemAsyncMethod()
    {
      return AccessTools.Method( ObjectsFactoryType, "CreateItemAsync" );
    }

    /// <summary>
    /// 4.0's AttachMods is a private async method whose own name is a PUA character, so it
    /// is matched on parameter-name shape instead. Parameter names are Param table entries
    /// and survive release builds; the obfuscation map does not.
    /// </summary>
    private static readonly string[] AttachModsParams =
        { "containerCollection", "collectionView", "cameraType", "player", "isAnimated", "ct", "yield" };

    internal static MethodBase AttachModsMethod()
    {
      var hit = ObjectsFactoryType
                .GetMethods( AccessTools.all )
                .FirstOrDefault( m =>
                {
                  var p = m.GetParameters();
                  return p.Length == AttachModsParams.Length
                      && p.Select( x => x.Name ).SequenceEqual( AttachModsParams );
                } );

      if( hit == null )
        throw new InvalidOperationException(
            "[COTI] no method on the objects factory matches the AttachMods parameter shape" );
      return hit;
    }

    /// <summary>
    /// The nested SlotView class that declares InsertItem is itself PUA-named on 4.0 - confirmed
    /// by reflection probe, same as the other PUA types. InsertItem itself keeps its own name.
    /// </summary>
    internal static MethodBase InsertItemMethod()
    {
      return AccessTools.Method( TypeDeclaring( "InsertItem" ), "InsertItem" );
    }

    /// <summary>
    /// _fileCacheIndex and _memoryCacheIndex are PRIVATE fields on the obfuscated
    /// icon-creator base type on 4.0 (public on 4.1), so unlike every other member this
    /// class resolves, their names are wiped along with the type names - verified directly
    /// against Assembly-CSharp.dll, where both fields report an empty Name. They are found
    /// by shape instead: exactly two Dictionary&lt;int, X&gt; fields exist on that base
    /// type, and X is either int (file cache: hash -> file id) or the icon type itself
    /// (memory cache: hash -> icon). Private inherited fields are invisible through the
    /// derived type in reflection, so the base chain is walked one level at a time.
    /// </summary>
    private static Type _iconCacheOwner;
    private static FieldInfo _fileCacheField;
    private static FieldInfo _memoryCacheField;

    /// <summary>
    /// Keyed on the concrete type, and demands exactly one field of each shape.
    ///
    /// The cache has to be per-type: a FieldInfo resolved from one icon creator is meaningless
    /// against an instance of another, and GetValue would either throw or read the wrong object.
    /// The old "already resolved, return" check ignored which type produced them.
    ///
    /// Uniqueness matters for the same reason it does on the resources cache: the fields are
    /// identified by SHAPE, and last-wins on a shape match is a coin toss dressed up as a lookup.
    /// </summary>
    private static void ResolveIconCacheFields( Type concrete )
    {
      if( _iconCacheOwner == concrete )
        return;

      var fileFields = new List<FieldInfo>();
      var memoryFields = new List<FieldInfo>();

      for( var t = concrete; t != null && t != typeof( object ); t = t.BaseType )
      {
        foreach( var field in t.GetFields( AccessTools.all | BindingFlags.DeclaredOnly ) )
        {
          if( !field.FieldType.IsGenericType || field.FieldType.GetGenericTypeDefinition() != typeof( Dictionary<,> ) )
            continue;

          var args = field.FieldType.GetGenericArguments();
          if( args[0] != typeof( int ) )
            continue;

          ( args[1] == typeof( int ) ? fileFields : memoryFields ).Add( field );
        }
      }

      // Both resolved before either is committed, so a failure cannot leave one field from this
      // type sitting beside one from the last.
      var file = ExactlyOne( fileFields, "Dictionary<int,int> icon file-cache index", concrete );
      var memory = ExactlyOne( memoryFields, "Dictionary<int,TIcon> icon memory-cache index", concrete );

      _fileCacheField = file;
      _memoryCacheField = memory;
      _iconCacheOwner = concrete;
    }

    private static FieldInfo ExactlyOne( List<FieldInfo> found, string what, Type owner )
    {
      if( found.Count == 1 )
        return found[0];

      throw new InvalidOperationException(
          $"[COTI] expected exactly one {what} on {owner.FullName}, found {found.Count}" );
    }
#else
    internal static Type IconCreatorType => typeof( ItemIconCreator );

    internal static MethodBase GetItemIconMethod()
    {
      return AccessTools.Method( typeof( ItemIconCreator ), nameof( ItemIconCreator.GetItemIcon ) );
    }

    internal static MethodBase CreateItemAsyncMethod()
    {
      return AccessTools.Method( typeof( ObjectsFactory ), nameof( ObjectsFactory.CreateItemAsync ) );
    }

    internal static MethodBase AttachModsMethod()
    {
      return AccessTools.Method( typeof( ObjectsFactory ), nameof( ObjectsFactory.AttachMods ) );
    }

    internal static MethodBase InsertItemMethod()
    {
      // Nested type, so it cannot be named directly - AccessTools.Inner finds SlotView inside
      // ContainerCollectionView.
      var slotView = AccessTools.Inner( typeof( ContainerCollectionView ), "SlotView" );
      return AccessTools.Method( slotView, "InsertItem" );
    }
#endif

#if SPT40
    internal static (bool file, bool memory) RemoveFromIconCaches( object iconCreator, int hash )
    {
      ResolveIconCacheFields( iconCreator.GetType() );

      var file = RemoveFromDictionary( _fileCacheField, iconCreator, hash );
      var memory = RemoveFromDictionary( _memoryCacheField, iconCreator, hash );
      return ( file, memory );
    }

    private static bool RemoveFromDictionary( FieldInfo field, object owner, int hash )
    {
      var dict = field.GetValue( owner );
      if( dict == null )
        return false;

      var remove = AccessTools.Method( dict.GetType(), "Remove", new[] { typeof( int ) } );
      if( remove == null )
        throw new InvalidOperationException( $"[COTI] {dict.GetType()} has no Remove(int) method" );
      return (bool)remove.Invoke( dict, new object[] { hash } );
    }

    /// <summary>
    /// TemplateId is inherited from the (unobfuscated) Item base class, so it resolves by
    /// name - but it is declared as MongoID, not string, and a reflected MongoID boxes as
    /// itself rather than as the string 'as' expects to see. ToString() is exactly what
    /// MongoID's own implicit-to-string operator calls, so this matches the 4.1 branch's
    /// value, which gets that conversion for free at compile time.
    /// </summary>
    internal static string ContainerTemplateId( object containerCollection )
    {
      var prop = AccessTools.Property( containerCollection.GetType(), "TemplateId" );
      if( prop == null )
        throw new InvalidOperationException(
            $"[COTI] {containerCollection.GetType().FullName} has no TemplateId property" );

      // A null VALUE is data and stays null; only a missing MEMBER is a defect. This accessor is on
      // the mount path, where a silent null makes the host lookup miss and the device mount at the
      // host's origin with no diagnostic.
      return prop.GetValue( containerCollection )?.ToString();
    }

    /// <summary>
    /// GameObject is a FIELD on ContainerCollectionView, not a property, on both versions -
    /// verified directly against Assembly-CSharp.dll.
    /// </summary>
    internal static GameObject ViewGameObject( object collectionView )
    {
      var field = AccessTools.Field( collectionView.GetType(), "GameObject" );
      if( field == null )
        throw new InvalidOperationException(
            $"[COTI] {collectionView.GetType().FullName} has no GameObject field" );

      return field.GetValue( collectionView ) as GameObject;
    }

    /// <summary>
    /// Containers is a public property on ContainerCollection, same situation as TemplateId -
    /// the declaring type's name is wiped on 4.0 but the member itself keeps its source name.
    /// </summary>
    internal static IEnumerable Containers( object containerCollection )
    {
      var prop = AccessTools.Property( containerCollection.GetType(), "Containers" );
      if( prop == null )
        throw new InvalidOperationException(
            $"[COTI] {containerCollection.GetType().FullName} has no Containers property" );

      return prop.GetValue( containerCollection ) as IEnumerable;
    }

    /// <summary>
    /// IconsHash is a static utility class on the game side (used by CotiIconCacheInvalidator's
    /// own hash lookups), and its declaring type is PUA-named on 4.0 same as the other four -
    /// confirmed against Assembly-CSharp.dll via a throwaway reflection probe, the same way
    /// ObjectsFactory was confirmed absent by name. GetItemHash keeps its own name.
    /// </summary>
    private static MethodBase _getItemHashMethod;

    /// <summary>
    /// Exposed as a zero-arg *Method() so EftResolveProbe covers it by its existing rule, rather
    /// than this resolution being reachable only from a raid.
    /// </summary>
    internal static MethodBase GetItemHashMethod()
    {
      return _getItemHashMethod ??
             ( _getItemHashMethod = AccessTools.Method( TypeDeclaring( "GetItemHash" ), "GetItemHash" ) );
    }

    internal static int GetItemHash( EFT.InventoryLogic.Item item )
    {
      return (int)GetItemHashMethod().Invoke( null, new object[] { item } );
    }

    /// <summary>
    /// TransformTools is the same situation as IconsHash - a static game utility whose declaring
    /// type is PUA-named on 4.0, found and confirmed the same way. FindTransformRecursive keeps
    /// its own name.
    /// </summary>
    private static MethodBase _findTransformRecursiveMethod;

    internal static MethodBase FindTransformRecursiveMethod()
    {
      return _findTransformRecursiveMethod ??
             ( _findTransformRecursiveMethod =
                 AccessTools.Method( TypeDeclaring( "FindTransformRecursive" ), "FindTransformRecursive" ) );
    }

    internal static Transform FindTransformRecursive( Transform root, string name, bool ignoreCase )
    {
      return (Transform)FindTransformRecursiveMethod().Invoke( null, new object[] { root, name, ignoreCase } );
    }

    /// <summary>
    /// ResourcesCache is PUA-obfuscated on 4.0 like the other five types, found the same way -
    /// by the type declaring its one distinctively-named public method, RemoveFromCache. Unlike
    /// every other member this class resolves by name, _storage is PRIVATE on 4.0 (it is public
    /// on 4.1) - confirmed directly against Assembly-CSharp.dll, where the field reports an empty
    /// Name. It is the type's only static Dictionary&lt;string, object&gt; field, so it is found
    /// by that shape instead, the same technique ResolveIconCacheFields uses.
    /// </summary>
    private static Type ResourcesCacheType => TypeDeclaring( "RemoveFromCache" );

    private static FieldInfo _resourcesCacheStorageField;

    private static FieldInfo ResourcesCacheStorageField()
    {
      if( _resourcesCacheStorageField != null )
        return _resourcesCacheStorageField;

      var hit = ResourcesCacheType
                .GetFields( AccessTools.all )
                .Where( f => f.IsStatic && f.FieldType == typeof( Dictionary<string, object> ) )
                .ToList();

      if( hit.Count != 1 )
      {
        throw new InvalidOperationException(
            $"[COTI] expected exactly one static Dictionary<string,object> field on the resources cache type, found {hit.Count}" );
      }

      return _resourcesCacheStorageField = hit[0];
    }

    internal static void CacheSprite( string key, Sprite sprite )
    {
      var storage = (IDictionary)ResourcesCacheStorageField().GetValue( null );
      storage[key] = sprite;
    }

    /// <summary>
    /// NightVision itself is not renamed on 4.0, but CurrentColor is - EftResolveProbe reports it
    /// as Color_0. It is the type's only property of type Color, so it
    /// is found by that shape - there is nothing else on NightVision this could be confused with,
    /// since the public Color member is a field, not a property (see ApplyPhosphorTint's own use
    /// of that field for the base tint).
    /// </summary>
    private static PropertyInfo _nightVisionCurrentColorProperty;

    private static PropertyInfo NightVisionCurrentColorProperty()
    {
      if( _nightVisionCurrentColorProperty != null )
        return _nightVisionCurrentColorProperty;

      var hit = typeof( BSG.CameraEffects.NightVision )
                .GetProperties( AccessTools.all )
                .Where( p => p.PropertyType == typeof( Color ) && p.GetIndexParameters().Length == 0 )
                .ToList();

      if( hit.Count != 1 )
      {
        throw new InvalidOperationException(
            $"[COTI] expected exactly one Color-valued property on NightVision for CurrentColor, found {hit.Count}" );
      }

      return _nightVisionCurrentColorProperty = hit[0];
    }

    internal static Color NightVisionCurrentColor( BSG.CameraEffects.NightVision nightVision )
    {
      return (Color)NightVisionCurrentColorProperty().GetValue( nightVision );
    }

    /// <summary>
    /// Player is not renamed either, but ToggleGoggles is - to method_15. It stays PUBLIC; only the
    /// name goes. It is matched by shape instead: the only parameterless,
    /// void, instance method declared directly on Player whose body declares a local of type
    /// TogglableComponent - the "find the headwear's togglable component and flip it" local that
    /// the 4.1 source shows under ToggleGoggles's real name. Confirmed unique against a dump of
    /// all 79 candidates sharing that signature; exactly one has such a local.
    /// </summary>
    /// <summary>
    /// ThermalVision's VolumetricLightRenderer field, which OnPreCull dereferences without a null
    /// check. Named _volumetricLightRenderer on 4.1 and volumetricLightRenderer_0 on 4.0, so it is
    /// found by type instead - it is the only field of that type on either build.
    /// </summary>
    private static FieldInfo _volumetricLightRendererField;

    internal static FieldInfo VolumetricLightRendererField()
    {
        if( _volumetricLightRendererField != null )
            return _volumetricLightRendererField;

        var hits = typeof( ThermalVision )
                   .GetFields( AccessTools.all )
                   .Where( f => f.FieldType == typeof( VolumetricLightRenderer ) )
                   .ToList();

        if( hits.Count != 1 )
        {
            throw new InvalidOperationException(
                $"[COTI] expected exactly one VolumetricLightRenderer field on ThermalVision, found {hits.Count}" );
        }

        return _volumetricLightRendererField = hits[0];
    }

    private static MethodBase _toggleGogglesMethod;

    internal static MethodBase ToggleGogglesMethod()
    {
      if( _toggleGogglesMethod != null )
        return _toggleGogglesMethod;

      // Renamed to method_15 on 4.0 but still public, so do NOT filter on visibility.
      // TogglableComponent appears exactly once in Player, which is what makes this unambiguous.
      var hits = typeof( EFT.Player )
                 .GetMethods( AccessTools.all | BindingFlags.DeclaredOnly )
                 .Where( m => !m.IsStatic
                           && m.GetParameters().Length == 0
                           && m.ReturnType == typeof( void ) )
                 .Where( m => m.GetMethodBody()?.LocalVariables
                     .Any( v => v.LocalType == typeof( EFT.InventoryLogic.TogglableComponent ) ) == true )
                 .ToList();

      // Uniqueness is asserted, not assumed. The comment above already claims exactly one match;
      // FirstOrDefault would have taken a second one silently, and this patch SUPPRESSES the
      // original method - binding the wrong one leaves a player whose goggles stop responding to
      // their own keybind, with nothing logged. Every sibling resolver here demands one hit.
      if( hits.Count == 1 )
        return _toggleGogglesMethod = hits[0];

      if( hits.Count == 0 )
      {
        throw new InvalidOperationException(
            "[COTI] no parameterless Player method declares a TogglableComponent local (goggle toggle not found)" );
      }

      throw new InvalidOperationException(
          $"[COTI] {hits.Count} parameterless Player methods declare a TogglableComponent local " +
          $"({string.Join( ", ", hits.Select( m => m.Name ).ToArray() )}) - cannot tell which is the goggle toggle" );
    }
    /// <summary>
    /// The camera manager, which owns the optic camera manager. Renamed to CameraClass on 4.0, so it
    /// is found by the one type declaring the OpticCameraManager property - measured unique against
    /// 4.0's 15137 loadable types. On 4.1 three types carry a member of that name, which is why the
    /// 4.1 branch below names the type rather than searching for it.
    ///
    /// TypeDeclaring searches METHODS, hence the getter rather than the property: a property emits
    /// get_OpticCameraManager on its own declaring type, and that name is unique on 4.0 too.
    /// </summary>
    internal static Type CameraManagerType => TypeDeclaring( "get_OpticCameraManager" );

    /// <summary>
    /// The optic camera manager itself - GClass3687 on 4.0, a NUMBERED name and so exactly the kind
    /// this class never writes in source. Found by CurrentOpticSight's getter, unique on 4.0.
    ///
    /// Its members keep their source names on both builds: Camera, CurrentOpticSight, OnOpticEnabled,
    /// OnOpticDisabled. The one exception is IsAnyOpticCameraRendering, renamed to Boolean_0 - and it
    /// is not needed, because the game defines it as CurrentOpticSight != null and that member
    /// survives. EFT.CameraControl.OpticSight keeps its own name and its whole field layout on both
    /// builds, so it is named directly below.
    /// </summary>
    internal static Type OpticCameraManagerType => TypeDeclaring( "get_CurrentOpticSight" );

    /// <summary>
    /// Memoised per owning type and name. TryGetOptic runs on a render path, and AccessTools.Property
    /// is a fresh GetProperty call every time.
    /// </summary>
    private static readonly Dictionary<string, PropertyInfo> OpticProperties = new Dictionary<string, PropertyInfo>();

    private static PropertyInfo RequireProperty( Type owner, string name )
    {
      var key = owner.FullName + "." + name;

      PropertyInfo cached;
      if( OpticProperties.TryGetValue( key, out cached ) )
        return cached;

      var found = AccessTools.Property( owner, name );
      if( found == null )
        throw new InvalidOperationException( $"[COTI] {owner.FullName} has no {name} property" );

      return OpticProperties[key] = found;
    }

    internal static PropertyInfo CameraManagerExistProperty()
    {
      return RequireProperty( CameraManagerType, "Exist" );
    }

    internal static PropertyInfo CameraManagerInstanceProperty()
    {
      return RequireProperty( CameraManagerType, "Instance" );
    }

    internal static PropertyInfo OpticCameraManagerProperty()
    {
      return RequireProperty( CameraManagerType, "OpticCameraManager" );
    }

    internal static PropertyInfo OpticCameraProperty()
    {
      return RequireProperty( OpticCameraManagerType, "Camera" );
    }

    internal static PropertyInfo CurrentOpticSightProperty()
    {
      return RequireProperty( OpticCameraManagerType, "CurrentOpticSight" );
    }

    internal static bool TryGetOptic( out Camera camera, out OpticSight sight )
    {
      camera = null;
      sight = null;

      if( !(bool)CameraManagerExistProperty().GetValue( null ) )
        return false;

      var optic = OpticCameraManagerProperty().GetValue( CameraManagerInstanceProperty().GetValue( null ) );
      if( optic == null )
        return false;

      sight = CurrentOpticSightProperty().GetValue( optic ) as OpticSight;
      camera = OpticCameraProperty().GetValue( optic ) as Camera;
      return sight != null && camera != null;
    }
#else
    internal static (bool file, bool memory) RemoveFromIconCaches( object iconCreator, int hash )
    {
      var creator = (ItemIconCreator)iconCreator;
      return ( creator._fileCacheIndex.Remove( hash ), creator._memoryCacheIndex.Remove( hash ) );
    }

    internal static string ContainerTemplateId( object containerCollection )
    {
      return ( (ContainerCollection)containerCollection ).TemplateId;
    }

    internal static GameObject ViewGameObject( object collectionView )
    {
      var view = (ContainerCollectionView)collectionView;
      return view.GameObject;
    }

    internal static IEnumerable Containers( object containerCollection )
    {
      return ( (ContainerCollection)containerCollection ).Containers;
    }

    // Bound at compile time here, so these exist only to keep the probe's resolver list the same
    // shape on both builds - a resolver covered on 4.0 and invisible on 4.1 is how a coverage gap
    // hides.
    internal static MethodBase GetItemHashMethod()
    {
      return AccessTools.Method( typeof( IconsHash ), nameof( IconsHash.GetItemHash ) );
    }

    internal static MethodBase FindTransformRecursiveMethod()
    {
      return AccessTools.Method( typeof( TransformTools ), nameof( TransformTools.FindTransformRecursive ) );
    }

    internal static int GetItemHash( EFT.InventoryLogic.Item item )
    {
      return IconsHash.GetItemHash( item );
    }

    internal static Transform FindTransformRecursive( Transform root, string name, bool ignoreCase )
    {
      return TransformTools.FindTransformRecursive( root, name, ignoreCase );
    }

    internal static void CacheSprite( string key, Sprite sprite )
    {
      ResourcesCache._storage[key] = sprite;
    }

    internal static Color NightVisionCurrentColor( BSG.CameraEffects.NightVision nightVision )
    {
      return nightVision.CurrentColor;
    }

    internal static MethodBase ToggleGogglesMethod()
    {
      return AccessTools.Method( typeof( EFT.Player ), nameof( EFT.Player.ToggleGoggles ) );
    }

    internal static FieldInfo VolumetricLightRendererField()
    {
      var field = AccessTools.Field( typeof( ThermalVision ), "_volumetricLightRenderer" );
      if( field == null )
      {
        throw new InvalidOperationException(
            "[COTI] ThermalVision has no _volumetricLightRenderer field" );
      }

      return field;
    }
    internal static Type CameraManagerType => typeof( CameraManager );
    internal static Type OpticCameraManagerType => typeof( OpticCameraManager );

    // Bound at compile time here, so these exist only to keep the probe's resolver list the same
    // shape on both builds - see the note on GetItemHashMethod above.
    internal static PropertyInfo CameraManagerExistProperty()
    {
      return AccessTools.Property( typeof( CameraManager ), nameof( CameraManager.Exist ) );
    }

    internal static PropertyInfo CameraManagerInstanceProperty()
    {
      return AccessTools.Property( typeof( CameraManager ), nameof( CameraManager.Instance ) );
    }

    internal static PropertyInfo OpticCameraManagerProperty()
    {
      return AccessTools.Property( typeof( CameraManager ), nameof( CameraManager.OpticCameraManager ) );
    }

    internal static PropertyInfo OpticCameraProperty()
    {
      return AccessTools.Property( typeof( OpticCameraManager ), nameof( OpticCameraManager.Camera ) );
    }

    internal static PropertyInfo CurrentOpticSightProperty()
    {
      return AccessTools.Property( typeof( OpticCameraManager ), nameof( OpticCameraManager.CurrentOpticSight ) );
    }

    internal static bool TryGetOptic( out Camera camera, out OpticSight sight )
    {
      camera = null;
      sight = null;

      // Exist FIRST. Instance is `instance ?? (instance = new CameraManager())` on BOTH builds, so
      // reading it outside a raid CONSTRUCTS a manager rather than reporting that there is none.
      if( !CameraManager.Exist )
        return false;

      var optic = CameraManager.Instance.OpticCameraManager;
      if( optic == null )
        return false;

      sight = optic.CurrentOpticSight;
      camera = optic.Camera;
      return sight != null && camera != null;
    }
#endif

    // ---- Inspect window (ItemSpecificationPanel) --------------------------------------------
    //
    // Unlike ContainerCollectionView/IconsHash/ResourcesCache above, every TYPE and FIELD this
    // section touches keeps its source name on 4.0 - only two METHOD names are wiped
    // (CreateContextButton and BindButton, both on InteractionButtonsContainer). Confirmed by
    // decompiling both installs directly rather than inferred from the 4.1 names, so a plain
    // typeof and AccessTools.Field are enough here and only those two methods need a lookup.

    internal static Type ItemSpecificationPanelType => typeof( EFT.UI.ItemSpecificationPanel );

    internal static MethodBase ItemSpecificationPanelShowMethod()
    {
      // The only Show overload declared directly on the class on either build, so no parameter
      // list is needed to disambiguate.
      return AccessTools.Method( ItemSpecificationPanelType, "Show" );
    }

    internal static FieldInfo InteractionButtonsContainerField()
    {
      var field = AccessTools.Field( ItemSpecificationPanelType, "_interactionButtonsContainer" );
      if( field == null )
        throw new InvalidOperationException( "[COTI] ItemSpecificationPanel has no _interactionButtonsContainer field" );

      return field;
    }

    /// <summary>
    /// The inspected item. Named _item on 4.1 and item_0 on 4.0, so resolved by shape: the one field
    /// of type EFT.InventoryLogic.Item declared directly on the class. The nested FakeSlot helper
    /// carries a same-typed field, but DeclaredOnly on the outer type never sees it.
    /// </summary>
    private static FieldInfo _inspectedItemField;

    internal static FieldInfo InspectedItemField()
    {
      if( _inspectedItemField != null )
        return _inspectedItemField;

      var hits = ItemSpecificationPanelType
                 .GetFields( AccessTools.all | BindingFlags.DeclaredOnly )
                 .Where( f => f.FieldType == typeof( EFT.InventoryLogic.Item ) )
                 .ToList();

      if( hits.Count != 1 )
      {
        throw new InvalidOperationException(
            $"[COTI] expected exactly one Item field declared on ItemSpecificationPanel, found {hits.Count}" );
      }

      return _inspectedItemField = hits[0];
    }

    internal static FieldInfo ButtonTemplateField()
    {
      var field = AccessTools.Field( typeof( EFT.UI.InteractionButtonsContainer ), "_buttonTemplate" );
      if( field == null )
        throw new InvalidOperationException( "[COTI] InteractionButtonsContainer has no _buttonTemplate field" );

      return field;
    }

    internal static FieldInfo ButtonsContainerField()
    {
      var field = AccessTools.Field( typeof( EFT.UI.InteractionButtonsContainer ), "_buttonsContainer" );
      if( field == null )
        throw new InvalidOperationException( "[COTI] InteractionButtonsContainer has no _buttonsContainer field" );

      return field;
    }

#if SPT40
    /// <summary>
    /// InteractionButtonsContainer's own button-clone method is unnamed on 4.0 - it decompiles as
    /// method_1 - but its parameter names survive intact, same situation as AttachModsMethod
    /// above.
    /// </summary>
    private static readonly string[] CreateContextButtonParams =
        { "key", "caption", "template", "container", "sprite", "onButtonClicked", "onMouseHover", "subMenu", "autoClose" };

    /// <summary>
    /// Memoised, same rationale as _toggleGogglesMethod above: AddButton calls this every time
    /// the inspect window opens or redraws, not once at patch-registration time, so the scan
    /// this does over every InteractionButtonsContainer method is worth caching.
    /// </summary>
    private static MethodBase _createContextButtonMethod;

    internal static MethodBase CreateContextButtonMethod()
    {
      if( _createContextButtonMethod != null )
        return _createContextButtonMethod;

      var hits = typeof( EFT.UI.InteractionButtonsContainer )
                 .GetMethods( AccessTools.all )
                 .Where( m =>
                 {
                   var p = m.GetParameters();
                   return p.Length == CreateContextButtonParams.Length
                       && p.Select( x => x.Name ).SequenceEqual( CreateContextButtonParams );
                 } )
                 .ToList();

      if( hits.Count != 1 )
      {
        throw new InvalidOperationException(
            $"[COTI] expected exactly one method on InteractionButtonsContainer matching the CreateContextButton parameter shape, found {hits.Count}" );
      }

      return _createContextButtonMethod = hits[0];
    }

    /// <summary>
    /// BindButton's own name is wiped on 4.0 too - method_5 - but it keeps its single
    /// parameter's name, "button", of type SimpleContextMenuButton. Two other textual matches for
    /// that parameter shape exist in the 4.0 dump (a field inside a nested compiler-generated
    /// closure class, and a local variable) but GetMethods can only ever return methods, so
    /// neither is a candidate here. Memoised for the same reason as CreateContextButtonMethod.
    /// </summary>
    private static MethodBase _bindButtonMethod;

    internal static MethodBase BindButtonMethod()
    {
      if( _bindButtonMethod != null )
        return _bindButtonMethod;

      var hits = typeof( EFT.UI.InteractionButtonsContainer )
                 .GetMethods( AccessTools.all )
                 .Where( m =>
                 {
                   var p = m.GetParameters();
                   return p.Length == 1
                       && p[0].Name == "button"
                       && p[0].ParameterType == typeof( EFT.UI.SimpleContextMenuButton );
                 } )
                 .ToList();

      if( hits.Count != 1 )
      {
        throw new InvalidOperationException(
            $"[COTI] expected exactly one method on InteractionButtonsContainer matching the BindButton parameter shape, found {hits.Count}" );
      }

      return _bindButtonMethod = hits[0];
    }
#else
    internal static MethodBase CreateContextButtonMethod()
    {
      // A string literal, not nameof: CreateContextButton is overloaded (a generic <T> form
      // also exists), and AccessTools.Method needs the explicit parameter list below to pick
      // the non-generic one regardless.
      return AccessTools.Method( typeof( EFT.UI.InteractionButtonsContainer ), "CreateContextButton",
          new[]
          {
              typeof( string ), typeof( string ), typeof( EFT.UI.SimpleContextMenuButton ), typeof( RectTransform ),
              typeof( Sprite ), typeof( Action ), typeof( Action ), typeof( bool ), typeof( bool ),
          } );
    }

    internal static MethodBase BindButtonMethod()
    {
      return AccessTools.Method( typeof( EFT.UI.InteractionButtonsContainer ), "BindButton" );
    }
#endif
  }
}
