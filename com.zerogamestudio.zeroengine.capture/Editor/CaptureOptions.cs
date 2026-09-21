using System;
using System.IO;
using System.Text.RegularExpressions;

namespace ZeroEngine.Capture
{
    public sealed class CaptureOptions
    {
        public string OutputRoot { get; set; }
        public string Label { get; set; } = "recording";
        public int Width { get; set; } = 1280;
        public int Height { get; set; } = 720;
        public int FramesPerSecond { get; set; } = 30;
        public int SimulationFramesPerSecond { get; set; } = 60;
        public int MaximumFrames { get; set; } = 3600;

        internal CaptureOptions ValidatedCopy()
        {
            if (string.IsNullOrWhiteSpace(OutputRoot) || !Path.IsPathRooted(OutputRoot))
                throw new ArgumentException("OutputRoot must be an absolute external directory.");
            if (Label == null || !Regex.IsMatch(Label, "^[a-z0-9][a-z0-9-]{0,47}$"))
                throw new ArgumentException("Label must be a short lowercase ASCII identifier.");
            if (Width < 16 || Height < 16 || Width > 3840 || Height > 2160)
                throw new ArgumentOutOfRangeException(nameof(Width), "Capture size must be between 16x16 and 3840x2160.");
            if (FramesPerSecond < 10 || FramesPerSecond > 60 || SimulationFramesPerSecond < FramesPerSecond ||
                SimulationFramesPerSecond > 240 || SimulationFramesPerSecond % FramesPerSecond != 0)
                throw new ArgumentException("Simulation FPS must be an integral multiple of capture FPS (maximum 240).");
            if (MaximumFrames < 1 || MaximumFrames > FramesPerSecond * 120)
                throw new ArgumentOutOfRangeException(nameof(MaximumFrames), "Capture must be bounded to at most 120 seconds.");
            return new CaptureOptions
            {
                OutputRoot = Path.GetFullPath(OutputRoot), Label = Label, Width = Width, Height = Height,
                FramesPerSecond = FramesPerSecond, SimulationFramesPerSecond = SimulationFramesPerSecond,
                MaximumFrames = MaximumFrames
            };
        }
    }
}
