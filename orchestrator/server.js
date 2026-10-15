const http = require('http');
const { execFile, spawn } = require('child_process');
const { promisify } = require('util');
const { randomBytes } = require('crypto');
const { createPortAllocator } = require('./portAllocator');

const PORT = process.env.PORT || 8080;
const RAID_IMAGE = process.env.RAID_IMAGE || 'mmo-raid-server:local';
const HEALTH_TIMEOUT_SECONDS = parseInt(process.env.HEALTH_TIMEOUT_SECONDS || '90', 10);
const DEFAULT_RAID_PORT = parseInt(process.env.DEFAULT_RAID_PORT || '7777', 10);
const RAID_PORT_RANGE_START = parseInt(process.env.RAID_PORT_RANGE_START || '7800', 10);
const RAID_PORT_RANGE_END = parseInt(process.env.RAID_PORT_RANGE_END || '7899', 10);

// Deterministic host ports, lowest free first. `-p 0:<port>` used to let Docker pick an ephemeral
// port per container; that port became the players' zone address and died with the `--rm`
// container, leaving stale `last_zone` records pointing at nothing. A fixed range keeps zone
// addresses meaningful (and greppable) for the life of the instance.
const hostPorts = createPortAllocator({
    start: RAID_PORT_RANGE_START,
    end: RAID_PORT_RANGE_END,
});

const instances = new Map();

function generateInstanceId() {
    return randomBytes(8).toString('hex');
}

function runDocker(args) {
    return new Promise((resolve, reject) => {
        const proc = spawn('docker', args, { stdio: 'pipe' });
        let stdout = '';
        let stderr = '';
        proc.stdout.on('data', d => stdout += d);
        proc.stderr.on('data', d => stderr += d);
        proc.on('close', code => {
            if (code === 0) resolve(stdout.trim());
            else reject(new Error(stderr || `docker exited with code ${code}`));
        });
    });
}

async function startContainer(instanceId, raidId, buildRef, zoneTemplateId, tokenSecret, hostPort) {
    const containerName = `mmo-raid-${instanceId}`;
    const env = [
        `RAID_INSTANCE_ID=${raidId}`,
        `ZONE_TEMPLATE_ID=${zoneTemplateId}`,
        `ZONE_TRANSFER_SECRET=${tokenSecret}`,
        `INSTANCE_ID=${instanceId}`,
    ];

    const args = [
        'run', '-d', '--rm',
        '--name', containerName,
        '-p', `${hostPort}:${DEFAULT_RAID_PORT}`,
    ];

    for (const e of env) {
        args.push('-e', e);
    }

    args.push(RAID_IMAGE);

    try {
        const containerId = await runDocker(args);
        return { containerId, containerName };
    } catch (err) {
        throw new Error(`Failed to start container: ${err.message}`);
    }
}

async function getContainerHealth(containerName) {
    try {
        const inspect = await runDocker(['inspect', containerName]);
        const data = JSON.parse(inspect);
        const health = data[0]?.State?.Health?.Status;
        return health || 'unknown';
    } catch {
        return 'unknown';
    }
}

async function stopContainer(containerName) {
    try {
        await runDocker(['stop', containerName]);
        return true;
    } catch {
        return false;
    }
}

function parseBody(req) {
    return new Promise((resolve, reject) => {
        let body = '';
        req.on('data', chunk => body += chunk);
        req.on('end', () => {
            try {
                resolve(body ? JSON.parse(body) : {});
            } catch (e) {
                reject(new Error('Invalid JSON'));
            }
        });
    });
}

function sendJson(res, status, data) {
    res.writeHead(status, { 'Content-Type': 'application/json' });
    res.end(JSON.stringify(data));
}

