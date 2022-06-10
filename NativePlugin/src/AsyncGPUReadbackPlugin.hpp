#pragma once

#include <GL/glew.h>

#include "Unity/IUnityGraphics.h"
#include "Unity/IUnityInterface.h"

extern "C" {
void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API UnityPluginLoad(IUnityInterfaces* unityInterfaces);
void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API UnityPluginUnload();
auto UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API CheckCompatible() -> bool;
auto UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API RequestTextureMainThread(GLuint texture, int miplevel) -> int;
auto UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API RequestComputeBufferMainThread(GLuint computeBuffer, GLint bufferSize)
    -> int;
void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API KickstartRequestInRenderThread(int event_id);
auto UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API GetKickstartFunctionPtr() -> UnityRenderingEvent;
void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API UpdateRenderThread(int event_id);
auto UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API GetUpdateRenderThreadFunctionPtr() -> UnityRenderingEvent;
void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API UpdateMainThread();
void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API GetData(int event_id, void** buffer, size_t* length);
auto UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API TaskExists(int event_id) -> bool;
auto UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API TaskDone(int event_id) -> bool;
auto UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API TaskError(int event_id) -> bool;
}

void UNITY_INTERFACE_API OnGraphicsDeviceEvent(UnityGfxDeviceEventType eventType);
