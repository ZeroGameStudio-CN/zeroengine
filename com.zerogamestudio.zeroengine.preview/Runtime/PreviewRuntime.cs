using System;
using System.Collections.Generic;
using UnityEngine;

namespace ZeroEngine.Preview
{
    public sealed class PreviewFeature
    {
        private readonly bool productionEnabled;
        private bool? sessionOverride;
        public string Id { get; }
        internal bool ProductionEnabled=>productionEnabled;
        public bool IsEnabled=>PreviewRuntime.IsDevelopment&&sessionOverride.HasValue?sessionOverride.Value:productionEnabled;
        public event Action<bool> Changed;
        internal PreviewFeature(string id,bool productionEnabled){Id=id;this.productionEnabled=productionEnabled;}
        public void SetEnabled(bool enabled)
        {
            if(!PreviewRuntime.IsDevelopment)throw new InvalidOperationException("Production cannot set a development preview override.");
            bool before=IsEnabled;sessionOverride=enabled;
            if(before!=IsEnabled)Notify();
        }
        private void Notify()
        {
            if(Changed==null)return;
            foreach(Action<bool> listener in Changed.GetInvocationList())try{listener(IsEnabled);}catch(Exception error){Debug.LogException(error);}
        }
        internal void Reset(){sessionOverride=null;Changed=null;}
    }

    public static class PreviewRuntime
    {
        private static readonly Dictionary<string,PreviewFeature> features=new(StringComparer.Ordinal);
#if UNITY_EDITOR
        public static bool IsDevelopment=>true;
#else
        public static bool IsDevelopment=>false;
#endif
        public static PreviewFeature Feature(string id,bool productionEnabled=false)
        {
            PreviewDefinition.ValidateId(id);
            if(!features.TryGetValue(id,out var value)){value=new PreviewFeature(id,productionEnabled);features.Add(id,value);}
            else if(value.ProductionEnabled!=productionEnabled)throw new InvalidOperationException("Conflicting production default for preview ID: "+id);
            return value;
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSession(){foreach(var feature in features.Values)feature.Reset();}
    }
}
