using Unity.Collections;
using UnityEngine.Rendering;

// ReSharper disable once CheckNamespace
namespace Yangrc.OpenGLAsyncReadback
{
    /// <summary>
    /// Helper struct that wraps unity async readback and our opengl readback together, to hide difference
    /// </summary>
    public struct UniversalAsyncGPUReadbackRequest
    {
        internal UniversalAsyncGPUReadbackRequest(AsyncGPUReadbackRequest request)
        {
            uRequest = request;
            isPlugin = false;
            oRequest = default;
        }

        internal UniversalAsyncGPUReadbackRequest(OpenGLAsyncReadbackRequest request)
        {
            uRequest = default;
            isPlugin = false;
            oRequest = request;
        }

        public bool done => isPlugin ? oRequest.done : uRequest.done;

        public bool hasError => isPlugin ? oRequest.hasError : uRequest.hasError;

        /// <summary>
        /// Get data of a readback request.
        /// The data is allocated as temp, so it only stay alive for one frame.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public NativeArray<T> GetData<T>() where T : struct
        {
            return isPlugin ? oRequest.GetRawData<T>() : uRequest.GetData<T>();
        }

        public void WaitForCompletion()
        {
            if (isPlugin) oRequest.WaitForCompletion();
            else uRequest.WaitForCompletion();
        }

        public bool valid => !isPlugin || oRequest.Valid();

        private bool isPlugin { get; }

        //fields for unity request.
        private AsyncGPUReadbackRequest uRequest;

        //fields for opengl request.
        private OpenGLAsyncReadbackRequest oRequest;
    }
}
