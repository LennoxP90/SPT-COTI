using System;
using System.IO;
using Coti.Shared;
using UnityEngine;

namespace Coti.Client
{
  /// <summary>
  /// The thermal core's calibration click as the sensor comes on. Played the way EFT plays its own
  /// NVG flip (ClientPlayer.PlayToggleSound): a pooled non-spatial source on the client player's
  /// movement mixer, so the game's volume settings apply to it like they do to the goggles.
  ///
  /// The samples are kept and the clip built at play time, because EFT resets the audio system at
  /// startup and at every raid start (AudioUtils.ResetAudioBuffer), which empties a created clip.
  /// </summary>
  internal static class CotiCalibrationClick
  {
    private static CotiWav _wav;
    private static AudioClip _clip;

    internal static void Load( string pluginDirectory )
    {
      var path = Path.Combine( Path.Combine( pluginDirectory, "sounds" ), "coti_calibration_click.wav" );

      try
      {
        if( !File.Exists( path ) )
        {
          Plugin.Log.LogWarning( $"[COTI] {path} is missing - the power sequence will be silent" );
          return;
        }

        string? error;
        if( !CotiWavReader.TryRead( File.ReadAllBytes( path ), out _wav, out error ) )
          Plugin.Log.LogWarning( $"[COTI] {path} not loaded: {error} - the power sequence will be silent" );
      }
      catch( Exception ex )
      {
        _wav = null;
        Plugin.Log.LogWarning( $"[COTI] Could not load {path}: {ex.Message} - the power sequence will be silent" );
      }
    }

    /// <summary>
    /// Runs after CotiState.Update: the click needs a COTI on the goggles, so a CTRL+N with none
    /// attached stays silent.
    /// </summary>
    internal static void Tick( CotiPowerFrame frame )
    {
      if( !CotiActivation.ShouldClick(
              Plugin.IsHeadless, frame.ClickNow, CotiState.CotiAttached, Plugin.Config?.Enabled ?? true ) )
        return;

      var setting = Plugin.Config?.PowerSequence?.ClickVolume ?? 1f;
      var skipped = SkipReason( setting );
      if( skipped != null )
      {
        if( Plugin.Config?.VerboseLogging ?? false )
          Plugin.Log.LogInfo( $"[COTI] Calibration click skipped: {skipped}" );
        return;
      }

      var rebuilt = EnsureClip();

      var audio = MonoBehaviourSingleton<BetterAudio>.Instance;
      audio.PlayNonspatial( _clip, BetterAudio.AudioSourceGroupType.Character, 0f,
          CotiClickVolume.ForPlayNonspatial( setting ), audio.ClientPlayerMovementMixer );

      if( Plugin.Config?.VerboseLogging ?? false )
      {
        Plugin.Log.LogInfo( $"[COTI] Calibration click played at {setting:F2} " +
                            $"({_clip.length * 1000f:F0} ms{( rebuilt ? ", clip rebuilt" : "" )}, " +
                            $"mixer {( audio.ClientPlayerMovementMixer == null ? "none" : audio.ClientPlayerMovementMixer.name )})" );
      }
    }

    /// <summary>
    /// True when the clip had to be (re)built from the kept samples.
    /// </summary>
    private static bool EnsureClip()
    {
      if( !CotiClipCache.NeedsRebuild( _clip != null, _clip != null ? _clip.length : 0f ) )
        return false;

      if( _clip != null )
        UnityEngine.Object.Destroy( _clip );

      _clip = AudioClip.Create( "CotiCalibrationClick", _wav.FrameCount, _wav.Channels, _wav.SampleRate, false );
      _clip.SetData( _wav.Samples, 0 );
      return true;
    }

    private static string SkipReason( float setting )
    {
      if( _wav == null )
        return "no clip loaded";
      if( !( setting > 0f ) )
        return "Click Volume is 0";
      if( !MonoBehaviourSingleton<BetterAudio>.Instantiated )
        return "the game's audio is not up";
      return null;
    }
  }
}
