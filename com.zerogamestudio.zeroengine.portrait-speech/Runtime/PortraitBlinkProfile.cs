using System;
using UnityEngine;

namespace ZeroEngine.PortraitSpeech
{
    [Serializable]
    public struct PortraitBlinkEye
    {
        [Tooltip("Full source canvas, normalized from bottom left.")]
        public Rect area;
        public Vector2 skinSample;
        [Range(.15f,.85f)] public float closedHeight;
        [Range(-.4f,.4f)] public float tilt;
        [Range(-.2f,.2f)] public float curve;

        public bool IsConfigured => area.width>0&&area.height>0&&area.xMin>=0&&area.yMin>=0&&area.xMax<=1.00001f&&area.yMax<=1.00001f;
    }

    [Serializable]
    public struct PortraitBlinkProfile
    {
        public bool enabled;
        public PortraitBlinkEye leftEye,rightEye;
        public Color lidColor;
        [Tooltip("Original canvas size and protected pixel coordinates, with a bottom-left origin.")]
        public Vector2Int maskCanvasSize;
        public Vector2Int[] protectedPixels;

        [Tooltip("Pause playback while retaining the eyelid calibration.")]
        public bool playbackDisabled;
        public bool HasEyes => leftEye.IsConfigured||rightEye.IsConfigured;
        public bool CanBlink => enabled&&HasEyes&&!playbackDisabled;
        public bool CanRender(Vector2Int canvasSize) => CanBlink&&
            (protectedPixels==null||protectedPixels.Length==0||maskCanvasSize==canvasSize);
    }

    [Serializable]
    public struct PortraitBlinkTiming
    {
        [Min(0)] public float firstDelay;
        [Min(.5f)] public float interval;
        [Range(.06f,.5f)] public float duration;
        [Min(0)] public float closedHold;
        [Range(0,.3f)] public float intervalVariation;
        public static PortraitBlinkTiming Default => new(){firstDelay=2.8f,interval=4.2f,duration=.22f,closedHold=.035f,intervalVariation=.22f};
    }
}
