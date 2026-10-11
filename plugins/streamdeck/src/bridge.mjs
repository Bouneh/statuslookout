import net from 'node:net';
import { createHash } from 'node:crypto';

export function pipePath(domain = process.env.USERDOMAIN ?? '', user = process.env.USERNAME ?? '') {
  const suffix = createHash('sha256').update(`${domain}\\${user}`.toLowerCase()).digest('hex').slice(0, 16);
  return `\\\\.\\pipe\\StatusLookout.StreamDeck.v1.${suffix}`;
}

export function request(command = 'snapshot', fields = {}, path = pipePath()) {
  return new Promise((resolve, reject) => {
    const socket = net.createConnection(path);
    socket.setEncoding('utf8');
    let buffer = ''; let size = 0; let finished = false;
    const end = (error, value) => { if (finished) return; finished = true; socket.destroy(); error ? reject(error) : resolve(value); };
    socket.setTimeout(3500, () => end(new Error('StatusLookout did not respond.')));
    socket.on('connect', () => socket.write(JSON.stringify({ command, ...fields }) + '\n'));
    socket.on('error', () => end(new Error('StatusLookout is disconnected.')));
    socket.on('end', () => { if (!finished) end(new Error('Incomplete StatusLookout response.')); });
    socket.on('data', chunk => {
      size += Buffer.byteLength(chunk); if (size > 8 * 1024 * 1024) return end(new Error('StatusLookout response is too large.'));
      buffer += chunk; const newline = buffer.indexOf('\n'); if (newline < 0) return;
      try {
        const reply = JSON.parse(buffer.slice(0, newline));
        if (!reply.ok) throw new Error(reply.error || 'StatusLookout rejected the command.');
        if (reply.snapshot?.protocol !== 1 || !Array.isArray(reply.snapshot.services)) throw new Error('Unsupported StatusLookout protocol.');
        end(null, reply.snapshot);
      } catch (error) { end(error); }
    });
  });
}

export class Bridge {
  /** @type {import('./types.js').Snapshot | null} */
  snapshot = null;
  listeners = new Set();
  #pending = null;
  #queue = Promise.resolve();
  async poll() {
    if (this.#pending) return this.#pending;
    this.#pending = this.command('snapshot').catch(() => null).finally(() => { this.#pending = null; });
    return this.#pending;
  }
  async command(name, fields = {}) {
    const operation = this.#queue.then(() => request(name, fields));
    this.#queue = operation.then(() => undefined, () => undefined);
    try { this.snapshot = await operation; }
    catch (error) { this.snapshot = null; this.#publish(); throw error; }
    this.#publish(); return this.snapshot;
  }
  #publish() { for (const listener of this.listeners) listener(this.snapshot); }
}
