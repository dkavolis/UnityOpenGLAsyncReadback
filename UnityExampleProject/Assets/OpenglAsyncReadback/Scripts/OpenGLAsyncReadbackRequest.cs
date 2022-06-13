using System;
using System.Collections.Generic;
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
        // native callback function pointer prototypes
        private delegate void GLIssuePluginEventDelegate(IntPtr eventPtr, int eventId);

        private delegate void RequestCallbackDelegate(int eventId);

        // make sure delegates are never collected by GC
        private static readonly GLIssuePluginEventDelegate GLIssuePluginEvent = GL.IssuePluginEvent;
        private static readonly RequestCallbackDelegate RequestDisposedCallback = OnRequestDisposed;
        private static readonly RequestCallbackDelegate RequestCompletedCompleted = OnRequestComplete;

        public static bool IsAvailable()
        {
            return SystemInfo.graphicsDeviceType == GraphicsDeviceType.OpenGLCore; //Not tested on es3 yet.
        }

        /// <summary>
        /// Identify native task object handling the request.
        /// </summary>
        private int nativeTaskHandle;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        private AtomicSafetyHandle safetyHandle;
        private bool internalStorage;
#endif

        /// <summary>
        /// Check if the request is done
        /// </summary>
        public bool done => Request_Done(nativeTaskHandle);

        /// <summary>
        /// Check if the request has an error
        /// </summary>
        public bool hasError => Request_Error(nativeTaskHandle);

        public static OpenGLAsyncReadbackRequest CreateTextureRequest(int textureOpenGLName, int mipmapLevel)
        {
            var result = new OpenGLAsyncReadbackRequest
            {
                nativeTaskHandle = Request_Texture(textureOpenGLName, mipmapLevel)
            };
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            result.internalStorage = true;
            result.safetyHandle = AtomicSafetyHandle.Create();
            AtomicSafetyHandle.SetAllowReadOrWriteAccess(result.safetyHandle, false);
            RegisterRequest(result);
#endif
            return result;
        }

        public static unsafe OpenGLAsyncReadbackRequest CreateTextureRequest<T>(ref NativeArray<T> output,
            int textureOpenGLName, int mipmapLevel) where T : unmanaged
        {
            var result = new OpenGLAsyncReadbackRequest
            {
                nativeTaskHandle = Request_TextureIntoArray(output.GetUnsafePtr(),
                    output.Length * sizeof(T), textureOpenGLName, mipmapLevel)
            };
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            result.safetyHandle = NativeArrayUnsafeUtility.GetAtomicSafetyHandle(output);
            AtomicSafetyHandle.CheckWriteAndThrow(result.safetyHandle);
            AtomicSafetyHandle.SetAllowReadOrWriteAccess(result.safetyHandle, false);
            RegisterRequest(result);
#endif

            return result;
        }

        public static OpenGLAsyncReadbackRequest CreateComputeBufferRequest(int computeBufferOpenGLName, int size)
        {
            var result = new OpenGLAsyncReadbackRequest
            {
                nativeTaskHandle = Request_ComputeBuffer(computeBufferOpenGLName, size)
            };
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            result.internalStorage = true;
            result.safetyHandle = AtomicSafetyHandle.Create();
            AtomicSafetyHandle.SetAllowReadOrWriteAccess(result.safetyHandle, false);
            RegisterRequest(result);
#endif
            return result;
        }

        public static unsafe OpenGLAsyncReadbackRequest CreateComputeBufferRequest<T>(ref NativeArray<T> output,
            int computeBufferOpenGLName, int size) where T : unmanaged
        {
            var result = new OpenGLAsyncReadbackRequest
            {
                nativeTaskHandle = Request_ComputeBufferIntoArray(output.GetUnsafePtr(),
                    output.Length * sizeof(T), computeBufferOpenGLName, size)
            };
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            result.safetyHandle = NativeArrayUnsafeUtility.GetAtomicSafetyHandle(output);
            AtomicSafetyHandle.CheckWriteAndThrow(result.safetyHandle);
            AtomicSafetyHandle.SetAllowReadOrWriteAccess(result.safetyHandle, false);
            RegisterRequest(result);
#endif
            return result;
        }

        public bool Valid()
        {
            return Request_Exists(nativeTaskHandle);
        }

        public unsafe NativeArray<T> GetRawData<T>() where T : unmanaged
        {
            // Get data from cpp plugin
            void* ptr = null;
            var length = 0;
            bool success = Request_GetData(nativeTaskHandle, ref ptr, ref length);
            if (!success)
            {
                // using Request_GetData to check if the request is valid, only a single native function call if everything is fine
                if (!Request_Exists(nativeTaskHandle))
                    throw new InvalidOperationException("The request no longer exists!");
                if (!Request_Done(nativeTaskHandle))
                    throw new InvalidOperationException("The request is not done yet!");
                throw new InvalidOperationException("The request has an error!");
            }

            NativeArray<T> resultNativeArray =
                NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<T>(ptr, length / sizeof(T), Allocator.None);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            // recover safety handle
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref resultNativeArray, safetyHandle);
#endif

            return resultNativeArray;
        }

        public void WaitForCompletion()
        {
            Request_WaitForCompletion(nativeTaskHandle);
        }

        internal static void Update()
        {
            MainThread_UpdateOnce();
        }

        internal static void Initialize()
        {
            SetGLIssuePluginEventPtr(GLIssuePluginEvent);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            SetOnCompleteCallbackPtr(RequestCompletedCompleted);
            SetOnDestructCallbackPtr(RequestDisposedCallback);
#endif
        }

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        private static readonly Dictionary<int, OpenGLAsyncReadbackRequest> InternalStorageRequests =
            new Dictionary<int, OpenGLAsyncReadbackRequest>();

        private static readonly Dictionary<int, AtomicSafetyHandle> RequestSafetyHandles =
            new Dictionary<int, AtomicSafetyHandle>();

        private static void RegisterRequest(in OpenGLAsyncReadbackRequest request)
        {
            lock (RequestSafetyHandles)
            {
                RequestSafetyHandles.Add(request.nativeTaskHandle, request.safetyHandle);
            }

            if (request.internalStorage)
                InternalStorageRequests.Add(request.nativeTaskHandle, request);
        }
