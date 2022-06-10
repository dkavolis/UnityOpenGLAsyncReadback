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
        private static IntPtr _kickstartFunction = IntPtr.Zero;

        private static IntPtr kickStartFunction
        {
            get
            {
                if (_kickstartFunction == IntPtr.Zero) _kickstartFunction = GetKickstartFunctionPtr();
                return _kickstartFunction;
            }
        }

        private static IntPtr _renderThreadUpdateFunction = IntPtr.Zero;

        private static IntPtr renderThreadUpdateFunction
        {
            get
            {
                if (_renderThreadUpdateFunction == IntPtr.Zero)
                    _renderThreadUpdateFunction = GetUpdateRenderThreadFunctionPtr();
                return _renderThreadUpdateFunction;
            }
        }

        public static bool IsAvailable()
        {
            return SystemInfo.graphicsDeviceType == GraphicsDeviceType.OpenGLCore; //Not tested on es3 yet.
        }

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
        public bool done => Task_Done(nativeTaskHandle);

        /// <summary>
        /// Check if the request has an error
        /// </summary>
        public bool hasError => Task_Error(nativeTaskHandle);

        public static OpenGLAsyncReadbackRequest CreateTextureRequest(int textureOpenGLName, int mipmapLevel)
        {
            var result = new OpenGLAsyncReadbackRequest
            {
                nativeTaskHandle = Request_Texture(textureOpenGLName, mipmapLevel)
            };
            GL.IssuePluginEvent(kickStartFunction, result.nativeTaskHandle);
            return result;
        }

        public static unsafe OpenGLAsyncReadbackRequest CreateTextureRequest<T>(ref NativeArray<T> output,
            int textureOpenGLName, int mipmapLevel) where T : unmanaged
        {
            var result = new OpenGLAsyncReadbackRequest
            {
                nativeTaskHandle = Request_TextureIntoArray(output.GetUnsafePtr(),
                    output.Length * sizeof(T), textureOpenGLName, mipmapLevel),
                requestedAllocator = NativeArrayUtility<T>.GetAllocator(output)
            };
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            result.safetyHandle = NativeArrayUnsafeUtility.GetAtomicSafetyHandle(output);
            AtomicSafetyHandle.SetAllowReadOrWriteAccess(result.safetyHandle.Value, false);
#endif

            GL.IssuePluginEvent(kickStartFunction, result.nativeTaskHandle);
            return result;
        }

        public static OpenGLAsyncReadbackRequest CreateComputeBufferRequest(int computeBufferOpenGLName, int size)
        {
            var result = new OpenGLAsyncReadbackRequest
            {
                nativeTaskHandle = Request_ComputeBuffer(computeBufferOpenGLName, size)
            };
            GL.IssuePluginEvent(kickStartFunction, result.nativeTaskHandle);
            return result;
        }

        public static unsafe OpenGLAsyncReadbackRequest CreateComputeBufferRequest<T>(ref NativeArray<T> output,
            int computeBufferOpenGLName, int size) where T : unmanaged
        {
            var result = new OpenGLAsyncReadbackRequest
            {
                nativeTaskHandle = Request_ComputeBufferIntoArray(output.GetUnsafePtr(),
                    output.Length * sizeof(T), computeBufferOpenGLName, size),
                requestedAllocator = NativeArrayUtility<T>.GetAllocator(output)
            };
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            result.safetyHandle = NativeArrayUnsafeUtility.GetAtomicSafetyHandle(output);
            AtomicSafetyHandle.SetAllowReadOrWriteAccess(result.safetyHandle.Value, false);
#endif

            GL.IssuePluginEvent(kickStartFunction, result.nativeTaskHandle);
            return result;
        }

        public bool Valid()
        {
            return Task_Exists(nativeTaskHandle);
        }

        public unsafe NativeArray<T> GetRawData<T>() where T : struct
        {
            // Get data from cpp plugin
            void* ptr = null;
            var length = 0;
            bool success = Task_GetData(nativeTaskHandle, ref ptr, ref length);
            if (!success)
            {
                // using Task_GetData to check if the request is valid, only a single native function call if everything is fine
                if (!Task_Exists(nativeTaskHandle))
                    throw new InvalidOperationException("The request no longer exists!");
                if (!Task_Done(nativeTaskHandle))
                    throw new InvalidOperationException("The request is not done yet!");
                throw new InvalidOperationException("The request has an error!");
            }

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
            Task_WaitForCompletion(nativeTaskHandle);
        }

        internal static void Update()
        {
            UpdateMainThread();
            GL.IssuePluginEvent(renderThreadUpdateFunction, 0);
        }

        [DllImport("AsyncGPUReadbackPlugin")]
        private static extern bool CheckCompatible();

        [DllImport("AsyncGPUReadbackPlugin")]
        private static extern int Request_Texture(int texture, int miplevel);

        [DllImport("AsyncGPUReadbackPlugin")]
        private static extern unsafe int Request_TextureIntoArray(void* buffer, int size, int texture,
            int miplevel);

        [DllImport("AsyncGPUReadbackPlugin")]
        private static extern int Request_ComputeBuffer(int bufferID, int bufferSize);

        [DllImport("AsyncGPUReadbackPlugin")]
        private static extern unsafe int Request_ComputeBufferIntoArray(void* buffer, int size, int bufferID,
            int bufferSize);

        [DllImport("AsyncGPUReadbackPlugin")]
        private static extern IntPtr GetKickstartFunctionPtr();

        [DllImport("AsyncGPUReadbackPlugin")]
        private static extern void UpdateMainThread();

        [DllImport("AsyncGPUReadbackPlugin")]
        private static extern IntPtr GetUpdateRenderThreadFunctionPtr();

        [DllImport("AsyncGPUReadbackPlugin")]
        private static extern unsafe bool Task_GetData(int eventID, ref void* buffer, ref int length);

        [DllImport("AsyncGPUReadbackPlugin")]
        private static extern bool Task_Error(int eventID);

        [DllImport("AsyncGPUReadbackPlugin")]
        private static extern bool Task_Exists(int eventID);

        [DllImport("AsyncGPUReadbackPlugin")]
        private static extern bool Task_Done(int eventID);

        [DllImport("AsyncGPUReadbackPlugin")]
        private static extern void Task_WaitForCompletion(int eventID);
    }
}
