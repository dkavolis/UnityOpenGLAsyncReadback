using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

// ReSharper disable once CheckNamespace
namespace Yangrc.OpenGLAsyncReadback
{
    /// <summary>
    /// Remember RenderTexture native pointer.
    ///
    /// It's cost to call GetNativeTexturePtr() for Unity, it will cause sync between render thread and main thread.
    /// </summary>
    internal static class RenderTextureRegistry
    {
        private static readonly Dictionary<Texture, IntPtr> TexturePointers = new Dictionary<Texture, IntPtr>();

        private static readonly Dictionary<ComputeBuffer, IntPtr> ComputePointers =
            new Dictionary<ComputeBuffer, IntPtr>();

        public static IntPtr GetFor(Texture rt)
        {
            if (TexturePointers.TryGetValue(rt, out IntPtr ptr))
                return ptr;

            ptr = rt.GetNativeTexturePtr();
            TexturePointers.Add(rt, ptr);
            return ptr;
        }

        public static IntPtr GetFor(ComputeBuffer rt)
        {
            if (ComputePointers.TryGetValue(rt, out IntPtr ptr))
                return ptr;

            ptr = rt.GetNativeBufferPtr();
            ComputePointers.Add(rt, ptr);
            return ptr;
        }

        public static void ClearDeadRefs()
        {
            //Clear disposed pointers.
            foreach (KeyValuePair<ComputeBuffer, IntPtr> item in ComputePointers)
            {
                if (!item.Key.IsValid())
                    ComputePointers.Remove(item.Key);
            }

            foreach (KeyValuePair<Texture, IntPtr> item in TexturePointers)
            {
                if (item.Key == null)
                    TexturePointers.Remove(item.Key);
            }
        }
    }

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
