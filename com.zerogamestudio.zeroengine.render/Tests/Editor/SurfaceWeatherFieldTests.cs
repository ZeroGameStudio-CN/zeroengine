using System;
using NUnit.Framework;
using UnityEngine;
using ZeroEngine.Render.Weather;

namespace ZeroEngine.Render.Tests.Editor
{
    public sealed class SurfaceWeatherFieldTests
    {
        [Test]
        public void AdjacentRegions_BlendAcrossWorldPosition_WithoutCreatingADryGap()
        {
            var field = new SurfaceWeatherField();
            field.SetRegions(new[] {
                new SurfaceWeatherRegion("rain", new Bounds(new Vector3(-5,0,0),new Vector3(10,1,10)),new SurfaceWeatherValue(1,0),2),
                new SurfaceWeatherRegion("snow", new Bounds(new Vector3(5,0,0),new Vector3(10,1,10)),new SurfaceWeatherValue(0,1),2) });
            Assert.That(field.Sample(new Vector3(-2,20,0)).Wetness, Is.EqualTo(1));
            Assert.That(field.Sample(Vector3.zero).Wetness, Is.EqualTo(.5f).Within(.00001f));
            Assert.That(field.Sample(Vector3.zero).Snow, Is.EqualTo(.5f).Within(.00001f));
            Assert.That(field.Sample(new Vector3(2,-20,0)).Snow, Is.EqualTo(1));
        }

        [Test]
        public void HigherPriority_OverridesOverlappingLowerPriority_IndependentOfInputOrder()
        {
            var field = new SurfaceWeatherField { DefaultValue = new SurfaceWeatherValue(.1f,.2f) };
            var low = new SurfaceWeatherRegion("low",new Bounds(Vector3.zero,Vector3.one*10),new SurfaceWeatherValue(1,1),1);
            var high = new SurfaceWeatherRegion("high",new Bounds(Vector3.zero,Vector3.one*4),default,1,20);
            field.SetRegions(new[]{high,low}); Assert.That(field.Sample(Vector3.zero).Snow, Is.Zero);
            field.SetRegions(new[]{low,high}); Assert.That(field.Sample(Vector3.zero).Snow, Is.Zero);
            Assert.That(field.Sample(Vector3.one*100).Snow, Is.EqualTo(.2f));
        }

        [Test]
        public void InvalidRegionReplacement_KeepsThePreviousField()
        {
            var field = new SurfaceWeatherField();
            var region = new SurfaceWeatherRegion("original",new Bounds(Vector3.zero,Vector3.one),new SurfaceWeatherValue(.4f,.7f));
            field.SetRegions(new[]{region});
            Assert.Throws<ArgumentException>(()=>field.SetRegions(new[]{region,region}));
            Assert.That(field.Sample(Vector3.zero).Snow, Is.EqualTo(.7f));
            Assert.Throws<ArgumentOutOfRangeException>(()=>field.SetRegions(new SurfaceWeatherRegion[SurfaceWeatherField.Capacity+1]));
            Assert.That(field.Sample(Vector3.zero).Wetness, Is.EqualTo(.4f));
        }

        [TestCase(float.NaN,0f)]
        [TestCase(0f,float.PositiveInfinity)]
        [TestCase(-.01f,0f)]
        [TestCase(0f,1.01f)]
        public void InvalidState_IsRejected(float wet,float snow) => Assert.Throws<ArgumentOutOfRangeException>(()=>new SurfaceWeatherValue(wet,snow));

        [Test]
        public void ExposureFrame_LeavesRoofExposedAndFloorCovered_InTheSameMergedFootprint()
        {
            var projection = new SurfaceWeatherProjection(new Bounds(Vector3.zero,Vector3.one*10),Vector3.up,32,.04f);
            var depths = new float[32*32];Array.Fill(depths,3f);
            var frame = new SurfaceWeatherExposureFrame(projection,depths);
            Assert.That(frame.Sample(new Vector3(0,3.01f,0)), Is.EqualTo(1));
            Assert.That(frame.Sample(Vector3.zero), Is.Zero);
            Assert.That(frame.Sample(new Vector3(20,0,0)), Is.EqualTo(1));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void ExposureProjection_InvalidDirectionOrBounds_IsRejectedBeforeGpuWork(bool direction)
        {
            var bounds=new Bounds(Vector3.zero,Vector3.one*10);
            Vector3 axis=Vector3.up;
            if(direction)axis=new Vector3(float.NaN,1,0);else bounds.center=new Vector3(float.PositiveInfinity,0,0);
            Assert.Throws<ArgumentException>(()=>new SurfaceWeatherProjection(bounds,axis,32,.04f));
        }
        [Test]
        public void MaterialSourceResolution_TracksBorrowingAndNullableTerrainDefaults()
        {
            var original = new Material(Shader.Find("Hidden/InternalErrorShader"));
            var borrowed = new Material(original);
            try
            {
                SurfaceWeatherMaterialSources.Register(borrowed,original);
                Assert.That(SurfaceWeatherMaterialSources.Resolve(borrowed), Is.SameAs(original));
                SurfaceWeatherMaterialSources.Unregister(borrowed);
                SurfaceWeatherMaterialSources.Register(borrowed,null);
                Assert.That(SurfaceWeatherMaterialSources.Resolve(borrowed), Is.Null);
            }
            finally { SurfaceWeatherMaterialSources.Unregister(borrowed); UnityEngine.Object.DestroyImmediate(borrowed); UnityEngine.Object.DestroyImmediate(original); }
        }
    }
}
