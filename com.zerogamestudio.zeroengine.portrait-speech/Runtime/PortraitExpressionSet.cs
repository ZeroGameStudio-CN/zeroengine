using System;
using UnityEngine;

namespace ZeroEngine.PortraitSpeech
{
    [Serializable]
    public struct PortraitExpression
    {
        [Tooltip("Stable dialogue/content key, such as neutral, smile or curious.")]
        public string key;
        public Sprite sprite;
    }

    [CreateAssetMenu(menuName="ZeroEngine/UI/Portrait Expression Set")]
    public sealed class PortraitExpressionSet : ScriptableObject
    {
        public int actorId;
        public PortraitSpeechLibrary library;
        public PortraitExpression[] expressions=Array.Empty<PortraitExpression>();

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
