using System;
using System.IO;
using UnityEngine;

namespace ZeroEngine.Capture
{
    public sealed class CaptureSession : IDisposable
    {
        private readonly CaptureOptions _options;
        private readonly Func<Camera> _camera;
        private readonly Func<Canvas[]> _overlays;
        private readonly CameraFrameWriter _writer;
        private readonly float _previousCaptureDelta;
        private readonly bool _previousBackground;
        private int _lastTick;
        private int _simulationFrames;

        public string DirectoryPath { get; }
        public int FrameCount { get; private set; }
        public string State { get; private set; } = "recording";
        public string Error { get; private set; } = "";
        public bool IsRecording => State == "recording";

        internal CaptureSession(CaptureOptions options, Func<Camera> camera, Func<Canvas[]> overlays)
        {
            _options = options;
            _camera = camera;
            _overlays = overlays;
            DirectoryPath = Path.Combine(options.OutputRoot,
                DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ") + "-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DirectoryPath);
            _previousCaptureDelta = Time.captureDeltaTime;
            _previousBackground = Application.runInBackground;
            _lastTick = Time.frameCount;
            _writer = new CameraFrameWriter(options.Width, options.Height);
            Time.captureDeltaTime = 1f / options.SimulationFramesPerSecond;
            Application.runInBackground = true;
        }

        internal void Tick()
        {
            if (!IsRecording) return;
            try
            {
                var frame = Time.frameCount;
                if (frame == _lastTick) return;
                if (frame != _lastTick + 1 || !Mathf.Approximately(Time.captureDeltaTime,
                        1f / _options.SimulationFramesPerSecond))
                    throw new InvalidOperationException("Capture cadence changed; partial output is not continuous evidence.");
                _lastTick = frame;
                _simulationFrames++;
                if (_simulationFrames % (_options.SimulationFramesPerSecond / _options.FramesPerSecond) != 0) return;
                _writer.Write(_camera(), _overlays?.Invoke(), Path.Combine(DirectoryPath,
                    _options.Label + "-" + FrameCount.ToString("D4") + ".png"));
                FrameCount++;
                if (FrameCount >= _options.MaximumFrames) Cancel("frame-limit-reached");
            }
            catch (Exception exception)
            {
                Finish("failed", exception.ToString());
                Debug.LogException(exception);
            }
        }

        /// <summary>Call after the project has verified its scenario, before leaving PlayMode.</summary>
        public void Complete()
        {
            if (!IsRecording) throw new InvalidOperationException("Session already ended: " + State + "; " + Error);
            if (FrameCount == 0) throw new InvalidOperationException("An empty recording cannot complete.");
            Finish("completed", "");
        }

        public void Cancel(string reason = "cancelled")
        {
            if (IsRecording) Finish("cancelled", reason);
        }

        public void Dispose() => Cancel("disposed-before-completion");

        private void Finish(string state, string error)
        {
            State = state;
            Error = error;
            try { BackgroundCapture.Detach(this); }
            finally
            {
                Time.captureDeltaTime = _previousCaptureDelta;
                Application.runInBackground = _previousBackground;
                _writer.Dispose();
            }
            var manifest = new Manifest
            {
                completed = state == "completed", state = state, error = error,
                width = _options.Width, height = _options.Height,
                simulationFps = _options.SimulationFramesPerSecond,
                clips = new[] { new Clip { prefix = _options.Label, frames = FrameCount, fps = _options.FramesPerSecond } }
            };
            try
            {
                var name = manifest.completed ? "capture.json" : "session.json";
                var pending = Path.Combine(DirectoryPath, name + ".pending");
                File.WriteAllText(pending, JsonUtility.ToJson(manifest, true));
                File.Move(pending, Path.Combine(DirectoryPath, name));
            }
            catch (Exception exception)
            {
                State = "failed";
                Error = "Manifest write failed: " + exception.Message;
                throw;
            }
        }

        [Serializable] private sealed class Manifest
        {
            public int version = 1;
            public bool completed;
            public string state;
            public string error;
            public int width;
            public int height;
            public int simulationFps;
            public string timing = "fixed-simulation-step-not-performance";
            public bool audio = false;
            public Clip[] clips;
        }
        [Serializable] private sealed class Clip
        {
            public string prefix;
            public int frames;
            public int fps;
        }
    }
}
