#nullable enable
using System;
using System.Collections.Generic;

namespace Coti.Client
{
  /// <summary>
  /// The anchor bone suggestion and the cycling arithmetic behind it, both pure and over
  /// primitives - same reasoning as CotiInspectGateResolver: CotiPoseTuner's own state is Unity
  /// Transform/CurveRotator references a test cannot construct, so the decision itself lives here
  /// where a test can reach it. Source-linked into Coti.Tests the same way CotiInspectGate.cs is.
  /// #nullable enable, not the project's "annotations" default, because a null string genuinely
  /// means something distinct from an empty one here (no suggestion vs. suggest the root) and
  /// this file is compiled a second time under Coti.Tests's own "enable", where an unannotated
  /// string parameter fed a null literal in a test is a warning this project has zero tolerance
  /// for.
  /// </summary>
  public static class CotiAnchorAdvisor
  {
    /// <summary>
    /// Three outcomes: no rotator at all, a rotator on the root, or a named child transform.
    /// HasHinge is true on every vanilla NVG, so it does not distinguish anything - the presence of
    /// a CurveRotator does.
    /// </summary>
    public static string? SuggestAnchorBone( bool rotatorPresent, bool rotatedTransformIsRoot, string? rotatedTransformName )
    {
      if( !rotatorPresent )
        return null;

      return rotatedTransformIsRoot ? string.Empty : rotatedTransformName;
    }

    /// <summary>
    /// Wraps in either direction over candidateCount entries. currentIndex of -1 (the value being
    /// cycled from is not among the candidates at all - a hand-edited anchor name, or a bone the
    /// depth-limited scan never reached) still lands on a real entry rather than throwing or
    /// standing still: -1 is treated as "one step before the first entry," so cycling forward from
    /// an unknown value lands on the first candidate, same as it would from an empty list position.
    /// </summary>
    public static int NextCandidateIndex( int currentIndex, int candidateCount, int direction )
    {
      if( candidateCount <= 0 )
        return 0;

      var next = ( currentIndex + direction ) % candidateCount;
      return next < 0 ? next + candidateCount : next;
    }

    /// <summary>
    /// Ensures the suggested bone is in the candidate list, so it can always be selected even if
    /// the transform walk that built the list missed it.
    /// </summary>
    public static List<string> EnsureSuggestedIsCandidate( IReadOnlyList<string> candidates, string? suggested )
    {
      var result = new List<string>( candidates );

      // A direct null check, not string.IsNullOrEmpty, so the compiler's own flow analysis narrows
      // "suggested" to non-null below without depending on that BCL method carrying a
      // [NotNullWhen] attribute - net472's reference assemblies (this file's other build target)
      // predate that annotation, so IsNullOrEmpty leaves "suggested" looking possibly-null to the
      // compiler afterward and CS8604 fires on the Add call below, even though the check is
      // logically identical.
      if( suggested == null || suggested.Length == 0 )
        return result;

      foreach( var candidate in result )
      {
        if( string.Equals( candidate, suggested, StringComparison.OrdinalIgnoreCase ) )
          return result;
      }

      result.Add( suggested );
      return result;
    }
  }
}
