using System;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace ZeroEngine.PortraitSpeech
{
    [Serializable]
    [MovedFrom(true,"POB","POB.Runtime","DialogueTextStyle+PortraitSpeech")]
    public struct PortraitSpeechProfile
    {
        public int actorId;
        public string spriteName;
        [Tooltip("Normalized source-canvas rectangle, origin at bottom left.")]
        public Rect mouth;
        public Vector2 skinSample;
        [Tooltip("Sample the same source row at the configured skin column, to preserve a vertical cheek tint.")]
        public bool sampleSkinByRow;
        [Tooltip("Optional native-size patches: half open on the bottom, near closed on top. Transparent pixels keep the original art.")]
        public Texture2D mouthPoseSheet;
        public Color mouthColor,tongueColor,teethColor;
        [Tooltip("Explicit source-pixel silhouettes, useful when small portraits collapse several normalized poses into one.")]
        public bool usePixelPoses;
        [Tooltip("Source-pixel width, height, horizontal offset, vertical offset from the mouth-region center.")]
        public Vector4 lightPixels,openPixels,roundPixels,widePixels,tightPixels;
        public PortraitBlinkProfile blink;
        [Tooltip("Pause playback while retaining the mouth calibration.")]
        public bool speechDisabled;
        public bool HasMouth => mouth.width>0&&mouth.height>0;
        public bool CanSpeak => HasMouth&&!speechDisabled;
    }

    [CreateAssetMenu(menuName="ZeroEngine/UI/Portrait Speech Library")]
    public class PortraitSpeechLibrary : ScriptableObject
    {
        public float portraitSpeechFramesPerSecond=9,portraitSpeechHold=.12f;
        public float portraitMouthOpenTime=.035f,portraitMouthCloseTime=.065f,portraitMouthTransition=.045f;
        public PortraitBlinkTiming blinkTiming=PortraitBlinkTiming.Default;
        public PortraitSpeechProfile[] portraitSpeech=Array.Empty<PortraitSpeechProfile>();
#if UNITY_EDITOR
        [HideInInspector] public string portraitAuthoringStateJson;
#endif
        public bool TryGetPortraitProfile(int actorId,string spriteName,out PortraitSpeechProfile result)
        {
            if(portraitSpeech!=null&&!string.IsNullOrEmpty(spriteName))foreach(var entry in portraitSpeech)
                if(entry.actorId==actorId&&entry.spriteName==spriteName){result=entry;return true;}
            result=default;return false;
        }
        public bool TryGetPortraitSpeech(int actorId,Sprite sprite,out PortraitSpeechProfile result)
        {
            if(TryGetPortraitProfile(actorId,sprite?sprite.name:null,out result)&&result.CanSpeak)return true;
            result=default;return false;
        }
        public virtual bool TryGetFaceBounds(int actorId,string spriteName,out Rect face)
        {
            face=default;return false;
        }
        /// <summary>Allows a host to author an opt-in portrait without replacing its production database art.</summary>
        public virtual Sprite ResolveAuthoringPortrait(int actorId,Sprite source)=>source;
    }
}
