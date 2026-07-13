Shader "RoadPro/RoadPreview"
{
    Properties
    {
        _BaseColor("Base Color", Color) = (0.29, 0.40, 1.0, 0.45)
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
        }
        LOD 100

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Cull Off
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4  color      : COLOR;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                half4  color       : TEXCOORD1;
            };

            Varyings vert(Attributes input)
            {
                Varyings o;
                VertexPositionInputs posIn = GetVertexPositionInputs(input.positionOS.xyz);
                o.positionHCS = posIn.positionCS;
                o.color       = input.color;
                return o;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half3 col = input.color.rgb * _BaseColor.rgb;
                half a = input.color.a * _BaseColor.a;
                return half4(col, a);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
