// Light that adds to whatever is behind it, in the wearer's signature
// colour: the trail a lit fingertip leaves. The renderer's own colours fade
// it along its length.
Shader "Wirenook/Glow Trail"
{
    Properties
    {
        _WirenookSignature ("Signature colour (a share of a full turn of the hue)", Range(0, 1)) = 0
        _Strength ("Brightness", Range(0, 2)) = 1
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
            // Nothing at all where the avatar's shaders are blocked: a trail
            // in a fallback shader is a solid ribbon.
            "VRCFallback" = "Hidden"
        }

        Cull Off
        ZWrite Off
        Blend SrcAlpha One

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            #include "Packages/net.wirenook.assets/Shaders/WirenookSignature.hlsl"

            float _WirenookSignature;
            float _Strength;

            struct appdata
            {
                float4 vertex : POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                o.uv = v.uv;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // Brightest down the middle of the ribbon.
                float across = 1.0 - abs(i.uv.y * 2.0 - 1.0);
                float3 colour = wirenookSignatureColour(_WirenookSignature) * _Strength;
                return fixed4(colour, i.color.a * across);
            }
            ENDCG
        }
    }
}
