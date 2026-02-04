#!/bin/bash

# Build the project
dotnet build -c Release

# Create a wrapper script
cat > csm << 'EOF'
#!/bin/bash
dotnet "$HOME/.claude/tools/session-manager/bin/Release/net8.0/ClaudeSessionManager.dll"
EOF

chmod +x csm

# Move to a location in PATH
if [ -d "$HOME/.local/bin" ]; then
    mv csm "$HOME/.local/bin/"
    echo "✓ Installed to ~/.local/bin/csm"
elif [ -d "$HOME/bin" ]; then
    mv csm "$HOME/bin/"
    echo "✓ Installed to ~/bin/csm"
else
    echo "Creating ~/bin directory..."
    mkdir -p "$HOME/bin"
    mv csm "$HOME/bin/"
    echo "✓ Installed to ~/bin/csm"
    echo ""
    echo "Note: Add ~/bin to your PATH if it's not already there:"
    echo '  export PATH="$HOME/bin:$PATH"'
fi

echo ""
echo "Installation complete! Run 'csm' to start the Claude Session Manager"
