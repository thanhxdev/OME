FROM node:20-alpine AS builder

WORKDIR /app

# Copy shared package
COPY shared /app/shared
WORKDIR /app/shared
RUN npm install && npm run build

# Build monitoring-service
WORKDIR /app/monitoring-service
COPY monitoring-service/package*.json ./
RUN npm install

COPY monitoring-service ./
RUN npm run build

FROM node:20-alpine AS runner
WORKDIR /app

COPY --from=builder /app/shared /app/shared
COPY --from=builder /app/monitoring-service/node_modules /app/monitoring-service/node_modules
COPY --from=builder /app/monitoring-service/dist /app/monitoring-service/dist
COPY --from=builder /app/monitoring-service/package.json /app/monitoring-service/

WORKDIR /app/monitoring-service
EXPOSE 9100

CMD ["node", "dist/index.js"]
