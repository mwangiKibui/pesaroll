# Builds the React app and serves it with Caddy, which also proxies /api to the API.
# Build from the repo root: docker build -f deploy/web.Dockerfile -t pesaroll-web .
FROM node:22-alpine AS build
RUN corepack enable && corepack prepare pnpm@8.10.2 --activate
WORKDIR /web
COPY web/package.json web/pnpm-lock.yaml ./
RUN pnpm install --frozen-lockfile
COPY web/ ./
RUN pnpm build

FROM caddy:2
COPY deploy/Caddyfile /etc/caddy/Caddyfile
COPY --from=build /web/dist /srv/web
