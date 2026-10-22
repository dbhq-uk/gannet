# Security

Report a vulnerability privately through GitHub's "Report a vulnerability" button on the Security tab of this repository. Please do not open a public issue.

Gannet holds API keys for model and search services. Run it on localhost, or behind one of its sign-in modes (`ApiKey`, `Oidc`, `CloudflareAccess`). In its default `None` mode it refuses to listen on anything but the loopback address.

Supported versions: the latest commit on `main`.
