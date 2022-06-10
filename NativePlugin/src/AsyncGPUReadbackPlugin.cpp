#include <atomic>
#include <cstddef>
#include <cstring>
#include <map>
#include <memory>
#include <mutex>
#include <vector>

#include "TypeHelpers.hpp"
#include "Unity/IUnityGraphics.h"
#include "Unity/IUnityInterface.h"

#ifdef DEBUG
#  include <fstream>
#  include <thread>
#endif

struct BaseTask;
struct SsboTask;
struct FrameTask;

static IUnityGraphics* graphics = nullptr;
static UnityGfxRenderer renderer = kUnityGfxRendererNull;

static std::map<int, std::shared_ptr<BaseTask>> tasks;  // NOLINT(cert-err58-cpp)
static std::vector<int> pending_release_tasks;
static std::mutex tasks_mutex;
int next_event_id = 1;

extern "C" auto UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API CheckCompatible() -> bool;
static void UNITY_INTERFACE_API OnGraphicsDeviceEvent(UnityGfxDeviceEventType eventType);

// Call this on a function parameter to suppress the unused parameter warning
template <class T>
inline void unused(T const& result) {
  static_cast<void>(result);
}

struct BaseTask {
  // These vars might be accessed from both render thread and main thread. guard them.
  std::atomic<bool> initialized = false;
  std::atomic<bool> error = false;
  std::atomic<bool> done = false;
  /*Called in render thread*/
  virtual void StartRequest() = 0;
  virtual void Update() = 0;

  BaseTask() noexcept = default;
  BaseTask(BaseTask const&) noexcept = delete;
  BaseTask(BaseTask&&) noexcept = delete;
  auto operator=(BaseTask const&) noexcept = delete;
  auto operator=(BaseTask&&) noexcept = delete;
  virtual ~BaseTask() noexcept = default;

  auto GetData(size_t* length) -> char* {
    if (!done || error) { return nullptr; }
    std::lock_guard<std::mutex> guard(mainthread_data_mutex);
    if (this->result_data == nullptr) { return nullptr; }
    *length = result_data_length;
    return result_data.get();
  }

 protected:
  /*
   * Called by subclass in Update, to commit data and mark as done.
   */
  void FinishAndCommitData(std::unique_ptr<char[]> dataPtr, size_t length) {
    std::lock_guard<std::mutex> guard(mainthread_data_mutex);
    if (this->result_data != nullptr) {
      // WTF
      return;
    }
    this->result_data = std::move(dataPtr);
    this->result_data_length = length;
    done = true;
  }

  /*
  Called by subclass to mark as error.
  */
  void ErrorOut() {
    error = true;
    done = true;
  }

 private:
  std::mutex mainthread_data_mutex;
  std::unique_ptr<char[]> result_data = nullptr;
  size_t result_data_length = 0;
};

/*Task for readback from ssbo. Which is compute buffer in Unity
 */
struct SsboTask : public BaseTask {
  GLuint ssbo = 0;
  GLuint pbo = 0;
  GLsync fence = nullptr;
  GLint bufferSize = 0;

  void Init(GLuint _ssbo, GLint _bufferSize) {
    this->ssbo = _ssbo;
    this->bufferSize = _bufferSize;
  }

  void StartRequest() override {
    // bind it to GL_COPY_WRITE_BUFFER to wait for use
    glBindBuffer(GL_SHADER_STORAGE_BUFFER, this->ssbo);

    // Get our pbo ready.
    glGenBuffers(1, &pbo);
    glBindBuffer(GL_PIXEL_PACK_BUFFER, this->pbo);
    // Initialize pbo buffer storage.
    glBufferData(GL_PIXEL_PACK_BUFFER, bufferSize, nullptr, GL_STREAM_READ);

    // Copy data to pbo.
    glCopyBufferSubData(GL_SHADER_STORAGE_BUFFER, GL_PIXEL_PACK_BUFFER, 0, 0, bufferSize);

    // Unbind buffers.
    glBindBuffer(GL_PIXEL_PACK_BUFFER, 0);
    glBindBuffer(GL_SHADER_STORAGE_BUFFER, 0);

    // Create a fence.
    fence = glFenceSync(GL_SYNC_GPU_COMMANDS_COMPLETE, 0);
  }

