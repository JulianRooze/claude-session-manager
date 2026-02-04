#!/bin/bash

echo "Building native AOT executable for macOS..."

# Detect architecture
ARCH=$(uname -m)
if [ "$ARCH" = "arm64" ]; then
    RID="osx-arm64"
else
    RID="osx-x64"
fi

echo "Architecture: $ARCH"
echo "Runtime Identifier: $RID"
echo ""

# Clean previous builds
rm -rf bin/publish

# Build native AOT
dotnet publish -c Release -r $RID --self-contained

if [ $? -eq 0 ]; then
    echo ""
    echo "✓ Build successful!"
    echo ""
    echo "Executable location:"
    echo "  bin/Release/net8.0/$RID/publish/ClaudeSessionManager"
    echo ""
    echo "File size:"
    ls -lh bin/Release/net8.0/$RID/publish/ClaudeSessionManager | awk '{print "  " $5}'
    echo ""
    echo "To install, run:"
    echo "  ./install-native.sh"
else
    echo ""
    echo "✗ Build failed"
    exit 1
fi
