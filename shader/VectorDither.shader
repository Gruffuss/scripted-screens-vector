// Unity's UI/Default, with a per-pixel dither on the output alpha. Everything except the four
// lines marked VECTOR is a verbatim copy of UI-Default.shader from Unity 2022.3's built-in
// shaders, and it has to stay verbatim: ScriptedScreens puts a RectMask2D on every surface root,
// which clips through _ClipRect and the UNITY_UI_CLIP_RECT variant. Drop any of that and vector
// scenes draw outside their console.
//
// Why it exists: a glow's Gaussian tail crosses about five levels of an 8-bit output over several
// hundred screen pixels, so each level is a band tens of pixels wide. Both the vertex colour
// (UGUI packs it to Color32) and the framebuffer quantise, and at full glow contrast the two
// steps are the same size. Dithering the alpha per pixel scatters the rounding and the eye
// averages it back to a smooth ramp.
//
// It must be ALPHA that is dithered, not rgb. The blend is dst*(1-a) + rgb*a, so a perturbation
// of rgb moves the result by only delta*a, and in the tail a is ~4/255 -- nothing. A perturbation
// of a moves it by delta*(rgb - dst), which is the full glow contrast.
//
// It must be PER PIXEL. 0.11.48 dithered at the vertices instead and made the glow visibly worse:
// vertices are tens of pixels apart, so the offset moves the whole interpolated ramp rather than
// scattering the pixels inside it, and the band edges just turned blocky.

Shader "ScriptedScreensVector/UIDither"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255

        _ColorMask ("Color Mask", Float) = 15

        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0

        // VECTOR: dither amplitude in 255ths, set from the mod's config so it can be tuned in
        // game. 0 turns the shader back into plain UI/Default.
        _VectorDither ("Dither (255ths)", Float) = 1
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "Default"
        CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                // VECTOR: a blur's coverage in x, 1 where a vertex has none of its own. It rides
                // here because the canvas carries colour as Color32; a UV interpolates as float32.
                float2 coverage : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                fixed4 color    : COLOR;
                float2 texcoord  : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                half4 mask : TEXCOORD2;
                float2 coverage : TEXCOORD3;   // VECTOR
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            fixed4 _Color;
            fixed4 _TextureSampleAdd;
            float4 _ClipRect;
            float4 _MainTex_ST;
            float _UIMaskSoftnessX;
            float _UIMaskSoftnessY;

            // VECTOR: interleaved gradient noise. A 4x4 Bayer matrix has a four-pixel period,
            // which is plainly visible on a console three thousand pixels tall; this reads as
            // fine grain instead, at the same cost and with no texture.
            float VectorNoise(float2 p)
            {
                return frac(52.9829189 * frac(dot(p, float2(0.06711056, 0.00583715))));
            }

            float _VectorDither;

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                float4 vPosition = UnityObjectToClipPos(v.vertex);
                OUT.worldPosition = v.vertex;
                OUT.vertex = vPosition;

                float2 pixelSize = vPosition.w;
                pixelSize /= float2(1, 1) * abs(mul((float2x2)UNITY_MATRIX_P, _ScreenParams.xy));

                float4 clampedRect = clamp(_ClipRect, -2e10, 2e10);
                float2 maskUV = (v.vertex.xy - clampedRect.xy) / (clampedRect.zw - clampedRect.xy);
                OUT.texcoord = TRANSFORM_TEX(v.texcoord.xy, _MainTex);
                OUT.mask = half4(v.vertex.xy * 2 - clampedRect.xy - clampedRect.zw,
                    0.25 / (0.25 * half2(_UIMaskSoftnessX, _UIMaskSoftnessY) + abs(pixelSize.xy)));

                OUT.coverage = v.coverage;     // VECTOR
                OUT.color = v.color * _Color;
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                half4 color = IN.color * (tex2D(_MainTex, IN.texcoord) + _TextureSampleAdd);

                // VECTOR: the float ramp, before anything else touches alpha.
                color.a *= IN.coverage.x;

                #ifdef UNITY_UI_CLIP_RECT
                half2 m = saturate((_ClipRect.zw - _ClipRect.xy - abs(IN.mask.xy)) * IN.mask.zw);
                color.a *= m.x * m.y;
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip (color.a - 0.001);
                #endif

                // VECTOR: last thing before the blend, and only where something is already drawn,
                // so a clipped-out or empty pixel stays exactly zero.
                //
                // The amplitude is tapered to twice the alpha near zero. Without that, saturate()
                // clips the negative half of the noise while the positive half survives, and the
                // mean is lifted: a glow's faint tail settles at about half the amplitude instead
                // of reaching black, which reads as a haze that never fades out. Tapering keeps
                // the noise symmetric wherever there is room for it and silent where there is not.
                // Below half an output level a pixel would round to the background anyway, so
                // dithering there does not recover detail -- it converts "invisible" into sparse
                // single-level speckle on a flat dark field, which is one of the easiest things
                // for an eye to find. Cut it first. The discontinuity this introduces is half a
                // level, which is by definition below what the output can show.
                color.a *= step(0.5 / 255.0, color.a);

                float amp = min(_VectorDither / 255.0, 2.0 * color.a);
                color.a = saturate(color.a + (VectorNoise(IN.vertex.xy) - 0.5) * amp);

                return color;
            }
        ENDCG
        }
    }
}
