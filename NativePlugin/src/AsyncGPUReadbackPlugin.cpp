#include "AsyncGPUReadbackPlugin.hpp"

#include <algorithm>
#include <cassert>
#include <condition_variable>
#include <cstring>

#include "TypeHelpers.hpp"

class BaseTask;
class SsboTask;
class FrameTask;

struct Request {
  EventId id;
  std::shared_ptr<BaseTask> task = nullptr;
};

/**
 * @brief Owned or borrowed buffer
 */
class Buffer {
 public:
  Buffer() noexcept = default;
  Buffer(void* dst, size_t length) noexcept : data_(dst), length_(length) {}

  [[nodiscard]] auto data() const noexcept -> void* { return data_; }
  [[nodiscard]] auto size() const noexcept -> size_t { return length_; }

  void set(void* dst, size_t length) noexcept {
    data_ = dst;
    length_ = length;
  }

  void set(std::unique_ptr<char[]> data, size_t length) noexcept {
    storage_ = std::move(data);
    set(storage_.get(), length);
  }

  auto allocate_if_null(size_t length) -> void* {
    if (data_ == nullptr) set(std::make_unique<char[]>(length), length);
    return data_;
  }

 private:
  void* data_ = nullptr;
  size_t length_ = 0;
  std::unique_ptr<char[]> storage_ = nullptr;
};

class BaseTask {
 public:
  BaseTask() noexcept = default;
  BaseTask(void* dst, size_t length) noexcept : result_(dst, length) {}
  BaseTask(BaseTask const&) noexcept = delete;
  BaseTask(BaseTask&&) noexcept = delete;
  auto operator=(BaseTask const&) noexcept = delete;
  auto operator=(BaseTask&&) noexcept = delete;
  virtual ~BaseTask() noexcept = default;

  virtual void update() = 0;

  [[nodiscard]] auto is_initialized() const noexcept -> bool { return initialized_; }
  [[nodiscard]] auto is_done() const noexcept -> bool { return done_; }
  [[nodiscard]] auto has_error() const noexcept -> bool { return error_; }

  auto get_data(size_t& length) -> void* {
    if (!done_ || error_) { return nullptr; }
    std::scoped_lock guard(mutex_);
    length = result_.size();
    return result_.data();
  }

  void start_request() {
    on_start_request();
    initialized_ = true;
  }

 protected:
  virtual void on_start_request() = 0;

  void set_data_and_done(void* data, size_t length) {
    {
      std::scoped_lock guard(mutex_);
      void* dst = result_.allocate_if_null(length);
      std::memcpy(dst, data, std::min(result_.size(), length));
    }
    done_ = true;
  }

  /*
  Called by subclass to mark as error.
  */
  void set_error_and_done() {
    error_ = true;
    done_ = true;
  }

 private:
  Buffer result_;
  std::mutex mutex_;

  std::atomic<bool> initialized_ = false;
  std::atomic<bool> error_ = false;
  std::atomic<bool> done_ = false;
};

/*Task for readback from ssbo. Which is compute buffer in Unity
 */
class SsboTask : public BaseTask {
 public:
  using BaseTask::BaseTask;

  void init(GLuint _ssbo, GLint _bufferSize) {
    this->ssbo_ = _ssbo;
    this->buffer_size_ = _bufferSize;
  }

  void update() override {
    // Check fence state
    GLint status = 0;
    GLsizei length = 0;
    glGetSynciv(fence_, GL_SYNC_STATUS, sizeof(GLint), &length, &status);
    if (length <= 0) {
      set_error_and_done();
      clean_up();
      return;
    }

    // When it's done
    if (status == GL_SIGNALED) {
      // Bind back the pbo
      glBindBuffer(GL_PIXEL_PACK_BUFFER, pbo_);

      // Map the buffer and copy it to data
      void* ptr = glMapBufferRange(GL_PIXEL_PACK_BUFFER, 0, buffer_size_, GL_MAP_READ_BIT);

      // Allocate the final data buffer !!! WARNING: free, will have to be done on script side !!!!
      set_data_and_done(ptr, buffer_size_);

      // Unmap and unbind
      glUnmapBuffer(GL_PIXEL_PACK_BUFFER);
      glBindBuffer(GL_PIXEL_PACK_BUFFER, 0);
      clean_up();
    }
  }

