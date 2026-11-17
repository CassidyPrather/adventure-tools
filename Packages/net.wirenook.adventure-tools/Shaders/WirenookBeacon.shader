// Helps a group find the person wearing it. Two things are drawn, for
// everyone but the wearer:
//
//   a pin over the wearer's head, through walls, that never gets smaller on
//   screen than a set size, so it can be picked out across a large world;
//
//   an arrow at the edge of the centre of view that points the way to turn
//   whenever the pin itself is out of view.
//
// The mesh is two unit quads whose own positions are ignored; the second UV
// set says which is which (x = 0 the pin, x = 1 the arrow). Both shapes are
// drawn from distance functions, so there is no texture. The arrow is placed
// the way the screen notice is: in VR from the midpoint between the eyes, at
// a real depth, rather than painted flat on the lenses.
//
// The arrow can only be drawn while Unity believes the mesh is in view, and
// the wearer is by definition somewhere else, so the mesh carries bounds as
// large as the beacon's range. The object rests at zero scale, which shrinks
// those bounds to nothing until the beacon is switched on.
Shader "Wirenook/Beacon"
{
    Properties
    {
        _Show ("Show", Range(0, 1)) = 0
        _Fill ("Fill", Color) = (1, 0, 0.9, 1)
        _Ink ("Outline", Color) = (0.153, 0.078, 0.322, 1)
        _Paper ("Badge", Color) = (0.906, 0.988, 1, 1)
        _Lift ("Pin height above the origin (m)", Float) = 0.42
        _PinSize ("Pin size (m)", Float) = 0.34
        _PinAngle ("Smallest pin size, as a share of its distance", Float) = 0.04
        _Near ("Pin fades out within (m)", Float) = 3
        _Range ("Visible within (m)", Float) = 80
        _ArrowSize ("Arrow size (m)", Float) = 0.11
        _ArrowDistance ("Arrow distance from the eyes (m)", Float) = 1.3
        _ArrowReach ("Arrow offset from the centre of view, as a share of its distance", Float) = 0.34
        _Edge ("Share of the view the pin must be inside to count as seen", Range(0, 1)) = 0.92
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
            // than two stray quads standing on the avatar.
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

            float _Show;
            fixed4 _Fill;
            fixed4 _Ink;
            fixed4 _Paper;
            float _Lift;
            float _PinSize;
            float _PinAngle;
            float _Near;
            float _Range;
            float _ArrowSize;
            float _ArrowDistance;
            float _ArrowReach;
            float _Edge;

            // Set by VRChat: non-zero when rendering a mirror, or a handheld
            // camera or screenshot.
            float _VRChatMirrorMode;
            float _VRChatCameraMode;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float2 kind : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 look : TEXCOORD1; // x: 0 pin, 1 arrow; y: opacity
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

                // Only the object's position is used, which survives any scale.
                float3 target = mul(unity_ObjectToWorld, float4(0, 0, 0, 1)).xyz + float3(0, _Lift, 0);
                float3 offset = target - eye;
                float range = length(offset);
                float3 seen = float3(dot(offset, right), dot(offset, up), dot(offset, forward));

                // Where the pin falls in the view, -1 to 1 across each way.
                float2 halfView = 1.0 / abs(float2(UNITY_MATRIX_P._m00, UNITY_MATRIX_P._m11));
                float2 place = seen.xy / (max(seen.z, 0.01) * halfView);
                bool inView = seen.z > 0.01 && abs(place.x) < _Edge && abs(place.y) < _Edge;

                float3 world;
                // Close enough to see the wearer, both marks are only in the way.
                float opacity = _Show * smoothstep(_Near * 0.6, _Near, range);
                bool hidden = range > _Range || opacity <= 0.001;
                if (v.kind.x < 0.5)
                {
                    float size = max(_PinSize, range * _PinAngle);
                    world = target + right * ((v.uv.x - 0.5) * size) + up * (v.uv.y * size);
                }
                else
                {
                    // Which way to turn. Someone behind is reached by turning
                    // round, not by looking up or down, so the further behind
                    // the target is the more the arrow points sideways; dead
                    // behind, where neither side is shorter, it points right.
                    float2 way = seen.xy;
                    if (seen.z < 0.0)
                    {
                        float side = seen.x >= 0.0 ? 1.0 : -1.0;
                        way = float2(seen.x - side * seen.z, seen.y * 0.25);
                    }

                    float away = length(way);
                    way = away > 0.001 ? way / away : float2(1, 0);

                    // In a headset the edge of the view is too far out to
                    // look at comfortably, so the arrow stays near the centre;
                    // in a narrow view it stays inside the picture.
                    float reach = _ArrowDistance * min(_ArrowReach, 0.72 * min(halfView.x, halfView.y));
                    float3 along = right * way.x + up * way.y;
                    float3 across = right * way.y - up * way.x;
                    world = eye + forward * _ArrowDistance + along * reach
                        + across * ((v.uv.x - 0.5) * _ArrowSize)
                        + along * ((v.uv.y - 0.5) * _ArrowSize);

                    // An arrow is for the people present, not for their
                    // photos or every mirror in the room.
                    bool secondary = _VRChatMirrorMode > 0.5 || _VRChatCameraMode > 0.5;
                    hidden = hidden || inView || secondary;
                }

                o.pos = mul(UNITY_MATRIX_VP, float4(world, 1.0));
                // Collapsing every vertex to one point draws nothing.
                if (hidden) o.pos = float4(0, 0, 0, 1);
                o.uv = v.uv;
                o.look = float2(v.kind.x, opacity);
                return o;
            }

            float circle(float2 p, float2 centre, float radius)
            {
                return length(p - centre) - radius;
            }

            float ellipse(float2 p, float2 centre, float2 radii)
            {
                // Near enough to a distance for an edge a pixel wide.
                float2 q = (p - centre) / radii;
                return (length(q) - 1.0) * min(radii.x, radii.y);
            }

            // Signed distance to a triangle (Inigo Quilez).
            float triangleShape(float2 p, float2 a, float2 b, float2 c)
            {
                float2 e0 = b - a, e1 = c - b, e2 = a - c;
                float2 v0 = p - a, v1 = p - b, v2 = p - c;
                float2 q0 = v0 - e0 * saturate(dot(v0, e0) / dot(e0, e0));
                float2 q1 = v1 - e1 * saturate(dot(v1, e1) / dot(e1, e1));
                float2 q2 = v2 - e2 * saturate(dot(v2, e2) / dot(e2, e2));
                float s = sign(e0.x * e2.y - e0.y * e2.x);
                float2 d = min(min(float2(dot(q0, q0), s * (v0.x * e0.y - v0.y * e0.x)),
                                   float2(dot(q1, q1), s * (v1.x * e1.y - v1.y * e1.x))),
                               float2(dot(q2, q2), s * (v2.x * e2.y - v2.y * e2.x)));
                return -sqrt(d.x) * sign(d.y);
            }

            // 1 inside a shape, 0 outside, one pixel of blend at its edge.
            float inside(float distance)
            {
                float edge = max(fwidth(distance), 0.0001);
                return saturate(0.5 - distance / edge);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 p = i.uv;
                float outline;
                fixed3 colour;
                if (i.look.x < 0.5)
                {
                    // A map pin, point down on the bottom edge, with a rabbit on its badge.
                    float2 head = float2(0.5, 0.67);
                    float pin = min(circle(p, head, 0.30),
                                    triangleShape(p, float2(0.5, 0.02), float2(0.775, 0.55), float2(0.225, 0.55)));
                    float rabbit = min(circle(p, float2(0.5, 0.615), 0.088),
                                       min(ellipse(p, float2(0.447, 0.755), float2(0.033, 0.088)),
                                           ellipse(p, float2(0.553, 0.755), float2(0.033, 0.088))));
                    outline = inside(pin);
                    colour = lerp(_Ink.rgb, _Fill.rgb, inside(pin + 0.04));
                    colour = lerp(colour, _Paper.rgb, inside(circle(p, head, 0.205)));
                    colour = lerp(colour, _Ink.rgb, inside(rabbit));
                }
                else
                {
                    // An arrowhead pointing up the quad, notched at the back.
                    float head = triangleShape(p, float2(0.5, 0.97), float2(0.93, 0.08), float2(0.07, 0.08));
                    float notch = triangleShape(p, float2(0.5, 0.36), float2(0.93, 0.0), float2(0.07, 0.0));
                    float arrow = max(head, -notch);
                    outline = inside(arrow);
                    colour = lerp(_Ink.rgb, _Fill.rgb, inside(arrow + 0.05));
                }

                return fixed4(colour, outline * i.look.y);
            }
            ENDCG
        }
    }
}
