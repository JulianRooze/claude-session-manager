#!/bin/bash
mkdir -p ~/.local/bin

cat > ~/.local/bin/csm << 'WRAPPER'
#!/bin/bash
exec dotnet ~/.local/lib/csm/ClaudeSessionManager.dll "$@"
WRAPPER

chmod +x ~/.local/bin/csm

echo "✓ Installed csm to ~/.local/bin/csm"
echo ""
echo "Make sure ~/.local/bin is in your PATH"
echo "Run 'csm' to start the Claude Session Manager"
echo "Run 'csm search <query>' to search sessions"
