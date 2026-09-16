# ── Stage 1: Build ────────────────────────────────────────────────────────────
FROM node:20-alpine AS build
WORKDIR /app

# Instalar dependencias (capa separada para caché)
COPY package*.json ./
RUN npm ci --prefer-offline

# Copiar código fuente y construir
COPY . .
RUN npm run build

# ── Stage 2: Serve con Nginx ──────────────────────────────────────────────────
FROM nginx:alpine AS runtime

# Copiar configuración de Nginx
COPY nginx.conf /etc/nginx/conf.d/default.conf

# Copiar artefactos del build
COPY --from=build /app/dist /usr/share/nginx/html

EXPOSE 80

CMD ["nginx", "-g", "daemon off;"]
