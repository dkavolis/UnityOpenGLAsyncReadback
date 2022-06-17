
# UniversalAsyncGPUReadbackPlugin

This repo is heavily based on [Alabate's great work](https://github.com/Alabate/AsyncGPUReadbackPlugin), also added function to read compute buffer. The api is slightly changed from original repo, to make it easier to use. The native code has been refactored to be more safe. Since I can't reach to Alabate, so I create this new repo to add more functionality.  

On Unity 2018.2 was introduced a really neat feature: being able get a frame from the gpu to the cpu without blocking the rendering. This feature is really useful for screenshot or network stream of a game camera because we need the frame on the cpu, but we don't care if there is a little delay.

However this feature is only available on platform supporting DirectX (Windows) and Metal (Apple), but not OpenGL and it's not planned. (source: <https://forum.unity.com/threads/graphics-asynchronous-gpu-readback-api.529901/#post-3487735>).

This plugin aims to provide this feature for OpenGL platform. It tries to match the official AsyncGPUReadback as closes as possible to let you easily switch between the plugin or the official API. Under the hood, it use the official API if available on the current platform.

## Use it

### Install

Copy plugin folder at `UnityPlugin\Assets\Plugins` to your project. It contains both Windows and Linux native libraries for use.

Copy `UnityPlugin\Assets\UniversalAsyncGPUReadbackPlugin` to anywhere in your project. It contains C# code as interface of native code.

### The API

All C# apis are inside namespace `UniversalAsyncGPUReadbackPlugin`.

To start a readback, use `UniversalAsyncGPUReadbackRequest UniversalAsyncGPUReadbackRequest.Request(Texture tex)`. This function returns "universal" object, which means it could be a Unity's standard readback request, or a OpenGL request if under OpenGL environment. After that, it will automatically update during every frame(You don't have to manually call Update(), there's a global `DontDestroyOnLoad` `GameObject` doing this).

Once the request is started, you should check if it's done by `request.done` in update every frame. If it returns true, call `request.hasError` to check if any error exists. If no error, call `request.GetData<T>` to get result data in `NativeArray<T>`.

The done status will only be valid for one frame, then everything is automatically disposed. So once it's done, copy the data to your own storage ASAP.  

### Example

To see a working example you can open `UnityPlugin` with the Unity editor. It saves screenshot of the camera every 60 frames. The script taking screenshot is in `UnityPlugin/Assets/UniversalAsyncGPUReadbackPlugin/Scripts/UsePlugin.cs`

## Build Native Plugin

To build native plugin, you need to have cmake installed. If you have it, just go to NativePlugin/ folder and use cmake to build it. There's no other dependencies except OpenGL library(The `glew` library is statically linked using source code), which should always be available.

## Troubleshoots

### The type or namespace name 'AsyncGPUReadbackPluginNs' could not be found. Are you missing an assembly reference?

If you click on `AsyncGPUReadbackPlugin.dll` under Unity Editor, you will see  

```text
Plugin targets .NET 4.x and is marked as compatible with Editor, Editor can only use assemblies targeting .NET 3.5 or lower, please unselect Editor as compatible platform
```

In this case a solution is to change you runtime version in:

```text
Editor > Project Settings > Player > Other Settings > Scripting Runtime Version : Set it to '.NET 4.x Equivalent'.
```

### Other

There's lots of buffer/texture format/type in Unity, and I can't have them all tested. If you encounter any problem, feel free to create an new issue.

## Thanks

Again, thanks to [Alabate's great work](https://github.com/Alabate/AsyncGPUReadbackPlugin). It's his/her work that I learnt it's possible to do async readback under OpenGL.

# Changelog

2020-03-01 Remove ReadbackStarter class, it's not needed any more.
