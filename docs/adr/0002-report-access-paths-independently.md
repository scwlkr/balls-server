# Report local and Tailscale readiness independently

Balls Server reports readiness for a trusted local network and Tailscale as separate access paths; a host may be ready for either path or both. This replaces the v0.1.0 policy that withheld an overall Ready result unless both paths were available. The product will support both paths, but an individual owner does not need to enable an unused path, and the dashboard must never imply that prerequisite readiness means hosting is already configured or connection-verified.