  void Update() override {
    // Check fence state
    GLint status = 0;
    GLsizei length = 0;
    glGetSynciv(fence, GL_SYNC_STATUS, sizeof(GLint), &length, &status);
    if (length <= 0) {
      ErrorOut();
      Cleanup();
      return;
    }

    // When it's done
    if (status == GL_SIGNALED) {
      // Bind back the pbo
      glBindBuffer(GL_PIXEL_PACK_BUFFER, pbo);

      // Map the buffer and copy it to data
      void* ptr = glMapBufferRange(GL_PIXEL_PACK_BUFFER, 0, bufferSize, GL_MAP_READ_BIT);

      // Allocate the final data buffer !!! WARNING: free, will have to be done on script side !!!!
      std::unique_ptr<char[]> data = std::make_unique<char[]>(bufferSize);
      std::memcpy(data.get(), ptr, bufferSize);
      FinishAndCommitData(std::move(data), bufferSize);

      // Unmap and unbind
      glUnmapBuffer(GL_PIXEL_PACK_BUFFER);
      glBindBuffer(GL_PIXEL_PACK_BUFFER, 0);
      Cleanup();
    }
  }

  void Cleanup() {
    if (pbo != 0) {
      // Clear buffers
      glDeleteBuffers(1, &(pbo));
    }
    if (fence != nullptr) { glDeleteSync(fence); }
  }
};

/*Task for readback texture.
 */
struct FrameTask : public BaseTask {
  int size = 0;
  GLsync fence = nullptr;
  GLuint texture = 0;
  GLuint fbo = 0;
  GLuint pbo = 0;
  int miplevel = 0;
  int height = 0;
  int width = 0;
  int depth = 0;
  GLint internal_format = 0;

  void StartRequest() override {
    // Get texture information
    glBindTexture(GL_TEXTURE_2D, texture);
    glGetTexLevelParameteriv(GL_TEXTURE_2D, miplevel, GL_TEXTURE_WIDTH, &(width));
    glGetTexLevelParameteriv(GL_TEXTURE_2D, miplevel, GL_TEXTURE_HEIGHT, &(height));
    glGetTexLevelParameteriv(GL_TEXTURE_2D, miplevel, GL_TEXTURE_DEPTH, &(depth));
    glGetTexLevelParameteriv(GL_TEXTURE_2D, miplevel, GL_TEXTURE_INTERNAL_FORMAT, &(internal_format));
    int pixelBits = getPixelSizeFromInternalFormat(internal_format);
    size = depth * width * height * pixelBits / 8;
    // Check for errors
    if (size == 0 || pixelBits % 8 != 0  // Only support textures aligned to one byte.
        || getFormatFromInternalFormat(internal_format) == 0 || getTypeFromInternalFormat(internal_format) == 0) {
      ErrorOut();
      return;
    }

    // Create the fbo (frame buffer object) from the given texture
    glGenFramebuffers(1, &(fbo));

    // Bind the texture to the fbo
    glBindFramebuffer(GL_FRAMEBUFFER, fbo);
    glFramebufferTexture(GL_FRAMEBUFFER, GL_COLOR_ATTACHMENT0, texture, 0);

    // Create and bind pbo (pixel buffer object) to fbo
    glGenBuffers(1, &(pbo));
    glBindBuffer(GL_PIXEL_PACK_BUFFER, pbo);
    glBufferData(GL_PIXEL_PACK_BUFFER, size, nullptr, GL_DYNAMIC_READ);

    // Start the read request
    glReadBuffer(GL_COLOR_ATTACHMENT0);
    glReadPixels(0, 0, width, height, getFormatFromInternalFormat(internal_format),
                 getTypeFromInternalFormat(internal_format), nullptr);

    // Unbind buffers
    glBindBuffer(GL_PIXEL_PACK_BUFFER, 0);
    glBindFramebuffer(GL_FRAMEBUFFER, 0);

    // Fence to know when it's ready
    fence = glFenceSync(GL_SYNC_GPU_COMMANDS_COMPLETE, 0);
  }

