Shader "hidden/FastBlur"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }

        //veritcal
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;

            int PassIteration;
            float KernalSize;
            sampler2D _CameraBlurTexture;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }
            float4 frag(v2f i) : SV_Target
            {
                float sy = (float)_ScreenParams.y / exp(PassIteration);
                float dy = 1 / sy;
                const int radius = 4;

                // sample the texture
                float4 col = float4(0, 0, 0, 0);
                
                float halfRadius = radius * 0.5f;

                [unroll]
                for (int ii = 0; ii < radius; ii++)
                {
                    float id = ii - halfRadius;

                    float4 t = float4(i.uv + float2(0, dy * id), 0, PassIteration);
                    col += tex2Dlod(_MainTex, t);
                }

                col /= radius;

                return col;
            }
            ENDCG
        }
    
        //horizontal
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            
            sampler2D _CameraBlurTexture;
            int PassIteration;
            float KernalSize;

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }
            float4 frag(v2f i) : SV_Target
            {
                float sx = (float)_ScreenParams.x / exp(PassIteration);
                float dx = 1 / sx;

                // sample the texture
                float4 col = float4(0, 0, 0, 0);
                const int radius = 4;

                float halfRadius = radius * 0.5f;

                [unroll]
                for (int ii = 0; ii < radius; ii++)
                {
                    float id = ii - halfRadius;
                    float4 t = float4(i.uv + float2(dx * id, 0), 0, PassIteration);
                    col += tex2Dlod(_MainTex, t);
                }

                col /= radius;

                return col;
            }
            ENDCG
        }

        //kawase
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _MainTex_TexelSize;
            float _Offset;

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }
            float4 frag(v2f i) : SV_Target
            {
                float off = _Offset;
                float2 res = _MainTex_TexelSize.xy;

                float4 col = float4(0, 0, 0, 0);
                col.rgb = tex2D(_MainTex, i.uv).rgb;

                col.rgb += tex2D(_MainTex, i.uv + float2(off, off) * res).rgb;
                col.rgb += tex2D(_MainTex, i.uv + float2(off, -off) * res).rgb;
                col.rgb += tex2D(_MainTex, i.uv + float2(-off, off) * res).rgb;
                col.rgb += tex2D(_MainTex, i.uv + float2(-off, -off) * res).rgb;
                col.rgb /= 5.0f;

                return col;
            }
            ENDCG
        }
    }
}
