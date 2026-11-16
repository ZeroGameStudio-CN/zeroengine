using System;
using System.IO;
using UnityEngine;

namespace ZeroEngine.TCE.Presentation
{
    public struct TceVisualClock
    {
        public int Slot, Clip;
        public float Time, Speed;
        public bool Playing, Loop;
    }
    public struct TceVisualLight
    {
        public bool Enabled;
        public float Intensity, Range;
        public Color Color;
    }
    public struct TceVisualProperty
    {
        public int Renderer;
        public string Name;
        public byte Kind;
        public Vector4 Value;
    }

    /// <summary>Resolved visual controls; no network identity or gameplay state.</summary>
    public sealed class TceVisualFrame
    {
        public const int MaximumBytes = 32768;
        public TceVisualClock[] Animations = Array.Empty<TceVisualClock>();
        public TceVisualClock[] Directors = Array.Empty<TceVisualClock>();
        public TceVisualLight[] Lights = Array.Empty<TceVisualLight>();
        public TceVisualProperty[] Properties = Array.Empty<TceVisualProperty>();
        public bool[] Trails = Array.Empty<bool>();
        public byte[] Extension = Array.Empty<byte>();
        public Vector3 Velocity;
        public float AngularVelocity, Gravity;

        public byte[] Encode()
        {
            if(Animations==null || Directors==null || Lights==null || Properties==null || Trails==null || Extension==null
                || Animations.Length>64 || Directors.Length>16 || Lights.Length>256 || Properties.Length>1024 || Trails.Length>256 || Extension.Length>8192)
                throw new InvalidDataException("visual-state-budget");
            foreach(var p in Properties)if(string.IsNullOrEmpty(p.Name) || p.Name.Length>128 || p.Kind>2)throw new InvalidDataException("visual-property");
            using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
            writer.Write((byte)1);
            Clocks(writer, Animations); Clocks(writer, Directors);
            writer.Write(checked((ushort)Lights.Length));
            foreach (var x in Lights) { writer.Write(x.Enabled); Float(writer,x.Intensity);Float(writer,x.Range);Vector(writer,x.Color); }
            writer.Write(checked((ushort)Properties.Length));
            foreach(var x in Properties) { writer.Write(checked((ushort)x.Renderer));writer.Write(x.Name);writer.Write(x.Kind);Vector(writer,x.Value); }
            writer.Write(checked((ushort)Trails.Length));foreach(bool trail in Trails)writer.Write(trail);
            Vector(writer,Velocity);Float(writer,AngularVelocity);Float(writer,Gravity);
            writer.Write(checked((ushort)Extension.Length));writer.Write(Extension);
            if(stream.Length>MaximumBytes)throw new InvalidDataException("visual-state-budget");
            return stream.ToArray();
        }

        public static bool TryDecode(byte[] bytes, out TceVisualFrame frame)
        {
            frame=null;if(bytes==null || bytes.Length<1 || bytes.Length>MaximumBytes)return false;
            try
            {
                using var stream=new MemoryStream(bytes,false);using var reader=new BinaryReader(stream);
                if(reader.ReadByte()!=1)return false;
                var value=new TceVisualFrame { Animations=Clocks(reader,64),Directors=Clocks(reader,16) };
                int count=Count(reader,256);value.Lights=new TceVisualLight[count];
                for(int i=0;i<count;i++)value.Lights[i]=new TceVisualLight { Enabled=reader.ReadBoolean(),Intensity=Float(reader),Range=Float(reader),Color=Vector(reader) };
                count=Count(reader,1024);value.Properties=new TceVisualProperty[count];
                for(int i=0;i<count;i++){value.Properties[i]=new TceVisualProperty { Renderer=reader.ReadUInt16(),Name=reader.ReadString(),Kind=reader.ReadByte(),Value=Vector(reader) };if(value.Properties[i].Name.Length>128 || value.Properties[i].Kind>2)throw new InvalidDataException();}
                count=Count(reader,256);value.Trails=new bool[count];for(int i=0;i<count;i++)value.Trails[i]=reader.ReadBoolean();
                value.Velocity=Vector(reader);value.AngularVelocity=Float(reader);value.Gravity=Float(reader);
                count=Count(reader,8192);value.Extension=reader.ReadBytes(count);
                if(value.Extension.Length!=count || stream.Position!=stream.Length)return false;
                frame=value;return true;
            }
            catch(Exception e) when(e is IOException or OverflowException or ArgumentException){return false;}
        }

        public bool ControlsEqual(TceVisualFrame other)
        {
            if(other==null || !ClocksEqual(Animations,other.Animations) || !ClocksEqual(Directors,other.Directors)
                || Lights.Length!=other.Lights.Length || Properties.Length!=other.Properties.Length
                || Velocity!=other.Velocity || AngularVelocity!=other.AngularVelocity || Gravity!=other.Gravity
                || Extension.Length!=other.Extension.Length || Trails.Length!=other.Trails.Length)return false;
            for(int i=0;i<Lights.Length;i++)if(!Lights[i].Equals(other.Lights[i]))return false;
            for(int i=0;i<Properties.Length;i++)if(!Properties[i].Equals(other.Properties[i]))return false;
            for(int i=0;i<Extension.Length;i++)if(Extension[i]!=other.Extension[i])return false;
            for(int i=0;i<Trails.Length;i++)if(Trails[i]!=other.Trails[i])return false;
            return true;
        }
        static bool ClocksEqual(TceVisualClock[] a,TceVisualClock[] b)
        {
            if(a.Length!=b.Length)return false;
            for(int i=0;i<a.Length;i++)
                if(a[i].Slot!=b[i].Slot || a[i].Clip!=b[i].Clip || a[i].Speed!=b[i].Speed || a[i].Playing!=b[i].Playing || a[i].Loop!=b[i].Loop
                    || Math.Abs(a[i].Time-b[i].Time)>.25f && !a[i].Playing)return false;
            return true;
        }
        static void Clocks(BinaryWriter w,TceVisualClock[] clocks)
        {w.Write(checked((ushort)clocks.Length));foreach(var x in clocks){w.Write(checked((ushort)x.Slot));w.Write(x.Clip);Float(w,x.Time);Float(w,x.Speed);w.Write(x.Playing);w.Write(x.Loop);}}
        static TceVisualClock[] Clocks(BinaryReader r,int maximum)
        {int count=Count(r,maximum);var result=new TceVisualClock[count];for(int i=0;i<count;i++){result[i]=new TceVisualClock{Slot=r.ReadUInt16(),Clip=r.ReadInt32(),Time=Float(r),Speed=Float(r),Playing=r.ReadBoolean(),Loop=r.ReadBoolean()};if(result[i].Time<0 || result[i].Speed<0 || result[i].Speed>100)throw new InvalidDataException();}return result;}
        static int Count(BinaryReader r,int maximum){int count=r.ReadUInt16();if(count>maximum)throw new InvalidDataException();return count;}
        static void Float(BinaryWriter w,float f){if(!float.IsFinite(f) || Mathf.Abs(f)>1e8f)throw new InvalidDataException();w.Write(f);}
        static float Float(BinaryReader r){float f=r.ReadSingle();if(!float.IsFinite(f) || Mathf.Abs(f)>1e8f)throw new InvalidDataException();return f;}
        static void Vector(BinaryWriter w,Vector4 v){Float(w,v.x);Float(w,v.y);Float(w,v.z);Float(w,v.w);}
        static Vector4 Vector(BinaryReader r)=>new(Float(r),Float(r),Float(r),Float(r));
    }
}