 protected:
  void on_start_request() override {
    // bind it to GL_COPY_WRITE_BUFFER to wait for use
    glBindBuffer(GL_SHADER_STORAGE_BUFFER, this->ssbo_);

    // Get our pbo ready.
    glGenBuffers(1, &pbo_);
    glBindBuffer(GL_PIXEL_PACK_BUFFER, this->pbo_);
    // Initialize pbo buffer storage.
    glBufferData(GL_PIXEL_PACK_BUFFER, buffer_size_, nullptr, GL_STREAM_READ);

    // Copy data to pbo.
    glCopyBufferSubData(GL_SHADER_STORAGE_BUFFER, GL_PIXEL_PACK_BUFFER, 0, 0, buffer_size_);

    // Unbind buffers.
    glBindBuffer(GL_PIXEL_PACK_BUFFER, 0);
    glBindBuffer(GL_SHADER_STORAGE_BUFFER, 0);

    // Create a fence.
    fence_ = glFenceSync(GL_SYNC_GPU_COMMANDS_COMPLETE, 0);
  }

  void clean_up() {
    if (pbo_ != 0) {
      // Clear buffers
      glDeleteBuffers(1, &(pbo_));
    }
    if (fence_ != nullptr) { glDeleteSync(fence_); }
  }

 private:
  GLuint ssbo_ = 0;
  GLuint pbo_ = 0;
  GLsync fence_ = nullptr;
  GLint buffer_size_ = 0;
};

/*Task for readback texture.
 */
class FrameTask : public BaseTask {
 public:
  using BaseTask::BaseTask;

  void init(GLuint texture, int miplevel) {
    texture_ = texture;
    miplevel_ = miplevel;
  }

  void update() override {
    // Check fence state
    GLint status = 0;
    GLsizei length = 0;
    glGetSynciv(fence_, GL_SYNC_STATUS, sizeof(GLint), &length, &status);
    if (length <= 0) {
      set_error_and_done();
      clean_up();
      return;
    }

    // When it's done
    if (status == GL_SIGNALED) {
      // Bind back the pbo
      glBindBuffer(GL_PIXEL_PACK_BUFFER, pbo_);

      // Map the buffer and copy it to data
      void* ptr = glMapBufferRange(GL_PIXEL_PACK_BUFFER, 0, size_, GL_MAP_READ_BIT);
      set_data_and_done(ptr, size_);

      // Unmap and unbind
      glUnmapBuffer(GL_PIXEL_PACK_BUFFER);
      glBindBuffer(GL_PIXEL_PACK_BUFFER, 0);
      clean_up();
    }
  }

