'use strict';

const net = require('net');

/**
 * Deterministic host-port allocator for raid containers.
 *
 * The orchestrator used to publish raid containers with `-p 0:7777`, letting Docker pick a random
 * ephemeral host port. That port was then handed to players as their zone address — but the
 * container is ephemeral (`--rm`), so the port died with it, and the clients' persisted
 * `last_zone` record kept pointing players at a port nothing listens on. Allocating from a
 * fixed, configurable range instead means a zone address stays meaningful for as long as the
 * instance lives, and is trivially debuggable ("the raid is on 7800-7899") afterwards.
 *
 * Ports are handed out lowest-first and verified free with a real bind probe, so a port held by
 * some other host process is skipped rather than doomed-failed later by the Docker daemon.
 */
function createPortAllocator({ start, end, probe = defaultProbe } = {}) {
    if (!Number.isInteger(start) || !Number.isInteger(end) || start < 1 || end > 65535) {
        throw new RangeError(`Invalid port range: ${start}-${end}`);
    }
    if (start > end) {
        throw new RangeError(`Invalid port range: ${start}-${end}`);
    }

    const allocated = new Set();

    return {
        /** Resolves with the lowest free port in the range, or rejects when the range is exhausted. */
        async allocate() {
            for (let port = start; port <= end; port++) {
                if (allocated.has(port)) {
                    continue;
                }
                if (!(await probe(port))) {
                    continue;
                }
                allocated.add(port);
                return port;
            }
            throw new Error(`No free host port in range ${start}-${end}`);
        },

        /** Returns a port to the pool. Unknown and null ports are ignored. */
        release(port) {
            if (port != null) {
                allocated.delete(port);
            }
        },

        /** True while the port is reserved by a live instance. */
        isAllocated(port) {
            return allocated.has(port);
        },
    };
}

/** True when nothing on the host is listening on the port. */
function defaultProbe(port) {
    return new Promise(resolve => {
        const probe = net.createServer();
        probe.once('error', () => resolve(false));
        probe.once('listening', () => probe.close(() => resolve(true)));
        probe.listen({ port, host: '0.0.0.0' });
    });
}

module.exports = { createPortAllocator };
