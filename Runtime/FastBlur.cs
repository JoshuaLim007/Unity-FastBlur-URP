using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;


namespace Limworks.Rendering.FastBlur
{
    public class FastBlur : ScriptableRendererFeature
    {

        public abstract class BlurPass : ScriptableRenderPass 
        {
            public FastBlurSettings blurSettings { get; set; }
            public RenderTargetIdentifier colorSource { get; set; }

        }
        public class BlurPassStandard : BlurPass
        {
            Material blurMat => blurSettings.blurMat;

            
            RenderTargetHandle tempTexture;
            RenderTargetHandle BlurTexture;

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
                float offset = 1.5f;
                int scale = blurSettings.BlurScale;

                cmd.SetGlobalFloat("_Offset", offset);
                cmd.Blit(colorSource, tempTexture.id, blurMat, 2);

                for (int i = 0; i < blurIterations; i += 2)
                {
                    int iScaled = i * scale;
                    int iScaled1 = (i + 1) * scale;

                    cmd.SetGlobalFloat("_Offset", offset + iScaled);
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
        
        public class BlurPassIncremental : BlurPass
        {
            Material blurMat => blurSettings.blurMat;

            Vector2Int persistantResolutions;
            RenderTexture PersistantBlurTexture;
            RenderTexture tempPersistantBlurTexture;
            RenderTargetHandle tempTexture;
            public void CreateRenderTextures(RenderTextureDescriptor renderTextureDescriptor)
            {
                if(PersistantBlurTexture != null)
                {
                    PersistantBlurTexture.Release();
                    tempPersistantBlurTexture.Release();
                }

                var desc = renderTextureDescriptor;
                desc.width = desc.width >> 1;
                desc.height = desc.height >> 1;

                //Debug.Log("Fast Blur: Creating persistant render textures...");

                PersistantBlurTexture = new RenderTexture(desc);
                tempPersistantBlurTexture = new RenderTexture(desc);
                persistantResolutions = new Vector2Int(desc.width, desc.height);
            }
            public BlurPassIncremental(RenderTextureDescriptor renderTextureDescriptor)
            {
                CreateRenderTextures(renderTextureDescriptor);
            }
            int currentIteration = 0;
            int blurIterations => (int)blurSettings.Radius;
            int scale => blurSettings.BlurScale;
            bool once = false;
            float offset = 1.5f;
            int DoBlurIteration(CommandBuffer cmd, int i)
            {
                int iScaled = i * scale;
                int iScaled1 = (i + 1) * scale;

                cmd.SetGlobalFloat("_Offset", offset + iScaled);
                cmd.Blit(tempPersistantBlurTexture, tempTexture.id, blurMat, 2);

                cmd.SetGlobalFloat("_Offset", offset + iScaled1);
                cmd.Blit(tempTexture.id, tempPersistantBlurTexture, blurMat, 2);

                return i + 2;
            }
            int FinalizeBlur(CommandBuffer cmd)
            {
                cmd.SetGlobalFloat("_Offset", offset + blurIterations * scale);
                cmd.Blit(tempPersistantBlurTexture, PersistantBlurTexture, blurMat, 2);
                return 0;
            }

            public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
            {
                var desc = renderingData.cameraData.cameraTargetDescriptor;
                if (desc.width >> 1 != persistantResolutions.x || desc.height >> 1 != persistantResolutions.y)
                {
                    CreateRenderTextures(desc);
                    currentIteration = 0;
                    once = false;
                }

                cmd.GetTemporaryRT(tempTexture.id, renderingData.cameraData.cameraTargetDescriptor, FilterMode.Point);
            }
            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                var cmd = CommandBufferPool.Get("Temporal Fast Blur");

                if (once == false)
                {
                    cmd.Blit(colorSource, PersistantBlurTexture);
                    once = true;
                }

                if (currentIteration == 0)
                {
                    cmd.SetGlobalFloat("_Offset", offset);
                    cmd.Blit(colorSource, tempPersistantBlurTexture, blurMat, 2);
                    currentIteration++;
                }
                else
                {
                    if (currentIteration > blurIterations)
                    {
                        currentIteration = FinalizeBlur(cmd);
                    }
                    else
                    {
                        currentIteration = DoBlurIteration(cmd, currentIteration - 1);
                    }
                }


                if (blurSettings.ShowBlurredTexture)
                {
                    cmd.Blit(PersistantBlurTexture, colorSource);
                }
                Shader.SetGlobalTexture("_CameraBlurTexture", PersistantBlurTexture);
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();
                CommandBufferPool.Release(cmd);
            }
            public override void FrameCleanup(CommandBuffer cmd)
            {
                cmd.ReleaseTemporaryRT(tempTexture.id);
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
        bool IsUsingIncrementalBlur = false;
        public FastBlurSettings blurSettings = new FastBlurSettings();
        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (pass == null || IsUsingIncrementalBlur != blurSettings.UseIncrementalBlur)
            {
                pass = null;

                //Debug.Log("Fast Blur: Creaing render pass...");
                if (!blurSettings.UseIncrementalBlur)
                {
                    pass = new BlurPassStandard();
                }
                else
                {
                    pass = new BlurPassIncremental(renderingData.cameraData.cameraTargetDescriptor);
                }

                pass.blurSettings = blurSettings;
                CreateMat();

                IsUsingIncrementalBlur = blurSettings.UseIncrementalBlur;
            }
            if (blurSettings.blurMat == null)
            {
                CreateMat();
                return;
            }
            blurSettings.Radius = Mathf.Min(Mathf.Max(blurSettings.Radius, 2), 32);
            blurSettings.BlurScale = Mathf.Min(Mathf.Max(blurSettings.BlurScale, 1), blurSettings.Radius >> 1);
            blurSettings.blurMat.SetFloat("KernalSize", blurSettings.Radius);
            pass.colorSource = renderer.cameraColorTarget;
            pass.renderPassEvent = (blurSettings.RenderQueue + blurSettings.QueueOffset);
            renderer.EnqueuePass(pass);
        }

        public override void Create()
        {
            pass = null;
            //nothing, creating pass on the fly
        }
    }
    [System.Serializable]
    public class FastBlurSettings
    {
        public int Radius = 8;
        public int BlurScale = 1;
        public RenderPassEvent RenderQueue = RenderPassEvent.AfterRenderingTransparents;
        public int QueueOffset = 0;
        public bool ShowBlurredTexture = false;
        public bool UseIncrementalBlur = false;
        [HideInInspector] public Material blurMat;
    }
}
