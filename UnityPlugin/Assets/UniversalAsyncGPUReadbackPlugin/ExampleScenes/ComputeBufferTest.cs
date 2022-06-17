using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

namespace UniversalAsyncGPUReadbackPlugin
{
    public class ComputeBufferTest : MonoBehaviour
    {
        private NativeArray<float> test;
        private ComputeBuffer t;

        private UniversalAsyncGPUReadbackRequest request;
        // Start is called before the first frame update

        private IEnumerator Start()
        {
            t = new ComputeBuffer(100, 4, ComputeBufferType.Default);
            var tempList = new List<float>();
            for (var i = 0; i < 100; i++)
            {
                tempList.Add(i);
            }

            t.SetData(tempList);
            yield return null;
            request = AsyncReadback.Request(t);
        }

        private void Update()
        {
            if (!request.valid) return;
            if (!request.done) return;

            test = request.GetData<float>();
            foreach (float item in test)
            {
                Debug.Log(item);
            }
        }

        private void OnDestroy()
        {
            t.Dispose();
        }
    }
}
