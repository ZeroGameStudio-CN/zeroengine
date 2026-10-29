namespace ZeroEngine.Tutorial.Tests.Runtime
{
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

internal sealed class TutorialCaptureHost : MonoBehaviour { }

internal sealed class TutorialRealtimeCapture : IDisposable
{
    [Serializable] internal sealed class Frame { public string file; public double seconds; }
    [Serializable] private sealed class Manifest
    {
        public bool completed = true;
        public string source = "Native ScreenCapture after WaitForEndOfFrame";
        public string timing = "actual realtime timestamps; no fixed-step or speed change";
        public bool audio = false;
        public int width, height;
        public List<Frame> frames;
    }
    private readonly string directory;
    private readonly List<Frame> frames = new();
    private bool running;
    private int width, height;
    public bool Stopped { get; private set; }
    public string Error { get; private set; }
    public TutorialRealtimeCapture(string path) { directory=path; Directory.CreateDirectory(path); }
    public void Start(MonoBehaviour host) { running=true; host.StartCoroutine(Record()); }
    public void Stop() => running=false;
    public void Dispose() => Stop();
    private IEnumerator Record()
    {
        double start=Time.realtimeSinceStartupAsDouble;
        double next=start;
        while(running && Time.realtimeSinceStartupAsDouble<start+60)
        {
            yield return new WaitForEndOfFrame();
            if(Time.realtimeSinceStartupAsDouble<next) continue;
            double moment=Time.realtimeSinceStartupAsDouble;
            Texture2D texture=null;
            try
            {
                texture=ScreenCapture.CaptureScreenshotAsTexture();
                if(frames.Count==0) { width=texture.width; height=texture.height; }
                if(texture.width!=width || texture.height!=height) throw new InvalidOperationException("Capture size changed.");
                string file="frame-"+frames.Count.ToString("D5")+".jpg";
                File.WriteAllBytes(Path.Combine(directory,file),texture.EncodeToJPG(85));
                frames.Add(new Frame { file=file, seconds=moment-start });
                next=moment+1.0/15;
            }
            catch(Exception exception) { Error=exception.ToString(); running=false; }
            finally { if(texture) UnityEngine.Object.Destroy(texture); }
        }
        Stopped=true;
    }
    public void Complete()
    {
        if(!Stopped || Error!=null || frames.Count<3) throw new InvalidOperationException(Error??"Incomplete capture.");
        File.WriteAllText(Path.Combine(directory,"realtime-capture.json"),JsonUtility.ToJson(new Manifest { width=width,height=height,frames=frames },true));
    }
}

}
