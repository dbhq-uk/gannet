import { Container, getContainer } from "@cloudflare/containers";

interface Env {
  GANNET: DurableObjectNamespace<GannetContainer>;
}

/** The gannet web host in a container. One named instance, because Blazor keeps page state in process. */
export class GannetContainer extends Container<Env> {
  defaultPort = 8080;
  sleepAfter = "10m";
}

export default {
  async fetch(request: Request, env: Env): Promise<Response> {
    // Return the container's response directly: only fetch() preserves WebSocket upgrades (facts s.1e).
    return getContainer(env.GANNET, "gannet").fetch(request);
  },
} satisfies ExportedHandler<Env>;
