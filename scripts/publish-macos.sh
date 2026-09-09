#!/usr/bin/env bash
set -euo pipefail

version="0.1.2"
configuration="Release"
output_root=""
skip_verify=false
skip_smoke_test=false

usage() {
  cat <<'EOF'
Usage: ./scripts/publish-macos.sh [options]

Options:
  --version VERSION       Semantic version (default: 0.1.2)
  --configuration NAME    Debug or Release (default: Release)
  --output-root PATH      Release output root
  --skip-verify           Skip repository verification
  --skip-smoke-test       Skip native-architecture launch smoke test
  --help                  Show this help
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --version) version="${2:?Missing value for --version}"; shift 2 ;;
    --configuration) configuration="${2:?Missing value for --configuration}"; shift 2 ;;
    --output-root) output_root="${2:?Missing value for --output-root}"; shift 2 ;;
    --skip-verify) skip_verify=true; shift ;;
    --skip-smoke-test) skip_smoke_test=true; shift ;;
    --help) usage; exit 0 ;;
    *) echo "Unknown option: $1" >&2; usage >&2; exit 2 ;;
  esac
done

if [[ ! "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+([+-][0-9A-Za-z.-]+)?$ ]]; then
  echo "Version must be semantic version text: $version" >&2
  exit 2
fi

if [[ "$configuration" != "Debug" && "$configuration" != "Release" ]]; then
  echo "Configuration must be Debug or Release: $configuration" >&2
  exit 2
fi

if [[ "$(uname -s)" != "Darwin" ]]; then
  echo "macOS packaging must run on macOS so bundle tools and a native smoke test are available." >&2
  exit 1
fi

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repository_root="$(cd "$script_dir/.." && pwd)"
project_path="$repository_root/src/VaultDelta.Desktop/VaultDelta.Desktop.csproj"
plist_template="$repository_root/packaging/macos/Info.plist"
package_readme="$repository_root/packaging/macos/README.txt"
output_root="${output_root:-$repository_root/artifacts/releases/macos}"
mkdir -p "$output_root"
output_root="$(cd "$output_root" && pwd)"

checksum_path="$output_root/SHA256SUMS.txt"
if [[ -e "$checksum_path" ]]; then
  echo "Release checksum file already exists: $checksum_path" >&2
  exit 1
fi

if [[ "$skip_verify" == false ]]; then
  pwsh "$script_dir/verify.ps1" -Configuration "$configuration"
fi

git_commit="$(git -C "$repository_root" rev-parse HEAD)"
if [[ -n "$(git -C "$repository_root" status --porcelain)" ]]; then
  git_dirty=true
else
  git_dirty=false
fi

bundle_version="${version%%[-+]*}"
checksum_staging="$(mktemp "$output_root/.vaultdelta-checksums.XXXXXX")"
staging_root="$(mktemp -d "$output_root/.vaultdelta-macos-package.XXXXXX")"

cleanup() {
  rm -rf "$staging_root"
  rm -f "$checksum_staging"
}
trap cleanup EXIT

create_icon() {
  local resources_directory="$1"
  local icon_source="$staging_root/VaultDelta-1024.png"
  local iconset="$staging_root/VaultDelta.iconset"

  python3 - "$icon_source" <<'PY'
import binascii
import struct
import sys
import zlib

path = sys.argv[1]
size = 1024
rows = []
for y in range(size):
    row = bytearray([0])
    for x in range(size):
        dx = x - size / 2
        dy = y - size / 2
        inside = dx * dx + dy * dy < (size * 0.42) ** 2
        if inside:
            r = 35 + int(35 * y / size)
            g = 93 + int(75 * x / size)
            b = 190 + int(45 * (1 - y / size))
            a = 255
        else:
            r = g = b = a = 0
        row.extend((r, g, min(b, 255), a))
    rows.append(bytes(row))

def chunk(kind, data):
    return struct.pack('>I', len(data)) + kind + data + struct.pack('>I', binascii.crc32(kind + data) & 0xffffffff)

png = b'\x89PNG\r\n\x1a\n'
png += chunk(b'IHDR', struct.pack('>IIBBBBB', size, size, 8, 6, 0, 0, 0))
png += chunk(b'IDAT', zlib.compress(b''.join(rows), 9))
png += chunk(b'IEND', b'')
with open(path, 'wb') as stream:
    stream.write(png)
PY

  mkdir -p "$iconset"
  for size in 16 32 128 256 512; do
    sips -z "$size" "$size" "$icon_source" --out "$iconset/icon_${size}x${size}.png" >/dev/null
    double=$((size * 2))
    sips -z "$double" "$double" "$icon_source" --out "$iconset/icon_${size}x${size}@2x.png" >/dev/null
  done
  iconutil -c icns "$iconset" -o "$resources_directory/VaultDelta.icns"
}

host_arch="$(uname -m)"
for rid in osx-arm64 osx-x64; do
  package_name="VaultDelta-$version-$rid"
  package_directory="$output_root/$package_name"
  archive_path="$output_root/$package_name.zip"
  staged_package="$staging_root/$package_name"
  app_directory="$staged_package/Vault Delta.app"
  contents_directory="$app_directory/Contents"
  executable_directory="$contents_directory/MacOS"
  resources_directory="$contents_directory/Resources"

  if [[ -e "$package_directory" ]]; then
    echo "Release directory already exists: $package_directory" >&2
    exit 1
  fi
  if [[ -e "$archive_path" ]]; then
    echo "Release archive already exists: $archive_path" >&2
    exit 1
  fi

  mkdir -p "$executable_directory" "$resources_directory"
  dotnet publish "$project_path" \
    --configuration "$configuration" \
    --runtime "$rid" \
    --self-contained true \
    --output "$executable_directory" \
    -p:Version="$version" \
    -p:PublishSingleFile=false \
    -p:DebugType=None \
    -p:DebugSymbols=false

  required_files=(
    VaultDelta
    VaultDelta.dll
    VaultDelta.deps.json
    VaultDelta.runtimeconfig.json
    libcoreclr.dylib
    libhostfxr.dylib
    Avalonia.Base.dll
    VaultDelta.Application.dll
    VaultDelta.Domain.dll
    VaultDelta.Infrastructure.dll
  )
  for required_file in "${required_files[@]}"; do
    if [[ ! -f "$executable_directory/$required_file" ]]; then
      echo "Published package is missing required file: $required_file" >&2
      exit 1
    fi
  done
  chmod +x "$executable_directory/VaultDelta"

  sed \
    -e "s/__VERSION__/$version/g" \
    -e "s/__BUNDLE_VERSION__/$bundle_version/g" \
    "$plist_template" > "$contents_directory/Info.plist"
  printf 'APPL????' > "$contents_directory/PkgInfo"
  cp "$package_readme" "$staged_package/README.txt"
  create_icon "$resources_directory"

  cat > "$resources_directory/release.json" <<EOF
{
  "product": "Vault Delta",
  "version": "$version",
  "runtimeIdentifier": "$rid",
  "selfContained": true,
  "framework": "net10.0",
  "createdAtUtc": "$(date -u +'%Y-%m-%dT%H:%M:%SZ')",
  "gitCommit": "$git_commit",
  "gitDirty": $git_dirty,
  "signatureStatus": "Unsigned"
}
EOF

  plutil -lint "$contents_directory/Info.plist"
  [[ "$(/usr/libexec/PlistBuddy -c 'Print :CFBundleIdentifier' "$contents_directory/Info.plist")" == "io.vaultdelta.app" ]]
  [[ "$(/usr/libexec/PlistBuddy -c 'Print :CFBundleExecutable' "$contents_directory/Info.plist")" == "VaultDelta" ]]
  file "$executable_directory/VaultDelta" | grep -q 'Mach-O 64-bit executable'
  if [[ "$rid" == "osx-arm64" ]]; then
    file "$executable_directory/VaultDelta" | grep -q 'arm64'
    native_arch="arm64"
  else
    file "$executable_directory/VaultDelta" | grep -q 'x86_64'
    native_arch="x86_64"
  fi

  if [[ "$skip_smoke_test" == false && "$host_arch" == "$native_arch" ]]; then
    (cd "$executable_directory" && ./VaultDelta) >/dev/null 2>&1 &
    process_id=$!
    sleep 3
    if ! kill -0 "$process_id" 2>/dev/null; then
      wait "$process_id" || true
      echo "Published application exited during native smoke test: $rid" >&2
      exit 1
    fi
    kill "$process_id"
    wait "$process_id" || true
  fi

  mv "$staged_package" "$package_directory"
  ditto -c -k --sequesterRsrc --keepParent "$package_directory" "$archive_path"
  archive_hash="$(shasum -a 256 "$archive_path" | awk '{print tolower($1)}')"
  printf '%s  %s\n' "$archive_hash" "$(basename "$archive_path")" >> "$checksum_staging"
  echo "Created $archive_path ($archive_hash)"
done

mv "$checksum_staging" "$checksum_path"
echo "macOS packages created successfully in $output_root"
