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

    [Serializable]
    public struct PortraitExpressionGroup
    {
        public int actorId;
        [Tooltip("Only use a shared prefix for the same outfit and registered framing.")]
        public string spriteNamePrefix;
        [Tooltip("Authored closed or squinting poses; keep them static outside transitions.")]
        public Sprite[] closedPoses;
        [Tooltip("Poses sharing the prefix but changing the body's placement or composition.")]
        public Sprite[] excludedPoses;

        internal bool Contains(Sprite sprite)
        {
            if(!sprite||string.IsNullOrWhiteSpace(spriteNamePrefix)
                ||!sprite.name.StartsWith(spriteNamePrefix,StringComparison.Ordinal))return false;
            if(excludedPoses!=null)foreach(var excluded in excludedPoses)if(excluded==sprite)return false;
            return true;
        }
        internal PortraitExpressionEyes EyeState(Sprite sprite,PortraitSpeechProfile profile)
        {
            if(closedPoses!=null)foreach(var pose in closedPoses)
                if(pose==sprite)return PortraitExpressionEyes.Closed;
            return PortraitExpressionSet.HasCalibratedBlink(sprite,profile)
                ?PortraitExpressionEyes.Open:PortraitExpressionEyes.Unspecified;
        }
    }

    [CreateAssetMenu(menuName="ZeroEngine/UI/Portrait Speech Library")]
    public class PortraitSpeechLibrary : ScriptableObject
    {
        public float portraitSpeechFramesPerSecond=9,portraitSpeechHold=.12f;
        public float portraitMouthOpenTime=.035f,portraitMouthCloseTime=.065f,portraitMouthTransition=.045f;
        public PortraitBlinkTiming blinkTiming=PortraitBlinkTiming.Default;
        public PortraitSpeechProfile[] portraitSpeech=Array.Empty<PortraitSpeechProfile>();
        [Tooltip("Opt-in groups for automatic transitions. Hosts still decide when to request one.")]
        public PortraitExpressionGroup[] expressionGroups=Array.Empty<PortraitExpressionGroup>();
#if UNITY_EDITOR
        [HideInInspector] public string portraitAuthoringStateJson;
#endif
        public bool TryGetPortraitProfile(int actorId,string spriteName,out PortraitSpeechProfile result)
        {
            if(portraitSpeech!=null&&!string.IsNullOrEmpty(spriteName))foreach(var entry in portraitSpeech)
                if(entry.actorId==actorId&&entry.spriteName==spriteName){result=entry;return true;}
            result=default;return false;
        }
        /// <summary>Resolve one unambiguous same-outfit pair, without changing either portrait or profile.</summary>
        public bool TryResolveExpressionPair(int actorId,Sprite from,Sprite to,
            out PortraitExpressionEyes fromEyes,out PortraitExpressionEyes toEyes)
        {
            fromEyes=toEyes=PortraitExpressionEyes.Unspecified;
            if(from==to||expressionGroups==null||!PortraitExpressionSet.FitsCanvas(from,to))return false;
            int a=-1,b=-1;
            for(int i=0;i<expressionGroups.Length;i++)
            {
                var group=expressionGroups[i];
                if(group.actorId!=actorId)continue;
                if(group.Contains(from)){if(a>=0)return false;a=i;}
                if(group.Contains(to)){if(b>=0)return false;b=i;}
            }
            if(a<0||a!=b||!TryGetPortraitProfile(actorId,from.name,out var source)
                ||!TryGetPortraitProfile(actorId,to.name,out var target))return false;
            var selected=expressionGroups[a];
            var sourceEyes=selected.EyeState(from,source);
            var targetEyes=selected.EyeState(to,target);
            // A calibrated open endpoint supplies the eyelid movement. With two
            // static poses there is no automatic blink to hide the switch.
            if(sourceEyes==PortraitExpressionEyes.Unspecified||targetEyes==PortraitExpressionEyes.Unspecified
                ||sourceEyes==PortraitExpressionEyes.Closed&&targetEyes==PortraitExpressionEyes.Closed)return false;
            fromEyes=sourceEyes;toEyes=targetEyes;return true;
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
