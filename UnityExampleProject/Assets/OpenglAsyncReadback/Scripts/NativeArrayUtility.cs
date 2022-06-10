using System.Reflection;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

// ReSharper disable once CheckNamespace
namespace Yangrc.OpenGLAsyncReadback
{
    internal static class NativeArrayUtility<T> where T : struct
    {
        // ReSharper disable once StaticMemberInGenericType
        private static readonly int FieldOffset;

        static NativeArrayUtility()
        {
            FieldInfo fieldInfo =
                typeof(NativeArray<T>).GetField("m_AllocatorLabel", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldOffset = UnsafeUtility.GetFieldOffset(fieldInfo);
        }

        public static unsafe Allocator GetAllocator(NativeArray<T> array)
        {
            var ptr = (byte*)UnsafeUtility.AddressOf(ref array);
            return *(Allocator*)(ptr + FieldOffset);
        }
    }
}
