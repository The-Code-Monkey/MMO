Shader "MMO/DevTextures/DevGrid"
{
    Properties
    {
        _GridColor("Grid Color", Color) = (1,1,1,1)
        _BackgroundColor("Background Color", Color) = (0,0,0,1)
        _GridScale("Grid Scale (meters)", Float) = 1
        _LineWidth("Line Width", Range(0, 0.5)) = 0.05
        _WorldSpace("Use World Space", Float) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }

        // The grid is an unlit, flat pattern: the colour is written straight out, so the pass
        // needs nothing but the world position of the fragment.
        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/UnityInstancing.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _GridColor;
                float4 _BackgroundColor;
                float _GridScale;
                float _LineWidth;
                float _WorldSpace;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float2 texcoord : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = vertexInput.positionCS;
                output.positionWS = vertexInput.positionWS;
                output.texcoord = input.texcoord;

                return output;
            }

            float GridPattern(float2 uv, float lineWidth)
            {
                // "line" and "frac" are reserved in HLSL — a local may not shadow them.
                float2 cell = frac(uv);
                float2 width = lineWidth;
                float2 edge = step(cell, width) + step(1.0 - width, cell);
                return max(edge.x, edge.y);
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float3 pos = _WorldSpace > 0.5 ? input.positionWS : float3(input.texcoord.x, 0, input.texcoord.y);
                float2 uv = pos.xz * _GridScale;
                float pattern = GridPattern(uv, _LineWidth);
                half3 albedo = lerp(_BackgroundColor.rgb, _GridColor.rgb, pattern);
                return half4(albedo, 1.0);
            }
        ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/UnityInstancing.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = vertexInput.positionCS;
                output.texcoord = input.texcoord;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                return half4(0, 0, 0, 1);
            }
        ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            #pragma multi_compile_instancing
            #pragma multi_compile_shadowcaster

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/UnityInstancing.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = vertexInput.positionCS;
                output.texcoord = input.texcoord;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                return half4(0, 0, 0, 1);
            }
        ENDHLSL
        }
    }
}
