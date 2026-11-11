using System.Collections.Generic;
namespace ZeroEngine.Render
{
    /// <summary>Fair, bounded dirty uploads; generation tickets reject previous pooled lives.</summary>
    public sealed class BloodSurfaceUploadQueue
    {
        private struct Ticket {public BloodSurface Surface;public uint Generation;}
        private readonly Queue<Ticket> pending=new(256);
        private int frame=-1;
        public int MaximumPerFrame {get;set;}=8;
        public int Count=>pending.Count;
        public int TotalUploads {get;private set;}
        public int MaximumObserved {get;private set;}
        public void Enqueue(BloodSurface surface)
        {
            if(surface==null || surface.UploadQueued)return;
            if(pending.Count>=256){var old=pending.Dequeue();if(old.Surface.Generation==old.Generation)old.Surface.UploadQueued=false;}
            surface.UploadQueued=true;pending.Enqueue(new Ticket{Surface=surface,Generation=surface.Generation});
        }
        public void Pump(int frameNumber)
        {
            if(frame==frameNumber)return;frame=frameNumber;
            int available=pending.Count,done=0,budget=UnityEngine.Mathf.Clamp(MaximumPerFrame,1,128);
            for(int i=0;i<available&&done<budget;i++)
            {
                var ticket=pending.Dequeue();var surface=ticket.Surface;
                if(surface.Generation!=ticket.Generation || !surface.UploadQueued)continue;
                surface.UploadQueued=false;if(!surface.CanUpload)continue;
                surface.FlushPending();done++;TotalUploads++;
            }
            MaximumObserved=System.Math.Max(MaximumObserved,done);
        }
        public void Clear()
        {
            foreach(var ticket in pending)if(ticket.Surface.Generation==ticket.Generation)ticket.Surface.UploadQueued=false;
            pending.Clear();frame=-1;
        }
    }
}
