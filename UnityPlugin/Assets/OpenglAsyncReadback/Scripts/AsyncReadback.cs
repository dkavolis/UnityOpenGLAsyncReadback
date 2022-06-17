using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

// ReSharper disable once CheckNamespace
namespace Yangrc.OpenGLAsyncReadback
{
    /// <summary>
    /// A helper class to trigger readback update every frame.
    /// </summary>
    [AddComponentMenu("")]
    public class AsyncReadback : MonoBehaviour
    {
        private static bool _supportsAsyncGPUReadback;
        public static AsyncReadback instance { get; private set; }

        private void Awake()
        {
            instance = this;
        }

        private void Update()
        {
            OpenGLAsyncReadbackRequest.Update();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
            _supportsAsyncGPUReadback = SystemInfo.supportsAsyncGPUReadback;
            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.OpenGLCore) return;
            OpenGLAsyncReadbackRequest.Initialize();

            if (instance != null) return;
            var go = new GameObject("__OpenGL Async Readback Updater__");
            go.AddComponent<AsyncReadback>();
            if (!Application.isEditor) go.hideFlags = HideFlags.HideAndDontSave;

            DontDestroyOnLoad(go);
        }

        /// <summary>
        /// Request readback of a texture.
        /// </summary>
        /// <param name="src"></param>
        /// <param name="mipmapIndex"></param>
        /// <returns></returns>
        public static UniversalAsyncGPUReadbackRequest Request(Texture src, int mipmapIndex = 0)
        {
            if (_supportsAsyncGPUReadback)
                return new UniversalAsyncGPUReadbackRequest(AsyncGPUReadback.Request(src, mipIndex: mipmapIndex));

            return new UniversalAsyncGPUReadbackRequest(
                OpenGLAsyncReadbackRequest.CreateTextureRequest(src.GetNativeTexturePtr().ToInt32(), mipmapIndex));
        }

        public static UniversalAsyncGPUReadbackRequest RequestIntoNativeArray<T>(ref NativeArray<T> output, Texture src,
            int mipmapIndex = 0) where T : unmanaged
        {
            if (_supportsAsyncGPUReadback)
                return new UniversalAsyncGPUReadbackRequest(
                    AsyncGPUReadback.RequestIntoNativeArray(ref output, src, mipIndex: mipmapIndex));

            return new UniversalAsyncGPUReadbackRequest(OpenGLAsyncReadbackRequest.CreateTextureRequest(ref output,
                src.GetNativeTexturePtr().ToInt32(), mipmapIndex));
        }

        public static UniversalAsyncGPUReadbackRequest Request(ComputeBuffer computeBuffer)
        {
            if (_supportsAsyncGPUReadback)
                return new UniversalAsyncGPUReadbackRequest(AsyncGPUReadback.Request(computeBuffer));

            return new UniversalAsyncGPUReadbackRequest(OpenGLAsyncReadbackRequest.CreateComputeBufferRequest(
                (int)computeBuffer.GetNativeBufferPtr(), computeBuffer.stride * computeBuffer.count));
        }

        public static UniversalAsyncGPUReadbackRequest RequestIntoNativeArray<T>(ref NativeArray<T> output,
            ComputeBuffer computeBuffer) where T : unmanaged
        {
            if (_supportsAsyncGPUReadback)
                return new UniversalAsyncGPUReadbackRequest(
                    AsyncGPUReadback.RequestIntoNativeArray(ref output, computeBuffer));

            return new UniversalAsyncGPUReadbackRequest(OpenGLAsyncReadbackRequest.CreateComputeBufferRequest(
                ref output,
                (int)computeBuffer.GetNativeBufferPtr(), computeBuffer.stride * computeBuffer.count));
        }
    }
}
