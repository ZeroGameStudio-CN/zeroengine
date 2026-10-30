using UnityEngine;
namespace ZeroEngine.Render
{
    /// <summary>Borrowed backend shaders. No engine pipeline, project path or loader is selected here.</summary>
    public readonly struct BodyVisualResources
    {
        public Shader Body { get; }
        public Shader Surface { get; }
        public Shader Specks { get; }
        public BodyVisualResources(Shader body, Shader surface, Shader specks)
        { Body = body; Surface = surface; Specks = specks; }
    }
    public interface IRenderPoseSource
    {
        bool TryCapture(RenderPoseSnapshot snapshot, Transform origin);
    }
}
