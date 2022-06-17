#pragma once

#include <GL/glew.h>

#include "Unity/IUnityGraphics.h"
#include "Unity/IUnityInterface.h"

using EventId = int;
using GL_IssuePluginEventPtr = void(UNITY_INTERFACE_API*)(UnityRenderingEvent, EventId);
using RequestCallbackPtr = void(UNITY_INTERFACE_API*)(EventId);

#define EXPORT_API UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API

extern "C" {
// plugin interface

/**
 * @brief Unity plugin load event
 */
void EXPORT_API UnityPluginLoad(IUnityInterfaces* unityInterfaces);

/**
 * @brief Unity unload plugin event
 */
void EXPORT_API UnityPluginUnload();

/**
 * @brief Check if plugin is compatible with this system
 * This plugin is only compatible with opengl core
 */
auto EXPORT_API CheckCompatible() -> bool;

// requests
auto EXPORT_API Request_Texture(GLuint texture, int miplevel) -> EventId;
auto EXPORT_API Request_TextureIntoArray(void* data, size_t size, GLuint texture, int miplevel) -> EventId;
auto EXPORT_API Request_ComputeBuffer(GLuint computeBuffer, GLint bufferSize) -> EventId;
auto EXPORT_API Request_ComputeBufferIntoArray(void* data, size_t size, GLuint computeBuffer, GLint bufferSize)
    -> EventId;

// plugin methods
void EXPORT_API SetGLIssuePluginEventPtr(GL_IssuePluginEventPtr ptr);
void EXPORT_API SetOnCompleteCallbackPtr(RequestCallbackPtr ptr);
void EXPORT_API SetOnDestructCallbackPtr(RequestCallbackPtr ptr);
void EXPORT_API MainThread_UpdateOnce();

// request queries
auto EXPORT_API Request_GetData(EventId event_id, void** buffer, size_t* length) -> bool;
auto EXPORT_API Request_Exists(EventId event_id) -> bool;
auto EXPORT_API Request_Done(EventId event_id) -> bool;
auto EXPORT_API Request_Error(EventId event_id) -> bool;
void EXPORT_API Request_WaitForCompletion(EventId event_id);
}
