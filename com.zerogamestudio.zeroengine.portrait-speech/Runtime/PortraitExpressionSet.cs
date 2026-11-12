using System;
using UnityEngine;

namespace ZeroEngine.PortraitSpeech
{
    public enum PortraitExpressionEyes
    {
        Unspecified,
        Open,
        Closed
    }

    [Serializable]
    public struct PortraitExpressionTransitionSettings
    {
        [Range(.04f,.25f)] public float closeEyes;
        [Range(.04f,.25f)] public float openEyes;
        [Range(.025f,.14f)] public float faceBlend;
        [Range(.02f,.2f)] public float mouthBlend;

        public static PortraitExpressionTransitionSettings Default => new()
        {
            closeEyes=.10f,openEyes=.10f,faceBlend=.065f,mouthBlend=.065f
        };
    }

    [Serializable]
    public struct PortraitExpression
    {
        [Tooltip("Stable dialogue/content key, such as neutral, smile or curious.")]
        public string key;
        public Sprite sprite;
        [Tooltip("Matching non-empty key for portraits with the same outfit and framing.")]
        public string transitionGroup;
        public PortraitExpressionEyes transitionEyes;
    }

    [Serializable]
    public struct PortraitExpressionBridge
    {
        [Tooltip("Exact source expression key.")]
        public string from;
        [Tooltip("Exact destination expression key.")]
        public string to;
        [Tooltip("One transparent RGBA patch at native portrait pixel size. Paint only the eyes and brows.")]
        public Texture2D patch;
        [Tooltip("Patch origin in untrimmed source pixels, measured from the bottom left.")]
        public Vector2Int bottomLeft;
    }

    [CreateAssetMenu(menuName="ZeroEngine/UI/Portrait Expression Set")]
    public sealed class PortraitExpressionSet : ScriptableObject
    {
        public int actorId;
        public PortraitSpeechLibrary library;
        public PortraitExpression[] expressions=Array.Empty<PortraitExpression>();
        public PortraitExpressionTransitionSettings transition=PortraitExpressionTransitionSettings.Default;
        public PortraitExpressionBridge[] bridges=Array.Empty<PortraitExpressionBridge>();

        internal static bool FitsCanvas(Sprite a,Sprite b) =>
            a&&b&&a.rect.size==b.rect.size&&a.pivot==b.pivot&&Mathf.Approximately(a.pixelsPerUnit,b.pixelsPerUnit)
            &&(!a.packed||a.packingRotation==SpritePackingRotation.None)
            &&(!b.packed||b.packingRotation==SpritePackingRotation.None);

        internal static bool HasCalibratedBlink(Sprite sprite,PortraitSpeechProfile profile)
        {
            if(!sprite)return false;
            var blink=profile.blink;blink.playbackDisabled=false;
            return blink.CanRender(PortraitBlinkMaterial.CanvasSize(sprite));
        }

        internal PortraitExpressionBridge FindBridge(string from,string to,Vector2Int canvas)
        {
            if(bridges==null)return default;
            int match=-1;
            for(int i=0;i<bridges.Length;i++)
            {
                var item=bridges[i];
                if(!string.Equals(item.from,from,StringComparison.Ordinal)
                    ||!string.Equals(item.to,to,StringComparison.Ordinal))continue;
                if(match>=0)return default;
                match=i;
            }
            if(match<0)return default;
            var bridge=bridges[match];
            return bridge.patch&&bridge.bottomLeft.x>=0&&bridge.bottomLeft.y>=0
                &&bridge.bottomLeft.x<=canvas.x-bridge.patch.width
                &&bridge.bottomLeft.y<=canvas.y-bridge.patch.height?bridge:default;
        }

        public bool TryGet(string key,out PortraitExpression expression,out PortraitSpeechProfile profile)
        {
            expression=default;profile=default;
            if(string.IsNullOrWhiteSpace(key)||expressions==null)return false;
            int found=-1;
            for(int i=0;i<expressions.Length;i++)
                if(string.Equals(expressions[i].key,key,StringComparison.Ordinal))
                {if(found>=0)return false;found=i;}
            if(found<0||!expressions[found].sprite)return false;
            expression=expressions[found];
            if(!library||!library.TryGetPortraitProfile(actorId,expression.sprite.name,out profile))
                profile=new PortraitSpeechProfile{actorId=actorId,spriteName=expression.sprite.name};
            return true;
        }
    }
}
