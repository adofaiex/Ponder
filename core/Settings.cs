namespace Ponder
{
    public class PatchModeSettings
    {
        public bool useILPatch { get; set; } = false;
    }

    public class Settings
    {
        public PatchModeSettings patchMode { get; set; } = new PatchModeSettings();
        public bool enablePonder { get; set; } = true;
        public bool enableHoverHud { get; set; } = true;
        public bool enableDemoLogs { get; set; } = false;
        public bool enableDebugLogs { get; set; } = true;
        public float chargeTime { get; set; } = 1f;
    }
}
