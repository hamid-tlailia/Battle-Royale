# Last War game server (Colyseus + shared Rapier simulation). Works on any Docker host.
FROM node:22-slim
WORKDIR /app
COPY package.json tsconfig.base.json ./
COPY shared ./shared
COPY server ./server
# only the workspaces the server needs
RUN node -e "const p=require('./package.json');p.workspaces=['shared','server'];require('fs').writeFileSync('package.json',JSON.stringify(p))" \
 && npm install --omit=dev --no-audit --no-fund
ENV PORT=7860 NODE_ENV=production
EXPOSE 7860
CMD ["npx", "tsx", "server/src/index.ts"]
