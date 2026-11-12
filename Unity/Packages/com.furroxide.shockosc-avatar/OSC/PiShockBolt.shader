Shader "Furroxide/PiShock Bolt"
{
    Properties
    {
        _MainTex ("Badge Atlas", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _EmissionMap ("Indicator Mask", 2D) = "black" {}
        [HDR] _EmissionColor ("OSC Indicator", Color) = (0,0,0,1)
        _MinBrightness ("Minimum Light", Range(0,1)) = 0.25
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
            float4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float3 albedo = tex2D(_MainTex, i.uv).rgb * _Color.rgb;
                float3 emission = tex2D(_EmissionMap, i.uv).rgb * _EmissionColor.rgb;
                float4 color = float4(albedo * i.lighting + emission, 1);
                UNITY_APPLY_FOG(i.fogCoord, color);
                return color;
            }
            ENDCG
        }
    }
    Fallback "VRChat/Mobile/Diffuse"
}
