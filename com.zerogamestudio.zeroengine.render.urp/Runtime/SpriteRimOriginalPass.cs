using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace ZeroEngine.Render.Urp
{
    internal sealed class SpriteRimOriginalPass : IDisposable
    {
        private Material material;
        private Mesh full, half;
        private static readonly int FieldId = Shader.PropertyToID("_RimOriginalField");

        private sealed class FieldData
        {
            internal Material Material;
            internal Mesh Full, Half;
            internal List<SpriteRimOriginalGroup.Source> Lights;
            internal Rect Viewport;
        }

        private sealed class ActorData
        {
            internal TextureHandle Source, Destination, Field;
            internal List<SpriteRimOriginalGroup.Draw> Draws;
            internal Rect Viewport, ActorViewport;
        }

        internal void Record(RenderGraph graph, UniversalResourceData resources, UniversalCameraData camera,
            List<SpriteRimOriginalGroup> groups)
        {
            if (!material)
            {
                var shader = Resources.Load<Shader>("ZeroEngine/SpriteRimOriginal");
                if (!shader || !shader.isSupported) return;
                material = CoreUtils.CreateEngineMaterial(shader);
                full = MakeMesh(false); half = MakeMesh(true);
            }
            foreach (var group in groups)
            {
                var source = resources.activeColorTexture;
                var desc = graph.GetTextureDesc(source);
                var fieldDesc = new TextureDesc(480, 270)
                {
                    name = "Original rim: additive 2D light field", colorFormat = GraphicsFormat.R8G8B8A8_UNorm,
                    // The shipped light camera clears to black with alpha ONE, not transparent black.
                    clearBuffer = true, clearColor = Color.black, filterMode = FilterMode.Bilinear
                };
                var field = graph.CreateTexture(fieldDesc);
                var rect = camera.camera.rect;
                using (var builder = graph.AddRasterRenderPass<FieldData>("Original rim: light field", out var data))
                {
                    data.Material = material; data.Full = full; data.Half = half;
                    data.Lights = new List<SpriteRimOriginalGroup.Source>(group.Lights);
                    data.Viewport = new Rect(rect.x*480, rect.y*270, rect.width*480, rect.height*270);
                    builder.SetRenderAttachment(field, 0, AccessFlags.Write);
                    builder.SetRenderFunc(static (FieldData data, RasterGraphContext context) =>
                    {
                        context.cmd.SetViewport(data.Viewport);
                        foreach (var light in data.Lights)
                        {
                            var properties = context.renderGraphPool.GetTempMaterialPropertyBlock();
                            properties.SetVector("_RimOriginalSourceColor", light.Color);
                            properties.SetVector("_RimOriginalSourcePowers", light.Powers);
                            properties.SetFloat("_RimOriginalSourceShape", light.Shape == LightType.Point ? 0 : light.Shape == LightType.Directional ? 1 : 2);
                            properties.SetFloat("_RimOriginalSourceAngle", light.Angle);
                            context.cmd.DrawMesh(light.Shape == LightType.Spot ? data.Half : data.Full, light.Matrix, data.Material, 0, 0, properties);
                        }
                    });
                }
                desc.name = "Original rim: source-colour composite"; desc.clearBuffer = false;
                var destination = graph.CreateTexture(desc);
                var size = desc.CalculateFinalDimensions();
                using (var builder = graph.AddRasterRenderPass<ActorData>("Original rim: strongest-light silhouette", out var data))
                {
                    data.Source = source; data.Destination = destination; data.Field = field;
                    data.Draws = new List<SpriteRimOriginalGroup.Draw>(group.Draws);
                    data.Viewport = new Rect(0,0,size.x,size.y);
                    data.ActorViewport = new Rect(rect.x*size.x, rect.y*size.y, rect.width*size.x, rect.height*size.y);
                    builder.UseTexture(source, AccessFlags.Read); builder.UseTexture(field, AccessFlags.Read);
                    builder.UseTexture(resources.cameraDepthTexture, AccessFlags.Read);
                    foreach (var draw in group.Draws) builder.UseTexture(graph.ImportTexture(draw.Silhouette.ReferenceHandle), AccessFlags.Read);
                    builder.SetRenderAttachment(destination, 0, AccessFlags.Write);
                    builder.AllowGlobalStateModification(true);
                    builder.SetRenderFunc(static (ActorData data, RasterGraphContext context) =>
                    {
                        RTHandle handle = data.Source;
                        Vector2 scale = handle.useScaling ? new Vector2(handle.rtHandleProperties.rtHandleScale.x, handle.rtHandleProperties.rtHandleScale.y) : Vector2.one;
                        bool flip = context.GetTextureUVOrigin(in data.Source) != context.GetTextureUVOrigin(in data.Destination);
                        var bias = flip ? new Vector4(scale.x,-scale.y,0,scale.y) : new Vector4(scale.x,scale.y,0,0);
                        context.cmd.SetViewport(data.Viewport);
                        Blitter.BlitTexture(context.cmd, handle, bias, 0, false);
                        context.cmd.SetGlobalTexture(FieldId, data.Field);
                        context.cmd.SetViewport(data.ActorViewport);
                        foreach (var draw in data.Draws)
                            if (draw.Renderer && draw.Renderer.enabled)
                                for (int i = 0; i < draw.Materials.Count; i++) context.cmd.DrawRenderer(draw.Renderer,draw.Materials[i],i,1);
                    });
                }
                resources.cameraColor = destination;
            }
        }

        private static Mesh MakeMesh(bool spot)
        {
            var mesh = new Mesh { name = "Original rim 2D field", hideFlags = HideFlags.HideAndDontSave };
            float top = spot ? 0 : 1;
            mesh.vertices = new[] { new Vector3(-1,top,0), new Vector3(1,top,0), new Vector3(1,-1,0), new Vector3(-1,-1,0) };
            mesh.uv = new[] { new Vector2(-1,top), new Vector2(1,top), new Vector2(1,spot ? 1 : -1), new Vector2(-1,spot ? 1 : -1) };
            mesh.triangles = new[] { 0,1,2,2,3,0 }; mesh.RecalculateBounds();
            return mesh;
        }

        public void Dispose()
        { CoreUtils.Destroy(material); CoreUtils.Destroy(full); CoreUtils.Destroy(half); }
    }
}
