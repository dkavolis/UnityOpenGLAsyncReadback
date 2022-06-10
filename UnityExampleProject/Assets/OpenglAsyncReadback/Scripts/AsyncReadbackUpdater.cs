using UnityEngine;
using UnityEngine.Rendering;

// ReSharper disable once CheckNamespace
namespace Yangrc.OpenGLAsyncReadback
{
    /// <summary>
    /// A helper class to trigger readback update every frame.
    /// </summary>
    [AddComponentMenu("")]
    public class AsyncReadbackUpdater : MonoBehaviour
    {
        public static bool SupportsAsyncGPUReadback;
        private static AsyncReadbackUpdater _instance;

        private void Awake()
        {
            _instance = this;
        }

        private void Update()
        {
            OpenGLAsyncReadbackRequest.Update();
            RenderTextureRegistry.ClearDeadRefs();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
            SupportsAsyncGPUReadback = SystemInfo.supportsAsyncGPUReadback;
            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.OpenGLCore ||
                _instance != null) return;
            var go = new GameObject("__OpenGL Async Readback Updater__")
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            DontDestroyOnLoad(go);
            go.AddComponent<AsyncReadbackUpdater>();
        }
    }
}
