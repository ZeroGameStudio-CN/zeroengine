using System;
using System.Collections.Generic;
using System.Numerics;
namespace ZeroEngine.Fishing
{
    public readonly struct FishCandidate
    {
        public readonly int Id;
        public readonly Vector3 Position;
        public readonly float Width;
        public readonly bool Available;
        public FishCandidate(int id,Vector3 position,float width,bool available) {Id=id;Position=position;Width=width;Available=available;}
    }
    public static class SchoolSteering
    {
        // Read one stable snapshot for the whole step. Renderers and physics queries belong to the caller.
        public static Vector2 Calculate(int index,IReadOnlyList<Vector3> positions,IReadOnlyList<Vector2> velocities,
            IReadOnlyList<int> groups,IReadOnlyList<bool> active,FishSwimSettings settings)
        {
            if(settings==null||!settings.Enabled||!active[index]) return Vector2.Zero;
            Vector2 influence=Vector2.Zero,center=Vector2.Zero,alignment=Vector2.Zero;int count=0;
            for(int j=0;j<positions.Count;j++)
            {
                if(j==index||!active[j])continue;
                Vector2 gap=FishMath.XY(positions[index]-positions[j]);float distance=gap.Length();
                if(distance<.001f||distance>settings.NeighbourRadius)continue;
                if(distance<settings.PersonalSpace)influence+=gap/distance*(1-distance/Math.Max(.05f,settings.PersonalSpace))*1.5f;
                if(groups[index]>=0&&groups[index]==groups[j]) {center+=FishMath.XY(positions[j]);alignment+=velocities[j];count++;}
            }
            if(count>0) influence+=(center/count-FishMath.XY(positions[index]))*.2f+(alignment/count-velocities[index])*.16f;
            return influence;
        }
        // Reward selection stays outside this package. Keep the already-selected winner and invite neighbours.
        public static int[] SelectCandidates(int winner,Vector3 bait,IReadOnlyList<FishCandidate> fish,int capacity=3,int largeLimit=1)
        {
            var selected=new List<int>();int large=0;
            for(int i=0;i<fish.Count;i++) if(fish[i].Id==winner) {selected.Add(winner);large=fish[i].Width>1?1:0;break;}
            if(selected.Count==0)throw new ArgumentException("The winner must be an existing candidate.");
            while(selected.Count<Math.Max(1,capacity))
            {
                int best=-1;float distance=float.PositiveInfinity;
                for(int i=0;i<fish.Count;i++)
                {
                    var candidate=fish[i];
                    if(!candidate.Available||selected.Contains(candidate.Id)||candidate.Width>1&&large>=Math.Max(1,largeLimit))continue;
                    float next=Vector3.DistanceSquared(candidate.Position,bait);
                    if(next<distance){best=i;distance=next;}
                }
                if(best<0)break;
                selected.Add(fish[best].Id);if(fish[best].Width>1)large++;
            }
            return selected.ToArray();
        }
    }
}
