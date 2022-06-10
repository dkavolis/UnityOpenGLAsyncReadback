#pragma once

#include <GL/glew.h>

#include "Unity/IUnityGraphics.h"
#include "Unity/IUnityInterface.h"

using EventId = int;

#define EXPORT_API UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API

extern "C" {
void EXPORT_API UnityPluginLoad(IUnityInterfaces* unityInterfaces);
void EXPORT_API UnityPluginUnload();
auto EXPORT_API CheckCompatible() -> bool;

// requests
auto EXPORT_API Request_Texture(GLuint texture, int miplevel) -> EventId;
auto EXPORT_API Request_TextureIntoArray(void* data, size_t size, GLuint texture, int miplevel) -> EventId;
auto EXPORT_API Request_ComputeBuffer(GLuint computeBuffer, GLint bufferSize) -> EventId;
auto EXPORT_API Request_ComputeBufferIntoArray(void* data, size_t size, GLuint computeBuffer, GLint bufferSize)
    -> EventId;

// plugin events
void EXPORT_API KickstartRequestInRenderThread(EventId event_id);
auto EXPORT_API GetKickstartFunctionPtr() -> UnityRenderingEvent;
void EXPORT_API UpdateRenderThread(EventId event_id);
auto EXPORT_API GetUpdateRenderThreadFunctionPtr() -> UnityRenderingEvent;

void EXPORT_API UpdateMainThread();

// Task queries
auto EXPORT_API Task_GetData(EventId event_id, void** buffer, size_t* length) -> bool;
auto EXPORT_API Task_Exists(EventId event_id) -> bool;
auto EXPORT_API Task_Done(EventId event_id) -> bool;
auto EXPORT_API Task_Error(EventId event_id) -> bool;
void EXPORT_API Task_WaitForCompletion(EventId event_id);
}

void UNITY_INTERFACE_API OnGraphicsDeviceEvent(UnityGfxDeviceEventType eventType);
