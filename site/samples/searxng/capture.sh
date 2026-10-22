#!/usr/bin/env bash
# Runs every command shown on the SearXNG search API guide and writes each
# command and its exact output to a numbered file in this directory.
#
# Needs: docker, curl, jq, python3, node and npx, and the .NET 10 SDK.
# Everything binds to 127.0.0.1. All containers and the network are removed at
# the end, and on any error.
#
# Image: searxng/searxng, version 2026.10.19-81371861f
# Pinned by digest below. Pulled on 2026-10-21.
set -uo pipefail

cd "$(dirname "$0")"
OUT="$PWD"

IMAGE='searxng/searxng@sha256:5ed0323b5f5f8701264ef65e239a2bd83cb57098f2b971fa30bf79c06eeae271'
VALKEY='valkey/valkey:8-alpine'
export OUT IMAGE VALKEY

cleanup() {
  docker rm -f searxng searxng-no-json searxng-limiter valkey >/dev/null 2>&1
  docker network rm searxng-net >/dev/null 2>&1
}
trap cleanup EXIT
cleanup

# cap FILE: reads a command from stdin, writes "$ command" and then the
# command's combined output to FILE. Nothing is edited after the run.
cap() {
  local file="$1" cmd
  cmd="$(cat)"
  {
    printf '$ %s\n' "$cmd"
    bash -c "$cmd" 2>&1
  } >"$OUT/$file"
}

# capa FILE: like cap, but appends to FILE.
capa() {
  local file="$1" cmd
  cmd="$(cat)"
  {
    printf '$ %s\n' "$cmd"
    bash -c "$cmd" 2>&1
  } >>"$OUT/$file"
}

# wait_for PORT: waits until the instance on PORT answers on /healthz.
wait_for() {
  local port="$1"
  for _ in $(seq 1 90); do
    if curl -s -o /dev/null "http://127.0.0.1:$port/healthz"; then return 0; fi
    sleep 1
  done
  echo "instance on port $port did not start" >&2
  return 1
}

rm -f "$OUT"/[0-9][0-9]-*.txt

cap 00-environment.txt <<'CMD'
docker --version; python3 --version; dotnet --version; node --version; jq --version
CMD

# 01-02: start the instance with JSON enabled
cap 01-docker-run.txt <<'CMD'
docker run --name searxng -d \
  -p 127.0.0.1:8888:8080 \
  -v "$PWD/settings.yml:/etc/searxng/settings.yml:ro" \
  searxng/searxng@sha256:5ed0323b5f5f8701264ef65e239a2bd83cb57098f2b971fa30bf79c06eeae271
CMD
wait_for 8888 || exit 1

cap 02-version.txt <<'CMD'
docker logs searxng 2>&1 | head -n 1
CMD

# 03: the default 403, from a second instance without the json format
cap 03-json-403.txt <<'CMD'
docker run --name searxng-no-json -d \
  -p 127.0.0.1:8889:8080 \
  -v "$PWD/settings-no-json.yml:/etc/searxng/settings.yml:ro" \
  searxng/searxng@sha256:5ed0323b5f5f8701264ef65e239a2bd83cb57098f2b971fa30bf79c06eeae271
CMD
wait_for 8889 || exit 1
capa 03-json-403.txt <<'CMD'
curl -si 'http://127.0.0.1:8889/search?q=searxng&format=json'
CMD
docker rm -f searxng-no-json >/dev/null

# 04: the response shape, with the results array cut to the first two items
cap 04-search-json.txt <<'CMD'
curl -s 'http://127.0.0.1:8888/search?q=searxng+docker&format=json' | jq '.results |= .[:2]'
CMD

# 05: the working check
cap 05-working-check.txt <<'CMD'
curl -s 'http://127.0.0.1:8888/search?q=searxng+docker&format=json' | jq '.results | length'
CMD

# 06-08: parameters
cap 06-param-engines.txt <<'CMD'
curl -s 'http://127.0.0.1:8888/search?q=searxng&format=json&engines=brave,bing' \
  | jq '[.results[].engine] | unique'
CMD

cap 07-param-categories-time-range.txt <<'CMD'
curl -s 'http://127.0.0.1:8888/search?q=searxng&format=json&categories=news&time_range=month' \
  | jq '[.results[:3][] | {title, url, engine, publishedDate}]'
