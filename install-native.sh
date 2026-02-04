#!/bin/bash

# Detect architecture
ARCH=$(uname -m)
if [ "$ARCH" = "arm64" ]; then
    RID="osx-arm64"
else
    RID="osx-x64"
fi

EXECUTABLE="bin/Release/net8.0/$RID/publish/ClaudeSessionManager"

if [ ! -f "$EXECUTABLE" ]; then
    echo "Error: Executable not found at $EXECUTABLE"
    echo "Run ./build-native.sh first"
    exit 1
fi

# Choose installation directory
if [ -d "$HOME/.local/bin" ]; then
    INSTALL_DIR="$HOME/.local/bin"
elif [ -d "$HOME/bin" ]; then
    INSTALL_DIR="$HOME/bin"
else
    echo "Creating ~/bin directory..."
    mkdir -p "$HOME/bin"
    INSTALL_DIR="$HOME/bin"
fi

# Copy executable
cp "$EXECUTABLE" "$INSTALL_DIR/csm"
chmod +x "$INSTALL_DIR/csm"

echo "✓ Installed to $INSTALL_DIR/csm"
echo ""

# Check if directory is in PATH
if [[ ":$PATH:" != *":$INSTALL_DIR:"* ]]; then
    echo "Note: $INSTALL_DIR is not in your PATH"
    echo "Add this line to your ~/.zshrc or ~/.bashrc:"
    echo "  export PATH=\"$INSTALL_DIR:\$PATH\""
    echo ""
fi

echo "Installation complete! Run 'csm' to start the Claude Session Manager"
echo ""
echo "Native executable info:"
ls -lh "$INSTALL_DIR/csm" | awk '{print "  Size: " $5}'
