FROM ubuntu:24.04 AS build

ARG WHISPER_CPP_VERSION=v1.9.1

RUN apt-get update \
    && apt-get install -y --no-install-recommends build-essential ca-certificates cmake curl git \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /src
RUN git clone --branch ${WHISPER_CPP_VERSION} --depth 1 https://github.com/ggml-org/whisper.cpp.git . \
    && cmake -S . -B build \
        -DBUILD_SHARED_LIBS=OFF \
        -DWHISPER_BUILD_TESTS=OFF \
        -DWHISPER_BUILD_EXAMPLES=ON \
    && cmake --build build --config Release --target whisper-server -j 2 \
    && ./models/download-ggml-model.sh small

FROM ubuntu:24.04

RUN apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates curl ffmpeg libgomp1 \
    && rm -rf /var/lib/apt/lists/* \
    && useradd --create-home --uid 10001 whisper

COPY --from=build /src/build/bin/whisper-server /usr/local/bin/whisper-server
COPY --from=build /src/models/ggml-small.bin /models/ggml-small.bin

WORKDIR /tmp
USER whisper
EXPOSE 8080

ENTRYPOINT ["whisper-server"]
CMD ["--host", "0.0.0.0", "--port", "8080", "--model", "/models/ggml-small.bin", "--language", "auto", "--convert", "--no-gpu", "--threads", "4", "--processors", "1", "--no-timestamps"]
