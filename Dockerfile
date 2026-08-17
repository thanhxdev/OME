FROM ubuntu:24.04

ENV DEBIAN_FRONTEND=noninteractive

# Install dependencies
RUN apt-get update && apt-get install -y \
    build-essential \
    cmake \
    ninja-build \
    pkg-config \
    git \
    ffmpeg \
    libavcodec-dev \
    libavformat-dev \
    libswscale-dev \
    libspdlog-dev \
    libfmt-dev \
    libgtest-dev \
    nlohmann-json3-dev \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app

# Copy source
COPY . .

# Build
RUN cmake --preset production && \
    cmake --build --preset production

CMD ["./build/bin/OpenMediaServer"]
