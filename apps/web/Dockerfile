FROM node:24-bookworm AS deps
WORKDIR /repo
RUN corepack enable

COPY package.json pnpm-lock.yaml pnpm-workspace.yaml ./
COPY apps/web/package.json apps/web/package.json
RUN pnpm install --frozen-lockfile

FROM deps AS build
ARG NEXT_PUBLIC_PAYAFFE_API_BASE_URL=http://localhost:8080
ARG NEXT_PUBLIC_GLITCHTIP_DSN
ARG NEXT_PUBLIC_DEPLOYMENT_ENVIRONMENT=production
ARG NEXT_PUBLIC_RELEASE
ENV NEXT_PUBLIC_PAYAFFE_API_BASE_URL=$NEXT_PUBLIC_PAYAFFE_API_BASE_URL
ENV NEXT_PUBLIC_GLITCHTIP_DSN=$NEXT_PUBLIC_GLITCHTIP_DSN
ENV NEXT_PUBLIC_DEPLOYMENT_ENVIRONMENT=$NEXT_PUBLIC_DEPLOYMENT_ENVIRONMENT
ENV NEXT_PUBLIC_RELEASE=$NEXT_PUBLIC_RELEASE
COPY apps/web apps/web
RUN pnpm --filter @payaffe/web build

FROM node:24-bookworm-slim
WORKDIR /app
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
ENV NODE_ENV=production
ENV HOSTNAME=0.0.0.0
ENV PORT=3000
EXPOSE 3000
COPY --from=build /repo/apps/web/.next/standalone ./
COPY --from=build /repo/apps/web/.next/static ./apps/web/.next/static
USER node
CMD ["node", "apps/web/server.js"]
