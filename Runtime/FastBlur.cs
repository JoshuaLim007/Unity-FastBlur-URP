using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;


namespace Limworks.Rendering.FastBlur
{
    public class FastBlur : ScriptableRendererFeature
    {

        public class BlurPass : ScriptableRenderPass
        {
            public FastBlurSettings blurSettings { get; set; }
            Material blurMat => blurSettings.blurMat;

            public RenderTargetIdentifier colorSource { get; set; }
            
            public RenderTargetHandle tempTexture;
            public RenderTargetHandle BlurTexture;

            int blurIterations => (int)blurSettings.Radius;

            public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
            {
                BlurTexture.Init("_CameraBlurTexture");
                var desc = renderingData.cameraData.cameraTargetDescriptor;
                desc.width = desc.width >> 1;
                desc.height = desc.height >> 1;
                cmd.GetTemporaryRT(BlurTexture.id, desc, FilterMode.Point);
                cmd.GetTemporaryRT(tempTexture.id, desc, FilterMode.Point);
            }
            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                var cmd = CommandBufferPool.Get("Fast Blur");

                cmd.SetGlobalFloat("_Offset", 1.5f);
                cmd.Blit(colorSource, tempTexture.id, blurMat, 2);
                
                int scale = blurSettings.BlurScale;
                float offset = 1.5f;

                for (int i = 0; i < blurIterations; i += 2)
                {
                    int iScaled = i * scale;
                    int iScaled1 = (i + 1) * scale;

                    cmd.SetGlobalFloat("_Offset", offset + iScaled);
                    //apply vertical blur iteration
                    cmd.Blit(tempTexture.id, BlurTexture.id, blurMat, 2);

                    cmd.SetGlobalFloat("_Offset", offset + iScaled1);
                    cmd.Blit(BlurTexture.id, tempTexture.id, blurMat, 2);
                }

                cmd.SetGlobalFloat("_Offset", offset + blurIterations * scale);
                cmd.Blit(tempTexture.id, BlurTexture.id, blurMat, 2);
                if (blurSettings.ShowBlurredTexture)
                {
                    cmd.Blit(BlurTexture.id, colorSource);
                }

                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();
                CommandBufferPool.Release(cmd);
            }
            public override void FrameCleanup(CommandBuffer cmd)
            {
                cmd.ReleaseTemporaryRT(tempTexture.id);
                cmd.ReleaseTemporaryRT(BlurTexture.id);
            }
        }
        void CreateMat()
        {
            if(blurSettings == null)
            {
                return;
            }

            var shader = Shader.Find("hidden/FastBlur");
            if(shader == null)
            {
                Debug.LogWarning("Cannot find hidden/FastBlur shader!");
                return;
            }

            blurSettings.blurMat = CoreUtils.CreateEngineMaterial(shader);

        }
        BlurPass pass;
        [SerializeField] FastBlurSettings blurSettings = new FastBlurSettings();
        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if(blurSettings.blurMat == null)
            {
                CreateMat();
                return;
            }
            blurSettings.Radius = Mathf.Min(Mathf.Max(blurSettings.Radius, 2), 20);
            blurSettings.BlurScale = Mathf.Min(Mathf.Max(blurSettings.BlurScale, 1), blurSettings.Radius >> 1);
            blurSettings.blurMat.SetFloat("KernalSize", blurSettings.Radius);
            pass.colorSource = renderer.cameraColorTarget;
            pass.renderPassEvent = (blurSettings.RenderQueue + blurSettings.QueueOffset);
            renderer.EnqueuePass(pass);
        }
        public override void Create()
        {
            pass = new BlurPass();
            pass.blurSettings = blurSettings;
            CreateMat();
        }
    }
    [System.Serializable]
    public class FastBlurSettings
    {
        public int Radius = 8;
        public int BlurScale = 4;
        public RenderPassEvent RenderQueue = RenderPassEvent.AfterRenderingTransparents;
        public int QueueOffset = 0;
        public bool ShowBlurredTexture = false;
        [HideInInspector] public Material blurMat;
    }
}
