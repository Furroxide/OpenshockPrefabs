Shader "Furroxide/PiShock Bolt"
{
    Properties
    {
        _MainTex ("Badge Atlas", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _EmissionMap ("Indicator Mask", 2D) = "black" {}
        [HDR] _EmissionColor ("OSC Indicator", Color) = (0,0,0,1)
        _MinBrightness ("Minimum Light", Range(0,1)) = 0.25
        [HideInInspector] _FeedbackEnabled ("Feedback Enabled", Float) = 0
        [HideInInspector] _FeedbackMode ("Feedback Mode", Float) = 0
        [HideInInspector] _TouchStrength ("Contact Attempt", Float) = 0
        [HideInInspector] _ActiveStrength ("Active Strength", Float) = 0
        [HideInInspector] _CooldownStrength ("Cooldown Strength", Float) = 0
        [HideInInspector] _CooldownProgress ("Cooldown Progress", Float) = 0
        [HideInInspector] _IntensityLevel ("Reported Intensity", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "VRCFallback"="Toon" }
        Pass
        {
            Name "FORWARD"
            Tags { "LightMode"="ForwardBase" }
            Cull Back
            ZWrite On
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fwdbase
            #pragma multi_compile_fog
            #pragma skip_variants LIGHTMAP_ON DYNAMICLIGHTMAP_ON DIRLIGHTMAP_COMBINED SHADOWS_SCREEN SHADOWS_DEPTH SHADOWS_CUBE SHADOWS_SHADOWMASK
            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            sampler2D _MainTex;
            sampler2D _EmissionMap;
            float4 _MainTex_ST;
            float4 _Color;
            float4 _EmissionColor;
            float _MinBrightness;
            float _FeedbackEnabled;
            float _FeedbackMode;
            float _TouchStrength;
            float _ActiveStrength;
            float _CooldownStrength;
            float _CooldownProgress;
            float _IntensityLevel;
            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 lighting : TEXCOORD1;
                UNITY_FOG_COORDS(2)
                UNITY_VERTEX_OUTPUT_STEREO
            };
            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                float3 normal = UnityObjectToWorldNormal(v.normal);
                float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                float3 lightDir = normalize(UnityWorldSpaceLightDir(worldPos));
                float diffuse = saturate(dot(normal, lightDir));
                o.lighting = max(_MinBrightness, max(0, ShadeSH9(float4(normal, 1))) + _LightColor0.rgb * diffuse);
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }
            // Front-only UVs map the lightning face to millimetres. The other faces use
            // atlas swatches and cannot receive the perimeter feedback.
            float3 feedbackPerimeter(float2 uv)
            {
                float enabled = saturate(_FeedbackEnabled);
                float active = enabled * saturate(_ActiveStrength);
                float cooling = enabled * saturate(_CooldownStrength);
                float touch = enabled * saturate(_TouchStrength)
                    * (1.0 - saturate(_ActiveStrength)) * (1.0 - saturate(_CooldownStrength));
                if (active + cooling + touch < .001) return 0;
                if (uv.x < .035 || uv.x > .81 || uv.y < .035 || uv.y > .97) return 0;
                float2 p = float2((uv.x - .04) * (60.0 / .76) - 30.0,
                                  (uv.y - .04) * (76.0 / .92) - 38.0);
                const float2 points[16] = {
                    float2(-21.6101,-6.261), float2(-21.2736,-6.8),
                    float2(-8.8735,-6.8), float2(-8.4963,-7.3571),
                    float2(-18.0507,-31.2429), float2(-17.6735,-31.8),
                    float2(-2.3497,-31.8), float2(-1.4259,-31.2949),
                    float2(20.061,2.2248), float2(19.8481,2.9982),
                    float2(9.3807,8.2319), float2(9.0857,9.0495),
                    float2(18.8543,31.2508), float2(18.4959,31.8),
                    float2(-2.4025,31.8), float2(-3.266,31.261)
                };
                float closestSquared = 1000000;
                float phase = 0;
                float travelled = 0;
                [unroll] for (int n = 0; n < 16; n++)
                {
                    float2 a = points[n];
                    float2 b = points[(n + 1) % 16];
                    float2 edge = b - a;
                    float portion = saturate(dot(p - a, edge) / max(dot(edge, edge), .0001));
                    float2 delta = p - (a + edge * portion);
                    float distanceSquared = dot(delta, delta);
                    float edgeLength = length(edge);
                    if (distanceSquared < closestSquared)
                    {
                        closestSquared = distanceSquared;
                        phase = (travelled + edgeLength * portion) / 198.26665;
                    }
                    travelled += edgeLength;
                }
                float closest = sqrt(closestSquared);
                float borderStrength = 1.0 - smoothstep(.45, 1.65, closest);
                float activeBorder = 1.0 - smoothstep(.45, 2.25, closest);
                float progress = saturate(_CooldownProgress);
                float cooldownArc = 1.0 - smoothstep(progress - .012, progress + .012, phase);
                return borderStrength * (float3(.1, .9, 1.4) * touch
                    + float3(1.25, .42, .08) * cooling * cooldownArc)
                    + activeBorder * float3(.65, 1.2, .78) * active;
            }
            float4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float3 atlas = tex2D(_MainTex, i.uv).rgb;
                float3 albedo = atlas * _Color.rgb;
                // The authored yellow bolt has a distinct red/blue and green/blue gap.
                // Restrict the mask to the front UV island so atlas swatches stay unchanged.
                float front = step(.035, i.uv.x) * (1.0 - step(.81, i.uv.x))
                    * step(.035, i.uv.y) * (1.0 - step(.97, i.uv.y));
                float bolt = front * smoothstep(.18, .42, atlas.r - atlas.b)
                    * smoothstep(.12, .34, atlas.g - atlas.b);
                float active = saturate(_FeedbackEnabled) * saturate(_ActiveStrength);
                albedo = lerp(albedo, float3(.72, .92, .76), bolt * active);

                // The existing small indicator doubles as a subordinate fill meter.
                // It is local unless the badge's installer opts into syncing intensity.
                float level = saturate(_IntensityLevel);
                float height = saturate((i.uv.y - .308) / .118);
                float fill = step(.01, level) * (1.0 - smoothstep(level - .02, level + .02, height));
                float3 emission = tex2D(_EmissionMap, i.uv).rgb * _EmissionColor.rgb
                    * lerp(1.0, max(.035, fill), active);
                emission += bolt * active * float3(.48, 1.08, .55);
                emission += feedbackPerimeter(i.uv);
                float4 color = float4(albedo * i.lighting + emission, 1);
                UNITY_APPLY_FOG(i.fogCoord, color);
                return color;
            }
            ENDCG
        }
    }
    Fallback "VRChat/Mobile/Diffuse"
}
