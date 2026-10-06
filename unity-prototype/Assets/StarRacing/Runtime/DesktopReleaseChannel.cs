namespace StarRacingPrototype.Distribution {
 public static class DesktopReleaseChannel {
#if STAR_RACING_TEST_CHANNEL
  public const string Track="test";
#else
  public const string Track="production";
#endif
 }
}
