# Multi-stage Dockerfile for Frontend (Angular SPA)
# Stage 1: Build Angular application
FROM node:22.14-alpine AS build
WORKDIR /app

# Copy dependency manifests and install packages
COPY package*.json ./
RUN npm ci

# Copy source files and build production bundle
COPY . .
RUN npm run build -- --configuration production

# Stage 2: Serve application using unprivileged Nginx Alpine (non-root)
FROM nginxinc/nginx-unprivileged:1.27-alpine AS runtime
WORKDIR /usr/share/nginx/html

# Clean default nginx files
RUN rm -rf ./*

# Copy built artifacts from build stage
COPY --from=build /app/dist/*/browser ./

# Copy custom Nginx configuration supporting SPA client-side routing and security headers
COPY nginx-security-headers.conf /etc/nginx/snippets/security-headers.conf
COPY nginx.conf /etc/nginx/conf.d/default.conf

EXPOSE 8080
CMD ["nginx", "-g", "daemon off;"]
