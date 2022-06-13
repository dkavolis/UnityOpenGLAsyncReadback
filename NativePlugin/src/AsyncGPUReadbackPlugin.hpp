#pragma once

#include <atomic>
#include <memory>
#include <mutex>
#include <vector>

#include "AsyncGPUReadbackPluginAPI.hpp"

struct Request;
class BaseTask;

class Plugin {
 public:
  [[nodiscard]] static auto instance() noexcept -> Plugin&;

  /** @brief Request data readback from a texture. Data will be destroyed on the next call to update_once() after the
   * request is complete
   *
   * @param texture OpenGL texture id
   * @param miplevel
   * @return event_id request handle
   */
  [[nodiscard]] auto request_texture(GLuint texture, int miplevel) -> EventId;

  /**
   * @brief Request data readback from a texture into an existing array
   * @param buffer pointer to existing array to write data to
   * @param size size in bytes of buffer
   * @param texture OpenGL texture id
   * @param miplevel
   * @return event_id request handle
   */
  [[nodiscard]] auto request_texture(void* buffer, size_t size, GLuint texture, int miplevel) -> EventId;

  /**
   * @brief Request data readback from a compute buffer. Data will be destroyed on the next call to update_once() after
   * the request is complete
   * @param compute_buffer OpenGL compute buffer id
   * @param buffer_size compute buffer size in bytes
   * @return event_id request handle
   */
  [[nodiscard]] auto request_compute_buffer(GLuint compute_buffer, GLint buffer_size) -> EventId;

  /**
   * @brief Request data readback from a compute buffer into an existing array.
   * @param buffer pointer to existing array to write data to
   * @param size size in bytes of buffer
   * @param compute_buffer OpenGL compute buffer id
   * @param buffer_size compute buffer size in bytes
   * @return
   */
  [[nodiscard]] auto request_compute_buffer(void* buffer, size_t size, GLuint compute_buffer, GLint buffer_size)
      -> EventId;

  /**
   * @brief Set the pointer to GL.IssuePluginEvent as the interface does not export it, must be called prior to
   * submitting any requests or updates
   * @param ptr
   */
  void set_issue_plugin_event(GL_IssuePluginEventPtr ptr) noexcept { issue_plugin_event_ = ptr; }

  /**
   * @brief Set callback function when a request is completed, called from the render thread
   * @param ptr
   */
  void set_on_complete(RequestCallbackPtr ptr) noexcept { on_complete_ = ptr; }

  /**
   * @brief Set callback function when a request is disposed
   * @param ptr
   */
  void set_on_destruct(RequestCallbackPtr ptr) noexcept { on_destruct_ = ptr; }

  /** @brief Update in main thread.
   * This will erase tasks that are marked as done in last frame.
   * Also save tasks that are done this frame.
   * By doing this, all tasks are done for one frame, then removed.
   */
  void update_once();

  /**
   * @brief Get data from the request
   * @param event_id request id
   * @param buffer pointer to the retrieved data
   * @param length size in bytes of the retrieved data
   * @return true if data was returned
   * @return false otherwise, such as the request is still ongoing, had error
   */
  auto get_data(EventId event_id, void*& buffer, size_t& length) -> bool;

  /**
   * @brief Check if the request exists
   * @param event_id request id
   * @return true if request still exists, false otherwise
   */
  [[nodiscard]] auto exists(EventId event_id) const -> bool;

  /**
   * @brief Check if the request has completed
   * @param event_id request id
   * @return true if request still has completed, false otherwise
   */
  [[nodiscard]] auto is_done(EventId event_id) const -> bool;

  /**
   * @brief Check if the request had an error
   * @param event_id request id
   * @return true if request still had an error, false otherwise
   */
  [[nodiscard]] auto has_error(EventId event_id) const -> bool;

  /**
   * @brief Block and wait for the request to complete
   * @param event_id request id
   */
  void wait_for_completion(EventId event_id) const;

 private:
  Plugin() noexcept = default;

  mutable std::mutex mutex_;
  std::vector<Request> requests_;
  std::vector<EventId> pending_release_;
  std::atomic<EventId> next_event_id_ = 0;
  GL_IssuePluginEventPtr issue_plugin_event_ = nullptr;
  RequestCallbackPtr on_complete_ = nullptr;
  RequestCallbackPtr on_destruct_ = nullptr;

  void update_render_thread_once();

  using request_iterator = typename std::vector<Request>::const_iterator;

  [[nodiscard]] auto insert_pos(EventId event_id) const -> request_iterator;
  [[nodiscard]] auto find(EventId event_id) const -> request_iterator;
  auto insert(std::shared_ptr<BaseTask> task) -> EventId;
};
