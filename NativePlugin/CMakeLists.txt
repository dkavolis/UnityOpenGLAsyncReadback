cmake_minimum_required(VERSION 3.11)
project(OpenGLAsyncGPUReadbackPlugin LANGUAGES C CXX)

set(HEADERS
    src/TypeHelpers.hpp
    src/OpenGLAsyncGPUReadbackPlugin.hpp
    src/OpenGLAsyncGPUReadbackPluginAPI.hpp
    src/Unity/IUnityGraphics.h
    src/Unity/IUnityGraphicsD3D9.h
    src/Unity/IUnityGraphicsD3D11.h
    src/Unity/IUnityGraphicsD3D12.h
    src/Unity/IUnityGraphicsMetal.h
    src/Unity/IUnityGraphicsVulkan.h
    src/Unity/IUnityInterface.h)
set(SOURCES src/OpenGLAsyncGPUReadbackPlugin.cpp src/OpenGLAsyncGPUReadbackPluginAPI.cpp)

find_package(OpenGL REQUIRED)
include_directories(${OpenGL_INCLUDE_DIR})

# GLEW
set(GLEW_SOURCES glew/src/glew.c)
add_library(GLEW ${GLEW_SOURCES})
target_compile_definitions(GLEW PUBLIC -D GLEW_STATIC)
set_property(TARGET GLEW PROPERTY POSITION_INDEPENDENT_CODE ON)
target_include_directories(GLEW PUBLIC glew/include)

source_group("Source Files" FILES ${SOURCES} ${HEADERS})

add_library(${PROJECT_NAME} SHARED ${HEADERS} ${SOURCES})
target_link_libraries(${PROJECT_NAME} ${OPENGL_LIBRARY} GLEW)
set_target_properties(
  ${PROJECT_NAME} PROPERTIES LINKER_LANGUAGE CXX RUNTIME_OUTPUT_DIRECTORY ${CMAKE_CURRENT_SOURCE_DIR}/../bin
                             LIBRARY_OUTPUT_DIRECTORY ${CMAKE_CURRENT_SOURCE_DIR}/../bin)
target_compile_features(${PROJECT_NAME} PRIVATE cxx_std_20)

if(MSVC)
  target_compile_options(${PROJECT_NAME} PRIVATE /W4 /WX)
else()
  target_compile_options(${PROJECT_NAME} PRIVATE -Wall -Wextra -pedantic -Werror)
endif()

if(CMAKE_SYSTEM_NAME MATCHES "Darwin")
  # macOS
  set(dirname MacOS)
elseif(CMAKE_SYSTEM_NAME MATCHES "Linux")
  set(dirname Linux)
elseif(CMAKE_SYSTEM_NAME MATCHES "Windows")
  set(dirname Windows)
endif()

if(dirname)
  set(outdir ${CMAKE_CURRENT_SOURCE_DIR}/../UnityPlugin/Assets/UniversalAsyncGPUReadbackPlugin/Plugins/${dirname})
  message("Native plugin will also be copied to ${outdir}")
  add_custom_command(
    TARGET ${PROJECT_NAME}
    POST_BUILD
    COMMAND ${CMAKE_COMMAND} -E copy $<TARGET_FILE:${PROJECT_NAME}> ${outdir}/$<TARGET_FILE_NAME:${PROJECT_NAME}>
    COMMAND ${CMAKE_COMMAND} -E echo Copied $<TARGET_FILE:${PROJECT_NAME}> ${outdir}/$<TARGET_FILE_NAME:${PROJECT_NAME}>
    VERBATIM)

  if(MSVC AND CMAKE_BUILD_TYPE MATCHES "Debug|RelWithDebInfo")
    add_custom_command(
      TARGET ${PROJECT_NAME}
      POST_BUILD
      COMMAND ${CMAKE_COMMAND} -E copy $<TARGET_PDB_FILE:${PROJECT_NAME}>
              ${outdir}/$<TARGET_PDB_FILE_NAME:${PROJECT_NAME}>
      COMMAND ${CMAKE_COMMAND} -E echo Copied $<TARGET_PDB_FILE:${PROJECT_NAME}>
              ${outdir}/$<TARGET_PDB_FILE_NAME:${PROJECT_NAME}>
      VERBATIM)
  endif()
endif()
