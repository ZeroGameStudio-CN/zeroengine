#if UNITY_EDITOR
using UnityEngine;
namespace ZeroEngine.Render.Tests.Runtime
{
    // Own synthetic inputs replayed against shipped sprite DXBC db16799079de343e74ca306823e7b297247af3f7120ccb1287797708a0b94f02.
    // The game binary and textures are not distributed with this package.
    internal static class SpriteRimOriginalGoldenSamples
    {
        internal readonly struct Sample
        {
            internal readonly int Mode; internal readonly Vector4 Light; internal readonly Vector3 Result;
            internal Sample(int mode, Vector4 light, Vector3 result) { Mode=mode; Light=light; Result=result; }
        }
        internal static readonly Sample[] Values =
        {
            new Sample(0,new Vector4(0.1f,0.3f,0.7f,0.8f),new Vector3(0.01740000024f,0.03480000049f,0.06960000098f)),
            new Sample(0,new Vector4(1.1f,0.4f,0.9f,1.3f),new Vector3(0.01740000024f,0.03480000049f,0.06960000098f)),
            new Sample(0,new Vector4(0.3f,1.2f,1.4f,0.6f),new Vector3(0.01740000024f,0.03480000049f,0.06960000098f)),
            new Sample(1,new Vector4(0.1f,0.3f,0.7f,0.8f),new Vector3(0.07240941375f,0.188301608f,0.4200859368f)),
            new Sample(1,new Vector4(1.1f,0.4f,0.9f,1.3f),new Vector3(1.019333243f,0.3720330894f,0.8228818774f)),
            new Sample(1,new Vector4(0.3f,1.2f,1.4f,0.6f),new Vector3(0.1459020078f,0.5331515074f,0.6292571425f)),
            new Sample(2,new Vector4(0.1f,0.3f,0.7f,0.8f),new Vector3(0.006263999734f,0.02366399951f,0.0918719992f)),
            new Sample(2,new Vector4(1.1f,0.4f,0.9f,1.3f),new Vector3(0.04454400018f,0.02575200237f,0.1419839859f)),
            new Sample(2,new Vector4(0.3f,1.2f,1.4f,0.6f),new Vector3(0.01322400011f,0.06403200328f,0.1447679996f)),
            new Sample(3,new Vector4(0.1f,0.3f,0.7f,0.8f),new Vector3(0.08258531988f,0.2041307688f,0.4336537719f)),
            new Sample(3,new Vector4(1.1f,0.4f,0.9f,1.3f),new Vector3(1.01749599f,0.3940808177f,0.8302311301f)),
            new Sample(3,new Vector4(0.3f,1.2f,1.4f,0.6f),new Vector3(0.151837945f,0.5297595263f,0.6156893373f)),
            new Sample(4,new Vector4(0.1f,0.3f,0.7f,0.8f),new Vector3(0.004871999845f,0.01531200018f,0.05289599672f)),
            new Sample(4,new Vector4(1.1f,0.4f,0.9f,1.3f),new Vector3(0.01966199838f,0.007656001486f,0.060552001f)),
            new Sample(4,new Vector4(0.3f,1.2f,1.4f,0.6f),new Vector3(0.01009199955f,0.03897599876f,0.08630399406f)),
            new Sample(5,new Vector4(0.1f,0.3f,0.7f,0.8f),new Vector3(0.08371596783f,0.2109146863f,0.4653121531f)),
            new Sample(5,new Vector4(1.1f,0.4f,0.9f,1.3f),new Vector3(1.037706375f,0.4087793529f,0.8963745236f)),
            new Sample(5,new Vector4(0.3f,1.2f,1.4f,0.6f),new Vector3(0.1543819159f,0.5501113534f,0.6631768346f)),
            new Sample(6,new Vector4(0.1f,0.3f,0.7f,0.8f),new Vector3(0.01894666813f,0.04673143104f,0.1995199919f)),
            new Sample(6,new Vector4(1.1f,0.4f,0.9f,1.3f),new Vector3(1.019333243f,0.06581391394f,0.7310159206f)),
            new Sample(6,new Vector4(0.3f,1.2f,1.4f,0.6f),new Vector3(0.02187428623f,0.1705200076f,0.1844400167f)),
            new Sample(7,new Vector4(0.1f,0.3f,0.7f,0.8f),new Vector3(0.08371596038f,0.2250478864f,0.5077117085f)),
            new Sample(7,new Vector4(1.1f,0.4f,0.9f,1.3f),new Vector3(0.7903755903f,0.2957138121f,0.6490436196f)),
            new Sample(7,new Vector4(0.3f,1.2f,1.4f,0.6f),new Vector3(0.2250478864f,0.8610414863f,1.002373457f)),
            new Sample(8,new Vector4(0.1f,0.3f,0.7f,0.8f),new Vector3(0.01740000024f,0.03480000049f,0.06960000098f)),
            new Sample(8,new Vector4(1.1f,0.4f,0.9f,1.3f),new Vector3(0.01740000024f,0.03480000049f,0.06960000098f)),
            new Sample(8,new Vector4(0.3f,1.2f,1.4f,0.6f),new Vector3(0.01740000024f,0.03480000049f,0.06960000098f)),
        };
    }
}
#endif
