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
            public BlurPassStandard(RenderTextureDescriptor renderTextureDescriptor)
            {
                Init(renderTextureDescriptor);
            }
            public override void Dispose()
            {
                if (BlurTexture != null)
                    BlurTexture.Release();
            }
            public void Init(RenderTextureDescriptor renderTextureDescriptor)
            {
                if(BlurTexture != null)
                    BlurTexture.Release();

                const float baseMpx = 1920 * 1080;
                const float maxMpx = 8294400;
                float mpx = renderTextureDescriptor.width * renderTextureDescriptor.height;
                float t = Mathf.InverseLerp(baseMpx, maxMpx, mpx);
                t = Mathf.Clamp01(t);
                //if resolution 4K and above, blur at half resolution
                if (mpx >= 8.29)
                {
                    float scale = Mathf.Lerp(1, 0.5f, t);
                    renderTextureDescriptor.width = Mathf.FloorToInt(renderTextureDescriptor.width * scale);
                    renderTextureDescriptor.height = Mathf.FloorToInt(renderTextureDescriptor.height * scale);
                }
                renderTextureDescriptor1 = renderTextureDescriptor;
                BlurTexture = new RenderTexture(renderTextureDescriptor.width, renderTextureDescriptor.height, 0, RenderTextureFormat.ARGBHalf, 0);
            }
            public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
            {
                if(renderingData.cameraData.cameraTargetDescriptor.width != renderTextureDescriptor1.width
                    || renderingData.cameraData.cameraTargetDescriptor.height != renderTextureDescriptor1.height)
                {
                    Init(renderingData.cameraData.cameraTargetDescriptor);
                }

                Shader.SetGlobalTexture("_CameraBlurTexture", BlurTexture);
                var desc = renderTextureDescriptor1;
                cmd.GetTemporaryRT(tempTexture.id, desc, FilterMode.Bilinear);
            }
            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                var cmd = CommandBufferPool.Get("Fast Blur");
                float offset = 1.0f;

                //first iteration of blur
                cmd.SetGlobalFloat("_Offset", offset);
                cmd.Blit(colorSource, tempTexture.id, blurMat, 2);
                
                offset = 0.5f;
                //rest of the iteration
                for (int i = 1; i < blurIterations + 1; i++)
                {
                    cmd.SetGlobalFloat("_Offset", offset * i);
                    cmd.Blit(tempTexture.id, BlurTexture, blurMat, 2);
                    cmd.Blit(BlurTexture, tempTexture.id);
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
        private void OnEnable()
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
                currentPass = new BlurPassStandard(renderingData.cameraData.cameraTargetDescriptor);
#if UNITY_EDITOR
                if (renderingData.cameraData.isSceneViewCamera)
                {
                    sceneview_pass = currentPass;
                }
                else
                {
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
