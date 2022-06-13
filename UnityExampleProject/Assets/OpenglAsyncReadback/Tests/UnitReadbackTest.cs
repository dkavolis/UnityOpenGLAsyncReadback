using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Unity.Collections;
using UnityEngine.TestTools;
using Yangrc.OpenGLAsyncReadback;

namespace OpenglAsyncReadback.Tests
{
    public abstract class UnitReadbackTest : IDisposable
    {
        private UniversalAsyncGPUReadbackRequest request;

        protected abstract IReadOnlyList<int> expected { get; }

        [SetUp]
        public void Setup()
        {
            Assert.That(UnityEngine.Object.FindObjectOfType<AsyncReadback>(), Is.Not.Null);
            request = Start();
        }

        protected abstract UniversalAsyncGPUReadbackRequest Start();

        [UnityTest]
        public IEnumerator DataIsReadBackAndRequestsAreDisposedOfInTheNextFrame()
        {
            while (!request.done) yield return null;

            Assert.False(request.hasError);
            request.ReleaseNativeArray();

            NativeArray<int> data = request.GetData<int>();
            Assert.AreEqual(data.Length, expected.Count);
            Assert.That(() => { return !data.Where((t, i) => t != expected[i]).Any(); });

            yield return null;

            // request should have been disposed of by the next frame
            Assert.True(request.hasError);
            Assert.True(request.done);
        }

        protected virtual void Dispose(bool disposing)
        {
        }

        [TearDown]
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}
