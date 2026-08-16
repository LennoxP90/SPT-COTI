namespace Coti.Client
{
  public static class CotiMaskResolver
  {
    public static string ResolveMaskName( CotiConfig config, string hostTemplateId )
    {
      if( config == null || hostTemplateId == null )
        return CotiConfig.FallbackMaskName;
      if( config.NvgHosts == null )
        return CotiConfig.FallbackMaskName;

      CotiNvgHostConfig host;
      if( !config.NvgHosts.TryGetValue( hostTemplateId, out host ) )
        return CotiConfig.FallbackMaskName;
      if( host == null || string.IsNullOrEmpty( host.MaskName ) )
        return CotiConfig.FallbackMaskName;

      return host.MaskName;
    }
  }
}
