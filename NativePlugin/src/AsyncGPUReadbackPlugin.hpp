#pragma once

#include <GL/glew.h>

#include "Unity/IUnityGraphics.h"
#include "Unity/IUnityInterface.h"

using EventId = int;

// TODO: requests into existing buffers (avoiding allocations and copies)

extern "C" {
void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API UnityPluginLoad(IUnityInterfaces* unityInterfaces);
void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API UnityPluginUnload();
auto UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API CheckCompatible() -> bool;
auto UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API RequestTextureMainThread(GLuint texture, int miplevel) -> EventId;
auto UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API RequestComputeBufferMainThread(GLuint computeBuffer, GLint bufferSize)
    -> EventId;
void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API KickstartRequestInRenderThread(EventId event_id);
auto UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API GetKickstartFunctionPtr() -> UnityRenderingEvent;
void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API UpdateRenderThread(EventId event_id);
auto UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API GetUpdateRenderThreadFunctionPtr() -> UnityRenderingEvent;
void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API UpdateMainThread();
void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API GetData(EventId event_id, void** buffer, size_t* length);
auto UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API TaskExists(EventId event_id) -> bool;
auto UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API TaskDone(EventId event_id) -> bool;
auto UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API TaskError(EventId event_id) -> bool;
}

void UNITY_INTERFACE_API OnGraphicsDeviceEvent(UnityGfxDeviceEventType eventType);