#endif

        private static void OnRequestDisposed(int handle)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (!InternalStorageRequests.TryGetValue(handle, out OpenGLAsyncReadbackRequest request)) return;
            InternalStorageRequests.Remove(handle);
            // data in internal storage is deallocated so accessing it is an error
            AtomicSafetyHandle.CheckDeallocateAndThrow(request.safetyHandle);
            AtomicSafetyHandle.Release(request.safetyHandle);
#endif
        }

        private static void OnRequestComplete(int handle)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            lock (RequestSafetyHandles)
            {
                if (!RequestSafetyHandles.TryGetValue(handle, out AtomicSafetyHandle safetyHandle)) return;
                AtomicSafetyHandle.CheckExistsAndThrow(safetyHandle);
                AtomicSafetyHandle.SetAllowReadOrWriteAccess(safetyHandle, true);
            }
#endif
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
        private static extern void SetGLIssuePluginEventPtr(GLIssuePluginEventDelegate func);


        [DllImport("AsyncGPUReadbackPlugin")]
        private static extern void SetOnCompleteCallbackPtr(RequestCallbackDelegate func);

        [DllImport("AsyncGPUReadbackPlugin")]
        private static extern void SetOnDestructCallbackPtr(RequestCallbackDelegate func);


        [DllImport("AsyncGPUReadbackPlugin")]
        private static extern void MainThread_UpdateOnce();


        [DllImport("AsyncGPUReadbackPlugin")]
        private static extern unsafe bool Request_GetData(int eventID, ref void* buffer, ref int length);

        [DllImport("AsyncGPUReadbackPlugin")]
        private static extern bool Request_Error(int eventID);

        [DllImport("AsyncGPUReadbackPlugin")]
        private static extern bool Request_Exists(int eventID);

        [DllImport("AsyncGPUReadbackPlugin")]
        private static extern bool Request_Done(int eventID);

        [DllImport("AsyncGPUReadbackPlugin")]
        private static extern void Request_WaitForCompletion(int eventID);
    }
}
