#!/bin/sh
# Writes the spike's test media (docs/spike-mechanism-a.md) to dev/media, with the ffmpeg of the
# Jellyfin 12.4 image: nothing to install but Docker.
#
# Every audio track is a sine at its own pitch, so the ear tells which one plays:
#   jpn 330 Hz (high E), fre 262 Hz (middle C), eng 196 Hz (low G).
set -eu

out=${1:-"$(cd "$(dirname "$0")" && pwd)/media"}
mkdir -p "$out"

docker run --rm -i --user "$(id -u):$(id -g)" -v "$out:/media" --entrypoint /bin/sh jellyfin/jellyfin:12.4 -s <<'SCRIPT'
set -eu
ff() { /usr/lib/jellyfin-ffmpeg/ffmpeg -hide_banner -loglevel error -y "$@"; }

# mkv FILE TRACK...: 60 s of H.264 test pattern plus one AAC track per TRACK, given as
# lang:title[:default]. The track marked default is the file's default, the others are not.
mkv() {
  file=$1; shift
  inputs="-f lavfi -i testsrc2=size=1280x720:rate=25:duration=60"
  maps="-map 0:v"
  meta=""
  n=0
  for track in "$@"; do
    lang=${track%%:*}; rest=${track#*:}; title=${rest%%:*}
    case $lang in jpn) freq=330 ;; fre) freq=262 ;; *) freq=196 ;; esac
    n=$((n + 1))
    inputs="$inputs -f lavfi -i sine=frequency=$freq:duration=60"
    maps="$maps -map $n:a"
    a=$((n - 1))
    disposition=0
    case $rest in *:default) disposition=default ;; esac
    meta="$meta -metadata:s:a:$a language=$lang -metadata:s:a:$a title=$title -disposition:a:$a $disposition"
  done
  mkdir -p "$(dirname "$file")"
  # shellcheck disable=SC2086
  ff $inputs $maps -disposition:v:0 default -c:v libx264 -preset veryfast -pix_fmt yuv420p -c:a aac -ac 2 $meta "$file"
}

# Three languages, English is the file default.
mkv "/media/Movies/Spike Movie (2026)/Spike Movie (2026).mkv" jpn:Japanese fre:French eng:English:default

# Two versions of one movie: the French one sorts first, only the original has Japanese.
mkv "/media/Movies/Spike Versions (2026)/Spike Versions (2026) - VF.mkv" fre:French:default eng:English
mkv "/media/Movies/Spike Versions (2026)/Spike Versions (2026) - VO.mkv" jpn:Japanese:default fre:French

# Two episodes with different track orders: index-based memory would land on the wrong language.
mkv "/media/Shows/Spike Show/Season 01/Spike Show - S01E01.mkv" jpn:Japanese eng:English:default fre:French
mkv "/media/Shows/Spike Show/Season 01/Spike Show - S01E02.mkv" eng:English:default fre:French jpn:Japanese
SCRIPT

echo "Test media written to $out"
