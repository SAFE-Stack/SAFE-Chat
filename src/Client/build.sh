#!/bin/bash
# Client Build Script

echo "🚀 Building Client (Modern Fable Client)..."

# Add dotnet tools to path
export PATH="$PATH:$HOME/.dotnet/tools"

# Restore packages if needed
echo "📦 Restoring .NET packages..."
dotnet restore

# Compile F# to JavaScript with Fable
echo "🔄 Compiling F# to JavaScript with Fable..."
fable

# Build with webpack
echo "📦 Building bundle with vite..."
vite build

echo "✅ Client build completed!"
echo "📁 Bundle created: ./public/bundle.js"
echo ""
echo "To start development server:"
echo "  yarn start"
echo ""
echo "To serve with the Giraffe server:"
echo "  cd ../Server && dotnet run"