const server = http.createServer(async (req, res) => {
    const url = new URL(req.url, `http://localhost:${PORT}`);
    const path = url.pathname;
    const method = req.method;

    try {
        if (method === 'POST' && path === '/instances') {
            const body = await parseBody(req);
            const { raidId, buildRef, zoneTemplateId, tokenSecret } = body;

            if (!raidId || !zoneTemplateId || !tokenSecret) {
                return sendJson(res, 400, { error: 'raidId, zoneTemplateId, and tokenSecret are required' });
            }

            let hostPort;
            try {
                hostPort = await hostPorts.allocate();
            } catch (err) {
                return sendJson(res, 503, { error: err.message });
            }

            const instanceId = generateInstanceId();
            const handle = {
                instanceId,
                raidId,
                buildRef,
                zoneTemplateId,
                tokenSecret,
                hostPort,
                state: 'Requested',
                createdAt: Date.now(),
            };
            instances.set(instanceId, handle);

            startContainer(instanceId, raidId, buildRef || RAID_IMAGE, zoneTemplateId, tokenSecret, hostPort)
                .then(({ containerId, containerName }) => {
                    handle.containerId = containerId;
                    handle.containerName = containerName;
                    handle.state = 'Starting';
                })
                .catch(err => {
                    handle.state = 'Failed';
                    handle.error = err.message;
                    hostPorts.release(hostPort);
                    console.error(`[orchestrator] Failed to start container for ${instanceId}:`, err.message);
                });

            return sendJson(res, 201, { instanceId });
        }

        if (method === 'GET' && path.startsWith('/instances/')) {
            const instanceId = path.split('/')[2];
            const handle = instances.get(instanceId);

            if (!handle) {
                return sendJson(res, 404, { error: 'Instance not found' });
            }

            if (handle.state === 'Starting' || handle.state === 'Requested') {
                if (handle.containerName) {
                    const health = await getContainerHealth(handle.containerName);
                    if (health === 'healthy') {
                        handle.state = 'Healthy';
                    } else if (health === 'unhealthy') {
                        handle.state = 'Failed';
                        await stopContainer(handle.containerName);
                        hostPorts.release(handle.hostPort);
                    }
                }

                const elapsed = (Date.now() - handle.createdAt) / 1000;
                if (handle.state !== 'Failed' && elapsed > HEALTH_TIMEOUT_SECONDS) {
                    handle.state = 'TimedOut';
                    if (handle.containerName) {
                        await stopContainer(handle.containerName);
                    }
                    hostPorts.release(handle.hostPort);
                }
            }

            const response = {
                instanceId: handle.instanceId,
                state: handle.state,
                raidId: handle.raidId,
                zoneTemplateId: handle.zoneTemplateId,
                startedAt: handle.createdAt,
            };

            if (handle.state === 'Healthy' || handle.state === 'Ready') {
                response.host = 'host.docker.internal';
                response.port = handle.hostPort;
            }

            if (handle.error) {
                response.error = handle.error;
            }

            return sendJson(res, 200, response);
        }

        if (method === 'DELETE' && path.startsWith('/instances/')) {
            const instanceId = path.split('/')[2];
            const handle = instances.get(instanceId);

            if (!handle) {
                return sendJson(res, 404, { error: 'Instance not found' });
            }

            handle.state = 'Stopping';

            if (handle.containerName) {
                await stopContainer(handle.containerName);
            }
            hostPorts.release(handle.hostPort);

            instances.delete(instanceId);
            return sendJson(res, 200, { state: 'stopped', instanceId });
        }

        if (method === 'GET' && path === '/health') {
            return sendJson(res, 200, { status: 'ok' });
        }

        sendJson(res, 404, { error: 'Not found' });
    } catch (err) {
        console.error('[orchestrator] Request error:', err);
        sendJson(res, 500, { error: 'Internal server error' });
    }
});

server.listen(PORT, () => {
    console.log(`[orchestrator] Listening on port ${PORT}`);
    console.log(`[orchestrator] RAID_IMAGE=${RAID_IMAGE}`);
    console.log(`[orchestrator] HEALTH_TIMEOUT_SECONDS=${HEALTH_TIMEOUT_SECONDS}`);
    console.log(`[orchestrator] Raid host ports: ${RAID_PORT_RANGE_START}-${RAID_PORT_RANGE_END} (container port ${DEFAULT_RAID_PORT})`);
});