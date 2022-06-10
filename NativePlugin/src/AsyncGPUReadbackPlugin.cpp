#include "AsyncGPUReadbackPlugin.hpp"

#include <algorithm>
#include <atomic>
#include <cstddef>
#include <cstring>
#include <map>
#include <memory>
#include <mutex>
#include <variant>
#include <vector>

#include "TypeHelpers.hpp"

struct BaseTask;
struct SsboTask;
struct FrameTask;

static IUnityGraphics* graphics = nullptr;
static UnityGfxRenderer renderer = kUnityGfxRendererNull;

struct TaskEntry {
  EventId id;
  std::shared_ptr<BaseTask> task = nullptr;
};

/**
 * @brief Owned or borrowed buffer
 */
class Buffer {
 public:
  Buffer() noexcept = default;
  Buffer(void* data, size_t length) noexcept : buffer_(data), length_(length) {}

  [[nodiscard]] auto data() const noexcept -> void* {
    if (auto* ptr = std::get_if<void*>(&buffer_); ptr != nullptr) { return *ptr; }
    return std::get<std::unique_ptr<char[]>>(buffer_).get();
  }

  [[nodiscard]] auto size() const noexcept -> size_t { return length_; }

  void set(void* data, size_t length) noexcept {
    buffer_ = data;
    length_ = length;
  }

  void set(std::unique_ptr<char[]> data, size_t length) noexcept {
    buffer_ = std::move(data);
    length_ = length;
  }

  auto allocate_if_null(size_t length) -> void* {
    void* ptr = data();
    if (ptr == nullptr) {
      std::unique_ptr<char[]> data = std::make_unique<char[]>(length);
      ptr = data.get();
      set(std::move(data), length);
    }

    return ptr;
  }

  [[nodiscard]] auto owned() const noexcept -> bool { return std::holds_alternative<std::unique_ptr<char[]>>(buffer_); }

 private:
  std::variant<void*, std::unique_ptr<char[]>> buffer_ = static_cast<void*>(nullptr);
  size_t length_ = 0;
};

static std::vector<TaskEntry> tasks;  // unlikely to be large so a lookup in sorted array may be faster than std::map
static std::vector<EventId> pending_release_tasks;
static std::mutex tasks_mutex;
EventId next_event_id = 1;

struct BaseTask {
  // These vars might be accessed from both render thread and main thread. guard them.
  std::atomic<bool> initialized = false;
  std::atomic<bool> error = false;
  std::atomic<bool> done = false;
  /*Called in render thread*/
  virtual void StartRequest() = 0;
  virtual void Update() = 0;

  BaseTask() noexcept = default;
  BaseTask(void* data, size_t length) noexcept : result_(data, length) {}
  BaseTask(BaseTask const&) noexcept = delete;
  BaseTask(BaseTask&&) noexcept = delete;
  auto operator=(BaseTask const&) noexcept = delete;
  auto operator=(BaseTask&&) noexcept = delete;
  virtual ~BaseTask() noexcept = default;

  auto GetData(size_t* length) -> void* {
    if (!done || error) { return nullptr; }
    std::scoped_lock guard(mainthread_data_mutex);
    *length = result_.size();
    return result_.data();
  }

  [[nodiscard]] auto BufferSize(size_t other) const noexcept -> size_t { return std::min(result_.size(), other); }

 protected:
  auto AllocateOrGet(size_t length) -> void* { return result_.allocate_if_null(length); }

  /*
  Called by subclass to mark as error.
  */
  void ErrorOut() {
    error = true;
    done = true;
  }

 private:
  std::mutex mainthread_data_mutex;
  Buffer result_;
};

/*Task for readback from ssbo. Which is compute buffer in Unity
 */
struct SsboTask : public BaseTask {
  GLuint ssbo = 0;
  GLuint pbo = 0;
  GLsync fence = nullptr;
  GLint bufferSize = 0;

  using BaseTask::BaseTask;

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
      void* data = AllocateOrGet(bufferSize);
      std::memcpy(data, ptr, BufferSize(bufferSize));
      this->done = true;

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

