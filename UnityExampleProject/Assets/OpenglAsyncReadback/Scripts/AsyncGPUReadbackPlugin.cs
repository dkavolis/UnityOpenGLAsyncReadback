using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

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


    public static class RuntimeInitializer
    {
        public static readonly bool SupportsAsyncGPUReadback;

        static RuntimeInitializer()
        {
            SupportsAsyncGPUReadback = SystemInfo.supportsAsyncGPUReadback;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.OpenGLCore ||
                AsyncReadbackUpdater.Instance != null) return;
            var go = new GameObject("__OpenGL Async Readback Updater__")
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            Object.DontDestroyOnLoad(go);
            go.AddComponent<AsyncReadbackUpdater>();
        }
    }

    /// <summary>
    /// Helper struct that wraps unity async readback and our opengl readback together, to hide difference
    /// </summary>
    public struct UniversalAsyncGPUReadbackRequest
    {
        /// <summary>
        /// Request readback of a texture.
        /// </summary>
        /// <param name="src"></param>
        /// <param name="mipmapIndex"></param>
        /// <returns></returns>
        public static UniversalAsyncGPUReadbackRequest Request(Texture src, int mipmapIndex = 0)
        {
            if (RuntimeInitializer.SupportsAsyncGPUReadback)
            {
                return new UniversalAsyncGPUReadbackRequest
                {
                    isPlugin = false,
                    uRequest = AsyncGPUReadback.Request(src, mipIndex: mipmapIndex),
                };
            }

            return new UniversalAsyncGPUReadbackRequest
            {
                isPlugin = true,
                oRequest = OpenGLAsyncReadbackRequest.CreateTextureRequest(
                    RenderTextureRegistry.GetFor(src).ToInt32(), mipmapIndex)
            };
        }

        public static UniversalAsyncGPUReadbackRequest RequestIntoNativeArray<T>(ref NativeArray<T> output, Texture src,
            int mipmapIndex = 0) where T : unmanaged
        {
            if (RuntimeInitializer.SupportsAsyncGPUReadback)
            {
                return new UniversalAsyncGPUReadbackRequest
                {
                    isPlugin = false,
                    uRequest = AsyncGPUReadback.RequestIntoNativeArray(ref output, src, mipIndex: mipmapIndex),
                };
            }

            return new UniversalAsyncGPUReadbackRequest
            {
                isPlugin = true,
                oRequest = OpenGLAsyncReadbackRequest.CreateTextureRequest(ref output,
                    RenderTextureRegistry.GetFor(src).ToInt32(), mipmapIndex)
            };
        }

        public static UniversalAsyncGPUReadbackRequest Request(ComputeBuffer computeBuffer)
        {
            if (RuntimeInitializer.SupportsAsyncGPUReadback)
            {
                return new UniversalAsyncGPUReadbackRequest
                {
                    isPlugin = false,
                    uRequest = AsyncGPUReadback.Request(computeBuffer),
                };
            }

            return new UniversalAsyncGPUReadbackRequest
            {
                isPlugin = true,
                oRequest = OpenGLAsyncReadbackRequest.CreateComputeBufferRequest(
                    (int)computeBuffer.GetNativeBufferPtr(), computeBuffer.stride * computeBuffer.count),
            };
        }

        public static UniversalAsyncGPUReadbackRequest RequestIntoNativeArray<T>(ref NativeArray<T> output,
            ComputeBuffer computeBuffer) where T : unmanaged
        {
            if (RuntimeInitializer.SupportsAsyncGPUReadback)
            {
                return new UniversalAsyncGPUReadbackRequest
                {
                    isPlugin = false,
                    uRequest = AsyncGPUReadback.RequestIntoNativeArray(ref output, computeBuffer),
                };
            }

            return new UniversalAsyncGPUReadbackRequest
            {
                isPlugin = true,
                oRequest = OpenGLAsyncReadbackRequest.CreateComputeBufferRequest(ref output,
                    (int)computeBuffer.GetNativeBufferPtr(), computeBuffer.stride * computeBuffer.count),
            };
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

        private bool isPlugin { get; set; }

        //fields for unity request.
        private AsyncGPUReadbackRequest uRequest;

        //fields for opengl request.
        private OpenGLAsyncReadbackRequest oRequest;
    }
}
