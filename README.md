# Unity-FastBlur-URP

Fast Kawase blur for Unity URP 2021 LTS

Blurred Texture is stored as `_CameraBlurTexture`

Incremental Mode nearly takes 0 ms to render at the cost of the update rate for the blurred texture. (recommeneded)

Standard Mode takes less than 1ms to render.