  void Update() override {
    // Check fence state
    GLint status = 0;
    GLsizei length = 0;
    glGetSynciv(fence, GL_SYNC_STATUS, sizeof(GLint), &length, &status);
    if (length <= 0) {
      ErrorOut();
      Cleanup();
      return;
    }

    // When it's done
    if (status == GL_SIGNALED) {
      // Bind back the pbo
      glBindBuffer(GL_PIXEL_PACK_BUFFER, pbo);

      // Map the buffer and copy it to data

      std::unique_ptr<char[]> data = std::make_unique<char[]>(size);
      void* ptr = glMapBufferRange(GL_PIXEL_PACK_BUFFER, 0, size, GL_MAP_READ_BIT);
      std::memcpy(data.get(), ptr, size);
      FinishAndCommitData(std::move(data), size);

      // Unmap and unbind
      glUnmapBuffer(GL_PIXEL_PACK_BUFFER);
      glBindBuffer(GL_PIXEL_PACK_BUFFER, 0);
      Cleanup();
    }
  }

  void Cleanup() {
    // Clear buffers
    if (fbo != 0) glDeleteFramebuffers(1, &(fbo));
    if (pbo != 0) glDeleteBuffers(1, &(pbo));
    if (fence != nullptr) glDeleteSync(fence);
  }
};

/**
 * Unity plugin load event
 */
extern "C" void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API UnityPluginLoad(IUnityInterfaces* unityInterfaces) {
  graphics = unityInterfaces->Get<IUnityGraphics>();
  graphics->RegisterDeviceEventCallback(OnGraphicsDeviceEvent);

  // Run OnGraphicsDeviceEvent(initialize) manually on plugin load
  // to not miss the event in case the graphics device is already initialized
  OnGraphicsDeviceEvent(kUnityGfxDeviceEventInitialize);

  if (CheckCompatible()) { glewInit(); }
}

/**
 * Unity unload plugin event
 */
extern "C" void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API UnityPluginUnload() {
  graphics->UnregisterDeviceEventCallback(OnGraphicsDeviceEvent);
}

/**
 * Called for every graphics device events
 */
static void UNITY_INTERFACE_API OnGraphicsDeviceEvent(UnityGfxDeviceEventType eventType) {
  // Create graphics API implementation upon initialization
  if (eventType == kUnityGfxDeviceEventInitialize) { renderer = graphics->GetRenderer(); }

  // Cleanup graphics API implementation upon shutdown
  if (eventType == kUnityGfxDeviceEventShutdown) { renderer = kUnityGfxRendererNull; }
}

/**
 * Check if plugin is compatible with this system
 * This plugin is only compatible with opengl core
 */
extern "C" auto UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API CheckCompatible() -> bool {
  return (renderer == kUnityGfxRendererOpenGLCore);
}

auto InsertEvent(std::shared_ptr<BaseTask> task) -> int {
  int event_id = next_event_id;
  next_event_id++;

  std::lock_guard<std::mutex> guard(tasks_mutex);
  tasks[event_id] = std::move(task);

  return event_id;
}

/**
 * @brief Init of the make request action.
 * You then have to call makeRequest_renderThread
 * via GL.IssuePluginEvent with the returned event_id
 *
 * @param texture OpenGL texture id
 * @return event_id to give to other functions and to IssuePluginEvent
 */
extern "C" auto UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API RequestTextureMainThread(GLuint texture, int miplevel)
    -> int {
  // Create the task
  std::shared_ptr<FrameTask> task = std::make_shared<FrameTask>();
  task->texture = texture;
  task->miplevel = miplevel;
  return InsertEvent(task);
}

