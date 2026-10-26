namespace StarRacingPrototype {
 // The selected Vector has one full-resolution level: no lossy distance geometry.
 // Keep the existing QA entry points while checking the replacement contract.
 public static class VehicleModelLodChecks {
  public static void Run()=>VectorRuntimeChecks.Run();
  public static void Quality()=>VectorRenderQualityChecks.Run();
 }
}
