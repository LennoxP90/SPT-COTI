using System;
using System.Collections.Generic;
using System.Diagnostics;
using Coti.Client.Patches;
using Coti.Shared;
using Diz.DependencyManager;
using Diz.Resources;
using EFT.InventoryLogic;
using UnityEngine;

namespace Coti.Client
{
  /// <summary>
  /// The bundle keys of every magnified scope, built once from the item templates, and the hand-off from a bundle load to
  /// the tagger. Every bundle that starts loading is recorded (weakly) so a scope's dependencies can be read: a bundle's
  /// Assets hold only what it declares, never the materials it includes, so a material shared with another item is found
  /// in the scope's dependency bundles instead, and only while they are Loaded. A scope that loads before the templates
  /// exist waits.
  /// </summary>
  internal static class CotiScopeIndex
  {
    private static HashSet<string> _keys;
    private static readonly Dictionary<string, WeakReference<EasyBundle>> Bundles = new Dictionary<string, WeakReference<EasyBundle>>( StringComparer.Ordinal );
    private static readonly List<string> Pending = new List<string>();
    private static readonly HashSet<int> SharedIds = new HashSet<int>();
    private static readonly Func<string, IEnumerable<string>> DependenciesOf = Dependencies;

    internal static void Build( IEnumerable<ItemTemplate> templates )
    {
      var verbose = VerboseLogging;
      var started = verbose ? Stopwatch.GetTimestamp() : 0L;
      var keys = new HashSet<string>( StringComparer.Ordinal );
      var count = 0;
      foreach( var template in templates )
      {
        count++;
        if( template is SightModTemplate sight && IsMagnifiedScope( sight ) )
          keys.Add( sight.Prefab?.path );
      }
      keys.Remove( null );
      keys.Remove( "" );
      _keys = keys;
      if( verbose )
        Plugin.Log.LogInfo( $"[COTI] scope glass: {keys.Count} magnified sight bundle(s) of {count} template(s) in {Milliseconds( started ):0.0} ms, {Pending.Count} queued" );

      // Each on its own guard: one bad bundle must not stop the rest.
      foreach( var key in Pending )
      {
        var pending = key;
        CotiPatchGuard.Run( nameof( CotiScopeIndex ), () => TagIfScope( pending ) );
      }
      Pending.Clear();
    }

    /// <summary>
    /// A bundle has started loading: record it, and if it is (or before the index exists, may be) a scope, tag it the
    /// moment it turns Loaded.
    /// </summary>
    internal static void OnBundleLoading( EasyBundle bundle )
    {
      var key = bundle?.Key;
      if( string.IsNullOrEmpty( key ) )
        return;
      if( Bundles.TryGetValue( key, out var reference ) )
        reference.SetTarget( bundle );
      else
        Bundles.Add( key, new WeakReference<EasyBundle>( bundle ) );
      if( _keys != null && !_keys.Contains( key ) )
        return;

      Action unsubscribe = null;
      unsubscribe = bundle.LoadState.Subscribe( state =>
      {
        if( state == ELoadState.Loading )
          return;
        unsubscribe?.Invoke();
        if( state == ELoadState.Loaded )
          CotiPatchGuard.Run( nameof( CotiScopeIndex ), () => OnLoaded( key ) );
      } );
    }

    private static void OnLoaded( string key )
    {
      if( _keys == null )
        Pending.Add( key );
      else
        TagIfScope( key );
    }

    private static void TagIfScope( string key )
    {
      if( !_keys.Contains( key ) )
        return;
      var assets = LoadedAssets( key );
      if( assets == null )
        return;
      var verbose = VerboseLogging;
      var started = verbose ? Stopwatch.GetTimestamp() : 0L;
      var tagged = CotiScopeGlassTagger.Tag( assets, SharedMaterials( key ), CotiScopeGlass.Mode( Plugin.Config.Image.Glass, Plugin.Config.Image.ScopeGlassReflections ) );
      if( verbose )
        Plugin.Log.LogInfo( $"[COTI] scope glass: {tagged} material(s) tagged in {key} in {Milliseconds( started ):0.00} ms" );
    }

    /// <summary>A recorded bundle's assets while it is Loaded; null once it is unloaded or gone.</summary>
    private static UnityEngine.Object[] LoadedAssets( string key )
    {
      return Bundles.TryGetValue( key, out var reference ) && reference.TryGetTarget( out var bundle )
             && bundle.LoadState.Value == ELoadState.Loaded ? bundle.Assets : null;
    }

    /// <summary>The instance id of every material a Loaded bundle in this one's dependency closure holds or draws.</summary>
    private static HashSet<int> SharedMaterials( string key )
    {
      SharedIds.Clear();
      foreach( var dependency in CotiScopeGlass.DependencyClosure( key, DependenciesOf ) )
      {
        var assets = LoadedAssets( dependency );
        if( assets != null )
          CotiScopeGlassTagger.CollectMaterials( assets, SharedIds );
      }
      return SharedIds;
    }

    private static IEnumerable<string> Dependencies( string key )
    {
      return Bundles.TryGetValue( key, out var reference ) && reference.TryGetTarget( out var bundle ) ? bundle.DependencyKeys : null;
    }

    private static bool IsMagnifiedScope( SightModTemplate sight )
    {
      var isScope = sight is OpticScopeTemplate || sight is AssaultScopeTemplate;
      return CotiScopeGlass.IsMagnified( CotiScopeGlass.MaxZoom( sight.Zooms ), isScope, sight is SpecialScopeTemplate, CotiOpticFusion.MinimumMagnification );
    }

    private static bool VerboseLogging => Plugin.Config != null && Plugin.Config.VerboseLogging;

    private static double Milliseconds( long started )
    {
      return ( Stopwatch.GetTimestamp() - started ) * 1000.0 / Stopwatch.Frequency;
    }
  }
}
