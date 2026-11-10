using UnityEngine;

namespace ZeroEngine.PortraitSpeech
{
    /// <summary>Presentation-clock blink with held closure and bounded local timing variation.</summary>
    public static class PortraitBlinkMotion
    {
        public static float Evaluate(float elapsed,PortraitBlinkTiming timing)=>
            Evaluate(elapsed,timing.firstDelay,timing.interval,timing.duration,timing.closedHold,timing.intervalVariation);

        public static float Evaluate(float elapsed,float firstDelay,float interval,float duration,float closedHold,float intervalVariation)
        {
            if(float.IsNaN(elapsed)||float.IsInfinity(elapsed)||elapsed<0)return 0;
            interval=Mathf.Max(.5f,interval);
            duration=Mathf.Clamp(duration,.06f,Mathf.Min(.5f,interval*.35f));
            closedHold=Mathf.Clamp(closedHold,0,duration*.4f);
            firstDelay=Mathf.Max(0,firstDelay);intervalVariation=Mathf.Clamp(intervalVariation,0,.3f);
            int nearest=Mathf.FloorToInt((elapsed-firstDelay)/interval);
            // Adjacent candidates cover early/late blinks without queues or catch-up bursts.
            for(int cycle=Mathf.Max(0,nearest-1);cycle<=nearest+1;cycle++)
            {
                float offset=cycle==0?0:Variation(cycle)*interval*intervalVariation;
                float age=elapsed-(firstDelay+cycle*interval+offset);
                if(age<0||age>=duration)continue;
                float close=(duration-closedHold)*.3f;
                if(age<close)return Mathf.SmoothStep(0,1,age/close);
                if(age<close+closedHold)return 1;
                return 1-Mathf.SmoothStep(0,1,(age-close-closedHold)/(duration-close-closedHold));
            }
            return 0;
        }

        private static float Variation(int cycle)
        {
            unchecked
            {
                uint value=(uint)cycle*747796405u+2891336453u;
                value=(value^(value>>16))*2246822519u;value^=value>>13;
                return (value&0x00ffffffu)/8388607.5f-1;
            }
        }
    }
}
