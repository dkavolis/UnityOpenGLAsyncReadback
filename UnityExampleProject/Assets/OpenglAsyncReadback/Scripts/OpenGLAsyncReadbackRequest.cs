using System;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Rendering;

// ReSharper disable once CheckNamespace
namespace Yangrc.OpenGLAsyncReadback
{
    internal struct OpenGLAsyncReadbackRequest
    {
        public static bool IsAvailable()
        {
            return SystemInfo.graphicsDeviceType == GraphicsDeviceType.OpenGLCore; //Not tested on es3 yet.
        }

        // TODO: return the pointer to task directly to avoid lookup on every call?

        /// <summary>
        /// Identify native task object handling the request.
        /// </summary>
        private int nativeTaskHandle;

        private Allocator requestedAllocator;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        private AtomicSafetyHandle? safetyHandle;
#endif

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

        public static unsafe OpenGLAsyncReadbackRequest CreateTextureRequest<T>(ref NativeArray<T> output,
            int textureOpenGLName, int mipmapLevel) where T : unmanaged
        {
            var result = new OpenGLAsyncReadbackRequest
            {
                nativeTaskHandle = RequestTextureIntoArrayMainThread(output.GetUnsafePtr(),
                    output.Length * sizeof(T), textureOpenGLName, mipmapLevel),
                requestedAllocator = NativeArrayUtility<T>.GetAllocator(output)
            };
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            result.safetyHandle = NativeArrayUnsafeUtility.GetAtomicSafetyHandle(output);
            AtomicSafetyHandle.SetAllowReadOrWriteAccess(result.safetyHandle.Value, false);
#endif

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

        public static unsafe OpenGLAsyncReadbackRequest CreateComputeBufferRequest<T>(ref NativeArray<T> output,
            int computeBufferOpenGLName, int size) where T : unmanaged
        {
            var result = new OpenGLAsyncReadbackRequest
            {
                nativeTaskHandle = RequestComputeBufferIntoArrayMainThread(output.GetUnsafePtr(),
                    output.Length * sizeof(T), computeBufferOpenGLName, size),
                requestedAllocator = NativeArrayUtility<T>.GetAllocator(output)
            };
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            result.safetyHandle = NativeArrayUnsafeUtility.GetAtomicSafetyHandle(output);
            AtomicSafetyHandle.SetAllowReadOrWriteAccess(result.safetyHandle.Value, false);
#endif

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

            NativeArray<T> resultNativeArray =
                NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<T>(ptr, length, requestedAllocator);
            requestedAllocator = Allocator.Invalid; // any subsequent calls to this will result in array views instead

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (safetyHandle == null) return resultNativeArray;

            // recover safety handle
            AtomicSafetyHandle.SetAllowReadOrWriteAccess(safetyHandle.Value, true);
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref resultNativeArray, safetyHandle.Value);
            safetyHandle = null;
#endif

            return resultNativeArray;
        }

        public void WaitForCompletion()
        {
            WaitForCompletion(nativeTaskHandle);
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
        private static extern unsafe int RequestTextureIntoArrayMainThread(void* buffer, int size, int texture,
            int miplevel);

        [DllImport("AsyncGPUReadbackPlugin")]
        private static extern int RequestComputeBufferMainThread(int bufferID, int bufferSize);

        [DllImport("AsyncGPUReadbackPlugin")]
        private static extern unsafe int RequestComputeBufferIntoArrayMainThread(void* buffer, int size, int bufferID,
            int bufferSize);

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

        [DllImport("AsyncGPUReadbackPlugin")]
        private static extern void WaitForCompletion(int eventID);
    }
}
