using System;
namespace ZeroEngine.Fishing
{
    public enum BaitPhase { Airborne, Approaching, Inspecting, Nibbling, ReadyToHook, Cancelled }
    // A physical contact gate, independent of reward selection and of the player's optional QTE.
    public sealed class BaitEncounter
    {
        private float inspectDuration,nibbleDuration,age,phaseAge;
        public int FishId {get;private set;}=-1;
        public BaitPhase Phase {get;private set;}=BaitPhase.Cancelled;
        public bool TargetsHook => Phase==BaitPhase.Nibbling||Phase==BaitPhase.ReadyToHook;
        public bool ReadyToHook => Phase==BaitPhase.ReadyToHook;
        public float Nibble => Phase==BaitPhase.Nibbling?(float)Math.Sin(Math.PI*FishMath.Clamp01(phaseAge/nibbleDuration)):0;
        public float Progress => ReadyToHook?1:Phase==BaitPhase.Nibbling?.75f+.25f*FishMath.Clamp01(phaseAge/nibbleDuration)
            :Phase==BaitPhase.Inspecting?.75f*FishMath.Clamp01(phaseAge/inspectDuration):0;
        public float Timeout {get;set;}=20;
        public void Begin(int winner,float inspectSeconds,float nibbleSeconds=.4f)
        {
            if(winner<0||!FishMath.Finite(inspectSeconds)||!FishMath.Finite(nibbleSeconds)||inspectSeconds<=0||nibbleSeconds<=0)
                throw new ArgumentException("Use an existing fish and finite positive encounter durations.");
            FishId=winner;inspectDuration=inspectSeconds;nibbleDuration=nibbleSeconds;age=phaseAge=0;Phase=BaitPhase.Airborne;
        }
        public void Land() {if(Phase==BaitPhase.Airborne)Phase=BaitPhase.Approaching;}
        public void Advance(float delta,bool winnerAtInspectSlot,bool companionAtInspectSlot,bool mouthAtHook)
        {
            if(!FishMath.Finite(delta)||delta<=0||Phase==BaitPhase.Airborne||Phase==BaitPhase.Cancelled||ReadyToHook)return;
            age+=delta;
            if(age>Math.Max(inspectDuration+nibbleDuration,Timeout)) {Cancel();return;}
            if(Phase==BaitPhase.Approaching)
            {
                if(winnerAtInspectSlot){Phase=BaitPhase.Inspecting;phaseAge=0;}
            }
            else if(Phase==BaitPhase.Inspecting)
            {
                if(winnerAtInspectSlot)phaseAge+=delta;
                // A slow neighbour cannot deadlock an otherwise reachable hook.
                if(phaseAge>=inspectDuration&&(companionAtInspectSlot||age>=inspectDuration*2)) {Phase=BaitPhase.Nibbling;phaseAge=0;}
            }
            else if(Phase==BaitPhase.Nibbling&&mouthAtHook)
            {
                phaseAge+=delta;
                if(phaseAge>=nibbleDuration) {Phase=BaitPhase.ReadyToHook;phaseAge=nibbleDuration;}
            }
        }
        public void Cancel() {Phase=BaitPhase.Cancelled;FishId=-1;age=phaseAge=0;}
    }
}
