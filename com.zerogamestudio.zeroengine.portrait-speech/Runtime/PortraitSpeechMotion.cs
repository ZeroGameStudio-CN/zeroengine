using UnityEngine;

namespace ZeroEngine.PortraitSpeech
{
    /// <summary>Cosmetic, text-driven speech shared by runtime portraits and their authoring preview.</summary>
    public sealed class PortraitSpeechMotion
    {
        private float nextPoseAt,poseChangedAt;
        private int pose,previousPose;
        public float SpeechUntil{get;private set;}
        public float Openness{get;private set;}
        public float Blend{get;private set;}=1;
        public int CharacterIndex{get;private set;}=-1;
        public int Frame=>Openness>.12f?pose:0;
        public int PreviousFrame=>previousPose;

        public void Reveal(char character,int index,float now,float framesPerSecond,float hold)
        {
            if(char.IsPunctuation(character)||char.IsWhiteSpace(character)||char.IsControl(character)){Complete(false);return;}
            if(char.IsSurrogate(character)||char.GetUnicodeCategory(character)==System.Globalization.UnicodeCategory.NonSpacingMark)return;
            if(index<CharacterIndex)Reset();
            SpeechUntil=now+Mathf.Max(.03f,hold);
            CharacterIndex=index;
            if(now<nextPoseAt&&pose!=0)return;
            int next=SelectPose(character,index);
            if(next!=pose){previousPose=pose;pose=next;poseChangedAt=now;Blend=0;}
            nextPoseAt=now+1/Mathf.Clamp(framesPerSecond,1,24);
        }

        public void Advance(float now,float delta,float openTime,float closeTime,float transitionTime)
        {
            bool speaking=now<SpeechUntil;
            float duration=speaking?openTime:closeTime;
            Openness=Mathf.MoveTowards(Openness,speaking?1:0,Mathf.Max(0,delta)/Mathf.Max(.01f,duration));
            Blend=Mathf.SmoothStep(0,1,Mathf.Clamp01((now-poseChangedAt)/Mathf.Max(.01f,transitionTime)));
            if(!speaking&&Openness==0){pose=previousPose=0;CharacterIndex=-1;Blend=1;}
        }

        public void Complete(bool immediate)
        {
            SpeechUntil=nextPoseAt=0;
            if(immediate)Reset();
        }

        public void Reset()
        {
            SpeechUntil=nextPoseAt=poseChangedAt=Openness=0;pose=previousPose=0;CharacterIndex=-1;Blend=1;
        }

        public static int SelectPose(char character,int index)
        {
            switch(char.ToLowerInvariant(character))
            {
                case 'a':return 2;
                case 'o':case 'u':return 3;
                case 'e':case 'i':return 4;
                case 'm':case 'b':case 'p':return 5;
                default:return character<128?1:1+(character+index*3)%5;
            }
        }
    }
}
