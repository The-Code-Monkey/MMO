# MMO Raid Orchestrator

A minimal HTTP control plane for on-demand raid instance orchestration using Docker.

## Endpoints

### `POST /instances`

Start a new raid instance container.

**Request:**
```json
{
  "raidId": "unique-raid-identifier",
  "buildRef": "mmo-raid-server:local",
  "zoneTemplateId": "raid",
  "tokenSecret": "shared-secret-for-zone-transfer-tokens"
}
```

**Response (201):**
```json
{
  "instanceId": "abc123def456"
}
```

### `GET /instances/{instanceId}`

Get the status of a raid instance.

**Response (200):**
```json
{
  "instanceId": "abc123def456",
  "state": "Healthy",
  "raidId": "unique-raid-identifier",
  "zoneTemplateId": "raid",
  "startedAt": 1700000000000,
  "host": "host.docker.internal",
  "port": 32768
}
```

**States:**
- `Requested` - Instance requested, container not yet started
- `Starting` - Container started, waiting for health check
- `Healthy` - Container healthy, host/port available
- `Ready` - Alias for Healthy (game server can use it)
- `Failed` - Container failed to start or became unhealthy
- `TimedOut` - Container did not become healthy within timeout
- `Stopping` - Instance is being stopped

### `DELETE /instances/{instanceId}`

Stop and remove a raid instance container.

**Response (200):**
```json
{
  "state": "stopped",
  "instanceId": "abc123def456"
}
```

### `GET /health`

Health check endpoint.

**Response (200):**
```json
{
  "status": "ok"
}
```

## Configuration (Environment Variables)

| Variable | Default | Description |
|----------|---------|-------------|
| `PORT` | `8080` | HTTP server port |
| `RAID_IMAGE` | `mmo-raid-server:local` | Docker image to run for raid instances |
| `HEALTH_TIMEOUT_SECONDS` | `90` | Max seconds to wait for container to become healthy |
| `DEFAULT_RAID_PORT` | `7777` | Port the raid server listens on inside the container |

## Docker Compose Integration

Add to your `docker-compose.yml`:

```yaml
services:
  orchestrator:
    build: ./orchestrator
    ports:
      - "8080:8080"
    volumes:
      - "/var/run/docker.sock:/var/run/docker.sock"
    environment:
      RAID_IMAGE: mmo-raid-server:local
      HEALTH_TIMEOUT_SECONDS: "90"
    restart: unless-stopped
```

**Important:** The orchestrator needs access to the Docker socket (`/var/run/docker.sock`) to manage containers. This is a privileged operation — only run in trusted local development environments.

## Manual Testing

```bash
# Start the orchestrator
docker compose up -d orchestrator

# Start a raid instance
curl -X POST http://localhost:8080/instances \
  -H "Content-Type: application/json" \
  -d '{
    "raidId": "raid-001",
    "buildRef": "mmo-raid-server:local",
    "zoneTemplateId": "raid",
    "tokenSecret": "super-secret-token"
  }'

# Poll for status (replace INSTANCE_ID)
curl http://localhost:8080/instances/INSTANCE_ID

# Stop the instance
curl -X DELETE http://localhost:8080/instances/INSTANCE_ID
```

## Container Health Checks

The raid server Docker image must include a health check for the orchestrator to detect readiness. Example `Dockerfile` for the raid server:

```dockerfile
FROM unity-base-image

# ... your build steps ...

HEALTHCHECK --interval=5s --timeout=3s --start-period=10s --retries=12 \
  CMD wget -q --spider http://localhost:7777/health || exit 1

ENTRYPOINT ["./MmoRaidServer"]
```

The orchestrator polls `docker inspect` for the container's health status. When it becomes `healthy`, the published host port is resolved and returned.

## Security Notes

- This orchestrator is designed for **local development only**.
- It exposes the Docker socket, which grants root-equivalent access to the host.
- In production, replace with a proper orchestrator (Agones, Kubernetes, etc.) behind authentication.
- The token secret is passed to the container as an environment variable; ensure the raid server image handles it securely.

## Integration with Game Server

The Unity game server uses `IOrchestratorClient` to communicate with this service:

1. Call `StartAsync` → `POST /instances`
2. Poll `WaitHealthyAsync` → `GET /instances/{id}` until `Healthy`/`Ready`
3. Register the returned host/port as a `ZoneRegistryRecord`
4. Proceed with existing raid allocation + zone transfer flow
5. On raid completion, call `StopAsync` → `DELETE /instances/{id}`