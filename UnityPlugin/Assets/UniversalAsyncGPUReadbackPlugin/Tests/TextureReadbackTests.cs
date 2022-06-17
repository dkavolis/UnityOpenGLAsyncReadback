using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Unity.Collections;
using UnityEngine;
using Yangrc.OpenGLAsyncReadback;
// ReSharper disable UnusedType.Global

namespace OpenglAsyncReadback.Tests
{
    [StructLayout(LayoutKind.Explicit)]
    internal struct ColorIntConverter
    {
        [FieldOffset(0)] private int i;
        [FieldOffset(0)] private Color32 c;

        public static Color32 AsColor(int i)
        {
            return new ColorIntConverter { i = i }.c;
        }
    }

    public abstract class TextureReadbackTestBase : UnitReadbackTest
    {
        private static readonly int[] Values = { 1, 2, 3, 4, 5, 42 };
        private static readonly Color32[] Colors = Values.Select(ColorIntConverter.AsColor).ToArray();
        private Texture2D texture;

        protected override IReadOnlyList<int> expected => Values;

        protected override UniversalAsyncGPUReadbackRequest Start()
        {
            texture = new Texture2D(Values.Length, 1);
            texture.SetPixels32(Colors);
            texture.Apply();

            return StartRequest(texture);
        }

        protected abstract UniversalAsyncGPUReadbackRequest StartRequest(Texture tex);

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            Object.DestroyImmediate(texture);
        }
    }

    public class TextureReadbackTest : TextureReadbackTestBase
    {
        protected override UniversalAsyncGPUReadbackRequest StartRequest(Texture tex)
        {
            return AsyncReadback.Request(tex);
        }
    }

    public class TextureReadbackIntoArrayTest : TextureReadbackTestBase
    {
        private NativeArray<int> items;
        protected override UniversalAsyncGPUReadbackRequest StartRequest(Texture tex)
        {
            items = new NativeArray<int>(tex.width * tex.height, Allocator.Persistent);
            return AsyncReadback.RequestIntoNativeArray(ref items, tex);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (items.IsCreated) items.Dispose();
        }
    }
}
