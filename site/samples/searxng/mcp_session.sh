#!/usr/bin/env bash
# Talks to the mcp-searxng server over stdio with raw JSON-RPC, the way an MCP
# client does. Usage: mcp_session.sh tools-list | mcp_session.sh search "query"
set -euo pipefail
export SEARXNG_URL="http://127.0.0.1:8888"
init='{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"capture","version":"1"}}}'
ready='{"jsonrpc":"2.0","method":"notifications/initialized"}'
case "$1" in
  tools-list)
    call='{"jsonrpc":"2.0","id":2,"method":"tools/list"}' ;;
  search)
    call=$(jq -cn --arg q "$2" '{jsonrpc:"2.0",id:2,method:"tools/call",params:{name:"searxng_web_search",arguments:{query:$q}}}') ;;
esac
{ echo "$init"; echo "$ready"; echo "$call"; sleep 40; } | npx -y mcp-searxng@2.7.0 2>/dev/null | jq -c 'select(.id == 2)'