 protected:
  void on_start_request() override {
    // Get texture information
    glBindTexture(GL_TEXTURE_2D, texture_);
    glGetTexLevelParameteriv(GL_TEXTURE_2D, miplevel_, GL_TEXTURE_WIDTH, &(width_));
    glGetTexLevelParameteriv(GL_TEXTURE_2D, miplevel_, GL_TEXTURE_HEIGHT, &(height_));
    glGetTexLevelParameteriv(GL_TEXTURE_2D, miplevel_, GL_TEXTURE_DEPTH, &(depth_));
    glGetTexLevelParameteriv(GL_TEXTURE_2D, miplevel_, GL_TEXTURE_INTERNAL_FORMAT, &(internal_format_));
    int pixelBits = getPixelSizeFromInternalFormat(internal_format_);
    size_ = depth_ * width_ * height_ * pixelBits / 8;
    // Check for errors
    if (size_ == 0 || pixelBits % 8 != 0  // Only support textures aligned to one byte.
        || getFormatFromInternalFormat(internal_format_) == 0 || getTypeFromInternalFormat(internal_format_) == 0) {
      set_error_and_done();
      return;
    }

    // Create the fbo (frame buffer object) from the given texture
    glGenFramebuffers(1, &(fbo_));

    // Bind the texture to the fbo
    glBindFramebuffer(GL_FRAMEBUFFER, fbo_);
    glFramebufferTexture(GL_FRAMEBUFFER, GL_COLOR_ATTACHMENT0, texture_, 0);

    // Create and bind pbo (pixel buffer object) to fbo
    glGenBuffers(1, &(pbo_));
    glBindBuffer(GL_PIXEL_PACK_BUFFER, pbo_);
    glBufferData(GL_PIXEL_PACK_BUFFER, size_, nullptr, GL_DYNAMIC_READ);

    // Start the read request
    glReadBuffer(GL_COLOR_ATTACHMENT0);
    glReadPixels(0, 0, width_, height_, getFormatFromInternalFormat(internal_format_),
                 getTypeFromInternalFormat(internal_format_), nullptr);

    // Unbind buffers
    glBindBuffer(GL_PIXEL_PACK_BUFFER, 0);
    glBindFramebuffer(GL_FRAMEBUFFER, 0);

    // Fence to know when it's ready
    fence_ = glFenceSync(GL_SYNC_GPU_COMMANDS_COMPLETE, 0);
  }

  void clean_up() {
    // Clear buffers
    if (fbo_ != 0) glDeleteFramebuffers(1, &(fbo_));
    if (pbo_ != 0) glDeleteBuffers(1, &(pbo_));
    if (fence_ != nullptr) glDeleteSync(fence_);
  }

 private:
  int size_ = 0;
  GLsync fence_ = nullptr;
  GLuint texture_ = 0;
  GLuint fbo_ = 0;
  GLuint pbo_ = 0;
  int miplevel_ = 0;
  int height_ = 0;
  int width_ = 0;
  int depth_ = 0;
  GLint internal_format_ = 0;
};

auto Plugin::instance() noexcept -> Plugin& {
  static Plugin plugin;
  return plugin;
}

auto Plugin::request_texture(GLuint texture, int miplevel) -> EventId {
  std::shared_ptr<FrameTask> task = std::make_shared<FrameTask>();
  task->init(texture, miplevel);
  return insert(std::move(task));
}

auto Plugin::request_texture(void* buffer, size_t size, GLuint texture, int miplevel) -> EventId {
  std::shared_ptr<FrameTask> task = std::make_shared<FrameTask>(buffer, size);
  task->init(texture, miplevel);
  return insert(std::move(task));
}

auto Plugin::request_compute_buffer(GLuint compute_buffer, GLint buffer_size) -> EventId {
  std::shared_ptr<SsboTask> task = std::make_shared<SsboTask>();
  task->init(compute_buffer, buffer_size);
  return insert(std::move(task));
}

auto Plugin::request_compute_buffer(void* buffer, size_t size, GLuint compute_buffer, GLint buffer_size) -> EventId {
  std::shared_ptr<SsboTask> task = std::make_shared<SsboTask>(buffer, size);
  task->init(compute_buffer, buffer_size);
  return insert(std::move(task));
}

void Plugin::update_once() {
  std::scoped_lock guard(mutex_);

  // Remove tasks that are done in the last update.
  if (!pending_release_.empty()) {
    std::erase_if(requests_, [this](Request const& request) {
      auto iter = std::lower_bound(pending_release_.cbegin(), pending_release_.cend(), request.id);
      bool erased = iter != pending_release_.cend() && *iter == request.id;
      if (erased && on_destruct_ != nullptr) on_destruct_(request.id);
      return erased;
    });
    pending_release_.clear();
  }

  // Push new done tasks to pending list.
  for (auto& request : requests_) {
    if (request.task->is_done()) { pending_release_.push_back(request.id); }
  }

  assert(issue_plugin_event_ != nullptr);
  issue_plugin_event_([](EventId /* event_id */) { instance().update_render_thread_once(); }, 0);
}

