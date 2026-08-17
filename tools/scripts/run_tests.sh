#!/bin/bash
set -e

PRESET=${1:-demo}

echo "Running tests for preset $PRESET..."

BUILD_DIR="build-$PRESET"

if [ ! -d "$BUILD_DIR" ]; then
    echo "Error: Build directory $BUILD_DIR does not exist. Please build first."
    exit 1
fi

# CTest is run from the build directory
ctest --test-dir "$BUILD_DIR" --output-on-failure -C Debug

echo "All tests passed successfully!"
