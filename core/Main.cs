using HarmonyLib;

namespace Ponder
{
    public static class Main
    {
        public static IHandler Handler { get; private set; } = null!;
        public static Harmony Harmony { get; private set; } = null!;
        public static Settings Settings { get; private set; } = null!;

        public static bool Initialize(IHandler handler)
        {
            Handler = handler;
            Settings = handler.LoadSettings<Settings>();
            Harmony = new Harmony(handler.ModId);

            handler.OnToggle += OnToggle;

            PonderManager.Ensure();

            handler.Log("Mod initialized");
            return true;
        }

        private static void OnToggle(bool value)
        {
            if (value)
            {
                Handler.Log("Mod enabled");
                PatchManager.UpdateAllPatches();
                AsyncPatchManager.Start();
                AsyncPatchManager.UpdateAllPatchesAsync();
            }
            else
            {
                Handler.Log("Mod disabled");
                AsyncPatchManager.Stop();
                PatchManager.UnpatchAll();
            }
        }
    }
}