  using BaseTask::BaseTask;

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
      void* ptr = glMapBufferRange(GL_PIXEL_PACK_BUFFER, 0, size, GL_MAP_READ_BIT);
      void* data = AllocateOrGet(size);
      std::memcpy(data, ptr, BufferSize(size));
      this->done = true;

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
void UnityPluginLoad(IUnityInterfaces* unityInterfaces) {
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
void UnityPluginUnload() { graphics->UnregisterDeviceEventCallback(OnGraphicsDeviceEvent); }

/**
 * Called for every graphics device events
 */
void OnGraphicsDeviceEvent(UnityGfxDeviceEventType eventType) {
  // Create graphics API implementation upon initialization
  if (eventType == kUnityGfxDeviceEventInitialize) { renderer = graphics->GetRenderer(); }

  // Cleanup graphics API implementation upon shutdown
  if (eventType == kUnityGfxDeviceEventShutdown) { renderer = kUnityGfxRendererNull; }
}

/**
 * Check if plugin is compatible with this system
 * This plugin is only compatible with opengl core
 */
auto CheckCompatible() -> bool { return (renderer == kUnityGfxRendererOpenGLCore); }

[[nodiscard]] auto LowerBound(EventId event_id) noexcept {
  return std::lower_bound(tasks.cbegin(), tasks.cend(), event_id,
                          [](TaskEntry const& entry, EventId id) noexcept { return entry.id < id; });
}

[[nodiscard]] auto FindEvent(EventId event_id) noexcept {
  auto pos = LowerBound(event_id);
  if (pos != tasks.cend() && pos->id == event_id) [[likely]]
    return pos;
  return tasks.cend();
}

auto InsertEvent(std::shared_ptr<BaseTask> task) -> EventId {
  EventId event_id = next_event_id++;

  std::scoped_lock guard(tasks_mutex);

  if (tasks.back().id < event_id) [[likely]] {
    tasks.emplace_back(TaskEntry{
        .id = event_id,
        .task = std::move(task),
    });
  } else {
    // in the unlikely event of overflow
    tasks.insert(LowerBound(event_id), TaskEntry{
                                           .id = event_id,
                                           .task = std::move(task),
                                       });
  }

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
auto Request_Texture(GLuint texture, int miplevel) -> EventId {
  // Create the task
  std::shared_ptr<FrameTask> task = std::make_shared<FrameTask>();
  task->texture = texture;
  task->miplevel = miplevel;
  return InsertEvent(std::move(task));
}

auto Request_TextureIntoArray(void* data, size_t size, GLuint texture, int miplevel) -> EventId {
  std::shared_ptr<FrameTask> task = std::make_shared<FrameTask>(data, size);
  task->texture = texture;
  task->miplevel = miplevel;
  return InsertEvent(std::move(task));
}

auto Request_ComputeBuffer(GLuint computeBuffer, GLint bufferSize) -> EventId {
  // Create the task
  std::shared_ptr<SsboTask> task = std::make_shared<SsboTask>();
  task->Init(computeBuffer, bufferSize);
  return InsertEvent(std::move(task));
}

auto Request_ComputeBufferIntoArray(void* data, size_t size, GLuint computeBuffer, GLint bufferSize) -> EventId {
  // Create the task
  std::shared_ptr<SsboTask> task = std::make_shared<SsboTask>(data, size);
  task->Init(computeBuffer, bufferSize);
  return InsertEvent(std::move(task));
}

/**
 * @brief Create a a read texture request
 * Has to be called by GL.IssuePluginEvent
 * @param event_id containing the the task index, given by makeRequest_mainThread
 */
void KickstartRequestInRenderThread(EventId event_id) {
  // Get task back
  std::scoped_lock guard(tasks_mutex);
  std::shared_ptr<BaseTask> task = FindEvent(event_id)->task;  // always called with a valid id
  task->StartRequest();
  // Done init
  task->initialized = true;
}

auto GetKickstartFunctionPtr() -> UnityRenderingEvent { return KickstartRequestInRenderThread; }

/**
 * Update all current available tasks. Should be called in render thread.
 */
void UpdateRenderThread(EventId /* event_id */) {
  // Lock up.
  std::scoped_lock guard(tasks_mutex);
  for (auto& entry : tasks) {
    auto const& task = entry.task;
    if (task != nullptr && task->initialized && !task->done) task->Update();
  }
}

auto GetUpdateRenderThreadFunctionPtr() -> UnityRenderingEvent { return UpdateRenderThread; }

/**
 * Update in main thread.
 * This will erase tasks that are marked as done in last frame.
 * Also save tasks that are done this frame.
 * By doing this, all tasks are done for one frame, then removed.
 */
void UpdateMainThread() {
  // Lock up.
  std::scoped_lock guard(tasks_mutex);

  // Remove tasks that are done in the last update.
  std::erase_if(tasks, [](TaskEntry const& entry) noexcept {
    return std::find(pending_release_tasks.cbegin(), pending_release_tasks.cend(), entry.id) !=
           pending_release_tasks.cend();
  });
  pending_release_tasks.clear();

  // Push new done tasks to pending list.
  for (auto& entry : tasks) {
    if (entry.task->done) { pending_release_tasks.push_back(entry.id); }
  }
}

/**
 * @brief Get data from the main thread.
 * The data owner is still native plugin, outside caller should copy the data asap to avoid any problem.
 *
 * @return true if data received
 * @return false otherwise
 */
auto Task_GetData(EventId event_id, void** buffer, size_t* length) -> bool {
  // Get task back
  std::scoped_lock guard(tasks_mutex);
  auto iter = FindEvent(event_id);

  // Do something only if initialized (thread safety)
  if (iter == tasks.cend() || !iter->task->done) [[unlikely]] { return false; }

  // Return the pointer.
  // The memory ownership doesn't transfer.
  auto dataPtr = iter->task->GetData(length);
  *buffer = dataPtr;

  return true;
}

/**
 * @brief Check if request exists
 * @param event_id containing the the task index, given by makeRequest_mainThread
 */
auto Task_Exists(EventId event_id) -> bool {
  // Get task back
  std::scoped_lock guard(tasks_mutex);
  return FindEvent(event_id) != tasks.cend();
}

/**
 * @brief Check if request is done
 * @param event_id containing the the task index, given by makeRequest_mainThread
 */
auto Task_Done(EventId event_id) -> bool {
  // Get task back
  std::scoped_lock guard(tasks_mutex);
  auto ite = FindEvent(event_id);
  if (ite != tasks.cend()) [[likely]]
    return ite->task->done;
  return true;  // If it's disposed, also assume it's done.
}

/**
 * @brief Check if request is in error
 * @param event_id containing the the task index, given by makeRequest_mainThread
 */
auto Task_Error(EventId event_id) -> bool {
  // Get task back
  std::scoped_lock guard(tasks_mutex);
  auto ite = FindEvent(event_id);
  if (ite != tasks.end()) [[likely]]
    return ite->task->error;

  return true;  // It's disposed, assume as error.
}

void Task_WaitForCompletion(EventId event_id) {
  std::shared_ptr<BaseTask> task;

  // get the task first
  {
    std::scoped_lock guard(tasks_mutex);
    auto iter = FindEvent(event_id);
    if (iter != tasks.cend() || iter->task->done) return;
    task = iter->task;
  }

  while (!task->done) { UpdateRenderThread(0); }
}
