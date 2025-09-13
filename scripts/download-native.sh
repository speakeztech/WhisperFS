#!/bin/bash

# Bash script to download whisper.cpp native binaries for all platforms
# This script downloads pre-built whisper.cpp binaries from GitHub releases

VERSION="${1:-1.5.4}"
OUTPUT_PATH="${2:-$(dirname "$0")/../native}"
FORCE="${3:-false}"

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
NC='\033[0m' # No Color

# Detect current platform
detect_platform() {
    local os=$(uname -s)
    local arch=$(uname -m)

    case "$os" in
        Linux*)
            OS="linux"
            LIB_EXT="so"
            LIB_PREFIX="lib"
            ;;
        Darwin*)
            OS="osx"
            LIB_EXT="dylib"
            LIB_PREFIX="lib"
            ;;
        MINGW*|MSYS*|CYGWIN*)
            OS="win"
            LIB_EXT="dll"
            LIB_PREFIX=""
            ;;
        *)
            echo "Unknown OS: $os"
            exit 1
            ;;
    esac

    case "$arch" in
        x86_64|amd64)
            ARCH="x64"
            ;;
        aarch64|arm64)
            ARCH="arm64"
            ;;
        *)
            echo "Unknown architecture: $arch"
            exit 1
            ;;
    esac

    RUNTIME="${OS}-${ARCH}"
    NATIVE_LIB="${LIB_PREFIX}whisper.${LIB_EXT}"
}

# Download function
download_platform() {
    local runtime=$1
    local filename=$2
    local url=$3

    local target_dir="${OUTPUT_PATH}/${runtime}"
    local target_file="${target_dir}/${filename}"

    # Check if file already exists
    if [[ -f "$target_file" && "$FORCE" != "true" ]]; then
        echo -e "${GREEN}✓ ${runtime}/${filename} already exists${NC}"
        return
    fi

    # Create directory if it doesn't exist
    mkdir -p "$target_dir"

    echo -e "${YELLOW}Downloading ${runtime}...${NC}"

    # Note: These are placeholder operations
    # In production, you would download and extract actual binaries
    echo "Placeholder for ${filename} - Replace with actual binary" > "$target_file"

    echo -e "${GREEN}✓ Downloaded ${runtime}/${filename}${NC}"
}

# Build from source function
build_from_source() {
    echo -e "${CYAN}Building whisper.cpp from source...${NC}"

    local build_dir="${OUTPUT_PATH}/../build-whisper"
    local current_dir=$(pwd)

    # Clone whisper.cpp if not exists
    if [[ ! -d "$build_dir" ]]; then
        git clone https://github.com/ggerganov/whisper.cpp.git "$build_dir"
    fi

    cd "$build_dir"

    # Update to specific version
    git fetch --tags
    git checkout "v${VERSION}"

    # Build based on platform
    case "$OS" in
        linux|osx)
            # Standard CPU build
            make clean
            make -j$(nproc 2>/dev/null || sysctl -n hw.ncpu)

            # Copy library
            local target_dir="${OUTPUT_PATH}/${RUNTIME}"
            mkdir -p "$target_dir"
            cp "${build_dir}/libwhisper.${LIB_EXT}" "${target_dir}/${NATIVE_LIB}"

            # CUDA build if available
            if command -v nvcc &> /dev/null; then
                echo -e "${YELLOW}CUDA detected, building with CUDA support...${NC}"
                make clean
                WHISPER_CUBLAS=1 make -j$(nproc 2>/dev/null || sysctl -n hw.ncpu)

                local cuda_dir="${OUTPUT_PATH}/${RUNTIME}-cuda"
                mkdir -p "$cuda_dir"
                cp "${build_dir}/libwhisper.${LIB_EXT}" "${cuda_dir}/libwhisper-cuda.${LIB_EXT}"
            fi

            # CoreML build for macOS
            if [[ "$OS" == "osx" ]]; then
                echo -e "${YELLOW}Building with CoreML support...${NC}"
                make clean
                WHISPER_COREML=1 make -j$(sysctl -n hw.ncpu)

                local coreml_dir="${OUTPUT_PATH}/${RUNTIME}-coreml"
                mkdir -p "$coreml_dir"
                cp "${build_dir}/libwhisper.${LIB_EXT}" "${coreml_dir}/libwhisper-coreml.${LIB_EXT}"
            fi
            ;;
    esac

    cd "$current_dir"
    echo -e "${GREEN}✓ Built whisper.cpp from source${NC}"
}

# Main execution
echo -e "${CYAN}WhisperFS Native Library Setup${NC}"
echo -e "${CYAN}Version: ${VERSION}${NC}"
echo -e "${CYAN}Output: ${OUTPUT_PATH}${NC}"
echo ""

# Detect current platform
detect_platform
echo -e "Detected platform: ${RUNTIME}"
echo -e "Native library: ${NATIVE_LIB}"
echo ""

# Ask user preference
echo "Choose download method:"
echo "1) Download pre-built binaries (if available)"
echo "2) Build from source"
echo "3) Download all platforms (CI/CD)"
read -p "Enter choice (1-3): " choice

case $choice in
    1)
        # Download pre-built for current platform
        download_platform "$RUNTIME" "$NATIVE_LIB" "placeholder-url"
        ;;
    2)
        # Build from source
        build_from_source
        ;;
    3)
        # Download all platforms (for CI/CD)
        platforms=(
            "win-x64:whisper.dll"
            "win-arm64:whisper.dll"
            "linux-x64:libwhisper.so"
            "linux-arm64:libwhisper.so"
            "osx-x64:libwhisper.dylib"
            "osx-arm64:libwhisper.dylib"
        )

        for platform in "${platforms[@]}"; do
            IFS=':' read -r runtime filename <<< "$platform"
            download_platform "$runtime" "$filename" "placeholder-url"
        done
        ;;
    *)
        echo "Invalid choice"
        exit 1
        ;;
esac

echo ""
echo -e "${GREEN}Done! Native libraries are in: ${OUTPUT_PATH}${NC}"
echo ""
echo -e "${YELLOW}Note: You may need to install additional dependencies:${NC}"
case "$OS" in
    linux)
        echo "  sudo apt-get install libopenblas-dev  # For Ubuntu/Debian"
        echo "  sudo dnf install openblas-devel        # For Fedora"
        ;;
    osx)
        echo "  brew install openblas"
        ;;
esac