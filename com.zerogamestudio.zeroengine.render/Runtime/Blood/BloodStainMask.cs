using System;
using UnityEngine;

namespace ZeroEngine.Render
{
    // Persistent dye in source-colour space. Animation poses/atlas positions never select the stained regions.
    public class BloodStainMask
    {
        public const int Size=64, ColourSteps=16, MaximumAlpha=220, LightMaximumAlpha=180;
        public readonly Color32[] Pixels=new Color32[Size*Size];
        private readonly float[] pendingGroups=new float[8];
        private readonly uint patternSeed;
        private uint splashSequence;
        private float strength,age,fold=1;
        private int pendingEvents;
        public float Fade=>Mathf.Clamp01((8-age)/5);
        public float Intensity=>strength*Fade;
        public int Tier=>Intensity<=0?0:Intensity<.28f?1:Intensity<.65f?2:3;
        public int Revision { get; private set; }
        public int LastUpdatedColours { get; private set; }
        public int Deposits { get; private set; }
        public BloodStainMask(uint seed)
        {
            if(seed==0)seed=1;
            patternSeed=seed;
        }
        public static int ColourIndex(Color colour)
        {
            int r=Mathf.RoundToInt(Mathf.Clamp01(colour.r)*15),g=Mathf.RoundToInt(Mathf.Clamp01(colour.g)*15),b=Mathf.RoundToInt(Mathf.Clamp01(colour.b)*15);
            return r+g*16+b*256;
        }
        public bool HasPending=>pendingEvents>0;
        public float DisplayFade=>Fade*fold;
        public void ResetForPool()
        {
            Array.Clear(Pixels,0,Pixels.Length);Array.Clear(pendingGroups,0,pendingGroups.Length);
            strength=age=0;fold=1;pendingEvents=0;Revision=LastUpdatedColours=Deposits=0;
            splashSequence=(splashSequence+1u)&~1u;
        }
        public void Deposit(float amount)
        {
            if(amount<=0 || float.IsNaN(amount) || float.IsInfinity(amount))return;
            if(Fade<=0)
            {
                for(int i=0;i<pendingGroups.Length;i++)pendingGroups[i]=0;
                pendingEvents=0;
                // A new blood cycle starts with a complete pair, using the next random permutation.
                splashSequence=(splashSequence+1u)&~1u;
            }
            float current=Intensity;
            fold*=Fade;strength=Mathf.Clamp01(current+amount);age=0;
            // Four small deposits per splash. Each pair visits all eight groups once, in seeded order.
            uint shot=splashSequence++;
            uint order=Hash(unchecked((int)(shot>>1)),0,0,patternSeed);
            int offset=(int)(order&7u),stride=(int)((order>>3)&3u)*2+1,start=(int)(shot&1u)*4;
            for(int i=0;i<4;i++)
            {
                int group=((start+i)*stride+offset)&7;
                pendingGroups[group]=Mathf.Min(1,pendingGroups[group]+amount*2);
            }
            pendingEvents=Mathf.Min(8,pendingEvents+1);
            Deposits++;
        }
        public void Advance(float delta){if(float.IsFinite(delta)&&delta>0)age+=delta;}
        public bool Flush()
        {
            if(pendingEvents==0)return false;
            for(int i=0;i<Pixels.Length;i++)
            {
                float r=(i&15)/15f,g=((i>>4)&15)/15f,b=((i>>8)&15)/15f;
                float value=Mathf.Max(r,Mathf.Max(g,b));
                float chroma=value-Mathf.Min(r,Mathf.Min(g,b));
                uint key=Hash(i&15,(i>>4)&15,(i>>8)&15,patternSeed);
                float grain=(key&65535u)/65535f;
                int group=(int)((key>>16)&7u);
                float incoming=Mathf.Clamp01(pendingGroups[group]*1.8f*Mathf.Lerp(.65f,1,grain));
                float previous=Pixels[i].a/255f*fold;
                float alpha=1-(1-previous)*(1-incoming);
                // Bright neutral/cool hair stays translucent; near-black contours remain legible.
                int cap=value<.13f?42:(value>.65f && (chroma<.24f || b>r+.04f)?LightMaximumAlpha:MaximumAlpha);
                byte shade=(byte)Mathf.RoundToInt(grain*255);
                Pixels[i]=new Color32(shade,shade,shade,(byte)Mathf.Min(cap,Mathf.RoundToInt(alpha*255)));
            }
            for(int i=0;i<pendingGroups.Length;i++)pendingGroups[i]=0;
            LastUpdatedColours=Pixels.Length;pendingEvents=0;fold=1;Revision++;
            return true;
        }
        private static uint Hash(int x,int y,int z,uint seed)
        {
            uint value=unchecked((uint)x*374761393u+(uint)y*668265263u+(uint)z*2246822519u+seed*747796405u);
            value=(value^(value>>13))*1274126177u;return value^(value>>16);
        }
    }
}
