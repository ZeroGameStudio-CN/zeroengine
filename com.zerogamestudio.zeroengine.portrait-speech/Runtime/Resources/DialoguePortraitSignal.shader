Shader "ZeroEngine/UI/PortraitSpeech"
{
    Properties
    {
        [PerRendererData] _MainTex("Sprite Texture",2D)="white" {}
        _Color("Tint",Color)=(1,1,1,1)
        _HoloColor("Screen light",Color)=(.78,.79,.8,1)
        _BackdropColor("Remote room",Color)=(.055,.057,.062,.98)
        _Connection("Connection reveal",Range(0,1))=1
        _Strength("Frame light",Range(0,1))=.9
        _SignalTime("Realtime",Float)=0
        _Layer("Background / frame / portrait",Float)=0
        _Size("Size",Vector)=(225,225,0,0)
        _UvRect("Sprite UV bounds",Vector)=(0,0,1,1)
        _GlitchStrength("Glitch strength",Range(0,1))=.8
        _GlitchInterval("Event interval",Float)=4.2
        _GlitchDuration("Event duration",Float)=.14
        _GlitchPixels("Displacement",Float)=2
        _SignalStrength("Transmission interference",Range(0,1))=0
        _SignalDuration("Transmission burst duration",Float)=.16
        _SignalPixels("Transmission displacement",Float)=5
        _ScreenTexture("Display pixel texture",Range(0,.12))=0
        _ScreenNoise("Display grain",Range(0,.12))=0
        _EditorPreview("Editor preview output",Float)=0
        _MouthFrame("Mouth pose",Float)=0
        _MouthPrevious("Previous mouth pose",Float)=0
        _MouthOpen("Mouth openness",Range(0,1))=0
        _MouthBlend("Pose transition",Range(0,1))=1
        _MouthRect("Mouth region",Vector)=(0,0,0,0)
        _UsePixelPoses("Use source pixel poses",Float)=0
        [NoScaleOffset] _MouthPoseSheet("Drawn mouth pose patches",2D)="black" {}
        _PixelLight("Light: pixel size and offset",Vector)=(2,1,0,0)
        _PixelOpen("Open: pixel size and offset",Vector)=(3,2,.5,-.5)
        _PixelRound("Round: pixel size and offset",Vector)=(2,2,0,-.5)
        _PixelWide("Wide: pixel size and offset",Vector)=(4,1,0,0)
        _PixelTight("Tight: pixel size and offset",Vector)=(1,1,.5,0)
        _SkinSample("Skin sample UV",Vector)=(0,0,0,0)
        _SampleSkinByRow("Sample the current source row",Float)=0
        _SourceSize("Source pixel grid",Vector)=(150,150,0,0)
        _SpritePadding("Trimmed sprite padding",Vector)=(0,0,0,0)
        _MouthColor("Mouth interior",Color)=(.55,.19,.24,1)
        _TongueColor("Lower mouth",Color)=(.85,.46,.5,1)
        _TeethColor("Teeth",Color)=(.98,.94,.9,1)
        _BlinkAmount("Eye closure",Range(0,1))=0
        _BlinkEyeA("Left eye source rectangle",Vector)=(0,0,0,0)
        _BlinkEyeB("Right eye source rectangle",Vector)=(0,0,0,0)
        _BlinkPoseA("Left lid height, tilt, curve",Vector)=(.4,0,.1,0)
        _BlinkPoseB("Right lid height, tilt, curve",Vector)=(.4,0,.1,0)
        _BlinkSkinA("Left eyelid skin sample",Vector)=(0,0,0,0)
        _BlinkSkinB("Right eyelid skin sample",Vector)=(0,0,0,0)
        _BlinkLidColor("Closed eyelid",Color)=(.15,.07,.085,1)
        _BlinkMask("Protected source pixels",2D)="black" {}
        _BlinkMaskRect("Protection texture source bounds",Vector)=(0,0,0,0)
        _StencilComp("Stencil Comparison",Float)=8
        _Stencil("Stencil ID",Float)=0
        _StencilOp("Stencil Operation",Float)=0
        _StencilWriteMask("Stencil Write Mask",Float)=255
        _StencilReadMask("Stencil Read Mask",Float)=255
        _ColorMask("Color Mask",Float)=15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip("Alpha clip",Float)=0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "CanUseSpriteAtlas"="True" }
        Stencil {Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask]}
        Cull Off ZWrite Off ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha ColorMask [_ColorMask]
        Pass
        {
            CGPROGRAM
            #pragma target 3.0 PORTRAIT_DRAWN_MOUTH
            #pragma multi_compile_local _ PORTRAIT_DRAWN_MOUTH
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            struct Attributes{float4 vertex:POSITION;float2 uv:TEXCOORD0;float4 color:COLOR;};
            struct Varyings{float4 vertex:SV_POSITION;float4 local:TEXCOORD1;float2 uv:TEXCOORD0;float4 color:COLOR;};
            sampler2D _MainTex;
            sampler2D _BlinkMask;
            #if defined(PORTRAIT_DRAWN_MOUTH)
            sampler2D _MouthPoseSheet;
            float4 _MouthPoseSheet_TexelSize;
            #endif
            float4 _MainTex_TexelSize,_Color,_HoloColor,_Size,_ClipRect,_UvRect,_BackdropColor;
            float _Connection,_Strength,_SignalTime,_Layer,_GlitchStrength,_GlitchInterval,_GlitchDuration,_GlitchPixels;
            float _SignalStrength,_SignalDuration,_SignalPixels,_MouthFrame,_MouthPrevious,_MouthOpen,_MouthBlend,_ScreenTexture,_ScreenNoise,_EditorPreview;
            float _SampleSkinByRow;
            float4 _MouthRect,_SkinSample,_SourceSize,_SpritePadding,_MouthColor,_TongueColor,_TeethColor;
            float _UsePixelPoses;
            float4 _PixelLight,_PixelOpen,_PixelRound,_PixelWide,_PixelTight;
            float _BlinkAmount;
            float4 _BlinkEyeA,_BlinkEyeB,_BlinkPoseA,_BlinkPoseB,_BlinkSkinA,_BlinkSkinB,_BlinkLidColor,_BlinkMaskRect;
            Varyings vert(Attributes input)
            {
                Varyings output;output.local=input.vertex;output.vertex=UnityObjectToClipPos(input.vertex);output.uv=input.uv;output.color=input.color*_Color;return output;
            }
            float2 BoundedUV(float2 value)
            {
                return clamp(value,_UvRect.xy+_MainTex_TexelSize.xy*.5,_UvRect.zw-_MainTex_TexelSize.xy*.5);
            }
            float3 MouthPose(float frame)
            {
                if(frame<.5)return float3(.23,.1,0);
                if(frame<1.5)return float3(.20,.24,0);
                if(frame<2.5)return float3(.35,.44,0);
                if(frame<3.5)return float3(.24,.42,0);
                if(frame<4.5)return float3(.43,.26,1);
                return float3(.32,.12,0);
            }
            float4 PixelPose(float frame)
            {
                if(frame<1.5)return _PixelLight;
                if(frame<2.5)return _PixelOpen;
                if(frame<3.5)return _PixelRound;
                if(frame<4.5)return _PixelWide;
                return _PixelTight;
            }
            #if defined(PORTRAIT_DRAWN_MOUTH)
            float DrawnMouthStage(float frame)
            {
                if(frame<.5||(frame>=1.5&&frame<2.5))return 0;
                return frame>=4.5?2:1;
            }
            float3 DrawnMouthAt(float2 uv,float2 span,float3 original)
            {
                float2 trimmed=max(_SourceSize.xy-_SpritePadding.xy-_SpritePadding.zw,1);
                float2 sourcePixel=_SpritePadding.xy+(uv-_UvRect.xy)/span*trimmed;
                float2 start=_MouthRect.xy*_SourceSize.xy;
                float2 grid=max(1,round(_MouthRect.zw*_SourceSize.xy));
                float2 mouthPoint=floor(sourcePixel-start+.001)+.5;
                float stage=lerp(DrawnMouthStage(_MouthPrevious),DrawnMouthStage(_MouthFrame),_MouthBlend)*_MouthOpen;
                if(any(mouthPoint<0)||any(mouthPoint>=grid)||stage<.32||
                   any(abs(_MouthPoseSheet_TexelSize.zw-grid*float2(1,2))>.01))return original;
                float row=stage<1.68?0:1;
                float2 patchUV=(mouthPoint+float2(0,row*grid.y))/(grid*float2(1,2));
                float4 patch=tex2Dlod(_MouthPoseSheet,float4(patchUV,0,0));
                return lerp(original,patch.rgb,patch.a);
            }
            #endif
            float4 PortraitPixel(float2 uv)
            {
                float4 pixel=tex2D(_MainTex,BoundedUV(uv));
                #if !defined(PORTRAIT_DRAWN_MOUTH)
                float2 span=max(_UvRect.zw-_UvRect.xy,.00001);
                float2 relative=(uv-_UvRect.xy)/span;
                float2 trimmedSize=max(_SourceSize.xy-_SpritePadding.xy-_SpritePadding.zw,1);
                float2 sourcePixel=_SpritePadding.xy+relative*trimmedSize;
                float2 sourceRelative=sourcePixel/max(_SourceSize.xy,1);
                if(_MouthFrame>.5&&all(sourceRelative>=_MouthRect.xy)&&all(sourceRelative<_MouthRect.xy+_MouthRect.zw))
                {
                    // Only the configured native mouth pixels change; closed/idle samples the original.
                    float2 grid=max(1,round(_MouthRect.zw*_SourceSize.xy));
                    float2 gridPoint=floor(sourcePixel-_MouthRect.xy*_SourceSize.xy+.001)+.5;
                    float2 q=gridPoint/grid-.5;
                    float3 pose=lerp(MouthPose(_MouthPrevious),MouthPose(_MouthFrame),_MouthBlend);
                    float2 radius=pose.xy*float2(lerp(.6,1,_MouthOpen),lerp(.3,1,_MouthOpen));
                    float colorY=q.y;
                    if(_UsePixelPoses>.5)
                    {
                        float4 pixelPose=lerp(PixelPose(_MouthPrevious),PixelPose(_MouthFrame),_MouthBlend);
                        q=gridPoint-grid*.5-pixelPose.zw;
                        radius=max(pixelPose.xy,1)*.5*float2(lerp(.6,1,_MouthOpen),lerp(.3,1,_MouthOpen));
                        colorY=q.y/max(pixelPose.y,1);
                    }
                    float2 ellipse=q/max(radius,.01);float shape=step(dot(ellipse,ellipse),1);
                    float2 skinPixel=_SkinSample.xy*_SourceSize.xy;
                    if(_SampleSkinByRow>.5)skinPixel.y=floor(sourcePixel.y+.001)+.5;
                    float2 skinRelative=(skinPixel-_SpritePadding.xy)/trimmedSize;
                    float3 skin=tex2D(_MainTex,BoundedUV(_UvRect.xy+skinRelative*span)).rgb;
                    float3 mouth=lerp(_MouthColor.rgb,_TongueColor.rgb,step(colorY,-.12));
                    mouth=lerp(mouth,_TeethColor.rgb,pose.z*step(.02,colorY));
                    pixel.rgb=lerp(skin,mouth,shape);
                }
                #endif
                return pixel;
            }
            bool InEye(float2 source,float4 box)
            {
                return all(box.zw>0)&&all(source>=box.xy)&&all(source<box.xy+box.zw);
            }
            bool ProtectedEyePixel(float2 sourcePixel)
            {
                float2 localPixel=floor(sourcePixel+.001)+.5-_BlinkMaskRect.xy;
                if(any(localPixel<0)||any(localPixel>=_BlinkMaskRect.zw))return false;
                return tex2D(_BlinkMask,localPixel/max(1,_BlinkMaskRect.zw)).r>.5;
            }
            float3 EyeBlink(float2 uv,float2 span,float3 original)
            {
                float2 sourceSize=max(_SourceSize.xy,1);
                float2 trimmed=max(sourceSize-_SpritePadding.xy-_SpritePadding.zw,1);
                float2 sourcePixel=_SpritePadding.xy+(uv-_UvRect.xy)/span*trimmed;
                float2 source=sourcePixel/sourceSize;
                bool left=InEye(source,_BlinkEyeA);
                if((!left&&!InEye(source,_BlinkEyeB))||ProtectedEyePixel(sourcePixel))return original;

                float4 box=left?_BlinkEyeA:_BlinkEyeB;
                float4 pose=left?_BlinkPoseA:_BlinkPoseB;
                float2 skin=(left?_BlinkSkinA:_BlinkSkinB).xy;
                float2 grid=max(1,round(box.zw*sourceSize));
                float2 pointInEye=floor(sourcePixel-box.xy*sourceSize+.001)+.5;
                float2 q=pointInEye/grid;
                float dx=q.x-.5;
                float lid=clamp(pose.x+dx*pose.y+dx*dx*4*pose.z,.1,.9);
                float bottom=lid*_BlinkAmount,top=lerp(1,lid,_BlinkAmount);
                float2 skinUV=_UvRect.xy+(skin*sourceSize-_SpritePadding.xy)/trimmed*span;
                float3 closed=tex2D(_MainTex,BoundedUV(skinUV)).rgb;
                if(q.y>=bottom&&q.y<top&&_BlinkAmount<.98)
                {
                    float eyeY=(q.y-bottom)/max(.01,top-bottom);
                    float2 samplePixel=box.xy*sourceSize+float2(pointInEye.x,min(grid.y-.5,floor(eyeY*grid.y)+.5));
                    if(!ProtectedEyePixel(samplePixel))
                    {
                        float2 eyeUV=_UvRect.xy+(samplePixel-_SpritePadding.xy)/trimmed*span;
                        closed=tex2D(_MainTex,BoundedUV(eyeUV)).rgb;
                    }
                }
                if(_BlinkAmount>.28&&q.x>.09&&q.x<.91&&floor(pointInEye.y)==floor(top*grid.y))
                    closed=lerp(closed,_BlinkLidColor.rgb,_BlinkLidColor.a);
                return lerp(original,closed,smoothstep(0,.16,_BlinkAmount));
            }
            fixed4 frag(Varyings input):SV_Target
            {
                float period=max(.5,_GlitchInterval);
                float eventId=floor(_SignalTime/period);
                float age=_SignalTime-eventId*period;
                float packet=(1-step(1,eventId))*(1-smoothstep(_GlitchDuration*.55,max(.03,_GlitchDuration),age))*_GlitchStrength;
                float seed=frac(sin(eventId*127.1+311.7)*43758.5453);
                float recurring=step(1,eventId)*(1-smoothstep(_SignalDuration*.4,max(.03,_SignalDuration),age))*_SignalStrength;
                float bandCenter=.35+seed*.4;
                float3 color;float alpha;
                if(_Layer>1.5)
                {
                    bool frame=_Layer>2.5;
                    float2 span=max(_UvRect.zw-_UvRect.xy,.00001);
                    float2 relative=(input.uv-_UvRect.xy)/span;
                    float band=1-smoothstep(.035,.055,abs(relative.y-bandCenter));
                    float tear=step(.68,frac(floor(relative.y*21)*.618+seed));
                    float shift=(seed>.5?1:-1)*(_GlitchPixels*band*packet+_SignalPixels*tear*recurring)/max(1,_Size.x);
                    float4 originalPixel=PortraitPixel(input.uv);
                    float4 shiftedPixel=PortraitPixel(input.uv+float2(shift*span.x,0));
                    float4 redPixel=PortraitPixel(input.uv+float2(shift*span.x*1.65,0));
                    float4 bluePixel=PortraitPixel(input.uv-float2(shift*span.x*.65,0));
                    float3 separated=float3(redPixel.a>.1?redPixel.r:originalPixel.r,shiftedPixel.a>.1?shiftedPixel.g:originalPixel.g,bluePixel.a>.1?bluePixel.b:originalPixel.b);
                    color=lerp(originalPixel.rgb,separated,saturate(band*packet+tear*recurring));
                    #if defined(PORTRAIT_DRAWN_MOUTH)
                    if(!frame&&_MouthFrame>.5)color=DrawnMouthAt(input.uv,span,color);
                    #endif
                    if(!frame&&_BlinkAmount>.001)color=EyeBlink(input.uv,span,color);
                    float noise=frac(sin(dot(floor(relative*_SourceSize.xy/4),float2(12.9898,78.233))+floor(_SignalTime*11))*43758.5453)-.5;
                    float2 cell=frac(input.local.xy*.5)-.5;
                    float pixelLattice=smoothstep(.28,.52,length(cell));
                    color=color*(1-tear*recurring*.18-pixelLattice*_ScreenTexture)+noise*_ScreenNoise;
                    alpha=frame?shiftedPixel.a:originalPixel.a;
                }
                else
                {
                    float2 uv=input.uv;
                    float2 viewPixel=(uv-.5)*_Size.xy;
                    float2 q=abs(viewPixel)-(_Size.xy*.5-10);
                    float distance=length(max(q,0))+min(max(q.x,q.y),0)-10;
                    float inside=1-smoothstep(-2,1,distance);
                    float halo=exp(-dot((uv-float2(.48,.48))*float2(2.2,1.8),(uv-float2(.48,.48))*float2(2.2,1.8)));
                    float edge=max(pow(abs(uv.x-.5)*2,10),pow(abs(uv.y-.5)*2,12));
                    float band=1-smoothstep(.035,.055,abs(uv.y-bandCenter));
                    if(_Layer<.5)
                    {
                        float glow=(halo*.13+edge*.055+packet*edge*.015)*_Strength;
                        alpha=(_BackdropColor.a+glow*.1)*inside;
                        color=(_BackdropColor.rgb*_BackdropColor.a+_HoloColor.rgb*glow*.1)/max(_BackdropColor.a+glow*.1,.001);
                    }
                    else
                    {
                        alpha=(edge*.04+packet*band*edge*.22)*_Strength*inside;
                        color=_HoloColor.rgb;
                    }
                }
                alpha*=input.color.a*_Connection;
                #ifdef UNITY_UI_CLIP_RECT
                alpha*=UnityGet2DClipping(input.local.xy,_ClipRect);
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(alpha-.001);
                #endif
                #ifndef UNITY_COLORSPACE_GAMMA
                if(_EditorPreview>.5)color=LinearToGammaSpace(color);
                #endif
                return fixed4(color*input.color.rgb,alpha);
            }
            ENDCG
        }
    }
}