auto Plugin::get_data(EventId event_id, void*& buffer, size_t& length) -> bool {
  std::scoped_lock guard(mutex_);
  auto iter = find(event_id);

  // Do something only if initialized (thread safety)
  if (iter == requests_.cend() || !iter->task->is_done() || iter->task->has_error()) [[unlikely]] { return false; }

  // Return the pointer.
  // The memory ownership doesn't transfer.
  buffer = iter->task->get_data(length);

  return true;
}

auto Plugin::exists(EventId event_id) const -> bool {
  std::scoped_lock guard(mutex_);
  return find(event_id) != requests_.cend();
}

auto Plugin::is_done(EventId event_id) const -> bool {
  std::scoped_lock guard(mutex_);
  auto ite = find(event_id);
  if (ite != requests_.cend()) [[likely]]
    return ite->task->is_done();
  return true;  // If it's disposed, also assume it's done.
}

auto Plugin::has_error(EventId event_id) const -> bool {
  std::scoped_lock guard(mutex_);
  auto ite = find(event_id);
  if (ite != requests_.end()) [[likely]]
    return ite->task->has_error();

  return true;  // It's disposed, assume as error.
}

void Plugin::wait_for_completion(EventId event_id) const {
  std::shared_ptr<BaseTask> task;

  // get the task first
  {
    std::scoped_lock guard(mutex_);
    auto iter = find(event_id);
    if (iter != requests_.cend() || iter->task->is_done()) return;
    task = iter->task;
  }

  // using example from https://en.cppreference.com/w/cpp/thread/condition_variable
  static std::mutex mutex;
  static std::condition_variable cv;
  static bool event_complete = false;

  assert(issue_plugin_event_ != nullptr);
  while (!task->is_done()) {
    std::unique_lock lock(mutex);
    event_complete = false;

    // can only update tasks from render thread
    // don't update main thread since this blocks it, the completed tasks will then not be destroyed until the next
    // frame
    issue_plugin_event_(
        [](EventId /* event_id */) {
          instance().update_render_thread_once();

          std::lock_guard lock(mutex);
          event_complete = true;
          cv.notify_one();
        },
        0);

    cv.wait(lock, [] { return event_complete; });
  }
}

void Plugin::update_render_thread_once() {
  std::scoped_lock guard(mutex_);
  for (auto const& request : requests_) {
    auto const& task = request.task;
    if (task != nullptr && task->is_initialized() && !task->is_done()) {
      task->update();
      if (on_complete_ != nullptr && task->is_done()) on_complete_(request.id);
    }
  }
}

auto Plugin::insert_pos(EventId event_id) const -> Plugin::request_iterator {
  return std::lower_bound(requests_.cbegin(), requests_.cend(), event_id,
                          [](Request const& task, EventId id) noexcept { return task.id < id; });
}

auto Plugin::find(EventId event_id) const -> Plugin::request_iterator {
  auto pos = insert_pos(event_id);
  if (pos != requests_.cend() && pos->id == event_id) [[likely]]
    return pos;
  return requests_.cend();
}

auto Plugin::insert(std::shared_ptr<BaseTask> task) -> EventId {
  EventId event_id = next_event_id_++;

  {
    std::scoped_lock guard(mutex_);

    if (requests_.empty() || requests_.back().id < event_id) [[likely]] {
      requests_.emplace_back(Request{
          .id = event_id,
          .task = std::move(task),
      });
    } else {
      // in the unlikely event of overflow
      requests_.insert(insert_pos(event_id), Request{
                                                 .id = event_id,
                                                 .task = std::move(task),
                                             });
    }
  }

  assert(issue_plugin_event_ != nullptr);
  issue_plugin_event_(
      [](EventId id) {
        Plugin& plugin = instance();

        std::shared_ptr<BaseTask> task;
        {
          std::scoped_lock guard(plugin.mutex_);
          task = plugin.find(id)->task;  // always valid id
        }
        task->start_request();
      },
      event_id);

  return event_id;
}
