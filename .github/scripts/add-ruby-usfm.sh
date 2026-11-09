#!/usr/bin/env bash
# Adds USFM ruby markup (\rb base|reading\rb*) to every verse of a USFM zip.
# Usage: add-ruby-usfm.sh <usfm.zip> <ruby map .tsv: word<TAB>reading> <output zip>
set -euo pipefail

src="$1"
map="$2"
out="$(realpath -m "$3")"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

unzip -q "$src" -d "$work/usfm"
count=0
while IFS= read -r -d '' f; do
  # Only touch verse text lines; each mapped word is wrapped at most once per line.
  awk -F'\t' '
    NR == FNR { if ($1 !~ /^#/ && $1 != "" && $2 != "") map[$1] = $2; next }
    /^\\v / {
      n = split($0, parts, /[ \t]+/); line = ""
      for (i = 1; i <= n; i++) {
        w = parts[i]; key = w; gsub(/[^A-Za-z]/, "", key)
        if (i > 2 && key in map && !(key in done)) {
          pre = w; sub(key ".*$", "", pre); post = w; sub("^" pre key, "", post)
          w = pre "\\rb " key "|" map[key] "\\rb*" post; done[key] = 1
        }
        line = line (i > 1 ? " " : "") w
      }
      delete done; print line; next
    }
    { print }
  ' "$map" "$f" > "$f.new" && mv "$f.new" "$f"
  count=$((count + 1))
done < <(find "$work/usfm" -type f \( -name '*.usfm' -o -name '*.sfm' \) -print0)

[ "$count" -gt 0 ] || { echo "No USFM files found in $src" >&2; exit 1; }
grep -rq '\\rb ' "$work/usfm" || { echo "No ruby was added; check $map" >&2; exit 1; }

rm -f "$out"
(cd "$work/usfm" && zip -q -r "$out" .)
echo "Wrote $out ($count books)"
