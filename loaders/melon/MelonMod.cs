using MelonLoader;

[assembly: MelonInfo(typeof(Ponder.Loaders.AdofaiMelonMod), "Ponder", "0.6.0", "xbodwf")]
[assembly: MelonGame("7th Beat Games", "A Dance of Fire and Ice")]

namespace Ponder.Loaders
{
    public class AdofaiMelonMod : MelonMod
    {
        private MelonHandler? _handler;

        public override void OnInitializeMelon()
        {
            _handler = new MelonHandler(this);
            Main.Initialize(_handler);
            _handler.TriggerToggle(true);
        }

        public override void OnUpdate()
        {
            _handler?.TriggerUpdate(UnityEngine.Time.deltaTime);
        }

        public override void OnGUI()
        {
            _handler?.TriggerGUI();
        }
    }
}
