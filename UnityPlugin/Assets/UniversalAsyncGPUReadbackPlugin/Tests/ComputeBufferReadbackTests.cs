using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

// ReSharper disable UnusedType.Global

namespace UniversalAsyncGPUReadbackPlugin.Tests
{
    public abstract class ComputeBufferReadbackTestBase : UnitReadbackTest
    {
        private static readonly int[] Values = { 1, 2, 3, 4, 5, 42 };
        private ComputeBuffer buffer;

        protected override IReadOnlyList<int> expected => Values;

        protected override UniversalAsyncGPUReadbackRequest Start()
        {
            buffer = new ComputeBuffer(Values.Length, sizeof(int));
            buffer.SetData(Values);

            return StartRequest(buffer);
        }

        protected abstract UniversalAsyncGPUReadbackRequest StartRequest(ComputeBuffer buffer);

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            buffer?.Dispose();
        }
    }

    public class ComputeBufferReadbackTest : ComputeBufferReadbackTestBase
    {
        protected override UniversalAsyncGPUReadbackRequest StartRequest(ComputeBuffer buffer)
        {
            return AsyncReadback.Request(buffer);
        }
    }

    public class ComputeBufferReadbackIntoArrayTest : ComputeBufferReadbackTestBase
    {
        private NativeArray<int> items;
        protected override UniversalAsyncGPUReadbackRequest StartRequest(ComputeBuffer buffer)
        {
            items = new NativeArray<int>(buffer.count, Allocator.Persistent);
            return AsyncReadback.RequestIntoNativeArray(ref items, buffer);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (items.IsCreated) items.Dispose();
        }
    }
}