extern "C" auto UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API RequestComputeBufferMainThread(GLuint computeBuffer,
                                                                                          GLint bufferSize) -> int {
  // Create the task
  std::shared_ptr<SsboTask> task = std::make_shared<SsboTask>();
  task->Init(computeBuffer, bufferSize);
  return InsertEvent(task);
}

/**
 * @brief Create a a read texture request
 * Has to be called by GL.IssuePluginEvent
 * @param event_id containing the the task index, given by makeRequest_mainThread
 */
extern "C" void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API KickstartRequestInRenderThread(int event_id) {
  // Get task back
  std::lock_guard<std::mutex> guard(tasks_mutex);
  std::shared_ptr<BaseTask> task = tasks[event_id];
  task->StartRequest();
  // Done init
  task->initialized = true;
}

extern "C" auto UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API GetKickstartFunctionPtr() -> UnityRenderingEvent {
  return KickstartRequestInRenderThread;
}

/**
 * Update all current available tasks. Should be called in render thread.
 */
extern "C" void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API UpdateRenderThread(int event_id) {
  unused(event_id);
  // Lock up.
  std::lock_guard<std::mutex> guard(tasks_mutex);
  for (auto ite = tasks.begin(); ite != tasks.end(); ite++) {
    auto task = ite->second;
    if (task != nullptr && task->initialized && !task->done) task->Update();
  }
}

extern "C" auto UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API GetUpdateRenderThreadFunctionPtr() -> UnityRenderingEvent {
  return UpdateRenderThread;
}

/**
 * Update in main thread.
 * This will erase tasks that are marked as done in last frame.
 * Also save tasks that are done this frame.
 * By doing this, all tasks are done for one frame, then removed.
 */
extern "C" void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API UpdateMainThread() {
  // Lock up.
  std::lock_guard<std::mutex> guard(tasks_mutex);

  // Remove tasks that are done in the last update.
  for (auto& event_id : pending_release_tasks) {
    auto t = tasks.find(event_id);
    if (t != tasks.end()) { tasks.erase(t); }
  }
  pending_release_tasks.clear();

  // Push new done tasks to pending list.
  for (auto ite = tasks.begin(); ite != tasks.end(); ite++) {
    auto task = ite->second;
    if (task->done) { pending_release_tasks.push_back(ite->first); }
  }
}

/**
 * @brief Get data from the main thread.
 * The data owner is still native plugin, outside caller should copy the data asap to avoid any problem.
 *
 */
extern "C" void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API GetData(int event_id, void** buffer, size_t* length) {
  // Get task back
  std::lock_guard<std::mutex> guard(tasks_mutex);
  std::shared_ptr<BaseTask> task = tasks[event_id];

  // Do something only if initialized (thread safety)
  if (!task->done) { return; }

  // Return the pointer.
  // The memory ownership doesn't transfer.
  auto dataPtr = task->GetData(length);
  *buffer = dataPtr;
}

/**
 * @brief Check if request exists
 * @param event_id containing the the task index, given by makeRequest_mainThread
 */
extern "C" auto UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API TaskExists(int event_id) -> bool {
  // Get task back
  std::lock_guard<std::mutex> guard(tasks_mutex);
  bool result = tasks.find(event_id) != tasks.end();

  return result;
}

/**
 * @brief Check if request is done
 * @param event_id containing the the task index, given by makeRequest_mainThread
 */
extern "C" auto UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API TaskDone(int event_id) -> bool {
  // Get task back
  std::lock_guard<std::mutex> guard(tasks_mutex);
  auto ite = tasks.find(event_id);
  if (ite != tasks.end()) return ite->second->done;
  return true;  // If it's disposed, also assume it's done.
}

/**
 * @brief Check if request is in error
 * @param event_id containing the the task index, given by makeRequest_mainThread
 */
extern "C" auto UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API TaskError(int event_id) -> bool {
  // Get task back
  std::lock_guard<std::mutex> guard(tasks_mutex);
  auto ite = tasks.find(event_id);
  if (ite != tasks.end()) return ite->second->error;

  return true;  // It's disposed, assume as error.
}
