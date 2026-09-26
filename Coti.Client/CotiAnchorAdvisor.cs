#nullable enable
using System;
using System.Collections.Generic;

namespace Coti.Client
{
  /// <summary>
  /// The anchor bone suggestion and the cycling arithmetic behind it, pure and over primitives.
  /// CotiPoseTuner's own state is Unity Transform/CurveRotator references a test cannot
  /// construct, so the decision lives here, source-linked into Coti.Tests like CotiInspectGate.cs.
  /// #nullable enable rather than the project's "annotations" default, because a null string
  /// means something distinct from an empty one here (no suggestion vs. suggest the root), and
  /// the file also compiles under Coti.Tests's "enable", which treats nullable warnings as
  /// errors.
  /// </summary>
  public static class CotiAnchorAdvisor
  {
    /// <summary>
    /// Three outcomes: no rotator at all, a rotator on the root, or a named child transform.
    /// HasHinge is true on every vanilla NVG, so it distinguishes nothing; the presence of a
    /// CurveRotator does.
    /// </summary>
    public static string? SuggestAnchorBone( bool rotatorPresent, bool rotatedTransformIsRoot, string? rotatedTransformName )
    {
      if( !rotatorPresent )
        return null;

      return rotatedTransformIsRoot ? string.Empty : rotatedTransformName;
    }

    /// <summary>
    /// Wraps in either direction over candidateCount entries. A currentIndex of -1 (the current
    /// value is not among the candidates, such as a hand-edited anchor name or a bone the
    /// depth-limited scan never reached) is treated as one step before the first entry, so
    /// cycling forward lands on the first candidate.
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

      // A direct null check rather than string.IsNullOrEmpty: net472's reference assemblies lack
      // [NotNullWhen] on that method, so the compiler would not narrow "suggested" to non-null and
      // CS8604 would fire on the Add call below.
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
