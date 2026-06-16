using UnityEngine;

namespace ZeroEngine.UI.Combat
{
    public interface IBattleFloatingTextPresenter
    {
        void ShowDamage(Vector3 worldPosition, int amount, bool critical = false, Transform followTarget = null);
        void ShowHeal(Vector3 worldPosition, int amount, Transform followTarget = null);
        void ShowStatus(Vector3 worldPosition, string text, Color color, Transform followTarget = null);
    }
}
