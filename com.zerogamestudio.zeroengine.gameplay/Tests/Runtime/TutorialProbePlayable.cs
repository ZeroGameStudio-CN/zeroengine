using UnityEngine;
using UnityEngine.Playables;

namespace ZeroEngine.Tutorial.Tests.Runtime
{
    public sealed class TutorialProbePlayable : PlayableAsset
    {
        public override double duration => .15;
        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        { var playable = Playable.Create(graph); playable.SetDuration(duration); return playable; }
    }
}
