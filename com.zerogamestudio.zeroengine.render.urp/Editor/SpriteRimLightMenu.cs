using System;
using UnityEditor;
using UnityEngine;

namespace ZeroEngine.Render.Urp.Editor
{
    /// <summary>Explicit authoring entry; ordinary scene lights stay unmarked.</summary>
    public static class SpriteRimLightMenu
    {
        [MenuItem("GameObject/Light/角色边缘点光", false, 20)]
        private static void CreatePoint(MenuCommand command) => Create(LightType.Point, command.context as GameObject);

        [MenuItem("GameObject/Light/角色边缘聚光", false, 21)]
        private static void CreateSpot(MenuCommand command) => Create(LightType.Spot, command.context as GameObject);

        public static GameObject Create(LightType type, GameObject parent = null)
        {
            if (type != LightType.Point && type != LightType.Spot)
                throw new ArgumentOutOfRangeException(nameof(type), "Actor rim sources support Point and Spot lights.");
            var host = new GameObject(type == LightType.Point ? "角色边缘点光" : "角色边缘聚光");
            Undo.RegisterCreatedObjectUndo(host, "创建角色边缘灯");
            GameObjectUtility.SetParentAndAlign(host, parent);
            var light = Undo.AddComponent<Light>(host);
            light.type = type;
            light.color = new Color(.109434f, .717042f, 1f);
            light.intensity = 10f;
            light.range = 8f;
            light.shadows = LightShadows.None;
            if (type == LightType.Spot) { light.spotAngle = 60f; light.innerSpotAngle = 40f; }
            Undo.AddComponent<SpriteRimLightSource>(host);
            Selection.activeGameObject = host;
            return host;
        }
    }
}
