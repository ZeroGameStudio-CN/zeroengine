using System;
using System.Collections.Generic;
using UnityEngine;

namespace ZeroEngine.Tutorial.Guidance
{
    public readonly struct TutorialMaskHole
    {
        public readonly Rect Rect;
        public readonly TutorialMaskShape Shape;
        public readonly float Radius;
        public readonly bool Border;
        public TutorialMaskHole(Rect rect, TutorialMaskShape shape, float radius, bool border = true)
        { Rect = rect; Shape = shape; Radius = radius; Border = border; }
    }

    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class TutorialMaskGraphic : UnityEngine.UI.MaskableGraphic
    {
        public const int MaximumHoles = 16;
        private readonly Vector4[] rectangles = new Vector4[MaximumHoles];
        private readonly Vector4[] shapes = new Vector4[MaximumHoles];
        private Material ownedMaterial;

        public void Configure(IReadOnlyList<TutorialMaskHole> holes, TutorialFocusStyle style)
        {
            if (holes.Count > MaximumHoles) throw new InvalidOperationException("Too many tutorial target/exclusion holes.");
            if (!ownedMaterial)
            {
                var shader = Resources.Load<Shader>("ZeroEngine/Tutorial/TutorialMask");
                if (!shader) throw new InvalidOperationException("The tutorial mask shader is unavailable.");
                ownedMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                material = ownedMaterial;
                canvasRenderer.cullTransparentMesh = false;
            }
            for (int index = 0; index < holes.Count; index++)
            {
                var hole = holes[index];
                rectangles[index] = new Vector4(hole.Rect.center.x, hole.Rect.center.y, hole.Rect.width * .5f, hole.Rect.height * .5f);
                shapes[index] = new Vector4((int)hole.Shape, hole.Radius, hole.Border ? 1 : 0, 0);
            }
            color = style.Shade;
            ownedMaterial.SetInt("_HoleCount", holes.Count);
            ownedMaterial.SetVectorArray("_Holes", rectangles);
            ownedMaterial.SetVectorArray("_Shapes", shapes);
            ownedMaterial.SetColor("_BorderColor", style.Border);
            ownedMaterial.SetFloat("_BorderWidth", style.BorderWidth);
            ownedMaterial.SetFloat("_Feather", Mathf.Max(.01f, style.Feather));
        }

        protected override void OnDestroy()
        {
            if (ownedMaterial) TutorialObjectLifetime.Destroy(ownedMaterial);
            base.OnDestroy();
        }
    }
}
