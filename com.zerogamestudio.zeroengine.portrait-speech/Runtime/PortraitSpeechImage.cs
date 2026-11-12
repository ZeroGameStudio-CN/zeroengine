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
        private readonly PortraitSpeechMotion localMotion=new();
        private PortraitSpeechMotion hostMotion;
        private bool manual,borrowedMaterial;
        public PortraitSpeechMotion Motion=>hostMotion??localMotion;
        public bool IsTransitioning=>pending!=null;
        private readonly PortraitBlinkMaterial blinkMaterial=new();
        private Image image;
        private Material original,owned;
        private Sprite boundSprite;
        private float boundAt;
        private ExpressionTransition pending;
        private const string TransitionMouthKeyword="PORTRAIT_EXPRESSION_MOUTH";
        private const float BridgeSeconds=.02f;
        private static readonly int BridgeTex=Shader.PropertyToID("_ExpressionBridgeTex");
        private static readonly int BridgeRect=Shader.PropertyToID("_ExpressionBridgeRect");

        private sealed class ExpressionTransition
        {
            internal PortraitExpressionSet set;
            internal string key;
            internal PortraitExpression target;
            internal PortraitSpeechProfile targetProfile;
            internal Sprite from;
            internal Rect face,mouth,leftEye,rightEye;
            internal Vector4 closedArcA,closedArcB;
            internal float closeStart,closeEnd,cutAt,initialBlink,openSeconds,mouthSeconds,swappedAt;
            internal PortraitExpressionBridge bridge;
            internal bool closeSource,openTarget,swapped,bridgeShown;
        }

        private void OnEnable()
        {
            image=GetComponent<Image>();original=image.material;
            boundSprite=image.overrideSprite;boundAt=Time.unscaledTime;localMotion.Reset();
            if(expressionSet&&!string.IsNullOrEmpty(initialExpression))SetExpression(initialExpression);
            var shader=Resources.Load<Shader>(PortraitSpeechMaterial.ResourceName);
            if(!shader)return;
            owned=new Material(shader){hideFlags=HideFlags.HideAndDontSave};owned.SetFloat("_Layer",2);owned.SetFloat("_Connection",1);owned.SetFloat("_GlitchStrength",0);image.material=owned;
        }
        public bool SetExpression(string key)=>SetExpression(key,false);
        public bool SetExpression(string key,bool preservePlayback)
        {
            if(!expressionSet||!expressionSet.TryGet(key,out var expression,out var resolved))return false;
            pending=null;
            ApplyExpression(key,expression,resolved,Time.unscaledTime,preservePlayback);
            return true;
        }

        /// <summary>
        /// Opt in to a soft hand-off within a calibrated outfit group. Incompatible
        /// portraits take the ordinary immediate switch; unknown keys leave state intact.
        /// </summary>
        public bool TransitionToExpression(string key)=>BeginExpressionTransition(key,Time.unscaledTime);

        private bool BeginExpressionTransition(string key,float now)
        {
            if(!expressionSet||!expressionSet.TryGet(key,out var target,out var targetProfile))return false;
            if(!image)image=GetComponent<Image>();
            if(pending!=null&&pending.key==key)return true;
            if(pending==null&&CurrentExpression==key&&image.overrideSprite==target.sprite)return true;
            if(!owned||pending!=null||!expressionSet.TryGet(CurrentExpression,out var source,out var sourceProfile)
                ||image.overrideSprite!=source.sprite||!CanTransition(source,target,sourceProfile,targetProfile))
            {
                pending=null;
                ApplyExpression(key,target,targetProfile,now,false);
                return true;
            }

            bool closeSource=source.transitionEyes==PortraitExpressionEyes.Open;
            bool openTarget=target.transitionEyes==PortraitExpressionEyes.Open;
            var settings=expressionSet.transition;
            var defaults=PortraitExpressionTransitionSettings.Default;
            float closeSeconds=Duration(settings.closeEyes,defaults.closeEyes,.04f,.25f);
            float settle=Mathf.Clamp(Motion.Openness*Mathf.Max(.01f,closeTime),0,.15f);
            float closeStart=now+(closeSource?Mathf.Min(.025f,settle):settle);
            float initial=profile.blink.CanBlink?PortraitBlinkMotion.Evaluate(now-boundAt,blinkTiming):0;
            float closeEnd=Mathf.Max(now+settle,closeStart+(closeSource?closeSeconds:0));
            var canvas=source.sprite.rect.size;
            var mouth=JoinRegion(sourceProfile.mouth,targetProfile.mouth);
            // Keep either pose's painted eye pixels away from the face overlay.
            // The visible eyelid is supplied by the calibrated blink instead.
            float eyePadding=openTarget?1:2;
            var leftEye=PadRegion(JoinRegion(sourceProfile.blink.leftEye.area,targetProfile.blink.leftEye.area),canvas,eyePadding);
            var rightEye=PadRegion(JoinRegion(sourceProfile.blink.rightEye.area,targetProfile.blink.rightEye.area),canvas,eyePadding);
            var face=PadRegion(JoinRegion(JoinRegion(leftEye,rightEye),mouth),canvas,6);
            var bridge=expressionSet.FindBridge(CurrentExpression,key,Vector2Int.RoundToInt(canvas));
            float faceSeconds=face.width>0?Duration(settings.faceBlend,defaults.faceBlend,.025f,.14f):0;
            if(bridge.patch)faceSeconds=Mathf.Max(faceSeconds,BridgeSeconds);
            pending=new ExpressionTransition
            {
                set=expressionSet,key=key,target=target,targetProfile=targetProfile,from=source.sprite,
                face=face,mouth=mouth,leftEye=leftEye,rightEye=rightEye,
                closedArcA=ProjectClosedContour(sourceProfile.blink.leftEye,targetProfile.blink.leftEye),
                closedArcB=ProjectClosedContour(sourceProfile.blink.rightEye,targetProfile.blink.rightEye),
                closeSource=closeSource,openTarget=openTarget,closeStart=closeStart,closeEnd=closeEnd,
                cutAt=closeEnd+faceSeconds,bridge=bridge,
                initialBlink=initial,openSeconds=Duration(settings.openEyes,defaults.openEyes,.04f,.25f),
                mouthSeconds=Duration(settings.mouthBlend,defaults.mouthBlend,.02f,.2f)
            };
            Motion.Complete(false);
            return true;
        }

        private static float Duration(float value,float fallback,float lo,float hi) =>
            value>0?Mathf.Clamp(value,lo,hi):fallback;
        private bool CanTransition(PortraitExpression a,PortraitExpression b,PortraitSpeechProfile pa,PortraitSpeechProfile pb)
        {
            if(image.type!=Image.Type.Simple||!PortraitExpressionSet.FitsCanvas(a.sprite,b.sprite)||string.IsNullOrWhiteSpace(a.transitionGroup)
                ||!string.Equals(a.transitionGroup,b.transitionGroup,System.StringComparison.Ordinal))return false;
            bool Supported(PortraitExpression e,PortraitSpeechProfile p)=>e.transitionEyes==PortraitExpressionEyes.Closed
                ||e.transitionEyes==PortraitExpressionEyes.Open&&PortraitExpressionSet.HasCalibratedBlink(e.sprite,p);
            return Supported(a,pa)&&Supported(b,pb);
        }
        private static Rect JoinRegion(Rect a,Rect b)
        {
            bool Valid(Rect r)=>r.width>0&&r.height>0&&r.xMin>=0&&r.yMin>=0&&r.xMax<=1&&r.yMax<=1;
            if(!Valid(a))return Valid(b)?b:default;
            if(!Valid(b))return a;
            return Rect.MinMaxRect(Mathf.Min(a.xMin,b.xMin),Mathf.Min(a.yMin,b.yMin),Mathf.Max(a.xMax,b.xMax),Mathf.Max(a.yMax,b.yMax));
        }

        private static Rect PadRegion(Rect r,Vector2 canvas,float pixels)
        {
            if(r.width<=0||r.height<=0)return default;
            float x=pixels/Mathf.Max(1,canvas.x),y=pixels/Mathf.Max(1,canvas.y);
            return Rect.MinMaxRect(Mathf.Clamp01(r.xMin-x),Mathf.Clamp01(r.yMin-y),
                Mathf.Clamp01(r.xMax+x),Mathf.Clamp01(r.yMax+y));
        }

        private static Vector4 ProjectClosedContour(PortraitBlinkEye eye,PortraitBlinkEye target)
        {
            if(!eye.IsConfigured||!target.IsConfigured)return Vector4.zero;
            // Re-express the target parabola in the source box. Keep the outgoing
            // rectangle and protection mask fixed; only its drawn eyelid changes.
            float offset=(eye.area.center.x-target.area.center.x)/target.area.width;
            float xScale=eye.area.width/target.area.width;
            float yScale=target.area.height/eye.area.height;
            float curve=target.curve;
            float height=(target.area.yMin-eye.area.yMin)/eye.area.height
                +yScale*(target.closedHeight+target.tilt*offset+4*curve*offset*offset);
            float tilt=yScale*xScale*(target.tilt+8*curve*offset);
            return new Vector4(Mathf.Clamp(height,.15f,.85f),Mathf.Clamp(tilt,-1,1),Mathf.Clamp(yScale*xScale*xScale*curve,-1,1),1);
        }

        private static void ApproachClosedContour(ref PortraitBlinkEye eye,Vector4 pose,float weight)
        {
            if(pose.w<=0)return;
            eye.closedHeight=Mathf.Lerp(eye.closedHeight,pose.x,weight);
            eye.tilt=Mathf.Lerp(eye.tilt,pose.y,weight);
            eye.curve=Mathf.Lerp(eye.curve,pose.z,weight);
        }

        private void ApplyExpression(string key,PortraitExpression expression,PortraitSpeechProfile resolved,float now,bool preservePlayback)
        {
            if(!image)image=GetComponent<Image>();
            bool changed=image.overrideSprite!=expression.sprite||CurrentExpression!=key;
            if(changed&&owned&&ClearBridge(owned))
            {
                var rendered=image.materialForRendering;
                if(rendered&&rendered!=owned)ClearBridge(rendered);
            }
            image.overrideSprite=expression.sprite;profile=resolved;CurrentExpression=key;
            if(changed){boundSprite=expression.sprite;if(!preservePlayback){boundAt=now;Motion.Reset();}blinkMaterial.Dispose();ExpressionChanged?.Invoke(key);}
        }

        public void RevealVisibleCharacter(char value,int index)
        {
            CheckSprite(Time.unscaledTime);
            if(isActiveAndEnabled)Motion.Reveal(value,index,Time.unscaledTime,framesPerSecond,hold);
        }
        public void Complete(bool immediate=false)
        {
            if(immediate&&pending!=null)
            {
                var next=pending;pending=null;boundAt=Time.unscaledTime;
                if(!next.swapped)ApplyExpression(next.key,next.target,next.targetProfile,boundAt,false);
            }
            Motion.Complete(immediate);
        }

        /// <summary>Use the host clock and, optionally, its existing portrait material and speech motion.</summary>
        public bool BeginManualPresentation(Material material=null,PortraitSpeechMotion motion=null,float blinkAge=0)
        {
            if(!isActiveAndEnabled||manual)return false;
            if(!owned)OnEnable();
            if(!owned||material&&material.shader!=owned.shader)return false;
            if(material&&material!=owned)
            {
                var temporary=owned;
                owned=material;borrowedMaterial=true;
                if(image.material==temporary)image.material=material;
                Release(temporary);
            }
            hostMotion=motion;
            boundAt=Time.unscaledTime-Mathf.Max(0,blinkAge);
            manual=true;return true;
        }
        public void PresentManually(float now,float delta)
        {
            if(manual)UpdatePresentation(now,delta);
        }
        public void EndManualPresentation()
        {
            if(manual)ResetPresentation();
        }
        private void LateUpdate()
        {
            if(!manual)UpdatePresentation(Time.unscaledTime,Time.unscaledDeltaTime);
        }
        private void UpdatePresentation(float now,float delta)
        {
            if(!owned)return;
            CheckSprite(now);
            if(pending!=null&&pending.set!=expressionSet)pending=null;
            Motion.Advance(now,delta,openTime,closeTime,transition);
            var next=pending;
            bool showBridge=next!=null&&!next.swapped&&next.bridge.patch
                &&now>=next.cutAt-BridgeSeconds&&(now<next.cutAt||!next.bridgeShown);
            // If a slow frame skips the window, display the authored pose once
            // before switching. Do not delay a transition that already showed it.
            if(showBridge)next.bridgeShown=true;
            if(next!=null&&!next.swapped&&!showBridge&&now>=next.cutAt)
            {
                next.swapped=true;next.swappedAt=now;boundAt=now;
                // New glyphs may have arrived during a mid-line transition. Keep that speech.
                ApplyExpression(next.key,next.target,next.targetProfile,now,true);
                if(!owned)return;
                next=pending;
            }
            PortraitSpeechProfile? active=boundSprite&&profile.spriteName==boundSprite.name?profile:null;
            var blink=active?.blink??default;var size=PortraitBlinkMaterial.CanvasSize(boundSprite);
            float amount=blink.CanBlink?PortraitBlinkMotion.Evaluate(now-boundAt,blinkTiming):0;
            float oldMouth=0,newFace=0;
            if(next!=null)
            {
                if(!next.swapped)
                {
                    if(next.closeSource)
                    {
                        blink.playbackDisabled=false;
                        amount=Mathf.SmoothStep(next.initialBlink,1,Mathf.InverseLerp(next.closeStart,next.closeEnd,now));
                    }
                    else amount=0;
                    // Move one fully closed contour. Mixing the two raw eye
                    // images here would double the strokes or reveal the new iris.
                    if(next.face.width>0&&now>next.closeEnd)
                    {
                        newFace=Mathf.SmoothStep(0,1,Mathf.InverseLerp(next.closeEnd,next.cutAt,now));
                        if(next.closeSource)
                        {
                            ApproachClosedContour(ref blink.leftEye,next.closedArcA,newFace);
                            ApproachClosedContour(ref blink.rightEye,next.closedArcB,newFace);
                        }
                    }
                }
                else
                {
                    float age=Mathf.Max(0,now-next.swappedAt);
                    bool blending=next.mouth.width>0&&!(active?.CanSpeak==true&&Motion.Frame>0);
                    if(blending)oldMouth=1-Mathf.SmoothStep(0,1,Mathf.Clamp01(age/next.mouthSeconds));
                    bool reopening=next.openTarget&&age<next.openSeconds;
                    if(reopening)
                    {
                        blink.playbackDisabled=false;
                        amount=1-Mathf.SmoothStep(0,1,Mathf.Clamp01(age/next.openSeconds));
                    }
                    else amount=0;
                    if(!reopening&&oldMouth<=.001f){pending=null;boundAt=now;}
                }
            }
            void Apply(Material target)
            {
                PortraitSpeechMaterial.Apply(image,target,active,Motion);
                blinkMaterial.Apply(target,blink,amount,size);
                ApplyTransitionOverlay(target,next,newFace>.001f?newFace:oldMouth,showBridge);
            }
            Apply(owned);var rendered=image.materialForRendering;if(rendered&&rendered!=owned)Apply(rendered);
        }
        private static bool ClearBridge(Material target)
        {
            if(target.GetVector(BridgeRect).z<=0)return false;
            target.SetVector(BridgeRect,Vector4.zero);
            target.SetTexture(BridgeTex,null);
            return true;
        }

        private static void ApplyTransitionOverlay(Material target,ExpressionTransition next,float weight,bool showBridge)
        {
            bool incoming=next!=null&&!next.swapped;
            Sprite sprite=next==null?null:(incoming?next.target.sprite:next.from);
            bool active=(weight>.001f||showBridge)&&sprite;
            Vector4 Bounds(Rect r)=>new Vector4(r.x,r.y,r.width,r.height);
            Vector4 SkinSample(PortraitBlinkEye eye)
            {
                var p=eye.skinSample;
                return eye.IsConfigured&&p.x>0&&p.y>0&&p.x<1&&p.y<1?new Vector4(p.x,p.y,1,0):Vector4.zero;
            }
            if(active)
            {
                if(!target.IsKeywordEnabled(TransitionMouthKeyword))target.EnableKeyword(TransitionMouthKeyword);
                target.SetTexture("_ExpressionFromTex",sprite.texture);
                target.SetVector("_ExpressionFromUV",UnityEngine.Sprites.DataUtility.GetOuterUV(sprite));
                target.SetVector("_ExpressionFromPadding",UnityEngine.Sprites.DataUtility.GetPadding(sprite));
                target.SetVector("_ExpressionMouthRegion",Bounds(incoming?next.face:next.mouth));
                target.SetVector("_ExpressionExcludeMouth",incoming?Bounds(next.mouth):Vector4.zero);
                target.SetVector("_ExpressionExcludeEyeA",incoming?Bounds(next.leftEye):Vector4.zero);
                target.SetVector("_ExpressionExcludeEyeB",incoming?Bounds(next.rightEye):Vector4.zero);
                var targetBlink=next.targetProfile.blink;
                bool tint=incoming&&next.closeSource;
                target.SetVector("_ExpressionIncomingSkinA",tint?SkinSample(targetBlink.leftEye):Vector4.zero);
                target.SetVector("_ExpressionIncomingSkinB",tint?SkinSample(targetBlink.rightEye):Vector4.zero);
                bool alignClosed=tint&&!next.openTarget;
                target.SetVector("_ExpressionClosedArcA",alignClosed?next.closedArcA:Vector4.zero);
                target.SetVector("_ExpressionClosedArcB",alignClosed?next.closedArcB:Vector4.zero);
                target.SetFloat("_ExpressionFromWeight",weight);
                if(showBridge)
                {
                    var bridge=next.bridge;
                    target.SetTexture(BridgeTex,bridge.patch);
                    target.SetVector(BridgeRect,new Vector4(bridge.bottomLeft.x,bridge.bottomLeft.y,bridge.patch.width,bridge.patch.height));
                }
                else ClearBridge(target);
            }
            else if(target.IsKeywordEnabled(TransitionMouthKeyword))
            {
                target.DisableKeyword(TransitionMouthKeyword);
                target.SetTexture("_ExpressionFromTex",null);
                target.SetFloat("_ExpressionFromWeight",0);
                ClearBridge(target);
            }
        }
        private void CheckSprite(float now){if(image&&boundSprite!=image.overrideSprite){pending=null;boundSprite=image.overrideSprite;boundAt=now;Motion.Reset();}}
        private static void Release(Material material)
        {
            if(Application.isPlaying)Destroy(material);else DestroyImmediate(material);
        }
        private void ResetPresentation()
        {
            pending=null;localMotion.Reset();blinkMaterial.Dispose();
            if(owned&&borrowedMaterial)
            {
                ApplyTransitionOverlay(owned,null,0,false);
                if(image&&image.material==owned)
                {
                    var rendered=image.materialForRendering;
                    if(rendered&&rendered!=owned)ApplyTransitionOverlay(rendered,null,0,false);
                }
            }
            else if(owned)
            {
                if(image&&image.material==owned)image.material=original;
                Release(owned);
            }
            owned=null;original=null;hostMotion=null;manual=borrowedMaterial=false;
        }
        private void OnDisable()=>ResetPresentation();
    }
}
