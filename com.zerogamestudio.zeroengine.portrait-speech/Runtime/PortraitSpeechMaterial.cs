using UnityEngine;
using UnityEngine.UI;

namespace ZeroEngine.PortraitSpeech
{
    public static class PortraitSpeechMaterial
    {
        public const string ResourceName="DialoguePortraitSignal";
        public const string ShaderName="ZeroEngine/UI/PortraitSpeech";
        public static void Apply(Image image,Material material,PortraitSpeechProfile? profile,PortraitSpeechMotion motion)
        {
            if(!image||!material)return;
            var sprite=image.overrideSprite;
            material.SetFloat("_MouthFrame",profile?.CanSpeak==true&&motion!=null?motion.Frame:0);
            material.SetFloat("_MouthPrevious",motion?.PreviousFrame??0);material.SetFloat("_MouthOpen",motion?.Openness??0);material.SetFloat("_MouthBlend",motion?.Blend??1);
            ApplyProfile(material,profile);
            material.SetVector("_SourceSize",sprite?new Vector4(sprite.rect.width,sprite.rect.height,0,0):Vector4.one);
            material.SetVector("_SpritePadding",sprite?UnityEngine.Sprites.DataUtility.GetPadding(sprite):Vector4.zero);
            material.SetVector("_UvRect",sprite?UnityEngine.Sprites.DataUtility.GetOuterUV(sprite):new Vector4(0,0,1,1));
        }
        public static void ApplyProfile(Material material,PortraitSpeechProfile? profile)
        {
            if(!material)return;
            var entry=profile??default;
            material.SetVector("_MouthRect",new Vector4(entry.mouth.x,entry.mouth.y,entry.mouth.width,entry.mouth.height));
            material.SetVector("_SkinSample",new Vector4(entry.skinSample.x,entry.skinSample.y,0,0));
            material.SetFloat("_SampleSkinByRow",entry.sampleSkinByRow?1:0);
            const string drawnKeyword="PORTRAIT_DRAWN_MOUTH";
            bool drawn=entry.mouthPoseSheet;
            if(material.IsKeywordEnabled(drawnKeyword)!=drawn)
            {
                if(drawn)material.EnableKeyword(drawnKeyword);
                else material.DisableKeyword(drawnKeyword);
            }
            material.SetTexture("_MouthPoseSheet",entry.mouthPoseSheet);
            material.SetColor("_MouthColor",entry.mouthColor);material.SetColor("_TongueColor",entry.tongueColor);material.SetColor("_TeethColor",entry.teethColor);
            material.SetFloat("_UsePixelPoses",entry.usePixelPoses?1:0);
            material.SetVector("_PixelLight",entry.lightPixels);material.SetVector("_PixelOpen",entry.openPixels);material.SetVector("_PixelRound",entry.roundPixels);material.SetVector("_PixelWide",entry.widePixels);material.SetVector("_PixelTight",entry.tightPixels);
        }
    }
}
