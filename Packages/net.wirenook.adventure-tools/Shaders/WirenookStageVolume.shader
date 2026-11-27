// Lays a colour over everything that is inside a closed shape: the ground,
// the walls, the people. A zone a game master sets down is such a shape, and
// so is each dash of the line round it, which therefore lies exactly on
// whatever surfaces the zone's edge crosses.
//
// It needs no depth texture, which a world may or may not provide, and no
// light on the avatar to force one. It counts instead, in one bit of the
// stencil buffer, as shadow volumes do: every face of the shape that lies
// behind what is already drawn flips the bit, back faces and front faces
// alike. A pixel whose surface is inside the shape has one more back face
// behind it than front faces, and ends with the bit set; any other pixel
// ends as it began. The last pass colours the pixels whose bit is set and
// clears it, so the buffer is left as it was found.
//
// Shapes drawn in one go share the bit: where two of them overlap, the count
// is even again and the overlap is left uncoloured. A material that must not
// cancel another's uses a bit of its own (_Bit).
//
// _Spare names a stencil value that marks pixels to leave alone: wirenook
// avatars mark themselves with 128, which is how a pause dims the room and
// not the game master standing in it.
Shader "Wirenook/Stage Volume"
{
    Properties
    {
        _WirenookSignature ("Signature colour (a share of a full turn of the hue)", Range(0, 1)) = 0
        _Colour ("A colour of its own", Color) = (1, 1, 1, 1)
        _Own ("Use that colour and not the signature colour", Range(0, 1)) = 0
        _Strength ("How much is laid over what is inside", Range(0, 1)) = 0.12
        _Dim ("Lay darkness over it instead of the colour", Range(0, 1)) = 0
        _Rim ("How much the shape's own surface shows, edge on", Range(0, 1)) = 0
        _Bit ("Stencil bit it counts in (a power of two)", Float) = 16
        _Spare ("Stencil value that marks pixels to leave alone", Float) = 128
        [Enum(UnityEngine.Rendering.CompareFunction)] _SpareWhen ("Leave them alone when (Never: do not)", Float) = 1
    }

    SubShader
    {
        Tags
        {
            // After everything solid and the sky, before most things that are see-through.
            "Queue" = "Transparent-100"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
            "DisableBatching" = "True"
            // If a viewer blocks this avatar's shaders, show nothing rather
            // than a solid ball standing in the room.
            "VRCFallback" = "Hidden"
        }

        ZWrite Off

        CGINCLUDE
        #pragma multi_compile_instancing
        #include "UnityCG.cginc"
        #include "Packages/net.wirenook.assets/Shaders/WirenookSignature.hlsl"

        float _WirenookSignature;
        float4 _Colour;
        float _Own;
        float _Strength;
        float _Dim;
        float _Rim;

        struct appdata
        {
            float4 vertex : POSITION;
            float3 normal : NORMAL;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct v2f
        {
            float4 pos : SV_POSITION;
            float edge : TEXCOORD0;
            UNITY_VERTEX_OUTPUT_STEREO
        };

        v2f vert(appdata v)
        {
            v2f o;
            UNITY_SETUP_INSTANCE_ID(v);
            UNITY_INITIALIZE_OUTPUT(v2f, o);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
            o.pos = UnityObjectToClipPos(v.vertex);
            float3 world = mul(unity_ObjectToWorld, v.vertex).xyz;
            float3 normal = UnityObjectToWorldNormal(v.normal);
            float3 toEye = normalize(_WorldSpaceCameraPos - world);
            o.edge = 1.0 - abs(dot(normal, toEye));
            return o;
        }

        fixed4 nothing(v2f i) : SV_Target
        {
            return 0;
        }

        // The wearer's signature colour, unless the material has one of its
        // own: a zone that is hot is not hot in somebody's favourite blue.
        float3 tone()
        {
            return lerp(wirenookSignatureColour(_WirenookSignature), _Colour.rgb, _Own);
        }

        fixed4 laid(v2f i) : SV_Target
        {
            float3 colour = lerp(tone(), float3(0, 0, 0), _Dim);
            return fixed4(colour, _Strength);
        }

        fixed4 rim(v2f i) : SV_Target
        {
            float edge = i.edge * i.edge;
            return fixed4(tone(), _Rim * edge * edge);
        }
        ENDCG

        // Back faces that lie behind something.
        Pass
        {
            Cull Front
            ZTest LEqual
            ColorMask 0
            Stencil
            {
                WriteMask [_Bit]
                Comp Always
                ZFail Invert
            }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment nothing
            ENDCG
        }

        // Front faces that do.
        Pass
        {
            Cull Back
            ZTest LEqual
            ColorMask 0
            Stencil
            {
                WriteMask [_Bit]
                Comp Always
                ZFail Invert
            }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment nothing
            ENDCG
        }

        // Pixels that are marked to be left alone lose the bit.
        Pass
        {
            Cull Front
            ZTest Always
            ColorMask 0
            Stencil
            {
                Ref [_Spare]
                ReadMask [_Spare]
                WriteMask [_Bit]
                Comp [_SpareWhen]
                Pass Zero
            }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment nothing
            ENDCG
        }

        // What is left is inside: colour it, and clear the bit.
        Pass
        {
            Cull Front
            ZTest Always
            Blend SrcAlpha OneMinusSrcAlpha
            Stencil
            {
                Ref [_Bit]
                ReadMask [_Bit]
                WriteMask [_Bit]
                Comp Equal
                Pass Zero
            }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment laid
            ENDCG
        }

        // The shape's own surface, faintly, where it is seen edge on: what
        // shows of a zone that nothing stands in.
        Pass
        {
            Cull Back
            ZTest LEqual
            Blend SrcAlpha One

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment rim
            ENDCG
        }
    }
}
