import { Server } from '@colyseus/core';
import { WebSocketTransport } from '@colyseus/ws-transport';
import { MatchRoom } from './MatchRoom';

const PORT = Number(process.env.PORT || 7860);

const gameServer = new Server({
  transport: new WebSocketTransport({ pingInterval: 5000, pingMaxRetries: 3 }),
  greet: false,
  // Health check for the host / uptime pings and the client's "server online" indicator.
  express: (app) => {
    app.get(['/', '/health'], (_req, res) => { res.set('access-control-allow-origin', '*').type('text/plain').send('Last War game server OK'); });
  },
});
gameServer.define('match', MatchRoom);
await gameServer.listen(PORT);
console.log(`Last War server listening on :${PORT}`);
