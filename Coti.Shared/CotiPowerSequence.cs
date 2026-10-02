namespace Coti.Shared
{
  public enum CotiPowerPhase
  {
    Off,
    Initializing,
    Warming,
    On,
    PoweringOff,
  }

  /// <summary>
  /// What the device's own display shows. Blank is lit but empty: the gap between
  /// "Initializing..." and the thermal image on the real ECOTI.
  /// </summary>
  public enum CotiDisplayMessage
  {
    None,
    Blank,
    Initializing,
    PowerOff,
  }

  public sealed class CotiPowerTimings
  {
    public double InitializingSeconds { get; set; } = 1.2;
    public double WarmingSeconds { get; set; } = 0.3;
    public double PowerOffSeconds { get; set; } = 1.5;
  }

  public readonly struct CotiPowerFrame
  {
    public CotiPowerFrame( CotiPowerPhase phase, bool clickNow, float gainBoost )
    {
      Phase = phase;
      ClickNow = clickNow;
      GainBoost = gainBoost;
    }

    public CotiPowerPhase Phase { get; }

    /// <summary>
    /// True on exactly one frame per boot: ClickLeadSeconds before the image appears.
    /// </summary>
    public bool ClickNow { get; }

    /// <summary>
    /// Multiplier on the thermal's intensity while the core's gain settles. 1 outside the settle.
    /// </summary>
    public float GainBoost { get; }

    public bool ThermalOn => Phase == CotiPowerPhase.On;

    public CotiDisplayMessage Message
    {
      get
      {
        switch( Phase )
        {
          case CotiPowerPhase.Initializing: return CotiDisplayMessage.Initializing;
          case CotiPowerPhase.Warming: return CotiDisplayMessage.Blank;
          case CotiPowerPhase.PoweringOff: return CotiDisplayMessage.PowerOff;
          default: return CotiDisplayMessage.None;
        }
      }
    }

    public static CotiPowerFrame Steady( CotiPowerPhase phase )
    {
      return new CotiPowerFrame( phase, false, 1f );
    }
  }

  /// <summary>
  /// The ECOTI's power state. CTRL+N calls Press; every frame calls Advance with the same clock.
  /// A press mid-transition reverses it. Disabled, a press flips straight between Off and On.
  /// </summary>
  public sealed class CotiPowerSequence
  {
    public const double SettleSeconds = 0.6;
    public const float SettleBoost = 1.25f;

    private CotiPowerPhase _phase;
    private double _phaseStart;
    private double _settleStart = double.NaN;
    private bool _clicked;

    /// <summary>
    /// The calibration click comes this long before the image, inside the warm-up gap; a gap
    /// shorter than this clicks as it begins.
    /// </summary>
    public const double ClickLeadSeconds = 0.15;

    public CotiPowerSequence( CotiPowerTimings? timings = null, bool startOn = true )
    {
      Timings = timings ?? new CotiPowerTimings();
      _phase = startOn ? CotiPowerPhase.On : CotiPowerPhase.Off;
    }

    public CotiPowerTimings Timings { get; set; }

    public bool Enabled { get; set; } = true;

    public CotiPowerPhase Phase => _phase;

    public void Press( double now )
    {
      if( !Enabled )
      {
        SnapToDestination();
        Enter( _phase == CotiPowerPhase.On ? CotiPowerPhase.Off : CotiPowerPhase.On, now );
        return;
      }

      var next = _phase == CotiPowerPhase.Off || _phase == CotiPowerPhase.PoweringOff
          ? CotiPowerPhase.Initializing
          : CotiPowerPhase.PoweringOff;

      Enter( next, now );
    }

    public CotiPowerFrame Advance( double now )
    {
      if( !Enabled )
      {
        SnapToDestination();
        return CotiPowerFrame.Steady( _phase );
      }

      var click = RunTransitions( now );
      return new CotiPowerFrame( _phase, click, GainAt( now ) );
    }

    /// <summary>
    /// Walks every threshold the clock has passed, so one long frame lands in the right phase.
    /// Returns whether the calibration click fell due during the walk.
    /// </summary>
    private bool RunTransitions( double now )
    {
      var clicked = false;

      while( true )
      {
        var elapsed = now - _phaseStart;

        if( _phase == CotiPowerPhase.Initializing && elapsed >= Duration( Timings.InitializingSeconds ) )
        {
          Step( CotiPowerPhase.Warming, Timings.InitializingSeconds );
          continue;
        }

        if( _phase == CotiPowerPhase.Warming && !_clicked && elapsed >= ClickPoint() )
        {
          _clicked = true;
          clicked = true;
        }

        if( _phase == CotiPowerPhase.Warming && elapsed >= Duration( Timings.WarmingSeconds ) )
        {
          Step( CotiPowerPhase.On, Timings.WarmingSeconds );
          _settleStart = _phaseStart;
          continue;
        }

        if( _phase == CotiPowerPhase.PoweringOff && elapsed >= Duration( Timings.PowerOffSeconds ) )
        {
          Step( CotiPowerPhase.Off, Timings.PowerOffSeconds );
          continue;
        }

        return clicked;
      }
    }

    private void Step( CotiPowerPhase next, double seconds )
    {
      _phaseStart += Duration( seconds );
      _phase = next;
    }

    private void Enter( CotiPowerPhase next, double now )
    {
      _phase = next;
      _phaseStart = now;
      _settleStart = double.NaN;
      _clicked = false;
    }

    private double ClickPoint()
    {
      var point = Duration( Timings.WarmingSeconds ) - ClickLeadSeconds;
      return point > 0 ? point : 0;
    }

    private void SnapToDestination()
    {
      if( _phase == CotiPowerPhase.Initializing || _phase == CotiPowerPhase.Warming )
        _phase = CotiPowerPhase.On;
      else if( _phase == CotiPowerPhase.PoweringOff )
        _phase = CotiPowerPhase.Off;

      _settleStart = double.NaN;
    }

    private float GainAt( double now )
    {
      if( _phase != CotiPowerPhase.On || double.IsNaN( _settleStart ) )
        return 1f;

      var progress = ( now - _settleStart ) / SettleSeconds;
      if( progress >= 1 )
        return 1f;
      if( progress < 0 )
        progress = 0;

      var remaining = 1 - progress;
      return 1f + ( SettleBoost - 1f ) * (float)( remaining * remaining );
    }

    /// <summary>
    /// Negative and NaN durations count as zero, so a bad F12 value cannot stall the device.
    /// </summary>
    private static double Duration( double seconds )
    {
      return seconds > 0 ? seconds : 0;
    }
  }
}
