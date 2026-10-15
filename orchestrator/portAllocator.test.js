'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');

const { createPortAllocator } = require('./portAllocator');

test('allocate hands out the first free port in the range', async () => {
    const allocator = createPortAllocator({ start: 7801, end: 7803, probe: async () => true });

    assert.equal(await allocator.allocate(), 7801);
    assert.equal(await allocator.allocate(), 7802);
    assert.equal(await allocator.allocate(), 7803);
});

test('allocate skips ports the probe says are already taken', async () => {
    const taken = new Set([7801, 7803]);
    const allocator = createPortAllocator({
        start: 7801,
        end: 7804,
        probe: async port => !taken.has(port),
    });

    assert.equal(await allocator.allocate(), 7802);
    assert.equal(await allocator.allocate(), 7804);
});

test('allocate never hands the same port to two live instances', async () => {
    const allocator = createPortAllocator({ start: 7801, end: 7802, probe: async () => true });

    const first = await allocator.allocate();
    const second = await allocator.allocate();
    assert.notEqual(first, second);
    await assert.rejects(() => allocator.allocate(), /No free host port/);
});

test('release returns a port to the pool for reuse', async () => {
    const allocator = createPortAllocator({ start: 7801, end: 7801, probe: async () => true });

    const port = await allocator.allocate();
    await assert.rejects(() => allocator.allocate(), /No free host port/);

    allocator.release(port);
    assert.equal(await allocator.allocate(), 7801);
});

test('release ignores null and unknown ports', () => {
    const allocator = createPortAllocator({ start: 7801, end: 7802, probe: async () => true });

    assert.doesNotThrow(() => allocator.release(null));
    assert.doesNotThrow(() => allocator.release(9999));
});

test('allocate rejects when the whole range is occupied', async () => {
    const allocator = createPortAllocator({ start: 7801, end: 7802, probe: async () => false });

    await assert.rejects(() => allocator.allocate(), /No free host port/);
});

test('invalid ranges are rejected up front', () => {
    assert.throws(() => createPortAllocator({ start: 10, end: 5 }), RangeError);
    assert.throws(() => createPortAllocator({ start: 0, end: 5 }), RangeError);
    assert.throws(() => createPortAllocator({ start: 1, end: 70000 }), RangeError);
    assert.throws(() => createPortAllocator({ start: 7800 }), RangeError);
});

test('the default probe refuses a port something else is listening on', async t => {
    const net = require('net');

    // Use the OS-assigned port as "some other process is already on it".
    const squatter = net.createServer();
    await new Promise(resolve => squatter.listen(0, '127.0.0.1', resolve));
    const busyPort = squatter.address().port;
    t.after(() => squatter.close());

    const freeAllocator = createPortAllocator({ start: busyPort, end: busyPort + 1 });
    assert.equal(await freeAllocator.allocate(), busyPort + 1,
        'The squatted port must be skipped, the next one handed out.');
});