CMD

cap 08-param-pageno.txt <<'CMD'
for p in 1 2; do
  echo "pageno=$p"
  curl -s "http://127.0.0.1:8888/search?q=searxng+docker&format=json&pageno=$p" \
    | jq -r '.results[:3][].url'
done
CMD

cap 09-param-language-safesearch.txt <<'CMD'
curl -s 'http://127.0.0.1:8888/search?q=wetter+berlin&format=json&language=de&safesearch=2' \
  | jq '{query, results: (.results | length), first: .results[0].title}'
CMD

# 10: POST works as well as GET
cap 10-post.txt <<'CMD'
curl -s -X POST 'http://127.0.0.1:8888/search' -d q=searxng -d format=json \
  | jq '.results | length'
CMD

# 11-12: clients
cap 11-python.txt <<'CMD'
python3 client.py "searxng docker"
CMD

cap 12-csharp.txt <<'CMD'
dotnet run client.cs -- "searxng docker"
CMD

# 13: the same call shaped as an agent tool
cap 13-agent-tool.txt <<'CMD'
python3 agent_tool.py "searxng docker"
CMD

# 14-15: the same instance through an MCP server (mcp-searxng 2.7.0 over stdio)
cap 14-mcp-tools.txt <<'CMD'
./mcp_session.sh tools-list | jq '[.result.tools[] | {name, required: .inputSchema.required}]'
CMD

cap 15-mcp-search.txt <<'CMD'
./mcp_session.sh search "searxng docker" | jq -r '.result.content[0].text' | head -n 14
CMD

# 16: which upstream engines failed on this run
cap 16-unresponsive-engines.txt <<'CMD'
curl -s 'http://127.0.0.1:8888/search?q=searxng+docker&format=json' | jq '.unresponsive_engines'
CMD

# 17: asking only for an engine that is suspended gives an empty results array
cap 17-empty-results.txt <<'CMD'
curl -s 'http://127.0.0.1:8888/search?q=searxng&format=json&engines=wikipedia' \
  | jq '{results: (.results | length), unresponsive_engines}'
CMD

# 18-19: the limiter
docker network create searxng-net >/dev/null
docker run -d --name valkey --network searxng-net "$VALKEY" >/dev/null

cap 18-limiter-429.txt <<'CMD'
docker run --name searxng-limiter -d --network searxng-net \
  -p 127.0.0.1:8890:8080 \
  -v "$PWD/settings-limiter.yml:/etc/searxng/settings.yml:ro" \
  searxng/searxng@sha256:5ed0323b5f5f8701264ef65e239a2bd83cb57098f2b971fa30bf79c06eeae271
CMD
wait_for 8890 || exit 1
capa 18-limiter-429.txt <<'CMD'
curl -si 'http://127.0.0.1:8890/search?q=searxng&format=json'
CMD
capa 18-limiter-429.txt <<'CMD'
docker logs searxng-limiter 2>&1 | grep 'botdetection:'
CMD
docker rm -f searxng-limiter >/dev/null

cap 19-limiter-fixed.txt <<'CMD'
docker run --name searxng-limiter -d --network searxng-net \
  -p 127.0.0.1:8890:8080 \
  -v "$PWD/settings-limiter.yml:/etc/searxng/settings.yml:ro" \
  -v "$PWD/limiter.toml:/etc/searxng/limiter.toml:ro" \
  searxng/searxng@sha256:5ed0323b5f5f8701264ef65e239a2bd83cb57098f2b971fa30bf79c06eeae271
CMD
wait_for 8890 || exit 1
capa 19-limiter-fixed.txt <<'CMD'
curl -s -o /dev/null -w 'HTTP %{http_code}\n' 'http://127.0.0.1:8890/search?q=searxng&format=json'
CMD
capa 19-limiter-fixed.txt <<'CMD'
curl -s 'http://127.0.0.1:8890/search?q=searxng&format=json' | jq '.results | length'
CMD

# 20: stop and remove everything
cap 20-cleanup.txt <<'CMD'
docker rm -f searxng searxng-limiter valkey
docker network rm searxng-net
docker ps -a --filter name=searxng --filter name=valkey -q | wc -l
CMD
