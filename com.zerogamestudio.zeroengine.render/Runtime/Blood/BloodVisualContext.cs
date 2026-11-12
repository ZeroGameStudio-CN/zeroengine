using System;
using UnityEngine;
namespace ZeroEngine.Render
{
    /// <summary>Caller-owned visual session. No actor lookup, damage, save or room manager.</summary>
    public sealed class BloodVisualContext : IDisposable
    {
        public BloodVisualResources Resources {get;}
        public BloodSurfaceUploadQueue Uploads {get;} = new();
        public Func<float> DeltaTime {get;set;} = () => Time.deltaTime;
        public Func<Camera> ViewCamera {get;set;} = () => Camera.main;
        public Func<Color,Color> RemapBlood {get;set;} = value => value;
        public Action<GameObject> PrepareAuthoredSpray {get;set;}
        public Transform ResidueParent {get;set;}
        public int TerrainMask {get;set;} = ~0;
        public int OneWayLayer {get;set;} = -1;
        public bool WorldEnabled {get;set;}
        public bool DebrisEnabled {get;set;} = true;
        public bool RetainResidue {get;set;} = true;
        public int ScopeVersion {get;private set;}
        public int ActiveBursts {get;set;}
        public int CollisionCount {get;set;}
        public int SettledCount {get;set;}
        internal BloodResidueStore Residue;
        public event Action PaletteChanged;
        public BloodVisualContext(BloodVisualResources resources) => Resources=resources??throw new ArgumentNullException(nameof(resources));
        public void RefreshPalette() => PaletteChanged?.Invoke();
        public void ClearScope(){ScopeVersion++;BloodResidueStore.Clear(this);Uploads.Clear();}
        public void Dispose(){WorldEnabled=false;ClearScope();PaletteChanged=null;Resources.Dispose();}
    }
}
