using BepInEx;

namespace Ponder.Loaders
{
    [BepInPlugin(ModId, "Ponder", "0.6.0")]
    [BepInProcess("A Dance of Fire and Ice.exe")]
    public class AdofaiBepInPlugin : BaseUnityPlugin
    {
        private const string ModId = "Ponder";
        private BepInHandler? _handler;

        private void Awake()
        {
            _handler = new BepInHandler(Logger);
            Main.Initialize(_handler);
            _handler.TriggerToggle(true);
        }

        private void Update()
        {
            _handler?.TriggerUpdate(UnityEngine.Time.deltaTime);
        }

        private void OnGUI()
        {
            _handler?.TriggerGUI();
        }
    }
}
