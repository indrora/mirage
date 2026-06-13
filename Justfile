# Justfile for MirageBox — build/packaging chores.
#
# Conventions:
#   * Icon source of truth is the Affinity export at art/icon/icon.png (1024px).
#     `just icons` derives every generated asset from it; never hand-edit the
#     files under src/MirageBox.Oasis.Desktop/Assets or icon.icns directly.
#   * The .icns lives at the Desktop project ROOT (not Assets/); `bundle-mac`
#     copies it into the synthesized Oasis.app/Contents/Resources/.
#
# Run `just` with no arguments to see this list.

desktop := "src/MirageBox.Oasis.Desktop"
desktop_csproj := desktop / "MirageBox.Oasis.Desktop.csproj"
icon_src := "art/icon/icon.png"

# Version stamped into archive filenames. Keep in sync with the CFBundle*
# version in the Desktop csproj until that gets centralised somewhere.
version := "0.1.0"

# Where packaged artifacts land. Repo-root bin/ is covered by the dotnet
# .gitignore, so these don't get committed by accident.
dist := "bin"

# Default macOS runtime identifier, derived from the host CPU. The mac recipes
# take a `rid` argument so you can cross-build, e.g. `just bundle-mac osx-x64`.
mac_rid := if arch() == "aarch64" { "osx-arm64" } else { "osx-x64" }

# .app bundle metadata, baked into the synthesized Info.plist. bundle_exe MUST
# match the apphost name dotnet emits (the assembly name, no extension) or
# macOS can't launch the bundle.
bundle_id := "io.github.indrora.mirage.oasis"
bundle_exe := "MirageBox.Oasis.Desktop"

# List available recipes.
default:
    @just --list

# Debug build of the desktop app.
build:
    dotnet build {{ desktop_csproj }}

# Run the desktop app (Debug).
run:
    dotnet run --project {{ desktop_csproj }}

# The 1024px export is used VERBATIM as the icns 512x512@2x rung; only the
# rungs that have no exact-size export get machine-downscaled. If smaller
# hand-tuned exports ever land in art/, teach this recipe to prefer them.
#
# Regenerate all icon assets (icns/ico/png) from the Affinity export.
icons:
    #!/usr/bin/env bash
    set -euo pipefail
    iconset="$(mktemp -d)/oasis.iconset"
    mkdir -p "$iconset"
    # Exact-size rung: no resampling.
    cp {{ icon_src }} "$iconset/icon_512x512@2x.png"
    for s in 16 32 128 256 512; do
        magick {{ icon_src }} -resize ${s}x${s} "$iconset/icon_${s}x${s}.png"
        d=$((s * 2))
        magick {{ icon_src }} -resize ${d}x${d} "$iconset/icon_${s}x${s}@2x.png"
    done
    iconutil -c icns "$iconset" -o {{ desktop }}/icon.icns
    # Windows embedded-resource icon (multi-res .ico)...
    magick {{ icon_src }} -define icon:auto-resize=256,128,64,48,32,24,16 {{ desktop }}/Assets/icon.ico
    # ...and the runtime Window.Icon. PNG on purpose: Skia can't decode .ico
    # off-Windows, so an .ico here would leave the macOS dock icon blank.
    cp {{ icon_src }} {{ desktop }}/Assets/icon.png
    rm -rf "$iconset"
    echo "icon assets regenerated from {{ icon_src }}"

