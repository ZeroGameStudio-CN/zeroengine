using UnityEngine;
using UnityEngine.UI;

namespace ZeroEngine.PortraitSpeech
{
    [RequireComponent(typeof(Image)),DisallowMultipleComponent]
    public sealed class PortraitSpeechImage : MonoBehaviour
    {
        public PortraitSpeechProfile profile;
        public PortraitExpressionSet expressionSet;
        public string initialExpression;
        public string CurrentExpression{get;private set;}
        public event System.Action<string> ExpressionChanged;
        public float framesPerSecond=9,hold=.12f,openTime=.035f,closeTime=.065f,transition=.045f;
        public PortraitBlinkTiming blinkTiming=PortraitBlinkTiming.Default;
        public PortraitSpeechMotion Motion{get;}=new();
        private readonly PortraitBlinkMaterial blinkMaterial=new();
        private Image image;
        private Material original,owned;
        private Sprite boundSprite;
        private float boundAt;
        private void OnEnable()
        {
            image=GetComponent<Image>();original=image.material;
            boundSprite=image.overrideSprite;boundAt=Time.unscaledTime;Motion.Reset();
            if(expressionSet&&!string.IsNullOrEmpty(initialExpression))SetExpression(initialExpression);
            var shader=Resources.Load<Shader>(PortraitSpeechMaterial.ResourceName);
            if(!shader)return;
            owned=new Material(shader){hideFlags=HideFlags.HideAndDontSave};owned.SetFloat("_Layer",2);owned.SetFloat("_Connection",1);owned.SetFloat("_GlitchStrength",0);image.material=owned;
        }
        public bool SetExpression(string key)=>SetExpression(key,false);
        public bool SetExpression(string key,bool preservePlayback)
        {
            if(!expressionSet||!expressionSet.TryGet(key,out var expression,out var resolved))return false;
            if(!image)image=GetComponent<Image>();
            bool changed=image.overrideSprite!=expression.sprite||CurrentExpression!=key;
            image.overrideSprite=expression.sprite;profile=resolved;CurrentExpression=key;
            if(changed){boundSprite=expression.sprite;if(!preservePlayback){boundAt=Time.unscaledTime;Motion.Reset();}blinkMaterial.Dispose();ExpressionChanged?.Invoke(key);}
            return true;
        }
        public void RevealVisibleCharacter(char value,int index)
        {
            CheckSprite();
            if(isActiveAndEnabled)Motion.Reveal(value,index,Time.unscaledTime,framesPerSecond,hold);
        }
        public void Complete(bool immediate=false)=>Motion.Complete(immediate);
        private void LateUpdate()
        {
            if(!owned)return;
            CheckSprite();
            Motion.Advance(Time.unscaledTime,Time.unscaledDeltaTime,openTime,closeTime,transition);
            PortraitSpeechProfile? active=boundSprite&&profile.spriteName==boundSprite.name?profile:null;
            var blink=active?.blink??default;var size=PortraitBlinkMaterial.CanvasSize(boundSprite);
            float amount=blink.CanBlink?PortraitBlinkMotion.Evaluate(Time.unscaledTime-boundAt,blinkTiming):0;
            void Apply(Material target){PortraitSpeechMaterial.Apply(image,target,active,Motion);blinkMaterial.Apply(target,blink,amount,size);}
            Apply(owned);var rendered=image.materialForRendering;if(rendered&&rendered!=owned)Apply(rendered);
        }
        private void CheckSprite(){if(image&&boundSprite!=image.overrideSprite){boundSprite=image.overrideSprite;boundAt=Time.unscaledTime;Motion.Reset();}}
        private void OnDisable()
        {
            Motion.Reset();blinkMaterial.Dispose();if(image&&image.material==owned)image.material=original;
            if(owned){if(Application.isPlaying)Destroy(owned);else DestroyImmediate(owned);}owned=null;original=null;
        }
    }
}
