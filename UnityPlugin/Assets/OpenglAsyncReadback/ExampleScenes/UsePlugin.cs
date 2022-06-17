using UnityEngine;
using System.Collections.Generic;
using Unity.Collections;
using System.IO;

// ReSharper disable once CheckNamespace
namespace Yangrc.OpenGLAsyncReadback
{
    /// <summary>
    /// Example of usage inspired from https://github.com/keijiro/AsyncCaptureTest/blob/master/Assets/AsyncCapture.cs
    /// </summary>
    public class UsePlugin : MonoBehaviour
    {
        private readonly Queue<UniversalAsyncGPUReadbackRequest> requests =
            new Queue<UniversalAsyncGPUReadbackRequest>();

        private RenderTexture rt;
        private Camera cam;

        private void Start()
        {
            cam = GetComponent<Camera>();
        }

        private void Update()
        {
            while (requests.Count > 0)
            {
                UniversalAsyncGPUReadbackRequest req = requests.Peek();

                if (req.hasError)
                {
                    Debug.LogError("GPU readback error detected.");
                    requests.Dequeue();
                }
                else if (req.done)
                {
                    // Get data from the request when it's done
                    NativeArray<byte> buffer = req.GetData<byte>();

                    // Save the image
                    SaveBitmap(buffer, cam.pixelWidth, cam.pixelHeight);

                    requests.Dequeue();
                }
                else
                {
                    break;
                }
            }
        }

        private void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            Graphics.Blit(source, destination);

            if (Time.frameCount % 60 != 0) return;
            if (requests.Count < 8)
                requests.Enqueue(AsyncReadback.Request(source));
            else
                Debug.LogWarning("Too many requests.");
        }

        private void SaveBitmap(NativeArray<byte> buffer, int width, int height)
        {
            Debug.Log("Write to file");
            var texture = new Texture2D(width, height, TextureFormat.RGBAHalf, false);
            texture.LoadRawTextureData(buffer);
            File.WriteAllBytes("test.png", texture.EncodeToPNG());
        }
    }
}
