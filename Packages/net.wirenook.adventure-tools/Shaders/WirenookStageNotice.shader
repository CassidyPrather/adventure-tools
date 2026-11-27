// A screen notice for the people inside a place: one card from an atlas,
// drawn as a head-locked panel in front of every viewer whose eyes are
// within a radius of a point in the world, and of nobody else. A pause says
// so with it to exactly the people it has paused.
//
// It is Wirenook/Screen Notice with a different test for who sees it. The
// mesh is a quad whose four corners all sit on the point (a bone carries
// them there); which corner each is comes from its UV, and where the card is
// drawn comes from the viewer's own view.
Shader "Wirenook/Stage Notice"
{
    Properties
    {
        _MainTex ("Card atlas", 2D) = "white" {}
        _Columns ("Atlas columns", Float) = 4
        _Rows ("Atlas rows", Float) = 2
        _Index ("Card index", Float) = 0
        _Show ("Show", Range(0, 1)) = 0
        _Radius ("Shown to eyes within (m)", Float) = 3
        _Size ("Card width (m)", Float) = 0.5
        _Distance ("Distance from the eyes (m)", Float) = 1.6
        _Height ("Height above the centre of view (m)", Float) = 0.42
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
            // than a stray textured quad.
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
            float _Radius;
            float _Size;
            float _Distance;
            float _Height;

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

                // Capped as a share of what is visible at the card's
                // distance, so a narrow view does not push it off the edge.
                float halfView = _Distance / abs(UNITY_MATRIX_P._m11);
                float width = min(_Size, halfView * 0.9);
                float lift = min(_Height, halfView * 0.62);

                float2 corner = v.uv - 0.5;
                float settle = (1.0 - _Show) * width * 0.1;
                float3 world = eye
                    + forward * _Distance
                    + up * (lift + settle + corner.y * width * 0.5)
                    + right * (corner.x * width);
                o.pos = mul(UNITY_MATRIX_VP, float4(world, 1.0));

                // Every corner rests on the point itself.
                float3 centre = mul(unity_ObjectToWorld, v.vertex).xyz;
                bool outside = distance(centre, eye) > _Radius;
                // For the people present, not for their photos or every mirror in the room.
                bool secondary = _VRChatMirrorMode > 0.5 || _VRChatCameraMode > 0.5;
                if (outside || secondary || _Show <= 0.001)
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