# Preferred over `icons` when exact-size exports exist: each .icns rung and each
# .ico frame is copied straight from an art/icon/icon-<px>.png export, so there
# is ZERO resampling. A mac .icns tops out at 1024px (512@2x), so icon-2048 is
# not needed even if you export one. Writes both files into Desktop/Assets.
# (Window.Icon's icon.png is handled by `just icons`, not here.)
#
# Build icon.icns + icon.ico from the exact-size exports in art/icon/.
icons-exact:
    #!/usr/bin/env bash
    # NB: macOS system bash is 3.2 — no associative arrays. Keep it portable.
    set -euo pipefail
    src="art/icon"
    iconset="$(mktemp -d)/oasis.iconset"
    mkdir -p "$iconset"
    # iconset_rung:exact_source_px — every rung is a real export, nothing scaled.
    for pair in \
        "icon_16x16:16"     "icon_16x16@2x:32" \
        "icon_32x32:32"     "icon_32x32@2x:64" \
        "icon_128x128:128"  "icon_128x128@2x:256" \
        "icon_256x256:256"  "icon_256x256@2x:512" \
        "icon_512x512:512"  "icon_512x512@2x:1024"; do
        rung="${pair%%:*}"
        px="${pair##*:}"
        f="$src/icon-$px.png"
        [[ -f "$f" ]] || { echo "missing $f — need an exact ${px}px export" >&2; exit 1; }
        cp "$f" "$iconset/$rung.png"
    done
    iconutil -c icns "$iconset" -o {{ desktop }}/Assets/icon.icns
    rm -rf "$iconset"
    # Windows .ico: bundle whichever standard small exports exist, at native size.
    ico_inputs=()
    for px in 16 24 32 48 64 128 256; do
        [[ -f "$src/icon-$px.png" ]] && ico_inputs+=("$src/icon-$px.png")
    done
    [[ ${#ico_inputs[@]} -gt 0 ]] || { echo "no icon-<px>.png exports found in $src" >&2; exit 1; }
    magick "${ico_inputs[@]}" {{ desktop }}/Assets/icon.ico
    echo "icns + ico rebuilt from ${#ico_inputs[@]} exact exports in $src"

# Per https://docs.avaloniaui.net/docs/deployment/macos#manual-packaging:
# publish straight into Contents/MacOS, drop the .icns into Contents/Resources,
# write Info.plist. UseAppHost=true is required so a native launcher (not just
# the .dll) lands in MacOS/, otherwise Finder can't start the bundle.
#
# Synthesize bin/Oasis.app by hand (Release, self-contained).
bundle-mac rid=mac_rid:
    #!/usr/bin/env bash
    set -euo pipefail
    app="{{ dist }}/Oasis.app"
    rm -rf "$app"
    mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
    dotnet publish {{ desktop_csproj }} -c Release -r {{ rid }} \
        --self-contained -p:UseAppHost=true -o "$app/Contents/MacOS"
    cp {{ desktop }}/Assets/icon.icns "$app/Contents/Resources/icon.icns"
    cat > "$app/Contents/Info.plist" <<'PLIST'
    <?xml version="1.0" encoding="UTF-8"?>
    <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
    <plist version="1.0">
    <dict>
        <key>CFBundleName</key>            <string>Oasis</string>
        <key>CFBundleDisplayName</key>     <string>Oasis</string>
        <key>CFBundleIdentifier</key>      <string>{{ bundle_id }}</string>
        <key>CFBundleExecutable</key>      <string>{{ bundle_exe }}</string>
        <key>CFBundleIconFile</key>        <string>icon.icns</string>
        <key>CFBundleVersion</key>         <string>{{ version }}</string>
        <key>CFBundleShortVersionString</key> <string>{{ version }}</string>
        <key>CFBundleInfoDictionaryVersion</key> <string>6.0</string>
        <key>CFBundlePackageType</key>     <string>APPL</string>
        <key>LSMinimumSystemVersion</key>  <string>10.15</string>
        <key>NSHighResolutionCapable</key> <true/>
    </dict>
    </plist>
    PLIST
    echo "built $app"

# Build the .app and drop it into /Applications (replaces any existing copy).
install rid=mac_rid: (bundle-mac rid)
    rm -rf /Applications/Oasis.app
    cp -R {{ dist }}/Oasis.app /Applications/

# Publish + archive one runtime identifier into bin/. Hidden from --list
# because the platform recipes below wrap it, but you can call it directly
# for any RID, e.g. `just _package win-arm64` or `just _package osx-x64`.
#
# Windows gets a .zip (preserves nothing unix-y it needs); everything else
# gets a .tar.gz so the executable bit on the launcher survives.
_package rid:
    #!/usr/bin/env bash
    set -euo pipefail
    stage="{{ dist }}/stage/{{ rid }}"
    rm -rf "$stage"
    dotnet publish {{ desktop_csproj }} -c Release -r {{ rid }} --self-contained -o "$stage"
    mkdir -p {{ dist }}
    name="Oasis-{{ version }}-{{ rid }}"
    case "{{ rid }}" in
        win-*) ( cd "{{ dist }}/stage" && rm -f "../$name.zip" && zip -qr "../$name.zip" "{{ rid }}" ) ;;
        *)     tar -czf "{{ dist }}/$name.tar.gz" -C "{{ dist }}/stage" "{{ rid }}" ;;
    esac
    echo "packaged -> {{ dist }}/$name"

# Package a self-contained Windows x64 build (.zip) into bin/.
package-win: (_package "win-x64")

# tar (not zip) preserves the launcher's executable bit.
#
# Synthesize the macOS .app, then archive it into bin/ as a .tar.gz.
package-mac rid=mac_rid: (bundle-mac rid)
    tar -czf {{ dist }}/Oasis-{{ version }}-{{ rid }}.tar.gz -C {{ dist }} Oasis.app
    @echo "packaged -> {{ dist }}/Oasis-{{ version }}-{{ rid }}.tar.gz"

# Package a self-contained Linux x64 build (.tar.gz) into bin/.
package-linux: (_package "linux-x64")

# Package all three desktop platforms into bin/.
package-all: package-win package-mac package-linux

# Remove build output across the solution, including packaged artifacts.
clean:
    dotnet clean MirageBox.sln
    find src -type d \( -name bin -o -name obj \) -prune -exec rm -rf {} +
    rm -rf {{ dist }}
