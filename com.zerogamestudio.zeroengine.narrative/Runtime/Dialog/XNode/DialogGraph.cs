#if XNODE_PRESENT
using UnityEngine;
using XNode;
using System.Linq;

namespace ZeroEngine.Dialog.XNodeIntegration
{
    [CreateAssetMenu(fileName = "New Dialog Graph", menuName = "ZeroEngine/Narrative/Dialogue/XNode Graph")]
    public class DialogGraph : NodeGraph
    {
        public BaseDialogNode GetStartNode()
        {
            return nodes.OfType<StartNode>().FirstOrDefault();
        }
    }
}
#endif
