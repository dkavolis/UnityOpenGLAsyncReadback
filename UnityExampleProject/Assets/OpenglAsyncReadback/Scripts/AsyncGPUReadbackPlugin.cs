using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
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
            if (TexturePointers.ContainsKey(rt))
            {
                return TexturePointers[rt];
            }

            IntPtr ptr = rt.GetNativeTexturePtr();
            TexturePointers.Add(rt, ptr);
            return ptr;
        }

        public static IntPtr GetFor(ComputeBuffer rt)
        {
            if (ComputePointers.ContainsKey(rt))
            {
                return ComputePointers[rt];
            }

            IntPtr ptr = rt.GetNativeBufferPtr();
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
                {
                    TexturePointers.Remove(item.Key);
                }
            }
        }
    }


    public static class RuntimeInitializer
    {
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
            if (SystemInfo.supportsAsyncGPUReadback)
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

        public static UniversalAsyncGPUReadbackRequest Request(ComputeBuffer computeBuffer)
        {
            if (SystemInfo.supportsAsyncGPUReadback)
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

        public static UniversalAsyncGPUReadbackRequest OpenGLRequestTexture(int texture, int mipmapIndex)
        {
            return new UniversalAsyncGPUReadbackRequest
            {
                isPlugin = true,
                oRequest = OpenGLAsyncReadbackRequest.CreateTextureRequest(texture, mipmapIndex)
            };
        }

        public static UniversalAsyncGPUReadbackRequest OpenGLRequestComputeBuffer(int computeBuffer, int size)
        {
            return new UniversalAsyncGPUReadbackRequest
            {
                isPlugin = true,
                oRequest = OpenGLAsyncReadbackRequest.CreateComputeBufferRequest(computeBuffer, size)
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

        public bool valid => !isPlugin || oRequest.Valid();

        private bool isPlugin { get; set; }

        //fields for unity request.
        private AsyncGPUReadbackRequest uRequest;

        //fields for opengl request.
        private OpenGLAsyncReadbackRequest oRequest;
    }

    internal struct OpenGLAsyncReadbackRequest
    {
        public static bool IsAvailable()
        {
            return SystemInfo.graphicsDeviceType == GraphicsDeviceType.OpenGLCore; //Not tested on es3 yet.
        }

        /// <summary>
        /// Identify native task object handling the request.
        /// </summary>
        private int nativeTaskHandle;

        /// <summary>
        /// Check if the request is done
        /// </summary>
        public bool done => TaskDone(nativeTaskHandle);

        /// <summary>
        /// Check if the request has an error
        /// </summary>
        public bool hasError => TaskError(nativeTaskHandle);

        public static OpenGLAsyncReadbackRequest CreateTextureRequest(int textureOpenGLName, int mipmapLevel)
        {
            var result = new OpenGLAsyncReadbackRequest
            {
                nativeTaskHandle = RequestTextureMainThread(textureOpenGLName, mipmapLevel)
            };
            GL.IssuePluginEvent(GetKickstartFunctionPtr(), result.nativeTaskHandle);
            return result;
        }

        public static OpenGLAsyncReadbackRequest CreateComputeBufferRequest(int computeBufferOpenGLName, int size)
        {
            var result = new OpenGLAsyncReadbackRequest
            {
                nativeTaskHandle = RequestComputeBufferMainThread(computeBufferOpenGLName, size)
            };
            GL.IssuePluginEvent(GetKickstartFunctionPtr(), result.nativeTaskHandle);
            return result;
        }

        public bool Valid()
        {
            return TaskExists(nativeTaskHandle);
        }

        private void AssertRequestValid()
        {
            if (!Valid())
            {
                throw new UnityException("The request is not valid!");
            }
        }

        public unsafe NativeArray<T> GetRawData<T>() where T : struct
        {
            AssertRequestValid();
            if (!done)
            {
                throw new InvalidOperationException("The request is not done yet!");
            }

            // Get data from cpp plugin
            void* ptr = null;
            var length = 0;
            GetData(nativeTaskHandle, ref ptr, ref length);

            //Copy data from plugin native memory to unity-controlled native memory.
            var resultNativeArray = new NativeArray<T>(length / UnsafeUtility.SizeOf<T>(), Allocator.Temp);
            UnsafeUtility.MemMove(resultNativeArray.GetUnsafePtr(), ptr, length);
            //Though there exists an api named NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray.
            //It's only for internal use. The document on docs.unity3d.com is a lie.

            return resultNativeArray;
        }

        internal static void Update()
        {
            UpdateMainThread();
            GL.IssuePluginEvent(GetUpdateRenderThreadFunctionPtr(), 0);
        }

        [DllImport("AsyncGPUReadbackPlugin")]
        private static extern bool CheckCompatible();

        [DllImport("AsyncGPUReadbackPlugin")]
        private static extern int RequestTextureMainThread(int texture, int miplevel);

        [DllImport("AsyncGPUReadbackPlugin")]
        private static extern int RequestComputeBufferMainThread(int bufferID, int bufferSize);

        [DllImport("AsyncGPUReadbackPlugin")]
        private static extern IntPtr GetKickstartFunctionPtr();

        [DllImport("AsyncGPUReadbackPlugin")]
        private static extern IntPtr UpdateMainThread();

        [DllImport("AsyncGPUReadbackPlugin")]
        private static extern IntPtr GetUpdateRenderThreadFunctionPtr();

        [DllImport("AsyncGPUReadbackPlugin")]
        private static extern unsafe void GetData(int eventID, ref void* buffer, ref int length);

        [DllImport("AsyncGPUReadbackPlugin")]
        private static extern bool TaskError(int eventID);

        [DllImport("AsyncGPUReadbackPlugin")]
        private static extern bool TaskExists(int eventID);

        [DllImport("AsyncGPUReadbackPlugin")]
        private static extern bool TaskDone(int eventID);
    }
}
