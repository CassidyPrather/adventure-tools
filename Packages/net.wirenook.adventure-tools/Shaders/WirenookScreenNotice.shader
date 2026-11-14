// Draws one card from an atlas as a head-locked panel in front of every
// viewer who can see the avatar: a way to say "portal dropping" or "respawn"
// to a whole group without relying on anyone hearing it.
//
// The mesh is a unit quad whose position on the avatar is ignored. Each
// vertex is placed in the viewer's own view instead, a fixed distance ahead
// and above the centre of sight. In VR both eyes place it from the midpoint
// between them, so it sits at a real depth rather than being painted flat on
// the lenses, which is uncomfortable to look at.
Shader "Wirenook/Screen Notice"
{
    Properties
    {
        _MainTex ("Card atlas", 2D) = "white" {}
        _Columns ("Atlas columns", Float) = 4
        _Rows ("Atlas rows", Float) = 2
        _Index ("Card index", Float) = 0
        _Show ("Show", Range(0, 1)) = 0
        _Size ("Card width (m)", Float) = 0.55
        _Distance ("Distance from the eyes (m)", Float) = 1.6
        _Height ("Height above the centre of view (m)", Float) = 0.42
        _Range ("Visible within (m)", Float) = 30
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Overlay"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
            "DisableBatching" = "True"
            // If a viewer blocks this avatar's shaders, show nothing rather
            // than a stray textured quad standing on the avatar.
            "VRCFallback" = "Hidden"
        }

        Cull Off
        ZWrite Off
        ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float _Columns;
            float _Rows;
            float _Index;
            float _Show;
            float _Size;
            float _Distance;
            float _Height;
            float _Range;

            // Set by VRChat: non-zero when rendering a mirror, or a handheld
            // camera or screenshot.
            float _VRChatMirrorMode;
            float _VRChatCameraMode;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float alpha : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                #if defined(USING_STEREO_MATRICES)
                float3 eye = (unity_StereoWorldSpaceCameraPos[0] + unity_StereoWorldSpaceCameraPos[1]) * 0.5;
                #else
                float3 eye = _WorldSpaceCameraPos;
                #endif

                float3 right = normalize(unity_CameraToWorld._m00_m10_m20);
                float3 up = normalize(unity_CameraToWorld._m01_m11_m21);
                float3 forward = normalize(unity_CameraToWorld._m02_m12_m22);

                // The size and height are given in metres, which suits a
                // headset's wide view. A narrow view (a desktop player zoomed
                // in, a small window) would push a card of that size off the
                // top edge, so both are capped as a share of what is visible
                // at the card's distance.
                float halfView = _Distance / abs(UNITY_MATRIX_P._m11);
                float width = min(_Size, halfView * 0.9);
                float lift = min(_Height, halfView * 0.62);

                // Slides down into place as it appears.
                float settle = (1.0 - _Show) * width * 0.1;
                float3 world = eye
                    + forward * _Distance
                    + up * (lift + settle + v.vertex.y * width * 0.5)
                    + right * (v.vertex.x * width);
                o.pos = mul(UNITY_MATRIX_VP, float4(world, 1.0));

                float3 origin = mul(unity_ObjectToWorld, float4(0, 0, 0, 1)).xyz;
                bool tooFar = distance(origin, eye) > _Range;
                // A notice is for the people present, not for their photos or
                // every mirror in the room.
                bool secondary = _VRChatMirrorMode > 0.5 || _VRChatCameraMode > 0.5;
                if (tooFar || secondary || _Show <= 0.001)
                {
                    // Collapsing every vertex to one point draws nothing.
                    o.pos = float4(0, 0, 0, 1);
                }

                float column = fmod(_Index, _Columns);
                float row = floor(_Index / _Columns);
                // Card 0 is the top-left of the image; V runs bottom-up.
                o.uv = (float2(column, _Rows - 1.0 - row) + v.uv) / float2(_Columns, _Rows);
                o.alpha = _Show;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 colour = tex2D(_MainTex, i.uv);
                colour.a *= i.alpha;
                return colour;
            }
            ENDCG
        }
    }
}
