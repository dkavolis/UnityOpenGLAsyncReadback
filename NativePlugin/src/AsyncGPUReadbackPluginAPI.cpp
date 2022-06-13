#include "AsyncGPUReadbackPluginAPI.hpp"

#include "AsyncGPUReadbackPlugin.hpp"

static IUnityGraphics* graphics = nullptr;
static UnityGfxRenderer renderer = kUnityGfxRendererNull;

/**
 * Called for every graphics device events
 */
void UNITY_INTERFACE_API OnGraphicsDeviceEvent(UnityGfxDeviceEventType eventType);

void UnityPluginLoad(IUnityInterfaces* unityInterfaces) {
  graphics = unityInterfaces->Get<IUnityGraphics>();
  graphics->RegisterDeviceEventCallback(OnGraphicsDeviceEvent);

  // Run OnGraphicsDeviceEvent(initialize) manually on plugin load
  // to not miss the event in case the graphics device is already initialized
  OnGraphicsDeviceEvent(kUnityGfxDeviceEventInitialize);

  if (CheckCompatible()) { glewInit(); }
}

void UnityPluginUnload() { graphics->UnregisterDeviceEventCallback(OnGraphicsDeviceEvent); }

void OnGraphicsDeviceEvent(UnityGfxDeviceEventType eventType) {
  // Create graphics API implementation upon initialization
  if (eventType == kUnityGfxDeviceEventInitialize) { renderer = graphics->GetRenderer(); }

  // Cleanup graphics API implementation upon shutdown
  if (eventType == kUnityGfxDeviceEventShutdown) { renderer = kUnityGfxRendererNull; }
}

auto CheckCompatible() -> bool { return (renderer == kUnityGfxRendererOpenGLCore); }

auto Request_Texture(GLuint texture, int miplevel) -> EventId {
  return Plugin::instance().request_texture(texture, miplevel);
}

auto Request_TextureIntoArray(void* data, size_t size, GLuint texture, int miplevel) -> EventId {
  return Plugin::instance().request_texture(data, size, texture, miplevel);
}

auto Request_ComputeBuffer(GLuint computeBuffer, GLint bufferSize) -> EventId {
  return Plugin::instance().request_compute_buffer(computeBuffer, bufferSize);
}

auto Request_ComputeBufferIntoArray(void* data, size_t size, GLuint computeBuffer, GLint bufferSize) -> EventId {
  return Plugin::instance().request_compute_buffer(data, size, computeBuffer, bufferSize);
}

void SetGLIssuePluginEventPtr(GL_IssuePluginEventPtr ptr) { Plugin::instance().set_issue_plugin_event(ptr); }

void SetOnCompleteCallbackPtr(RequestCallbackPtr ptr) { Plugin::instance().set_on_complete(ptr); }

void SetOnDestructCallbackPtr(RequestCallbackPtr ptr) { Plugin::instance().set_on_destruct(ptr); }

void MainThread_UpdateOnce() { Plugin::instance().update_once(); }

auto Request_GetData(EventId event_id, void** buffer, size_t* length) -> bool {
  if (buffer == nullptr || length == nullptr) return false;
  return Plugin::instance().get_data(event_id, *buffer, *length);
}

auto Request_Exists(EventId event_id) -> bool { return Plugin::instance().exists(event_id); }

auto Request_Done(EventId event_id) -> bool { return Plugin::instance().is_done(event_id); }

auto Request_Error(EventId event_id) -> bool { return Plugin::instance().has_error(event_id); }

void Request_WaitForCompletion(EventId event_id) { Plugin::instance().wait_for_completion(event_id); }
