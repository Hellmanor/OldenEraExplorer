#!/bin/bash
set -e

# Change to project root directory
cd "$(dirname "$0")/.."

APP_NAME="OldenEraExplorer"

# Read VERSION from environment variable (optional)
VERSION="${VERSION:-}"

# Build package name with version if provided
if [ -n "$VERSION" ]; then
    PACKAGE_BASE="${APP_NAME}-v${VERSION}"
    echo "========================================="
    echo "Building Olden Era Explorer Release v${VERSION}"
    echo "========================================="
else
    PACKAGE_BASE="${APP_NAME}"
    echo "========================================="
    echo "Building Olden Era Explorer Release"
    echo "========================================="
fi

# Define all target RIDs
RIDS=("win-x64" "linux-x64" "osx-x64" "osx-arm64")

# Build frontend
echo ""
echo "Step 1: Building frontend..."
# Unset VERSION to prevent it from affecting npm/dotnet builds
# VERSION should only control the ZIP file names, not the build process
unset VERSION
npm ci
npm run build --prefix frontend
echo "Frontend build complete!"

# Copy frontend build to backend wwwroot (for embedding into executable)
echo ""
echo "Step 2: Copying frontend to backend wwwroot for embedding..."
rm -rf backend/src/API/wwwroot
mkdir -p backend/src/API/wwwroot
cp -r frontend/dist/* backend/src/API/wwwroot/
echo "Frontend files ready for embedding!"

# Generate tray icon for Linux/macOS (24x24 PNG from favicon.ico)
echo ""
echo "Step 3: Generating platform-specific tray icon..."
convert frontend/public/favicon.ico -resize 24x24 frontend/public/tray-icon.png
echo "Tray icon generated!"

# Clean dist directory
echo ""
echo "Step 4: Cleaning dist directory..."
rm -rf dist
mkdir -p dist

# Build backend for each RID
echo ""
echo "Step 5: Building backend for all platforms..."
for rid in "${RIDS[@]}"; do
    echo ""
    echo "  Building for $rid..."
    dotnet publish backend/src/API/API.csproj \
        -c Release \
        -r "$rid" \
        --self-contained true \
        -o "dist/$rid" \
        -p:PublishSingleFile=true \
        -p:EnableCompressionInSingleFile=true \
        -p:IncludeNativeLibrariesForSelfExtract=true \
        -p:DebugType=none \
        -p:DebugSymbols=false

    # Clean up unnecessary files
    echo "  Cleaning up $rid..."
    rm -f "dist/$rid"/*.pdb
    rm -f "dist/$rid"/*.json
    rm -f "dist/$rid"/web.config
    rm -rf "dist/$rid/wwwroot"
    rm -rf "dist/$rid/ExtractedAssets"
    rm -rf "dist/$rid/CustomAssets"

    echo "  $rid build complete!"
done

# Clean up wwwroot from source (it was only needed for embedding)
echo ""
echo "Step 6: Cleaning up temporary build files..."
rm -rf backend/src/API/wwwroot
rm -f frontend/public/tray-icon.png
echo "Cleanup complete!"

echo ""
echo "Step 7: Creating zip packages..."
for rid in "${RIDS[@]}"; do
    echo "  Zipping $rid..."
    cd "dist/$rid"
    zip -r "../${PACKAGE_BASE}-${rid}.zip" .
    cd ../..
    echo "  Created: dist/${PACKAGE_BASE}-${rid}.zip"
done

echo ""
echo "========================================="
echo "Release build complete!"
echo ""
echo "Packages:"
for rid in "${RIDS[@]}"; do
    SIZE=$(du -h "dist/${PACKAGE_BASE}-${rid}.zip" | cut -f1)
    echo "  dist/${PACKAGE_BASE}-${rid}.zip ($SIZE)"
done
echo ""
echo "Contents:"
for rid in "${RIDS[@]}"; do
    echo "  dist/$rid/"
    ls -la "dist/$rid/"
done
echo "========================================="
echo ""
echo "To run a release build:"
echo "  Windows: dist/win-x64/${APP_NAME}.exe"
echo "  Linux:   dist/linux-x64/${APP_NAME}"
echo "  macOS:   dist/osx-x64/${APP_NAME}"
echo ""
