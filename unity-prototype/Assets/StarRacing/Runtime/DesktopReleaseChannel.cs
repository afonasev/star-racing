namespace StarRacingPrototype.Distribution {
 public static class DesktopReleaseChannel {
#if STAR_RACING_TEST_CHANNEL
  public const string Track="test";
#else
  public const string Track="production";
#endif
#if STAR_RACING_UNSIGNED_PRODUCTION
  public const bool AllowsUnsignedProduction=true;
#else
  public const bool AllowsUnsignedProduction=false;
#endif
 }
}
