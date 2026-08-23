FROM node:24-bookworm AS deps
WORKDIR /repo
RUN corepack enable

COPY package.json pnpm-lock.yaml pnpm-workspace.yaml ./
COPY apps/web/package.json apps/web/package.json
RUN pnpm install --frozen-lockfile

FROM deps AS build
# Deliberately without a default. Next.js compiles NEXT_PUBLIC_* into the
# browser bundle, so anything defaulted here would be baked into the published
# image and pin it to one installation. Left unset, the bundle calls /api on
# whatever origin served the page, which is the same-origin topology a reverse
# proxy in front of both hosts produces. Pass it only for the other topology,
# where the API answers at a different origin.
ARG NEXT_PUBLIC_PAYAFFE_API_BASE_URL
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
