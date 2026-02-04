#!/bin/bash
echo "Building Claude Session Manager..."
dotnet publish -c Release -o ~/.local/lib/csm
echo ""
echo "✓ Build complete!"
echo ""
echo "To install, run: ./install.sh"
