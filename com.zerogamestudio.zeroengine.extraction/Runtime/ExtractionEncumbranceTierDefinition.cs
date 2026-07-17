using System;
using UnityEngine.Scripting.APIUpdating;

namespace POB.Extraction
{
    // M2 SW.2 负重档位：MinLoadPercent 是该档生效的负重比例下限(含)，MovementPercentModifier 是
    // 移速的 PercentMult 修正值(如 -0.3 = 降 30%)，DisablesDoubleJump 为真时二段跳被压到 1 次。
    // 0% 档通常 MovementPercentModifier=0/DisablesDoubleJump=false，代表"无惩罚"基线。
    [Serializable]
    [MovedFrom(true, sourceAssembly: "POB.Runtime")]
    public class ExtractionEncumbranceTierDefinition
    {
        public int MinLoadPercent;
        public float MovementPercentModifier;
        public bool DisablesDoubleJump;

        public bool IsValid => MinLoadPercent >= 0 && MovementPercentModifier > -1f;

        public ExtractionEncumbranceTierDefinition(
            int minLoadPercent,
            float movementPercentModifier,
            bool disablesDoubleJump)
        {
            MinLoadPercent = minLoadPercent;
            MovementPercentModifier = movementPercentModifier;
            DisablesDoubleJump = disablesDoubleJump;
        }
    }
}
