using HarmonyLib;

namespace Ponder
{
    /// <summary>
    /// Feature-grouped patch declarations. Each nested [HarmonyPatch] class is
    /// registered in PatchManager with a settings-driven enable condition.
    /// </summary>
    public static class PonderPatches
    {
        [HarmonyPatch(typeof(scrController), "Start")]
        public static class ControllerStartPatch
        {
            public static void Postfix()
            {
                Main.Handler.Log("Controller start (Ponder)");
            }
        }
    }

    // public static class PonderEditorPatches
    // {
    //     [HarmonyPatch(typeof(scnEditor), "Update")]
    //     public static class EditorUpdatePatch
    //     {
    //         public static void Postfix(scnEditor __instance) { }
    //     }
    // }
}
