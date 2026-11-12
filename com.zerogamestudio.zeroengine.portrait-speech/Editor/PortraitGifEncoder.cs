using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace ZeroEngine.PortraitSpeech.Editor
{
    internal static class PortraitGifEncoder
    {
        private struct Bin
        {
            public int key,count;
            public long r,g,b;
            public int Channel(int axis) => (key>>(axis==0?10:axis==1?5:0))&31;
        }

        private readonly struct Box
        {
            public readonly int start,length,axis,count;
            public readonly double score;
            public Box(Bin[] bins,int start,int length)
            {
                this.start=start;this.length=length;
                int r0=31,g0=31,b0=31,r1=0,g1=0,b1=0,total=0;
                for(int i=start;i<start+length;i++)
                {
                    var bin=bins[i];int r=bin.Channel(0),g=bin.Channel(1),b=bin.Channel(2);
                    r0=Math.Min(r0,r);g0=Math.Min(g0,g);b0=Math.Min(b0,b);
                    r1=Math.Max(r1,r);g1=Math.Max(g1,g);b1=Math.Max(b1,b);total+=bin.count;
                }
                int dr=r1-r0,dg=g1-g0,db=b1-b0;
                axis=dr>=dg&&dr>=db?0:dg>=db?1:2;
                count=total;score=length<2?0:Math.Max(dr,Math.Max(dg,db))*Math.Sqrt(total);
            }
        }

        private static int Key(Color32 color)=>(color.r>>3)<<10|(color.g>>3)<<5|color.b>>3;

        // One adaptive palette for the whole loop keeps the pixels outside the animation stable.
        private static Color32[] Palette(IReadOnlyList<Color32[]> frames,out byte[] lookup)
        {
            var histogram=new Bin[32768];
            Color32[] prior=null;
            foreach(var frame in frames)
            {
                if(ReferenceEquals(frame,prior))continue;
                prior=frame;
                foreach(var color in frame)
                {
                    int key=Key(color);
                    ref var bin=ref histogram[key];
                    bin.key=key;bin.count++;bin.r+=color.r;bin.g+=color.g;bin.b+=color.b;
                }
            }
            var colors=new List<Bin>();
            foreach(var bin in histogram)if(bin.count>0)colors.Add(bin);
            var bins=colors.ToArray();
            var boxes=new List<Box>{new Box(bins,0,bins.Length)};
            while(boxes.Count<256)
            {
                int largest=-1;double score=0;
                for(int i=0;i<boxes.Count;i++)if(boxes[i].score>score){largest=i;score=boxes[i].score;}
                if(largest<0)break;
                var box=boxes[largest];
                Array.Sort(bins,box.start,box.length,Comparer<Bin>.Create((a,b)=>
                {
                    int comparison=a.Channel(box.axis).CompareTo(b.Channel(box.axis));
                    return comparison==0?a.key.CompareTo(b.key):comparison;
                }));
                int split=1,sum=bins[box.start].count;
                while(split<box.length-1&&sum<box.count/2)sum+=bins[box.start+split++].count;
                boxes[largest]=new Box(bins,box.start,split);
                boxes.Add(new Box(bins,box.start+split,box.length-split));
            }
            var palette=new Color32[256];lookup=new byte[32768];
            for(int i=0;i<boxes.Count;i++)
            {
                var box=boxes[i];long r=0,g=0,b=0;
                for(int j=box.start;j<box.start+box.length;j++)
                {
                    var bin=bins[j];lookup[bin.key]=(byte)i;r+=bin.r;g+=bin.g;b+=bin.b;
                }
                palette[i]=new Color32((byte)(r/box.count),(byte)(g/box.count),(byte)(b/box.count),255);
            }
            return palette;
        }

        public static int Write(Stream output,int width,int height,IReadOnlyList<Color32[]> frames,int delay)
        {
            var palette=Palette(frames,out var lookup);
            using var writer=new BinaryWriter(output,Encoding.ASCII,true);
            writer.Write(Encoding.ASCII.GetBytes("GIF89a"));
            writer.Write((ushort)width);writer.Write((ushort)height);writer.Write(new byte[]{0xf7,0,0});
            foreach(var color in palette){writer.Write(color.r);writer.Write(color.g);writer.Write(color.b);}
            writer.Write(new byte[]{0x21,0xff,11});writer.Write(Encoding.ASCII.GetBytes("NETSCAPE2.0"));
            writer.Write(new byte[]{3,1,0,0,0});
            var pixels=new byte[width*height];var previous=new byte[pixels.Length];
            Color32[] last=null;
            int held=0,written=0;
            foreach(var frame in frames)
            {
                if(ReferenceEquals(frame,last)){held+=delay;continue;}
                last=frame;int index=0;
                for(int y=height-1;y>=0;y--)
                for(int x=0;x<width;x++)pixels[index++]=lookup[Key(frame[y*width+x])];
                if(held>0&&pixels.AsSpan().SequenceEqual(previous)){held+=delay;continue;}
                if(held>0)WriteFrame();
                (pixels,previous)=(previous,pixels);held=delay;
            }
            if(held>0)WriteFrame();
            writer.Write((byte)0x3b);
            return written;

            void WriteFrame()
            {
                writer.Write(new byte[]{0x21,0xf9,4,4});writer.Write((ushort)held);writer.Write(new byte[]{0,0});
                writer.Write((byte)0x2c);writer.Write((ushort)0);writer.Write((ushort)0);
                writer.Write((ushort)width);writer.Write((ushort)height);writer.Write((byte)0);
                WritePixels(writer,previous);
                written++;
            }
        }

        private static void WritePixels(BinaryWriter writer,byte[] pixels)
        {
            // A short LZW dictionary is reset before it needs ten-bit codes.
            writer.Write((byte)8);
            const int Clear=256,End=257;
            int next=258,prefix=pixels[0],bits=0,bitCount=0,used=0;
            var dictionary=new Dictionary<int,int>(253);var block=new byte[255];
            void FlushBlock(){if(used>0){writer.Write((byte)used);writer.Write(block,0,used);used=0;}}
            void PutByte(byte value){block[used++]=value;if(used==255)FlushBlock();}
            void Emit(int code)
            {
                bits|=code<<bitCount;bitCount+=9;
                while(bitCount>=8){PutByte((byte)bits);bits>>=8;bitCount-=8;}
            }
            Emit(Clear);
            for(int i=1;i<pixels.Length;i++)
            {
                int value=pixels[i],key=prefix<<8|value;
                if(dictionary.TryGetValue(key,out int combined)){prefix=combined;continue;}
                Emit(prefix);
                if(next<511)dictionary.Add(key,next++);
                else{Emit(Clear);dictionary.Clear();next=258;}
                prefix=value;
            }
            Emit(prefix);Emit(End);
            if(bitCount>0)PutByte((byte)bits);
            FlushBlock();writer.Write((byte)0);
        }
    }
}
