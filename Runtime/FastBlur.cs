using System;
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
        public static FastBlur Instance { get; private set; }
        internal abstract class BlurPass : ScriptableRenderPass , IDisposable
        {
            public FastBlurSettings blurSettings { get; set; }
            public RenderTargetIdentifier colorSource { get; set; }

            public virtual void Dispose()
            {
            }
        }
        internal class BlurPassStandard : BlurPass
        {
            Material blurMat => blurSettings.BlurMat;

            
            RenderTargetHandle tempTexture;
            RenderTexture BlurTexture;

            int blurIterations => (int)blurSettings.Radius;
            RenderTextureDescriptor renderTextureDescriptor1;
            Resolution downScaledResolution;
            bool isScene = false;
            public BlurPassStandard(RenderTextureDescriptor renderTextureDescriptor, bool isScene = false)
            {
                Init(renderTextureDescriptor);
                this.isScene = isScene;
                if(isScene)
                    tempTexture.Init("_tempTexture_scene");
                else
                    tempTexture.Init("_tempTexture");
            }
            public override void Dispose()
            {
                if (BlurTexture != null)
                    BlurTexture.Release();
            }
            public void Init(RenderTextureDescriptor renderTextureDescriptor)
            {
                renderTextureDescriptor.mipCount = 0;
                renderTextureDescriptor.useMipMap = false;
                renderTextureDescriptor.depthBufferBits = 0;
                renderTextureDescriptor.colorFormat = RenderTextureFormat.RGB111110Float;
                renderTextureDescriptor1 = renderTextureDescriptor;

                Dispose();

                const float baseMpx = 1920 * 1080;
                const float maxMpx = 8294400;
                float mpx = renderTextureDescriptor.width * renderTextureDescriptor.height;
                float t = Mathf.InverseLerp(baseMpx, maxMpx, mpx);
                t = Mathf.Clamp01(t);
                float scale = Mathf.Lerp(1, 0.5f, t);
                downScaledResolution = new Resolution();
                downScaledResolution.height = Mathf.FloorToInt(renderTextureDescriptor.height * scale);
                downScaledResolution.width = Mathf.FloorToInt(renderTextureDescriptor.width * scale);
                renderTextureDescriptor.width = downScaledResolution.width;
                renderTextureDescriptor.height = downScaledResolution.height;
                BlurTexture = new RenderTexture(renderTextureDescriptor.width, renderTextureDescriptor.height, 0, renderTextureDescriptor.colorFormat, 0);
                BlurTexture.filterMode = FilterMode.Bilinear;
                BlurTexture.name = isScene ? "_CameraBlurTexture_scene" : "_CameraBlurTexture";
            }
            public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
            {
                if(renderingData.cameraData.cameraTargetDescriptor.width != renderTextureDescriptor1.width
                    || renderingData.cameraData.cameraTargetDescriptor.height != renderTextureDescriptor1.height)
                {
                    Init(renderingData.cameraData.cameraTargetDescriptor);
                }
                Shader.SetGlobalTexture(BlurTexture.name, BlurTexture);
            }
            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                var cmd = CommandBufferPool.Get("Fast Blur");
                cmd.GetTemporaryRT(tempTexture.id, downScaledResolution.width, downScaledResolution.height, 0, FilterMode.Bilinear, renderTextureDescriptor1.colorFormat);

                float offset = 0.5f;

                //first iteration of blur
                cmd.SetGlobalFloat("_Offset", offset);
                if(blurIterations == 1)
                {
                    cmd.Blit(colorSource, BlurTexture, blurMat, 2);
                }
                else
                {
                    cmd.Blit(colorSource, tempTexture.id, blurMat, 2);
                }

                offset = 0.5f;
                //rest of the iteration
                int iterationCount = 1;
                int maxIter = blurIterations;
                while (iterationCount < maxIter)
                {
                    cmd.SetGlobalFloat("_Offset", offset * (float)iterationCount);
                    cmd.Blit(tempTexture.id, BlurTexture, blurMat, 2);
                    iterationCount++;

                    if(iterationCount < blurIterations)
                    {
                        cmd.SetGlobalFloat("_Offset", offset * (float)iterationCount);
                        cmd.Blit(BlurTexture, tempTexture.id, blurMat, 2);
                        iterationCount++;

                        if(iterationCount >= blurIterations)
                        {
                            cmd.Blit(tempTexture.id, BlurTexture);
                        }
                    }
                }

                if (blurSettings.ShowBlurredTexture)
                {
                    cmd.Blit(BlurTexture, colorSource);
                }

                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();
                CommandBufferPool.Release(cmd);
            }
            public override void FrameCleanup(CommandBuffer cmd)
            {
                cmd.ReleaseTemporaryRT(tempTexture.id);
            }
        }
        private void Awake()
        {
            Instance = this;
        }
        void CreateMat()
        {
            var shader = Shader.Find("hidden/FastBlur");
            if(shader == null)
            {
                Debug.LogWarning("Cannot find hidden/FastBlur shader!");
                return;
            }

            Settings.BlurMat = CoreUtils.CreateEngineMaterial(shader);

        }
        BlurPass pass = null;
#if UNITY_EDITOR
        BlurPass sceneview_pass = null;
#endif
        public FastBlurSettings Settings = FastBlurSettings.Default;
        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            Settings.Radius = Mathf.Min(Mathf.Max(Settings.Radius, 1), 128);
#if UNITY_EDITOR
            BlurPass currentPass = renderingData.cameraData.isSceneViewCamera ? sceneview_pass : pass;
#else
            BlurPass currentPass = pass;
#endif
            if (currentPass == null)
            {
                
#if UNITY_EDITOR
                if (renderingData.cameraData.isSceneViewCamera)
                {
                    currentPass = new BlurPassStandard(renderingData.cameraData.cameraTargetDescriptor, true);
                    sceneview_pass = currentPass;
                }
                else
                {
                    currentPass = new BlurPassStandard(renderingData.cameraData.cameraTargetDescriptor, false);
                    pass = currentPass;
                }
#else
                pass = currentPass;
#endif
                CreateMat();
            }
            currentPass.blurSettings = Settings;
            if (Settings.BlurMat == null)
            {
                CreateMat();
                return;
            }
            currentPass.colorSource = renderer.cameraColorTarget;
            currentPass.renderPassEvent = (Settings.RenderQueue + Settings.QueueOffset);
            renderer.EnqueuePass(currentPass);
        }

        private void OnDestroy()
        {
            Dispose(true);
        }
        private void OnValidate()
        {
            Dispose(true);
        }
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if(pass != null)
                pass.Dispose();

#if UNITY_EDITOR
            if (sceneview_pass != null)
                sceneview_pass.Dispose();
#endif
        }
        public override void Create()
        {
            pass = null;
            //nothing, creating pass on the fly
        }
    }
    [System.Serializable]
    public struct FastBlurSettings
    {
        [Tooltip("Approximate blur radius")]
        [Range(1, 128)]
        public int Radius;
        [Tooltip("When to do blurring")]
        public RenderPassEvent RenderQueue;
        [Tooltip("When to do blurring + offset")]
        public int QueueOffset;
        [Tooltip("Render blurred texture to screen")]
        public bool ShowBlurredTexture;
        internal Material BlurMat { get; set; }
        public static FastBlurSettings Default => new FastBlurSettings()
        {
            BlurMat = null,
            Radius = 32,
            RenderQueue = RenderPassEvent.AfterRenderingTransparents,
            QueueOffset = 0,
            ShowBlurredTexture = false,
        };
    }
}
