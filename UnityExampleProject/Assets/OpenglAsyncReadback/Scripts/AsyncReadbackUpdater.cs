using UnityEngine;

// ReSharper disable once CheckNamespace
namespace Yangrc.OpenGLAsyncReadback
{
    /// <summary>
    /// A helper class to trigger readback update every frame.
    /// </summary>
    [AddComponentMenu("")]
    public class AsyncReadbackUpdater : MonoBehaviour
    {
        public static AsyncReadbackUpdater Instance;

        private void Awake()
        {
            Instance = this;
        }

        private void Update()
        {
            OpenGLAsyncReadbackRequest.Update();
            RenderTextureRegistry.ClearDeadRefs();
        }
    }
}
