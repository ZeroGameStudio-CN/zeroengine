using System;

namespace ZeroEngine.TCE
{
    public sealed class OnInstallTrigger : TceTrigger<OnInstallTriggerData>
    {
        public override void OnInstall()
        {
            Trigger(Context.Owner, Context.InstallSource);
        }
    }

    [Serializable]
    [TceComponentDoc(TceComponentDocCategory.Trigger, "On Install", "Fires once when the graph is installed.", "Use this trigger for immediate setup rules that should run after all conditions and effects are initialized.")]
    public sealed class OnInstallTriggerData : TceTriggerData<OnInstallTrigger>
    {
    }
}
