# Cloudflare deployment

Runs gannet's web host in a Cloudflare Container behind a small Worker. This is how the maintainer's private copy runs; you can use it for your own.

- `wrangler.jsonc` builds the repository's `Dockerfile` for `linux/amd64` and runs it as one `basic` instance (1 GiB, 1/4 vCPU).
- The Worker sends every request to one named container, because Blazor Server keeps each open page's state in the server process.
- Put Cloudflare Access in front of the hostname before you attach it. gannet's `CloudflareAccess` sign-in mode also checks the Access token itself.

Deploy: `npm ci && npx wrangler deploy` with `CLOUDFLARE_API_TOKEN` and `CLOUDFLARE_ACCOUNT_ID` set and Docker running.
